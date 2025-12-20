using Canopy.Core.Logging;
using System.Diagnostics;

namespace Canopy.Linux.Services;

/// <summary>
/// Centralized helper for managing application icons on Linux.
/// Handles icon discovery, loading, and desktop integration.
/// </summary>
public static class AppIconManager
{
    private static ICanopyLogger? _logger;
    private static string? _cachedIconPath;
    private static bool _initialized;

    private static ICanopyLogger Logger => _logger ??= CanopyLoggerFactory.CreateLogger<LinuxTrayIconService>();

    /// <summary>
    /// The icon name (for theme lookups)
    /// </summary>
    public const string IconName = "canopy";

    /// <summary>
    /// Initialize icons - call early in app startup.
    /// </summary>
    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        Logger.Info("Initializing icon system...");
        
        // Find source icon
        var sourcePath = FindIconFile();
        if (sourcePath == null)
        {
            Logger.Warning("No icon file found!");
            return;
        }

        Logger.Info($"Found icon: {sourcePath}");
        _cachedIconPath = sourcePath;

        // Set default window icon (for all GTK windows)
        SetDefaultWindowIcons(sourcePath);
        
        // Install icon to user's icon theme for desktop integration
        InstallIconToTheme(sourcePath);
        
        // Create/update desktop entry for proper taskbar integration
        InstallDesktopEntry(sourcePath);
    }

    /// <summary>
    /// Finds the icon file in various locations.
    /// </summary>
    private static string? FindIconFile()
    {
        var searchPaths = new[]
        {
            // In Assets folder (development and published)
            Path.Combine(AppContext.BaseDirectory, "Assets", "canopy.png"),
            // Next to executable
            Path.Combine(AppContext.BaseDirectory, "canopy.png"),
            // Current directory
            Path.Combine(Directory.GetCurrentDirectory(), "Assets", "canopy.png"),
            Path.Combine(Directory.GetCurrentDirectory(), "canopy.png"),
            // Installed locations
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), 
                ".local", "share", "icons", "hicolor", "256x256", "apps", "canopy.png"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), 
                ".local", "share", "pixmaps", "canopy.png"),
        };

        foreach (var path in searchPaths)
        {
            Logger.Debug($"Checking: {path}");
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    /// <summary>
    /// Gets the absolute path to the application icon.
    /// </summary>
    public static string? GetIconPath()
    {
        if (_cachedIconPath != null && File.Exists(_cachedIconPath))
            return _cachedIconPath;
        
        // Try to find it again
        _cachedIconPath = FindIconFile();
        return _cachedIconPath;
    }

    /// <summary>
    /// Gets a Pixbuf of the application icon at the specified size.
    /// </summary>
    public static Gdk.Pixbuf? GetPixbuf(int size = 256)
    {
        try
        {
            var iconPath = GetIconPath();
            if (iconPath == null)
            {
                return LoadFromTheme("applications-games", size);
            }

            var original = new Gdk.Pixbuf(iconPath);
            if (original.Width != size || original.Height != size)
            {
                return original.ScaleSimple(size, size, Gdk.InterpType.Bilinear);
            }
            return original;
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to load icon pixbuf: {ex.Message}");
            return LoadFromTheme("applications-games", size);
        }
    }

    private static Gdk.Pixbuf? LoadFromTheme(string iconName, int size)
    {
        try
        {
            var theme = Gtk.IconTheme.Default;
            return theme.LoadIcon(iconName, size, Gtk.IconLookupFlags.UseBuiltin);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Sets the default window icons for all GTK windows using multiple sizes.
    /// </summary>
    private static void SetDefaultWindowIcons(string iconPath)
    {
        try
        {
            var original = new Gdk.Pixbuf(iconPath);
            var sizes = new[] { 16, 32, 48, 64, 128, 256 };
            var iconList = new List<Gdk.Pixbuf>();
            
            foreach (var size in sizes)
            {
                var scaled = original.ScaleSimple(size, size, Gdk.InterpType.Bilinear);
                if (scaled != null)
                {
                    iconList.Add(scaled);
                }
            }

            if (iconList.Count > 0)
            {
                Gtk.Window.DefaultIconList = iconList.ToArray();
                Logger.Debug($"Default window icons set with {iconList.Count} sizes");
            }
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to set default window icons: {ex.Message}");
            
            // Fallback to single file
            try
            {
                Gtk.Window.SetDefaultIconFromFile(iconPath);
            }
            catch { }
        }
    }

    /// <summary>
    /// Installs the icon to the user's hicolor icon theme.
    /// </summary>
    private static void InstallIconToTheme(string sourcePath)
    {
        try
        {
            var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var iconThemeDir = Path.Combine(homeDir, ".local", "share", "icons", "hicolor");
            
            var original = new Gdk.Pixbuf(sourcePath);
            var sizes = new[] { 16, 22, 24, 32, 48, 64, 128, 256, 512 };
            
            foreach (var size in sizes)
            {
                try
                {
                    var sizeDir = Path.Combine(iconThemeDir, $"{size}x{size}", "apps");
                    Directory.CreateDirectory(sizeDir);
                    
                    var targetPath = Path.Combine(sizeDir, $"{IconName}.png");
                    var scaled = original.ScaleSimple(size, size, Gdk.InterpType.Bilinear);
                    scaled?.Save(targetPath, "png");
                }
                catch { }
            }

            // Update icon cache (don't wait for it)
            Task.Run(() =>
            {
                try
                {
                    var process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "gtk-update-icon-cache",
                            Arguments = $"-f -t \"{iconThemeDir}\"",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true
                        }
                    };
                    process.Start();
                    process.WaitForExit(5000);
                }
                catch { }
            });

            Logger.Debug("Icons installed to hicolor theme");
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to install icons to theme: {ex.Message}");
        }
    }

    /// <summary>
    /// Creates/updates the desktop entry for proper taskbar/dock integration.
    /// The StartupWMClass must match what we set with g_set_prgname().
    /// </summary>
    private static void InstallDesktopEntry(string iconPath)
    {
        try
        {
            var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var applicationsDir = Path.Combine(homeDir, ".local", "share", "applications");
            Directory.CreateDirectory(applicationsDir);
            
            var desktopEntryPath = Path.Combine(applicationsDir, "canopy.desktop");
            var execPath = Path.Combine(AppContext.BaseDirectory, "Canopy.Linux");
            
            // Use absolute path to icon for reliability
            // Also install to theme, but use absolute path as primary
            var iconRef = iconPath; // Absolute path is most reliable
            
            var desktopEntry = $@"[Desktop Entry]
Version=1.0
Type=Application
Name=Canopy
GenericName=Game Overlay
Comment=Game overlay and tracking application
Exec=""{execPath}"" %u
Icon={iconRef}
Terminal=false
Categories=Game;Utility;
Keywords=games;overlay;tracking;
MimeType=x-scheme-handler/canopy;
StartupWMClass=canopy
StartupNotify=true
";

            File.WriteAllText(desktopEntryPath, desktopEntry);
            Logger.Debug($"Desktop entry installed: {desktopEntryPath}");
            Logger.Debug($"Desktop entry Icon={iconRef}");
            Logger.Debug($"Desktop entry StartupWMClass=canopy");

            // Update desktop database
            Task.Run(() =>
            {
                try
                {
                    var process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "update-desktop-database",
                            Arguments = $"\"{applicationsDir}\"",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true
                        }
                    };
                    process.Start();
                    process.WaitForExit(5000);
                }
                catch { }
            });
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to install desktop entry: {ex.Message}");
        }
    }
}
