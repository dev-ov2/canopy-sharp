using Canopy.Core.Application;
using Canopy.Core.Logging;
using Canopy.Linux.Services;
using Gdk;
using Gtk;
using Microsoft.Extensions.DependencyInjection;
using System.Reflection;
using Window = Gtk.Window;
using WindowType = Gtk.WindowType;

namespace Canopy.Linux.Windows;

/// <summary>
/// Settings window for Canopy application on Linux
/// </summary>
public class SettingsWindow : Window
{
    private readonly ICanopyLogger _logger;
    private readonly ISettingsService _settingsService;
    private readonly LinuxPlatformServices _platformServices;

    // UI Elements
    private Switch? _startWithSystemToggle;
    private Switch? _startOpenToggle;
    private Switch? _autoUpdateToggle;
    private Switch? _enableOverlayToggle;
    private Entry? _overlayShortcutEntry;
    private Entry? _dragShortcutEntry;
    private Box? _overlayShortcutBox;
    private Box? _dragShortcutBox;
    private Button? _resetPositionButton;
    private Label? _versionLabel;

    private bool _settingsLoaded;
    private bool _isCapturingShortcut;
    private Entry? _activeShortcutEntry;

    public SettingsWindow() : base(WindowType.Toplevel)
    {
        _logger = CanopyLoggerFactory.CreateLogger<SettingsWindow>();
        _settingsService = App.Services.GetRequiredService<ISettingsService>();
        _platformServices = App.Services.GetRequiredService<LinuxPlatformServices>();

        SetupWindow();
        SetupUI();
        
        Shown += OnShown;
        DeleteEvent += OnDeleteEvent;
        
        _logger.Info("SettingsWindow created");
    }

    private void SetupWindow()
    {
        Title = "Canopy Settings";
        SetDefaultSize(500, 600);
        SetPosition(WindowPosition.Center);
        Resizable = false;

        // Set window icon
        var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "canopy.png");
        if (System.IO.File.Exists(iconPath))
        {
            try
            {
                Icon = new Pixbuf(iconPath);
            }
            catch { }
        }
    }

    private void SetupUI()
    {
        var mainBox = new Box(Orientation.Vertical, 16);
        mainBox.MarginStart = 24;
        mainBox.MarginEnd = 24;
        mainBox.MarginTop = 24;
        mainBox.MarginBottom = 24;

        // Header
        var titleLabel = new Label("Settings");
        titleLabel.StyleContext.AddClass("title");
        titleLabel.Halign = Align.Start;
        mainBox.PackStart(titleLabel, false, false, 0);

        // General section
        mainBox.PackStart(CreateSectionLabel("General"), false, false, 8);

        // Start with system
        var (startWithBox, startWithToggle) = CreateToggleSetting(
            "Start with System",
            "Launch Canopy when you log in");
        _startWithSystemToggle = startWithToggle;
        mainBox.PackStart(startWithBox, false, false, 0);

        // Start open
        var (startOpenBox, startOpenToggle) = CreateToggleSetting(
            "Open on Startup",
            "Show Canopy when your computer starts up");
        _startOpenToggle = startOpenToggle;
        mainBox.PackStart(startOpenBox, false, false, 0);

        // Auto-update
        var (autoUpdateBox, autoUpdateToggle) = CreateToggleSetting(
            "Auto-update",
            "Automatically download and install updates");
        _autoUpdateToggle = autoUpdateToggle;
        mainBox.PackStart(autoUpdateBox, false, false, 0);

        // Separator
        mainBox.PackStart(new Separator(Orientation.Horizontal), false, false, 8);

        // Overlay section
        mainBox.PackStart(CreateSectionLabel("Overlay"), false, false, 8);

        // Enable overlay
        var (enableOverlayBox, enableOverlayToggle) = CreateToggleSetting(
            "Enable overlay",
            "Show the floating overlay window");
        _enableOverlayToggle = enableOverlayToggle;
        mainBox.PackStart(enableOverlayBox, false, false, 0);

        // Toggle shortcut
        var (overlayShortcutBox, overlayShortcutEntry) = CreateEntrySetting(
            "Toggle shortcut",
            "Keyboard shortcut to show/hide overlay");
        _overlayShortcutBox = overlayShortcutBox;
        _overlayShortcutEntry = overlayShortcutEntry;
        _overlayShortcutEntry.IsEditable = false;
        _overlayShortcutEntry.FocusInEvent += OnShortcutFocusIn;
        _overlayShortcutEntry.FocusOutEvent += OnShortcutFocusOut;
        _overlayShortcutEntry.KeyPressEvent += OnOverlayShortcutKeyPress;
        mainBox.PackStart(overlayShortcutBox, false, false, 0);

        // Drag shortcut
        var (dragShortcutBox, dragShortcutEntry) = CreateEntrySetting(
            "Drag shortcut",
            "Hold to drag the overlay");
        _dragShortcutBox = dragShortcutBox;
        _dragShortcutEntry = dragShortcutEntry;
        _dragShortcutEntry.IsEditable = false;
        _dragShortcutEntry.FocusInEvent += OnShortcutFocusIn;
        _dragShortcutEntry.FocusOutEvent += OnShortcutFocusOut;
        _dragShortcutEntry.KeyPressEvent += OnDragShortcutKeyPress;
        mainBox.PackStart(dragShortcutBox, false, false, 0);

        // Reset position button
        _resetPositionButton = new Button("Reset Overlay Position");
        _resetPositionButton.Halign = Align.Start;
        _resetPositionButton.Clicked += OnResetPositionClicked;
        mainBox.PackStart(_resetPositionButton, false, false, 8);

        // Separator
        mainBox.PackStart(new Separator(Orientation.Horizontal), false, false, 8);

        // Footer
        var footerBox = new Box(Orientation.Horizontal, 8);
        
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        _versionLabel = new Label($"Canopy v{version?.Major}.{version?.Minor}.{version?.Build}");
        _versionLabel.StyleContext.AddClass("version");
        _versionLabel.Halign = Align.Start;
        
        var closeButton = new Button("Close");
        closeButton.Clicked += (_, _) => Hide();

        footerBox.PackStart(_versionLabel, true, true, 0);
        footerBox.PackEnd(closeButton, false, false, 0);
        
        mainBox.PackEnd(footerBox, false, false, 0);

        Add(mainBox);
        ApplyStyles();
        ShowAll();
        
        // Wire up toggle events after UI is built
        WireUpToggleEvents();
    }

    private void WireUpToggleEvents()
    {
        // Use notify::active signal which is available on all GtkSwitch versions
        _startWithSystemToggle!.AddNotification("active", (o, args) => OnStartWithSystemChanged());
        _startOpenToggle!.AddNotification("active", (o, args) => OnStartOpenChanged());
        _autoUpdateToggle!.AddNotification("active", (o, args) => OnAutoUpdateChanged());
        _enableOverlayToggle!.AddNotification("active", (o, args) => OnEnableOverlayChanged());
    }

    private Label CreateSectionLabel(string text)
    {
        var label = new Label(text);
        label.StyleContext.AddClass("section-title");
        label.Halign = Align.Start;
        return label;
    }

    private (Box, Switch) CreateToggleSetting(string title, string description)
    {
        var box = new Box(Orientation.Horizontal, 8);
        
        var labelBox = new Box(Orientation.Vertical, 2);
        labelBox.Halign = Align.Start;
        
        var titleLabel = new Label(title);
        titleLabel.Halign = Align.Start;
        
        var descLabel = new Label(description);
        descLabel.StyleContext.AddClass("description");
        descLabel.Halign = Align.Start;
        
        labelBox.PackStart(titleLabel, false, false, 0);
        labelBox.PackStart(descLabel, false, false, 0);
        
        var toggle = new Switch();
        toggle.Valign = Align.Center;
        
        box.PackStart(labelBox, true, true, 0);
        box.PackEnd(toggle, false, false, 0);
        
        return (box, toggle);
    }

    private (Box, Entry) CreateEntrySetting(string title, string description)
    {
        var box = new Box(Orientation.Horizontal, 8);
        
        var labelBox = new Box(Orientation.Vertical, 2);
        labelBox.Halign = Align.Start;
        
        var titleLabel = new Label(title);
        titleLabel.Halign = Align.Start;
        
        var descLabel = new Label(description);
        descLabel.StyleContext.AddClass("description");
        descLabel.Halign = Align.Start;
        
        labelBox.PackStart(titleLabel, false, false, 0);
        labelBox.PackStart(descLabel, false, false, 0);
        
        var entry = new Entry();
        entry.WidthChars = 15;
        entry.Valign = Align.Center;
        
        box.PackStart(labelBox, true, true, 0);
        box.PackEnd(entry, false, false, 0);
        
        return (box, entry);
    }

    private void ApplyStyles()
    {
        var css = @"
            .title {
                font-size: 24px;
                font-weight: bold;
            }
            .section-title {
                font-size: 18px;
                font-weight: 600;
            }
            .description {
                font-size: 12px;
                color: gray;
            }
            .version {
                font-size: 12px;
                color: gray;
            }
        ";

        var provider = new CssProvider();
        provider.LoadFromData(css);
        StyleContext.AddProviderForScreen(Screen.Default, provider, StyleProviderPriority.Application);
    }

    private void OnShown(object? sender, EventArgs e)
    {
        if (!_settingsLoaded)
        {
            _settingsLoaded = true;
            LoadSettings();
        }
    }

    private async void LoadSettings()
    {
        var settings = _settingsService.Settings;
        var isAutoStartEnabled = await _platformServices.IsAutoStartEnabledAsync();
        
        _startWithSystemToggle!.Active = isAutoStartEnabled;
        _startOpenToggle!.Active = settings.StartOpen;
        _autoUpdateToggle!.Active = settings.AutoUpdate;
        _enableOverlayToggle!.Active = settings.EnableOverlay;
        _overlayShortcutEntry!.Text = settings.OverlayToggleShortcut;
        _dragShortcutEntry!.Text = settings.OverlayDragShortcut;
        
        UpdateOverlayControlsState();
    }

    private void UpdateOverlayControlsState()
    {
        var isEnabled = _enableOverlayToggle!.Active;
        _overlayShortcutBox!.Sensitive = isEnabled;
        _dragShortcutBox!.Sensitive = isEnabled;
        _resetPositionButton!.Sensitive = isEnabled;
    }

    private void OnStartWithSystemChanged()
    {
        if (!_settingsLoaded) return;
        
        var isActive = _startWithSystemToggle!.Active;
        _ = _platformServices.SetAutoStartAsync(isActive, _startOpenToggle!.Active);
        _settingsService.Update(s => s.StartWithWindows = isActive);
        _startOpenToggle.Sensitive = isActive;
        
        _logger.Info($"Start with system: {isActive}");
    }

    private void OnStartOpenChanged()
    {
        if (!_settingsLoaded) return;
        _settingsService.Update(s => s.StartOpen = _startOpenToggle!.Active);
    }

    private void OnAutoUpdateChanged()
    {
        if (!_settingsLoaded) return;
        _settingsService.Update(s => s.AutoUpdate = _autoUpdateToggle!.Active);
    }

    private void OnEnableOverlayChanged()
    {
        if (!_settingsLoaded) return;
        
        var isActive = _enableOverlayToggle!.Active;
        _settingsService.Update(s => s.EnableOverlay = isActive);
        UpdateOverlayControlsState();

        if (!isActive)
        {
            try 
            { 
                App.Services.GetRequiredService<OverlayWindow>().HideOverlay(); 
            } 
            catch { }
        }
    }

    private void OnShortcutFocusIn(object? sender, FocusInEventArgs args)
    {
        _isCapturingShortcut = true;
        _activeShortcutEntry = sender as Entry;
        if (_activeShortcutEntry != null)
        {
            _activeShortcutEntry.Text = "Press keys...";
        }
    }

    private void OnShortcutFocusOut(object? sender, FocusOutEventArgs args)
    {
        _isCapturingShortcut = false;
        var entry = sender as Entry;
        if (entry?.Text == "Press keys...")
        {
            var settings = _settingsService.Settings;
            entry.Text = entry == _overlayShortcutEntry 
                ? settings.OverlayToggleShortcut 
                : settings.OverlayDragShortcut;
        }
        _activeShortcutEntry = null;
    }

    [GLib.ConnectBefore]
    private void OnOverlayShortcutKeyPress(object? sender, KeyPressEventArgs args)
    {
        if (CaptureShortcut(args, out var shortcut))
        {
            _settingsService.Update(s => s.OverlayToggleShortcut = shortcut);
        }
    }

    [GLib.ConnectBefore]
    private void OnDragShortcutKeyPress(object? sender, KeyPressEventArgs args)
    {
        if (CaptureShortcut(args, out var shortcut))
        {
            _settingsService.Update(s => s.OverlayDragShortcut = shortcut);
        }
    }

    private bool CaptureShortcut(KeyPressEventArgs args, out string shortcut)
    {
        shortcut = "";
        if (!_isCapturingShortcut || _activeShortcutEntry == null) return false;

        args.RetVal = true;
        var sb = new System.Text.StringBuilder();

        var state = args.Event.State;
        if (state.HasFlag(ModifierType.ControlMask))
            sb.Append("Ctrl+");
        if (state.HasFlag(ModifierType.Mod1Mask)) // Alt
            sb.Append("Alt+");
        if (state.HasFlag(ModifierType.ShiftMask))
            sb.Append("Shift+");

        var key = args.Event.Key;
        if (sb.Length > 0 && !IsModifierKey(key))
        {
            sb.Append(KeyToString(key));
            shortcut = sb.ToString();
            _activeShortcutEntry.Text = shortcut;
            _isCapturingShortcut = false;
            _logger.Debug($"Captured shortcut: {shortcut}");
            return true;
        }

        return false;
    }

    private static bool IsModifierKey(Gdk.Key key)
    {
        return key is Gdk.Key.Control_L or Gdk.Key.Control_R or
                      Gdk.Key.Alt_L or Gdk.Key.Alt_R or
                      Gdk.Key.Shift_L or Gdk.Key.Shift_R or
                      Gdk.Key.Super_L or Gdk.Key.Super_R or
                      Gdk.Key.Meta_L or Gdk.Key.Meta_R;
    }

    private static string KeyToString(Gdk.Key key)
    {
        // Convert GTK key to readable string
        var keyName = key.ToString();
        
        // Handle common cases
        if (keyName.Length == 1 && char.IsLetterOrDigit(keyName[0]))
            return keyName.ToUpper();
            
        return keyName switch
        {
            "space" => "Space",
            "Return" => "Enter",
            "Escape" => "Escape",
            _ when keyName.StartsWith("F") => keyName, // F1-F12
            _ => keyName.Replace("_", "")
        };
    }

    private void OnResetPositionClicked(object? sender, EventArgs e)
    {
        _settingsService.Update(s => { s.OverlayX = null; s.OverlayY = null; });
        
        try 
        { 
            App.Services.GetRequiredService<OverlayWindow>().PositionAtScreenEdge(); 
        } 
        catch { }
        
        _logger.Info("Reset overlay position");
    }

    private void OnDeleteEvent(object sender, DeleteEventArgs args)
    {
        args.RetVal = true;
        Hide();
    }
}
