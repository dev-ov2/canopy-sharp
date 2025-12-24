using Canopy.Core.Application;
using Canopy.Core.Auth;
using Canopy.Core.GameDetection;
using Canopy.Core.Input;
using Canopy.Core.Logging;
using Canopy.Core.Models;
using Canopy.Core.Notifications;
using Canopy.Core.Platform;
using Canopy.Windows.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Windows.AppLifecycle;
using System.Diagnostics;
using Velopack;
using Windows.ApplicationModel.Activation;
using DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue;
using AppSettings = Canopy.Core.Application.AppSettings;
using SingleInstanceGuard = Canopy.Core.Application.SingleInstanceGuard;

namespace Canopy.Windows;

/// <summary>
/// WinUI 3 Application entry point
/// </summary>
public partial class App : Application
{
    private static readonly ICanopyLogger _logger;
    private IHost? _host;
    private ITrayIconService? _trayIconService;
    private MainWindow? _mainWindow;
    private SettingsWindow? _settingsWindow;
    private AppCoordinator? _appCoordinator;

    public static IServiceProvider Services { get; private set; } = null!;
    public static MainWindow? MainWindowInstance { get; private set; }
    public static DispatcherQueue DispatcherQueue { get; private set; } = null!;

    static App()
    {
        // Initialize logging
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Canopy");
        CanopyLoggerFactory.SetLogDirectory(appDataPath);
        _logger = CanopyLoggerFactory.CreateLogger<App>();
    }

    public App()
    {
        InitializeComponent();

        // Register protocol handler
        var exePath = Path.Combine(AppContext.BaseDirectory, "Canopy.Windows.exe");
        WindowsPlatformServices.RegisterProtocol("canopy", exePath);
        
        // Subscribe to protocol activations from Program
        Program.ProtocolActivated += OnProtocolActivated;
        
        _logger.Info("App initialized");
    }

    private void OnProtocolActivated(object? sender, Uri uri)
    {
        _logger.Debug($"Protocol activated: {uri}");
        
        if (DispatcherQueue != null)
        {
            DispatcherQueue.TryEnqueue(() => HandleProtocolActivation(uri));            
        }
    }

    private void HandleProtocolActivation(Uri uri)
    {
        _logger.Debug($"Handling protocol: {uri}");
        MainWindowInstance?.Activate();
        
        if (_appCoordinator != null)
        {
            _ = _appCoordinator.HandleProtocolActivationAsync(uri);
        }
        else
        {
            // Fallback for early activation before coordinator is ready
            var uriString = uri.ToString();
            if (uriString.Contains("#id_token="))
            {
                _mainWindow?.OnTokenReceived(uriString.Split("#id_token=")[1]);
            }
        }
    }

    protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        _logger.Info("Canopy Windows starting...");

        VelopackApp.Build().Run();
        DispatcherQueue = DispatcherQueue.GetForCurrentThread();

        // Ensure single instance
        if (!SingleInstanceGuard.TryAcquire())
        {
            _logger.Warning("Another instance is already running");
            var activationArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
            var mainInstance = AppInstance.FindOrRegisterForKey("CanopyMainInstance");
            
            if (!mainInstance.IsCurrent)
            {
                await mainInstance.RedirectActivationToAsync(activationArgs);
                System.Diagnostics.Process.GetCurrentProcess().Kill();
                return;
            }
        }

        // Build host with DI
        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(ConfigureServices)
            .Build();

        Services = _host.Services;
        await _host.StartAsync();

        // Initialize coordinator and wire up events
        _appCoordinator = Services.GetRequiredService<AppCoordinator>();
        SetupCoordinatorEvents();

        // Sync startup registration with settings
        InitializeStartupRegistration();

        // Create main window
        var startMinimized = Environment.GetCommandLineArgs().Contains("--minimized");
        _mainWindow = Services.GetRequiredService<MainWindow>();
        MainWindowInstance = _mainWindow;
        
        if (!startMinimized)
        {
            _mainWindow.Activate();
        }

        // Initialize tray icon
        _trayIconService = Services.GetRequiredService<ITrayIconService>();
        _trayIconService.Initialize();
        _trayIconService.ShowWindowRequested += (_, _) => _appCoordinator.RequestShowWindow();
        _trayIconService.SettingsRequested += (_, _) => _appCoordinator.RequestShowSettings();
        _trayIconService.RescanGamesRequested += async (_, _) => await _appCoordinator.RequestRescanGamesAsync();
        _trayIconService.QuitRequested += (_, _) => _appCoordinator.RequestQuit();

        // Register hotkeys
        RegisterHotkeys();
        Services.GetRequiredService<ISettingsService>().SettingsChanged += OnSettingsChanged;

        // Start game detection
        var gameService = Services.GetRequiredService<GameService>();
        gameService.GameStarted += OnGameStarted;
        gameService.GameStopped += OnGameStopped;

        _ = Task.Run(async () =>
        {
            _logger.Info("Starting game scan...");
            await gameService.ScanAllGamesAsync();
            _logger.Info("Game scan complete.");
        });

        // Subscribe to auto-update notifications
        var updateService = Services.GetRequiredService<UpdateService>();
        updateService.UpdateAvailable += OnUpdateAvailable;
        updateService.UpdateError += OnUpdateError;
        
        _logger.Info($"App startup complete. AutoUpdate={Services.GetRequiredService<ISettingsService>().Settings.AutoUpdate}");
    }

    private void OnUpdateError(object? sender, string errorMessage)
    {
        _logger.Error($"Update error received: {errorMessage}");
    }

    private void OnUpdateAvailable(object? sender, Services.UpdateInfo updateInfo)
    {
        _logger.Info($"OnUpdateAvailable fired: Version={updateInfo.Version}, IsVelopack={updateInfo.IsVelopackUpdate}");
        
        DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                var notificationService = Services.GetRequiredService<INotificationService>();
                var settings = Services.GetRequiredService<ISettingsService>().Settings;

                _logger.Debug($"AutoUpdate setting: {settings.AutoUpdate}");

                if (settings.AutoUpdate)
                {
                    // Auto-update enabled: download silently and notify when ready
                    _logger.Info("Auto-update enabled, starting download...");
                    await notificationService.ShowAsync(
                        "Update Available",
                        $"Canopy v{updateInfo.Version} is downloading...");

                    try
                    {
                        var updateService = Services.GetRequiredService<UpdateService>();
                        await updateService.DownloadAndInstallAsync(updateInfo);
                        // App will restart automatically after download completes
                        _logger.Info("DownloadAndInstallAsync completed");
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"Auto-update download failed", ex);
                        await notificationService.ShowAsync(
                            "Update Failed",
                            "Please update manually from Settings.");
                    }
                }
                else
                {
                    // Auto-update disabled: just notify the user
                    _logger.Info("Auto-update disabled, showing notification only");
                    await notificationService.ShowAsync(
                        "Update Available",
                        $"Canopy v{updateInfo.Version} is available. Open Settings to update.");
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"OnUpdateAvailable exception", ex);
            }
        });
    }

    private void OnGameStarted(object? sender, GameStatePayload payload)
    {
        _logger.Info($"Game started: {payload.Name}");
        DispatcherQueue.TryEnqueue(() =>
        {
            var settings = Services.GetRequiredService<ISettingsService>().Settings;
            if (settings.EnableOverlay)
            {
                var overlay = Services.GetRequiredService<OverlayWindow>();
                overlay.UpdateGameInfo(payload.Name);
                overlay.Show();
            }
        });
    }

    private void OnGameStopped(object? sender, GameStatePayload payload)
    {
        _logger.Info($"Game stopped: {payload.Name}");
        DispatcherQueue.TryEnqueue(() =>
        {
            Services.GetRequiredService<OverlayWindow>().UpdateGameInfo(null);
        });
    }

    private void SetupCoordinatorEvents()
    {
        if (_appCoordinator == null) return;

        _appCoordinator.ShowWindowRequested += (_, _) => 
            DispatcherQueue.TryEnqueue(() => _mainWindow?.Activate());

        _appCoordinator.ShowSettingsRequested += (_, _) => 
            DispatcherQueue.TryEnqueue(ShowSettingsWindow);

        _appCoordinator.OverlayToggleRequested += (_, _) => 
            DispatcherQueue.TryEnqueue(() => Services.GetRequiredService<OverlayWindow>().Toggle());

        _appCoordinator.OverlayDragToggleRequested += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            var overlay = Services.GetRequiredService<OverlayWindow>();
            if (overlay.IsVisible) overlay.ToggleDragMode();
        });

        _appCoordinator.TokenReady += (_, e) => DispatcherQueue.TryEnqueue(async () =>
        {
            await Services.GetRequiredService<WebViewIpcBridge>().Send(new Core.IPC.IpcMessage
            {
                Type = Core.IPC.IpcMessageTypes.TokenReceived,
                Payload = new { token = e.Token }
            });
        });

        _appCoordinator.QuitRequested += async (_, _) =>
        {
            _logger.Info("Quit requested");
            await ShutdownAsync();
            Exit();
        };
    }

    private void ConfigureServices(IServiceCollection services)
    {
        // Core application services
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<TokenExchangeService>();
        services.AddSingleton<AppCoordinator>();

        // Platform services
        services.AddSingleton<WindowsPlatformServices>();
        services.AddSingleton<IPlatformServices>(sp => sp.GetRequiredService<WindowsPlatformServices>());
        services.AddSingleton<ITrayIconService, TrayIconService>();
        services.AddSingleton<INotificationService, WindowsNotificationService>();

        // Hotkeys
        services.AddSingleton<HotkeyService>();
        services.AddSingleton<IHotkeyService>(sp => sp.GetRequiredService<HotkeyService>());

        // Game detection
        services.AddSingleton<IGameScanner, SteamScanner>();
        services.AddSingleton<IGameScanner, RemoteMappingsScanner>();
        services.AddSingleton<IGameDetector, GameDetector>();
        services.AddSingleton<GameService>();

        // Updates
        services.AddSingleton<UpdateService>();

        // Windows/UI
        services.AddSingleton<MainWindow>();
        services.AddSingleton<OverlayWindow>();
        services.AddSingleton<WebViewIpcBridge>();
    }

    private void ShowSettingsWindow()
    {
        if (_settingsWindow == null)
        {
            _settingsWindow = new SettingsWindow();
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }
        _settingsWindow.Activate();
    }

    private void InitializeStartupRegistration()
    {
        var settings = Services.GetRequiredService<ISettingsService>().Settings;
        var platformServices = Services.GetRequiredService<WindowsPlatformServices>();
        
        var isRegistered = platformServices.IsRegisteredForStartup();
        if (isRegistered != settings.StartWithWindows)
        {
            platformServices.SetStartupRegistration(settings.StartWithWindows, settings.StartOpen);
            _logger.Info($"Startup registration synced: {settings.StartWithWindows}");
        }
    }

    private void OnSettingsChanged(object? sender, AppSettings settings)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            UnregisterHotkeys();
            RegisterHotkeys();
        });
    }

    private void RegisterHotkeys()
    {
        var hotkeyService = Services.GetRequiredService<HotkeyService>();
        var settings = Services.GetRequiredService<ISettingsService>().Settings;
        
        if (_mainWindow != null)
        {
            hotkeyService.Initialize(_mainWindow);

            if (settings.EnableOverlay)
            {
                hotkeyService.RegisterHotkey(settings.OverlayToggleShortcut, HotkeyNames.ToggleOverlay);
                hotkeyService.RegisterHotkey(settings.OverlayDragShortcut, HotkeyNames.ToggleOverlayDrag);
            }

            hotkeyService.HotkeyPressed += OnHotkeyPressed;
            _logger.Info("Hotkeys registered");
        }
    }

    private void UnregisterHotkeys()
    {
        var hotkeyService = Services.GetRequiredService<HotkeyService>();
        hotkeyService.HotkeyPressed -= OnHotkeyPressed;
        hotkeyService.UnregisterAll();
    }

    private void OnHotkeyPressed(object? sender, HotkeyEventArgs args)
    {
        _logger.Debug($"Hotkey pressed: {args.Name}");
        _appCoordinator?.HandleHotkeyPressed(args.Name);
    }

    public async Task ShutdownAsync()
    {
        _logger.Info("Shutting down...");
        
        _appCoordinator?.Dispose();
        _trayIconService?.Dispose();
        Services.GetService<UpdateService>()?.Dispose();

        if (_host != null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        SingleInstanceGuard.Release();
        
        _logger.Info("Shutdown complete");
    }
}
