namespace Canopy.Core.Models;

/// <summary>
/// Represents a detected game from any platform (Steam, Epic, etc.)
/// </summary>
public class GameStatePayload
{
    /// <summary>
    /// Gets or sets the current state of the game as a string value.
    /// </summary>
    public required String State { get; set; }

    /// <summary>
    /// The platform this game belongs to
    /// </summary>
    public required String Source { get; set; }

    /// <summary>
    /// Display name of the game
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Unique identifier for the game (platform-specific ID)
    /// </summary>
    public required string AppId { get; set; }

    /// <summary>
    /// Icon or header image path for the game
    /// </summary>
    public string? IconPath { get; set; }
}

/// <summary>
/// Supported game states
/// </summary>
public enum GameState
{
    Started,
    Stopped
}