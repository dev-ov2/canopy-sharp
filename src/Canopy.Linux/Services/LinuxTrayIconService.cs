using System.Diagnostics;
using System.Runtime.InteropServices;
using Canopy.Core.Logging;
using Canopy.Core.Platform;
using Gtk;

namespace Canopy.Linux.Services;

/// <summary>
/// Linux system tray icon service using AppIndicator.
/// 
/// This service is OPTIONAL - the app will function without a tray icon.
/// If initialization fails, the app continues without a tray.
/// 
/// Tries multiple library variants in order:
/// 1. libayatana-appindicator-glib (GLib-only, recommended for new code)
/// 2. libayatana-appindicator3 (GTK3 variant, deprecated but widely available)
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
    private bool _initializationFailed;
    
    private IntPtr _appIndicator = IntPtr.Zero;
    private AppIndicatorLibrary _activeLibrary = AppIndicatorLibrary.None;

    private enum AppIndicatorLibrary
    {
        None,
        AyatanaGlib,      // libayatana-appindicator-glib (recommended)
        AyatanaGtk3,      // libayatana-appindicator3 (deprecated but common)
        LegacyGtk3        // libappindicator3 (legacy Ubuntu)
    }

    public event EventHandler? ShowWindowRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? RescanGamesRequested;
    public event EventHandler? QuitRequested;

    /// <summary>
    /// Indicates whether the tray icon was successfully initialized.
    /// The app can function without a tray icon.
    /// </summary>
    public bool IsAvailable => _activeLibrary != AppIndicatorLibrary.None && !_initializationFailed;

    public LinuxTrayIconService()
    {
        _logger = CanopyLoggerFactory.CreateLogger<LinuxTrayIconService>();
    }

    public void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        // Use GLib.Idle to ensure we're on the main thread
        // But also set a flag so we don't block app startup
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
            _logger.Info("Initializing tray icon (optional feature)...");
            
            // First, try to create the AppIndicator WITHOUT a menu
            // This tests if the library is available before creating GTK widgets
            if (!TryInitializeAppIndicator())
            {
                _initializationFailed = true;
                _logger.Warning("AppIndicator initialization failed - tray icon disabled");
                _logger.Warning("The app will continue without a system tray icon.");
                _logger.Warning("Install one of:");
                _logger.Warning("  Arch: sudo pacman -S libayatana-appindicator");
                _logger.Warning("  Ubuntu: sudo apt install libayatana-appindicator3-1");
                return;
            }

            // Now create the context menu (only if AppIndicator worked)
            try
            {
                CreateContextMenu();
                
                // Set the menu on the indicator
                if (_contextMenu != null && _appIndicator != IntPtr.Zero)
                {
                    SetMenu(_activeLibrary, _appIndicator, _contextMenu.Handle);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Failed to create tray menu: {ex.Message}");
                _logger.Warning("Tray icon will be visible but menu may not work");
            }

            _logger.Info($"Tray icon initialized successfully using {_activeLibrary}");
        }
        catch (Exception ex)
        {
            _initializationFailed = true;
            _logger.Warning($"Tray icon initialization failed: {ex.Message}");
            _logger.Warning("The app will continue without a system tray icon.");
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
        
        // 1. Try libayatana-appindicator-glib (GLib variant - recommended for new code)
        if (TryLibrary(AppIndicatorLibrary.AyatanaGlib, iconArg))
            return true;
            
        // 2. Try libayatana-appindicator3 (GTK3 variant - deprecated but widely available)
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

            // Set status to Active (1) - don't set menu yet
            SetStatus(library, _appIndicator, 1);

            _activeLibrary = library;
            _logger.Debug($"Successfully created AppIndicator with {library}");
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
        openItem.Activated += (_, _) => 
        {
            try { ShowWindowRequested?.Invoke(this, EventArgs.Empty); }
            catch (Exception ex) { _logger.Warning($"ShowWindowRequested handler error: {ex.Message}"); }
        };
        _contextMenu.Append(openItem);

        _contextMenu.Append(new SeparatorMenuItem());

        var rescanItem = new MenuItem("Rescan Games");
        rescanItem.Activated += (_, _) => 
        {
            try { RescanGamesRequested?.Invoke(this, EventArgs.Empty); }
            catch (Exception ex) { _logger.Warning($"RescanGamesRequested handler error: {ex.Message}"); }
        };
        _contextMenu.Append(rescanItem);

        var settingsItem = new MenuItem("Settings");
        settingsItem.Activated += (_, _) => 
        {
            try { SettingsRequested?.Invoke(this, EventArgs.Empty); }
            catch (Exception ex) { _logger.Warning($"SettingsRequested handler error: {ex.Message}"); }
        };
        _contextMenu.Append(settingsItem);

        _contextMenu.Append(new SeparatorMenuItem());

        var quitItem = new MenuItem("Quit");
        quitItem.Activated += (_, _) => 
        {
            try { QuitRequested?.Invoke(this, EventArgs.Empty); }
            catch (Exception ex) { _logger.Warning($"QuitRequested handler error: {ex.Message}"); }
        };
        _contextMenu.Append(quitItem);

        _contextMenu.ShowAll();
    }

    public void ShowBalloon(string title, string message)
    {
        // This uses notify-send, which is separate from the tray icon
        // and works even if the tray icon failed to initialize
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
            _logger.Warning($"Failed to show notification: {ex.Message}");
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

        try
        {
            _contextMenu?.Dispose();
        }
        catch (Exception ex)
        {
            _logger.Debug($"Error disposing context menu: {ex.Message}");
        }
        _contextMenu = null;

        _logger.Info("Tray icon disposed");
    }

    #region AppIndicator P/Invoke

    // =========================================================================
    // libayatana-appindicator-glib (GLib-only, RECOMMENDED for new code)
    // Package: libayatana-appindicator on Arch (provides the -glib variant)
    // This is the modern, non-deprecated library
    // =========================================================================
    
    // Try multiple possible .so names for the glib variant
    [DllImport("libayatana-appindicator-glib.so.1", EntryPoint = "app_indicator_new")]
    private static extern IntPtr ayatana_glib_v1_app_indicator_new(string id, string iconName, int category);
    
    [DllImport("libayatana-appindicator-glib.so.1", EntryPoint = "app_indicator_set_status")]
    private static extern void ayatana_glib_v1_app_indicator_set_status(IntPtr indicator, int status);
    
    [DllImport("libayatana-appindicator-glib.so.1", EntryPoint = "app_indicator_set_menu")]
    private static extern void ayatana_glib_v1_app_indicator_set_menu(IntPtr indicator, IntPtr menu);

    // Wrapper that tries different glib library names
    private static IntPtr ayatana_glib_app_indicator_new(string id, string iconName, int category)
    {
        // Try libayatana-appindicator-glib.so.1 first
        try { return ayatana_glib_v1_app_indicator_new(id, iconName, category); }
        catch (DllNotFoundException) { }
        
        // Try without version suffix
        try { return ayatana_glib_nover_app_indicator_new(id, iconName, category); }
        catch (DllNotFoundException) { }
        
        throw new DllNotFoundException("libayatana-appindicator-glib not found");
    }
    
    private static void ayatana_glib_app_indicator_set_status(IntPtr indicator, int status)
    {
        try { ayatana_glib_v1_app_indicator_set_status(indicator, status); return; }
        catch (DllNotFoundException) { }
        
        try { ayatana_glib_nover_app_indicator_set_status(indicator, status); return; }
        catch (DllNotFoundException) { }
        
        throw new DllNotFoundException("libayatana-appindicator-glib not found");
    }
    
    private static void ayatana_glib_app_indicator_set_menu(IntPtr indicator, IntPtr menu)
    {
        try { ayatana_glib_v1_app_indicator_set_menu(indicator, menu); return; }
        catch (DllNotFoundException) { }
        
        try { ayatana_glib_nover_app_indicator_set_menu(indicator, menu); return; }
        catch (DllNotFoundException) { }
        
        throw new DllNotFoundException("libayatana-appindicator-glib not found");
    }

    // Without version suffix
    [DllImport("libayatana-appindicator-glib.so", EntryPoint = "app_indicator_new")]
    private static extern IntPtr ayatana_glib_nover_app_indicator_new(string id, string iconName, int category);
    
    [DllImport("libayatana-appindicator-glib.so", EntryPoint = "app_indicator_set_status")]
    private static extern void ayatana_glib_nover_app_indicator_set_status(IntPtr indicator, int status);
    
    [DllImport("libayatana-appindicator-glib.so", EntryPoint = "app_indicator_set_menu")]
    private static extern void ayatana_glib_nover_app_indicator_set_menu(IntPtr indicator, IntPtr menu);

    // =========================================================================
    // libayatana-appindicator3 (GTK3 variant - deprecated but widely available)
    // Package: libayatana-appindicator-gtk3 on Arch
    // Shows deprecation warning but still works
    // =========================================================================
    [DllImport("libayatana-appindicator3.so.1", EntryPoint = "app_indicator_new")]
    private static extern IntPtr ayatana_gtk3_app_indicator_new(string id, string iconName, int category);
    
    [DllImport("libayatana-appindicator3.so.1", EntryPoint = "app_indicator_set_status")]
    private static extern void ayatana_gtk3_app_indicator_set_status(IntPtr indicator, int status);
    
    [DllImport("libayatana-appindicator3.so.1", EntryPoint = "app_indicator_set_menu")]
    private static extern void ayatana_gtk3_app_indicator_set_menu(IntPtr indicator, IntPtr menu);

    // =========================================================================
    // Legacy libappindicator3 (Ubuntu/older distros)
    // =========================================================================
    [DllImport("libappindicator3.so.1", EntryPoint = "app_indicator_new")]
    private static extern IntPtr legacy_gtk3_app_indicator_new(string id, string iconName, int category);
    
    [DllImport("libappindicator3.so.1", EntryPoint = "app_indicator_set_status")]
    private static extern void legacy_gtk3_app_indicator_set_status(IntPtr indicator, int status);
    
    [DllImport("libappindicator3.so.1", EntryPoint = "app_indicator_set_menu")]
    private static extern void legacy_gtk3_app_indicator_set_menu(IntPtr indicator, IntPtr menu);

    #endregion
}
