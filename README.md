# Canopy

Cross-platform desktop app for game tracking, built with .NET 8. Wraps a web app in a native WebView with Steam game detection, system tray integration, and OAuth support.

## Features

### Windows ✅

- WebView2 wrapper for the Canopy web app
- Steam game detection and monitoring
- Game overlay with drag support
- Global hotkeys
- System tray with context menu
- Auto-updates via Velopack
- Protocol handler (`canopy://`)

### Linux 🚧

- WebKitGTK wrapper
- Steam game detection
- System tray (AppIndicator)
- Auto-start support
- Protocol handler
- Update notifications

### macOS 📋

- Planned

## Project Structure

```
src/
├── Canopy.Core/        # Shared cross-platform code
├── Canopy.Windows/     # Windows (WinUI 3 + WebView2)
├── Canopy.Linux/       # Linux (GTK# + WebKitGTK)
├── Canopy.Mac/         # macOS (stub)
└── Canopy.Setup/       # Windows installer
```

## Requirements

### Windows

- Windows 10 1803+ (build 17134)
- .NET 8.0 Runtime (bundled)
- WebView2 Runtime (auto-installed)

### Linux

- .NET 8.0 Runtime
- GTK 3, WebKitGTK, libappindicator3
- X11 (Wayland has limited support)

### Development

- .NET 8.0 SDK
- Visual Studio 2022 or VS Code

## Building

```bash
# Windows
dotnet build src/Canopy.Windows -c Release

# Linux
dotnet build src/Canopy.Linux -c Release

# Run
dotnet run --project src/Canopy.Windows
dotnet run --project src/Canopy.Linux
```

## Configuration

| Platform | Settings Location                 |
|----------|-----------------------------------|
| Windows  | `%LOCALAPPDATA%\Canopy\settings.json` |
| Linux    | `~/.config/canopy/settings.json` |

## Architecture

### Core Layer

- **AppCoordinator** - Central event hub
- **ISettingsService** - Settings persistence
- **IpcBridgeBase** - WebView communication
- **GameService** - Game detection aggregator

### Platform Layer

Each platform implements:

- Settings service
- IPC bridge
- Hotkey service
- Tray icon service
- Platform services (startup, protocol handler)

## Contributing

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Open a Pull Request

See [CONTRIBUTING.md](CONTRIBUTING.md) for details.

## License

MIT - See [LICENSE](LICENSE)
