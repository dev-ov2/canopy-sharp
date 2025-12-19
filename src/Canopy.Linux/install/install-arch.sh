#!/bin/bash
# Canopy Install Script for Arch Linux / CachyOS
# This script installs Canopy and its dependencies

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

# Detect desktop environment
DE="${XDG_CURRENT_DESKTOP:-unknown}"
echo -e "${YELLOW}Detected desktop environment: $DE${NC}"

# Install dependencies
echo -e "${YELLOW}Installing dependencies...${NC}"
sudo pacman -S --needed --noconfirm \
    dotnet-runtime-8.0 \
    gtk3 \
    webkit2gtk \
    libnotify \
    xdg-utils \
    libx11

# Install AppIndicator library (required for system tray on most DEs)
echo -e "${YELLOW}Installing AppIndicator support...${NC}"
if pacman -Ss libayatana-appindicator | grep -q "libayatana-appindicator"; then
    sudo pacman -S --needed --noconfirm libayatana-appindicator || true
fi

# Try AUR packages if yay/paru available
if command -v yay &> /dev/null; then
    echo -e "${YELLOW}Installing AUR dependencies...${NC}"
    yay -S --needed --noconfirm libappindicator-gtk3 2>/dev/null || true
elif command -v paru &> /dev/null; then
    echo -e "${YELLOW}Installing AUR dependencies...${NC}"
    paru -S --needed --noconfirm libappindicator-gtk3 2>/dev/null || true
fi

# Create installation directory
INSTALL_DIR="$HOME/.local/share/canopy"
BIN_DIR="$HOME/.local/bin"
APPLICATIONS_DIR="$HOME/.local/share/applications"
ICONS_DIR="$HOME/.local/share/icons/hicolor"
PIXMAPS_DIR="$HOME/.local/share/pixmaps"

echo -e "${YELLOW}Creating directories...${NC}"
mkdir -p "$INSTALL_DIR"
mkdir -p "$BIN_DIR"
mkdir -p "$APPLICATIONS_DIR"
mkdir -p "$ICONS_DIR/256x256/apps"
mkdir -p "$ICONS_DIR/128x128/apps"
mkdir -p "$ICONS_DIR/64x64/apps"
mkdir -p "$ICONS_DIR/48x48/apps"
mkdir -p "$ICONS_DIR/24x24/apps"
mkdir -p "$ICONS_DIR/16x16/apps"
mkdir -p "$PIXMAPS_DIR"

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

# Install icons
if [ -f "$INSTALL_DIR/Assets/canopy.png" ]; then
    echo -e "${YELLOW}Installing icons...${NC}"
    
    # Copy to hicolor icon theme (standard location)
    cp "$INSTALL_DIR/Assets/canopy.png" "$ICONS_DIR/256x256/apps/canopy.png"
    
    # Create scaled versions if ImageMagick is available
    if command -v convert &> /dev/null; then
        convert "$INSTALL_DIR/Assets/canopy.png" -resize 128x128 "$ICONS_DIR/128x128/apps/canopy.png"
        convert "$INSTALL_DIR/Assets/canopy.png" -resize 64x64 "$ICONS_DIR/64x64/apps/canopy.png"
        convert "$INSTALL_DIR/Assets/canopy.png" -resize 48x48 "$ICONS_DIR/48x48/apps/canopy.png"
        convert "$INSTALL_DIR/Assets/canopy.png" -resize 24x24 "$ICONS_DIR/24x24/apps/canopy.png"
        convert "$INSTALL_DIR/Assets/canopy.png" -resize 16x16 "$ICONS_DIR/16x16/apps/canopy.png"
    else
        # Just copy the same file if no ImageMagick
        cp "$INSTALL_DIR/Assets/canopy.png" "$ICONS_DIR/128x128/apps/canopy.png"
        cp "$INSTALL_DIR/Assets/canopy.png" "$ICONS_DIR/64x64/apps/canopy.png"
        cp "$INSTALL_DIR/Assets/canopy.png" "$ICONS_DIR/48x48/apps/canopy.png"
        cp "$INSTALL_DIR/Assets/canopy.png" "$ICONS_DIR/24x24/apps/canopy.png"
        cp "$INSTALL_DIR/Assets/canopy.png" "$ICONS_DIR/16x16/apps/canopy.png"
    fi
    
    # Also copy to pixmaps (fallback location)
    cp "$INSTALL_DIR/Assets/canopy.png" "$PIXMAPS_DIR/canopy.png"
else
    echo -e "${YELLOW}Warning: Icon file not found, icons will not be installed${NC}"
fi

# Create desktop entry
cat > "$APPLICATIONS_DIR/canopy.desktop" << EOF
[Desktop Entry]
Version=1.0
Type=Application
Name=Canopy
GenericName=Game Overlay
Comment=Game overlay and tracking application
Exec=$INSTALL_DIR/Canopy.Linux %u
Icon=canopy
Terminal=false
Categories=Game;Utility;
Keywords=games;overlay;tracking;
MimeType=x-scheme-handler/canopy;
StartupWMClass=canopy
StartupNotify=true
EOF

# Validate desktop entry
if command -v desktop-file-validate &> /dev/null; then
    desktop-file-validate "$APPLICATIONS_DIR/canopy.desktop" 2>/dev/null || true
fi

# Register protocol handler
xdg-mime default canopy.desktop x-scheme-handler/canopy 2>/dev/null || true

# Update icon cache
if command -v gtk-update-icon-cache &> /dev/null; then
    gtk-update-icon-cache -f -t "$ICONS_DIR" 2>/dev/null || true
fi

# Update desktop database
if command -v update-desktop-database &> /dev/null; then
    update-desktop-database "$APPLICATIONS_DIR" 2>/dev/null || true
fi

echo ""
echo -e "${GREEN}=== Canopy installed successfully! ===${NC}"
echo ""
echo "You can now run Canopy by:"
echo "  - Searching for 'Canopy' in your application menu"
echo "  - Running 'canopy' in the terminal"
echo ""
echo "To enable autostart, open Canopy Settings and enable 'Start with System'."
echo ""

# Desktop-specific notes
case "$DE" in
    *GNOME*|*gnome*)
        echo -e "${YELLOW}GNOME detected:${NC}"
        echo "For system tray support, install the AppIndicator extension:"
        echo "  https://extensions.gnome.org/extension/615/appindicator-support/"
        echo "Or via: sudo pacman -S gnome-shell-extension-appindicator"
        ;;
    *KDE*|*Plasma*|*kde*|*plasma*)
        echo -e "${YELLOW}KDE Plasma detected:${NC}"
        echo "System tray should work out of the box."
        ;;
    *XFCE*|*xfce*)
        echo -e "${YELLOW}XFCE detected:${NC}"
        echo "System tray should work out of the box."
        ;;
    *)
        echo -e "${YELLOW}Note:${NC} If the tray icon doesn't appear,"
        echo "your desktop may need AppIndicator support enabled."
        ;;
esac
echo ""
