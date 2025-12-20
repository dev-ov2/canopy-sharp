using Canopy.Core.IPC;
using Canopy.Core.Logging;
using System.Text.Json;
using System.Text.RegularExpressions;
using WebKit;

namespace Canopy.Linux.Services;

/// <summary>
/// WebKitGTK IPC bridge for communication between native C# and web JavaScript.
/// 
/// Communication flow:
/// - JS → Native: JavaScript modifies document.title with a special prefix, we poll for it
/// - Native → JS: Calls window.chrome.webview._dispatchMessage shim
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
    private string _originalTitle = "";
    private bool _isInitialized;
    
    private const string MessagePrefix = "___CANOPY_IPC___:";

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

        // Watch for title changes - we use title as a communication channel
        webView.AddNotification("title", OnTitleChanged);

        // Inject the compatibility shim after page loads
        webView.LoadChanged += OnLoadChanged;
        
        _isInitialized = true;
        _logger.Info("WebView IPC bridge initialized");
    }

    private void OnTitleChanged(object o, GLib.NotifyArgs args)
    {
        try
        {
            var title = _webView?.Title;
            if (string.IsNullOrEmpty(title)) return;

            // Check if this is an IPC message
            if (title.StartsWith(MessagePrefix))
            {
                var json = title.Substring(MessagePrefix.Length);
                _logger.Debug($"Received IPC: {(json.Length > 100 ? json[..100] + "..." : json)}");
                
                // Restore the original title
                RestoreTitle();
                
                // Process the message
                HandleIncomingMessage(json);
            }
            else if (!title.StartsWith("___"))
            {
                // Save the real title
                _originalTitle = title;
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Error processing title change", ex);
        }
    }

    private void RestoreTitle()
    {
        if (_webView != null && !string.IsNullOrEmpty(_originalTitle))
        {
            try
            {
                var script = $"document.title = '{_originalTitle.Replace("'", "\\'")}';";
                _webView.RunJavascript(script, null, null);
            }
            catch (Exception ex)
            {
                _logger.Debug($"Failed to restore title: {ex.Message}");
            }
        }
    }

    private void OnLoadChanged(object? sender, LoadChangedArgs args)
    {
        if (args.LoadEvent == LoadEvent.Finished)
        {
            // Save the original title
            _originalTitle = _webView?.Title ?? "";
            InjectCompatibilityShim();
        }
    }

    /// <summary>
    /// Injects a shim that makes window.chrome.webview work like WebView2.
    /// Uses document.title modification for JS→Native communication.
    /// </summary>
    private void InjectCompatibilityShim()
    {
        if (_webView == null)
        {
            _logger.Warning("Cannot inject shim - WebView is null");
            return;
        }

        try
        {
            var script = $@"
(function() {{
    if (window.__canopyBridgeReady) return;
    window.__canopyBridgeReady = true;

    var MESSAGE_PREFIX = '{MessagePrefix}';
    var messageHandlers = [];
    var originalTitle = document.title;
    var messageQueue = [];
    var sending = false;

    function sendNextMessage() {{
        if (messageQueue.length === 0) {{
            sending = false;
            document.title = originalTitle;
            return;
        }}
        sending = true;
        var msg = messageQueue.shift();
        document.title = MESSAGE_PREFIX + msg;
        // Give native time to read it
        setTimeout(sendNextMessage, 10);
    }}

    // Create chrome.webview shim
    if (!window.chrome) window.chrome = {{}};
    window.chrome.webview = {{
        // Send message to native via title change
        postMessage: function(message) {{
            var json = typeof message === 'string' ? message : JSON.stringify(message);
            messageQueue.push(json);
            if (!sending) sendNextMessage();
        }},
        // Register handler for messages from native
        addEventListener: function(event, handler) {{
            if (event === 'message') {{
                messageHandlers.push(handler);
            }}
        }},
        removeEventListener: function(event, handler) {{
            if (event === 'message') {{
                var idx = messageHandlers.indexOf(handler);
                if (idx >= 0) messageHandlers.splice(idx, 1);
            }}
        }},
        // Called by native code to dispatch messages
        _dispatchMessage: function(data) {{
            var event = {{ data: data }};
            messageHandlers.forEach(function(h) {{
                try {{ h(event); }} catch(e) {{ console.error('Message handler error:', e); }}
            }});
        }}
    }};

    console.log('[Canopy] WebView bridge ready');
}})();
";
            _webView.RunJavascript(script, null, null);
            _logger.Debug("Compatibility shim injected");
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to inject compatibility shim", ex);
        }
    }

    protected override bool HandleBuiltInMessage(IpcMessage message)
    {
        if (base.HandleBuiltInMessage(message))
            return true;

        // Handle open-external
        if (message.Type == "open-external" && message.Payload != null)
        {
            try
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
            }
            catch (Exception ex)
            {
                _logger.Warning($"Failed to handle open-external: {ex.Message}");
            }
            return true;
        }

        return false;
    }

    /// <summary>
    /// Sends a message to the web app by calling the shim's _dispatchMessage function.
    /// </summary>
    public override async Task Send(IpcMessage message)
    {
        if (_webView == null || !_isInitialized)
        {
            _logger.Debug("Cannot send - WebView not initialized");
            return;
        }

        try
        {
            var json = SerializeMessage(message);
            
            // Escape for JavaScript string
            var escaped = json
                .Replace("\\", "\\\\")
                .Replace("'", "\\'")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r");
            
            // Call the shim's dispatch function
            var script = $"if(window.chrome&&window.chrome.webview)window.chrome.webview._dispatchMessage(JSON.parse('{escaped}'));";
            
            // Use GLib.Idle.Add instead of Gtk.Application.Invoke for better compatibility
            GLib.Idle.Add(() =>
            {
                try
                {
                    _webView?.RunJavascript(script, null, null);
                }
                catch (Exception ex)
                {
                    _logger.Debug($"RunJavascript failed: {ex.Message}");
                }
                return false;
            });
        }
        catch (Exception ex)
        {
            _logger.Warning($"Failed to send IPC message: {ex.Message}");
        }

        await Task.CompletedTask;
    }
}
