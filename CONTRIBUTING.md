# Contributing to Canopy

## Getting Started

### Prerequisites
- .NET 8.0 SDK
- Visual Studio 2022 or VS Code
- Windows 10+ (for Windows development)
- Linux with GTK 3 (for Linux development)

### Setup

```bash
git clone https://github.com/YOUR_USERNAME/canopy-sharp.git
cd canopy-sharp
dotnet restore
dotnet build
```

## Project Structure

| Project | Description |
|---------|-------------|
| `Canopy.Core` | Cross-platform shared code |
| `Canopy.Windows` | Windows (WinUI 3 + WebView2) |
| `Canopy.Linux` | Linux (GTK# + WebKitGTK) |
| `Canopy.Mac` | macOS (planned) |

## Guidelines

### Code Style
- Follow `.editorconfig` settings
- Use file-scoped namespaces
- Add XML docs for public APIs

### Architecture
- Implement shared logic in `Canopy.Core`
- Use interfaces for platform services
- Use `AppCoordinator` for cross-cutting events
- Register services via DI

### Adding Features

**New game platform:**
1. Implement `IGameScanner` in `Canopy.Core/GameDetection/`
2. Register in platform's DI configuration

**New platform support:**
1. Create project referencing `Canopy.Core`
2. Implement: `ISettingsService`, `IHotkeyService`, `ITrayIconService`
3. Extend `IpcBridgeBase` for WebView

## Pull Requests

1. Create feature branch from `main`
2. Make changes with clear commits
3. Ensure `dotnet build` succeeds
4. Create PR with description

## License

Contributions are licensed under MIT.
