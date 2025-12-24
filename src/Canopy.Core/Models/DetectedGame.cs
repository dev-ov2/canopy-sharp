namespace Canopy.Core.Models;

/// <summary>
/// Represents a detected game from any platform (Steam, Epic, etc.)
/// </summary>
public class DetectedGame
{
    /// <summary>
    /// Unique identifier for the game (platform-specific ID)
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// Display name of the game
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Absolute path to the game's installation directory
    /// </summary>
    public required string InstallPath { get; set; }

    /// <summary>
    /// Path to the game's executable
    /// </summary>
    public string? ExecutablePath { get; set; }

    /// <summary>
    /// The platform this game belongs to
    /// </summary>
    public required GamePlatform Platform { get; set; }

    /// <summary>
    /// Whether the game is currently running
    /// </summary>
    public bool IsRunning { get; set; }

    /// <summary>
    /// Icon or header image path for the game
    /// </summary>
    public string? IconPath { get; set; }

    /// <summary>
    /// Last time the game was played
    /// </summary>
    public DateTime? LastPlayed { get; set; }

    /// <summary>
    /// Total playtime in minutes
    /// </summary>
    public int? PlaytimeMinutes { get; set; }

    /// <summary>
    /// Process names to match for detecting if the game is running.
    /// Used primarily for games detected from remote mappings.
    /// Names should be without file extension.
    /// </summary>
    public string[]? ProcessNames { get; set; }

    /// <summary>
    /// Deep search patterns that match against extended process properties
    /// (ProcessName, WindowTitle/MainWindowTitle, FileDescription, ProductName, CompanyName, CommandLine, ExecutablePath).
    /// Case-insensitive substring matching is used.
    /// </summary>
    public string[]? DeepSearchPatterns { get; set; }
}

/// <summary>
/// Supported game platforms
/// </summary>
public enum GamePlatform
{
    Steam,
    Epic,
    GOG,
    Xbox,
    Origin,
    Ubisoft,
    BattleNet,
    Custom
}
