using Canopy.Core.Application;
using Canopy.Core.IPC;
using Canopy.Core.Logging;
using Canopy.Linux.Services;
using Gdk;
using Gtk;
using Microsoft.Extensions.DependencyInjection;
using System.Runtime.InteropServices;
using System.Text.Json;
using Window = Gtk.Window;
using WindowType = Gtk.WindowType;

namespace Canopy.Linux.Windows;

/// <summary>
/// Overlay window that displays on top of games and other windows.
/// Uses X11 EWMH hints (_NET_WM_STATE_ABOVE, _NET_WM_STATE_FULLSCREEN bypass)
/// and periodic raising to stay above fullscreen applications.
/// </summary>
public class OverlayWindow : Window
{
    private readonly ICanopyLogger _logger;
    private readonly LinuxWebViewIpcBridge _ipcBridge;
    private readonly ISettingsService _settingsService;
    
    private bool _isDragEnabled;
    private bool _isDragging;
    private int _dragStartX, _dragStartY;
    private int _windowStartX, _windowStartY;
    private uint _keepOnTopTimerId;
    private bool _x11Initialized;

    // UI Elements
    private Label? _gameNameLabel;
    private Label? _gameStatusLabel;
    private Label? _pointsLabel;
    private EventBox? _contentBox;
    private EventBox? _dragOverlay;

    private const int OverlayWidth = 280;
    private const int OverlayHeight = 480;

    public bool IsVisible { get; private set; }
    public bool IsDragEnabled => _isDragEnabled;

    public OverlayWindow() : base(WindowType.Toplevel)
    {
        _logger = CanopyLoggerFactory.CreateLogger<OverlayWindow>();
        _ipcBridge = App.Services.GetRequiredService<LinuxWebViewIpcBridge>();
        _settingsService = App.Services.GetRequiredService<ISettingsService>();

        _ipcBridge.Subscribe(IpcMessageTypes.GameStarted, OnGameStarted);
        _ipcBridge.Subscribe(IpcMessageTypes.DataReceived, OnDataReceived);

        ConfigureWindow();
        BuildUI();
        
        // Wire up events for X11 setup
        Realized += (s, e) => {
            _logger.Debug("OverlayWindow realized");
            RestorePosition();
            InitializeX11();
        };
        
        MapEvent += (s, e) => {
            _logger.Debug("OverlayWindow mapped");
            EnsureOnTop();
        };
        
        _logger.Info("OverlayWindow created");
    }

    private void ConfigureWindow()
    {
        Title = "Canopy Overlay";
        SetDefaultSize(OverlayWidth, OverlayHeight);
        SetSizeRequest(OverlayWidth, OverlayHeight);
        
        // No window decorations
        Decorated = false;
        
        // Don't show in taskbar or pager
        SkipTaskbarHint = true;
        SkipPagerHint = true;
        
        // Stay above other windows
        KeepAbove = true;
        
        // Don't steal focus
        AcceptFocus = false;
        FocusOnMap = false;
        
        // Appear on all workspaces
        Stick();
        
        // Use Dock type for overlay behavior - this is key for staying above fullscreen
        // Dock windows are meant for panels/docks and stay above normal windows
        TypeHint = WindowTypeHint.Dock;
        
        // RGBA for transparency
        AppPaintable = true;
        if (Screen.IsComposited)
        {
            var visual = Screen.RgbaVisual;
            if (visual != null)
            {
                Visual = visual;
                _logger.Debug("RGBA visual enabled (compositing active)");
            }
        }
        else
        {
            _logger.Debug("Compositing not active - transparency may not work");
        }

        // Dragging support
        AddEvents((int)(EventMask.ButtonPressMask | EventMask.ButtonReleaseMask | EventMask.PointerMotionMask));
        ButtonPressEvent += OnButtonPress;
        ButtonReleaseEvent += OnButtonRelease;
        MotionNotifyEvent += OnMotionNotify;
    }

    /// <summary>
    /// Initialize X11-specific properties for overlay behavior.
    /// </summary>
    private void InitializeX11()
    {
        if (_x11Initialized || Window == null) return;
        
        var sessionType = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
        _logger.Debug($"Session type: {sessionType}");
        
        if (string.Equals(sessionType, "wayland", StringComparison.OrdinalIgnoreCase))
        {
            _logger.Info("Running on Wayland - X11 hints not applicable");
            _x11Initialized = true;
            return;
        }

        try
        {
            ApplyX11OverlayProperties();
            _x11Initialized = true;
        }
        catch (Exception ex)
        {
            _logger.Warning($"X11 initialization failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Applies X11 Extended Window Manager Hints for overlay behavior.
    /// Reference: https://specifications.freedesktop.org/wm-spec/wm-spec-1.3.html
    /// </summary>
    private void ApplyX11OverlayProperties()
    {
        var display = gdk_x11_get_default_xdisplay();
        if (display == IntPtr.Zero)
        {
            _logger.Debug("Failed to get X11 display");
            return;
        }

        var xid = gdk_x11_window_get_xid(Window.Handle);
        if (xid == IntPtr.Zero)
        {
            _logger.Debug("Failed to get X11 window ID");
            return;
        }

        var root = XDefaultRootWindow(display);
        
        // Get atoms
        var netWmState = XInternAtom(display, "_NET_WM_STATE", false);
        var netWmStateAbove = XInternAtom(display, "_NET_WM_STATE_ABOVE", false);
        var netWmStateSticky = XInternAtom(display, "_NET_WM_STATE_STICKY", false);
        var netWmStateSkipTaskbar = XInternAtom(display, "_NET_WM_STATE_SKIP_TASKBAR", false);
        var netWmStateSkipPager = XInternAtom(display, "_NET_WM_STATE_SKIP_PAGER", false);

        // Add _NET_WM_STATE_ABOVE
        SendNetWmStateMessage(display, xid, root, netWmState, 1, netWmStateAbove, IntPtr.Zero);
        
        // Add _NET_WM_STATE_STICKY (show on all workspaces)
        SendNetWmStateMessage(display, xid, root, netWmState, 1, netWmStateSticky, IntPtr.Zero);
        
        // Add skip taskbar/pager
        SendNetWmStateMessage(display, xid, root, netWmState, 1, netWmStateSkipTaskbar, netWmStateSkipPager);

        XFlush(display);
        
        _logger.Debug("X11 overlay properties applied (_NET_WM_STATE_ABOVE, _NET_WM_STATE_STICKY)");
    }

    private void SendNetWmStateMessage(IntPtr display, IntPtr window, IntPtr root,
        IntPtr messageType, int action, IntPtr atom1, IntPtr atom2)
    {
        var ev = new XClientMessageEvent
        {
            type = 33, // ClientMessage
            serial = IntPtr.Zero,
            send_event = true,
            display = display,
            window = window,
            message_type = messageType,
            format = 32,
            data_l0 = action, // 0=remove, 1=add, 2=toggle
            data_l1 = atom1,
            data_l2 = atom2,
            data_l3 = 1, // Source indication (1=normal application)
            data_l4 = IntPtr.Zero
        };

        // SubstructureRedirectMask | SubstructureNotifyMask = 0x180000
        XSendEvent(display, root, false, 0x180000, ref ev);
    }

    /// <summary>
    /// Ensures the overlay stays on top. Called periodically and on map events.
    /// </summary>
    private void EnsureOnTop()
    {
        if (!IsVisible) return;

        try
        {
            KeepAbove = true;
            Window?.Raise();
            
            // Re-apply X11 properties periodically to combat window manager interference
            if (_x11Initialized)
            {
                try
                {
                    var display = gdk_x11_get_default_xdisplay();
                    var xid = gdk_x11_window_get_xid(Window!.Handle);
                    if (display != IntPtr.Zero && xid != IntPtr.Zero)
                    {
                        // Use XRaiseWindow for more forceful raising
                        XRaiseWindow(display, xid);
                        XFlush(display);
                    }
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            _logger.Debug($"EnsureOnTop error: {ex.Message}");
        }
    }

    private void BuildUI()
    {
        var overlay = new Overlay();
        
        // Main content
        _contentBox = new EventBox();
        _contentBox.Name = "overlay-content";
        
        var vbox = new Box(Orientation.Vertical, 12);
        vbox.MarginStart = 20;
        vbox.MarginEnd = 20;
        vbox.MarginTop = 20;
        vbox.MarginBottom = 20;

        // Game info header
        var headerBox = new Box(Orientation.Vertical, 4);
        headerBox.Halign = Align.Center;

        _gameNameLabel = new Label("No game running") { Name = "game-name" };
        _gameStatusLabel = new Label("Start a game to begin") { Name = "game-status" };

        headerBox.PackStart(_gameNameLabel, false, false, 0);
        headerBox.PackStart(_gameStatusLabel, false, false, 0);

        // Points display
        var pointsBox = new Box(Orientation.Vertical, 4);
        pointsBox.Halign = Align.Center;

        _pointsLabel = new Label("--") { Name = "points-value" };
        var pointsSubLabel = new Label("POINTS EARNED") { Name = "points-label" };

        pointsBox.PackStart(_pointsLabel, false, false, 0);
        pointsBox.PackStart(pointsSubLabel, false, false, 0);

        // Stats grid
        var statsBox = BuildStatsGrid();

        vbox.PackStart(headerBox, false, false, 8);
        vbox.PackStart(pointsBox, false, false, 16);
        vbox.PackStart(statsBox, true, true, 0);

        _contentBox.Add(vbox);
        overlay.Add(_contentBox);

        // Drag mode overlay
        _dragOverlay = new EventBox();
        _dragOverlay.Name = "drag-overlay";
        _dragOverlay.NoShowAll = true;
        _dragOverlay.Visible = false;
        
        var dragLabel = new Label("Drag to move\nPress shortcut again to exit");
        dragLabel.Name = "drag-label";
        dragLabel.Justify = Justification.Center;
        dragLabel.Valign = Align.Center;
        dragLabel.Halign = Align.Center;
        _dragOverlay.Add(dragLabel);
        
        overlay.AddOverlay(_dragOverlay);

        Add(overlay);
        ApplyCSS();
    }

    private Box BuildStatsGrid()
    {
        var box = new Box(Orientation.Vertical, 12);
        
        var row1 = new Box(Orientation.Horizontal, 16) { Homogeneous = true };
        row1.PackStart(CreateStat("Total Points", "--"), true, true, 0);
        row1.PackStart(CreateStat("Donations", "--"), true, true, 0);

        var row2 = new Box(Orientation.Horizontal, 16) { Homogeneous = true };
        row2.PackStart(CreateStat("To Next", "--"), true, true, 0);
        row2.PackStart(CreateStat("Total Given", "--"), true, true, 0);

        box.PackStart(row1, false, false, 0);
        box.PackStart(row2, false, false, 0);
        
        return box;
    }

    private Box CreateStat(string label, string value)
    {
        var box = new Box(Orientation.Vertical, 2) { Halign = Align.Center };
        box.PackStart(new Label(label) { Name = "stat-label" }, false, false, 0);
        box.PackStart(new Label(value) { Name = "stat-value" }, false, false, 0);
        return box;
    }

    private void ApplyCSS()
    {
        var css = @"
            #overlay-content {
                background-color: rgba(15, 15, 15, 0.92);
                border-radius: 12px;
                border: 1px solid rgba(255, 255, 255, 0.1);
            }
            #game-name {
                color: #ffffff;
                font-size: 18px;
                font-weight: bold;
            }
            #game-status {
                color: rgba(255, 255, 255, 0.5);
                font-size: 11px;
            }
            #points-value {
                color: #ffffff;
                font-size: 36px;
                font-weight: bold;
            }
            #points-label {
                color: rgba(255, 255, 255, 0.5);
                font-size: 12px;
                letter-spacing: 2px;
            }
            #stat-label {
                color: rgba(255, 255, 255, 0.5);
                font-size: 10px;
            }
            #stat-value {
                color: #ffffff;
                font-size: 18px;
                font-weight: bold;
            }
            #drag-overlay {
                background-color: rgba(234, 179, 8, 0.85);
                border-radius: 12px;
            }
            #drag-label {
                color: #000000;
                font-size: 14px;
                font-weight: bold;
            }
        ";

        try
        {
            var provider = new CssProvider();
            provider.LoadFromData(css);
            StyleContext.AddProviderForScreen(Screen.Default, provider, StyleProviderPriority.Application);
        }
        catch (Exception ex)
        {
            _logger.Warning($"CSS error: {ex.Message}");
        }
    }

    private void RestorePosition()
    {
        var settings = _settingsService.Settings;
        if (settings.OverlayX.HasValue && settings.OverlayY.HasValue)
        {
            Move(settings.OverlayX.Value, settings.OverlayY.Value);
            _logger.Debug($"Restored position: {settings.OverlayX}, {settings.OverlayY}");
        }
        else
        {
            PositionAtScreenEdge();
        }
    }

    public void PositionAtScreenEdge()
    {
        var screen = Screen.Default;
        if (screen != null)
        {
            var x = screen.Width - OverlayWidth - 20;
            var y = 20;
            Move(x, y);
            _logger.Debug($"Default position: {x}, {y}");
        }
    }

    private void SavePosition()
    {
        GetPosition(out int x, out int y);
        _settingsService.Update(s => { s.OverlayX = x; s.OverlayY = y; });
        _logger.Debug($"Saved position: {x}, {y}");
    }

    public void UpdateGameInfo(string? gameName, string? elapsedTime = null)
    {
        GLib.Idle.Add(() =>
        {
            if (gameName != null)
            {
                _gameNameLabel!.Text = gameName;
                _gameStatusLabel!.Text = elapsedTime ?? "Playing now";
                _pointsLabel!.Text = "0";
            }
            else
            {
                _gameNameLabel!.Text = "No game running";
                _gameStatusLabel!.Text = "Start a game to begin";
                _pointsLabel!.Text = "--";
            }
            return false;
        });
    }

    private void OnGameStarted(IpcMessage message)
    {
        _logger.Debug("IPC: GameStarted received");
    }

    private void OnDataReceived(IpcMessage message)
    {
        if (message.Payload == null) return;

        try
        {
            var payload = (JsonElement)message.Payload;
            if (payload.TryGetProperty("type", out var typeElem) && 
                typeElem.GetString() == "INTERVAL_COUNTER_UPDATE" &&
                payload.TryGetProperty("data", out var data))
            {
                var points = data.GetInt32();
                GLib.Idle.Add(() =>
                {
                    _pointsLabel!.Text = points.ToString();
                    return false;
                });
            }
        }
        catch (Exception ex)
        {
            _logger.Debug($"Data processing error: {ex.Message}");
        }
    }

    public void Toggle()
    {
        _logger.Debug($"Toggle called. IsVisible={IsVisible}, EnableOverlay={_settingsService.Settings.EnableOverlay}");
        
        if (!_settingsService.Settings.EnableOverlay)
        {
            _logger.Debug("Overlay disabled in settings");
            return;
        }

        if (IsVisible)
            HideOverlay();
        else
            ShowOverlay();
    }

    public void ShowOverlay()
    {
        if (!_settingsService.Settings.EnableOverlay)
        {
            _logger.Debug("ShowOverlay: Overlay disabled in settings");
            return;
        }

        _logger.Info("Showing overlay");
        
        GLib.Idle.Add(() =>
        {
            try
            {
                ShowAll();
                _dragOverlay?.Hide();
                RestorePosition();
                
                // Apply all overlay properties
                KeepAbove = true;
                InitializeX11();
                EnsureOnTop();
                
                IsVisible = true;
                StartKeepOnTopTimer();
                
                _logger.Debug("Overlay shown successfully");
            }
            catch (Exception ex)
            {
                _logger.Error($"ShowOverlay failed: {ex.Message}");
            }
            return false;
        });
    }

    public void HideOverlay()
    {
        _logger.Info("Hiding overlay");
        
        if (_isDragEnabled)
        {
            ToggleDragMode();
        }
        
        StopKeepOnTopTimer();

        GLib.Idle.Add(() =>
        {
            Hide();
            IsVisible = false;
            _logger.Debug("Overlay hidden");
            return false;
        });
    }

    private void StartKeepOnTopTimer()
    {
        StopKeepOnTopTimer();
        
        // Aggressively ensure window stays on top every 500ms
        // This is necessary because some window managers (especially with fullscreen games)
        // may lower the window despite _NET_WM_STATE_ABOVE
        _keepOnTopTimerId = GLib.Timeout.Add(500, () =>
        {
            if (!IsVisible) return false;
            
            EnsureOnTop();
            return true;
        });
        
        _logger.Debug("Keep-on-top timer started (500ms interval)");
    }

    private void StopKeepOnTopTimer()
    {
        if (_keepOnTopTimerId != 0)
        {
            GLib.Source.Remove(_keepOnTopTimerId);
            _keepOnTopTimerId = 0;
            _logger.Debug("Keep-on-top timer stopped");
        }
    }

    public void ToggleDragMode()
    {
        _isDragEnabled = !_isDragEnabled;
        _logger.Debug($"Drag mode: {_isDragEnabled}");
        
        GLib.Idle.Add(() =>
        {
            if (_dragOverlay != null)
            {
                _dragOverlay.Visible = _isDragEnabled;
                if (_isDragEnabled) _dragOverlay.ShowAll();
            }
            AcceptFocus = _isDragEnabled;
            return false;
        });
    }

    private void OnButtonPress(object? sender, ButtonPressEventArgs args)
    {
        if (_isDragEnabled && args.Event.Button == 1)
        {
            _isDragging = true;
            _dragStartX = (int)args.Event.XRoot;
            _dragStartY = (int)args.Event.YRoot;
            GetPosition(out _windowStartX, out _windowStartY);
            _logger.Debug($"Drag started at {_dragStartX}, {_dragStartY}");
        }
    }

    private void OnButtonRelease(object? sender, ButtonReleaseEventArgs args)
    {
        if (_isDragging)
        {
            _isDragging = false;
            SavePosition();
            _logger.Debug("Drag ended, position saved");
        }
    }

    private void OnMotionNotify(object? sender, MotionNotifyEventArgs args)
    {
        if (_isDragging)
        {
            var dx = (int)args.Event.XRoot - _dragStartX;
            var dy = (int)args.Event.YRoot - _dragStartY;
            Move(_windowStartX + dx, _windowStartY + dy);
        }
    }

    #region X11 Interop

    [StructLayout(LayoutKind.Sequential)]
    private struct XClientMessageEvent
    {
        public int type;
        public IntPtr serial;
        [MarshalAs(UnmanagedType.Bool)] public bool send_event;
        public IntPtr display;
        public IntPtr window;
        public IntPtr message_type;
        public int format;
        public int data_l0;
        public IntPtr data_l1;
        public IntPtr data_l2;
        public IntPtr data_l3;
        public IntPtr data_l4;
    }

    [DllImport("libgdk-3.so.0")]
    private static extern IntPtr gdk_x11_get_default_xdisplay();

    [DllImport("libgdk-3.so.0")]
    private static extern IntPtr gdk_x11_window_get_xid(IntPtr gdkWindow);

    [DllImport("libX11.so.6")]
    private static extern IntPtr XInternAtom(IntPtr display, string atomName, bool onlyIfExists);

    [DllImport("libX11.so.6")]
    private static extern IntPtr XDefaultRootWindow(IntPtr display);

    [DllImport("libX11.so.6")]
    private static extern int XSendEvent(IntPtr display, IntPtr window, bool propagate, int mask, ref XClientMessageEvent ev);

    [DllImport("libX11.so.6")]
    private static extern int XRaiseWindow(IntPtr display, IntPtr window);

    [DllImport("libX11.so.6")]
    private static extern int XFlush(IntPtr display);

    #endregion
}
