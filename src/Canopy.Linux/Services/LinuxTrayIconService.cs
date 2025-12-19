using Canopy.Core.Logging;
using Canopy.Core.Platform;
using Gtk;

namespace Canopy.Linux.Services;

/// <summary>
/// Linux system tray icon service using GTK StatusIcon or AppIndicator
/// </summary>
public class LinuxTrayIconService : ITrayIconService
{
    private readonly ICanopyLogger _logger;
    private StatusIcon? _statusIcon;
    private Menu? _contextMenu;
    private bool _isDisposed;

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
            // Create status icon
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "canopy.png");
            
            if (File.Exists(iconPath))
            {
                _statusIcon = new StatusIcon(new Gdk.Pixbuf(iconPath));
            }
            else
            {
                _statusIcon = StatusIcon.NewFromIconName("applications-games");
            }

            if (_statusIcon != null)
            {
                _statusIcon.Visible = true;
                _statusIcon.TooltipText = "Canopy - Game Overlay";

                // Create context menu
                CreateContextMenu();

                // Handle events
                _statusIcon.Activate += (_, _) => ShowWindowRequested?.Invoke(this, EventArgs.Empty);
                _statusIcon.PopupMenu += OnPopupMenu;

                _logger.Info("Tray icon initialized");
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to initialize tray icon", ex);
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
            // Use notify-send for notifications on Linux
            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
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
        if (_statusIcon != null)
        {
            Application.Invoke((_, _) =>
            {
                _statusIcon.TooltipText = text;
            });
        }
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

        _contextMenu?.Dispose();
        _contextMenu = null;

        _logger.Info("Tray icon disposed");
    }
}
