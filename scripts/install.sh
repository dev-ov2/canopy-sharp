#!/bin/bash
# Canopy One-Click Installer for Linux
# Downloads the latest release from GitHub and installs it
#
# Usage:
#   curl -fsSL https://raw.githubusercontent.com/dev-ov2/canopy-sharp/main/scripts/install.sh | bash
#
# Or with a specific version:
#   VERSION=1.0.0 curl -fsSL ... | bash

set -e

# Configuration
GITHUB_REPO="dev-ov2/canopy-sharp"
INSTALL_DIR="$HOME/.local/share/canopy"
BIN_DIR="$HOME/.local/bin"

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m'

echo -e "${CYAN}"
echo "  ╔═══════════════════════════════════════╗"
echo "  ║         Canopy Linux Installer        ║"
echo "  ╚═══════════════════════════════════════╝"
echo -e "${NC}"

# Check for required tools
for cmd in curl tar; do
    if ! command -v $cmd &> /dev/null; then
        echo -e "${RED}Error: $cmd is required but not installed.${NC}"
        exit 1
    fi
done

# Get version (latest if not specified)
if [ -z "$VERSION" ]; then
    echo -e "${YELLOW}Fetching latest release...${NC}"
    RELEASE_INFO=$(curl -fsSL "https://api.github.com/repos/$GITHUB_REPO/releases/latest" 2>/dev/null || echo "")
    
    if [ -z "$RELEASE_INFO" ]; then
        echo -e "${RED}Error: Could not fetch release information.${NC}"
        echo "Check your internet connection or try specifying a version:"
        echo "  VERSION=1.0.0 $0"
        exit 1
    fi
    
    VERSION=$(echo "$RELEASE_INFO" | grep '"tag_name"' | sed -E 's/.*"v?([^"]+)".*/\1/')
    
    if [ -z "$VERSION" ]; then
        echo -e "${RED}Error: Could not determine latest version.${NC}"
        exit 1
    fi
fi

# Remove 'v' prefix if present
VERSION="${VERSION#v}"

echo -e "Version: ${GREEN}$VERSION${NC}"
echo ""

# Download URL
DOWNLOAD_URL="https://github.com/$GITHUB_REPO/releases/download/v$VERSION/canopy-$VERSION-linux-x64.tar.gz"
echo -e "${YELLOW}Downloading...${NC}"

# Create temp directory
TMP_DIR=$(mktemp -d)
trap "rm -rf $TMP_DIR" EXIT

# Download with progress
if ! curl -fSL --progress-bar "$DOWNLOAD_URL" -o "$TMP_DIR/canopy.tar.gz"; then
    echo -e "${RED}Error: Download failed.${NC}"
    echo ""
    echo "Check that version $VERSION exists at:"
    echo "  https://github.com/$GITHUB_REPO/releases"
    exit 1
fi

# Extract
echo -e "${YELLOW}Extracting...${NC}"
tar -xzf "$TMP_DIR/canopy.tar.gz" -C "$TMP_DIR"

# Find the extracted directory
EXTRACT_DIR=$(find "$TMP_DIR" -maxdepth 1 -type d -name "canopy-*" | head -1)
if [ -z "$EXTRACT_DIR" ]; then
    echo -e "${RED}Error: Could not find extracted directory.${NC}"
    exit 1
fi

# Check for lib directory or direct executable
if [ -d "$EXTRACT_DIR/lib" ]; then
    APP_SOURCE="$EXTRACT_DIR/lib"
elif [ -f "$EXTRACT_DIR/Canopy.Linux" ]; then
    APP_SOURCE="$EXTRACT_DIR"
else
    echo -e "${RED}Error: Invalid archive structure.${NC}"
    exit 1
fi

# Install
echo -e "${YELLOW}Installing to $INSTALL_DIR...${NC}"

# Remove old installation
rm -rf "$INSTALL_DIR"

# Create directories
mkdir -p "$INSTALL_DIR"
mkdir -p "$BIN_DIR"
mkdir -p "$HOME/.local/share/applications"
mkdir -p "$HOME/.local/share/icons/hicolor/256x256/apps"

# Copy files
cp -r "$APP_SOURCE/"* "$INSTALL_DIR/"
chmod +x "$INSTALL_DIR/Canopy.Linux"

# Create symlink in bin
ln -sf "$INSTALL_DIR/Canopy.Linux" "$BIN_DIR/canopy"

# Install icon
ICON_SRC=""
if [ -f "$EXTRACT_DIR/share/icons/hicolor/256x256/apps/canopy.png" ]; then
    ICON_SRC="$EXTRACT_DIR/share/icons/hicolor/256x256/apps/canopy.png"
elif [ -f "$INSTALL_DIR/Assets/canopy.png" ]; then
    ICON_SRC="$INSTALL_DIR/Assets/canopy.png"
fi

if [ -n "$ICON_SRC" ]; then
    cp "$ICON_SRC" "$HOME/.local/share/icons/hicolor/256x256/apps/canopy.png"
fi

# Install desktop entry
DESKTOP_SRC="$EXTRACT_DIR/share/applications/canopy.desktop"
if [ -f "$DESKTOP_SRC" ]; then
    sed "s|Exec=canopy|Exec=$INSTALL_DIR/Canopy.Linux|g" \
        "$DESKTOP_SRC" > "$HOME/.local/share/applications/canopy.desktop"
else
    cat > "$HOME/.local/share/applications/canopy.desktop" << EOF
[Desktop Entry]
Version=1.0
Type=Application
Name=Canopy
Comment=Game tracking application
Exec=$INSTALL_DIR/Canopy.Linux %u
Icon=canopy
Terminal=false
Categories=Game;Utility;
MimeType=x-scheme-handler/canopy;
StartupWMClass=canopy
EOF
fi

# Update caches (suppress errors)
update-desktop-database "$HOME/.local/share/applications" 2>/dev/null || true
gtk-update-icon-cache -q -t -f "$HOME/.local/share/icons/hicolor" 2>/dev/null || true
xdg-mime default canopy.desktop x-scheme-handler/canopy 2>/dev/null || true

echo ""
echo -e "${GREEN}════════════════════════════════════════${NC}"
echo -e "${GREEN}  Canopy v$VERSION installed!${NC}"
echo -e "${GREEN}════════════════════════════════════════${NC}"
echo ""
echo "Run Canopy:"
echo "  • Command: ${CYAN}canopy${NC}"
echo "  • Or find 'Canopy' in your application menu"
echo ""

# Check if bin dir is in PATH
if [[ ":$PATH:" != *":$BIN_DIR:"* ]]; then
    echo -e "${YELLOW}Note:${NC} ~/.local/bin is not in your PATH."
    echo "Add it with:"
    echo "  echo 'export PATH=\"\$HOME/.local/bin:\$PATH\"' >> ~/.bashrc && source ~/.bashrc"
    echo ""
    echo "Or run directly:"
    echo "  $INSTALL_DIR/Canopy.Linux"
    echo ""
fi

echo "Uninstall:"
echo "  rm -rf ~/.local/share/canopy ~/.local/bin/canopy ~/.local/share/applications/canopy.desktop"
echo ""
