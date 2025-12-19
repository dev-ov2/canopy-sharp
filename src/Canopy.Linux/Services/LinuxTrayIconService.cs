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
            // Get icon - AppIndicator can use:
            // 1. An icon name from the theme (e.g., "firefox")
            // 2. An absolute path to an icon file
            var iconPath = AppIconManager.GetIconPath();
            string iconArg;
            
            if (iconPath != null && File.Exists(iconPath))
            {
                // Use absolute path - this is the most reliable method
                // Remove the .png extension as AppIndicator adds it
                iconArg = iconPath.EndsWith(".png") 
                    ? iconPath.Substring(0, iconPath.Length - 4) 
                    : iconPath;
                _logger.Debug($"Using icon path: {iconArg}");
            }
            else
            {
                // Fallback to system icon
                iconArg = "applications-games";
                _logger.Warning($"Icon not found, using fallback: {iconArg}");
            }

            // Create AppIndicator
            // Category 0 = ApplicationStatus
            _appIndicator = app_indicator_new("canopy", iconArg, 0);

            if (_appIndicator == IntPtr.Zero)
            {
                _logger.Warning("app_indicator_new returned null");
                return false;
            }

            // Set status to Active (1)
            app_indicator_set_status(_appIndicator, 1);
            
            // Set the menu (required)
            if (_contextMenu != null)
            {
                app_indicator_set_menu(_appIndicator, _contextMenu.Handle);
            }

            _logger.Debug("AppIndicator created successfully");
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

    // Wrapper functions that try both library variants
    private static IntPtr app_indicator_new(string id, string iconName, int category)
    {
        // Try ayatana first (Arch, modern distros)
        try 
        { 
            return ayatana_app_indicator_new(id, iconName, category); 
        }
        catch (DllNotFoundException) { }
        
        // Try ubuntu/older
        try 
        { 
            return ubuntu_app_indicator_new(id, iconName, category); 
        }
        catch (DllNotFoundException) { }
        
        return IntPtr.Zero;
    }

    private static void app_indicator_set_status(IntPtr indicator, int status)
    {
        try { ayatana_app_indicator_set_status(indicator, status); return; }
        catch (DllNotFoundException) { }
        
        try { ubuntu_app_indicator_set_status(indicator, status); }
        catch (DllNotFoundException) { }
    }

    private static void app_indicator_set_menu(IntPtr indicator, IntPtr menu)
    {
        try { ayatana_app_indicator_set_menu(indicator, menu); return; }
        catch (DllNotFoundException) { }
        
        try { ubuntu_app_indicator_set_menu(indicator, menu); }
        catch (DllNotFoundException) { }
    }

    // Ayatana AppIndicator (Arch, modern distros)
    [DllImport("libayatana-appindicator3.so.1", EntryPoint = "app_indicator_new")]
    private static extern IntPtr ayatana_app_indicator_new(string id, string iconName, int category);
    
    [DllImport("libayatana-appindicator3.so.1", EntryPoint = "app_indicator_set_status")]
    private static extern void ayatana_app_indicator_set_status(IntPtr indicator, int status);
    
    [DllImport("libayatana-appindicator3.so.1", EntryPoint = "app_indicator_set_menu")]
    private static extern void ayatana_app_indicator_set_menu(IntPtr indicator, IntPtr menu);

    // Ubuntu/older AppIndicator
    [DllImport("libappindicator3.so.1", EntryPoint = "app_indicator_new")]
    private static extern IntPtr ubuntu_app_indicator_new(string id, string iconName, int category);
    
    [DllImport("libappindicator3.so.1", EntryPoint = "app_indicator_set_status")]
    private static extern void ubuntu_app_indicator_set_status(IntPtr indicator, int status);
    
    [DllImport("libappindicator3.so.1", EntryPoint = "app_indicator_set_menu")]
    private static extern void ubuntu_app_indicator_set_menu(IntPtr indicator, IntPtr menu);

    #endregion
}
