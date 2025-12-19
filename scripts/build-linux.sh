#!/bin/bash
# Canopy Linux Build Script
# Creates a self-contained distribution package

set -e

# Configuration
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
echo "Configuration: $CONFIGURATION"
echo "Runtime: $RUNTIME"
echo ""

# Check .NET SDK
if ! command -v dotnet &> /dev/null; then
    echo -e "${RED}Error: .NET SDK not found${NC}"
    echo "Please install .NET 8 SDK: https://dot.net/download"
    exit 1
fi

DOTNET_VERSION=$(dotnet --version)
echo -e "${GREEN}Found .NET SDK: $DOTNET_VERSION${NC}"

# Clean dist directory
echo -e "${YELLOW}Cleaning dist directory...${NC}"
rm -rf "$DIST_DIR"
mkdir -p "$DIST_DIR"

# Publish directory
PUBLISH_DIR="$DIST_DIR/publish"
PACKAGE_NAME="canopy-$VERSION-linux-x64"
PACKAGE_DIR="$DIST_DIR/$PACKAGE_NAME"

# Build and publish
echo -e "${YELLOW}Building and publishing...${NC}"
dotnet publish "$PROJECT_FILE" \
    -c "$CONFIGURATION" \
    -r "$RUNTIME" \
    -o "$PUBLISH_DIR" \
    --self-contained true \
    -p:Version="$VERSION" \
    -p:PublishSingleFile=false \
    -p:DebugType=none \
    -p:DebugSymbols=false

if [ $? -ne 0 ]; then
    echo -e "${RED}Build failed${NC}"
    exit 1
fi

echo -e "${GREEN}Build successful${NC}"

# Create package directory structure
echo -e "${YELLOW}Creating package...${NC}"
mkdir -p "$PACKAGE_DIR"
mkdir -p "$PACKAGE_DIR/lib"
mkdir -p "$PACKAGE_DIR/share/applications"
mkdir -p "$PACKAGE_DIR/share/icons/hicolor/256x256/apps"

# Copy application files
cp -r "$PUBLISH_DIR/"* "$PACKAGE_DIR/lib/"

# Make main executable
chmod +x "$PACKAGE_DIR/lib/Canopy.Linux"

# Copy icon if exists
ICON_SRC="$PROJECT_DIR/Assets/canopy.png"
if [ -f "$ICON_SRC" ]; then
    cp "$ICON_SRC" "$PACKAGE_DIR/share/icons/hicolor/256x256/apps/canopy.png"
fi

# Create launcher script
cat > "$PACKAGE_DIR/canopy" << 'EOF'
#!/bin/bash
# Canopy Launcher Script
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
export LD_LIBRARY_PATH="$SCRIPT_DIR/lib:$LD_LIBRARY_PATH"
exec "$SCRIPT_DIR/lib/Canopy.Linux" "$@"
EOF
chmod +x "$PACKAGE_DIR/canopy"

# Create desktop entry
cat > "$PACKAGE_DIR/share/applications/canopy.desktop" << EOF
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
EOF

# Create install script
cat > "$PACKAGE_DIR/install.sh" << 'EOF'
#!/bin/bash
# Canopy Install Script
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
INSTALL_DIR="${INSTALL_DIR:-$HOME/.local/share/canopy}"
BIN_DIR="${BIN_DIR:-$HOME/.local/bin}"
APPLICATIONS_DIR="$HOME/.local/share/applications"
ICONS_DIR="$HOME/.local/share/icons/hicolor/256x256/apps"

echo "Installing Canopy to $INSTALL_DIR..."

# Create directories
mkdir -p "$INSTALL_DIR"
mkdir -p "$BIN_DIR"
mkdir -p "$APPLICATIONS_DIR"
mkdir -p "$ICONS_DIR"

# Copy files
cp -r "$SCRIPT_DIR/lib/"* "$INSTALL_DIR/"
chmod +x "$INSTALL_DIR/Canopy.Linux"

# Create symlink
ln -sf "$INSTALL_DIR/Canopy.Linux" "$BIN_DIR/canopy"

# Install icon
if [ -f "$SCRIPT_DIR/share/icons/hicolor/256x256/apps/canopy.png" ]; then
    cp "$SCRIPT_DIR/share/icons/hicolor/256x256/apps/canopy.png" "$ICONS_DIR/"
fi

# Install desktop entry
sed "s|Exec=canopy|Exec=$INSTALL_DIR/Canopy.Linux|g" \
    "$SCRIPT_DIR/share/applications/canopy.desktop" > "$APPLICATIONS_DIR/canopy.desktop"

# Update desktop database
update-desktop-database "$APPLICATIONS_DIR" 2>/dev/null || true

# Register protocol handler
xdg-mime default canopy.desktop x-scheme-handler/canopy 2>/dev/null || true

echo ""
echo "Canopy installed successfully!"
echo ""
echo "You can now:"
echo "  - Run 'canopy' from the terminal"
echo "  - Find 'Canopy' in your application menu"
echo ""
EOF
chmod +x "$PACKAGE_DIR/install.sh"

# Create uninstall script
cat > "$PACKAGE_DIR/uninstall.sh" << 'EOF'
#!/bin/bash
# Canopy Uninstall Script
INSTALL_DIR="${INSTALL_DIR:-$HOME/.local/share/canopy}"
BIN_DIR="${BIN_DIR:-$HOME/.local/bin}"
APPLICATIONS_DIR="$HOME/.local/share/applications"
ICONS_DIR="$HOME/.local/share/icons/hicolor/256x256/apps"

echo "Uninstalling Canopy..."

rm -rf "$INSTALL_DIR"
rm -f "$BIN_DIR/canopy"
rm -f "$APPLICATIONS_DIR/canopy.desktop"
rm -f "$ICONS_DIR/canopy.png"

update-desktop-database "$APPLICATIONS_DIR" 2>/dev/null || true

echo "Canopy uninstalled."
EOF
chmod +x "$PACKAGE_DIR/uninstall.sh"

# Create tar.gz archive
echo -e "${YELLOW}Creating archive...${NC}"
cd "$DIST_DIR"
tar -czf "$PACKAGE_NAME.tar.gz" "$PACKAGE_NAME"

# Calculate size
ARCHIVE_SIZE=$(du -h "$PACKAGE_NAME.tar.gz" | cut -f1)

# Clean up
rm -rf "$PUBLISH_DIR"

echo ""
echo -e "${GREEN}=== Build Complete ===${NC}"
echo ""
echo -e "Output: ${CYAN}$DIST_DIR/$PACKAGE_NAME.tar.gz${NC} ($ARCHIVE_SIZE)"
echo ""
echo "To install:"
echo "  1. Extract: tar -xzf $PACKAGE_NAME.tar.gz"
echo "  2. Run: cd $PACKAGE_NAME && ./install.sh"
echo ""
echo "Or run directly without installing:"
echo "  cd $PACKAGE_NAME && ./canopy"
echo ""
