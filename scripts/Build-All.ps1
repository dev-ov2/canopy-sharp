# Canopy Cross-Platform Build Script
# Creates GUI installers for Windows and Linux using Velopack

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

# Check for Velopack CLI
Write-Host "Checking for Velopack CLI..." -ForegroundColor Yellow
$vpkPath = Get-Command "vpk" -ErrorAction SilentlyContinue

if (-not $vpkPath) {
    Write-Host "Velopack CLI not found. Installing..." -ForegroundColor Yellow
    # Install version 0.0.626 which works with .NET 8
    dotnet tool install -g vpk --version 0.0.626
    
    # Refresh PATH
    $env:Path = [System.Environment]::GetEnvironmentVariable("Path", "Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path", "User")
    
    $vpkPath = Get-Command "vpk" -ErrorAction SilentlyContinue
    if (-not $vpkPath) {
        Write-Error "Failed to install Velopack CLI. Please run 'dotnet tool install -g vpk --version 0.0.626' manually."
        exit 1
    }
}

Write-Host "Velopack CLI found" -ForegroundColor Green

# Clean dist directory
if (Test-Path $DistDir) {
    Remove-Item -Path $DistDir -Recurse -Force
}
New-Item -ItemType Directory -Path $DistDir -Force | Out-Null

# Define build targets
$targets = @()

if ($Platform -eq "all" -or $Platform -eq "windows") {
    $targets += @{
        Name = "Windows x64"
        Project = "src\Canopy.Windows\Canopy.Windows.csproj"
        Runtime = "win-x64"
        MainExe = "Canopy.Windows.exe"
        PackId = "Canopy"
        Icon = "src\Canopy.Windows\Assets\canopy.ico"
        Channel = if ($Channel) { $Channel } else { "win" }
    }
}

if ($Platform -eq "all" -or $Platform -eq "linux") {
    $targets += @{
        Name = "Linux x64"
        Project = "src\Canopy.Linux\Canopy.Linux.csproj"
        Runtime = "linux-x64"
        MainExe = "Canopy.Linux"
        PackId = "Canopy"
        Icon = $null  # Linux uses different icon format
        Channel = if ($Channel) { $Channel } else { "linux" }
    }
}

# Build each target
foreach ($target in $targets) {
    Write-Host ""
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "Building $($target.Name)..." -ForegroundColor Cyan
    Write-Host "========================================" -ForegroundColor Cyan
    
    $projectPath = Join-Path $RootDir $target.Project
    
    if (-not (Test-Path $projectPath)) {
        Write-Host "  Skipping - project not found: $($target.Project)" -ForegroundColor Yellow
        continue
    }
    
    $publishDir = Join-Path $DistDir "publish-$($target.Runtime)"
    $releasesDir = Join-Path $DistDir "releases-$($target.Channel)"
    
    New-Item -ItemType Directory -Path $publishDir -Force | Out-Null
    New-Item -ItemType Directory -Path $releasesDir -Force | Out-Null
    
    # Publish the application
    Write-Host "Publishing application..." -ForegroundColor Yellow
    $publishArgs = @(
        "publish",
        $projectPath,
        "-c", $Configuration,
        "-r", $target.Runtime,
        "-o", $publishDir,
        "--self-contained", "true",
        "-p:Version=$Version",
        "-p:DebugType=none",
        "-p:DebugSymbols=false"
    )
    
    try {
        & dotnet @publishArgs
        
        if ($LASTEXITCODE -ne 0) {
            Write-Host "  Build failed for $($target.Name)" -ForegroundColor Red
            continue
        }
        
        Write-Host "  Build successful" -ForegroundColor Green
        
        # Create Velopack package
        Write-Host "Creating installer package..." -ForegroundColor Yellow
        
        $vpkArgs = @(
            "pack",
            "-u", $target.PackId,
            "-v", $Version,
            "-p", $publishDir,
            "-e", $target.MainExe,
            "-o", $releasesDir,
            "-c", $target.Channel,
            "--packTitle", "Canopy",
            "--packAuthors", "Canopy"
        )
        
        # Add icon if specified and exists
        if ($target.Icon) {
            $iconPath = Join-Path $RootDir $target.Icon
            if (Test-Path $iconPath) {
                $vpkArgs += @("-i", $iconPath)
            }
        }
        
        & vpk @vpkArgs
        
        if ($LASTEXITCODE -eq 0) {
            Write-Host "  Installer created successfully" -ForegroundColor Green
            
            # List created files
            Get-ChildItem $releasesDir -File | ForEach-Object {
                $size = [math]::Round($_.Length / 1MB, 2)
                Write-Host "    - $($_.Name) ($size MB)" -ForegroundColor Gray
            }
        }
        else {
            Write-Host "  Installer creation failed" -ForegroundColor Red
        }
        
        # Clean up publish directory to save space
        Remove-Item -Path $publishDir -Recurse -Force
    }
    catch {
        Write-Host "  Error building $($target.Name): $_" -ForegroundColor Red
    }
}

# Summary
Write-Host ""
Write-Host "=== Build Complete ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "Output directory: $DistDir" -ForegroundColor White
Write-Host ""

# List all releases directories
Get-ChildItem $DistDir -Directory -Filter "releases-*" | ForEach-Object {
    Write-Host "Platform: $($_.Name -replace 'releases-', '')" -ForegroundColor Cyan
    Get-ChildItem $_.FullName -File | ForEach-Object {
        $size = [math]::Round($_.Length / 1MB, 2)
        Write-Host "  - $($_.Name) ($size MB)" -ForegroundColor Gray
    }
    Write-Host ""
}

Write-Host "Distribution:" -ForegroundColor Cyan
Write-Host "  Windows: Run Canopy-win-Setup.exe for GUI installation" -ForegroundColor White
Write-Host "  Linux:   Run the AppImage or extract the portable package" -ForegroundColor White
