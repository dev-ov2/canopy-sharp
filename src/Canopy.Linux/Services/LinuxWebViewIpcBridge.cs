using Canopy.Core.IPC;
using Canopy.Core.Logging;
using System.Text.Json;
using WebKit;

namespace Canopy.Linux.Services;

/// <summary>
/// WebKitGTK IPC bridge for communication between native C# and web JavaScript
/// </summary>
public class LinuxWebViewIpcBridge : IpcBridgeBase
{
    private readonly ICanopyLogger _logger;
    private readonly LinuxPlatformServices _platformServices;
    private WebView? _webView;

    public LinuxWebViewIpcBridge(LinuxPlatformServices platformServices)
    {
        _platformServices = platformServices;
        _logger = CanopyLoggerFactory.CreateLogger<LinuxWebViewIpcBridge>();
    }

    /// <summary>
    /// Initializes the bridge with a WebKitGTK WebView instance
    /// </summary>
    public void Initialize(WebView webView)
    {
        _webView = webView;

        // Inject the bridge script after page loads
        webView.LoadChanged += OnLoadChanged;
        
        _logger.Info("WebView IPC bridge initialized");
    }

    private void OnLoadChanged(object? sender, LoadChangedArgs args)
    {
        if (args.LoadEvent == LoadEvent.Finished)
        {
            InjectBridgeScript();
        }
    }

    private void InjectBridgeScript()
    {
        // For WebKitGTK, we'll use a simpler approach with JavaScript polling
        // since the UserContentManager API may vary between versions
        var script = @"
            (function() {
                // Create message queue for native communication
                window._canopyMessageQueue = [];
                
                window.canopyBridge = {
                    postMessage: function(message) {
                        window._canopyMessageQueue.push(JSON.stringify(message));
                    }
                };
                
                // Override window.postMessage for compatibility
                window.postNativeMessage = function(message) {
                    window.canopyBridge.postMessage(message);
                };
                
                // Handle incoming messages from native
                window.addEventListener('canopy-message', function(e) {
                    // Process message from native side
                    console.log('Received from native:', e.detail);
                });
                
                console.log('Canopy bridge initialized');
            })();
        ";

        _webView?.RunJavascript(script, null, null);
    }

    /// <summary>
    /// Handles built-in message types specific to Linux
    /// </summary>
    protected override bool HandleBuiltInMessage(IpcMessage message)
    {
        if (base.HandleBuiltInMessage(message))
            return true;

        // Handle open-external
        if (message.Type == "open-external" && message.Payload != null)
        {
            _logger.Debug($"Handling open-external: {message.Type}");
            var payload = (JsonElement)message.Payload;
            if (payload.TryGetProperty("url", out var urlElement))
            {
                var url = urlElement.GetString();
                if (!string.IsNullOrEmpty(url))
                {
                    _platformServices.OpenUrl(url);
                }
            }
            return true;
        }

        return false;
    }

    public override async Task Send(IpcMessage message)
    {
        if (_webView == null) return;

        var json = SerializeMessage(message);
        var escapedJson = json
            .Replace("\\", "\\\\")
            .Replace("'", "\\'")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
        
        var script = $"window.dispatchEvent(new CustomEvent('canopy-message', {{ detail: JSON.parse('{escapedJson}') }}));";
        
        Gtk.Application.Invoke((_, _) =>
        {
            _webView?.RunJavascript(script, null, null);
        });

        await Task.CompletedTask;
    }
}
