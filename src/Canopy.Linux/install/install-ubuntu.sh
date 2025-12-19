#!/bin/bash
# Canopy Install Script for Ubuntu/Debian
# This script installs Canopy and its dependencies on Ubuntu-based distributions

set -e

echo "=== Canopy Installer for Ubuntu/Debian ==="
echo ""

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Check if running as root
if [[ $EUID -eq 0 ]]; then
   echo -e "${RED}This script should not be run as root${NC}"
   exit 1
fi

# Update package lists
echo -e "${YELLOW}Updating package lists...${NC}"
sudo apt-get update

# Install dependencies
echo -e "${YELLOW}Installing dependencies...${NC}"
sudo apt-get install -y \
    dotnet-runtime-8.0 \
    libgtk-3-0 \
    libwebkit2gtk-4.0-37 \
    libnotify-bin \
    xdg-utils \
    libx11-6

# If dotnet-runtime-8.0 is not available, try installing from Microsoft
if ! dpkg -l | grep -q dotnet-runtime-8.0; then
    echo -e "${YELLOW}Installing .NET 8 from Microsoft repository...${NC}"
    
    # Add Microsoft package repository
    wget https://packages.microsoft.com/config/ubuntu/$(lsb_release -rs)/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
    sudo dpkg -i packages-microsoft-prod.deb
    rm packages-microsoft-prod.deb
    
    sudo apt-get update
    sudo apt-get install -y dotnet-runtime-8.0
fi

# Create installation directory
INSTALL_DIR="$HOME/.local/share/canopy"
BIN_DIR="$HOME/.local/bin"
APPLICATIONS_DIR="$HOME/.local/share/applications"
ICONS_DIR="$HOME/.local/share/icons/hicolor"

echo -e "${YELLOW}Creating directories...${NC}"
mkdir -p "$INSTALL_DIR"
mkdir -p "$BIN_DIR"
mkdir -p "$APPLICATIONS_DIR"
mkdir -p "$ICONS_DIR/256x256/apps"
mkdir -p "$ICONS_DIR/128x128/apps"
mkdir -p "$ICONS_DIR/64x64/apps"
mkdir -p "$ICONS_DIR/48x48/apps"

# Copy application files
echo -e "${YELLOW}Installing Canopy...${NC}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
APP_DIR="$(dirname "$SCRIPT_DIR")"

# If running from published output
if [ -f "$APP_DIR/Canopy.Linux" ]; then
    cp -r "$APP_DIR/"* "$INSTALL_DIR/"
elif [ -f "$SCRIPT_DIR/../bin/Release/net8.0/linux-x64/publish/Canopy.Linux" ]; then
    cp -r "$SCRIPT_DIR/../bin/Release/net8.0/linux-x64/publish/"* "$INSTALL_DIR/"
else
    echo -e "${RED}Error: Cannot find Canopy binaries.${NC}"
    echo "Please run 'dotnet publish -c Release -r linux-x64 --self-contained' first."
    exit 1
fi

# Make executable
chmod +x "$INSTALL_DIR/Canopy.Linux"

# Create symlink
ln -sf "$INSTALL_DIR/Canopy.Linux" "$BIN_DIR/canopy"

# Ensure ~/.local/bin is in PATH
if ! echo "$PATH" | grep -q "$HOME/.local/bin"; then
    echo ""
    echo -e "${YELLOW}Note: Add ~/.local/bin to your PATH by adding this to ~/.bashrc:${NC}"
    echo '  export PATH="$HOME/.local/bin:$PATH"'
    echo ""
fi

# Install icon
if [ -f "$INSTALL_DIR/Assets/canopy.png" ]; then
    cp "$INSTALL_DIR/Assets/canopy.png" "$ICONS_DIR/256x256/apps/canopy.png"
    cp "$INSTALL_DIR/Assets/canopy.png" "$ICONS_DIR/128x128/apps/canopy.png"
    cp "$INSTALL_DIR/Assets/canopy.png" "$ICONS_DIR/64x64/apps/canopy.png"
    cp "$INSTALL_DIR/Assets/canopy.png" "$ICONS_DIR/48x48/apps/canopy.png"
fi

# Create desktop entry
cat > "$APPLICATIONS_DIR/canopy.desktop" << EOF
[Desktop Entry]
Type=Application
Name=Canopy
Comment=Game overlay and tracking application
Exec=$INSTALL_DIR/Canopy.Linux %u
Icon=canopy
Terminal=false
Categories=Game;Utility;
MimeType=x-scheme-handler/canopy;
StartupWMClass=canopy
EOF

# Register protocol handler
xdg-mime default canopy.desktop x-scheme-handler/canopy 2>/dev/null || true

# Update icon cache
gtk-update-icon-cache "$ICONS_DIR" 2>/dev/null || true

# Update desktop database
update-desktop-database "$APPLICATIONS_DIR" 2>/dev/null || true

echo ""
echo -e "${GREEN}=== Canopy installed successfully! ===${NC}"
echo ""
echo "You can now run Canopy by:"
echo "  - Searching for 'Canopy' in your application menu"
echo "  - Running 'canopy' in the terminal"
echo ""
echo "To enable autostart, open Canopy Settings and enable 'Start with System'."
echo ""
