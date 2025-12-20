#!/bin/bash
# Canopy Install Script
# Installs Canopy to ~/.local for the current user

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
INSTALL_DIR="$HOME/.local/share/canopy"
BIN_DIR="$HOME/.local/bin"

echo "Installing Canopy..."

# Create directories
mkdir -p "$INSTALL_DIR"
mkdir -p "$BIN_DIR"
mkdir -p "$HOME/.local/share/applications"
mkdir -p "$HOME/.local/share/icons/hicolor/256x256/apps"

# Find application files
if [ -d "$SCRIPT_DIR/lib" ]; then
    APP_DIR="$SCRIPT_DIR/lib"
elif [ -f "$SCRIPT_DIR/Canopy.Linux" ]; then
    APP_DIR="$SCRIPT_DIR"
else
    echo "Error: Cannot find Canopy files."
    exit 1
fi

# Copy application
cp -r "$APP_DIR/"* "$INSTALL_DIR/"
chmod +x "$INSTALL_DIR/Canopy.Linux"

# Create symlink
ln -sf "$INSTALL_DIR/Canopy.Linux" "$BIN_DIR/canopy"

# Install icon
if [ -f "$SCRIPT_DIR/share/icons/hicolor/256x256/apps/canopy.png" ]; then
    cp "$SCRIPT_DIR/share/icons/hicolor/256x256/apps/canopy.png" \
        "$HOME/.local/share/icons/hicolor/256x256/apps/"
elif [ -f "$INSTALL_DIR/Assets/canopy.png" ]; then
    cp "$INSTALL_DIR/Assets/canopy.png" \
        "$HOME/.local/share/icons/hicolor/256x256/apps/canopy.png"
fi

# Install desktop entry
if [ -f "$SCRIPT_DIR/share/applications/canopy.desktop" ]; then
    sed "s|Exec=canopy|Exec=$INSTALL_DIR/Canopy.Linux|g" \
        "$SCRIPT_DIR/share/applications/canopy.desktop" > \
        "$HOME/.local/share/applications/canopy.desktop"
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

# Update caches
update-desktop-database "$HOME/.local/share/applications" 2>/dev/null || true
gtk-update-icon-cache -q -t -f "$HOME/.local/share/icons/hicolor" 2>/dev/null || true
xdg-mime default canopy.desktop x-scheme-handler/canopy 2>/dev/null || true

echo ""
echo "Canopy installed!"
echo "  Run: canopy"
echo "  Or find 'Canopy' in your app menu"
echo ""
