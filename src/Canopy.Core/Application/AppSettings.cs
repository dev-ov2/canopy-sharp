namespace Canopy.Core.Application;

/// <summary>
/// Application settings model - shared across all platforms
/// </summary>
public class AppSettings
{
    // General
    public bool StartWithWindows { get; set; } = true;  // Named for Windows but applies to all 
    public bool StartOpen { get; set; } = true;
    public bool AutoUpdate { get; set; } = true;

    // Overlay
    public bool EnableOverlay { get; set; } = true;
    public string OverlayToggleShortcut { get; set; } = "Ctrl+Alt+O";
    public string OverlayDragShortcut { get; set; } = "Ctrl+Alt+D";

    /// <summary>
    /// When true, uses the compact toolbar-style overlay (narrower, 2/3 height).
    /// When false, uses the full-size overlay.
    /// </summary>
    public bool CompactOverlay { get; set; } = true;

    // Overlay position (persisted when dragged)
    public int? OverlayX { get; set; }
    public int? OverlayY { get; set; }
}
