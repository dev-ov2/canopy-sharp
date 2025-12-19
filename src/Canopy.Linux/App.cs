using Canopy.Core.Application;
using Canopy.Core.Auth;
using Canopy.Core.GameDetection;
using Canopy.Core.Input;
using Canopy.Core.Logging;
using Canopy.Core.Models;
using Canopy.Core.Notifications;
using Canopy.Core.Platform;
using Canopy.Linux.Services;
using Canopy.Linux.Windows;
using Gtk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Canopy.Linux;

/// <summary>
/// Main Linux application class
/// </summary>
public class App : Gtk.Application
{
    private readonly ICanopyLogger _logger;
    private IHost? _host;
    private ITrayIconService? _trayIconService;
    private MainWindow? _mainWindow;
    private SettingsWindow? _settingsWindow;
    private OverlayWindow? _overlayWindow;
    private AppCoordinator? _appCoordinator;
    private readonly bool _startMinimized;
    private static string[]? _commandLineArgs;

    public static IServiceProvider Services { get; private set; } = null!;

    public App(bool startMinimized = false, string[]? args = null) : base("gg.ovv.canopy", GLib.ApplicationFlags.None)
    {
        _startMinimized = startMinimized;
        _commandLineArgs = args;
        
        // Initialize logging
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var logDir = Path.Combine(home, ".local", "share", "canopy");
        CanopyLoggerFactory.SetLogDirectory(logDir);
        
        _logger = CanopyLoggerFactory.CreateLogger<App>();
        _logger.Info("Canopy Linux starting...");
    }

    protected override void OnActivated()
    {
        base.OnActivated();
        
        if (_mainWindow != null)
        {
            _mainWindow.ShowAndActivate();
            return;
        }

        // Check for protocol args on first activation
        if (_commandLineArgs != null)
        {
            foreach (var arg in _commandLineArgs)
            {
                if (arg.StartsWith("canopy://"))
                {
                    _logger.Info($"Protocol activation: {arg}");
                    if (Uri.TryCreate(arg, UriKind.Absolute, out var uri))
                    {
                        // Queue for after initialization
                        GLib.Idle.Add(() =>
                        {
                            HandleProtocolActivation(uri);
                            return false;
                        });
                    }
                }
            }
        }

        Initialize();
    }

    private async void Initialize()
    {
        // Ensure single instance
        if (!SingleInstanceGuard.TryAcquire())
        {
            _logger.Warning("Another instance is already running");
            Quit();
            return;
        }

        // Build host with DI
        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(ConfigureServices)
            .Build();

        Services = _host.Services;
        await _host.StartAsync();

        // Register protocol handler
        Services.GetRequiredService<LinuxPlatformServices>().RegisterProtocol("canopy");

        // Initialize coordinator and wire up events
        _appCoordinator = Services.GetRequiredService<AppCoordinator>();
        SetupCoordinatorEvents();

        // Sync startup registration with settings
        await InitializeStartupRegistration();

        // Create windows
        _mainWindow = Services.GetRequiredService<MainWindow>();
        _overlayWindow = Services.GetRequiredService<OverlayWindow>();
        
        AddWindow(_mainWindow);
        AddWindow(_overlayWindow);

        if (!_startMinimized)
        {
            _mainWindow.ShowAndActivate();
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

        _logger.Info("App startup complete");
    }

    private void ConfigureServices(IServiceCollection services)
    {
        // Core application services
        services.AddSingleton<ISettingsService, LinuxSettingsService>();
        services.AddSingleton<TokenExchangeService>();
        services.AddSingleton<AppCoordinator>();

        // Platform services
        services.AddSingleton<LinuxPlatformServices>();
        services.AddSingleton<IPlatformServices>(sp => sp.GetRequiredService<LinuxPlatformServices>());
        services.AddSingleton<ITrayIconService, LinuxTrayIconService>();
        services.AddSingleton<INotificationService, LinuxNotificationService>();

        // Hotkeys
        services.AddSingleton<LinuxHotkeyService>();
        services.AddSingleton<IHotkeyService>(sp => sp.GetRequiredService<LinuxHotkeyService>());

        // Game detection
        services.AddSingleton<IGameScanner, SteamScanner>();
        services.AddSingleton<IGameDetector, LinuxGameDetector>();
        services.AddSingleton<GameService>();

        // Windows/UI
        services.AddSingleton<MainWindow>();
        services.AddSingleton<OverlayWindow>();
        services.AddSingleton<LinuxWebViewIpcBridge>();
    }

    private void SetupCoordinatorEvents()
    {
        if (_appCoordinator == null) return;

        _appCoordinator.ShowWindowRequested += (_, _) =>
            Gtk.Application.Invoke((_, _) => _mainWindow?.ShowAndActivate());

        _appCoordinator.ShowSettingsRequested += (_, _) =>
            Gtk.Application.Invoke((_, _) => ShowSettingsWindow());

        _appCoordinator.OverlayToggleRequested += (_, _) =>
            Gtk.Application.Invoke((_, _) => _overlayWindow?.Toggle());

        _appCoordinator.OverlayDragToggleRequested += (_, _) =>
            Gtk.Application.Invoke((_, _) =>
            {
                if (_overlayWindow?.IsVisible == true)
                    _overlayWindow.ToggleDragMode();
            });

        _appCoordinator.TokenReady += (_, e) =>
            Gtk.Application.Invoke(async (_, _) =>
            {
                await Services.GetRequiredService<LinuxWebViewIpcBridge>().Send(new Core.IPC.IpcMessage
                {
                    Type = Core.IPC.IpcMessageTypes.TokenReceived,
                    Payload = new { token = e.Token }
                });
            });

        _appCoordinator.QuitRequested += async (_, _) =>
        {
            _logger.Info("Quit requested");
            await ShutdownAsync();
            Quit();
        };
    }

    private void ShowSettingsWindow()
    {
        if (_settingsWindow == null)
        {
            _settingsWindow = new SettingsWindow();
            AddWindow(_settingsWindow);
            _settingsWindow.DeleteEvent += (_, _) => _settingsWindow = null;
        }
        _settingsWindow.ShowAll();
        _settingsWindow.Present();
    }

    private async Task InitializeStartupRegistration()
    {
        var settings = Services.GetRequiredService<ISettingsService>().Settings;
        var platformServices = Services.GetRequiredService<LinuxPlatformServices>();
        
        var isRegistered = await platformServices.IsAutoStartEnabledAsync();
        if (isRegistered != settings.StartWithWindows)
        {
            await platformServices.SetAutoStartAsync(settings.StartWithWindows, settings.StartOpen);
            _logger.Info($"Startup registration synced: {settings.StartWithWindows}");
        }
    }

    private void OnSettingsChanged(object? sender, AppSettings settings)
    {
        Gtk.Application.Invoke((_, _) =>
        {
            UnregisterHotkeys();
            RegisterHotkeys();
        });
    }

    private void RegisterHotkeys()
    {
        var hotkeyService = Services.GetRequiredService<LinuxHotkeyService>();
        var settings = Services.GetRequiredService<ISettingsService>().Settings;

        if (settings.EnableOverlay)
        {
            hotkeyService.RegisterHotkey(settings.OverlayToggleShortcut, HotkeyNames.ToggleOverlay);
            hotkeyService.RegisterHotkey(settings.OverlayDragShortcut, HotkeyNames.ToggleOverlayDrag);
        }

        hotkeyService.HotkeyPressed += OnHotkeyPressed;
        _logger.Info("Hotkeys registered");
    }

    private void UnregisterHotkeys()
    {
        var hotkeyService = Services.GetRequiredService<LinuxHotkeyService>();
        hotkeyService.HotkeyPressed -= OnHotkeyPressed;
        hotkeyService.UnregisterAll();
    }

    private void OnHotkeyPressed(object? sender, HotkeyEventArgs args)
    {
        _logger.Debug($"Hotkey pressed: {args.Name}");
        _appCoordinator?.HandleHotkeyPressed(args.Name);
    }

    private void OnGameStarted(object? sender, GameStatePayload payload)
    {
        _logger.Info($"Game started: {payload.Name}");
        
        Gtk.Application.Invoke((_, _) =>
        {
            var settings = Services.GetRequiredService<ISettingsService>().Settings;
            if (settings.EnableOverlay)
            {
                _overlayWindow?.UpdateGameInfo(payload.Name);
                _overlayWindow?.ShowOverlay();
            }
        });
    }

    private void OnGameStopped(object? sender, GameStatePayload payload)
    {
        _logger.Info($"Game stopped: {payload.Name}");
        
        Gtk.Application.Invoke((_, _) =>
        {
            _overlayWindow?.UpdateGameInfo(null);
        });
    }

    private void HandleProtocolActivation(Uri uri)
    {
        _logger.Info($"Handling protocol: {uri}");
        _mainWindow?.ShowAndActivate();

        if (_appCoordinator != null)
        {
            _ = _appCoordinator.HandleProtocolActivationAsync(uri);
        }
        else
        {
            var uriString = uri.ToString();
            if (uriString.Contains("#id_token="))
            {
                _mainWindow?.OnTokenReceived(uriString.Split("#id_token=")[1]);
            }
        }
    }

    public async Task ShutdownAsync()
    {
        _logger.Info("Shutting down...");
        
        _appCoordinator?.Dispose();
        _trayIconService?.Dispose();
        
        Services.GetService<LinuxHotkeyService>()?.Dispose();

        if (_host != null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        SingleInstanceGuard.Release();
        
        _logger.Info("Shutdown complete");
    }
}
