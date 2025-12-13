using Canopy.Core.IPC;
using Canopy.Core.Platform;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using System.Text.Json;

namespace Canopy.Windows.Services;

/// <summary>
/// WebView2 IPC bridge for communication between native C# and web JavaScript
/// </summary>
public class WebViewIpcBridge : IpcBridgeBase
{
    private readonly WindowsPlatformServices _platformServices;
    private Microsoft.Web.WebView2.Core.CoreWebView2? _webView;

    public WebViewIpcBridge()
    {
        _platformServices = App.Services.GetRequiredService<WindowsPlatformServices>();
    }

    /// <summary>
    /// Initializes the bridge with a WebView2 instance
    /// </summary>
    public void Initialize(Microsoft.Web.WebView2.Core.CoreWebView2 webView)
    {
        _webView = webView;
        _webView.WebMessageReceived += OnWebMessageReceived;
        Debug.WriteLine("webview ready");
    }

    private void OnWebMessageReceived(Microsoft.Web.WebView2.Core.CoreWebView2 sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
    {
        HandleIncomingMessage(e.WebMessageAsJson);
    }

    /// <summary>
    /// Handles built-in message types specific to Windows
    /// </summary>
    protected override bool HandleBuiltInMessage(IpcMessage message)
    {
        // Handle base built-in messages first
        if (base.HandleBuiltInMessage(message))
            return true;

        // Handle open-external
        if (message.Type == "open-external" && message.Payload != null)
        {
            Debug.WriteLine(message.Type);
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
        _webView.PostWebMessageAsJson(json);
    }
}
