using Canopy.Core.Auth;
using Canopy.Core.GameDetection;
using Canopy.Core.IPC;
using Canopy.Core.Models;
using Canopy.Windows.Interop;
using Canopy.Windows.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using System.Diagnostics;
using Windows.Graphics;
using Windows.UI;
using WinRT.Interop;

namespace Canopy.Windows;

/// <summary>
/// Main application window hosting WebView2 for the Canopy web app.
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly WebViewIpcBridge _ipcBridge;
    private readonly GameService _gameService;
    private AppWindow? _appWindow;
    private bool _isInitialized;
    private MinimumSizeEnforcer? _minimumSizeEnforcer;
    private GameStatePayload? _gameStatePayload;

#if DEBUG
    private const string TargetUrl = "http://localhost:3000?sharp";
#else
    private const string TargetUrl = "https://canopy.ovv.gg?sharp";
#endif

    private const int MinWidth = 1170;
    private const int MinHeight = 645;
    private const int DefaultWidth = 1200;
    private const int DefaultHeight = 800;

    public MainWindow()
    {
        InitializeComponent();
        SetupWindow();

        _ipcBridge = App.Services.GetRequiredService<WebViewIpcBridge>();
        _gameService = App.Services.GetRequiredService<GameService>();

        _gameService.GamesDetected += OnGamesDetected;
        _gameService.GameStarted += OnGameStarted;
        _gameService.GameStopped += OnGameStopped;

        Activated += MainWindow_Activated;
        Closed += MainWindow_Closed;
    }

    private void SetupWindow()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        _minimumSizeEnforcer = new MinimumSizeEnforcer(hwnd, MinWidth, MinHeight);

        if (_appWindow == null) return;

        _appWindow.Resize(new SizeInt32(DefaultWidth, DefaultHeight));

        var appWindow = this.AppWindow;
        appWindow.SetPresenter(AppWindowPresenterKind.Default);

        var titleBar = appWindow.TitleBar;
        this.SetTitleBar(AppTitleBar);
        titleBar.ExtendsContentIntoTitleBar = true;

        titleBar.ButtonForegroundColor = Colors.White;
        titleBar.ButtonHoverForegroundColor = Colors.White;
        titleBar.ButtonHoverBackgroundColor = Color.FromArgb(30, 255, 255, 255);
        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        titleBar.ButtonPressedBackgroundColor = Color.FromArgb(60, 255, 255, 255);

        // Center on screen
        var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Nearest);
        if (displayArea != null)
        {
            var centerX = (displayArea.WorkArea.Width - DefaultWidth) / 2;
            var centerY = (displayArea.WorkArea.Height - DefaultHeight) / 2;
            _appWindow.Move(new PointInt32(centerX, centerY));
        }

        _appWindow.Closing += AppWindow_Closing;
    }

    private async void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        if (!_isInitialized)
        {
            await InitializeWebViewAsync();
            _isInitialized = true;
        }
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        _gameService.GamesDetected -= OnGamesDetected;
        _gameService.GameStarted -= OnGameStarted;
        _gameService.GameStopped -= OnGameStopped;
        _minimumSizeEnforcer?.Dispose();
    }

    private void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        args.Cancel = true;
        Hide();
    }

    private async Task InitializeWebViewAsync()
    {
        try
        {
            await WebView.EnsureCoreWebView2Async();
            ConfigureWebViewSettings();
            _ipcBridge.Initialize(WebView.CoreWebView2);

            Debug.WriteLine($"Navigating to: {TargetUrl}");
            WebView.Source = new Uri(TargetUrl);
            WebView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
            WebView.CoreWebView2.NavigationStarting += OnNavigationStarting;
            WebView.CoreWebView2.NewWindowRequested += OnNewWindowRequested;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"WebView initialization failed: {ex.Message}");
            LoadingOverlay.Visibility = Visibility.Visible;
            var errorText = new TextBlock
            {
                Text = $"Failed to initialize WebView2:\n{ex.Message}\n\nMake sure WebView2 Runtime is installed.",
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.Red),
                TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            ((Grid)LoadingOverlay).Children.Clear();
            ((Grid)LoadingOverlay).Children.Add(errorText);
        }
    }

    private void ConfigureWebViewSettings()
    {
        var settings = WebView.CoreWebView2.Settings;

#if DEBUG
        settings.AreDevToolsEnabled = true;
        settings.AreDefaultContextMenusEnabled = true;
#else
        settings.AreDevToolsEnabled = false;
        settings.AreDefaultContextMenusEnabled = false;
#endif

        settings.IsStatusBarEnabled = true;
        settings.IsZoomControlEnabled = false;
        settings.IsBuiltInErrorPageEnabled = true;
        settings.IsWebMessageEnabled = true;
        settings.IsScriptEnabled = true;
    }

    private void OnNavigationStarting(
        Microsoft.Web.WebView2.Core.CoreWebView2 sender,
        Microsoft.Web.WebView2.Core.CoreWebView2NavigationStartingEventArgs args)
    {
        Debug.WriteLine($"Navigation starting: {args.Uri}");
    }

    private void OnNavigationCompleted(
        Microsoft.Web.WebView2.Core.CoreWebView2 sender,
        Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs args)
    {
        Debug.WriteLine($"Navigation completed. Success: {args.IsSuccess}, Status: {args.HttpStatusCode}");
        
        if (args.IsSuccess)
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
            WebView.Visibility = Visibility.Visible;
            SendInitialData();
        }
        else
        {
            Debug.WriteLine($"Navigation failed with error: {args.WebErrorStatus}");
        }
    }

    private void OnNewWindowRequested(
        Microsoft.Web.WebView2.Core.CoreWebView2 sender,
        Microsoft.Web.WebView2.Core.CoreWebView2NewWindowRequestedEventArgs args)
    {
        args.Handled = true;
        App.Services.GetRequiredService<WindowsPlatformServices>().OpenUrl(args.Uri);
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
        },
        TimeSpan.FromSeconds(10));

        if (ack != null && _gameStatePayload != null)
        {
            App.DispatcherQueue.TryEnqueue(async () => await _ipcBridge.Send(new IpcMessage
            {
                Type = IpcMessageTypes.GameStateUpdate,
                Payload = _gameStatePayload
            }));
        }

        await _ipcBridge.Send(new IpcMessage
        {
            Type = IpcMessageTypes.Syn,
            Payload = new { prime = true }
        });
    }

    private void OnGamesDetected(object? sender, IReadOnlyList<DetectedGame> games)
    {
        App.DispatcherQueue.TryEnqueue(async () =>
        {
            await _ipcBridge.Send(new IpcMessage
            {
                Type = IpcMessageTypes.GamesDetected,
                Payload = games
            });
        });
    }

    private void OnGameStarted(object? sender, GameStatePayload payload)
    {
        _gameStatePayload = payload;
        
        // Dispatch to UI thread - this is critical!
        App.DispatcherQueue.TryEnqueue(async () =>
        {
            if (_isInitialized)
            {
                await _ipcBridge.Send(new IpcMessage
                {
                    Type = IpcMessageTypes.GameStateUpdate,
                    Payload = payload
                });
            }
        });
        // Note: Don't call ShowAndActivate() here - let the user control the window
    }

    private void OnGameStopped(object? sender, GameStatePayload payload)
    {
        _gameStatePayload = payload;
        App.DispatcherQueue.TryEnqueue(async () =>
        {
            await _ipcBridge.Send(new IpcMessage
            {
                Type = IpcMessageTypes.GameStateUpdate,
                Payload = payload
            });
        });
    }

    /// <summary>
    /// Handles OAuth token received from protocol activation.
    /// </summary>
    public void OnTokenReceived(string token)
    {
        App.DispatcherQueue.TryEnqueue(async () =>
        {
            Debug.WriteLine("Token received, exchanging...");

            try
            {
                var idToken = token.Split('&').FirstOrDefault();
                if (string.IsNullOrEmpty(idToken))
                {
                    Debug.WriteLine("No id_token found");
                    return;
                }

                var tokenService = new TokenExchangeService();
                var customToken = await tokenService.ExchangeTokenAsync(idToken);

                if (string.IsNullOrEmpty(customToken))
                {
                    Debug.WriteLine("Token exchange failed");
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
                Debug.WriteLine($"Token exchange error: {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Hides the window to system tray.
    /// </summary>
    public void Hide() => _appWindow?.Hide();

    /// <summary>
    /// Shows and activates the window.
    /// Must be called from UI thread.
    /// </summary>
    public void ShowAndActivate()
    {
        // Ensure we're on the UI thread
        if (App.DispatcherQueue.HasThreadAccess)
        {
            _appWindow?.Show();
            Activate();
        }
        else
        {
            App.DispatcherQueue.TryEnqueue(() =>
            {
                _appWindow?.Show();
                Activate();
            });
        }
    }

    /// <summary>
    /// Gets the window handle for Win32 interop.
    /// </summary>
    public IntPtr GetHandle() => WindowNative.GetWindowHandle(this);
}
