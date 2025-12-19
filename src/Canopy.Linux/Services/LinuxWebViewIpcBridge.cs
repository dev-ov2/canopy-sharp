using Canopy.Core.IPC;
using Canopy.Core.Logging;
using System.Text.Json;
using WebKit;

namespace Canopy.Linux.Services;

/// <summary>
/// WebKitGTK IPC bridge for communication between native C# and web JavaScript.
/// 
/// Communication flow:
/// - JS → Native: window.webkit.messageHandlers.canopy.postMessage(json)
/// - Native → JS: Calls window.chrome.webview.postMessage shim or dispatches event
/// 
/// The bridge injects a compatibility shim so the web app can use the same API as WebView2:
/// - window.chrome.webview.postMessage(message) - sends to native
/// - window.chrome.webview.addEventListener('message', handler) - receives from native
/// </summary>
public class LinuxWebViewIpcBridge : IpcBridgeBase
{
    private readonly ICanopyLogger _logger;
    private readonly LinuxPlatformServices _platformServices;
    private WebView? _webView;
    private UserContentManager? _contentManager;

    public LinuxWebViewIpcBridge(LinuxPlatformServices platformServices)
    {
        _platformServices = platformServices;
        _logger = CanopyLoggerFactory.CreateLogger<LinuxWebViewIpcBridge>();
    }

    /// <summary>
    /// Initializes the bridge with a WebKitGTK WebView instance.
    /// </summary>
    public void Initialize(WebView webView)
    {
        _webView = webView;
        _contentManager = webView.UserContentManager;

        if (_contentManager != null)
        {
            // Register script message handler - creates window.webkit.messageHandlers.canopy
            _contentManager.RegisterScriptMessageHandler("canopy");
            _contentManager.ScriptMessageReceived += OnScriptMessageReceived;
            _logger.Info("Registered WebKit script message handler 'canopy'");
        }
        else
        {
            _logger.Warning("UserContentManager is null - IPC will not work");
        }

        // Inject the compatibility shim after page loads
        webView.LoadChanged += OnLoadChanged;
        
        _logger.Info("WebView IPC bridge initialized");
    }

    private void OnLoadChanged(object? sender, LoadChangedArgs args)
    {
        if (args.LoadEvent == LoadEvent.Finished)
        {
            InjectCompatibilityShim();
        }
    }

    /// <summary>
    /// Injects a shim that makes window.chrome.webview work like WebView2.
    /// This allows the web app to use the same code for both platforms.
    /// </summary>
    private void InjectCompatibilityShim()
    {
        // This shim makes the WebView2 API work on WebKitGTK:
        // - postMessage routes to window.webkit.messageHandlers.canopy.postMessage
        // - addEventListener('message', ...) stores handlers that we call from native
        var script = @"
(function() {
    if (window.__canopyBridgeReady) return;
    window.__canopyBridgeReady = true;

    // Storage for message handlers
    var messageHandlers = [];

    // Create chrome.webview shim
    if (!window.chrome) window.chrome = {};
    window.chrome.webview = {
        // Send message to native (routes to WebKit handler)
        postMessage: function(message) {
            if (window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.canopy) {
                var json = typeof message === 'string' ? message : JSON.stringify(message);
                window.webkit.messageHandlers.canopy.postMessage(json);
            }
        },
        // Register handler for messages from native
        addEventListener: function(event, handler) {
            if (event === 'message') {
                messageHandlers.push(handler);
            }
        },
        removeEventListener: function(event, handler) {
            if (event === 'message') {
                var idx = messageHandlers.indexOf(handler);
                if (idx >= 0) messageHandlers.splice(idx, 1);
            }
        },
        // Called by native code to dispatch messages
        _dispatchMessage: function(data) {
            var event = { data: data };
            messageHandlers.forEach(function(h) {
                try { h(event); } catch(e) { console.error('Message handler error:', e); }
            });
        }
    };

    console.log('[Canopy] WebView bridge ready');
})();
";
        _webView?.RunJavascript(script, null, null);
        _logger.Debug("Compatibility shim injected");
    }

    private void OnScriptMessageReceived(object? sender, ScriptMessageReceivedArgs args)
    {
        try
        {
            string? json = null;

            // Try to extract the string value from the JavaScript result
            try
            {
                var jsResult = args.JsResult;
                
                // Use reflection to access the Value property (API varies by binding version)
                var valueProperty = jsResult.GetType().GetProperty("Value");
                if (valueProperty != null)
                {
                    var jsValue = valueProperty.GetValue(jsResult);
                    if (jsValue != null)
                    {
                        var isStringProp = jsValue.GetType().GetProperty("IsString");
                        var isString = isStringProp?.GetValue(jsValue) as bool? ?? false;
                        
                        if (isString)
                        {
                            json = jsValue.ToString();
                        }
                    }
                }
                
                // Fallback
                if (string.IsNullOrEmpty(json))
                {
                    var str = jsResult.ToString();
                    if (!string.IsNullOrEmpty(str) && !str.StartsWith("WebKit."))
                    {
                        json = str;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"Could not extract JS value: {ex.Message}");
            }

            if (!string.IsNullOrEmpty(json))
            {
                _logger.Debug($"Received from JS: {(json.Length > 100 ? json[..100] + "..." : json)}");
                HandleIncomingMessage(json);
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Error receiving script message", ex);
        }
    }

    protected override bool HandleBuiltInMessage(IpcMessage message)
    {
        if (base.HandleBuiltInMessage(message))
            return true;

        // Handle open-external
        if (message.Type == "open-external" && message.Payload != null)
        {
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

    /// <summary>
    /// Sends a message to the web app by calling the shim's _dispatchMessage function.
    /// This mimics WebView2's PostWebMessageAsJson behavior.
    /// </summary>
    public override async Task Send(IpcMessage message)
    {
        if (_webView == null) return;

        var json = SerializeMessage(message);
        
        // Escape for JavaScript string
        var escaped = json
            .Replace("\\", "\\\\")
            .Replace("'", "\\'")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r");
        
        // Call the shim's dispatch function - same pattern as WebView2's postMessage
        var script = $"if(window.chrome&&window.chrome.webview)window.chrome.webview._dispatchMessage(JSON.parse('{escaped}'));";
        
        Gtk.Application.Invoke((_, _) =>
        {
            _webView?.RunJavascript(script, null, null);
        });

        await Task.CompletedTask;
    }
}
