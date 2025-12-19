#!/bin/bash
# Canopy Uninstall Script
# This script removes Canopy from your system

set -e

echo "=== Canopy Uninstaller ==="
echo ""

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

INSTALL_DIR="$HOME/.local/share/canopy"
BIN_DIR="$HOME/.local/bin"
APPLICATIONS_DIR="$HOME/.local/share/applications"
ICONS_DIR="$HOME/.local/share/icons/hicolor"
CONFIG_DIR="$HOME/.config/canopy"
AUTOSTART_DIR="$HOME/.config/autostart"

echo -e "${YELLOW}Removing Canopy...${NC}"

# Remove application files
if [ -d "$INSTALL_DIR" ]; then
    rm -rf "$INSTALL_DIR"
    echo "  Removed: $INSTALL_DIR"
fi

# Remove symlink
if [ -L "$BIN_DIR/canopy" ]; then
    rm "$BIN_DIR/canopy"
    echo "  Removed: $BIN_DIR/canopy"
fi

# Remove desktop entry
if [ -f "$APPLICATIONS_DIR/canopy.desktop" ]; then
    rm "$APPLICATIONS_DIR/canopy.desktop"
    echo "  Removed: $APPLICATIONS_DIR/canopy.desktop"
fi

# Remove protocol handler
if [ -f "$APPLICATIONS_DIR/canopy-handler.desktop" ]; then
    rm "$APPLICATIONS_DIR/canopy-handler.desktop"
    echo "  Removed: $APPLICATIONS_DIR/canopy-handler.desktop"
fi

# Remove autostart entry
if [ -f "$AUTOSTART_DIR/canopy.desktop" ]; then
    rm "$AUTOSTART_DIR/canopy.desktop"
    echo "  Removed: $AUTOSTART_DIR/canopy.desktop"
fi

# Remove icons
for size in 48 64 128 256; do
    icon_path="$ICONS_DIR/${size}x${size}/apps/canopy.png"
    if [ -f "$icon_path" ]; then
        rm "$icon_path"
        echo "  Removed: $icon_path"
    fi
done

# Ask about config
echo ""
read -p "Do you want to remove configuration and logs? [y/N] " -n 1 -r
echo ""
if [[ $REPLY =~ ^[Yy]$ ]]; then
    if [ -d "$CONFIG_DIR" ]; then
        rm -rf "$CONFIG_DIR"
        echo "  Removed: $CONFIG_DIR"
    fi
fi

# Update caches
gtk-update-icon-cache "$ICONS_DIR" 2>/dev/null || true
update-desktop-database "$APPLICATIONS_DIR" 2>/dev/null || true

echo ""
echo -e "${GREEN}=== Canopy has been uninstalled ===${NC}"
echo ""
