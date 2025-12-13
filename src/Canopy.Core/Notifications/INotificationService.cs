namespace Canopy.Core.Notifications;

/// <summary>
/// Interface for platform-specific notification implementations
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Shows a notification to the user
    /// </summary>
    Task<bool> ShowAsync(CanopyNotification notification);

    /// <summary>
    /// Shows a simple text notification
    /// </summary>
    Task<bool> ShowAsync(string title, string? body = null);

    /// <summary>
    /// Dismisses a notification by ID
    /// </summary>
    Task DismissAsync(string notificationId);

    /// <summary>
    /// Dismisses all notifications with a specific tag
    /// </summary>
    Task DismissByTagAsync(string tag);

    /// <summary>
    /// Clears all notifications from this app
    /// </summary>
    Task ClearAllAsync();

    /// <summary>
    /// Event raised when a notification is clicked
    /// </summary>
    event EventHandler<NotificationResult>? NotificationClicked;

    /// <summary>
    /// Event raised when a notification action button is clicked
    /// </summary>
    event EventHandler<NotificationResult>? NotificationActionClicked;

    /// <summary>
    /// Event raised when a notification is dismissed
    /// </summary>
    event EventHandler<NotificationResult>? NotificationDismissed;
}
