namespace Canopy.Core.Notifications;

/// <summary>
/// Notification priority levels
/// </summary>
public enum NotificationPriority
{
    Low,
    Normal,
    High,
    Urgent
}

/// <summary>
/// Represents a notification to be displayed to the user
/// </summary>
public class CanopyNotification
{
    /// <summary>
    /// Unique identifier for the notification
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Title of the notification
    /// </summary>
    public required string Title { get; set; }

    /// <summary>
    /// Body text of the notification
    /// </summary>
    public string? Body { get; set; }

    /// <summary>
    /// Icon to display (can be a path or URI)
    /// </summary>
    public string? Icon { get; set; }

    /// <summary>
    /// Priority level
    /// </summary>
    public NotificationPriority Priority { get; set; } = NotificationPriority.Normal;

    /// <summary>
    /// Whether the notification is silent (no sound)
    /// </summary>
    public bool Silent { get; set; }

    /// <summary>
    /// Duration before auto-dismiss (null = platform default)
    /// </summary>
    public TimeSpan? Duration { get; set; }

    /// <summary>
    /// Action buttons for the notification
    /// </summary>
    public List<NotificationAction> Actions { get; set; } = new();

    /// <summary>
    /// Custom data to pass when notification is clicked
    /// </summary>
    public object? Data { get; set; }

    /// <summary>
    /// Tag for grouping notifications
    /// </summary>
    public string? Tag { get; set; }
}

/// <summary>
/// Action button for a notification
/// </summary>
public class NotificationAction
{
    /// <summary>
    /// Unique identifier for the action
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// Display text for the action button
    /// </summary>
    public required string Text { get; set; }

    /// <summary>
    /// Optional icon for the action
    /// </summary>
    public string? Icon { get; set; }
}

/// <summary>
/// Result of a notification interaction
/// </summary>
public class NotificationResult
{
    public required string NotificationId { get; set; }
    public string? ActionId { get; set; }
    public bool WasClicked { get; set; }
    public bool WasDismissed { get; set; }
}
