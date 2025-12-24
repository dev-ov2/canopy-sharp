using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Canopy.Core.Logging;
using Canopy.Core.Models;

namespace Canopy.Core.GameDetection;

/// <summary>
/// Scans for games defined in a remote JSON mappings file.
/// This allows adding game detection without app updates.
/// </summary>
public class RemoteMappingsScanner : IGameScanner
{
    private readonly ICanopyLogger _logger;
    private readonly HttpClient _httpClient;
    private readonly string _mappingsUrl;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    /// <summary>
    /// Default URL for the remote mappings file
    /// </summary>
    public const string DefaultMappingsUrl = 
        "https://gist.githubusercontent.com/dev-ov2/a6e4d27235b9456faeb55967caf8b64f/raw/canopy-mappings.json";

    public GamePlatform Platform => GamePlatform.Custom;
    
    public bool IsAvailable => true;

    public RemoteMappingsScanner() : this(DefaultMappingsUrl)
    {
    }

    public RemoteMappingsScanner(string mappingsUrl)
    {
        _logger = CanopyLoggerFactory.CreateLogger<RemoteMappingsScanner>();
        _mappingsUrl = mappingsUrl;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Canopy/1.0");
    }

    public string? GetInstallPath() => null;

    public async Task<IReadOnlyList<DetectedGame>> DetectGamesAsync(CancellationToken cancellationToken = default)
    {
        var games = new List<DetectedGame>();

        try
        {
            _logger.Debug($"Fetching remote mappings from: {_mappingsUrl}");
            
            var response = await _httpClient.GetAsync(_mappingsUrl, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.Warning($"Failed to fetch remote mappings: HTTP {response.StatusCode}");
                return games;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            
            if (string.IsNullOrWhiteSpace(json) || json == "[]")
            {
                _logger.Debug("Remote mappings file is empty");
                return games;
            }

            var mappings = JsonSerializer.Deserialize<List<GameMapping>>(json, JsonOptions);
            
            if (mappings == null || mappings.Count == 0)
            {
                _logger.Debug("No game mappings found in remote file");
                return games;
            }

            _logger.Info($"Loaded {mappings.Count} game mappings from remote");

            foreach (var mapping in mappings)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!mapping.IsValidForCurrentPlatform())
                {
                    _logger.Debug($"Skipping {mapping.Name}: not valid for current platform");
                    continue;
                }

                var game = mapping.ToDetectedGame();
                if (game != null)
                {
                    games.Add(game);
                    _logger.Debug($"Added game from mapping: {game.Name}");
                }
            }

            _logger.Info($"Remote mappings: {games.Count} games applicable to current platform");
        }
        catch (HttpRequestException ex)
        {
            _logger.Warning($"Network error fetching remote mappings: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            _logger.Debug("Remote mappings fetch was cancelled");
        }
        catch (JsonException ex)
        {
            _logger.Warning($"Failed to parse remote mappings JSON: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.Error("Unexpected error fetching remote mappings", ex);
        }

        return games;
    }
}

/// <summary>
/// Represents a game mapping from the remote JSON file.
/// </summary>
public class GameMapping
{
    /// <summary>
    /// Unique identifier for the game
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// Display name of the game
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Platform identifier (e.g., "custom", "steam", "epic")
    /// </summary>
    public string Platform { get; set; } = "custom";

    /// <summary>
    /// Icon URL for the game
    /// </summary>
    public string? IconUrl { get; set; }

    /// <summary>
    /// Process detection rules for the game
    /// </summary>
    public ProcessDetection? Process { get; set; }

    /// <summary>
    /// Path detection rules (install paths to check)
    /// </summary>
    public PathDetection? Paths { get; set; }

    /// <summary>
    /// Whether this mapping is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Checks if this mapping is valid for the current operating system
    /// </summary>
    public bool IsValidForCurrentPlatform()
    {
        if (!Enabled) return false;

        // If process detection is specified, check if it has rules for current OS
        if (Process != null)
        {
            if (OperatingSystem.IsWindows() && Process.Windows?.Length > 0)
                return true;
            if (OperatingSystem.IsLinux() && Process.Linux?.Length > 0)
                return true;
            if (OperatingSystem.IsMacOS() && Process.MacOS?.Length > 0)
                return true;

            // If platform-specific rules are empty, check for common rules
            if (Process.Common?.Length > 0)
                return true;

            // Deep search patterns work on all platforms
            if (Process.DeepSearch?.Length > 0)
                return true;
        }

        // If path detection is specified, check if any paths exist
        if (Paths != null)
        {
            var pathsToCheck = GetPathsForCurrentPlatform();
            if (pathsToCheck.Any(p => Directory.Exists(ExpandPath(p))))
                return true;
        }

        return Process != null || Paths != null;
    }

    /// <summary>
    /// Converts this mapping to a DetectedGame if valid
    /// </summary>
    public DetectedGame? ToDetectedGame()
    {
        var processNames = GetProcessNamesForCurrentPlatform();
        var installPath = GetInstallPathForCurrentPlatform();
        var deepSearchPatterns = Process?.DeepSearch;

        // We need at least a process name, install path, or deep search pattern for detection
        if (processNames.Length == 0 && string.IsNullOrEmpty(installPath) && (deepSearchPatterns == null || deepSearchPatterns.Length == 0))
        {
            return null;
        }

        var platform = Platform.ToLowerInvariant() switch
        {
            "steam" => GamePlatform.Steam,
            "epic" => GamePlatform.Epic,
            "gog" => GamePlatform.GOG,
            "xbox" => GamePlatform.Xbox,
            "origin" => GamePlatform.Origin,
            "ubisoft" => GamePlatform.Ubisoft,
            "battlenet" => GamePlatform.BattleNet,
            _ => GamePlatform.Custom
        };

        return new DetectedGame
        {
            Id = Id,
            Name = Name,
            Platform = platform,
            InstallPath = installPath ?? string.Empty,
            ExecutablePath = null,
            IconPath = IconUrl,
            ProcessNames = processNames.Length > 0 ? processNames : null,
            DeepSearchPatterns = deepSearchPatterns?.Length > 0 ? deepSearchPatterns : null
        };
    }

    /// <summary>
    /// Gets the process names applicable to the current platform
    /// </summary>
    public string[] GetProcessNamesForCurrentPlatform()
    {
        if (Process == null) return [];

        var names = new List<string>();

        // Add common process names (cross-platform)
        if (Process.Common?.Length > 0)
            names.AddRange(Process.Common);

        // Add platform-specific process names
        if (OperatingSystem.IsWindows() && Process.Windows?.Length > 0)
            names.AddRange(Process.Windows);
        else if (OperatingSystem.IsLinux() && Process.Linux?.Length > 0)
            names.AddRange(Process.Linux);
        else if (OperatingSystem.IsMacOS() && Process.MacOS?.Length > 0)
            names.AddRange(Process.MacOS);

        return names.ToArray();
    }

    private string? GetInstallPathForCurrentPlatform()
    {
        var paths = GetPathsForCurrentPlatform();
        foreach (var path in paths)
        {
            var expanded = ExpandPath(path);
            if (Directory.Exists(expanded))
                return expanded;
        }
        return null;
    }

    private string[] GetPathsForCurrentPlatform()
    {
        if (Paths == null) return [];

        var paths = new List<string>();

        if (Paths.Common?.Length > 0)
            paths.AddRange(Paths.Common);

        if (OperatingSystem.IsWindows() && Paths.Windows?.Length > 0)
            paths.AddRange(Paths.Windows);
        else if (OperatingSystem.IsLinux() && Paths.Linux?.Length > 0)
            paths.AddRange(Paths.Linux);
        else if (OperatingSystem.IsMacOS() && Paths.MacOS?.Length > 0)
            paths.AddRange(Paths.MacOS);

        return paths.ToArray();
    }

    private static string ExpandPath(string path)
    {
        // Expand environment variables and special folders
        var result = Environment.ExpandEnvironmentVariables(path);
        
        // Handle ~ for home directory on Unix
        if (result.StartsWith("~"))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            result = Path.Combine(home, result.Substring(1).TrimStart('/', '\\'));
        }

        return result;
    }
}

/// <summary>
/// Process detection configuration for a game
/// </summary>
public class ProcessDetection
{
    /// <summary>
    /// Process names that work on all platforms (without extension)
    /// </summary>
    public string[]? Common { get; set; }

    /// <summary>
    /// Windows process names (without .exe extension)
    /// </summary>
    public string[]? Windows { get; set; }

    /// <summary>
    /// Linux process names
    /// </summary>
    public string[]? Linux { get; set; }

    /// <summary>
    /// macOS process names
    /// </summary>
    public string[]? MacOS { get; set; }

    /// <summary>
    /// Deep search patterns that match against extended process properties.
    /// Searches: WindowTitle, FileDescription, ProductName, CompanyName, CommandLine, ExecutablePath.
    /// Case-insensitive substring matching. Works on all platforms.
    /// </summary>
    public string[]? DeepSearch { get; set; }
}

/// <summary>
/// Path detection configuration for a game
/// </summary>
public class PathDetection
{
    /// <summary>
    /// Paths that work on all platforms
    /// </summary>
    public string[]? Common { get; set; }

    /// <summary>
    /// Windows installation paths (supports %ENVVAR% and ~)
    /// </summary>
    public string[]? Windows { get; set; }

    /// <summary>
    /// Linux installation paths (supports ~ and env vars)
    /// </summary>
    public string[]? Linux { get; set; }

    /// <summary>
    /// macOS installation paths (supports ~ and env vars)
    /// </summary>
    public string[]? MacOS { get; set; }
}
