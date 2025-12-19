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
/// Overlay window that sits on top of games.
/// Uses X11 EWMH hints to stay above other windows including fullscreen games.
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
    private uint _stayOnTopTimerId;

    // UI Elements
    private Label? _gameNameLabel;
    private Label? _gameStatusLabel;
    private Label? _pointsLabel;
    private Box? _statsBox;
    private EventBox? _dragHandle;
    private Fixed? _mainContainer;

    private const int DefaultWidth = 280;
    private const int DefaultHeight = 520;

    public bool IsVisible { get; private set; }
    public bool IsDragEnabled => _isDragEnabled;

    public OverlayWindow() : base(WindowType.Popup) // Use Popup for overlay behavior
    {
        _logger = CanopyLoggerFactory.CreateLogger<OverlayWindow>();
        _ipcBridge = App.Services.GetRequiredService<LinuxWebViewIpcBridge>();
        _settingsService = App.Services.GetRequiredService<ISettingsService>();

        _ipcBridge.Subscribe(IpcMessageTypes.GameStarted, OnGameStarted);
        _ipcBridge.Subscribe(IpcMessageTypes.DataReceived, OnDataReceived);

        SetupWindow();
        SetupUI();
        
        // Set up X11 properties after window is realized
        Realized += OnRealized;
        
        _logger.Info("OverlayWindow created");
    }

    private void SetupWindow()
    {
        Title = "Canopy Overlay";
        SetDefaultSize(DefaultWidth, DefaultHeight);
        Decorated = false;
        Resizable = false;
        SkipTaskbarHint = true;
        SkipPagerHint = true;
        
        // These are critical for overlay behavior
        AcceptFocus = false;
        KeepAbove = true;
        
        // Make window semi-transparent
        AppPaintable = true;
        var visual = Screen.RgbaVisual;
        if (visual != null)
        {
            Visual = visual;
        }

        // Handle events for dragging
        ButtonPressEvent += OnButtonPress;
        ButtonReleaseEvent += OnButtonRelease;
        MotionNotifyEvent += OnMotionNotify;
        AddEvents((int)(EventMask.ButtonPressMask | EventMask.ButtonReleaseMask | EventMask.PointerMotionMask));
    }

    private void OnRealized(object? sender, EventArgs args)
    {
        RestorePosition();
        
        // Apply X11 EWMH properties for overlay behavior
        ApplyX11OverlayHints();
    }

    /// <summary>
    /// Applies X11 EWMH hints to make the window behave as an overlay.
    /// Uses _NET_WM_STATE_ABOVE and _NET_WM_WINDOW_TYPE_DOCK.
    /// </summary>
    private void ApplyX11OverlayHints()
    {
        var gdkWindow = this.Window;
        if (gdkWindow == null)
        {
            _logger.Warning("GDK window is null, cannot apply X11 hints");
            return;
        }

        try
        {
            // These are set on the GTK window, not GDK window
            TypeHint = WindowTypeHint.Dock;
            KeepAbove = true;
            
            // Make it stick to all workspaces
            Stick();
            
            _logger.Debug("X11 overlay hints applied via GTK");
        }
        catch (Exception ex)
        {
            _logger.Warning($"Failed to apply hints via GTK: {ex.Message}");
        }

        // Also try direct X11 calls for better compatibility
        try
        {
            SetX11AlwaysOnTop();
        }
        catch (Exception ex)
        {
            _logger.Warning($"Failed to apply X11 hints directly: {ex.Message}");
        }
    }

    /// <summary>
    /// Uses direct X11 calls to set _NET_WM_STATE_ABOVE atom.
    /// This is more reliable than GDK for some window managers.
    /// </summary>
    private void SetX11AlwaysOnTop()
    {
        var gdkWindow = this.Window;
        if (gdkWindow == null) return;

        // Get X11 window ID
        var xid = GetX11WindowId(gdkWindow);
        if (xid == IntPtr.Zero)
        {
            _logger.Debug("Could not get X11 window ID");
            return;
        }

        // Get X11 display
        var display = gdk_x11_get_default_xdisplay();
        if (display == IntPtr.Zero)
        {
            _logger.Debug("Could not get X11 display");
            return;
        }

        // Get atoms for _NET_WM_STATE and _NET_WM_STATE_ABOVE
        var netWmState = XInternAtom(display, "_NET_WM_STATE", false);
        var netWmStateAbove = XInternAtom(display, "_NET_WM_STATE_ABOVE", false);
        var netWmStateSticky = XInternAtom(display, "_NET_WM_STATE_STICKY", false);

        if (netWmState == IntPtr.Zero || netWmStateAbove == IntPtr.Zero)
        {
            _logger.Debug("Could not get X11 atoms");
            return;
        }

        // Send client message to set _NET_WM_STATE_ABOVE
        var rootWindow = XDefaultRootWindow(display);
        
        // _NET_WM_STATE message: action (1=add), atom1, atom2
        SendX11ClientMessage(display, xid, rootWindow, netWmState, 
            1, // _NET_WM_STATE_ADD
            netWmStateAbove,
            netWmStateSticky);

        XFlush(display);
        
        _logger.Debug("X11 _NET_WM_STATE_ABOVE set via XSendEvent");
    }

    private void SendX11ClientMessage(IntPtr display, IntPtr window, IntPtr rootWindow, 
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
            format = 32
        };
        
        // Set data (action, atom1, atom2, 0, 0)
        ev.data_l0 = action;
        ev.data_l1 = atom1;
        ev.data_l2 = atom2;
        ev.data_l3 = IntPtr.Zero;
        ev.data_l4 = IntPtr.Zero;

        XSendEvent(display, rootWindow, false, 
            0x180000, // SubstructureRedirectMask | SubstructureNotifyMask
            ref ev);
    }

    private IntPtr GetX11WindowId(Gdk.Window gdkWindow)
    {
        try
        {
            return gdk_x11_window_get_xid(gdkWindow.Handle);
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    private void SetupUI()
    {
        _mainContainer = new Fixed();

        // Main overlay box with dark background
        var overlayBox = new Box(Orientation.Vertical, 16);
        overlayBox.MarginStart = 24;
        overlayBox.MarginEnd = 24;
        overlayBox.MarginTop = 24;
        overlayBox.MarginBottom = 24;
        overlayBox.SetSizeRequest(DefaultWidth - 8, DefaultHeight - 8);

        // Header: Game name and status
        var headerBox = new Box(Orientation.Vertical, 4);
        headerBox.Halign = Align.Center;

        _gameNameLabel = new Label("No game running");
        _gameNameLabel.StyleContext.AddClass("game-name");
        
        _gameStatusLabel = new Label("sleeping - start a game to wake");
        _gameStatusLabel.StyleContext.AddClass("game-status");

        headerBox.PackStart(_gameNameLabel, false, false, 0);
        headerBox.PackStart(_gameStatusLabel, false, false, 0);

        // Points counter
        var pointsBox = new Box(Orientation.Vertical, 4);
        pointsBox.Halign = Align.Center;

        _pointsLabel = new Label("--");
        _pointsLabel.StyleContext.AddClass("points-value");
        
        var pointsSubLabel = new Label("POINTS EARNED");
        pointsSubLabel.StyleContext.AddClass("points-label");

        pointsBox.PackStart(_pointsLabel, false, false, 0);
        pointsBox.PackStart(pointsSubLabel, false, false, 0);

        // Stats grid
        _statsBox = new Box(Orientation.Vertical, 16);
        _statsBox.Halign = Align.Fill;
        CreateStatsGrid();

        // Pack everything
        overlayBox.PackStart(headerBox, false, false, 16);
        overlayBox.PackStart(pointsBox, false, false, 16);
        overlayBox.PackStart(_statsBox, true, true, 0);

        // Wrap in an event box for styling
        var eventBox = new EventBox();
        eventBox.Add(overlayBox);
        eventBox.StyleContext.AddClass("overlay-bg");

        // Drag handle (hidden by default)
        _dragHandle = new EventBox();
        _dragHandle.SetSizeRequest(DefaultWidth - 8, DefaultHeight - 8);
        _dragHandle.StyleContext.AddClass("drag-handle");
        _dragHandle.Visible = false;
        
        var dragLabel = new Label("Drag to Move\nPress shortcut again to exit");
        dragLabel.StyleContext.AddClass("drag-label");
        dragLabel.Justify = Justification.Center;
        _dragHandle.Add(dragLabel);

        _mainContainer.Put(eventBox, 4, 4);
        _mainContainer.Put(_dragHandle, 4, 4);

        Add(_mainContainer);
        ApplyStyles();
        ShowAll();
        _dragHandle.Hide();
    }

    private void CreateStatsGrid()
    {
        var row1 = new Box(Orientation.Horizontal, 12);
        var row2 = new Box(Orientation.Horizontal, 12);

        row1.PackStart(CreateStatBox("Stat1", "Total Points", "--", ""), true, true, 0);
        row1.PackStart(CreateStatBox("Stat2", "Donations Made", "--", ""), true, true, 0);
        row2.PackStart(CreateStatBox("Stat3", "To New Donation", "--", ""), true, true, 0);
        row2.PackStart(CreateStatBox("Stat4", "Total Donations", "--", ""), true, true, 0);

        _statsBox!.PackStart(row1, true, true, 0);
        _statsBox.PackStart(row2, true, true, 0);
    }

    private Box CreateStatBox(string name, string label, string value, string helptext)
    {
        var box = new Box(Orientation.Vertical, 2);
        box.Halign = Align.End;
        box.Name = name;

        var labelWidget = new Label(label);
        labelWidget.StyleContext.AddClass("stat-label");
        labelWidget.Name = $"{name}Label";

        var valueWidget = new Label(value);
        valueWidget.StyleContext.AddClass("stat-value");
        valueWidget.Name = $"{name}Value";

        var helptextWidget = new Label(helptext);
        helptextWidget.StyleContext.AddClass("stat-helptext");
        helptextWidget.Name = $"{name}Helptext";

        box.PackStart(labelWidget, false, false, 0);
        box.PackStart(valueWidget, false, false, 0);
        box.PackStart(helptextWidget, false, false, 0);

        return box;
    }

    private void ApplyStyles()
    {
        var css = @"
            .overlay-bg {
                background-color: rgba(0, 0, 0, 0.78);
                border-radius: 8px;
            }
            .game-name {
                color: white;
                font-size: 22px;
                font-weight: 600;
            }
            .game-status {
                color: rgba(255, 255, 255, 0.5);
                font-size: 12px;
            }
            .points-value {
                color: white;
                font-size: 48px;
                font-weight: bold;
            }
            .points-label {
                color: rgba(255, 255, 255, 0.5);
                font-size: 16px;
            }
            .stat-label {
                color: rgba(255, 255, 255, 0.5);
                font-size: 11px;
            }
            .stat-value {
                color: white;
                font-size: 24px;
                font-weight: 600;
            }
            .stat-helptext {
                color: rgba(255, 255, 255, 0.5);
                font-size: 11px;
            }
            .drag-handle {
                background-color: rgba(234, 179, 8, 0.5);
                border-radius: 8px;
            }
            .drag-label {
                color: #EAB308;
                font-size: 18px;
                font-weight: 600;
            }
        ";

        var provider = new CssProvider();
        provider.LoadFromData(css);
        StyleContext.AddProviderForScreen(Gdk.Screen.Default, provider, StyleProviderPriority.Application);
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

    private void SavePosition()
    {
        GetPosition(out int x, out int y);
        _settingsService.Update(s =>
        {
            s.OverlayX = x;
            s.OverlayY = y;
        });
        _logger.Debug($"Saved overlay position: {x}, {y}");
    }

    public void PositionAtScreenEdge()
    {
        var screen = Gdk.Screen.Default;
        if (screen != null)
        {
            var x = screen.Width - DefaultWidth;
            var y = 0;
            Move(x, y);
        }
    }

    private void OnGameStarted(IpcMessage message)
    {
        _logger.Debug("OnGameStarted received");
    }

    private void OnDataReceived(IpcMessage message)
    {
        if (message.Payload == null) return;

        try
        {
            var payload = (JsonElement)message.Payload;
            
            if (!payload.TryGetProperty("type", out var typeElement))
                return;
                
            var type = typeElement.GetString();

            GLib.Idle.Add(() =>
            {
                switch (type)
                {
                    case "OVERLAY_STATISTICS":
                        if (payload.TryGetProperty("data", out var statsData))
                        {
                            UpdateStats(statsData);
                        }
                        break;

                    case "INTERVAL_COUNTER_UPDATE":
                        if (payload.TryGetProperty("data", out var counterData))
                        {
                            _pointsLabel!.Text = counterData.GetInt32().ToString();
                        }
                        break;
                }
                return false;
            });
        }
        catch (Exception ex)
        {
            _logger.Error("Error processing overlay data", ex);
        }
    }

    private void UpdateStats(JsonElement statsData)
    {
        var data = statsData.EnumerateArray().ToList();
    }

    public void UpdateGameInfo(string? gameName, string? elapsedTime = null)
    {
        GLib.Idle.Add(() =>
        {
            if (gameName != null)
            {
                _gameNameLabel!.Text = gameName;
                _gameStatusLabel!.Text = elapsedTime != null
                    ? $"Playing now · Time elapsed: {elapsedTime}"
                    : "Playing now";
                _pointsLabel!.Text = "0";
            }
            else
            {
                _gameNameLabel!.Text = "No game running";
                _gameStatusLabel!.Text = "sleeping - start a game to wake";
                _pointsLabel!.Text = "--";
            }
            return false;
        });
    }

    public void ToggleDragMode()
    {
        _isDragEnabled = !_isDragEnabled;
        
        GLib.Idle.Add(() =>
        {
            if (_dragHandle != null)
            {
                _dragHandle.Visible = _isDragEnabled;
            }
            
            // In drag mode, we need to accept focus for input
            AcceptFocus = _isDragEnabled;
            
            return false;
        });
        
        _logger.Debug($"Drag mode: {_isDragEnabled}");
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
            var deltaX = (int)args.Event.XRoot - _dragStartX;
            var deltaY = (int)args.Event.YRoot - _dragStartY;
            Move(_windowStartX + deltaX, _windowStartY + deltaY);
        }
    }

    public void Toggle()
    {
        if (!_settingsService.Settings.EnableOverlay)
            return;

        if (IsVisible)
        {
            HideOverlay();
        }
        else
        {
            ShowOverlay();
        }
    }

    public void ShowOverlay()
    {
        if (!_settingsService.Settings.EnableOverlay)
            return;

        GLib.Idle.Add(() =>
        {
            Show();
            RestorePosition();
            
            // Re-apply overlay hints
            ApplyX11OverlayHints();
            
            // Raise window
            var gdkWindow = this.Window;
            gdkWindow?.Raise();
            
            IsVisible = true;
            
            // Start timer to periodically ensure we're on top
            StartStayOnTopTimer();
            
            return false;
        });
        
        _logger.Debug("Overlay shown");
    }

    private void StartStayOnTopTimer()
    {
        StopStayOnTopTimer();
        
        // Periodically ensure the window stays on top
        _stayOnTopTimerId = GLib.Timeout.Add(1000, () =>
        {
            if (!IsVisible) return false;
            
            // Re-raise and re-apply hints
            var gdkWindow = this.Window;
            if (gdkWindow != null)
            {
                gdkWindow.Raise();
                KeepAbove = true;
            }
            
            return true; // Continue timer
        });
    }

    private void StopStayOnTopTimer()
    {
        if (_stayOnTopTimerId != 0)
        {
            GLib.Source.Remove(_stayOnTopTimerId);
            _stayOnTopTimerId = 0;
        }
    }

    public void HideOverlay()
    {
        if (_isDragEnabled)
        {
            ToggleDragMode();
        }

        StopStayOnTopTimer();

        GLib.Idle.Add(() =>
        {
            Hide();
            IsVisible = false;
            return false;
        });
        
        _logger.Debug("Overlay hidden");
    }

    #region X11 P/Invoke

    [StructLayout(LayoutKind.Sequential)]
    private struct XClientMessageEvent
    {
        public int type;
        public IntPtr serial;
        [MarshalAs(UnmanagedType.Bool)]
        public bool send_event;
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
    private static extern int XSendEvent(IntPtr display, IntPtr window, bool propagate, 
        int eventMask, ref XClientMessageEvent eventSend);

    [DllImport("libX11.so.6")]
    private static extern int XFlush(IntPtr display);

    #endregion
}
