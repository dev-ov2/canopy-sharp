# Canopy Windows Build and Package Script
# Creates a GUI installer with options

param(
    [string]$Configuration = "Release",
    [Parameter(Mandatory=$true, HelpMessage="Version")]
    [string]$Version,
    [ValidateSet("x64", "x86", "arm64")]
    [string]$Architecture = "x64",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RootDir = Split-Path -Parent $ScriptDir
$ProjectPath = Join-Path $RootDir "src\Canopy.Windows\Canopy.Windows.csproj"
$SetupProjectPath = Join-Path $RootDir "src\Canopy.Setup\Canopy.Setup.csproj"
$DistDir = Join-Path $RootDir "dist"
$PublishDir = Join-Path $DistDir "publish"
$PackageDir = Join-Path $DistDir "package"
$ReleasesDir = Join-Path $DistDir "releases"

Write-Host "=== Canopy Build Script ===" -ForegroundColor Cyan
Write-Host "Configuration: $Configuration"
Write-Host "Version: $Version"
Write-Host "Architecture: $Architecture"
Write-Host ""

# Determine runtime identifier
$RuntimeId = "win-$Architecture"

# Step 1: Check for Velopack CLI (for creating the nupkg)
Write-Host "Checking for Velopack CLI..." -ForegroundColor Yellow
$vpkPath = Get-Command "vpk" -ErrorAction SilentlyContinue

if (-not $vpkPath) {
    Write-Host "Velopack CLI not found. Installing..." -ForegroundColor Yellow
    dotnet tool install -g vpk --version 0.0.626
    
    $env:Path = [System.Environment]::GetEnvironmentVariable("Path", "Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path", "User")
    
    $vpkPath = Get-Command "vpk" -ErrorAction SilentlyContinue
    if (-not $vpkPath) {
        Write-Error "Failed to install Velopack CLI."
        exit 1
    }
}

Write-Host "Velopack CLI found" -ForegroundColor Green

# Step 2: Clean and prepare directories
if (-not $SkipBuild) {
    Write-Host ""
    Write-Host "Cleaning previous build..." -ForegroundColor Yellow
    if (Test-Path $DistDir) {
        Remove-Item -Path $DistDir -Recurse -Force
    }
    New-Item -ItemType Directory -Path $DistDir -Force | Out-Null
    New-Item -ItemType Directory -Path $PublishDir -Force | Out-Null
    New-Item -ItemType Directory -Path $PackageDir -Force | Out-Null
    New-Item -ItemType Directory -Path $ReleasesDir -Force | Out-Null

    # Step 3: Build and publish the main application
    Write-Host ""
    Write-Host "Building Canopy application..." -ForegroundColor Yellow
    
    $publishArgs = @(
        "publish",
        $ProjectPath,
        "-c", $Configuration,
        "-r", $RuntimeId,
        "-o", $PublishDir,
        "--self-contained", "true",
        "-p:PublishSingleFile=false",
        "-p:Version=$Version",
        "-p:FileVersion=$Version",
        "-p:AssemblyVersion=$Version",
        "-p:DebugType=none",
        "-p:DebugSymbols=false"
    )
    
    & dotnet @publishArgs
    
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Build failed with exit code $LASTEXITCODE"
        exit $LASTEXITCODE
    }
    
    Write-Host "Application build completed!" -ForegroundColor Green
}

# Step 4: Ensure icons exist
$icoSource = Join-Path $RootDir "src\Canopy.Windows\Assets\canopy.ico"
$pngSource = Join-Path $RootDir "src\Canopy.Windows\Assets\canopy.png"
$publishAssetsDir = Join-Path $PublishDir "Assets"

if (-not (Test-Path $publishAssetsDir)) {
    New-Item -ItemType Directory -Path $publishAssetsDir -Force | Out-Null
}

if (Test-Path $icoSource) {
    Copy-Item $icoSource -Destination $publishAssetsDir -Force
}
if (Test-Path $pngSource) {
    Copy-Item $pngSource -Destination $publishAssetsDir -Force
}

# Step 5: Create Velopack package (nupkg)
Write-Host ""
Write-Host "Creating application package..." -ForegroundColor Yellow

$vpkArgs = @(
    "pack",
    "-u", "Canopy",
    "-v", $Version,
    "-p", $PublishDir,
    "-e", "Canopy.Windows.exe",
    "-o", $PackageDir,
    "-c", "win",
    "--packTitle", "Canopy",
    "--packAuthors", "Canopy"
)

if (Test-Path $icoSource) {
    $vpkArgs += @("-i", $icoSource)
}

& vpk @vpkArgs

if ($LASTEXITCODE -ne 0) {
    Write-Error "Package creation failed"
    exit $LASTEXITCODE
}

Write-Host "Package created!" -ForegroundColor Green

# Step 6: Build the custom Setup installer
Write-Host ""
Write-Host "Building custom Setup installer..." -ForegroundColor Yellow

# Copy the nupkg to the Setup project's Package folder
$setupPackageDir = Join-Path $RootDir "src\Canopy.Setup\Package"
New-Item -ItemType Directory -Path $setupPackageDir -Force | Out-Null

$nupkgFile = Get-ChildItem $PackageDir -Filter "*.nupkg" | Select-Object -First 1
if ($nupkgFile) {
    Copy-Item $nupkgFile.FullName -Destination $setupPackageDir -Force
    Write-Host "Copied package to Setup project" -ForegroundColor Gray
}

# Build and publish the Setup project
$setupPublishDir = Join-Path $DistDir "setup-publish"
$setupArgs = @(
    "publish",
    $SetupProjectPath,
    "-c", $Configuration,
    "-r", $RuntimeId,
    "-o", $setupPublishDir,
    "--self-contained", "true",
    "-p:PublishSingleFile=true",
    "-p:Version=$Version",
    "-p:DebugType=none",
    "-p:DebugSymbols=false"
)

& dotnet @setupArgs

if ($LASTEXITCODE -ne 0) {
    Write-Host "Custom installer build failed. Falling back to Velopack installer." -ForegroundColor Yellow
    
    # Copy Velopack's setup to releases
    Copy-Item "$PackageDir\*" -Destination $ReleasesDir -Recurse -Force
}
else {
    Write-Host "Custom installer built successfully!" -ForegroundColor Green
    
    # Copy the setup exe to releases
    $setupExe = Get-ChildItem $setupPublishDir -Filter "*.exe" | Select-Object -First 1
    if ($setupExe) {
        $finalName = "CanopySetup-$Version.exe"
        Copy-Item $setupExe.FullName -Destination (Join-Path $ReleasesDir $finalName) -Force
    }
    
    # Also copy the nupkg (for the installer to use)
    Copy-Item $nupkgFile.FullName -Destination $ReleasesDir -Force
    
    # Copy portable zip from Velopack output
    $portableZip = Get-ChildItem $PackageDir -Filter "*Portable.zip" | Select-Object -First 1
    if ($portableZip) {
        Copy-Item $portableZip.FullName -Destination $ReleasesDir -Force
    }
    
    # Clean up temp directories
    Remove-Item $setupPackageDir -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item $setupPublishDir -Recurse -Force -ErrorAction SilentlyContinue
}

# Clean up
Remove-Item $PackageDir -Recurse -Force -ErrorAction SilentlyContinue

# Summary
Write-Host ""
Write-Host "=== Build Complete ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "Output directory: $ReleasesDir" -ForegroundColor White
Write-Host ""
Write-Host "Created files:" -ForegroundColor White
Get-ChildItem $ReleasesDir -File | ForEach-Object {
    $size = [math]::Round($_.Length / 1MB, 2)
    Write-Host "  - $($_.Name) ($size MB)" -ForegroundColor Gray
}
Write-Host ""
Write-Host "Installation:" -ForegroundColor Cyan
Write-Host "  Run CanopySetup-$Version.exe for GUI installation with options:" -ForegroundColor White
Write-Host "    - Start with Windows" -ForegroundColor Gray
Write-Host "    - Create desktop shortcut" -ForegroundColor Gray
Write-Host "    - Create Start Menu shortcut" -ForegroundColor Gray
Write-Host "    - Launch after installation" -ForegroundColor Gray
