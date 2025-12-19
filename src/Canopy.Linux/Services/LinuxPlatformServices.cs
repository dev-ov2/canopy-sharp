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

    /// <summary>
    /// Sets up autostart for Linux desktops.
    /// Works with:
    /// - GNOME (uses XDG autostart)
    /// - KDE Plasma (uses XDG autostart)
    /// - XFCE (uses XDG autostart)
    /// - Other XDG-compliant desktops
    /// </summary>
    public Task SetAutoStartAsync(bool enabled, bool startOpen)
    {
        try
        {
            // XDG autostart directory - works on most Linux desktops including Arch
            var autostartDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config", "autostart");
            
            Directory.CreateDirectory(autostartDir);
            var desktopFilePath = Path.Combine(autostartDir, DesktopFileName);

            if (enabled)
            {
                var execPath = GetExecutablePath();
                var args = startOpen ? "" : " --minimized";
                var iconPath = AppIconManager.GetIconPath() ?? "canopy";
                
                // Desktop entry format per XDG specification
                // https://specifications.freedesktop.org/autostart-spec/autostart-spec-latest.html
                var desktopEntry = $"""
                    [Desktop Entry]
                    Type=Application
                    Version=1.0
                    Name=Canopy
                    GenericName=Game Overlay
                    Comment=Game overlay and tracking application
                    Exec="{execPath}"{args}
                    Icon={iconPath}
                    Terminal=false
                    Categories=Game;Utility;
                    StartupWMClass=canopy
                    X-GNOME-Autostart-enabled=true
                    X-KDE-autostart-after=panel
                    Hidden=false
                    """;
                
                File.WriteAllText(desktopFilePath, desktopEntry);
                _logger.Info($"Autostart enabled: {desktopFilePath}");
                _logger.Debug($"Exec={execPath}{args}");
            }
            else
            {
                if (File.Exists(desktopFilePath))
                {
                    File.Delete(desktopFilePath);
                    _logger.Info("Autostart disabled");
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
            
            if (!File.Exists(autostartPath))
                return Task.FromResult(false);
            
            // Check if the entry is not hidden/disabled
            var content = File.ReadAllText(autostartPath);
            if (content.Contains("Hidden=true", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(false);
            if (content.Contains("X-GNOME-Autostart-enabled=false", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(false);
            
            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    /// <summary>
    /// Gets the path to the current executable.
    /// </summary>
    private static string GetExecutablePath()
    {
        // Try multiple methods to get the executable path
        
        // Method 1: Process.MainModule
        var mainModule = Process.GetCurrentProcess().MainModule?.FileName;
        if (!string.IsNullOrEmpty(mainModule) && File.Exists(mainModule))
        {
            return mainModule;
        }
        
        // Method 2: AppContext.BaseDirectory + known executable name
        var baseDir = AppContext.BaseDirectory;
        var exePath = Path.Combine(baseDir, "Canopy.Linux");
        if (File.Exists(exePath))
        {
            return exePath;
        }
        
        // Method 3: /proc/self/exe (Linux-specific)
        try
        {
            var procPath = "/proc/self/exe";
            if (File.Exists(procPath))
            {
                var realPath = new FileInfo(procPath).LinkTarget;
                if (!string.IsNullOrEmpty(realPath) && File.Exists(realPath))
                {
                    return realPath;
                }
            }
        }
        catch { }
        
        // Fallback
        return mainModule ?? Path.Combine(baseDir, "Canopy.Linux");
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
            
            var execPath = GetExecutablePath();
            var desktopFilePath = Path.Combine(applicationsDir, "canopy.desktop");
            var iconPath = AppIconManager.GetIconPath() ?? "canopy";
            
            // Always update the desktop file to ensure it has the protocol handler
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
            _logger.Info($"Desktop entry written: {desktopFilePath}");
            
            // Register as default handler with xdg-mime
            RunCommand("xdg-mime", $"default canopy.desktop x-scheme-handler/{protocol}");
            
            // Also update mimeapps.list directly
            UpdateMimeAppsList(protocol);
            
            // Update desktop database
            RunCommand("update-desktop-database", applicationsDir);
            
            _logger.Info($"Protocol handler registered: {protocol}://");
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to register protocol: {protocol}", ex);
        }
    }

    private void UpdateMimeAppsList(string protocol)
    {
        try
        {
            var mimeappsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".local", "share", "applications", "mimeapps.list");
            
            var lines = File.Exists(mimeappsPath) 
                ? File.ReadAllLines(mimeappsPath).ToList() 
                : new List<string>();
            
            var mimeType = $"x-scheme-handler/{protocol}=canopy.desktop";
            var inDefaultSection = false;
            var found = false;
            var defaultSectionIndex = -1;
            
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].Trim() == "[Default Applications]")
                {
                    inDefaultSection = true;
                    defaultSectionIndex = i;
                }
                else if (lines[i].StartsWith("["))
                {
                    inDefaultSection = false;
                }
                else if (inDefaultSection && lines[i].StartsWith($"x-scheme-handler/{protocol}="))
                {
                    lines[i] = mimeType;
                    found = true;
                    break;
                }
            }
            
            if (!found)
            {
                if (defaultSectionIndex >= 0)
                {
                    lines.Insert(defaultSectionIndex + 1, mimeType);
                }
                else
                {
                    lines.Insert(0, "[Default Applications]");
                    lines.Insert(1, mimeType);
                }
            }
            
            File.WriteAllLines(mimeappsPath, lines);
            _logger.Debug("Updated mimeapps.list");
        }
        catch (Exception ex)
        {
            _logger.Debug($"Could not update mimeapps.list: {ex.Message}");
        }
    }

    private void RunCommand(string command, string arguments)
    {
        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = command,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            });
            process?.WaitForExit(5000);
            
            if (process?.ExitCode == 0)
            {
                _logger.Debug($"Command succeeded: {command} {arguments}");
            }
            else
            {
                _logger.Debug($"Command failed ({process?.ExitCode}): {command} {arguments}");
            }
        }
        catch (Exception ex)
        {
            _logger.Debug($"Command error: {command} - {ex.Message}");
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

        return (1920, 1080);
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
