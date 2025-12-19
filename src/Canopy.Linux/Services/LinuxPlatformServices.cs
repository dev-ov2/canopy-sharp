using System.Diagnostics;
using Canopy.Core.Logging;
using Canopy.Core.Platform;

namespace Canopy.Linux.Services;

/// <summary>
/// Linux-specific platform services implementation
/// </summary>
public class LinuxPlatformServices : IPlatformServices
{
    private readonly ICanopyLogger _logger;
    private const string AppName = "canopy";
    private const string DesktopFileName = "canopy.desktop";

    public LinuxPlatformServices()
    {
        _logger = CanopyLoggerFactory.CreateLogger<LinuxPlatformServices>();
    }

    #region Auto-Start

    public Task SetAutoStartAsync(bool enabled, bool startOpen)
    {
        try
        {
            var autostartDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config", "autostart");
            
            Directory.CreateDirectory(autostartDir);
            var desktopFilePath = Path.Combine(autostartDir, DesktopFileName);

            if (enabled)
            {
                var execPath = Process.GetCurrentProcess().MainModule?.FileName ?? "canopy";
                var args = startOpen ? "" : " --minimized";
                
                var desktopEntry = $"""
                    [Desktop Entry]
                    Type=Application
                    Name=Canopy
                    Comment=Game overlay and tracking application
                    Exec={execPath}{args}
                    Icon=canopy
                    Terminal=false
                    Categories=Game;Utility;
                    X-GNOME-Autostart-enabled=true
                    """;
                
                File.WriteAllText(desktopFilePath, desktopEntry);
                _logger.Info($"Registered for autostart: {desktopFilePath}");
            }
            else
            {
                if (File.Exists(desktopFilePath))
                {
                    File.Delete(desktopFilePath);
                    _logger.Info("Unregistered from autostart");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to set autostart", ex);
        }

        return Task.CompletedTask;
    }

    public Task<bool> IsAutoStartEnabledAsync()
    {
        try
        {
            var autostartPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config", "autostart", DesktopFileName);
            
            return Task.FromResult(File.Exists(autostartPath));
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    #endregion

    #region Protocol Registration

    /// <summary>
    /// Registers a custom URL protocol handler (e.g., canopy://)
    /// </summary>
    public void RegisterProtocol(string protocol)
    {
        try
        {
            var applicationsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".local", "share", "applications");
            
            Directory.CreateDirectory(applicationsDir);
            
            var execPath = Process.GetCurrentProcess().MainModule?.FileName ?? "canopy";
            var desktopFilePath = Path.Combine(applicationsDir, $"{protocol}-handler.desktop");
            
            var desktopEntry = $"""
                [Desktop Entry]
                Type=Application
                Name=Canopy Protocol Handler
                Exec={execPath} %u
                Terminal=false
                NoDisplay=true
                MimeType=x-scheme-handler/{protocol};
                """;
            
            File.WriteAllText(desktopFilePath, desktopEntry);
            
            // Register with xdg-mime
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "xdg-mime",
                Arguments = $"default {protocol}-handler.desktop x-scheme-handler/{protocol}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            process?.WaitForExit();
            
            _logger.Info($"Registered protocol handler: {protocol}");
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to register protocol: {protocol}", ex);
        }
    }

    #endregion

    #region URL/Path Opening

    public void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "xdg-open",
                Arguments = url,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            _logger.Debug($"Opened URL: {url}");
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to open URL: {url}", ex);
        }
    }

    public void OpenPath(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "xdg-open",
                Arguments = $"\"{path}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            _logger.Debug($"Opened path: {path}");
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to open path: {path}", ex);
        }
    }

    #endregion

    #region Screen Info

    public (int Width, int Height) GetPrimaryScreenSize()
    {
        try
        {
            var screen = Gdk.Screen.Default;
            if (screen != null)
            {
                return (screen.Width, screen.Height);
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to get primary screen size", ex);
        }

        return (1920, 1080); // Fallback
    }

    public IReadOnlyList<ScreenInfo> GetAllScreens()
    {
        var screens = new List<ScreenInfo>();

        try
        {
            var screen = Gdk.Screen.Default;
            if (screen != null)
            {
                var numMonitors = screen.NMonitors;
                for (int i = 0; i < numMonitors; i++)
                {
                    var geometry = screen.GetMonitorGeometry(i);
                    screens.Add(new ScreenInfo
                    {
                        DeviceName = $"Monitor {i}",
                        X = geometry.X,
                        Y = geometry.Y,
                        Width = geometry.Width,
                        Height = geometry.Height,
                        IsPrimary = i == screen.PrimaryMonitor,
                        ScaleFactor = screen.GetMonitorScaleFactor(i)
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to get all screens", ex);
        }

        if (screens.Count == 0)
        {
            var (width, height) = GetPrimaryScreenSize();
            screens.Add(new ScreenInfo
            {
                DeviceName = "Primary",
                X = 0,
                Y = 0,
                Width = width,
                Height = height,
                IsPrimary = true,
                ScaleFactor = 1.0
            });
        }

        return screens.AsReadOnly();
    }

    #endregion
}
