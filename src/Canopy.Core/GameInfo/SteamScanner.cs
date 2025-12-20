using Canopy.Core.Logging;
using Canopy.Core.Models;
using Gameloop.Vdf;
using Gameloop.Vdf.Linq;

namespace Canopy.Core.GameDetection;

/// <summary>
/// Scans installed Steam games by parsing Steam's configuration files
/// Reads libraryfolders.vdf and appmanifest files
/// </summary>
public class SteamScanner : IGameScanner
{
    private readonly ICanopyLogger _logger;
    
    public static readonly string[] IgnoredPathElements = { "wallpaper_engine" };

    public GamePlatform Platform => GamePlatform.Steam;
    
    public bool IsAvailable => GetInstallPath() != null;

    public SteamScanner()
    {
        _logger = CanopyLoggerFactory.CreateLogger<SteamScanner>();
    }

    /// <summary>
    /// Gets the Steam installation path based on the current OS
    /// </summary>
    public string? GetInstallPath()
    {
        if (OperatingSystem.IsWindows())
        {
            return GetWindowsSteamPath();
        }
        else if (OperatingSystem.IsLinux())
        {
            return GetLinuxSteamPath();
        }
        else if (OperatingSystem.IsMacOS())
        {
            return GetMacSteamPath();
        }

        return null;
    }

    private string? GetWindowsSteamPath()
    {
        // Check common Steam installation paths on Windows
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var steamPath = Path.Combine(programFiles, "Steam");

        if (Directory.Exists(steamPath))
            return steamPath;

        // Check registry for Steam path
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
                var path = key?.GetValue("SteamPath") as string;
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                    return path;
            }
            catch (Exception ex)
            {
                _logger.Warning($"Failed to read Steam registry: {ex.Message}");
            }
        }

        var drives = new[] { "C:", "D:", "E:", "F:" };
        foreach (var drive in drives)
        {
            var testPath = Path.Combine(drive, "Program Files (x86)", "Steam");
            if (Directory.Exists(testPath))
                return testPath;

            testPath = Path.Combine(drive, "Steam");
            if (Directory.Exists(testPath))
                return testPath;
        }

        return null;
    }

    private static string? GetLinuxSteamPath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var paths = new[]
        {
            Path.Combine(home, ".steam", "steam"),
            Path.Combine(home, ".local", "share", "Steam"),
            Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".steam", "steam") // Flatpak
        };

        return paths.FirstOrDefault(Directory.Exists);
    }

    private static string? GetMacSteamPath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var path = Path.Combine(home, "Library", "Application Support", "Steam");
        return Directory.Exists(path) ? path : null;
    }

    public async Task<IReadOnlyList<DetectedGame>> DetectGamesAsync(CancellationToken cancellationToken = default)
    {
        var steamPath = GetInstallPath();
        if (steamPath == null)
        {
            _logger.Debug("Steam not found");
            return [];
        }

        _logger.Debug($"Steam path: {steamPath}");

        var games = new List<DetectedGame>();
        var libraryFolders = await GetLibraryFoldersAsync(steamPath, cancellationToken);
        
        _logger.Debug($"Found {libraryFolders.Count} Steam library folders");

        foreach (var libraryPath in libraryFolders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var libraryGames = await ScanLibraryFolderAsync(libraryPath, cancellationToken);
            games.AddRange(libraryGames);
        }

        _logger.Info($"Steam: Found {games.Count} games");
        return games.AsReadOnly();
    }

    /// <summary>
    /// Parses libraryfolders.vdf to get all Steam library locations
    /// </summary>
    private async Task<List<string>> GetLibraryFoldersAsync(string steamPath, CancellationToken cancellationToken)
    {
        var folders = new List<string>();
        var libraryFoldersPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");

        if (!File.Exists(libraryFoldersPath))
        {
            var mainApps = Path.Combine(steamPath, "steamapps");
            if (Directory.Exists(mainApps))
                folders.Add(mainApps);
            return folders;
        }

        try
        {
            var content = await File.ReadAllTextAsync(libraryFoldersPath, cancellationToken);
            var vdf = VdfConvert.Deserialize(content);
            var libraryFoldersNode = vdf.Value;

            if (libraryFoldersNode != null)
            {
                foreach (var child in libraryFoldersNode.Children())
                {
                    if (child is VProperty property && int.TryParse(property.Key, out _))
                    {
                        var pathValue = property.Value?["path"]?.Value<string>();
                        if (!string.IsNullOrEmpty(pathValue))
                        {
                            var steamAppsPath = Path.Combine(pathValue, "steamapps");
                            if (Directory.Exists(steamAppsPath))
                            {
                                folders.Add(steamAppsPath);
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Warning($"Failed to parse libraryfolders.vdf: {ex.Message}");
            var mainApps = Path.Combine(steamPath, "steamapps");
            if (Directory.Exists(mainApps))
                folders.Add(mainApps);
        }

        return folders;
    }

    /// <summary>
    /// Scans a Steam library folder for installed games by reading appmanifest files
    /// </summary>
    private async Task<List<DetectedGame>> ScanLibraryFolderAsync(string steamAppsPath, CancellationToken cancellationToken)
    {
        var games = new List<DetectedGame>();

        if (!Directory.Exists(steamAppsPath))
            return games;

        var manifestFiles = Directory.GetFiles(steamAppsPath, "appmanifest_*.acf");

        foreach (var manifestPath in manifestFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            try
            {
                var game = await ParseAppManifestAsync(manifestPath, steamAppsPath, cancellationToken);
                if (game != null)
                {
                    games.Add(game);
                }
            }
            catch (Exception ex)
            {
                _logger.Debug($"Failed to parse {manifestPath}: {ex.Message}");
            }
        }

        return games;
    }

    /// <summary>
    /// Parses an appmanifest_*.acf file to extract game information
    /// </summary>
    private async Task<DetectedGame?> ParseAppManifestAsync(string manifestPath, string steamAppsPath, CancellationToken cancellationToken)
    {
        var content = await File.ReadAllTextAsync(manifestPath, cancellationToken);
        var vdf = VdfConvert.Deserialize(content);
        var appState = vdf.Value;

        if (appState == null)
            return null;

        var appId = appState["appid"]?.Value<string>();
        var name = appState["name"]?.Value<string>();
        var installDir = appState["installdir"]?.Value<string>();

        if (string.IsNullOrEmpty(appId) || string.IsNullOrEmpty(name) || string.IsNullOrEmpty(installDir))
            return null;

        // Skip various tools
        if (name.Contains("Steamworks Common") || name.Contains("Proton") || name.Contains("Steam Linux Runtime"))
            return null;

        var installPath = Path.Combine(steamAppsPath, "common", installDir);

        if (!Directory.Exists(installPath))
            return null;

        var lastPlayed = appState["LastPlayed"]?.Value<long>();
        var playtime = appState["playtime_forever"]?.Value<int>();

        return new DetectedGame
        {
            Id = appId,
            Name = name,
            InstallPath = installPath,
            Platform = GamePlatform.Steam,
            ExecutablePath = FindMainExecutable(installPath),
            IconPath = GetSteamGameIcon(appId),
            LastPlayed = lastPlayed > 0 ? DateTimeOffset.FromUnixTimeSeconds(lastPlayed.Value).DateTime : null,
            PlaytimeMinutes = playtime
        };
    }

    /// <summary>
    /// Attempts to find the main executable in a game folder
    /// </summary>
    private static string? FindMainExecutable(string gamePath)
    {
        if (!Directory.Exists(gamePath))
            return null;

        var ignoredExeNames = new[] { "unins", "crash", "redist", "setup" };
        var exePattern = OperatingSystem.IsWindows() ? "*.exe" : "*";

        var exeFiles = Directory.GetFiles(gamePath, exePattern, SearchOption.TopDirectoryOnly)
            .Where(f => OperatingSystem.IsWindows() || IsExecutable(f))
            .ToArray();

        var preferred = exeFiles.FirstOrDefault(f =>
        {
            var name = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
            bool launcherIgnored = name.Contains("launcher") && !name.EndsWith("launcher");

            return !ignoredExeNames.Any(ignored => name.Contains(ignored)) && !launcherIgnored;
        });

        return preferred ?? exeFiles.FirstOrDefault();
    }

    private static bool IsExecutable(string path)
    {
        try
        {
            var info = new FileInfo(path);
            // Check if file has execute permission on Unix
            return (info.Attributes & FileAttributes.Directory) == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets the Steam CDN URL for game header image
    /// </summary>
    private static string GetSteamGameIcon(string appId)
    {
        return $"https://steamcdn-a.akamaihd.net/steam/apps/{appId}/header.jpg";
    }
}
