namespace Canopy.Core.Platform;

/// <summary>
/// Interface for platform-specific application services
/// </summary>
public interface IPlatformServices
{
    /// <summary>
    /// Runs the application on startup
    /// </summary>
    Task SetAutoStartAsync(bool enabled, bool maximized);

    /// <summary>
    /// Checks if auto-start is enabled
    /// </summary>
    Task<bool> IsAutoStartEnabledAsync();

    /// <summary>
    /// Opens a URL in the default browser
    /// </summary>
    void OpenUrl(string url);

    /// <summary>
    /// Opens a file or folder in the default application
    /// </summary>
    void OpenPath(string path);

    /// <summary>
    /// Gets the primary screen dimensions
    /// </summary>
    (int Width, int Height) GetPrimaryScreenSize();

    /// <summary>
    /// Gets all screen bounds
    /// </summary>
    IReadOnlyList<ScreenInfo> GetAllScreens();
}

/// <summary>
/// Information about a display screen
/// </summary>
public class ScreenInfo
{
    public required string DeviceName { get; set; }
    public required int X { get; set; }
    public required int Y { get; set; }
    public required int Width { get; set; }
    public required int Height { get; set; }
    public bool IsPrimary { get; set; }
    public double ScaleFactor { get; set; } = 1.0;
}
