# Canopy Cross-Platform Build Script
# Creates installers for Windows (Velopack) and Linux (tar.gz)

param(
    [string]$Configuration = "Release",
    [string]$Version = "1.0.0",
    [ValidateSet("all", "windows", "linux")]
    [string]$Platform = "all",
    [string]$Channel = ""
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RootDir = Split-Path -Parent $ScriptDir
$DistDir = Join-Path $RootDir "dist"

Write-Host "=== Canopy Cross-Platform Build ===" -ForegroundColor Cyan
Write-Host "Configuration: $Configuration"
Write-Host "Version: $Version"
Write-Host "Platform: $Platform"
Write-Host ""

# Clean dist directory
if (Test-Path $DistDir) {
    Remove-Item -Path $DistDir -Recurse -Force
}
New-Item -ItemType Directory -Path $DistDir -Force | Out-Null

# Build Windows with Velopack
if ($Platform -eq "all" -or $Platform -eq "windows") {
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "Building Windows x64..." -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
    
    $projectPath = Join-Path $RootDir "src\Canopy.Windows\Canopy.Windows.csproj"
    
    if (Test-Path $projectPath) {
        # Check for Velopack CLI
        $vpkPath = Get-Command "vpk" -ErrorAction SilentlyContinue
        if (-not $vpkPath) {
            Write-Host "Installing Velopack CLI..." -ForegroundColor Yellow
            dotnet tool install -g vpk --version 0.0.626
            $env:Path = [System.Environment]::GetEnvironmentVariable("Path", "Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path", "User")
        }
        
        $publishDir = Join-Path $DistDir "publish-win-x64"
        $releasesDir = Join-Path $DistDir "releases-win"
        $winChannel = if ($Channel) { $Channel } else { "win" }
        
        New-Item -ItemType Directory -Path $publishDir -Force | Out-Null
        New-Item -ItemType Directory -Path $releasesDir -Force | Out-Null
        
        # Publish
        Write-Host "Publishing application..." -ForegroundColor Yellow
        & dotnet publish $projectPath `
            -c $Configuration `
            -r "win-x64" `
            -o $publishDir `
            --self-contained true `
            -p:Version=$Version `
            -p:DebugType=none `
            -p:DebugSymbols=false
        
        if ($LASTEXITCODE -eq 0) {
            Write-Host "Build successful" -ForegroundColor Green
            
            # Create Velopack package
            Write-Host "Creating Windows installer..." -ForegroundColor Yellow
            $iconPath = Join-Path $RootDir "src\Canopy.Windows\Assets\canopy.ico"
            
            $vpkArgs = @(
                "pack",
                "-u", "Canopy",
                "-v", $Version,
                "-p", $publishDir,
                "-e", "Canopy.Windows.exe",
                "-o", $releasesDir,
                "-c", $winChannel,
                "--packTitle", "Canopy",
                "--packAuthors", "Canopy"
            )
            
            if (Test-Path $iconPath) {
                $vpkArgs += @("-i", $iconPath)
            }
            
            & vpk @vpkArgs
            
            if ($LASTEXITCODE -eq 0) {
                Write-Host "Windows installer created successfully" -ForegroundColor Green
            } else {
                Write-Host "Windows installer creation failed" -ForegroundColor Red
            }
        } else {
            Write-Host "Windows build failed" -ForegroundColor Red
        }
        
        # Cleanup
        Remove-Item -Path $publishDir -Recurse -Force -ErrorAction SilentlyContinue
    } else {
        Write-Host "Windows project not found, skipping" -ForegroundColor Yellow
    }
}

# Build Linux with tar.gz
if ($Platform -eq "all" -or $Platform -eq "linux") {
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "Building Linux x64..." -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
    
    $projectPath = Join-Path $RootDir "src\Canopy.Linux\Canopy.Linux.csproj"
    
    if (Test-Path $projectPath) {
        $publishDir = Join-Path $DistDir "publish-linux-x64"
        $packageName = "canopy-$Version-linux-x64"
        $packageDir = Join-Path $DistDir $packageName
        $releasesDir = Join-Path $DistDir "releases-linux"
        
        New-Item -ItemType Directory -Path $publishDir -Force | Out-Null
        New-Item -ItemType Directory -Path $packageDir -Force | Out-Null
        New-Item -ItemType Directory -Path $releasesDir -Force | Out-Null
        
        # Publish
        Write-Host "Publishing application..." -ForegroundColor Yellow
        & dotnet publish $projectPath `
            -c $Configuration `
            -r "linux-x64" `
            -o $publishDir `
            --self-contained true `
            -p:Version=$Version `
            -p:DebugType=none `
            -p:DebugSymbols=false
        
        if ($LASTEXITCODE -eq 0) {
            Write-Host "Build successful" -ForegroundColor Green
            
            # Create package structure
            Write-Host "Creating Linux package..." -ForegroundColor Yellow
            
            $libDir = Join-Path $packageDir "lib"
            $iconsDir = Join-Path $packageDir "share\icons\hicolor\256x256\apps"
            $appsDir = Join-Path $packageDir "share\applications"
            
            New-Item -ItemType Directory -Path $libDir -Force | Out-Null
            New-Item -ItemType Directory -Path $iconsDir -Force | Out-Null
            New-Item -ItemType Directory -Path $appsDir -Force | Out-Null
            
            # Copy application files
            Copy-Item -Path "$publishDir\*" -Destination $libDir -Recurse
            
            # Copy icon if exists
            $iconSrc = Join-Path $RootDir "src\Canopy.Linux\Assets\canopy.png"
            if (Test-Path $iconSrc) {
                Copy-Item -Path $iconSrc -Destination (Join-Path $iconsDir "canopy.png")
            }
            
            # Create launcher script
            $launcherContent = @'
#!/bin/bash
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
export LD_LIBRARY_PATH="$SCRIPT_DIR/lib:$LD_LIBRARY_PATH"
exec "$SCRIPT_DIR/lib/Canopy.Linux" "$@"
'@
            Set-Content -Path (Join-Path $packageDir "canopy") -Value $launcherContent -NoNewline
            
            # Create desktop entry
            $desktopEntry = @"
[Desktop Entry]
Version=1.0
Type=Application
Name=Canopy
GenericName=Game Overlay
Comment=Game overlay and tracking application
Exec=canopy %u
Icon=canopy
Terminal=false
Categories=Game;Utility;
Keywords=games;overlay;tracking;
MimeType=x-scheme-handler/canopy;
StartupWMClass=canopy
StartupNotify=true
"@
            Set-Content -Path (Join-Path $appsDir "canopy.desktop") -Value $desktopEntry
            
            # Create install script
            $installScript = @'
#!/bin/bash
set -e
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
INSTALL_DIR="${INSTALL_DIR:-$HOME/.local/share/canopy}"
BIN_DIR="${BIN_DIR:-$HOME/.local/bin}"
APPLICATIONS_DIR="$HOME/.local/share/applications"
ICONS_DIR="$HOME/.local/share/icons/hicolor/256x256/apps"

echo "Installing Canopy to $INSTALL_DIR..."
mkdir -p "$INSTALL_DIR" "$BIN_DIR" "$APPLICATIONS_DIR" "$ICONS_DIR"

cp -r "$SCRIPT_DIR/lib/"* "$INSTALL_DIR/"
chmod +x "$INSTALL_DIR/Canopy.Linux"
ln -sf "$INSTALL_DIR/Canopy.Linux" "$BIN_DIR/canopy"

[ -f "$SCRIPT_DIR/share/icons/hicolor/256x256/apps/canopy.png" ] && \
    cp "$SCRIPT_DIR/share/icons/hicolor/256x256/apps/canopy.png" "$ICONS_DIR/"

sed "s|Exec=canopy|Exec=$INSTALL_DIR/Canopy.Linux|g" \
    "$SCRIPT_DIR/share/applications/canopy.desktop" > "$APPLICATIONS_DIR/canopy.desktop"

update-desktop-database "$APPLICATIONS_DIR" 2>/dev/null || true
xdg-mime default canopy.desktop x-scheme-handler/canopy 2>/dev/null || true

echo "Canopy installed! Run 'canopy' or find it in your application menu."
'@
            Set-Content -Path (Join-Path $packageDir "install.sh") -Value $installScript -NoNewline
            
            # Create tar.gz (use 7-Zip or tar if available)
            $tarPath = Join-Path $releasesDir "$packageName.tar.gz"
            
            # Try to use tar (Git for Windows includes it)
            $tarExe = Get-Command "tar" -ErrorAction SilentlyContinue
            if ($tarExe) {
                Push-Location $DistDir
                & tar -czf $tarPath $packageName
                Pop-Location
                
                if ($LASTEXITCODE -eq 0) {
                    $size = [math]::Round((Get-Item $tarPath).Length / 1MB, 2)
                    Write-Host "Linux package created: $packageName.tar.gz ($size MB)" -ForegroundColor Green
                } else {
                    Write-Host "Failed to create tar.gz" -ForegroundColor Red
                }
            } else {
                # Fall back to zip
                $zipPath = Join-Path $releasesDir "$packageName.zip"
                Compress-Archive -Path $packageDir -DestinationPath $zipPath
                $size = [math]::Round((Get-Item $zipPath).Length / 1MB, 2)
                Write-Host "Linux package created: $packageName.zip ($size MB)" -ForegroundColor Green
            }
        } else {
            Write-Host "Linux build failed" -ForegroundColor Red
        }
        
        # Cleanup
        Remove-Item -Path $publishDir -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item -Path $packageDir -Recurse -Force -ErrorAction SilentlyContinue
    } else {
        Write-Host "Linux project not found, skipping" -ForegroundColor Yellow
    }
}

# Summary
Write-Host ""
Write-Host "=== Build Complete ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "Output directory: $DistDir" -ForegroundColor White
Write-Host ""

Get-ChildItem $DistDir -Directory -Filter "releases-*" -ErrorAction SilentlyContinue | ForEach-Object {
    $platform = $_.Name -replace 'releases-', ''
    Write-Host "Platform: $platform" -ForegroundColor Cyan
    Get-ChildItem $_.FullName -File | ForEach-Object {
        $size = [math]::Round($_.Length / 1MB, 2)
        Write-Host "  - $($_.Name) ($size MB)" -ForegroundColor Gray
    }
}

Write-Host ""
Write-Host "Installation:" -ForegroundColor Cyan
Write-Host "  Windows: Run Canopy-win-Setup.exe" -ForegroundColor White
Write-Host "  Linux:   Extract tar.gz and run ./install.sh" -ForegroundColor White
