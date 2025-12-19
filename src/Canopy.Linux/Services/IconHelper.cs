using Canopy.Core.Logging;
using System.Diagnostics;

namespace Canopy.Linux.Services;

/// <summary>
/// Centralized helper for managing application icons on Linux.
/// Handles icon discovery, installation to system directories, and loading.
/// 
/// For Arch Linux / CachyOS with AppIndicator:
/// - Icons must be in a valid icon theme directory with index.theme
/// - Or use app_indicator_set_icon_theme_path to specify custom path
/// </summary>
public static class AppIconManager
{
    private static ICanopyLogger? _logger;
    private static string? _cachedIconPath;
    private static string? _installedIconDir;
    private static bool _initialized;

    private static ICanopyLogger Logger => _logger ??= CanopyLoggerFactory.CreateLogger<LinuxTrayIconService>();

    /// <summary>
    /// The icon name to use with AppIndicator (without extension or path)
    /// </summary>
    public const string IconName = "canopy";

    /// <summary>
    /// Gets the directory where icons are installed for AppIndicator.
    /// This is needed for app_indicator_set_icon_theme_path.
    /// </summary>
    public static string? InstalledIconDirectory => _installedIconDir;

    /// <summary>
    /// Initialize icons - call early in app startup.
    /// </summary>
    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        Logger.Info("Initializing icon system...");
        
        // Find source icon
        var sourcePath = FindSourceIcon();
        if (sourcePath == null)
        {
            Logger.Warning("No source icon found!");
            return;
        }

        Logger.Info($"Found source icon: {sourcePath}");
        _cachedIconPath = sourcePath;

        // Install icons to user directory
        InstallIcons(sourcePath);

        // Set default window icon
        SetDefaultWindowIcon(sourcePath);
    }

    /// <summary>
    /// Finds the source icon file.
    /// </summary>
    private static string? FindSourceIcon()
    {
        var searchPaths = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Assets", "canopy.png"),
            Path.Combine(AppContext.BaseDirectory, "canopy.png"),
            Path.Combine(Directory.GetCurrentDirectory(), "Assets", "canopy.png"),
        };

        foreach (var path in searchPaths)
        {
            Logger.Debug($"Checking for icon at: {path}");
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    /// <summary>
    /// Gets the path to the application icon.
    /// </summary>
    public static string? GetIconPath() => _cachedIconPath;

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
    /// Installs icons to the user's icon directory with proper theme structure.
    /// </summary>
    private static void InstallIcons(string sourcePath)
    {
        try
        {
            var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var baseIconDir = Path.Combine(homeDir, ".local", "share", "icons");
            var themeDir = Path.Combine(baseIconDir, "hicolor");
            
            // Load source icon
            Gdk.Pixbuf sourcePixbuf;
            try
            {
                sourcePixbuf = new Gdk.Pixbuf(sourcePath);
                Logger.Debug($"Source icon size: {sourcePixbuf.Width}x{sourcePixbuf.Height}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to load source icon: {ex.Message}");
                return;
            }

            // Install to hicolor theme at various sizes
            int[] sizes = { 16, 22, 24, 32, 48, 64, 96, 128, 256, 512 };
            
            foreach (var size in sizes)
            {
                try
                {
                    var sizeDir = Path.Combine(themeDir, $"{size}x{size}", "apps");
                    Directory.CreateDirectory(sizeDir);
                    
                    var targetPath = Path.Combine(sizeDir, $"{IconName}.png");
                    var scaled = sourcePixbuf.ScaleSimple(size, size, Gdk.InterpType.Bilinear);
                    scaled?.Save(targetPath, "png");
                    
                    Logger.Debug($"Installed {size}x{size} icon to {targetPath}");
                }
                catch (Exception ex)
                {
                    Logger.Warning($"Failed to install {size}x{size} icon: {ex.Message}");
                }
            }

            // Also create a scalable directory with the original
            try
            {
                var scalableDir = Path.Combine(themeDir, "scalable", "apps");
                Directory.CreateDirectory(scalableDir);
                File.Copy(sourcePath, Path.Combine(scalableDir, $"{IconName}.png"), true);
            }
            catch { }

            // Install to pixmaps as fallback
            try
            {
                var pixmapsDir = Path.Combine(homeDir, ".local", "share", "pixmaps");
                Directory.CreateDirectory(pixmapsDir);
                File.Copy(sourcePath, Path.Combine(pixmapsDir, $"{IconName}.png"), true);
                Logger.Debug("Installed to pixmaps");
            }
            catch { }

            // Create/update index.theme if it doesn't exist (required for GTK to recognize the theme)
            EnsureIndexTheme(themeDir);

            // Update icon cache
            UpdateIconCache(themeDir);

            // Store the base icon directory for AppIndicator
            _installedIconDir = baseIconDir;
            
            Logger.Info($"Icons installed to {themeDir}");
        }
        catch (Exception ex)
        {
            Logger.Error($"Failed to install icons: {ex.Message}");
        }
    }

    /// <summary>
    /// Ensures the hicolor theme has an index.theme file.
    /// </summary>
    private static void EnsureIndexTheme(string themeDir)
    {
        var indexPath = Path.Combine(themeDir, "index.theme");
        if (File.Exists(indexPath))
        {
            Logger.Debug("index.theme already exists");
            return;
        }

        try
        {
            // Create a basic index.theme file
            var content = @"[Icon Theme]
Name=hicolor
Comment=Hicolor Icon Theme
Directories=16x16/apps,22x22/apps,24x24/apps,32x32/apps,48x48/apps,64x64/apps,96x96/apps,128x128/apps,256x256/apps,512x512/apps,scalable/apps

[16x16/apps]
Size=16
Context=Applications
Type=Fixed

[22x22/apps]
Size=22
Context=Applications
Type=Fixed

[24x24/apps]
Size=24
Context=Applications
Type=Fixed

[32x32/apps]
Size=32
Context=Applications
Type=Fixed

[48x48/apps]
Size=48
Context=Applications
Type=Fixed

[64x64/apps]
Size=64
Context=Applications
Type=Fixed

[96x96/apps]
Size=96
Context=Applications
Type=Fixed

[128x128/apps]
Size=128
Context=Applications
Type=Fixed

[256x256/apps]
Size=256
Context=Applications
Type=Fixed

[512x512/apps]
Size=512
Context=Applications
Type=Fixed

[scalable/apps]
Size=256
Context=Applications
Type=Scalable
MinSize=16
MaxSize=512
";
            File.WriteAllText(indexPath, content);
            Logger.Debug("Created index.theme");
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to create index.theme: {ex.Message}");
        }
    }

    /// <summary>
    /// Updates the GTK icon cache.
    /// </summary>
    private static void UpdateIconCache(string themeDir)
    {
        try
        {
            // Try gtk-update-icon-cache
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "gtk-update-icon-cache",
                    Arguments = $"-f -t \"{themeDir}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            process.Start();
            var completed = process.WaitForExit(5000);
            
            if (completed && process.ExitCode == 0)
            {
                Logger.Debug("Icon cache updated via gtk-update-icon-cache");
            }
            else
            {
                // Try gtk4 variant
                try
                {
                    process = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = "gtk4-update-icon-cache",
                            Arguments = $"-f -t \"{themeDir}\"",
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
            }
        }
        catch (Exception ex)
        {
            Logger.Debug($"Icon cache update failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Sets the default window icon for all GTK windows.
    /// </summary>
    private static void SetDefaultWindowIcon(string iconPath)
    {
        try
        {
            // Method 1: Set from file directly
            Gtk.Window.SetDefaultIconFromFile(iconPath);
            Logger.Debug("Default window icon set from file");
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to set window icon from file: {ex.Message}");
            
            // Method 2: Try loading as pixbuf
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

    /// <summary>
    /// Adds the custom icon directory to GTK's icon search path.
    /// Call this after GTK is initialized.
    /// </summary>
    public static void AddToIconTheme()
    {
        if (_installedIconDir == null) return;

        try
        {
            var theme = Gtk.IconTheme.Default;
            theme.PrependSearchPath(_installedIconDir);
            Logger.Debug($"Added {_installedIconDir} to icon theme search path");
        }
        catch (Exception ex)
        {
            Logger.Warning($"Failed to add icon search path: {ex.Message}");
        }
    }
}
