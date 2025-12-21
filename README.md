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

### Linux Dependencies

```bash
# Arch Linux / CachyOS
sudo pacman -S gtk3 webkit2gtk libayatana-appindicator

# Ubuntu/Debian (22.04+)
sudo apt install libgtk-3-0 libwebkit2gtk-4.0-37 libayatana-appindicator3-1

# Ubuntu/Debian (older)
sudo apt install libgtk-3-0 libwebkit2gtk-4.0-37 libappindicator3-1

# Fedora
sudo dnf install gtk3 webkit2gtk3 libayatana-appindicator-gtk3

# GNOME (for system tray support)
# Arch:
sudo pacman -S gnome-shell-extension-appindicator
# Ubuntu:
sudo apt install gnome-shell-extension-appindicator
# Then enable the extension in GNOME Extensions app
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

If Canopy crashes, generate a detailed crash report:

```bash
# Download and run the diagnosis script
curl -fsSL https://raw.githubusercontent.com/dev-ov2/canopy-sharp/main/scripts/diagnose-crash.sh | bash
```

This collects:
- Coredump with stack traces
- System info (GTK, WebKit versions)
- Canopy logs
- Environment details

**For better stack traces**, install dotnet-dump:
```bash
dotnet tool install -g dotnet-dump
```

**If you see `??` in stack traces**, debug symbols (PDB files) are missing. Reinstall Canopy from the latest release.

### System Tray Not Showing

Canopy tries these AppIndicator libraries in order:
1. `libayatana-appindicator-glib` (recommended, no deprecation warnings)
2. `libayatana-appindicator3` (GTK3 variant, deprecated but works)
3. `libappindicator3` (legacy Ubuntu)

If you see deprecation warnings about `libayatana-appindicator`, install the glib variant:
```bash
# Arch Linux - the libayatana-appindicator package includes the glib variant
sudo pacman -S libayatana-appindicator
```

On GNOME, you also need the AppIndicator extension enabled.

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
