using System.Diagnostics;
using System.Runtime.InteropServices;
using Canopy.Core.Logging;
using Canopy.Core.Platform;
using Gtk;

namespace Canopy.Linux.Services;

/// <summary>
/// Linux system tray icon service using AppIndicator.
/// 
/// Tries multiple library variants in order:
/// 1. libayatana-appindicator-glib (GLib-only, lighter)
/// 2. libayatana-appindicator3 (GTK3 variant)
/// 3. libappindicator3 (legacy Ubuntu)
/// 
/// For Arch Linux / CachyOS:
/// - Install: sudo pacman -S libayatana-appindicator
/// - On GNOME: sudo pacman -S gnome-shell-extension-appindicator
/// </summary>
public class LinuxTrayIconService : ITrayIconService
{
    private readonly ICanopyLogger _logger;
    private Menu? _contextMenu;
    private bool _isDisposed;
    private bool _initialized;
    
    private IntPtr _appIndicator = IntPtr.Zero;
    private AppIndicatorLibrary _activeLibrary = AppIndicatorLibrary.None;

    private enum AppIndicatorLibrary
    {
        None,
        AyatanaGlib,
        AyatanaGtk3,
        LegacyGtk3
    }

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

            // Try to initialize AppIndicator with various libraries
            if (TryInitializeAppIndicator())
            {
                _logger.Info($"Tray icon initialized successfully using {_activeLibrary}");
                return;
            }
            
            _logger.Warning("AppIndicator initialization failed - no compatible library found");
            _logger.Warning("Install one of:");
            _logger.Warning("  Arch: sudo pacman -S libayatana-appindicator");
            _logger.Warning("  Ubuntu: sudo apt install libayatana-appindicator3-1");
            _logger.Warning("On GNOME also install: gnome-shell-extension-appindicator");
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to initialize tray icon", ex);
        }
    }

    private bool TryInitializeAppIndicator()
    {
        // Get icon path
        var iconPath = AppIconManager.GetIconPath();
        string iconArg;
        
        if (iconPath != null && File.Exists(iconPath))
        {
            // AppIndicator expects path without extension for some variants
            iconArg = iconPath.EndsWith(".png") 
                ? iconPath.Substring(0, iconPath.Length - 4) 
                : iconPath;
            _logger.Debug($"Using icon path: {iconArg}");
        }
        else
        {
            iconArg = "applications-games";
            _logger.Warning($"Icon not found, using fallback: {iconArg}");
        }

        // Try each library variant in order of preference
        
        // 1. Try libayatana-appindicator (GLib variant - lighter weight)
        if (TryLibrary(AppIndicatorLibrary.AyatanaGlib, iconArg))
            return true;
            
        // 2. Try libayatana-appindicator3 (GTK3 variant)
        if (TryLibrary(AppIndicatorLibrary.AyatanaGtk3, iconArg))
            return true;
            
        // 3. Try legacy libappindicator3 (Ubuntu)
        if (TryLibrary(AppIndicatorLibrary.LegacyGtk3, iconArg))
            return true;

        return false;
    }

    private bool TryLibrary(AppIndicatorLibrary library, string iconArg)
    {
        try
        {
            _logger.Debug($"Trying {library}...");
            
            _appIndicator = library switch
            {
                AppIndicatorLibrary.AyatanaGlib => ayatana_glib_app_indicator_new("canopy", iconArg, 0),
                AppIndicatorLibrary.AyatanaGtk3 => ayatana_gtk3_app_indicator_new("canopy", iconArg, 0),
                AppIndicatorLibrary.LegacyGtk3 => legacy_gtk3_app_indicator_new("canopy", iconArg, 0),
                _ => IntPtr.Zero
            };

            if (_appIndicator == IntPtr.Zero)
            {
                _logger.Debug($"{library}: app_indicator_new returned null");
                return false;
            }

            // Set status to Active (1)
            SetStatus(library, _appIndicator, 1);
            
            // Set the menu
            if (_contextMenu != null)
            {
                SetMenu(library, _appIndicator, _contextMenu.Handle);
            }

            _activeLibrary = library;
            _logger.Info($"Successfully initialized {library}");
            return true;
        }
        catch (DllNotFoundException ex)
        {
            _logger.Debug($"{library}: Library not found - {ex.Message}");
            return false;
        }
        catch (EntryPointNotFoundException ex)
        {
            _logger.Debug($"{library}: Entry point not found - {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            _logger.Debug($"{library}: Failed - {ex.Message}");
            return false;
        }
    }

    private static void SetStatus(AppIndicatorLibrary library, IntPtr indicator, int status)
    {
        switch (library)
        {
            case AppIndicatorLibrary.AyatanaGlib:
                ayatana_glib_app_indicator_set_status(indicator, status);
                break;
            case AppIndicatorLibrary.AyatanaGtk3:
                ayatana_gtk3_app_indicator_set_status(indicator, status);
                break;
            case AppIndicatorLibrary.LegacyGtk3:
                legacy_gtk3_app_indicator_set_status(indicator, status);
                break;
        }
    }

    private static void SetMenu(AppIndicatorLibrary library, IntPtr indicator, IntPtr menu)
    {
        switch (library)
        {
            case AppIndicatorLibrary.AyatanaGlib:
                ayatana_glib_app_indicator_set_menu(indicator, menu);
                break;
            case AppIndicatorLibrary.AyatanaGtk3:
                ayatana_gtk3_app_indicator_set_menu(indicator, menu);
                break;
            case AppIndicatorLibrary.LegacyGtk3:
                legacy_gtk3_app_indicator_set_menu(indicator, menu);
                break;
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
        // AppIndicator doesn't support dynamic tooltips in most variants
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

    // libayatana-appindicator (GLib variant - preferred)
    // Package: libayatana-appindicator on Arch
    [DllImport("libayatana-appindicator.so.1", EntryPoint = "app_indicator_new")]
    private static extern IntPtr ayatana_glib_app_indicator_new(string id, string iconName, int category);
    
    [DllImport("libayatana-appindicator.so.1", EntryPoint = "app_indicator_set_status")]
    private static extern void ayatana_glib_app_indicator_set_status(IntPtr indicator, int status);
    
    [DllImport("libayatana-appindicator.so.1", EntryPoint = "app_indicator_set_menu")]
    private static extern void ayatana_glib_app_indicator_set_menu(IntPtr indicator, IntPtr menu);

    // libayatana-appindicator3 (GTK3 variant)
    // Package: libayatana-appindicator-gtk3 on Arch
    [DllImport("libayatana-appindicator3.so.1", EntryPoint = "app_indicator_new")]
    private static extern IntPtr ayatana_gtk3_app_indicator_new(string id, string iconName, int category);
    
    [DllImport("libayatana-appindicator3.so.1", EntryPoint = "app_indicator_set_status")]
    private static extern void ayatana_gtk3_app_indicator_set_status(IntPtr indicator, int status);
    
    [DllImport("libayatana-appindicator3.so.1", EntryPoint = "app_indicator_set_menu")]
    private static extern void ayatana_gtk3_app_indicator_set_menu(IntPtr indicator, IntPtr menu);

    // Legacy libappindicator3 (Ubuntu/older)
    [DllImport("libappindicator3.so.1", EntryPoint = "app_indicator_new")]
    private static extern IntPtr legacy_gtk3_app_indicator_new(string id, string iconName, int category);
    
    [DllImport("libappindicator3.so.1", EntryPoint = "app_indicator_set_status")]
    private static extern void legacy_gtk3_app_indicator_set_status(IntPtr indicator, int status);
    
    [DllImport("libappindicator3.so.1", EntryPoint = "app_indicator_set_menu")]
    private static extern void legacy_gtk3_app_indicator_set_menu(IntPtr indicator, IntPtr menu);

    #endregion
}
