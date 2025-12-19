using System.Diagnostics;
using System.Runtime.InteropServices;
using Canopy.Core.Logging;
using Canopy.Core.Platform;
using Gtk;

namespace Canopy.Linux.Services;

/// <summary>
/// Linux system tray icon service using GtkStatusIcon with fallback approaches
/// </summary>
public class LinuxTrayIconService : ITrayIconService
{
    private readonly ICanopyLogger _logger;
    private Menu? _contextMenu;
    private bool _isDisposed;
    private bool _initialized;
    
    // StatusIcon (works on many DEs including with AppIndicator extensions)
    private StatusIcon? _statusIcon;
    
    // AppIndicator via D-Bus (alternative approach)
    private IntPtr _appIndicator = IntPtr.Zero;

    public event EventHandler? ShowWindowRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? RescanGamesRequested;
    public event EventHandler? QuitRequested;

    public LinuxTrayIconService()
    {
        _logger = CanopyLoggerFactory.CreateLogger<LinuxTrayIconService>();
    }

    public void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        try
        {
            // Create context menu first
            CreateContextMenu();

            // Try multiple approaches in order of preference
            
            // 1. Try StatusIcon first - simpler and works with AppIndicator GNOME extension
            if (TryInitializeStatusIcon())
            {
                _logger.Info("Tray icon initialized using StatusIcon");
                return;
            }

            // 2. Try AppIndicator
            if (TryInitializeAppIndicator())
            {
                _logger.Info("Tray icon initialized using AppIndicator");
                return;
            }
            
            _logger.Warning("No tray icon backend available - tray icon will not be shown");
            _logger.Warning("On GNOME, install 'AppIndicator and KStatusNotifierItem Support' extension");
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to initialize tray icon", ex);
        }
    }

    private bool TryInitializeStatusIcon()
    {
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "canopy.png");
            _logger.Debug($"Looking for icon at: {iconPath}");
            
            if (File.Exists(iconPath))
            {
                var pixbuf = new Gdk.Pixbuf(iconPath);
                // Scale to appropriate size for tray (typically 22x22 or 24x24)
                var scaled = pixbuf.ScaleSimple(24, 24, Gdk.InterpType.Bilinear);
                _statusIcon = new StatusIcon(scaled);
                _logger.Debug("StatusIcon created from PNG file");
            }
            else
            {
                // Try system icon
                _statusIcon = StatusIcon.NewFromIconName("applications-games");
                _logger.Debug("StatusIcon created from system icon");
            }

            if (_statusIcon == null)
            {
                _logger.Warning("Failed to create StatusIcon");
                return false;
            }

            _statusIcon.Visible = true;
            _statusIcon.TooltipText = "Canopy - Game Overlay";
            _statusIcon.Title = "Canopy";

            // Connect events
            _statusIcon.Activate += OnStatusIconActivate;
            _statusIcon.PopupMenu += OnStatusIconPopupMenu;

            // Check if it's actually embedded (visible in tray)
            // Note: This might return false initially, the icon can take time to embed
            GLib.Timeout.Add(1000, () =>
            {
                if (_statusIcon != null && _statusIcon.IsEmbedded)
                {
                    _logger.Info("StatusIcon is embedded in system tray");
                }
                else
                {
                    _logger.Warning("StatusIcon may not be visible - check if your DE supports status icons");
                }
                return false; // Don't repeat
            });

            return true;
        }
        catch (Exception ex)
        {
            _logger.Warning($"StatusIcon initialization failed: {ex.Message}");
            return false;
        }
    }

    private bool TryInitializeAppIndicator()
    {
        try
        {
            // Copy icon to a standard location where AppIndicator can find it
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "canopy.png");
            var iconDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "icons");
            var targetIconPath = Path.Combine(iconDir, "canopy-tray.png");

            Directory.CreateDirectory(iconDir);
            
            if (File.Exists(iconPath) && !File.Exists(targetIconPath))
            {
                File.Copy(iconPath, targetIconPath, true);
                _logger.Debug($"Copied icon to {targetIconPath}");
            }

            // Try to create AppIndicator with the icon path
            string iconName = File.Exists(targetIconPath) ? targetIconPath : "applications-games";
            
            _appIndicator = app_indicator_new("canopy-app", iconName, 0);

            if (_appIndicator == IntPtr.Zero)
            {
                _logger.Warning("app_indicator_new returned null");
                return false;
            }

            app_indicator_set_status(_appIndicator, 1); // Active
            
            if (_contextMenu != null)
            {
                app_indicator_set_menu(_appIndicator, _contextMenu.Handle);
            }

            _logger.Debug("AppIndicator created successfully");
            return true;
        }
        catch (DllNotFoundException)
        {
            _logger.Debug("AppIndicator library not found");
            return false;
        }
        catch (Exception ex)
        {
            _logger.Warning($"AppIndicator initialization failed: {ex.Message}");
            return false;
        }
    }

    private void CreateContextMenu()
    {
        _contextMenu = new Menu();

        var openItem = new MenuItem("Open Canopy");
        openItem.Activated += (_, _) => ShowWindowRequested?.Invoke(this, EventArgs.Empty);
        _contextMenu.Append(openItem);

        _contextMenu.Append(new SeparatorMenuItem());

        var rescanItem = new MenuItem("Rescan Games");
        rescanItem.Activated += (_, _) => RescanGamesRequested?.Invoke(this, EventArgs.Empty);
        _contextMenu.Append(rescanItem);

        var settingsItem = new MenuItem("Settings");
        settingsItem.Activated += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);
        _contextMenu.Append(settingsItem);

        _contextMenu.Append(new SeparatorMenuItem());

        var quitItem = new MenuItem("Quit");
        quitItem.Activated += (_, _) => QuitRequested?.Invoke(this, EventArgs.Empty);
        _contextMenu.Append(quitItem);

        _contextMenu.ShowAll();
    }

    private void OnStatusIconActivate(object? sender, EventArgs args)
    {
        ShowWindowRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnStatusIconPopupMenu(object? sender, PopupMenuArgs args)
    {
        _contextMenu?.Popup();
    }

    public void ShowBalloon(string title, string message)
    {
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "canopy.png");
            var iconArg = File.Exists(iconPath) ? $"-i \"{iconPath}\"" : "";
            
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "notify-send",
                    Arguments = $"{iconArg} -a Canopy \"{EscapeShellArg(title)}\" \"{EscapeShellArg(message)}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            process.Start();
            _logger.Debug($"Showed notification: {title}");
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to show notification", ex);
        }
    }

    public void SetTooltip(string text)
    {
        Gtk.Application.Invoke((_, _) =>
        {
            if (_statusIcon != null)
            {
                _statusIcon.TooltipText = text;
            }
        });
    }

    private static string EscapeShellArg(string arg)
    {
        return arg.Replace("\"", "\\\"").Replace("$", "\\$").Replace("`", "\\`");
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        if (_statusIcon != null)
        {
            _statusIcon.Visible = false;
            _statusIcon.Dispose();
            _statusIcon = null;
        }

        _contextMenu?.Dispose();
        _contextMenu = null;

        _logger.Info("Tray icon disposed");
    }

    #region AppIndicator P/Invoke

    // Try ayatana first, then ubuntu
    private static IntPtr app_indicator_new(string id, string iconName, int category)
    {
        try { return app_indicator_new_ayatana(id, iconName, category); }
        catch (DllNotFoundException) { }
        
        try { return app_indicator_new_ubuntu(id, iconName, category); }
        catch (DllNotFoundException) { }
        
        return IntPtr.Zero;
    }

    private static void app_indicator_set_status(IntPtr indicator, int status)
    {
        try { app_indicator_set_status_ayatana(indicator, status); return; }
        catch (DllNotFoundException) { }
        
        try { app_indicator_set_status_ubuntu(indicator, status); }
        catch (DllNotFoundException) { }
    }

    private static void app_indicator_set_menu(IntPtr indicator, IntPtr menu)
    {
        try { app_indicator_set_menu_ayatana(indicator, menu); return; }
        catch (DllNotFoundException) { }
        
        try { app_indicator_set_menu_ubuntu(indicator, menu); }
        catch (DllNotFoundException) { }
    }

    [DllImport("libayatana-appindicator3.so.1", EntryPoint = "app_indicator_new")]
    private static extern IntPtr app_indicator_new_ayatana(string id, string iconName, int category);
    
    [DllImport("libayatana-appindicator3.so.1", EntryPoint = "app_indicator_set_status")]
    private static extern void app_indicator_set_status_ayatana(IntPtr indicator, int status);
    
    [DllImport("libayatana-appindicator3.so.1", EntryPoint = "app_indicator_set_menu")]
    private static extern void app_indicator_set_menu_ayatana(IntPtr indicator, IntPtr menu);

    [DllImport("libappindicator3.so.1", EntryPoint = "app_indicator_new")]
    private static extern IntPtr app_indicator_new_ubuntu(string id, string iconName, int category);
    
    [DllImport("libappindicator3.so.1", EntryPoint = "app_indicator_set_status")]
    private static extern void app_indicator_set_status_ubuntu(IntPtr indicator, int status);
    
    [DllImport("libappindicator3.so.1", EntryPoint = "app_indicator_set_menu")]
    private static extern void app_indicator_set_menu_ubuntu(IntPtr indicator, IntPtr menu);

    #endregion
}
