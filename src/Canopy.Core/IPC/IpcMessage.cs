namespace Canopy.Core.IPC;

/// <summary>
/// Message sent between the native app and WebView
/// </summary>
public class IpcMessage
{
    /// <summary>
    /// Type of the message for routing
    /// </summary>
    public required string Type { get; set; }

    /// <summary>
    /// Optional channel for pub/sub style messaging
    /// </summary>
    public string? Channel { get; set; }

    /// <summary>
    /// Unique ID for request/response correlation
    /// </summary>
    public string? RequestId { get; set; }

    /// <summary>
    /// Payload data as dynamic object
    /// </summary>
    public object? Payload { get; set; }

    /// <summary>
    /// Timestamp of the message
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Standard IPC message types
/// </summary>
public static class IpcMessageTypes
{
    // iframe
    public const string Syn = "SYN";
    public const string Ack = "ACK";
    public const string DataReceived = "DATA_RECEIVED";
    public const string TokenReceived = "TOKEN_RECEIVED";

    // Game detection
    public const string GameStateUpdate = "GAME_STATE_UPDATE";
    public const string GamesDetected = "games:detected";
    public const string GameStarted = "games:started";
    public const string GameStopped = "games:stopped";
    public const string RescanGames = "games:rescan";

    // Overlay
    public const string OverlayShow = "overlay:show";
    public const string OverlayHide = "overlay:hide";
    public const string OverlayToggle = "overlay:toggle";
    public const string OverlayDragEnable = "overlay:drag:enable";
    public const string OverlayDragDisable = "overlay:drag:disable";
    public const string OverlayPosition = "overlay:position";

    // App
    public const string AppReady = "app:ready";
    public const string AppMinimize = "app:minimize";
    public const string AppQuit = "app:quit";

    // Notifications
    public const string NotificationShow = "notification:show";
    public const string NotificationClicked = "notification:clicked";
    public const string NotificationDismissed = "notification:dismissed";

    // Settings
    public const string SettingsGet = "settings:get";
    public const string SettingsSet = "settings:set";
    public const string SettingsChanged = "settings:changed";
}
