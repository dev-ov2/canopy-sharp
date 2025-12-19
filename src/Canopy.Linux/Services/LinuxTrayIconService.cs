using System.Diagnostics;
using System.Runtime.InteropServices;
using Canopy.Core.Logging;
using Canopy.Core.Platform;
using Gtk;

namespace Canopy.Linux.Services;

/// <summary>
/// Linux system tray icon service.
/// On modern Linux desktops, the "system tray" is typically provided by:
/// - KDE: Built-in StatusNotifierItem support
/// - GNOME: Requires "AppIndicator and KStatusNotifierItem Support" extension
/// - Other DEs: Usually have native StatusIcon or AppIndicator support
/// </summary>
public class LinuxTrayIconService : ITrayIconService
{
    private readonly ICanopyLogger _logger;
    private Menu? _contextMenu;
    private bool _isDisposed;
    private bool _initialized;
    
    // StatusIcon for tray
    private StatusIcon? _statusIcon;
    
    // AppIndicator handle (alternative)
    private IntPtr _appIndicator = IntPtr.Zero;
    private bool _usingAppIndicator;

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

        // Delay initialization to ensure GTK is fully ready
        GLib.Idle.Add(() =>
        {
            InitializeInternal();
            return false; // Don't repeat
        });
    }

    private void InitializeInternal()
    {
        try
        {
            _logger.Info("Initializing tray icon...");
            
            // Create context menu first
            CreateContextMenu();

            // Ensure icon exists in proper location
            EnsureIconInstalled();

            // Try AppIndicator first (more reliable on modern desktops)
            if (TryInitializeAppIndicator())
            {
                _usingAppIndicator = true;
                _logger.Info("Tray icon initialized using AppIndicator");
                return;
            }

            // Fallback to StatusIcon
            if (TryInitializeStatusIcon())
            {
                _logger.Info("Tray icon initialized using StatusIcon");
                return;
            }
            
            _logger.Warning("No tray icon backend available");
            _logger.Warning("On GNOME, install: gnome-shell-extension-appindicator");
            _logger.Warning("On KDE, tray should work out of the box");
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to initialize tray icon", ex);
        }
    }

    private void EnsureIconInstalled()
    {
        try
        {
            var sourceIcon = Path.Combine(AppContext.BaseDirectory, "Assets", "canopy.png");
            
            // If source doesn't exist, create a placeholder or use system icon
            if (!File.Exists(sourceIcon))
            {
                _logger.Warning($"Icon not found at {sourceIcon}");
                return;
            }

            // Install to user's icon directory for AppIndicator
            var userIconDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".local", "share", "icons", "hicolor", "256x256", "apps");
            
            Directory.CreateDirectory(userIconDir);
            
            var targetPath = Path.Combine(userIconDir, "canopy.png");
            if (!File.Exists(targetPath))
            {
                File.Copy(sourceIcon, targetPath, true);
                _logger.Debug($"Installed icon to {targetPath}");
            }

            // Also copy to pixmaps for some DEs
            var pixmapsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".local", "share", "pixmaps");
            
            Directory.CreateDirectory(pixmapsDir);
            
            var pixmapPath = Path.Combine(pixmapsDir, "canopy.png");
            if (!File.Exists(pixmapPath))
            {
                File.Copy(sourceIcon, pixmapPath, true);
                _logger.Debug($"Installed icon to {pixmapPath}");
            }
        }
        catch (Exception ex)
        {
            _logger.Warning($"Failed to install icon: {ex.Message}");
        }
    }

    private bool TryInitializeAppIndicator()
    {
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "canopy.png");
            
            // AppIndicator can use a full path or an icon name
            // Using full path is more reliable for custom icons
            string iconName;
            if (File.Exists(iconPath))
            {
                iconName = iconPath;
            }
            else
            {
                // Fall back to a system icon
                iconName = "applications-games";
            }

            _appIndicator = app_indicator_new("canopy", iconName, 0);

            if (_appIndicator == IntPtr.Zero)
            {
                _logger.Debug("app_indicator_new returned null");
                return false;
            }

            // Set status to Active (1)
            app_indicator_set_status(_appIndicator, 1);
            
            // Set the menu
            if (_contextMenu != null)
            {
                app_indicator_set_menu(_appIndicator, _contextMenu.Handle);
            }

            _logger.Debug("AppIndicator created successfully");
            return true;
        }
        catch (DllNotFoundException ex)
        {
            _logger.Debug($"AppIndicator library not available: {ex.Message}");
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

    private bool TryInitializeStatusIcon()
    {
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "canopy.png");
            
            if (File.Exists(iconPath))
            {
                try
                {
                    var pixbuf = new Gdk.Pixbuf(iconPath);
                    // Scale to 24x24 for tray
                    if (pixbuf.Width != 24 || pixbuf.Height != 24)
                    {
                        pixbuf = pixbuf.ScaleSimple(24, 24, Gdk.InterpType.Bilinear);
                    }
                    _statusIcon = new StatusIcon(pixbuf);
                }
                catch (Exception ex)
                {
                    _logger.Warning($"Failed to load icon from file: {ex.Message}");
                    _statusIcon = StatusIcon.NewFromIconName("applications-games");
                }
            }
            else
            {
                _statusIcon = StatusIcon.NewFromIconName("applications-games");
            }

            if (_statusIcon == null)
            {
                _logger.Warning("Failed to create StatusIcon");
                return false;
            }

            _statusIcon.Title = "Canopy";
            _statusIcon.TooltipText = "Canopy - Game Overlay";
            _statusIcon.Visible = true;

            // Connect events
            _statusIcon.Activate += OnStatusIconActivate;
            _statusIcon.PopupMenu += OnStatusIconPopupMenu;

            // Check embedded status after a delay
            GLib.Timeout.Add(2000, () =>
            {
                if (_statusIcon != null)
                {
                    if (_statusIcon.IsEmbedded)
                    {
                        _logger.Info("StatusIcon is embedded in system tray");
                    }
                    else
                    {
                        _logger.Warning("StatusIcon is not embedded - tray may not be visible");
                        _logger.Warning("Try installing a system tray extension for your desktop");
                    }
                }
                return false;
            });

            return true;
        }
        catch (Exception ex)
        {
            _logger.Warning($"StatusIcon initialization failed: {ex.Message}");
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
        GLib.Idle.Add(() =>
        {
            if (_statusIcon != null)
            {
                _statusIcon.TooltipText = text;
            }
            return false;
        });
    }

    private static string EscapeShellArg(string arg)
    {
        return arg.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("$", "\\$").Replace("`", "\\`");
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        GLib.Idle.Add(() =>
        {
            if (_statusIcon != null)
            {
                _statusIcon.Visible = false;
                _statusIcon.Dispose();
                _statusIcon = null;
            }

            _contextMenu?.Dispose();
            _contextMenu = null;
            
            return false;
        });

        _logger.Info("Tray icon disposed");
    }

    #region AppIndicator P/Invoke
    
    // Try multiple library names for different distros
    private static IntPtr app_indicator_new(string id, string iconName, int category)
    {
        // Try ayatana first (Arch, newer Ubuntu)
        try { return ayatana_app_indicator_new(id, iconName, category); }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
        
        // Try libappindicator3 (older Ubuntu)
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
