# Canopy Build & Distribution

This directory contains build scripts for creating GUI installers.

## Quick Start

```powershell
# Build Windows installer
.\scripts\Build-Installer.ps1

# Build with specific version
.\scripts\Build-Installer.ps1 -Version "1.2.0"

# Build all platforms (Windows + Linux)
.\scripts\Build-All.ps1
```

## Prerequisites

- **.NET 8 SDK**
- **Velopack CLI** (installed automatically): `dotnet tool install -g vpk --version 0.0.626`

## Build Output

Running `Build-Installer.ps1` creates:

| File | Size | Description |
|------|------|-------------|
| `CanopySetup-{version}.exe` | ~155 MB | **GUI Installer** with wizard |
| `Canopy-win-Portable.zip` | ~64 MB | Portable version (no install) |
| `Canopy-{version}-full.nupkg` | ~64 MB | Update package |

## GUI Installer Features

The custom installer provides:

- ? **Welcome screen** with app description
- ? **Options page** with checkboxes:
  - Start Canopy when Windows starts
  - Create desktop shortcut
  - Create Start Menu shortcut  
  - Launch Canopy after installation
- ? **Progress bar** during installation
- ? **Completion screen**
- ? Modern dark theme UI
- ? No admin rights required (per-user install)
- ? Registers in Add/Remove Programs

## Build Scripts

### `Build-Installer.ps1` (Windows)

| Parameter | Default | Description |
|-----------|---------|-------------|
| `-Configuration` | Release | Build configuration |
| `-Version` | 1.0.0 | Version number |
| `-Architecture` | x64 | Target (x64, x86, arm64) |
| `-SkipBuild` | false | Skip app build, reuse existing |

### `Build-All.ps1` (Cross-Platform)

| Parameter | Default | Description |
|-----------|---------|-------------|
| `-Configuration` | Release | Build configuration |
| `-Version` | 1.0.0 | Version number |
| `-Platform` | all | Target (all, windows, linux) |

## Installation Paths

- **Application**: `%LOCALAPPDATA%\Canopy`
- **Settings**: `%LOCALAPPDATA%\Canopy\settings.json`
- **Shortcuts**: Desktop and Start Menu

## Auto-Updates

The app checks for updates automatically using Velopack. Configure the update source in `UpdateService.cs`:

```csharp
// GitHub Releases
var mgr = new UpdateManager(new GithubSource("https://github.com/owner/repo", null, false));
```

## CI/CD

GitHub Actions workflow (`.github/workflows/build.yml`):
- Builds on every push to `main`
- Creates installers for Windows and Linux
- Publishes releases on version tags

**Create a release:**
```bash
git tag v1.0.0
git push origin v1.0.0
```

## Troubleshooting

### Velopack CLI version mismatch
The project uses Velopack 0.0.626 for .NET 8 compatibility:
```powershell
dotnet tool uninstall -g vpk
dotnet tool install -g vpk --version 0.0.626
```

### Large installer size
The installer is ~155 MB because it includes:
- .NET 8 runtime (self-contained)
- Windows App SDK
- WebView2 runtime

For a smaller download, users can install .NET 8 runtime separately and use the framework-dependent portable build.
