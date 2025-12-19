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
/// Settings window for Canopy application on Linux.
/// </summary>
public class SettingsWindow : Window
{
    private readonly ICanopyLogger _logger;
    private readonly ISettingsService _settingsService;
    private readonly LinuxPlatformServices _platformServices;

    // UI Elements
    private CheckButton? _startWithSystemCheck;
    private CheckButton? _startOpenCheck;
    private CheckButton? _autoUpdateCheck;
    private CheckButton? _enableOverlayCheck;
    private Entry? _overlayShortcutEntry;
    private Entry? _dragShortcutEntry;
    private Box? _overlaySettingsBox;
    private Button? _resetPositionButton;
    private Button? _checkUpdatesButton;

    private bool _isLoading = true; // Start as loading to prevent events during construction
    private bool _isCapturingShortcut;
    private Entry? _activeShortcutEntry;

    public SettingsWindow() : base(WindowType.Toplevel)
    {
        _logger = CanopyLoggerFactory.CreateLogger<SettingsWindow>();
        _settingsService = App.Services.GetRequiredService<ISettingsService>();
        _platformServices = App.Services.GetRequiredService<LinuxPlatformServices>();

        SetupWindow();
        BuildUI();
        
        DeleteEvent += OnDeleteEvent;
        
        _logger.Info("SettingsWindow created");
    }

    private void SetupWindow()
    {
        Title = "Canopy Settings";
        SetDefaultSize(480, 520);
        SetPosition(WindowPosition.Center);
        Resizable = false;
        BorderWidth = 0;

        var pixbuf = AppIconManager.GetPixbuf(64);
        if (pixbuf != null)
        {
            Icon = pixbuf;
        }
    }

    private void BuildUI()
    {
        var mainBox = new Box(Orientation.Vertical, 0);
        
        // Header
        var headerBox = new Box(Orientation.Horizontal, 0);
        headerBox.MarginStart = 24;
        headerBox.MarginEnd = 24;
        headerBox.MarginTop = 20;
        headerBox.MarginBottom = 16;
        
        var titleLabel = new Label();
        titleLabel.Markup = "<span size='x-large' weight='bold'>Settings</span>";
        titleLabel.Halign = Align.Start;
        headerBox.PackStart(titleLabel, false, false, 0);
        mainBox.PackStart(headerBox, false, false, 0);

        // Scrollable content
        var scrolled = new ScrolledWindow();
        scrolled.SetPolicy(PolicyType.Never, PolicyType.Automatic);
        
        var contentBox = new Box(Orientation.Vertical, 12);
        contentBox.MarginStart = 24;
        contentBox.MarginEnd = 24;
        contentBox.MarginBottom = 16;

        // === General Section ===
        contentBox.PackStart(CreateSectionHeader("General"), false, false, 0);
        
        _startWithSystemCheck = new CheckButton("Start with System");
        _startWithSystemCheck.TooltipText = "Launch Canopy automatically when you log in";
        contentBox.PackStart(_startWithSystemCheck, false, false, 0);
        
        _startOpenCheck = new CheckButton("Show Window on Startup");
        _startOpenCheck.TooltipText = "Open the main window when Canopy starts";
        _startOpenCheck.MarginStart = 20;
        contentBox.PackStart(_startOpenCheck, false, false, 0);

        // === Updates Section ===
        contentBox.PackStart(CreateSectionHeader("Updates"), false, false, 8);
        
        _autoUpdateCheck = new CheckButton("Check for Updates Automatically");
        _autoUpdateCheck.TooltipText = "Periodically check GitHub for new versions";
        contentBox.PackStart(_autoUpdateCheck, false, false, 0);
        
        _checkUpdatesButton = new Button("Check for Updates Now");
        _checkUpdatesButton.Halign = Align.Start;
        _checkUpdatesButton.MarginStart = 4;
        contentBox.PackStart(_checkUpdatesButton, false, false, 4);

        // === Overlay Section ===
        contentBox.PackStart(CreateSectionHeader("Overlay"), false, false, 8);
        
        _enableOverlayCheck = new CheckButton("Enable Game Overlay");
        _enableOverlayCheck.TooltipText = "Show the floating overlay window during games";
        contentBox.PackStart(_enableOverlayCheck, false, false, 0);
        
        // Overlay settings (indented)
        _overlaySettingsBox = new Box(Orientation.Vertical, 8);
        _overlaySettingsBox.MarginStart = 24;
        _overlaySettingsBox.MarginTop = 8;
        
        // Toggle shortcut
        var toggleShortcutBox = new Box(Orientation.Horizontal, 12);
        toggleShortcutBox.PackStart(new Label("Toggle Shortcut:") { WidthChars = 14 }, false, false, 0);
        _overlayShortcutEntry = new Entry { WidthChars = 16, IsEditable = false };
        toggleShortcutBox.PackStart(_overlayShortcutEntry, false, false, 0);
        _overlaySettingsBox.PackStart(toggleShortcutBox, false, false, 0);
        
        // Drag shortcut
        var dragShortcutBox = new Box(Orientation.Horizontal, 12);
        dragShortcutBox.PackStart(new Label("Drag Shortcut:") { WidthChars = 14 }, false, false, 0);
        _dragShortcutEntry = new Entry { WidthChars = 16, IsEditable = false };
        dragShortcutBox.PackStart(_dragShortcutEntry, false, false, 0);
        _overlaySettingsBox.PackStart(dragShortcutBox, false, false, 0);
        
        // Reset position button
        _resetPositionButton = new Button("Reset Overlay Position") { Halign = Align.Start };
        _overlaySettingsBox.PackStart(_resetPositionButton, false, false, 4);
        
        contentBox.PackStart(_overlaySettingsBox, false, false, 0);

        scrolled.Add(contentBox);
        mainBox.PackStart(scrolled, true, true, 0);

        // Footer
        mainBox.PackStart(new Separator(Orientation.Horizontal), false, false, 0);
        
        var footerBox = new Box(Orientation.Horizontal, 8);
        footerBox.MarginStart = 24;
        footerBox.MarginEnd = 24;
        footerBox.MarginTop = 12;
        footerBox.MarginBottom = 16;
        
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        var versionLabel = new Label($"Canopy v{version?.Major}.{version?.Minor}.{version?.Build}");
        versionLabel.Halign = Align.Start;
        versionLabel.StyleContext.AddClass("dim-label");
        
        var closeButton = new Button("Close");
        
        footerBox.PackStart(versionLabel, true, true, 0);
        footerBox.PackEnd(closeButton, false, false, 0);
        
        mainBox.PackStart(footerBox, false, false, 0);

        Add(mainBox);

        // Wire up events AFTER building UI
        WireUpEvents(closeButton);
    }

    private void WireUpEvents(Button closeButton)
    {
        _startWithSystemCheck!.Toggled += (s, e) => {
            if (_isLoading) return;
            var active = _startWithSystemCheck.Active;
            _logger.Info($"StartWithSystem toggled: {active}");
            _settingsService.Update(settings => settings.StartWithWindows = active);
            _ = _platformServices.SetAutoStartAsync(active, _startOpenCheck!.Active);
            _startOpenCheck.Sensitive = active;
        };
        
        _startOpenCheck!.Toggled += (s, e) => {
            if (_isLoading) return;
            var active = _startOpenCheck.Active;
            _logger.Info($"StartOpen toggled: {active}");
            _settingsService.Update(settings => settings.StartOpen = active);
            if (_startWithSystemCheck.Active)
            {
                _ = _platformServices.SetAutoStartAsync(true, active);
            }
        };
        
        _autoUpdateCheck!.Toggled += (s, e) => {
            if (_isLoading) return;
            var active = _autoUpdateCheck.Active;
            _logger.Info($"AutoUpdate toggled: {active}");
            _settingsService.Update(settings => settings.AutoUpdate = active);
        };
        
        _enableOverlayCheck!.Toggled += (s, e) => {
            if (_isLoading) return;
            var active = _enableOverlayCheck.Active;
            _logger.Info($"EnableOverlay toggled: {active}");
            _settingsService.Update(settings => settings.EnableOverlay = active);
            _overlaySettingsBox!.Sensitive = active;
            if (!active)
            {
                try { App.Services.GetRequiredService<OverlayWindow>().HideOverlay(); } catch { }
            }
        };

        _overlayShortcutEntry!.FocusInEvent += OnShortcutFocusIn;
        _overlayShortcutEntry.FocusOutEvent += OnShortcutFocusOut;
        _overlayShortcutEntry.KeyPressEvent += OnOverlayShortcutKeyPress;
        
        _dragShortcutEntry!.FocusInEvent += OnShortcutFocusIn;
        _dragShortcutEntry.FocusOutEvent += OnShortcutFocusOut;
        _dragShortcutEntry.KeyPressEvent += OnDragShortcutKeyPress;

        _resetPositionButton!.Clicked += (s, e) => {
            _logger.Info("Reset overlay position");
            _settingsService.Update(settings => { settings.OverlayX = null; settings.OverlayY = null; });
            try { App.Services.GetRequiredService<OverlayWindow>().PositionAtScreenEdge(); } catch { }
        };

        _checkUpdatesButton!.Clicked += OnCheckUpdatesClicked;
        closeButton.Clicked += (s, e) => Hide();
    }

    private Label CreateSectionHeader(string text)
    {
        var label = new Label();
        label.Markup = $"<span weight='bold'>{text}</span>";
        label.Halign = Align.Start;
        label.MarginTop = 8;
        return label;
    }

    /// <summary>
    /// Call this before showing the window to load current settings.
    /// </summary>
    public void LoadAndShow()
    {
        _isLoading = true;
        
        // Load settings on the GTK thread
        GLib.Idle.Add(() => {
            LoadSettingsSync();
            ShowAll();
            Present();
            _isLoading = false;
            return false;
        });
    }

    private void LoadSettingsSync()
    {
        try
        {
            var settings = _settingsService.Settings;
            
            // Check autostart status
            var autostartPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config", "autostart", "canopy.desktop");
            var isAutoStartEnabled = System.IO.File.Exists(autostartPath);
            
            _logger.Debug($"Loading settings: AutoStart={isAutoStartEnabled}, StartOpen={settings.StartOpen}, AutoUpdate={settings.AutoUpdate}, EnableOverlay={settings.EnableOverlay}");
            
            // Set UI state
            _startWithSystemCheck!.Active = isAutoStartEnabled;
            _startOpenCheck!.Active = settings.StartOpen;
            _startOpenCheck.Sensitive = isAutoStartEnabled;
            _autoUpdateCheck!.Active = settings.AutoUpdate;
            _enableOverlayCheck!.Active = settings.EnableOverlay;
            _overlayShortcutEntry!.Text = settings.OverlayToggleShortcut ?? "Ctrl+Alt+O";
            _dragShortcutEntry!.Text = settings.OverlayDragShortcut ?? "Ctrl+Alt+D";
            _overlaySettingsBox!.Sensitive = settings.EnableOverlay;
            
            _logger.Debug("Settings loaded into UI");
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to load settings: {ex.Message}");
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
            _logger.Info($"Overlay shortcut changed: {shortcut}");
            _settingsService.Update(s => s.OverlayToggleShortcut = shortcut);
        }
    }

    [GLib.ConnectBefore]
    private void OnDragShortcutKeyPress(object? sender, KeyPressEventArgs args)
    {
        if (CaptureShortcut(args, out var shortcut))
        {
            _logger.Info($"Drag shortcut changed: {shortcut}");
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
        if (state.HasFlag(ModifierType.ControlMask)) sb.Append("Ctrl+");
        if (state.HasFlag(ModifierType.Mod1Mask)) sb.Append("Alt+");
        if (state.HasFlag(ModifierType.ShiftMask)) sb.Append("Shift+");

        var key = args.Event.Key;
        if (sb.Length > 0 && !IsModifierKey(key))
        {
            sb.Append(KeyToString(key));
            shortcut = sb.ToString();
            _activeShortcutEntry.Text = shortcut;
            _isCapturingShortcut = false;
            return true;
        }

        return false;
    }

    private static bool IsModifierKey(Gdk.Key key) => key is 
        Gdk.Key.Control_L or Gdk.Key.Control_R or
        Gdk.Key.Alt_L or Gdk.Key.Alt_R or
        Gdk.Key.Shift_L or Gdk.Key.Shift_R or
        Gdk.Key.Super_L or Gdk.Key.Super_R or
        Gdk.Key.Meta_L or Gdk.Key.Meta_R;

    private static string KeyToString(Gdk.Key key)
    {
        var keyName = key.ToString();
        if (keyName.Length == 1 && char.IsLetterOrDigit(keyName[0]))
            return keyName.ToUpper();
        return keyName switch
        {
            "space" => "Space",
            "Return" => "Enter",
            _ when keyName.StartsWith("F") => keyName,
            _ => keyName.Replace("_", "")
        };
    }

    private async void OnCheckUpdatesClicked(object? sender, EventArgs e)
    {
        _checkUpdatesButton!.Sensitive = false;
        _checkUpdatesButton.Label = "Checking...";
        
        try
        {
            var updateService = App.Services.GetRequiredService<LinuxUpdateService>();
            var updateInfo = await updateService.CheckForUpdatesAsync();
            
            using var dialog = new MessageDialog(this, DialogFlags.Modal,
                updateInfo != null ? MessageType.Info : MessageType.Info,
                updateInfo != null ? ButtonsType.YesNo : ButtonsType.Ok,
                updateInfo != null 
                    ? $"Canopy v{updateInfo.Version} is available.\n\nWould you like to open the download page?"
                    : "You're running the latest version of Canopy.");
            
            dialog.Title = updateInfo != null ? "Update Available" : "No Updates";
            var response = (ResponseType)dialog.Run();
            
            if (response == ResponseType.Yes && updateInfo != null)
            {
                updateService.OpenReleasePage(updateInfo);
            }
        }
        catch (Exception ex)
        {
            _logger.Warning($"Update check failed: {ex.Message}");
            using var dialog = new MessageDialog(this, DialogFlags.Modal, MessageType.Error, ButtonsType.Ok,
                $"Failed to check for updates:\n{ex.Message}");
            dialog.Title = "Error";
            dialog.Run();
        }
        finally
        {
            _checkUpdatesButton.Sensitive = true;
            _checkUpdatesButton.Label = "Check for Updates Now";
        }
    }

    private void OnDeleteEvent(object sender, DeleteEventArgs args)
    {
        args.RetVal = true;
        Hide();
    }
}
