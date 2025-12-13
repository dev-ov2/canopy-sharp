# Overlay Implementation Documentation & Pseudocode

## Overview

The Canopy overlay is a transparent, topmost window that displays on top of games and other applications. It uses WebView2 to render web content and supports hotkey-based visibility toggling and drag repositioning.

## Architecture

```
┌─────────────────────────────────────────────────────────┐
│                    Monitor Screen                        │
│  ┌─────────────────────────────────────┐ ┌───────────┐  │
│  │                                     │ │  Overlay  │  │
│  │           Game / Desktop            │ │  Window   │  │
│  │                                     │ │           │  │
│  │                                     │ │  WebView  │  │
│  │                                     │ │  Content  │  │
│  │                                     │ │           │  │
│  └─────────────────────────────────────┘ └───────────┘  │
│                                          x=width-400     │
│                                          y=0             │
└─────────────────────────────────────────────────────────┘
```

## Position Calculation

```
PSEUDOCODE: Position Overlay at Right Edge
─────────────────────────────────────────────
FUNCTION PositionOverlayAtScreenEdge():
    screenWidth = GetPrimaryScreenWidth()
    overlayWidth = 400  // Default width

    overlay.Left = screenWidth - overlayWidth
    overlay.Top = 0
    overlay.Height = screenHeight  // Or custom height
END FUNCTION
```

## Window Styles for Click-Through

The overlay needs to be transparent to mouse clicks when not in "drag mode". This is achieved using Windows Extended Window Styles.

### Win32 API Reference

- **Documentation**: https://learn.microsoft.com/en-us/windows/win32/winmsg/extended-window-styles
- **Key Styles**:
  - `WS_EX_LAYERED` (0x00080000): Required for transparency
  - `WS_EX_TRANSPARENT` (0x00000020): Click-through
  - `WS_EX_TOOLWINDOW` (0x00000080): Excludes from taskbar
  - `WS_EX_NOACTIVATE` (0x08000000): Prevents focus stealing

```
PSEUDOCODE: Make Window Click-Through
─────────────────────────────────────
FUNCTION MakeClickThrough(hwnd, enabled):
    currentStyle = GetWindowLong(hwnd, GWL_EXSTYLE)

    IF enabled THEN
        // Add transparent and layered flags
        newStyle = currentStyle | WS_EX_TRANSPARENT | WS_EX_LAYERED
    ELSE
        // Remove transparent flag, keep layered for visual transparency
        newStyle = currentStyle & ~WS_EX_TRANSPARENT
    END IF

    SetWindowLong(hwnd, GWL_EXSTYLE, newStyle)
END FUNCTION
```

## Hotkey Implementation

### Win32 RegisterHotKey API

- **Documentation**: https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey
- **NHotkey Library**: https://github.com/thomaslevesque/NHotkey (abstracts Win32 API)

```
PSEUDOCODE: Register Global Hotkeys
───────────────────────────────────
FUNCTION RegisterHotkeys():
    // Toggle overlay visibility: Ctrl+Shift+O
    RegisterHotKey("ToggleOverlay",
        Key = 'O',
        Modifiers = MOD_CONTROL | MOD_SHIFT,
        Callback = ToggleOverlayVisibility)

    // Toggle drag mode: Ctrl+Shift+D
    RegisterHotKey("ToggleDrag",
        Key = 'D',
        Modifiers = MOD_CONTROL | MOD_SHIFT,
        Callback = ToggleDragMode)

    // Show main window: Ctrl+Shift+C
    RegisterHotKey("ShowMain",
        Key = 'C',
        Modifiers = MOD_CONTROL | MOD_SHIFT,
        Callback = ShowMainWindow)
END FUNCTION

FUNCTION ToggleOverlayVisibility():
    IF overlay.IsVisible THEN
        overlay.Hide()
        SendIpcMessage("overlay:hide")
    ELSE
        overlay.Show()
        SendIpcMessage("overlay:show")
    END IF
END FUNCTION

FUNCTION ToggleDragMode():
    overlay.IsDragEnabled = NOT overlay.IsDragEnabled

    IF overlay.IsDragEnabled THEN
        ShowDragHandle()
        ShowResizeGrip()
        MakeClickThrough(hwnd, false)  // Allow clicks
        SendIpcMessage("overlay:drag:enable")
    ELSE
        HideDragHandle()
        HideResizeGrip()
        MakeClickThrough(hwnd, true)   // Pass through clicks
        SendIpcMessage("overlay:drag:disable")
    END IF
END FUNCTION
```

## Drag and Resize Implementation

### WPF Window Dragging

- **Documentation**: https://learn.microsoft.com/en-us/dotnet/desktop/wpf/windows/how-to-use-draggable-regions

```
PSEUDOCODE: Window Dragging
───────────────────────────
FUNCTION OnMouseLeftButtonDown(sender, args):
    IF IsDragEnabled THEN
        // WPF built-in method for window dragging
        DragMove()
    END IF
END FUNCTION

FUNCTION OnResizeGripDrag(horizontalDelta, verticalDelta):
    IF IsDragEnabled THEN
        newWidth = window.Width + horizontalDelta
        newHeight = window.Height + verticalDelta

        // Enforce minimum size
        IF newWidth > 200 THEN window.Width = newWidth
        IF newHeight > 200 THEN window.Height = newHeight
    END IF
END FUNCTION
```

## IPC Between Main Window and Overlay

Both windows share the same `WebViewIpcBridge` instance through dependency injection.

```
PSEUDOCODE: IPC Message Flow
────────────────────────────
// Native → Web
FUNCTION SendToWebView(messageType, payload):
    message = CreateIpcMessage(type, payload)
    json = SerializeToJson(message)
    webView.ExecuteScript("window.canopyIpc._handleMessage(" + json + ")")
END FUNCTION

// Web → Native
EVENT OnWebMessageReceived(json):
    message = DeserializeFromJson(json)

    // Route to subscribers
    FOR EACH subscriber IN GetSubscribers(message.Type):
        subscriber.Handle(message)
    END FOR

    // Raise event for other handlers
    RaiseEvent(MessageReceived, message)
END EVENT

// JavaScript Bridge (Injected)
window.canopyIpc = {
    send: (type, payload) => {
        chrome.webview.postMessage(JSON.stringify({ type, payload }))
    },
    on: (type, handler) => {
        handlers[type].push(handler)
        return () => handlers[type].remove(handler)
    }
}
```

## Overlay Visibility States

```
STATE DIAGRAM: Overlay States
─────────────────────────────

    ┌──────────────────┐
    │     Hidden       │
    └────────┬─────────┘
             │ Ctrl+Shift+O or API call
             ▼
    ┌──────────────────┐
    │  Visible (Click  │ ◄─── Normal operating mode
    │   Through Mode)  │      - Mouse passes through
    └────────┬─────────┘      - No drag handle
             │ Ctrl+Shift+D
             ▼
    ┌──────────────────┐
    │  Visible (Drag   │ ◄─── Repositioning mode
    │     Mode)        │      - Mouse captured
    └────────┬─────────┘      - Drag handle visible
             │ Ctrl+Shift+D
             ▼
    ┌──────────────────┐
    │  Visible (Click  │
    │   Through Mode)  │
    └──────────────────┘
```

## Per-Monitor DPI Awareness

- **Documentation**: https://learn.microsoft.com/en-us/windows/win32/hidpi/high-dpi-desktop-application-development-on-windows

```xml
<!-- app.manifest -->
<dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">
    PerMonitorV2
</dpiAwareness>
```

## WebView2 Transparency

- **Documentation**: https://learn.microsoft.com/en-us/microsoft-edge/webview2/how-to/webview2-wpf-transparency

```
PSEUDOCODE: Configure Transparent WebView
─────────────────────────────────────────
// XAML
<wv2:WebView2 DefaultBackgroundColor="Transparent" />

// Code-behind
webView.DefaultBackgroundColor = System.Drawing.Color.Transparent

// CSS in web content
body {
    background: transparent;
}
```

## Memory and Performance Considerations

```
PSEUDOCODE: Overlay Lifecycle
─────────────────────────────
// Create overlay lazily
FUNCTION GetOrCreateOverlay():
    IF overlayWindow IS NULL THEN
        overlayWindow = new OverlayWindow()
        overlayWindow.InitializeWebView()
    END IF
    RETURN overlayWindow
END FUNCTION

// Don't destroy on hide, just hide
FUNCTION HideOverlay():
    overlayWindow.Visibility = Hidden
    // WebView stays loaded for instant show
END FUNCTION

// Only dispose on app shutdown
FUNCTION OnAppShutdown():
    overlayWindow?.Dispose()
END FUNCTION
```

## Error Handling

```
PSEUDOCODE: Hotkey Registration Error Handling
──────────────────────────────────────────────
TRY
    RegisterHotKey("ToggleOverlay", Key.O, Modifiers.CtrlShift)
CATCH HotkeyAlreadyRegisteredException
    // Another app has this hotkey
    ShowNotification("Hotkey Conflict",
        "Ctrl+Shift+O is used by another application.
         You can change this in Settings.")

    // Offer alternative or let user customize
    PromptForAlternativeHotkey("ToggleOverlay")
END TRY
```

## Related Documentation

1. **WebView2**: https://learn.microsoft.com/en-us/microsoft-edge/webview2/
2. **WPF Windows**: https://learn.microsoft.com/en-us/dotnet/desktop/wpf/windows/
3. **Win32 Hotkeys**: https://learn.microsoft.com/en-us/windows/win32/inputdev/keyboard-input
4. **NHotkey**: https://github.com/thomaslevesque/NHotkey
5. **H.NotifyIcon**: https://github.com/HavenDV/H.NotifyIcon
6. **Toast Notifications**: https://learn.microsoft.com/en-us/windows/apps/design/shell/tiles-and-notifications/send-local-toast
