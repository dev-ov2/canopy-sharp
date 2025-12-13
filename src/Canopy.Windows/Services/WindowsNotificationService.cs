using Canopy.Core.Notifications;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace Canopy.Windows.Services;

/// <summary>
/// Windows notification service using Windows Toast Notifications (WinRT).
/// </summary>
public class WindowsNotificationService : INotificationService, IDisposable
{
    private readonly Dictionary<string, CanopyNotification> _activeNotifications = new();
    private ToastNotifier? _notifier;

    public event EventHandler<NotificationResult>? NotificationClicked;
    public event EventHandler<NotificationResult>? NotificationDismissed;

    // Required by interface but not yet implemented for action buttons
#pragma warning disable CS0067
    public event EventHandler<NotificationResult>? NotificationActionClicked;
#pragma warning restore CS0067

    public WindowsNotificationService()
    {
        try
        {
            _notifier = ToastNotificationManager.CreateToastNotifier("Canopy");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Toast notifier creation failed: {ex.Message}");
        }
    }

    public Task<bool> ShowAsync(CanopyNotification notification)
    {
        if (_notifier == null) return Task.FromResult(false);

        try
        {
            var toastXml = BuildToastXml(notification);
            var toast = new ToastNotification(toastXml) { Tag = notification.Id };

            if (!string.IsNullOrEmpty(notification.Tag))
            {
                toast.Group = notification.Tag;
            }

            _activeNotifications[notification.Id] = notification;

            toast.Activated += (_, _) =>
            {
                _activeNotifications.Remove(notification.Id);
                App.DispatcherQueue?.TryEnqueue(() =>
                {
                    NotificationClicked?.Invoke(this, new NotificationResult
                    {
                        NotificationId = notification.Id,
                        WasClicked = true
                    });
                });
            };

            toast.Dismissed += (_, _) =>
            {
                _activeNotifications.Remove(notification.Id);
                App.DispatcherQueue?.TryEnqueue(() =>
                {
                    NotificationDismissed?.Invoke(this, new NotificationResult
                    {
                        NotificationId = notification.Id,
                        WasDismissed = true
                    });
                });
            };

            _notifier.Show(toast);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Notification error: {ex.Message}");
            return Task.FromResult(false);
        }
    }

    public Task<bool> ShowAsync(string title, string? body = null)
    {
        return ShowAsync(new CanopyNotification { Title = title, Body = body });
    }

    public Task DismissAsync(string notificationId)
    {
        try
        {
            ToastNotificationManager.History.Remove(notificationId);
            _activeNotifications.Remove(notificationId);
        }
        catch
        {
            // Notification may already be dismissed
        }
        return Task.CompletedTask;
    }

    public Task DismissByTagAsync(string tag)
    {
        try
        {
            ToastNotificationManager.History.RemoveGroup(tag);
            var toRemove = _activeNotifications
                .Where(kvp => kvp.Value.Tag == tag)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var id in toRemove)
            {
                _activeNotifications.Remove(id);
            }
        }
        catch
        {
            // Group may not exist
        }
        return Task.CompletedTask;
    }

    public Task ClearAllAsync()
    {
        try
        {
            ToastNotificationManager.History.Clear();
            _activeNotifications.Clear();
        }
        catch
        {
            // May fail if no notifications exist
        }
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        // No cleanup needed for WinRT notifier
    }

    private static XmlDocument BuildToastXml(CanopyNotification notification)
    {
        var bodyXml = string.IsNullOrEmpty(notification.Body)
            ? ""
            : $"<text>{EscapeXml(notification.Body)}</text>";

        var audioXml = notification.Silent ? "<audio silent='true'/>" : "";

        var xml = $@"
<toast>
    <visual>
        <binding template='ToastGeneric'>
            <text>{EscapeXml(notification.Title)}</text>
            {bodyXml}
        </binding>
    </visual>
    {audioXml}
</toast>";

        var doc = new XmlDocument();
        doc.LoadXml(xml);
        return doc;
    }

    private static string EscapeXml(string text)
    {
        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;");
    }
}
