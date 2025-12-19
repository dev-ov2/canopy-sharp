#!/bin/bash
# Canopy Install Script for Arch Linux
# This script installs Canopy and its dependencies on Arch Linux

set -e

echo "=== Canopy Installer for Arch Linux ==="
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

# Install dependencies
echo -e "${YELLOW}Installing dependencies...${NC}"
sudo pacman -S --needed --noconfirm \
    dotnet-runtime-8.0 \
    gtk3 \
    webkit2gtk \
    libnotify \
    xdg-utils \
    libx11

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
