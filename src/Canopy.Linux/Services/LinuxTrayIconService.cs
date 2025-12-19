using System.Diagnostics;
using System.Runtime.InteropServices;
using Canopy.Core.Logging;
using Canopy.Core.Platform;
using Gtk;

namespace Canopy.Linux.Services;

/// <summary>
/// Linux system tray icon service using AppIndicator (libayatana-appindicator).
/// 
/// For Arch Linux / CachyOS:
/// - Requires libayatana-appindicator3 package
/// - On GNOME, requires gnome-shell-extension-appindicator
/// - Icons must be in an icon theme or use set_icon_theme_path
/// </summary>
public class LinuxTrayIconService : ITrayIconService
{
    private readonly ICanopyLogger _logger;
    private Menu? _contextMenu;
    private bool _isDisposed;
    private bool _initialized;
    
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

        GLib.Idle.Add(() =>
        {
            InitializeInternal();
            return false;
        });
    }

    private void InitializeInternal()
    {
        try
        {
            _logger.Info("Initializing tray icon...");
            
            // Create context menu (required for AppIndicator)
            CreateContextMenu();

            // Add our icon directory to the theme search path
            AppIconManager.AddToIconTheme();

            // Try to initialize AppIndicator
            if (TryInitializeAppIndicator())
            {
                _logger.Info("Tray icon initialized successfully");
                return;
            }
            
            _logger.Warning("AppIndicator initialization failed");
            _logger.Warning("Install: sudo pacman -S libayatana-appindicator3");
            _logger.Warning("On GNOME: sudo pacman -S gnome-shell-extension-appindicator");
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to initialize tray icon", ex);
        }
    }

    private bool TryInitializeAppIndicator()
    {
        try
        {
            // Create AppIndicator with icon name
            _appIndicator = app_indicator_new(
                "canopy-app",  // Unique ID
                AppIconManager.IconName,  // Icon name (without path/extension)
                0  // Category: ApplicationStatus
            );

            if (_appIndicator == IntPtr.Zero)
            {
                _logger.Warning("app_indicator_new returned null");
                return false;
            }

            // Set custom icon theme path if we installed icons there
            var iconDir = AppIconManager.InstalledIconDirectory;
            if (iconDir != null)
            {
                _logger.Debug($"Setting icon theme path: {iconDir}");
                app_indicator_set_icon_theme_path(_appIndicator, iconDir);
            }

            // Set the icon again after setting theme path
            app_indicator_set_icon_full(_appIndicator, AppIconManager.IconName, "Canopy");

            // Set status to Active
            app_indicator_set_status(_appIndicator, 1);
            
            // Set the menu
            if (_contextMenu != null)
            {
                app_indicator_set_menu(_appIndicator, _contextMenu.Handle);
            }

            // Set title
            app_indicator_set_title(_appIndicator, "Canopy");

            _logger.Debug($"AppIndicator created with icon: {AppIconManager.IconName}");
            return true;
        }
        catch (DllNotFoundException ex)
        {
            _logger.Debug($"AppIndicator library not found: {ex.Message}");
            return false;
        }
        catch (EntryPointNotFoundException ex)
        {
            _logger.Debug($"AppIndicator function not found: {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            _logger.Debug($"AppIndicator initialization failed: {ex.Message}");
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

    public void ShowBalloon(string title, string message)
    {
        try
        {
            var iconPath = AppIconManager.GetIconPath();
            var iconArg = iconPath != null ? $"-i \"{iconPath}\"" : "";
            
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
        // AppIndicator doesn't support dynamic tooltips
    }

    private static string EscapeShellArg(string arg)
    {
        return arg.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("$", "\\$").Replace("`", "\\`");
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _contextMenu?.Dispose();
        _contextMenu = null;

        _logger.Info("Tray icon disposed");
    }

    #region AppIndicator P/Invoke

    private static IntPtr app_indicator_new(string id, string iconName, int category)
    {
        try { return ayatana_app_indicator_new(id, iconName, category); }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
        
        try { return ubuntu_app_indicator_new(id, iconName, category); }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
        
        return IntPtr.Zero;
    }

    private static void app_indicator_set_status(IntPtr indicator, int status)
    {
        try { ayatana_app_indicator_set_status(indicator, status); return; }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
        
        try { ubuntu_app_indicator_set_status(indicator, status); }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }

    private static void app_indicator_set_menu(IntPtr indicator, IntPtr menu)
    {
        try { ayatana_app_indicator_set_menu(indicator, menu); return; }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
        
        try { ubuntu_app_indicator_set_menu(indicator, menu); }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }

    private static void app_indicator_set_title(IntPtr indicator, string title)
    {
        try { ayatana_app_indicator_set_title(indicator, title); return; }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
        
        try { ubuntu_app_indicator_set_title(indicator, title); }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }

    private static void app_indicator_set_icon_theme_path(IntPtr indicator, string iconThemePath)
    {
        try { ayatana_app_indicator_set_icon_theme_path(indicator, iconThemePath); return; }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
        
        try { ubuntu_app_indicator_set_icon_theme_path(indicator, iconThemePath); }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }

    private static void app_indicator_set_icon_full(IntPtr indicator, string iconName, string iconDesc)
    {
        try { ayatana_app_indicator_set_icon_full(indicator, iconName, iconDesc); return; }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
        
        try { ubuntu_app_indicator_set_icon_full(indicator, iconName, iconDesc); }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }

    // Ayatana AppIndicator
    [DllImport("libayatana-appindicator3.so.1")]
    private static extern IntPtr ayatana_app_indicator_new(string id, string iconName, int category);
    
    [DllImport("libayatana-appindicator3.so.1")]
    private static extern void ayatana_app_indicator_set_status(IntPtr indicator, int status);
    
    [DllImport("libayatana-appindicator3.so.1")]
    private static extern void ayatana_app_indicator_set_menu(IntPtr indicator, IntPtr menu);

    [DllImport("libayatana-appindicator3.so.1")]
    private static extern void ayatana_app_indicator_set_title(IntPtr indicator, string title);

    [DllImport("libayatana-appindicator3.so.1")]
    private static extern void ayatana_app_indicator_set_icon_theme_path(IntPtr indicator, string iconThemePath);

    [DllImport("libayatana-appindicator3.so.1")]
    private static extern void ayatana_app_indicator_set_icon_full(IntPtr indicator, string iconName, string iconDesc);

    // Ubuntu AppIndicator fallback
    [DllImport("libappindicator3.so.1")]
    private static extern IntPtr ubuntu_app_indicator_new(string id, string iconName, int category);
    
    [DllImport("libappindicator3.so.1")]
    private static extern void ubuntu_app_indicator_set_status(IntPtr indicator, int status);
    
    [DllImport("libappindicator3.so.1")]
    private static extern void ubuntu_app_indicator_set_menu(IntPtr indicator, IntPtr menu);

    [DllImport("libappindicator3.so.1")]
    private static extern void ubuntu_app_indicator_set_title(IntPtr indicator, string title);

    [DllImport("libappindicator3.so.1")]
    private static extern void ubuntu_app_indicator_set_icon_theme_path(IntPtr indicator, string iconThemePath);

    [DllImport("libappindicator3.so.1")]
    private static extern void ubuntu_app_indicator_set_icon_full(IntPtr indicator, string iconName, string iconDesc);

    #endregion
}
