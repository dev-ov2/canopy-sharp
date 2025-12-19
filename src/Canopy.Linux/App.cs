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
using Gdk;
using Gtk;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Runtime.InteropServices;

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
    private readonly string[]? _commandLineArgs;
    private bool _isInitialized;
    private FileSystemWatcher? _protocolWatcher;

    public static IServiceProvider Services { get; private set; } = null!;

    public App(bool startMinimized = false, string[]? args = null) 
        : base("gg.ovv.canopy", GLib.ApplicationFlags.None)
    {
        _startMinimized = startMinimized;
        _commandLineArgs = args;
        
        // Initialize logging
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var logDir = Path.Combine(home, ".local", "share", "canopy");
        CanopyLoggerFactory.SetLogDirectory(logDir);
        
        _logger = CanopyLoggerFactory.CreateLogger<App>();
        _logger.Info("Canopy Linux starting...");
        
        // Set the program name for GTK/GLib - this affects WM_CLASS
        GLib.Global.ProgramName = "canopy";
        SetPrgname("canopy");
        
        // Initialize icon system
        AppIconManager.Initialize();
    }

    private static void SetPrgname(string name)
    {
        try { g_set_prgname(name); }
        catch { }
    }

    [DllImport("libglib-2.0.so.0")]
    private static extern void g_set_prgname(string prgname);

    protected override void OnActivated()
    {
        base.OnActivated();
        _logger.Debug("OnActivated called");
        
        if (_mainWindow != null)
        {
            _mainWindow.ShowAndActivate();
            return;
        }

        if (!_isInitialized)
        {
            Initialize();
        }
    }

    private async void Initialize()
    {
        _isInitialized = true;
        
        // Ensure single instance
        if (!SingleInstanceGuard.TryAcquire())
        {
            _logger.Warning("Another instance is already running");
            
            // Send protocol URI to running instance via file
            if (_commandLineArgs != null)
            {
                foreach (var arg in _commandLineArgs)
                {
                    if (arg.StartsWith("canopy://"))
                    {
                        SendProtocolToRunningInstance(arg);
                        break;
                    }
                }
            }
            
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

        // Start watching for protocol files from other instances
        StartProtocolWatcher();

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

        // Handle protocol URI from command line args
        if (_commandLineArgs != null)
        {
            foreach (var arg in _commandLineArgs)
            {
                if (arg.StartsWith("canopy://") && Uri.TryCreate(arg, UriKind.Absolute, out var uri))
                {
                    _logger.Info($"Protocol URI from args: {arg}");
                    GLib.Idle.Add(() =>
                    {
                        HandleProtocolActivation(uri);
                        return false;
                    });
                    break;
                }
            }
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

    /// <summary>
    /// Sends a protocol URI to the running instance via a file.
    /// The running instance watches this directory for new files.
    /// </summary>
    private void SendProtocolToRunningInstance(string uri)
    {
        try
        {
            var protocolDir = GetProtocolDirectory();
            Directory.CreateDirectory(protocolDir);
            
            var filename = Path.Combine(protocolDir, $"protocol_{DateTime.UtcNow.Ticks}.uri");
            File.WriteAllText(filename, uri);
            
            _logger.Info($"Sent protocol URI to running instance: {uri}");
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to send protocol to running instance: {ex.Message}");
        }
    }

    /// <summary>
    /// Starts watching for protocol files from other instances.
    /// </summary>
    private void StartProtocolWatcher()
    {
        try
        {
            var protocolDir = GetProtocolDirectory();
            Directory.CreateDirectory(protocolDir);
            
            // Clean up any old protocol files
            foreach (var file in Directory.GetFiles(protocolDir, "*.uri"))
            {
                try { File.Delete(file); } catch { }
            }
            
            _protocolWatcher = new FileSystemWatcher(protocolDir, "*.uri")
            {
                NotifyFilter = NotifyFilters.CreationTime | NotifyFilters.FileName
            };
            
            _protocolWatcher.Created += OnProtocolFileCreated;
            _protocolWatcher.EnableRaisingEvents = true;
            
            _logger.Debug($"Protocol watcher started: {protocolDir}");
        }
        catch (Exception ex)
        {
            _logger.Warning($"Failed to start protocol watcher: {ex.Message}");
        }
    }

    private void OnProtocolFileCreated(object sender, FileSystemEventArgs e)
    {
        try
        {
            // Small delay to ensure file is written
            Thread.Sleep(50);
            
            if (File.Exists(e.FullPath))
            {
                var uri = File.ReadAllText(e.FullPath).Trim();
                File.Delete(e.FullPath);
                
                if (Uri.TryCreate(uri, UriKind.Absolute, out var parsedUri))
                {
                    _logger.Info($"Received protocol from another instance: {uri}");
                    
                    GLib.Idle.Add(() =>
                    {
                        HandleProtocolActivation(parsedUri);
                        return false;
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Warning($"Error processing protocol file: {ex.Message}");
        }
    }

    private static string GetProtocolDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".local", "share", "canopy", "protocol");
    }

    private void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<ISettingsService, LinuxSettingsService>();
        services.AddSingleton<TokenExchangeService>();
        services.AddSingleton<AppCoordinator>();

        services.AddSingleton<LinuxPlatformServices>();
        services.AddSingleton<IPlatformServices>(sp => sp.GetRequiredService<LinuxPlatformServices>());
        services.AddSingleton<ITrayIconService, LinuxTrayIconService>();
        services.AddSingleton<INotificationService, LinuxNotificationService>();

        services.AddSingleton<LinuxHotkeyService>();
        services.AddSingleton<IHotkeyService>(sp => sp.GetRequiredService<LinuxHotkeyService>());

        services.AddSingleton<IGameScanner, SteamScanner>();
        services.AddSingleton<IGameDetector, LinuxGameDetector>();
        services.AddSingleton<GameService>();

        services.AddSingleton<MainWindow>();
        services.AddSingleton<OverlayWindow>();
        services.AddSingleton<LinuxWebViewIpcBridge>();
    }

    private void SetupCoordinatorEvents()
    {
        if (_appCoordinator == null) return;

        _appCoordinator.ShowWindowRequested += (_, _) =>
            GLib.Idle.Add(() => { _mainWindow?.ShowAndActivate(); return false; });

        _appCoordinator.ShowSettingsRequested += (_, _) =>
            GLib.Idle.Add(() => { ShowSettingsWindow(); return false; });

        _appCoordinator.OverlayToggleRequested += (_, _) =>
            GLib.Idle.Add(() => { _overlayWindow?.Toggle(); return false; });

        _appCoordinator.OverlayDragToggleRequested += (_, _) =>
            GLib.Idle.Add(() =>
            {
                if (_overlayWindow?.IsVisible == true)
                    _overlayWindow.ToggleDragMode();
                return false;
            });

        _appCoordinator.TokenReady += (_, e) =>
            GLib.Idle.Add(() =>
            {
                _ = Services.GetRequiredService<LinuxWebViewIpcBridge>().Send(new Core.IPC.IpcMessage
                {
                    Type = Core.IPC.IpcMessageTypes.TokenReceived,
                    Payload = new { token = e.Token }
                });
                return false;
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
        GLib.Idle.Add(() =>
        {
            UnregisterHotkeys();
            RegisterHotkeys();
            return false;
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
        
        GLib.Idle.Add(() =>
        {
            var settings = Services.GetRequiredService<ISettingsService>().Settings;
            if (settings.EnableOverlay)
            {
                _overlayWindow?.UpdateGameInfo(payload.Name);
                _overlayWindow?.ShowOverlay();
            }
            return false;
        });
    }

    private void OnGameStopped(object? sender, GameStatePayload payload)
    {
        _logger.Info($"Game stopped: {payload.Name}");
        
        GLib.Idle.Add(() =>
        {
            _overlayWindow?.UpdateGameInfo(null);
            return false;
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
        
        _protocolWatcher?.Dispose();
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
