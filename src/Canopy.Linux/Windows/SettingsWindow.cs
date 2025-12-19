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
/// Uses CheckButtons for toggles (cleaner look than GTK3 Switch).
/// </summary>
public class SettingsWindow : Window
{
    private readonly ICanopyLogger _logger;
    private readonly ISettingsService _settingsService;
    private readonly LinuxPlatformServices _platformServices;

    // UI Elements - using CheckButton instead of Switch for cleaner appearance
    private CheckButton? _startWithSystemCheck;
    private CheckButton? _startOpenCheck;
    private CheckButton? _autoUpdateCheck;
    private CheckButton? _enableOverlayCheck;
    private Entry? _overlayShortcutEntry;
    private Entry? _dragShortcutEntry;
    private Box? _overlaySettingsBox;
    private Button? _resetPositionButton;
    private Button? _checkUpdatesButton;

    private bool _isLoading;
    private bool _isCapturingShortcut;
    private Entry? _activeShortcutEntry;

    public SettingsWindow() : base(WindowType.Toplevel)
    {
        _logger = CanopyLoggerFactory.CreateLogger<SettingsWindow>();
        _settingsService = App.Services.GetRequiredService<ISettingsService>();
        _platformServices = App.Services.GetRequiredService<LinuxPlatformServices>();

        SetupWindow();
        BuildUI();
        
        // Load settings after window is shown
        Shown += (_, _) => LoadSettingsAsync();
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

        // Set window icon
        var pixbuf = AppIconManager.GetPixbuf(64);
        if (pixbuf != null)
        {
            Icon = pixbuf;
        }
    }

    private void BuildUI()
    {
        // Main container with padding
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
        
        _startWithSystemCheck = CreateCheckSetting(
            "Start with System",
            "Launch Canopy automatically when you log in");
        _startWithSystemCheck.Toggled += OnStartWithSystemToggled;
        contentBox.PackStart(_startWithSystemCheck, false, false, 0);
        
        _startOpenCheck = CreateCheckSetting(
            "Show Window on Startup", 
            "Open the main window when Canopy starts");
        _startOpenCheck.Toggled += OnStartOpenToggled;
        contentBox.PackStart(_startOpenCheck, false, false, 0);

        // === Updates Section ===
        contentBox.PackStart(CreateSectionHeader("Updates"), false, false, 8);
        
        _autoUpdateCheck = CreateCheckSetting(
            "Check for Updates Automatically",
            "Periodically check GitHub for new versions");
        _autoUpdateCheck.Toggled += OnAutoUpdateToggled;
        contentBox.PackStart(_autoUpdateCheck, false, false, 0);
        
        _checkUpdatesButton = new Button("Check for Updates Now");
        _checkUpdatesButton.Halign = Align.Start;
        _checkUpdatesButton.MarginStart = 4;
        _checkUpdatesButton.Clicked += OnCheckUpdatesClicked;
        contentBox.PackStart(_checkUpdatesButton, false, false, 4);

        // === Overlay Section ===
        contentBox.PackStart(CreateSectionHeader("Overlay"), false, false, 8);
        
        _enableOverlayCheck = CreateCheckSetting(
            "Enable Game Overlay",
            "Show the floating overlay window during games");
        _enableOverlayCheck.Toggled += OnEnableOverlayToggled;
        contentBox.PackStart(_enableOverlayCheck, false, false, 0);
        
        // Overlay settings (indented)
        _overlaySettingsBox = new Box(Orientation.Vertical, 8);
        _overlaySettingsBox.MarginStart = 24;
        
        // Toggle shortcut
        var toggleShortcutBox = new Box(Orientation.Horizontal, 12);
        var toggleShortcutLabel = new Label("Toggle Shortcut:");
        toggleShortcutLabel.Halign = Align.Start;
        _overlayShortcutEntry = new Entry();
        _overlayShortcutEntry.WidthChars = 16;
        _overlayShortcutEntry.IsEditable = false;
        _overlayShortcutEntry.FocusInEvent += OnShortcutFocusIn;
        _overlayShortcutEntry.FocusOutEvent += OnShortcutFocusOut;
        _overlayShortcutEntry.KeyPressEvent += OnOverlayShortcutKeyPress;
        toggleShortcutBox.PackStart(toggleShortcutLabel, false, false, 0);
        toggleShortcutBox.PackStart(_overlayShortcutEntry, false, false, 0);
        _overlaySettingsBox.PackStart(toggleShortcutBox, false, false, 0);
        
        // Drag shortcut
        var dragShortcutBox = new Box(Orientation.Horizontal, 12);
        var dragShortcutLabel = new Label("Drag Shortcut:");
        dragShortcutLabel.Halign = Align.Start;
        dragShortcutLabel.WidthChars = 14; // Align with above
        _dragShortcutEntry = new Entry();
        _dragShortcutEntry.WidthChars = 16;
        _dragShortcutEntry.IsEditable = false;
        _dragShortcutEntry.FocusInEvent += OnShortcutFocusIn;
        _dragShortcutEntry.FocusOutEvent += OnShortcutFocusOut;
        _dragShortcutEntry.KeyPressEvent += OnDragShortcutKeyPress;
        dragShortcutBox.PackStart(dragShortcutLabel, false, false, 0);
        dragShortcutBox.PackStart(_dragShortcutEntry, false, false, 0);
        _overlaySettingsBox.PackStart(dragShortcutBox, false, false, 0);
        
        // Reset position button
        _resetPositionButton = new Button("Reset Overlay Position");
        _resetPositionButton.Halign = Align.Start;
        _resetPositionButton.Clicked += OnResetPositionClicked;
        _overlaySettingsBox.PackStart(_resetPositionButton, false, false, 4);
        
        contentBox.PackStart(_overlaySettingsBox, false, false, 0);

        scrolled.Add(contentBox);
        mainBox.PackStart(scrolled, true, true, 0);

        // Footer
        var footerBox = new Box(Orientation.Horizontal, 8);
        footerBox.MarginStart = 24;
        footerBox.MarginEnd = 24;
        footerBox.MarginTop = 12;
        footerBox.MarginBottom = 16;
        
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        var versionLabel = new Label();
        versionLabel.Markup = $"<span size='small' foreground='gray'>Canopy v{version?.Major}.{version?.Minor}.{version?.Build}</span>";
        versionLabel.Halign = Align.Start;
        
        var closeButton = new Button("Close");
        closeButton.Clicked += (_, _) => Hide();
        
        footerBox.PackStart(versionLabel, true, true, 0);
        footerBox.PackEnd(closeButton, false, false, 0);
        
        mainBox.PackEnd(footerBox, false, false, 0);
        mainBox.PackEnd(new Separator(Orientation.Horizontal), false, false, 0);

        Add(mainBox);
        ShowAll();
    }

    private Label CreateSectionHeader(string text)
    {
        var label = new Label();
        label.Markup = $"<span weight='bold'>{text}</span>";
        label.Halign = Align.Start;
        label.MarginTop = 8;
        return label;
    }

    private CheckButton CreateCheckSetting(string title, string description)
    {
        var check = new CheckButton();
        
        // Create a rich label for the check button
        var labelBox = new Box(Orientation.Vertical, 2);
        
        var titleLabel = new Label();
        titleLabel.Markup = title;
        titleLabel.Halign = Align.Start;
        
        var descLabel = new Label();
        descLabel.Markup = $"<span size='small' foreground='gray'>{description}</span>";
        descLabel.Halign = Align.Start;
        
        labelBox.PackStart(titleLabel, false, false, 0);
        labelBox.PackStart(descLabel, false, false, 0);
        
        check.Add(labelBox);
        check.MarginStart = 4;
        
        return check;
    }

    private async void LoadSettingsAsync()
    {
        _isLoading = true;
        
        try
        {
            var settings = _settingsService.Settings;
            var isAutoStartEnabled = await _platformServices.IsAutoStartEnabledAsync();
            
            _logger.Debug($"Loading settings: AutoStart={isAutoStartEnabled}, StartOpen={settings.StartOpen}, AutoUpdate={settings.AutoUpdate}, EnableOverlay={settings.EnableOverlay}");
            
            _startWithSystemCheck!.Active = isAutoStartEnabled;
            _startOpenCheck!.Active = settings.StartOpen;
            _startOpenCheck.Sensitive = isAutoStartEnabled;
            _autoUpdateCheck!.Active = settings.AutoUpdate;
            _enableOverlayCheck!.Active = settings.EnableOverlay;
            _overlayShortcutEntry!.Text = settings.OverlayToggleShortcut;
            _dragShortcutEntry!.Text = settings.OverlayDragShortcut;
            
            UpdateOverlayControlsSensitivity();
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to load settings: {ex.Message}");
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void UpdateOverlayControlsSensitivity()
    {
        var isEnabled = _enableOverlayCheck?.Active ?? false;
        _overlaySettingsBox!.Sensitive = isEnabled;
    }

    private void OnStartWithSystemToggled(object? sender, EventArgs e)
    {
        if (_isLoading) return;
        
        var isActive = _startWithSystemCheck!.Active;
        _logger.Info($"Start with system changed: {isActive}");
        
        // Update autostart
        _ = _platformServices.SetAutoStartAsync(isActive, _startOpenCheck!.Active);
        
        // Save to settings
        _settingsService.Update(s => s.StartWithWindows = isActive);
        
        // Update dependent control
        _startOpenCheck.Sensitive = isActive;
    }

    private void OnStartOpenToggled(object? sender, EventArgs e)
    {
        if (_isLoading) return;
        
        var isActive = _startOpenCheck!.Active;
        _logger.Info($"Start open changed: {isActive}");
        
        _settingsService.Update(s => s.StartOpen = isActive);
        
        // Also update the autostart entry if autostart is enabled
        if (_startWithSystemCheck!.Active)
        {
            _ = _platformServices.SetAutoStartAsync(true, isActive);
        }
    }

    private void OnAutoUpdateToggled(object? sender, EventArgs e)
    {
        if (_isLoading) return;
        
        var isActive = _autoUpdateCheck!.Active;
        _logger.Info($"Auto update changed: {isActive}");
        
        _settingsService.Update(s => s.AutoUpdate = isActive);
    }

    private void OnEnableOverlayToggled(object? sender, EventArgs e)
    {
        if (_isLoading) return;
        
        var isActive = _enableOverlayCheck!.Active;
        _logger.Info($"Enable overlay changed: {isActive}");
        
        _settingsService.Update(s => s.EnableOverlay = isActive);
        UpdateOverlayControlsSensitivity();

        if (!isActive)
        {
            try { App.Services.GetRequiredService<OverlayWindow>().HideOverlay(); }
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
        if (state.HasFlag(ModifierType.ControlMask))
            sb.Append("Ctrl+");
        if (state.HasFlag(ModifierType.Mod1Mask))
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
        var keyName = key.ToString();
        
        if (keyName.Length == 1 && char.IsLetterOrDigit(keyName[0]))
            return keyName.ToUpper();
            
        return keyName switch
        {
            "space" => "Space",
            "Return" => "Enter",
            "Escape" => "Escape",
            _ when keyName.StartsWith("F") => keyName,
            _ => keyName.Replace("_", "")
        };
    }

    private void OnResetPositionClicked(object? sender, EventArgs e)
    {
        _logger.Info("Reset overlay position");
        _settingsService.Update(s => { s.OverlayX = null; s.OverlayY = null; });
        
        try { App.Services.GetRequiredService<OverlayWindow>().PositionAtScreenEdge(); }
        catch { }
    }

    private async void OnCheckUpdatesClicked(object? sender, EventArgs e)
    {
        _checkUpdatesButton!.Sensitive = false;
        _checkUpdatesButton.Label = "Checking...";
        
        try
        {
            var updateService = App.Services.GetRequiredService<LinuxUpdateService>();
            var updateInfo = await updateService.CheckForUpdatesAsync();
            
            if (updateInfo != null)
            {
                var dialog = new MessageDialog(
                    this,
                    DialogFlags.Modal,
                    MessageType.Info,
                    ButtonsType.YesNo,
                    $"Canopy v{updateInfo.Version} is available.\n\nWould you like to open the download page?");
                
                dialog.Title = "Update Available";
                var response = (ResponseType)dialog.Run();
                dialog.Destroy();
                
                if (response == ResponseType.Yes)
                {
                    updateService.OpenReleasePage(updateInfo);
                }
            }
            else
            {
                var dialog = new MessageDialog(
                    this,
                    DialogFlags.Modal,
                    MessageType.Info,
                    ButtonsType.Ok,
                    "You're running the latest version of Canopy.");
                
                dialog.Title = "No Updates";
                dialog.Run();
                dialog.Destroy();
            }
        }
        catch (Exception ex)
        {
            _logger.Warning($"Update check failed: {ex.Message}");
            
            var dialog = new MessageDialog(
                this,
                DialogFlags.Modal,
                MessageType.Error,
                ButtonsType.Ok,
                $"Failed to check for updates:\n{ex.Message}");
            
            dialog.Title = "Error";
            dialog.Run();
            dialog.Destroy();
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
