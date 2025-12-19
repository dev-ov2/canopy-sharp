using Canopy.Core.Logging;
using System.Diagnostics;

namespace Canopy.Linux.Services;

/// <summary>
/// Centralized helper for managing application icons on Linux.
/// Handles icon discovery, installation to system directories, and loading.
/// </summary>
public static class AppIconManager
{
    private static ICanopyLogger? _logger;
    private static string? _cachedIconPath;
    private static bool _iconsInstalled;

    private static ICanopyLogger Logger => _logger ??= CanopyLoggerFactory.CreateLogger<LinuxTrayIconService>();

    /// <summary>
    /// Gets the path to the application icon, or null if not found.
    /// </summary>
    public static string? GetIconPath()
    {
        if (_cachedIconPath != null && File.Exists(_cachedIconPath))
            return _cachedIconPath;

        // Try multiple locations
        var possiblePaths = new[]
        {
            // Development: source directory
            Path.Combine(AppContext.BaseDirectory, "Assets", "canopy.png"),
            // Installed: same directory as executable
            Path.Combine(AppContext.BaseDirectory, "canopy.png"),
            // Installed to user icons
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), 
                ".local", "share", "icons", "hicolor", "256x256", "apps", "canopy.png"),
            // System-wide
            "/usr/share/icons/hicolor/256x256/apps/canopy.png",
            "/usr/share/pixmaps/canopy.png",
        };

        foreach (var path in possiblePaths)
        {
            if (File.Exists(path))
            {
                _cachedIconPath = path;
                Logger.Debug($"Found icon at: {path}");
                return path;
            }
        }

        Logger.Warning("Could not find canopy.png in any expected location");
        Logger.Warning($"Searched: {string.Join(", ", possiblePaths)}");
        return null;
    }

    /// <summary>
    /// Gets a Pixbuf of the application icon at the specified size.
    /// Returns null if icon cannot be loaded.
    /// </summary>
    public static Gdk.Pixbuf? GetPixbuf(int size = 256)
    {
        try
        {
            var iconPath = GetIconPath();
            if (iconPath == null)
            {
                // Try loading from icon theme as fallback
                return LoadFromTheme("applications-games", size);
            }

            // Load the original
            var original = new Gdk.Pixbuf(iconPath);
            
            // Scale if needed
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

    /// <summary>
    /// Loads an icon from the system icon theme.
    /// </summary>
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
    /// Installs the application icon to standard system locations.
    /// Returns the icon theme name to use with AppIndicator.
    /// </summary>
    public static string InstallToSystem()
    {
        const string iconThemeName = "canopy";
        
        if (_iconsInstalled)
            return iconThemeName;

        var sourcePath = GetIconPath();
        if (sourcePath == null)
        {
            Logger.Warning("No source icon found to install");
            return "applications-games"; // Fallback to system icon
        }

        try
        {
            Logger.Info($"Installing icons from: {sourcePath}");
            
            var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var iconThemeDir = Path.Combine(homeDir, ".local", "share", "icons", "hicolor");
            var pixmapsDir = Path.Combine(homeDir, ".local", "share", "pixmaps");

            // Load the source icon once
            Gdk.Pixbuf? sourcePixbuf = null;
            try
            {
                sourcePixbuf = new Gdk.Pixbuf(sourcePath);
                Logger.Debug($"Loaded source icon: {sourcePixbuf.Width}x{sourcePixbuf.Height}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to load source icon: {ex.Message}");
                return "applications-games";
            }

            // Install to various sizes
            int[] sizes = { 16, 22, 24, 32, 48, 64, 128, 256, 512 };
            
            foreach (var size in sizes)
            {
                try
                {
                    var sizeDir = Path.Combine(iconThemeDir, $"{size}x{size}", "apps");
                    Directory.CreateDirectory(sizeDir);
                    
                    var targetPath = Path.Combine(sizeDir, $"{iconThemeName}.png");
                    
                    // Scale and save
                    var scaled = sourcePixbuf.ScaleSimple(size, size, Gdk.InterpType.Bilinear);
                    if (scaled != null)
                    {
                        scaled.Save(targetPath, "png");
                        Logger.Debug($"Installed {size}x{size} icon");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning($"Failed to install {size}x{size} icon: {ex.Message}");
                }
            }

            // Also install to pixmaps (some older apps look here)
            try
            {
                Directory.CreateDirectory(pixmapsDir);
                var pixmapPath = Path.Combine(pixmapsDir, $"{iconThemeName}.png");
                File.Copy(sourcePath, pixmapPath, true);
                Logger.Debug($"Installed icon to pixmaps");
            }
            catch (Exception ex)
            {
                Logger.Warning($"Failed to install to pixmaps: {ex.Message}");
            }

            // Update icon cache (non-blocking)
            Task.Run(() => UpdateIconCache(iconThemeDir));

            _iconsInstalled = true;
            Logger.Info($"Icons installed as '{iconThemeName}'");
            return iconThemeName;
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to install icons: {ex.Message}");
            return "applications-games";
        }
    }

    /// <summary>
    /// Updates the GTK icon cache for the given directory.
    /// </summary>
    private static void UpdateIconCache(string iconThemeDir)
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
            process.WaitForExit(10000);
            
            if (process.ExitCode == 0)
            {
                Logger.Debug("Icon cache updated successfully");
            }
        }
        catch (Exception ex)
        {
            Logger.Debug($"Could not update icon cache: {ex.Message}");
        }
    }

    /// <summary>
    /// Sets the default icon for all GTK windows.
    /// Call this early in app startup.
    /// </summary>
    public static void SetDefaultWindowIcon()
    {
        try
        {
            var iconPath = GetIconPath();
            if (iconPath != null)
            {
                Gtk.Window.SetDefaultIconFromFile(iconPath);
                Logger.Debug("Default window icon set");
            }
            else
            {
                // Try theme icon
                var pixbuf = LoadFromTheme("applications-games", 256);
                if (pixbuf != null)
                {
                    Gtk.Window.DefaultIconList = new[] { pixbuf };
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to set default window icon: {ex.Message}");
        }
    }
}
