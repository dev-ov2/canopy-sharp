namespace Canopy.Core.IPC;

/// <summary>
/// Interface for IPC communication between native and web layers
/// </summary>
public interface IIpcBridge
{
    /// <summary>
    /// Sends a message to the web layer
    /// </summary>
    Task Send(IpcMessage message);

    /// <summary>
    /// Sends a message and waits for a response
    /// </summary>
    Task<T?> SendAndReceiveAsync<T>(IpcMessage message, TimeSpan? timeout = null);

    /// <summary>
    /// Subscribes to messages of a specific type
    /// </summary>
    IDisposable Subscribe(string messageType, Action<IpcMessage> handler);

    /// <summary>
    /// Event raised when a message is received from the web layer
    /// </summary>
    event EventHandler<IpcMessage>? MessageReceived;
}
