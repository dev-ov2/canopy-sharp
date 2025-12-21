# Canopy

Cross-platform desktop app for game tracking, built with .NET 8. Wraps a web app in a native WebView with Steam game detection, system tray integration, and OAuth support.

## Installation

### Windows

Download the latest installer from [Releases](https://github.com/dev-ov2/canopy-sharp/releases).

### Linux (One-Click)

```bash
curl -fsSL https://raw.githubusercontent.com/dev-ov2/canopy-sharp/main/scripts/install.sh | bash
```

Or install a specific version:
```bash
VERSION=1.0.0 curl -fsSL https://raw.githubusercontent.com/dev-ov2/canopy-sharp/main/scripts/install.sh | bash
```

### Linux (Manual)

```bash
# Download and extract
curl -fsSL https://github.com/dev-ov2/canopy-sharp/releases/latest/download/canopy-1.0.0-linux-x64.tar.gz | tar -xz
cd canopy-*-linux-x64
./install.sh
```

## Features

### Windows ✅

- WebView2 wrapper, Steam game detection, game overlay
- Global hotkeys, system tray, auto-updates
- Protocol handler (`canopy://`)

### Linux 🚧

- WebKitGTK wrapper, Steam game detection
- System tray (AppIndicator), auto-start
- Protocol handler, update notifications

### macOS 📋

- Planned

## Building

```bash
# Windows
dotnet build src/Canopy.Windows -c Release -r win-x64

# Linux
./scripts/build-linux.sh
```

## Project Structure

```
src/
├── Canopy.Core/        # Shared cross-platform code
├── Canopy.Windows/     # Windows (WinUI 3 + WebView2)
├── Canopy.Linux/       # Linux (GTK# + WebKitGTK)
└── Canopy.Setup/       # Windows installer
```

## Configuration

| Platform | Settings Location                 |
|----------|-----------------------------------|
| Windows  | `%LOCALAPPDATA%\Canopy\settings.json` |
| Linux    | `~/.config/canopy/settings.json` |

## Troubleshooting

### Linux Crashes

If Canopy crashes, you can generate a crash report:

```bash
# Download and run the diagnosis script
curl -fsSL https://raw.githubusercontent.com/dev-ov2/canopy-sharp/main/scripts/diagnose-crash.sh | bash
```

This will:
1. Find recent coredumps
2. Extract stack traces (if dotnet-dump is installed)
3. Collect system info and logs
4. Save a report to `~/canopy-crash-report/`

To install dotnet-dump for better crash analysis:
```bash
dotnet tool install -g dotnet-dump
```

## Uninstall

### Linux
```bash
rm -rf ~/.local/share/canopy ~/.local/bin/canopy
rm ~/.local/share/applications/canopy.desktop
rm ~/.local/share/icons/hicolor/256x256/apps/canopy.png
```

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md).

## License

MIT - See [LICENSE](LICENSE)
