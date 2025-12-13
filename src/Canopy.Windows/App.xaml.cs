using Canopy.Core.Application;
using Canopy.Core.Auth;
using Canopy.Core.GameDetection;
using Canopy.Core.Input;
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
    private IHost? _host;
    private ITrayIconService? _trayIconService;
    private MainWindow? _mainWindow;
    private SettingsWindow? _settingsWindow;
    private AppCoordinator? _appCoordinator;

    public static IServiceProvider Services { get; private set; } = null!;
    public static MainWindow? MainWindowInstance { get; private set; }
    public static DispatcherQueue DispatcherQueue { get; private set; } = null!;

    public App()
    {
        InitializeComponent();

        // Register protocol handler
        var exePath = Path.Combine(AppContext.BaseDirectory, "Canopy.Windows.exe");
        WindowsPlatformServices.RegisterProtocol("canopy", exePath);
        
        // Subscribe to protocol activations from Program
        Program.ProtocolActivated += OnProtocolActivated;
    }

    private void OnProtocolActivated(object? sender, Uri uri)
    {
        Debug.WriteLine($"Protocol activated: {uri}");
        
        if (DispatcherQueue != null)
        {
            DispatcherQueue.TryEnqueue(() => HandleProtocolActivation(uri));            
        }
    }

    private void HandleProtocolActivation(Uri uri)
    {
        Debug.WriteLine($"Handling protocol: {uri}");
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
#if DEBUG
        Debug.WriteLine("Canopy starting (console attached)");
#endif

        VelopackApp.Build().Run();
        DispatcherQueue = DispatcherQueue.GetForCurrentThread();

        // Ensure single instance
        if (!SingleInstanceGuard.TryAcquire())
        {
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
            Debug.WriteLine("Starting game scan...");
            await gameService.ScanAllGamesAsync();
            Debug.WriteLine("Game scan complete.");
        });
    }

    private void OnGameStarted(object? sender, GameStatePayload payload)
    {
        Debug.WriteLine($"Game started: {payload.Name}");
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
        Debug.WriteLine($"Game stopped: {payload.Name}");
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
            platformServices.SetStartupRegistration(settings.StartWithWindows);
            Debug.WriteLine($"Startup registration synced: {settings.StartWithWindows}");
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
        _appCoordinator?.HandleHotkeyPressed(args.Name);
    }

    public async Task ShutdownAsync()
    {
        _appCoordinator?.Dispose();
        _trayIconService?.Dispose();
        Services.GetService<UpdateService>()?.Dispose();

        if (_host != null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        SingleInstanceGuard.Release();
    }
}
