namespace Canopy.Core.Platform;

/// <summary>
/// Interface for system tray icon functionality
/// </summary>
public interface ITrayIconService : IDisposable
{
    /// <summary>
    /// Initializes and shows the tray icon
    /// </summary>
    void Initialize();

    /// <summary>
    /// Shows a balloon/toast notification from the tray
    /// </summary>
    void ShowBalloon(string title, string message);

    /// <summary>
    /// Updates the tray icon tooltip
    /// </summary>
    void SetTooltip(string text);

    /// <summary>
    /// Raised when the user requests to show the main window
    /// </summary>
    event EventHandler? ShowWindowRequested;

    /// <summary>
    /// Raised when the user requests to open settings
    /// </summary>
    event EventHandler? SettingsRequested;

    /// <summary>
    /// Raised when the user requests to rescan games
    /// </summary>
    event EventHandler? RescanGamesRequested;

    /// <summary>
    /// Raised when the user requests to quit the application
    /// </summary>
    event EventHandler? QuitRequested;
}
