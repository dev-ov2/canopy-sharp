using Canopy.Core.Logging;
using Canopy.Core.Notifications;

namespace Canopy.Linux.Services;

/// <summary>
/// Linux notification service using libnotify (notify-send)
/// </summary>
public class LinuxNotificationService : INotificationService, IDisposable
{
    private readonly ICanopyLogger _logger;
    private readonly Dictionary<string, CanopyNotification> _activeNotifications = new();

    public event EventHandler<NotificationResult>? NotificationClicked;
    public event EventHandler<NotificationResult>? NotificationActionClicked;
    public event EventHandler<NotificationResult>? NotificationDismissed;

    public LinuxNotificationService()
    {
        _logger = CanopyLoggerFactory.CreateLogger<LinuxNotificationService>();
    }

    public Task<bool> ShowAsync(CanopyNotification notification)
    {
        try
        {
            var args = BuildNotifyArgs(notification);
            
            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "notify-send",
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.Start();
            _activeNotifications[notification.Id] = notification;
            
            _logger.Debug($"Showed notification: {notification.Title}");
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to show notification", ex);
            return Task.FromResult(false);
        }
    }

    public Task<bool> ShowAsync(string title, string? body = null)
    {
        return ShowAsync(new CanopyNotification { Title = title, Body = body });
    }

    private string BuildNotifyArgs(CanopyNotification notification)
    {
        var args = new List<string>
        {
            "-a", "Canopy"
        };

        // Set urgency based on priority
        var urgency = notification.Priority switch
        {
            NotificationPriority.Low => "low",
            NotificationPriority.High or NotificationPriority.Urgent => "critical",
            _ => "normal"
        };
        args.Add("-u");
        args.Add(urgency);

        // Set icon if available
        var iconPath = notification.Icon ?? Path.Combine(AppContext.BaseDirectory, "Assets", "canopy.png");
        if (File.Exists(iconPath))
        {
            args.Add("-i");
            args.Add(iconPath);
        }

        // Set expiration time
        if (notification.Duration.HasValue)
        {
            args.Add("-t");
            args.Add(((int)notification.Duration.Value.TotalMilliseconds).ToString());
        }

        // Add title and body
        args.Add($"\"{EscapeArg(notification.Title)}\"");
        if (!string.IsNullOrEmpty(notification.Body))
        {
            args.Add($"\"{EscapeArg(notification.Body)}\"");
        }

        return string.Join(" ", args);
    }

    private static string EscapeArg(string arg)
    {
        return arg.Replace("\"", "\\\"").Replace("$", "\\$").Replace("`", "\\`");
    }

    public Task DismissAsync(string notificationId)
    {
        _activeNotifications.Remove(notificationId);
        // Linux notify-send doesn't support dismissing notifications programmatically
        return Task.CompletedTask;
    }

    public Task DismissByTagAsync(string tag)
    {
        var toRemove = _activeNotifications
            .Where(kvp => kvp.Value.Tag == tag)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var id in toRemove)
        {
            _activeNotifications.Remove(id);
        }

        return Task.CompletedTask;
    }

    public Task ClearAllAsync()
    {
        _activeNotifications.Clear();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        // Nothing to dispose
    }
}
