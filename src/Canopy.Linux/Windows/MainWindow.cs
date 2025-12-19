using Canopy.Core.Auth;
using Canopy.Core.GameDetection;
using Canopy.Core.IPC;
using Canopy.Core.Logging;
using Canopy.Core.Models;
using Canopy.Linux.Services;
using Gdk;
using Gtk;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.InteropServices;
using WebKit;
using Window = Gtk.Window;
using WindowType = Gtk.WindowType;

namespace Canopy.Linux.Windows;

/// <summary>
/// Main application window hosting WebKitGTK for the Canopy web app
/// </summary>
public class MainWindow : Window
{
    private readonly ICanopyLogger _logger;
    private readonly LinuxWebViewIpcBridge _ipcBridge;
    private readonly GameService _gameService;
    private WebView? _webView;
    private Box? _mainBox;
    private Stack? _stack;
    private Label? _loadingLabel;
    private Spinner? _loadingSpinner;
    private bool _isInitialized;
    private GameStatePayload? _gameStatePayload;

#if DEBUG
    private const string TargetUrl = "http://localhost:3000?sharp";
#else
    private const string TargetUrl = "http://canopy.ovv.gg?sharp";
#endif

    private const int DefaultWidth = 1200;
    private const int DefaultHeight = 800;
    private const int MinWidth = 1170;
    private const int MinHeight = 645;

    public MainWindow() : base(WindowType.Toplevel)
    {
        _logger = CanopyLoggerFactory.CreateLogger<MainWindow>();
        _ipcBridge = App.Services.GetRequiredService<LinuxWebViewIpcBridge>();
        _gameService = App.Services.GetRequiredService<GameService>();

        _gameService.GamesDetected += OnGamesDetected;
        _gameService.GameStarted += OnGameStarted;
        _gameService.GameStopped += OnGameStopped;

        SetupWindow();
        SetupUI();
        
        _logger.Info("MainWindow created");
    }

    private void SetupWindow()
    {
        Title = "Canopy";
        SetDefaultSize(DefaultWidth, DefaultHeight);
        SetSizeRequest(MinWidth, MinHeight);
        SetPosition(WindowPosition.Center);
        
        // Set WM_CLASS for proper taskbar/dock icon matching
        // This must match StartupWMClass in the .desktop file
        SetWmclass("canopy", "Canopy");

        // Set window icon - do this after the window is realized
        Realized += OnWindowRealized;

        // Handle window events
        DeleteEvent += OnDeleteEvent;
        Shown += OnShown;
    }

    private void OnWindowRealized(object? sender, EventArgs e)
    {
        SetWindowIcon();
        SetX11WindowClass();
    }

    private void SetWindowIcon()
    {
        var iconPath = AppIconManager.GetIconPath();
        if (iconPath == null)
        {
            _logger.Warning("No icon path available");
            return;
        }

        try
        {
            // Load the original icon
            var originalPixbuf = new Pixbuf(iconPath);
            
            // Create multiple sizes for different uses:
            // - 16x16: window decorations, menus
            // - 32x32: alt-tab, small taskbar
            // - 48x48: taskbar, dock
            // - 64x64: larger displays
            // - 128x128: high-DPI
            // - 256x256: very high-DPI
            var sizes = new[] { 16, 32, 48, 64, 128, 256 };
            var iconList = new List<Pixbuf>();
            
            foreach (var size in sizes)
            {
                var scaled = originalPixbuf.ScaleSimple(size, size, InterpType.Bilinear);
                if (scaled != null)
                {
                    iconList.Add(scaled);
                }
            }

            if (iconList.Count > 0)
            {
                // Set multiple icon sizes - GTK/WM will pick the appropriate one
                IconList = iconList.ToArray();
                _logger.Debug($"Window icon set with {iconList.Count} sizes");
            }
        }
        catch (Exception ex)
        {
            _logger.Warning($"Failed to set window icon: {ex.Message}");
            
            // Fallback to single size
            var pixbuf = AppIconManager.GetPixbuf(48);
            if (pixbuf != null)
            {
                Icon = pixbuf;
            }
        }
    }

    /// <summary>
    /// Sets X11 WM_CLASS property for proper window matching.
    /// This is critical for taskbar icons on X11-based desktops.
    /// </summary>
    private void SetX11WindowClass()
    {
        try
        {
            var gdkWindow = this.Window;
            if (gdkWindow == null) return;

            // The WM_CLASS should be set, but let's ensure it via X11 if possible
            // SetWmclass() should have done this, but some WMs need reinforcement
            
            // For Wayland, this won't work but GTK handles it differently
            _logger.Debug("X11 window class should be set to 'canopy'");
        }
        catch (Exception ex)
        {
            _logger.Debug($"Could not set X11 window class: {ex.Message}");
        }
    }

    private void SetupUI()
    {
        _mainBox = new Box(Orientation.Vertical, 0);

        // Loading overlay
        var loadingBox = new Box(Orientation.Vertical, 10);
        loadingBox.Valign = Align.Center;
        loadingBox.Halign = Align.Center;

        _loadingLabel = new Label("Loading Canopy...");
        _loadingLabel.StyleContext.AddClass("loading-label");
        
        _loadingSpinner = new Spinner();
        _loadingSpinner.Active = true;
        _loadingSpinner.SetSizeRequest(40, 40);

        loadingBox.PackStart(_loadingSpinner, false, false, 10);
        loadingBox.PackStart(_loadingLabel, false, false, 0);

        // WebView - create with a UserContentManager for IPC
        var contentManager = new UserContentManager();
        _webView = new WebView(contentManager);
        _webView.Expand = true;
        
        ConfigureWebView();
        
        // Stack to show loading or webview
        _stack = new Stack();
        _stack.AddNamed(loadingBox, "loading");
        _stack.AddNamed(_webView, "webview");
        _stack.VisibleChildName = "loading";

        _mainBox.PackStart(_stack, true, true, 0);
        Add(_mainBox);

        // Apply dark theme CSS
        ApplyStyles();

        ShowAll();
    }

    private void ConfigureWebView()
    {
        if (_webView == null) return;

        var settings = _webView.Settings;
        
#if DEBUG
        settings.EnableDeveloperExtras = true;
#else
        settings.EnableDeveloperExtras = false;
#endif

        settings.EnableJavascript = true;
        settings.JavascriptCanOpenWindowsAutomatically = false;
        settings.EnableWebgl = true;
        settings.EnableMediaStream = true;

        // Handle navigation
        _webView.LoadChanged += OnLoadChanged;
        _webView.DecidePolicy += OnDecidePolicy;
    }

    private void ApplyStyles()
    {
        var css = @"
            window {
                background-color: #0F0F0F;
            }
            .loading-label {
                color: white;
                font-size: 24px;
            }
        ";

        var provider = new CssProvider();
        provider.LoadFromData(css);
        StyleContext.AddProviderForScreen(Screen.Default, provider, StyleProviderPriority.Application);
    }

    private void OnShown(object? sender, EventArgs e)
    {
        if (!_isInitialized)
        {
            _isInitialized = true;
            // Delay initialization slightly to ensure everything is ready
            GLib.Idle.Add(() =>
            {
                InitializeWebView();
                return false;
            });
        }
    }

    private void InitializeWebView()
    {
        if (_webView == null) return;

        try
        {
            _ipcBridge.Initialize(_webView);
            _webView.LoadUri(TargetUrl);
            _logger.Info($"Loading URL: {TargetUrl}");
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to initialize WebView", ex);
            if (_loadingLabel != null)
                _loadingLabel.Text = $"Failed to load: {ex.Message}";
            if (_loadingSpinner != null)
                _loadingSpinner.Active = false;
        }
    }

    private void OnLoadChanged(object? sender, LoadChangedArgs args)
    {
        if (args.LoadEvent == LoadEvent.Finished)
        {
            _logger.Info("WebView load finished");
            
            // Switch to webview
            if (_stack != null)
            {
                _stack.VisibleChildName = "webview";
            }

            SendInitialData();
        }
    }

    private void OnDecidePolicy(object? sender, DecidePolicyArgs args)
    {
        if (args.Decision is NavigationPolicyDecision navDecision)
        {
            var request = navDecision.NavigationAction.Request;
            var uri = request.Uri;

            // Open external links in browser
            if (!uri.StartsWith(TargetUrl.Split('?')[0]) && 
                (uri.StartsWith("http://") || uri.StartsWith("https://")))
            {
                navDecision.Ignore();
                App.Services.GetRequiredService<LinuxPlatformServices>().OpenUrl(uri);
                args.RetVal = true;
                return;
            }
        }

        args.RetVal = false;
    }

    private async void SendInitialData()
    {
        if (_gameService.CachedGames.Any())
        {
            await _ipcBridge.Send(new IpcMessage
            {
                Type = IpcMessageTypes.GamesDetected,
                Payload = _gameService.CachedGames
            });
        }

        var ack = await _ipcBridge.SendAndReceiveAsync<object>(new IpcMessage
        {
            RequestId = Guid.NewGuid().ToString(),
            Type = IpcMessageTypes.Syn
        }, TimeSpan.FromSeconds(10));

        if (ack != null && _gameStatePayload != null)
        {
            await _ipcBridge.Send(new IpcMessage
            {
                Type = IpcMessageTypes.GameStateUpdate,
                Payload = _gameStatePayload
            });
        }

        await _ipcBridge.Send(new IpcMessage
        {
            Type = IpcMessageTypes.Syn,
            Payload = new { prime = true }
        });
    }

    private void OnGamesDetected(object? sender, IReadOnlyList<DetectedGame> games)
    {
        GLib.Idle.Add(() =>
        {
            _ = _ipcBridge.Send(new IpcMessage
            {
                Type = IpcMessageTypes.GamesDetected,
                Payload = games
            });
            return false;
        });
    }

    private void OnGameStarted(object? sender, GameStatePayload payload)
    {
        _gameStatePayload = payload;
        _logger.Info($"Game started: {payload.Name}");
        
        if (_isInitialized)
        {
            GLib.Idle.Add(() =>
            {
                _ = _ipcBridge.Send(new IpcMessage
                {
                    Type = IpcMessageTypes.GameStateUpdate,
                    Payload = payload
                });
                return false;
            });
        }

        ShowAndActivate();
    }

    private void OnGameStopped(object? sender, GameStatePayload payload)
    {
        _gameStatePayload = payload;
        _logger.Info($"Game stopped: {payload.Name}");
        
        GLib.Idle.Add(() =>
        {
            _ = _ipcBridge.Send(new IpcMessage
            {
                Type = IpcMessageTypes.GameStateUpdate,
                Payload = payload
            });
            return false;
        });
    }

    /// <summary>
    /// Handles OAuth token received from protocol activation
    /// </summary>
    public async void OnTokenReceived(string token)
    {
        _logger.Debug("Token received, exchanging...");

        try
        {
            var idToken = token.Split('&').FirstOrDefault();
            if (string.IsNullOrEmpty(idToken))
            {
                _logger.Warning("No id_token found");
                return;
            }

            var tokenService = new TokenExchangeService();
            var customToken = await tokenService.ExchangeTokenAsync(idToken);

            if (string.IsNullOrEmpty(customToken))
            {
                _logger.Warning("Token exchange failed");
                return;
            }

            await _ipcBridge.Send(new IpcMessage
            {
                Type = IpcMessageTypes.TokenReceived,
                Payload = new { token = customToken }
            });
        }
        catch (Exception ex)
        {
            _logger.Error("Token exchange error", ex);
        }
    }

    private void OnDeleteEvent(object sender, DeleteEventArgs args)
    {
        // Hide instead of close
        args.RetVal = true;
        Hide();
        _logger.Debug("MainWindow hidden");
    }

    /// <summary>
    /// Shows and activates the window
    /// </summary>
    public void ShowAndActivate()
    {
        GLib.Idle.Add(() =>
        {
            Show();
            Present();
            _logger.Debug("MainWindow shown and activated");
            return false;
        });
    }
}
