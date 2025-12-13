# Canopy

A cross-platform desktop application for game tracking and overlay, built with .NET 8. The app wraps a web application in a native WebView with features like Steam game detection, overlay support, global hotkeys, and system tray integration.

## Features

### ✅ Windows (Implemented)

- **WebView2 Wrapper**: Hosts the Canopy web app with native integration
- **Steam Game Detection**: Automatically scans and monitors your Steam library
- **Game Overlay**: Always-on-top overlay with drag support and customizable position
- **Global Hotkeys**: Configurable keyboard shortcuts for overlay control
- **System Tray**: Minimize to tray with context menu
- **Auto-Updates**: Built-in update system using Velopack
- **OAuth Integration**: Protocol handler for `canopy://` authentication
- **IPC Bridge**: Bidirectional communication between native and web layers

### 🚧 Planned

- **Linux**: GTK# or Avalonia UI with WebKitGTK
- **macOS**: AppKit/Catalyst with WKWebView

## Project Structure

```
CanopySharp/
├── src/
│   ├── Canopy.Core/           # Cross-platform shared code
│   │   ├── Application/       # Settings, coordinator, single instance
│   │   ├── Auth/              # Token exchange services
│   │   ├── GameDetection/     # Steam and platform detection
│   │   ├── Input/             # Hotkey abstractions
│   │   ├── IPC/               # Inter-process communication
│   │   ├── Models/            # Shared data models
│   │   ├── Notifications/     # Notification abstractions
│   │   └── Platform/          # Platform service interfaces
│   │
│   ├── Canopy.Windows/        # Windows WinUI 3 + WebView2
│   │   ├── Interop/           # Win32 API interop
│   │   ├── Services/          # Windows-specific services
│   │   └── Assets/            # Icons and resources
│   │
│   ├── Canopy.Linux/          # Linux (stub)
│   ├── Canopy.Mac/            # macOS (stub)
│   └── Canopy.Setup/          # Installer project
│
└── CanopySharp.sln
```

## Requirements

### Windows

- Windows 10 version 1803 (build 17134) or later
- .NET 8.0 Runtime (bundled with installer)
- WebView2 Runtime (auto-installed)

### Development

- .NET 8.0 SDK
- Visual Studio 2022 with:
  - .NET Desktop Development workload
  - Windows App SDK

## Building

```bash
# Clone the repository
git clone https://github.com/your-org/canopy-sharp.git
cd canopy-sharp

# Restore dependencies
dotnet restore

# Build Windows project
dotnet build src/Canopy.Windows -c Release

# Run in development
dotnet run --project src/Canopy.Windows
```

## Configuration

Settings are stored in platform-specific locations:

| Platform | Location |
|----------|----------|
| Windows | `%LOCALAPPDATA%\Canopy\settings.json` |
| Linux | `~/.local/share/canopy/settings.json` |
| macOS | `~/Library/Application Support/Canopy/settings.json` |

### Default Hotkeys

| Shortcut | Action |
|----------|--------|
| `Ctrl+Alt+O` | Toggle overlay visibility |
| `Ctrl+Alt+D` | Toggle overlay drag mode |

## Architecture

### Core Layer (`Canopy.Core`)

Platform-agnostic code shared across all implementations:

- **AppCoordinator**: Central event hub for app-wide actions
- **ISettingsService**: Settings persistence abstraction
- **IHotkeyService**: Global hotkey abstraction
- **IpcBridgeBase**: Base class for WebView IPC communication
- **GameService**: Aggregates game detection across platforms
- **TokenExchangeService**: Firebase authentication token exchange

### Platform Layer (`Canopy.Windows`, etc.)

Platform-specific implementations:

- **WindowsPlatformServices**: Startup registration, protocol handling
- **HotkeyService**: Win32 global hotkey registration
- **TrayIconService**: System tray with H.NotifyIcon
- **WebViewIpcBridge**: WebView2 message passing
- **GameDetector**: Process monitoring for running games

### IPC Communication

The app provides a JavaScript bridge for communication with the web layer:

```javascript
// Receive messages from native layer
window.addEventListener('message', (event) => {
  const { type, payload } = event.data;
  // Handle message...
});
```

#### Message Types

| Type | Direction | Description |
|------|-----------|-------------|
| `SYN` | Native → Web | App ready signal |
| `games:detected` | Native → Web | List of installed games |
| `GAME_STATE_UPDATE` | Native → Web | Game started/stopped |
| `TOKEN_RECEIVED` | Native → Web | OAuth token received |
| `open-external` | Web → Native | Open URL in browser |

## Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

### Adding a New Game Platform

1. Implement `IGameScanner` in `Canopy.Core/GameDetection/`
2. Register in DI container
3. The `GameService` will automatically include it in scans

### Adding Platform Support

1. Create new project (e.g., `Canopy.Linux`)
2. Implement platform services:
   - Inherit from `SettingsServiceBase`
   - Inherit from `IpcBridgeBase`
   - Implement `IHotkeyService`
   - Implement `ITrayIconService`
3. Subscribe to `AppCoordinator` events

## License

MIT License - See [LICENSE](LICENSE) file
