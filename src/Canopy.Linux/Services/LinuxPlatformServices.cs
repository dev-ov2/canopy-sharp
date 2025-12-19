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
    /// On Arch Linux, this requires:
    /// 1. A .desktop file with MimeType=x-scheme-handler/{protocol}
    /// 2. Registration with xdg-mime
    /// </summary>
    public void RegisterProtocol(string protocol)
    {
        try
        {
            var applicationsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".local", "share", "applications");
            
            Directory.CreateDirectory(applicationsDir);
            
            var execPath = Process.GetCurrentProcess().MainModule?.FileName 
                ?? Path.Combine(AppContext.BaseDirectory, "Canopy.Linux");
            
            // Use the main desktop file for protocol handling
            var desktopFilePath = Path.Combine(applicationsDir, "canopy.desktop");
            
            // Check if desktop file exists and has protocol handler
            bool needsUpdate = true;
            if (File.Exists(desktopFilePath))
            {
                var content = File.ReadAllText(desktopFilePath);
                if (content.Contains($"x-scheme-handler/{protocol}"))
                {
                    needsUpdate = false;
                    _logger.Debug($"Protocol {protocol} already registered");
                }
            }
            
            if (needsUpdate)
            {
                // Get icon path
                var iconPath = AppIconManager.GetIconPath() ?? "canopy";
                
                var desktopEntry = $"""
                    [Desktop Entry]
                    Version=1.0
                    Type=Application
                    Name=Canopy
                    GenericName=Game Overlay
                    Comment=Game overlay and tracking application
                    Exec="{execPath}" %u
                    Icon={iconPath}
                    Terminal=false
                    Categories=Game;Utility;
                    Keywords=games;overlay;tracking;
                    MimeType=x-scheme-handler/{protocol};
                    StartupWMClass=canopy
                    StartupNotify=true
                    """;
                
                File.WriteAllText(desktopFilePath, desktopEntry);
                _logger.Info($"Desktop entry updated with protocol handler: {desktopFilePath}");
            }
            
            // Register as default handler with xdg-mime
            try
            {
                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "xdg-mime",
                    Arguments = $"default canopy.desktop x-scheme-handler/{protocol}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                });
                process?.WaitForExit(5000);
                
                if (process?.ExitCode == 0)
                {
                    _logger.Info($"Registered as default handler for {protocol}://");
                }
                else
                {
                    _logger.Warning($"xdg-mime returned exit code: {process?.ExitCode}");
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"xdg-mime failed: {ex.Message}");
            }
            
            // Also update mimeapps.list directly (more reliable on some systems)
            try
            {
                var mimeappsPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".local", "share", "applications", "mimeapps.list");
                
                var mimeapps = File.Exists(mimeappsPath) 
                    ? File.ReadAllText(mimeappsPath) 
                    : "[Default Applications]\n";
                
                var mimeType = $"x-scheme-handler/{protocol}";
                var handler = "canopy.desktop";
                
                if (!mimeapps.Contains($"{mimeType}="))
                {
                    // Add to Default Applications section
                    if (mimeapps.Contains("[Default Applications]"))
                    {
                        mimeapps = mimeapps.Replace(
                            "[Default Applications]",
                            $"[Default Applications]\n{mimeType}={handler}");
                    }
                    else
                    {
                        mimeapps = $"[Default Applications]\n{mimeType}={handler}\n" + mimeapps;
                    }
                    
                    File.WriteAllText(mimeappsPath, mimeapps);
                    _logger.Debug($"Updated mimeapps.list");
                }
            }
            catch (Exception ex)
            {
                _logger.Debug($"Could not update mimeapps.list: {ex.Message}");
            }
            
            // Update desktop database
            try
            {
                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "update-desktop-database",
                    Arguments = applicationsDir,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                });
                process?.WaitForExit(5000);
            }
            catch { }
            
            _logger.Info($"Protocol handler registration complete: {protocol}://");
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
