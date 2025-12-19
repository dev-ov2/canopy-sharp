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
/// Uses multiple techniques to stay on top on X11 and Wayland.
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

    // UI Elements
    private Label? _gameNameLabel;
    private Label? _gameStatusLabel;
    private Label? _pointsLabel;
    private Box? _statsBox;
    private EventBox? _contentBox;
    private EventBox? _dragOverlay;

    private const int OverlayWidth = 280;
    private const int OverlayHeight = 520;

    public bool IsVisible { get; private set; }
    public bool IsDragEnabled => _isDragEnabled;

    public OverlayWindow() : base(WindowType.Toplevel) // Use Toplevel, not Popup
    {
        _logger = CanopyLoggerFactory.CreateLogger<OverlayWindow>();
        _ipcBridge = App.Services.GetRequiredService<LinuxWebViewIpcBridge>();
        _settingsService = App.Services.GetRequiredService<ISettingsService>();

        _ipcBridge.Subscribe(IpcMessageTypes.GameStarted, OnGameStarted);
        _ipcBridge.Subscribe(IpcMessageTypes.DataReceived, OnDataReceived);

        ConfigureWindow();
        BuildUI();
        
        Realized += OnWindowRealized;
        MapEvent += OnMapEvent;
        
        _logger.Info("OverlayWindow created");
    }

    private void ConfigureWindow()
    {
        // Basic window properties
        Title = "Canopy Overlay";
        SetDefaultSize(OverlayWidth, OverlayHeight);
        SetSizeRequest(OverlayWidth, OverlayHeight);
        
        // Remove window decorations
        Decorated = false;
        
        // Window hints for overlay behavior
        SkipTaskbarHint = true;
        SkipPagerHint = true;
        
        // Keep above other windows
        KeepAbove = true;
        
        // Don't steal focus from games
        AcceptFocus = false;
        FocusOnMap = false;
        
        // Stick to all workspaces
        Stick();
        
        // Window type hint - Utility or Notification work well for overlays
        TypeHint = WindowTypeHint.Notification;
        
        // Enable RGBA for transparency
        AppPaintable = true;
        var visual = Screen.RgbaVisual;
        if (visual != null && Screen.IsComposited)
        {
            Visual = visual;
            _logger.Debug("RGBA visual enabled");
        }

        // Set up for dragging
        AddEvents((int)(EventMask.ButtonPressMask | EventMask.ButtonReleaseMask | EventMask.PointerMotionMask));
        ButtonPressEvent += OnButtonPress;
        ButtonReleaseEvent += OnButtonRelease;
        MotionNotifyEvent += OnMotionNotify;
    }

    private void OnWindowRealized(object? sender, EventArgs e)
    {
        RestorePosition();
        SetupX11Properties();
    }

    private void OnMapEvent(object? sender, MapEventArgs e)
    {
        // Re-apply properties when window is mapped
        KeepAbove = true;
        SetupX11Properties();
    }

    /// <summary>
    /// Sets up X11 window properties for overlay behavior.
    /// </summary>
    private void SetupX11Properties()
    {
        if (Window == null) return;

        try
        {
            // Ensure we're above
            Window.Raise();
            KeepAbove = true;
            
            // Try X11-specific setup
            if (!IsWayland())
            {
                SetX11AlwaysOnTop();
            }
        }
        catch (Exception ex)
        {
            _logger.Debug($"X11 setup: {ex.Message}");
        }
    }

    private bool IsWayland()
    {
        var session = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
        return string.Equals(session, "wayland", StringComparison.OrdinalIgnoreCase);
    }

    private void SetX11AlwaysOnTop()
    {
        try
        {
            var gdkWindow = Window;
            if (gdkWindow == null) return;

            var display = gdk_x11_get_default_xdisplay();
            if (display == IntPtr.Zero) return;

            var xid = gdk_x11_window_get_xid(gdkWindow.Handle);
            if (xid == IntPtr.Zero) return;

            var root = XDefaultRootWindow(display);
            var netWmState = XInternAtom(display, "_NET_WM_STATE", false);
            var netWmStateAbove = XInternAtom(display, "_NET_WM_STATE_ABOVE", false);
            var netWmStateSticky = XInternAtom(display, "_NET_WM_STATE_STICKY", false);

            // Send _NET_WM_STATE message to add ABOVE and STICKY
            var ev = new XClientMessageEvent
            {
                type = 33, // ClientMessage
                send_event = true,
                display = display,
                window = xid,
                message_type = netWmState,
                format = 32,
                data_l0 = 1, // _NET_WM_STATE_ADD
                data_l1 = netWmStateAbove,
                data_l2 = netWmStateSticky
            };

            XSendEvent(display, root, false, 0x180000, ref ev);
            XFlush(display);
            
            _logger.Debug("X11 _NET_WM_STATE_ABOVE applied");
        }
        catch (Exception ex)
        {
            _logger.Debug($"X11 always-on-top failed: {ex.Message}");
        }
    }

    private void BuildUI()
    {
        // Main container
        var overlay = new Overlay();
        
        // Content area with dark background
        _contentBox = new EventBox();
        _contentBox.Name = "overlay-content";
        
        var mainVBox = new Box(Orientation.Vertical, 16);
        mainVBox.MarginStart = 20;
        mainVBox.MarginEnd = 20;
        mainVBox.MarginTop = 20;
        mainVBox.MarginBottom = 20;

        // Header
        var headerBox = new Box(Orientation.Vertical, 4);
        headerBox.Halign = Align.Center;

        _gameNameLabel = new Label("No game running");
        _gameNameLabel.Name = "game-name";
        
        _gameStatusLabel = new Label("sleeping - start a game to wake");
        _gameStatusLabel.Name = "game-status";

        headerBox.PackStart(_gameNameLabel, false, false, 0);
        headerBox.PackStart(_gameStatusLabel, false, false, 0);

        // Points
        var pointsBox = new Box(Orientation.Vertical, 4);
        pointsBox.Halign = Align.Center;

        _pointsLabel = new Label("--");
        _pointsLabel.Name = "points-value";
        
        var pointsSubLabel = new Label("POINTS EARNED");
        pointsSubLabel.Name = "points-label";

        pointsBox.PackStart(_pointsLabel, false, false, 0);
        pointsBox.PackStart(pointsSubLabel, false, false, 0);

        // Stats
        _statsBox = new Box(Orientation.Vertical, 12);
        _statsBox.Halign = Align.Fill;
        BuildStatsGrid();

        // Assemble
        mainVBox.PackStart(headerBox, false, false, 12);
        mainVBox.PackStart(pointsBox, false, false, 12);
        mainVBox.PackStart(_statsBox, true, true, 0);

        _contentBox.Add(mainVBox);
        overlay.Add(_contentBox);

        // Drag overlay (shown when drag mode is active)
        _dragOverlay = new EventBox();
        _dragOverlay.Name = "drag-overlay";
        _dragOverlay.NoShowAll = true;
        _dragOverlay.Visible = false;
        
        var dragLabel = new Label("Drag to Move\nPress shortcut to exit");
        dragLabel.Name = "drag-label";
        dragLabel.Justify = Justification.Center;
        dragLabel.Valign = Align.Center;
        dragLabel.Halign = Align.Center;
        _dragOverlay.Add(dragLabel);
        
        overlay.AddOverlay(_dragOverlay);

        Add(overlay);
        ApplyCSS();
    }

    private void BuildStatsGrid()
    {
        var row1 = new Box(Orientation.Horizontal, 16);
        row1.Homogeneous = true;
        row1.PackStart(CreateStatWidget("Total Points", "--"), true, true, 0);
        row1.PackStart(CreateStatWidget("Donations Made", "--"), true, true, 0);

        var row2 = new Box(Orientation.Horizontal, 16);
        row2.Homogeneous = true;
        row2.PackStart(CreateStatWidget("To Next Donation", "--"), true, true, 0);
        row2.PackStart(CreateStatWidget("Total Donations", "--"), true, true, 0);

        _statsBox!.PackStart(row1, false, false, 0);
        _statsBox.PackStart(row2, false, false, 0);
    }

    private Box CreateStatWidget(string label, string value)
    {
        var box = new Box(Orientation.Vertical, 2);
        box.Halign = Align.Center;

        var labelWidget = new Label(label) { Name = "stat-label" };
        var valueWidget = new Label(value) { Name = "stat-value" };

        box.PackStart(labelWidget, false, false, 0);
        box.PackStart(valueWidget, false, false, 0);

        return box;
    }

    private void ApplyCSS()
    {
        var css = @"
            #overlay-content {
                background-color: rgba(0, 0, 0, 0.85);
                border-radius: 12px;
            }
            #game-name {
                color: white;
                font-size: 20px;
                font-weight: bold;
            }
            #game-status {
                color: rgba(255, 255, 255, 0.6);
                font-size: 12px;
            }
            #points-value {
                color: white;
                font-size: 42px;
                font-weight: bold;
            }
            #points-label {
                color: rgba(255, 255, 255, 0.6);
                font-size: 14px;
                letter-spacing: 2px;
            }
            #stat-label {
                color: rgba(255, 255, 255, 0.6);
                font-size: 10px;
            }
            #stat-value {
                color: white;
                font-size: 20px;
                font-weight: bold;
            }
            #drag-overlay {
                background-color: rgba(234, 179, 8, 0.7);
                border-radius: 12px;
            }
            #drag-label {
                color: #000;
                font-size: 16px;
                font-weight: bold;
            }
        ";

        var provider = new CssProvider();
        provider.LoadFromData(css);
        StyleContext.AddProviderForScreen(Screen.Default, provider, StyleProviderPriority.Application);
    }

    private void RestorePosition()
    {
        var settings = _settingsService.Settings;
        if (settings.OverlayX.HasValue && settings.OverlayY.HasValue)
        {
            Move(settings.OverlayX.Value, settings.OverlayY.Value);
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
            _logger.Debug($"Positioned at {x}, {y}");
        }
    }

    private void SavePosition()
    {
        GetPosition(out int x, out int y);
        _settingsService.Update(s => { s.OverlayX = x; s.OverlayY = y; });
        _logger.Debug($"Position saved: {x}, {y}");
    }

    public void UpdateGameInfo(string? gameName, string? elapsedTime = null)
    {
        GLib.Idle.Add(() =>
        {
            if (gameName != null)
            {
                _gameNameLabel!.Text = gameName;
                _gameStatusLabel!.Text = elapsedTime != null
                    ? $"Playing · {elapsedTime}"
                    : "Playing now";
                _pointsLabel!.Text = "0";
            }
            else
            {
                _gameNameLabel!.Text = "No game running";
                _gameStatusLabel!.Text = "sleeping - start a game";
                _pointsLabel!.Text = "--";
            }
            return false;
        });
    }

    private void OnGameStarted(IpcMessage message) { }

    private void OnDataReceived(IpcMessage message)
    {
        if (message.Payload == null) return;

        try
        {
            var payload = (JsonElement)message.Payload;
            if (!payload.TryGetProperty("type", out var typeElement)) return;

            var type = typeElement.GetString();
            GLib.Idle.Add(() =>
            {
                if (type == "INTERVAL_COUNTER_UPDATE" && payload.TryGetProperty("data", out var data))
                {
                    _pointsLabel!.Text = data.GetInt32().ToString();
                }
                return false;
            });
        }
        catch (Exception ex)
        {
            _logger.Warning($"Error processing overlay data: {ex.Message}");
        }
    }

    public void Toggle()
    {
        if (!_settingsService.Settings.EnableOverlay) return;

        if (IsVisible)
            HideOverlay();
        else
            ShowOverlay();
    }

    public void ShowOverlay()
    {
        if (!_settingsService.Settings.EnableOverlay) return;

        GLib.Idle.Add(() =>
        {
            ShowAll();
            _dragOverlay?.Hide();
            RestorePosition();
            
            // Ensure on top
            KeepAbove = true;
            Window?.Raise();
            SetupX11Properties();
            
            IsVisible = true;
            StartKeepOnTopTimer();
            
            _logger.Debug("Overlay shown");
            return false;
        });
    }

    public void HideOverlay()
    {
        if (_isDragEnabled) ToggleDragMode();
        
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
        
        // Periodically ensure window stays on top (every 2 seconds)
        _keepOnTopTimerId = GLib.Timeout.Add(2000, () =>
        {
            if (!IsVisible) return false;
            
            KeepAbove = true;
            Window?.Raise();
            
            return true;
        });
    }

    private void StopKeepOnTopTimer()
    {
        if (_keepOnTopTimerId != 0)
        {
            GLib.Source.Remove(_keepOnTopTimerId);
            _keepOnTopTimerId = 0;
        }
    }

    public void ToggleDragMode()
    {
        _isDragEnabled = !_isDragEnabled;
        
        GLib.Idle.Add(() =>
        {
            if (_dragOverlay != null)
            {
                _dragOverlay.Visible = _isDragEnabled;
                if (_isDragEnabled) _dragOverlay.ShowAll();
            }
            
            AcceptFocus = _isDragEnabled;
            _logger.Debug($"Drag mode: {_isDragEnabled}");
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
        }
    }

    private void OnButtonRelease(object? sender, ButtonReleaseEventArgs args)
    {
        if (_isDragging)
        {
            _isDragging = false;
            SavePosition();
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
    private static extern int XFlush(IntPtr display);

    #endregion
}
