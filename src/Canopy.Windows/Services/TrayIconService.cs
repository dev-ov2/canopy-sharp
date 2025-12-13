using Canopy.Core.Platform;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using System.Runtime.InteropServices;

namespace Canopy.Windows.Services;

/// <summary>
/// Windows system tray icon service using H.NotifyIcon
/// </summary>
public class TrayIconService : ITrayIconService
{
    private TrayIcon? _trayIcon;
    private PopupMenu? _menu;
    private bool _isDisposed;
    private nint _iconHandle;

    public event EventHandler? ShowWindowRequested;
    public event EventHandler? RescanGamesRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? QuitRequested;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);
    
    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(nint hIcon);

    public void Initialize()
    {
        try
        {
            _trayIcon = new TrayIcon
            {
                ToolTip = "Canopy - Game Overlay"
            };

            // Load icon
            LoadIcon();

            // Create context menu
            CreateContextMenu();

            // Handle tray icon events
            _trayIcon.MessageWindow.MouseEventReceived += (_, args) =>
            {
                if (args.MouseEvent == MouseEvent.IconRightMouseUp)
                {
                    var hwnd = _trayIcon.MessageWindow.Handle;
                    GetCursorPos(out var pos);
                    _menu?.Show(hwnd, pos.X, pos.Y);
                }
                else if (args.MouseEvent == MouseEvent.IconLeftMouseUp ||
                         args.MouseEvent == MouseEvent.IconLeftDoubleClick)
                {
                    ShowWindowRequested?.Invoke(this, EventArgs.Empty);
                }
            };

            _trayIcon.Create();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Tray icon initialization failed: {ex.Message}");
        }
    }

    private void LoadIcon()
    {
        if (_trayIcon == null) return;

        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "canopy.ico");
            var pngPath = Path.Combine(AppContext.BaseDirectory, "Assets", "canopy.png");
            
            if (File.Exists(iconPath))
            {
                var icon = new System.Drawing.Icon(iconPath);
                _iconHandle = icon.Handle;
                _trayIcon.Icon = _iconHandle;
            }
            else if (File.Exists(pngPath))
            {
                using var bitmap = new System.Drawing.Bitmap(pngPath);
                _iconHandle = bitmap.GetHicon();
                _trayIcon.Icon = _iconHandle;
            }
            else
            {
                _trayIcon.Icon = System.Drawing.SystemIcons.Application.Handle;
            }
        }
        catch
        {
            _trayIcon.Icon = System.Drawing.SystemIcons.Application.Handle;
        }
    }

    private void CreateContextMenu()
    {
        _menu = new PopupMenu();

        _menu.Items.Add(new PopupMenuItem("Open Canopy", (_, _) =>
            ShowWindowRequested?.Invoke(this, EventArgs.Empty)));

        _menu.Items.Add(new PopupMenuSeparator());

        _menu.Items.Add(new PopupMenuItem("Rescan Games", (_, _) =>
            RescanGamesRequested?.Invoke(this, EventArgs.Empty)));

        _menu.Items.Add(new PopupMenuItem("Settings", (_, _) =>
            SettingsRequested?.Invoke(this, EventArgs.Empty)));

        _menu.Items.Add(new PopupMenuSeparator());

        _menu.Items.Add(new PopupMenuItem("Quit", (_, _) =>
            QuitRequested?.Invoke(this, EventArgs.Empty)));
    }

    public void ShowBalloon(string title, string message)
    {
        try
        {
            _trayIcon?.ShowNotification(title, message);
        }
        catch
        {
            // Notification may fail
        }
    }

    public void SetTooltip(string text)
    {
        _trayIcon?.UpdateToolTip(text);
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _trayIcon?.Dispose();
        _trayIcon = null;
        
        if (_iconHandle != 0)
        {
            DestroyIcon(_iconHandle);
            _iconHandle = 0;
        }
    }
}
