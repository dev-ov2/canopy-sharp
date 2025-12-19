using System.Diagnostics;
using System.Runtime.InteropServices;
using Canopy.Core.Logging;
using Canopy.Core.Platform;
using Gtk;

namespace Canopy.Linux.Services;

/// <summary>
/// Linux system tray icon service using libayatana-appindicator3 or fallback to StatusIcon
/// </summary>
public class LinuxTrayIconService : ITrayIconService
{
    private readonly ICanopyLogger _logger;
    private Menu? _contextMenu;
    private bool _isDisposed;
    
    // AppIndicator handle
    private IntPtr _appIndicator = IntPtr.Zero;
    private bool _useAppIndicator;
    
    // Fallback StatusIcon (for systems without AppIndicator)
    private StatusIcon? _statusIcon;

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
        try
        {
            // Try AppIndicator first (works on most modern desktops)
            if (TryInitializeAppIndicator())
            {
                _useAppIndicator = true;
                _logger.Info("Tray icon initialized using AppIndicator");
                return;
            }
            
            // Fallback to StatusIcon (legacy but works on some desktops)
            if (TryInitializeStatusIcon())
            {
                _useAppIndicator = false;
                _logger.Info("Tray icon initialized using StatusIcon");
                return;
            }
            
            _logger.Warning("No tray icon backend available");
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
            // Create context menu first (AppIndicator requires it)
            CreateContextMenu();

            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "canopy.png");
            
            // Try to create AppIndicator
            _appIndicator = app_indicator_new(
                "canopy-tray",
                File.Exists(iconPath) ? iconPath : "applications-games",
                AppIndicatorCategory.ApplicationStatus);

            if (_appIndicator == IntPtr.Zero)
            {
                _logger.Warning("app_indicator_new returned null");
                return false;
            }

            app_indicator_set_status(_appIndicator, AppIndicatorStatus.Active);
            app_indicator_set_title(_appIndicator, "Canopy");
            
            // Set the menu - AppIndicator requires a GTK menu
            if (_contextMenu != null)
            {
                app_indicator_set_menu(_appIndicator, _contextMenu.Handle);
            }

            return true;
        }
        catch (DllNotFoundException ex)
        {
            _logger.Debug($"AppIndicator library not found: {ex.Message}");
            return false;
        }
        catch (Exception ex)
        {
            _logger.Warning($"AppIndicator initialization failed: {ex.Message}");
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
                _statusIcon = new StatusIcon(new Gdk.Pixbuf(iconPath));
            }
            else
            {
                _statusIcon = StatusIcon.NewFromIconName("applications-games");
            }

            if (_statusIcon == null)
                return false;

            _statusIcon.Visible = true;
            _statusIcon.TooltipText = "Canopy - Game Overlay";

            // Create context menu if not already done
            if (_contextMenu == null)
                CreateContextMenu();

            // Handle events
            _statusIcon.Activate += (_, _) => ShowWindowRequested?.Invoke(this, EventArgs.Empty);
            _statusIcon.PopupMenu += OnPopupMenu;

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

    private void OnPopupMenu(object? sender, PopupMenuArgs args)
    {
        _contextMenu?.Popup();
    }

    public void ShowBalloon(string title, string message)
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "notify-send",
                    Arguments = $"-a Canopy \"{EscapeShellArg(title)}\" \"{EscapeShellArg(message)}\"",
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
            if (_useAppIndicator && _appIndicator != IntPtr.Zero)
            {
                app_indicator_set_title(_appIndicator, text);
            }
            else if (_statusIcon != null)
            {
                _statusIcon.TooltipText = text;
            }
        });
    }

    private static string EscapeShellArg(string arg)
    {
        return arg.Replace("\"", "\\\"").Replace("$", "\\$");
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

        // AppIndicator doesn't need explicit cleanup - it's tied to the process

        _contextMenu?.Dispose();
        _contextMenu = null;

        _logger.Info("Tray icon disposed");
    }

    #region libayatana-appindicator3 P/Invoke

    private enum AppIndicatorCategory
    {
        ApplicationStatus = 0,
        Communications = 1,
        SystemServices = 2,
        Hardware = 3,
        Other = 4
    }

    private enum AppIndicatorStatus
    {
        Passive = 0,
        Active = 1,
        Attention = 2
    }

    // Try ayatana first (newer, more common), then fall back to ubuntu's appindicator
    [DllImport("libayatana-appindicator3.so.1", EntryPoint = "app_indicator_new")]
    private static extern IntPtr app_indicator_new_ayatana(string id, string iconName, AppIndicatorCategory category);
    
    [DllImport("libayatana-appindicator3.so.1", EntryPoint = "app_indicator_set_status")]
    private static extern void app_indicator_set_status_ayatana(IntPtr indicator, AppIndicatorStatus status);
    
    [DllImport("libayatana-appindicator3.so.1", EntryPoint = "app_indicator_set_menu")]
    private static extern void app_indicator_set_menu_ayatana(IntPtr indicator, IntPtr menu);
    
    [DllImport("libayatana-appindicator3.so.1", EntryPoint = "app_indicator_set_title")]
    private static extern void app_indicator_set_title_ayatana(IntPtr indicator, string title);

    private static IntPtr app_indicator_new(string id, string iconName, AppIndicatorCategory category)
    {
        try
        {
            return app_indicator_new_ayatana(id, iconName, category);
        }
        catch (DllNotFoundException)
        {
            return app_indicator_new_ubuntu(id, iconName, category);
        }
    }

    private static void app_indicator_set_status(IntPtr indicator, AppIndicatorStatus status)
    {
        try
        {
            app_indicator_set_status_ayatana(indicator, status);
        }
        catch (DllNotFoundException)
        {
            app_indicator_set_status_ubuntu(indicator, status);
        }
    }

    private static void app_indicator_set_menu(IntPtr indicator, IntPtr menu)
    {
        try
        {
            app_indicator_set_menu_ayatana(indicator, menu);
        }
        catch (DllNotFoundException)
        {
            app_indicator_set_menu_ubuntu(indicator, menu);
        }
    }

    private static void app_indicator_set_title(IntPtr indicator, string title)
    {
        try
        {
            app_indicator_set_title_ayatana(indicator, title);
        }
        catch (DllNotFoundException)
        {
            app_indicator_set_title_ubuntu(indicator, title);
        }
    }

    // Ubuntu/older appindicator fallback
    [DllImport("libappindicator3.so.1", EntryPoint = "app_indicator_new")]
    private static extern IntPtr app_indicator_new_ubuntu(string id, string iconName, AppIndicatorCategory category);
    
    [DllImport("libappindicator3.so.1", EntryPoint = "app_indicator_set_status")]
    private static extern void app_indicator_set_status_ubuntu(IntPtr indicator, AppIndicatorStatus status);
    
    [DllImport("libappindicator3.so.1", EntryPoint = "app_indicator_set_menu")]
    private static extern void app_indicator_set_menu_ubuntu(IntPtr indicator, IntPtr menu);
    
    [DllImport("libappindicator3.so.1", EntryPoint = "app_indicator_set_title")]
    private static extern void app_indicator_set_title_ubuntu(IntPtr indicator, string title);

    #endregion
}
