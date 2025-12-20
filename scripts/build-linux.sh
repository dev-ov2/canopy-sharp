#!/bin/bash
# Canopy Linux Build Script
# Creates a self-contained distribution package for Arch Linux and other distros

set -e

VERSION="${VERSION:-1.0.0}"
CONFIGURATION="${CONFIGURATION:-Release}"
RUNTIME="linux-x64"

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m'

# Directories
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(dirname "$SCRIPT_DIR")"
SRC_DIR="$ROOT_DIR/src"
DIST_DIR="$ROOT_DIR/dist"
PROJECT_DIR="$SRC_DIR/Canopy.Linux"
PROJECT_FILE="$PROJECT_DIR/Canopy.Linux.csproj"

echo -e "${CYAN}=== Canopy Linux Build ===${NC}"
echo "Version: $VERSION"
echo "Runtime: $RUNTIME"
echo ""

# Check .NET SDK
if ! command -v dotnet &> /dev/null; then
    echo -e "${RED}Error: .NET SDK not found${NC}"
    echo "Install: sudo pacman -S dotnet-sdk-8.0"
    exit 1
fi

echo -e "${GREEN}Found .NET SDK: $(dotnet --version)${NC}"

# Clean and create dist directory
rm -rf "$DIST_DIR"
mkdir -p "$DIST_DIR"

PUBLISH_DIR="$DIST_DIR/publish"
PACKAGE_NAME="canopy-$VERSION-linux-x64"
PACKAGE_DIR="$DIST_DIR/$PACKAGE_NAME"

# Build
echo -e "${YELLOW}Building...${NC}"
dotnet publish "$PROJECT_FILE" \
    -c "$CONFIGURATION" \
    -r "$RUNTIME" \
    -o "$PUBLISH_DIR" \
    --self-contained true \
    -p:Version="$VERSION" \
    -p:PublishSingleFile=false \
    -p:DebugType=none \
    -p:DebugSymbols=false

echo -e "${GREEN}Build successful${NC}"

# Create package structure
echo -e "${YELLOW}Creating package...${NC}"
mkdir -p "$PACKAGE_DIR/lib"
mkdir -p "$PACKAGE_DIR/share/applications"
mkdir -p "$PACKAGE_DIR/share/icons/hicolor/256x256/apps"

cp -r "$PUBLISH_DIR/"* "$PACKAGE_DIR/lib/"
chmod +x "$PACKAGE_DIR/lib/Canopy.Linux"

# Icon
if [ -f "$PROJECT_DIR/Assets/canopy.png" ]; then
    cp "$PROJECT_DIR/Assets/canopy.png" "$PACKAGE_DIR/share/icons/hicolor/256x256/apps/"
fi

# Launcher script
cat > "$PACKAGE_DIR/canopy" << 'EOF'
#!/bin/bash
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
exec "$SCRIPT_DIR/lib/Canopy.Linux" "$@"
EOF
chmod +x "$PACKAGE_DIR/canopy"

# Desktop entry
cat > "$PACKAGE_DIR/share/applications/canopy.desktop" << EOF
[Desktop Entry]
Version=1.0
Type=Application
Name=Canopy
Comment=Game overlay and tracking
Exec=canopy %u
Icon=canopy
Terminal=false
Categories=Game;Utility;
MimeType=x-scheme-handler/canopy;
StartupWMClass=canopy
EOF

# Install script
cat > "$PACKAGE_DIR/install.sh" << 'INSTALL'
#!/bin/bash
set -e
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
INSTALL_DIR="${INSTALL_DIR:-$HOME/.local/share/canopy}"
BIN_DIR="$HOME/.local/bin"

echo "Installing Canopy..."

mkdir -p "$INSTALL_DIR" "$BIN_DIR"
mkdir -p "$HOME/.local/share/applications"
mkdir -p "$HOME/.local/share/icons/hicolor/256x256/apps"

cp -r "$SCRIPT_DIR/lib/"* "$INSTALL_DIR/"
chmod +x "$INSTALL_DIR/Canopy.Linux"
ln -sf "$INSTALL_DIR/Canopy.Linux" "$BIN_DIR/canopy"

[ -f "$SCRIPT_DIR/share/icons/hicolor/256x256/apps/canopy.png" ] && \
    cp "$SCRIPT_DIR/share/icons/hicolor/256x256/apps/canopy.png" \
       "$HOME/.local/share/icons/hicolor/256x256/apps/"

sed "s|Exec=canopy|Exec=$INSTALL_DIR/Canopy.Linux|g" \
    "$SCRIPT_DIR/share/applications/canopy.desktop" > \
    "$HOME/.local/share/applications/canopy.desktop"

update-desktop-database "$HOME/.local/share/applications" 2>/dev/null || true
xdg-mime default canopy.desktop x-scheme-handler/canopy 2>/dev/null || true

echo "Done! Run 'canopy' or find Canopy in your app menu."
INSTALL
chmod +x "$PACKAGE_DIR/install.sh"

# Uninstall script
cat > "$PACKAGE_DIR/uninstall.sh" << 'UNINSTALL'
#!/bin/bash
rm -rf "$HOME/.local/share/canopy"
rm -f "$HOME/.local/bin/canopy"
rm -f "$HOME/.local/share/applications/canopy.desktop"
rm -f "$HOME/.local/share/icons/hicolor/256x256/apps/canopy.png"
echo "Canopy uninstalled. Config remains in ~/.config/canopy/"
UNINSTALL
chmod +x "$PACKAGE_DIR/uninstall.sh"

# Create archive
cd "$DIST_DIR"
tar -czf "$PACKAGE_NAME.tar.gz" "$PACKAGE_NAME"
rm -rf "$PUBLISH_DIR"

ARCHIVE_SIZE=$(du -h "$PACKAGE_NAME.tar.gz" | cut -f1)

echo ""
echo -e "${GREEN}=== Build Complete ===${NC}"
echo ""
echo -e "Output: ${CYAN}$DIST_DIR/$PACKAGE_NAME.tar.gz${NC} ($ARCHIVE_SIZE)"
echo ""
echo "Install options:"
echo "  1. Extract and run install.sh"
echo "  2. Arch Linux: yay -S canopy-bin"
echo ""
