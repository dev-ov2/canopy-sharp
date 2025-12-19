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
/// Overlay window that sits on top of games
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

    public OverlayWindow() : base(WindowType.Toplevel)
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
        SkipTaskbarHint = true;
        SkipPagerHint = true;
        KeepAbove = true;
        AcceptFocus = false; // Don't steal focus from games
        TypeHint = WindowTypeHint.Dock; // Dock type windows stay on top
        
        // Make window semi-transparent
        AppPaintable = true;
        var visual = Screen.RgbaVisual;
        if (visual != null)
        {
            Visual = visual;
        }

        // Handle events
        ButtonPressEvent += OnButtonPress;
        ButtonReleaseEvent += OnButtonRelease;
        MotionNotifyEvent += OnMotionNotify;
        AddEvents((int)(EventMask.ButtonPressMask | EventMask.ButtonReleaseMask | EventMask.PointerMotionMask));
    }

    private void OnRealized(object? sender, EventArgs args)
    {
        // Additional X11 setup to ensure window stays on top
        RestorePosition();
        SetStayOnTop();
    }

    private void SetStayOnTop()
    {
        try
        {
            // Use GDK to set additional window hints
            // GTK's KeepAbove property should be sufficient for most compositors
            // Re-assert it here after the window is realized
            KeepAbove = true;
        }
        catch (Exception ex)
        {
            _logger.Warning($"Failed to set stay-on-top properties: {ex.Message}");
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
        // Create 2x2 grid of stats
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

            Gtk.Application.Invoke((_, _) =>
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
        // Update stat labels based on data
    }

    public void UpdateGameInfo(string? gameName, string? elapsedTime = null)
    {
        Gtk.Application.Invoke((_, _) =>
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
        });
    }

    public void ToggleDragMode()
    {
        _isDragEnabled = !_isDragEnabled;
        
        Gtk.Application.Invoke((_, _) =>
        {
            if (_dragHandle != null)
            {
                _dragHandle.Visible = _isDragEnabled;
            }
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

        Gtk.Application.Invoke((_, _) =>
        {
            Show();
            Present();
            KeepAbove = true; // Re-assert on show
            
            IsVisible = true;
            RestorePosition();
        });
        
        _logger.Debug("Overlay shown");
    }

    public void HideOverlay()
    {
        if (_isDragEnabled)
        {
            ToggleDragMode();
        }

        Gtk.Application.Invoke((_, _) =>
        {
            Hide();
            IsVisible = false;
        });
        
        _logger.Debug("Overlay hidden");
    }
}
