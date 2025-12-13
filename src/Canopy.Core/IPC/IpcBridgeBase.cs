using System.Text.Json;

namespace Canopy.Core.IPC;

/// <summary>
/// Base implementation for IPC bridges with shared logic
/// Platform-specific implementations inherit from this
/// </summary>
public abstract class IpcBridgeBase : IIpcBridge
{
    protected readonly Dictionary<string, List<Action<IpcMessage>>> Subscriptions = new();
    protected readonly Dictionary<string, TaskCompletionSource<IpcMessage>> PendingRequests = new();
    
    protected static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public event EventHandler<IpcMessage>? MessageReceived;

    /// <summary>
    /// Called by platform-specific code when a message is received from the webview
    /// </summary>
    protected void HandleIncomingMessage(string json)
    {
        try
        {
            // Handle double-encoded strings
            if (json.StartsWith("\"") && json.EndsWith("\""))
            {
                json = JsonSerializer.Deserialize<string>(json) ?? json;
            }

            var message = JsonSerializer.Deserialize<IpcMessage>(json, JsonOptions);

            if (message != null)
            {
                ProcessMessage(message);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"IPC parse error: {ex.Message}");
        }
    }

    /// <summary>
    /// Processes an incoming message - can be overridden for platform-specific handling
    /// </summary>
    protected virtual void ProcessMessage(IpcMessage message)
    {
        System.Diagnostics.Debug.WriteLineIf(message.Type != null, $"IPC received: {message.Type}");

        // Handle built-in message types
        if (!HandleBuiltInMessage(message))
        {
            // Not a built-in type, raise event and notify subscribers
            RaiseMessageReceived(message);
        }
    }

    /// <summary>
    /// Handles built-in message types. Returns true if handled.
    /// Override to add platform-specific built-in handlers.
    /// </summary>
    protected virtual bool HandleBuiltInMessage(IpcMessage message)
    {
        // Handle response messages for SendAndReceiveAsync
        if (message.RequestId != null && PendingRequests.TryGetValue(message.RequestId, out var tcs))
        {
            PendingRequests.Remove(message.RequestId);
            tcs.TrySetResult(message);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Raises the MessageReceived event and notifies subscribers
    /// </summary>
    protected void RaiseMessageReceived(IpcMessage message)
    {
        MessageReceived?.Invoke(this, message);

        if (Subscriptions.TryGetValue(message.Type, out var handlers))
        {
            foreach (var handler in handlers.ToList())
            {
                try
                {
                    handler(message);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"IPC handler error: {ex.Message}");
                }
            }
        }
    }

    /// <summary>
    /// Serializes a message to JSON
    /// </summary>
    protected string SerializeMessage(IpcMessage message)
    {
        return JsonSerializer.Serialize(message, JsonOptions);
    }

    /// <summary>
    /// Sends a message - platform-specific implementation required
    /// </summary>
    public abstract Task Send(IpcMessage message);

    /// <summary>
    /// Sends a message and waits for a response
    /// </summary>
    public async Task<T?> SendAndReceiveAsync<T>(IpcMessage message, TimeSpan? timeout = null)
    {
        message.RequestId = Guid.NewGuid().ToString();
        var tcs = new TaskCompletionSource<IpcMessage>();
        PendingRequests[message.RequestId] = tcs;

        var responseType = message.Type + ":response";
        IDisposable? subscription = null;
        subscription = Subscribe(responseType, response =>
        {
            if (response.RequestId == message.RequestId)
            {
                PendingRequests.Remove(message.RequestId);
                tcs.TrySetResult(response);
                subscription?.Dispose();
            }
        });

        await Send(message);

        var timeoutValue = timeout ?? TimeSpan.FromSeconds(30);
        var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(timeoutValue));

        if (completedTask != tcs.Task)
        {
            PendingRequests.Remove(message.RequestId);
            subscription.Dispose();
            throw new TimeoutException("IPC request timed out");
        }

        var result = await tcs.Task;
        if (result.Payload is JsonElement element)
        {
            return JsonSerializer.Deserialize<T>(element.GetRawText(), JsonOptions);
        }

        return default;
    }

    /// <summary>
    /// Subscribes to messages of a specific type
    /// </summary>
    public IDisposable Subscribe(string messageType, Action<IpcMessage> handler)
    {
        if (!Subscriptions.ContainsKey(messageType))
        {
            Subscriptions[messageType] = new List<Action<IpcMessage>>();
        }

        Subscriptions[messageType].Add(handler);

        return new Subscription(() =>
        {
            if (Subscriptions.TryGetValue(messageType, out var handlers))
            {
                handlers.Remove(handler);
            }
        });
    }

    private class Subscription : IDisposable
    {
        private readonly Action _dispose;
        public Subscription(Action dispose) => _dispose = dispose;
        public void Dispose() => _dispose();
    }
}
