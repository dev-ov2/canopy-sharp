namespace Canopy.Core.Input;

/// <summary>
/// Interface for global hotkey registration - implemented per-platform
/// </summary>
public interface IHotkeyService : IDisposable
{
    /// <summary>
    /// Registers a global hotkey
    /// </summary>
    /// <param name="shortcut">Shortcut string (e.g., "Ctrl+Alt+O")</param>
    /// <param name="name">Friendly name for the hotkey</param>
    /// <returns>Hotkey ID if successful, -1 if failed</returns>
    int RegisterHotkey(string shortcut, string name);

    /// <summary>
    /// Unregisters a hotkey by ID
    /// </summary>
    bool UnregisterHotkey(int id);

    /// <summary>
    /// Unregisters all hotkeys
    /// </summary>
    void UnregisterAll();

    /// <summary>
    /// Event raised when a registered hotkey is pressed
    /// </summary>
    event EventHandler<HotkeyEventArgs>? HotkeyPressed;
}

/// <summary>
/// Event args for hotkey press events
/// </summary>
public class HotkeyEventArgs : EventArgs
{
    /// <summary>
    /// The ID of the hotkey that was pressed
    /// </summary>
    public int HotkeyId { get; init; }

    /// <summary>
    /// The friendly name of the hotkey
    /// </summary>
    public string Name { get; init; } = "";

    /// <summary>
    /// The shortcut string that was pressed
    /// </summary>
    public string Shortcut { get; init; } = "";
}

/// <summary>
/// Modifier keys for hotkeys
/// </summary>
[Flags]
public enum KeyModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Super = 8  // Windows key / Command key
}

/// <summary>
/// Well-known hotkey names used by the application
/// </summary>
public static class HotkeyNames
{
    public const string ToggleOverlay = "ToggleOverlay";
    public const string ToggleOverlayDrag = "ToggleOverlayDrag";
}
