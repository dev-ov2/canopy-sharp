using Canopy.Core.Logging;

namespace Canopy.Linux.Services;

/// <summary>
/// Centralized helper for managing application icons on Linux.
/// Handles icon discovery and loading.
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

        // Set default window icon
        SetDefaultWindowIcon(sourcePath);
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
            // System locations
            "/usr/share/icons/hicolor/256x256/apps/canopy.png",
            "/usr/share/pixmaps/canopy.png",
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
    /// Sets the default window icon for all GTK windows.
    /// </summary>
    private static void SetDefaultWindowIcon(string iconPath)
    {
        try
        {
            Gtk.Window.SetDefaultIconFromFile(iconPath);
            Logger.Debug("Default window icon set");
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to set window icon: {ex.Message}");
            
            // Try loading as pixbuf
            try
            {
                var pixbuf = new Gdk.Pixbuf(iconPath);
                Gtk.Window.DefaultIconList = new[] { pixbuf };
                Logger.Debug("Default window icon set from pixbuf");
            }
            catch (Exception ex2)
            {
                Logger.Warning($"Failed to set window icon from pixbuf: {ex2.Message}");
            }
        }
    }
}
