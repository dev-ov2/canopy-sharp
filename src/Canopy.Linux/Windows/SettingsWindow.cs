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
/// Settings window for Canopy on Linux.
/// </summary>
public class SettingsWindow : Window
{
    private readonly ICanopyLogger _logger;
    private readonly ISettingsService _settingsService;
    private readonly LinuxPlatformServices _platformServices;

    private CheckButton? _startWithSystemCheck;
    private CheckButton? _startOpenCheck;
    private CheckButton? _autoUpdateCheck;
    private Button? _checkUpdatesButton;

    private bool _isLoading = true;

    public SettingsWindow() : base(WindowType.Toplevel)
    {
        _logger = CanopyLoggerFactory.CreateLogger<SettingsWindow>();
        _settingsService = App.Services.GetRequiredService<ISettingsService>();
        _platformServices = App.Services.GetRequiredService<LinuxPlatformServices>();

        SetupWindow();
        BuildUI();
        
        DeleteEvent += OnDeleteEvent;
    }

    private void SetupWindow()
    {
        Title = "Canopy Settings";
        SetDefaultSize(400, 300);
        SetPosition(WindowPosition.Center);
        Resizable = false;

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

        // Content
        var contentBox = new Box(Orientation.Vertical, 12);
        contentBox.MarginStart = 24;
        contentBox.MarginEnd = 24;
        contentBox.MarginBottom = 16;

        // General Section
        contentBox.PackStart(CreateSectionHeader("General"), false, false, 0);
        
        _startWithSystemCheck = new CheckButton("Start with System");
        _startWithSystemCheck.TooltipText = "Launch Canopy when you log in";
        contentBox.PackStart(_startWithSystemCheck, false, false, 0);
        
        _startOpenCheck = new CheckButton("Show Window on Startup");
        _startOpenCheck.TooltipText = "Open the main window when Canopy starts";
        _startOpenCheck.MarginStart = 20;
        contentBox.PackStart(_startOpenCheck, false, false, 0);

        // Updates Section
        contentBox.PackStart(CreateSectionHeader("Updates"), false, false, 8);
        
        _autoUpdateCheck = new CheckButton("Check for Updates Automatically");
        contentBox.PackStart(_autoUpdateCheck, false, false, 0);
        
        _checkUpdatesButton = new Button("Check for Updates Now");
        _checkUpdatesButton.Halign = Align.Start;
        _checkUpdatesButton.MarginStart = 4;
        contentBox.PackStart(_checkUpdatesButton, false, false, 4);

        // TODO: Add overlay settings when overlay is implemented
        // See: https://github.com/dev-ov2/canopy-sharp/issues/XXX

        mainBox.PackStart(contentBox, true, true, 0);

        // Footer
        mainBox.PackStart(new Separator(Orientation.Horizontal), false, false, 0);
        
        var footerBox = new Box(Orientation.Horizontal, 8);
        footerBox.MarginStart = 24;
        footerBox.MarginEnd = 24;
        footerBox.MarginTop = 12;
        footerBox.MarginBottom = 16;
        
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        var versionLabel = new Label($"v{version?.Major}.{version?.Minor}.{version?.Build}");
        versionLabel.Halign = Align.Start;
        versionLabel.StyleContext.AddClass("dim-label");
        
        var closeButton = new Button("Close");
        
        footerBox.PackStart(versionLabel, true, true, 0);
        footerBox.PackEnd(closeButton, false, false, 0);
        
        mainBox.PackStart(footerBox, false, false, 0);

        Add(mainBox);
        WireUpEvents(closeButton);
    }

    private void WireUpEvents(Button closeButton)
    {
        _startWithSystemCheck!.Toggled += (s, e) => {
            if (_isLoading) return;
            var active = _startWithSystemCheck.Active;
            _logger.Info($"StartWithSystem: {active}");
            _settingsService.Update(settings => settings.StartWithWindows = active);
            _ = _platformServices.SetAutoStartAsync(active, _startOpenCheck!.Active);
            _startOpenCheck.Sensitive = active;
        };
        
        _startOpenCheck!.Toggled += (s, e) => {
            if (_isLoading) return;
            var active = _startOpenCheck.Active;
            _logger.Info($"StartOpen: {active}");
            _settingsService.Update(settings => settings.StartOpen = active);
            if (_startWithSystemCheck.Active)
            {
                _ = _platformServices.SetAutoStartAsync(true, active);
            }
        };
        
        _autoUpdateCheck!.Toggled += (s, e) => {
            if (_isLoading) return;
            var active = _autoUpdateCheck.Active;
            _logger.Info($"AutoUpdate: {active}");
            _settingsService.Update(settings => settings.AutoUpdate = active);
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
            LoadSettings();
            ShowAll();
            Present();
            _isLoading = false;
            return false;
        });
    }

    private void LoadSettings()
    {
        try
        {
            var settings = _settingsService.Settings;
            
            // Check autostart status
            var autostartPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config", "autostart", "canopy.desktop");
            var isAutoStartEnabled = System.IO.File.Exists(autostartPath);
            
            _logger.Debug($"Loading settings: AutoStart={isAutoStartEnabled}, StartOpen={settings.StartOpen}, AutoUpdate={settings.AutoUpdate}");
            
            // Set UI state
            _startWithSystemCheck!.Active = isAutoStartEnabled;
            _startOpenCheck!.Active = settings.StartOpen;
            _startOpenCheck.Sensitive = isAutoStartEnabled;
            _autoUpdateCheck!.Active = settings.AutoUpdate;
            
            _logger.Debug("Settings loaded into UI");
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to load settings: {ex.Message}");
        }
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
                MessageType.Info,
                updateInfo != null ? ButtonsType.YesNo : ButtonsType.Ok,
                updateInfo != null 
                    ? $"Canopy v{updateInfo.Version} is available.\n\nOpen download page?"
                    : "You're running the latest version.");
            
            dialog.Title = updateInfo != null ? "Update Available" : "Up to Date";
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
