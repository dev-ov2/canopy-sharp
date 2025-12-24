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
    private AppCoordinator? _appCoordinator;
    private readonly bool _startMinimized;
    private readonly string[]? _commandLineArgs;
    private bool _isInitialized;
    private FileSystemWatcher? _protocolWatcher;
    private System.Timers.Timer? _protocolPollTimer;

    public static IServiceProvider Services { get; private set; } = null!;

    public App(bool startMinimized = false, string[]? args = null) 
        : base("gg.ovv.canopy", GLib.ApplicationFlags.None)
    {
        _startMinimized = startMinimized;
        _commandLineArgs = args;
        
        // Logger should already be initialized by Program.cs
        _logger = CanopyLoggerFactory.CreateLogger<App>();
        _logger.Debug("App constructor called");
        
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
            _logger.Debug("MainWindow exists, showing and activating");
            _mainWindow.ShowAndActivate();
            return;
        }

        if (!_isInitialized)
        {
            _logger.Debug("Not initialized, calling Initialize()");
            Initialize();
        }
    }

    private async void Initialize()
    {
        try
        {
            _isInitialized = true;
            
            // Try to acquire the lock (should succeed since we checked earlier)
            if (!SingleInstanceGuard.TryAcquire())
            {
                _logger.Error("Failed to acquire single instance lock (unexpected)");
                Quit();
                return;
            }

            _logger.Debug("Single instance lock acquired");

            // Build host with DI
            _host = Host.CreateDefaultBuilder()
                .ConfigureServices(ConfigureServices)
                .Build();

            Services = _host.Services;
            await _host.StartAsync();
            _logger.Debug("Host started");

            // Register protocol handler
            try
            {
                Services.GetRequiredService<LinuxPlatformServices>().RegisterProtocol("canopy");
            }
            catch (Exception ex)
            {
                _logger.Warning($"Failed to register protocol handler: {ex.Message}");
            }

            // Initialize coordinator and wire up events
            _appCoordinator = Services.GetRequiredService<AppCoordinator>();
            SetupCoordinatorEvents();

            // Start watching for protocol files from other instances
            StartProtocolWatcher();

            // Sync startup registration with settings
            try
            {
                await InitializeStartupRegistration();
            }
            catch (Exception ex)
            {
                _logger.Warning($"Failed to sync startup registration: {ex.Message}");
            }

            // Create windows
            _logger.Debug("Creating MainWindow...");
            _mainWindow = Services.GetRequiredService<MainWindow>();
            
            AddWindow(_mainWindow);
            _logger.Debug("Window created and added");

            if (!_startMinimized)
            {
                _mainWindow.ShowAndActivate();
            }

            // Handle protocol URI from command line args (first instance with protocol)
            if (_commandLineArgs != null)
            {
                foreach (var arg in _commandLineArgs)
                {
                    if (arg.StartsWith("canopy://") && Uri.TryCreate(arg, UriKind.Absolute, out var uri))
                    {
                        _logger.Info($"Processing protocol URI from args: {arg}");
                        GLib.Timeout.Add(500, () =>
                        {
                            HandleProtocolActivation(uri);
                            return false;
                        });
                        break;
                    }
                }
            }

            // Initialize tray icon
            try
            {
                _trayIconService = Services.GetRequiredService<ITrayIconService>();
                _trayIconService.Initialize();
                _trayIconService.ShowWindowRequested += (_, _) => _appCoordinator.RequestShowWindow();
                _trayIconService.SettingsRequested += (_, _) => _appCoordinator.RequestShowSettings();
                _trayIconService.RescanGamesRequested += async (_, _) => await _appCoordinator.RequestRescanGamesAsync();
                _trayIconService.QuitRequested += (_, _) => _appCoordinator.RequestQuit();
            }
            catch (Exception ex)
            {
                _logger.Warning($"Failed to initialize tray icon: {ex.Message}");
            }

            // Register hotkeys
            try
            {
                RegisterHotkeys();
            }
            catch (Exception ex)
            {
                _logger.Warning($"Failed to register hotkeys: {ex.Message}");
            }
            
            Services.GetRequiredService<ISettingsService>().SettingsChanged += OnSettingsChanged;

            // Start game detection
            var gameService = Services.GetRequiredService<GameService>();
            gameService.GameStarted += OnGameStarted;
            gameService.GameStopped += OnGameStopped;

            _= Task.Run(async () =>
            {
                try
                {
                    _logger.Info("Starting game scan...");
                    await gameService.ScanAllGamesAsync();
                    _logger.Info("Game scan complete.");
                }
                catch (Exception ex)
                {
                    _logger.Error("Game scan failed", ex);
                }
            });

            // Subscribe to update notifications
            try
            {
                var updateService = Services.GetRequiredService<LinuxUpdateService>();
                updateService.UpdateAvailable += OnUpdateAvailable;
                updateService.UpdateError += (_, error) => _logger.Warning($"Update check error: {error}");
            }
            catch (Exception ex)
            {
                _logger.Warning($"Failed to initialize update service: {ex.Message}");
            }

            _logger.Info("App startup complete");
        }
        catch (Exception ex)
        {
            _logger.Error("Fatal error during initialization", ex);
            // Don't crash - try to show what we can
        }
    }

    private void OnUpdateAvailable(object? sender, LinuxUpdateInfo updateInfo)
    {
        _logger.Info($"Update available: v{updateInfo.Version}");
        
        GLib.Idle.Add(() =>
        {
            try
            {
                var notificationService = Services.GetRequiredService<INotificationService>();
                var settings = Services.GetRequiredService<ISettingsService>().Settings;

                if (settings.AutoUpdate)
                {
                    // Show notification with download link
                    _ = notificationService.ShowAsync(
                        "Update Available",
                        $"Canopy v{updateInfo.Version} is available. Click to download.");
                    
                    // Open the release page
                    Services.GetRequiredService<LinuxUpdateService>().OpenReleasePage(updateInfo);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Failed to show update notification: {ex.Message}");
            }
            return false;
        });
    }

    /// <summary>
    /// Sends a protocol URI to the running instance via a file.
    /// </summary>
    private void SendProtocolToRunningInstance(string uri)
    {
        try
        {
            var protocolDir = GetProtocolDirectory();
            Directory.CreateDirectory(protocolDir);
            
            var filename = Path.Combine(protocolDir, $"protocol_{DateTime.UtcNow.Ticks}.uri");
            File.WriteAllText(filename, uri);
            
            _logger.Info($"Wrote protocol file: {filename}");
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to send protocol to running instance: {ex.Message}");
        }
    }

    /// <summary>
    /// Starts watching for protocol files from other instances.
    /// Uses both FileSystemWatcher and polling for reliability.
    /// </summary>
    private void StartProtocolWatcher()
    {
        try
        {
            var protocolDir = GetProtocolDirectory();
            Directory.CreateDirectory(protocolDir);
            
            // Clean up any old protocol files first, but process them
            foreach (var file in Directory.GetFiles(protocolDir, "*.uri"))
            {
                try
                {
                    var uri = File.ReadAllText(file).Trim();
                    File.Delete(file);
                    
                    if (Uri.TryCreate(uri, UriKind.Absolute, out var parsedUri))
                    {
                        _logger.Info($"Found pending protocol file: {uri}");
                        GLib.Timeout.Add(100, () =>
                        {
                            HandleProtocolActivation(parsedUri);
                            return false;
                        });
                    }
                }
                catch { }
            }
            
            // Start FileSystemWatcher
            try
            {
                _protocolWatcher = new FileSystemWatcher(protocolDir, "*.uri")
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.LastWrite
                };
                
                _protocolWatcher.Created += OnProtocolFileCreated;
                _protocolWatcher.Changed += OnProtocolFileCreated;
                _protocolWatcher.EnableRaisingEvents = true;
                
                _logger.Debug($"FileSystemWatcher started: {protocolDir}");
            }
            catch (Exception ex)
            {
                _logger.Warning($"FileSystemWatcher failed: {ex.Message}, using polling fallback");
            }
            
            // Also use polling as a fallback (FileSystemWatcher can be unreliable on some Linux filesystems)
            _protocolPollTimer = new System.Timers.Timer(500);
            _protocolPollTimer.Elapsed += (_, _) => PollForProtocolFiles();
            _protocolPollTimer.Start();
            
            _logger.Debug("Protocol polling started");
        }
        catch (Exception ex)
        {
            _logger.Warning($"Failed to start protocol watcher: {ex.Message}");
        }
    }

    private void PollForProtocolFiles()
    {
        try
        {
            var protocolDir = GetProtocolDirectory();
            if (!Directory.Exists(protocolDir)) return;
            
            foreach (var file in Directory.GetFiles(protocolDir, "*.uri"))
            {
                ProcessProtocolFile(file);
            }
        }
        catch
        {
            // Ignore polling errors
        }
    }

    private void OnProtocolFileCreated(object sender, FileSystemEventArgs e)
    {
        _logger.Debug($"Protocol file event: {e.ChangeType} - {e.FullPath}");
        
        // Small delay to ensure file is fully written
        Thread.Sleep(50);
        ProcessProtocolFile(e.FullPath);
    }

    private void ProcessProtocolFile(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return;
            
            var uri = File.ReadAllText(filePath).Trim();
            
            // Try to delete the file
            try { File.Delete(filePath); }
            catch { return; } // If we can't delete, another thread is handling it
            
            if (Uri.TryCreate(uri, UriKind.Absolute, out var parsedUri))
            {
                _logger.Info($"Processing protocol from file: {uri}");
                
                GLib.Idle.Add(() =>
                {
                    HandleProtocolActivation(parsedUri);
                    return false;
                });
            }
        }
        catch (Exception ex)
        {
            _logger.Warning($"Error processing protocol file {filePath}: {ex.Message}");
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

        // Update service
        services.AddSingleton<LinuxUpdateService>();

        services.AddSingleton<LinuxHotkeyService>();
        services.AddSingleton<IHotkeyService>(sp => sp.GetRequiredService<LinuxHotkeyService>());

        services.AddSingleton<IGameScanner, SteamScanner>();
        services.AddSingleton<IGameScanner, RemoteMappingsScanner>();
        services.AddSingleton<IGameDetector, LinuxGameDetector>();
        services.AddSingleton<GameService>();

        services.AddSingleton<MainWindow>();
        services.AddSingleton<LinuxWebViewIpcBridge>();
    }

    private void SetupCoordinatorEvents()
    {
        if (_appCoordinator == null) return;

        _appCoordinator.ShowWindowRequested += (_, _) =>
            GLib.Idle.Add(() => { _mainWindow?.ShowAndActivate(); return false; });

        _appCoordinator.ShowSettingsRequested += (_, _) =>
            GLib.Idle.Add(() => { ShowSettingsWindow(); return false; });

        // TODO: Re-add overlay support when ready
        // _appCoordinator.OverlayToggleRequested += ...
        // _appCoordinator.OverlayDragToggleRequested += ...

        _appCoordinator.TokenReady += (_, e) =>
        {
            _logger.Info("TokenReady event received, sending to webview");
            GLib.Idle.Add(() =>
            {
                _ = Services.GetRequiredService<LinuxWebViewIpcBridge>().Send(new Core.IPC.IpcMessage
                {
                    Type = Core.IPC.IpcMessageTypes.TokenReceived,
                    Payload = new { token = e.Token }
                });
                return false;
            });
        };

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
            _settingsWindow.DeleteEvent += (_, args) => {
                args.RetVal = true;
                _settingsWindow.Hide();
            };
            _settingsWindow.Hidden += (_, _) => {
                // Don't null it out - reuse the window
            };
        }
        
        // Load settings and show
        _settingsWindow.LoadAndShow();
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
                // TODO: Show overlay when implemented
            }
            return false;
        });
    }

    private void OnGameStopped(object? sender, GameStatePayload payload)
    {
        _logger.Info($"Game stopped: {payload.Name}");
        
        GLib.Idle.Add(() =>
        {
            // TODO: Update overlay when implemented
            return false;
        });
    }

    private void HandleProtocolActivation(Uri uri)
    {
        _logger.Info($"HandleProtocolActivation: {uri}");
        _logger.Debug($"_mainWindow is null: {_mainWindow == null}");
        _logger.Debug($"_appCoordinator is null: {_appCoordinator == null}");
        
        if (_mainWindow != null)
        {
            _mainWindow.ShowAndActivate();
        }
        else
        {
            _logger.Warning("MainWindow is null in HandleProtocolActivation!");
        }

        if (_appCoordinator != null)
        {
            _logger.Debug("Calling AppCoordinator.HandleProtocolActivationAsync");
            _ = _appCoordinator.HandleProtocolActivationAsync(uri);
        }
        else
        {
            _logger.Warning("AppCoordinator is null, falling back to direct token handling");
            var uriString = uri.ToString();
            if (uriString.Contains("#id_token="))
            {
                var token = uriString.Split("#id_token=")[1];
                _logger.Debug($"Extracted token, calling OnTokenReceived");
                _mainWindow?.OnTokenReceived(token);
            }
        }
    }

    public async Task ShutdownAsync()
    {
        _logger.Info("Shutting down...");
        
        _protocolPollTimer?.Stop();
        _protocolPollTimer?.Dispose();
        _protocolWatcher?.Dispose();
        _appCoordinator?.Dispose();
        _trayIconService?.Dispose();
        
        Services.GetService<LinuxHotkeyService>()?.Dispose();
        Services.GetService<LinuxUpdateService>()?.Dispose();

        if (_host != null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        SingleInstanceGuard.Release();
        
        _logger.Info("Shutdown complete");
    }
}
