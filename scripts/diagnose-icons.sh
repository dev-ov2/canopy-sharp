#!/bin/bash
# Canopy Icon Diagnostics Script
# Run this to check if icons are set up correctly

echo "=== Canopy Icon Diagnostics ==="
echo ""

# Check icon file in app directory
echo "1. Checking for icon in app directory..."
APP_DIR="$(dirname "$(readlink -f "$0")")"
if [ -f "$APP_DIR/Assets/canopy.png" ]; then
    echo "   ✓ Found: $APP_DIR/Assets/canopy.png"
    file "$APP_DIR/Assets/canopy.png"
else
    echo "   ✗ NOT FOUND: $APP_DIR/Assets/canopy.png"
fi
echo ""

# Check hicolor theme
echo "2. Checking hicolor icon theme..."
HICOLOR="$HOME/.local/share/icons/hicolor"
for size in 16 32 48 64 128 256; do
    ICON="$HICOLOR/${size}x${size}/apps/canopy.png"
    if [ -f "$ICON" ]; then
        echo "   ✓ Found: ${size}x${size}"
    else
        echo "   ✗ Missing: ${size}x${size}"
    fi
done
echo ""

# Check desktop entry
echo "3. Checking desktop entry..."
DESKTOP="$HOME/.local/share/applications/canopy.desktop"
if [ -f "$DESKTOP" ]; then
    echo "   ✓ Found: $DESKTOP"
    echo "   Contents:"
    grep -E "^(Name|Icon|StartupWMClass)=" "$DESKTOP" | sed 's/^/      /'
else
    echo "   ✗ NOT FOUND: $DESKTOP"
fi
echo ""

# Check WM_CLASS of running window
echo "4. Checking running Canopy window..."
if command -v xprop &> /dev/null; then
    WM_CLASS=$(xprop -root -notype _NET_CLIENT_LIST 2>/dev/null | grep -o '0x[0-9a-f]*' | head -20 | while read wid; do
        xprop -id "$wid" WM_CLASS 2>/dev/null | grep -i canopy && break
    done)
    if [ -n "$WM_CLASS" ]; then
        echo "   ✓ Found window: $WM_CLASS"
    else
        echo "   ? No Canopy window found or xprop not working"
    fi
else
    echo "   ? xprop not installed (install xorg-xprop)"
fi
echo ""

# Check icon cache
echo "5. Checking icon cache..."
CACHE="$HICOLOR/icon-theme.cache"
if [ -f "$CACHE" ]; then
    CACHE_TIME=$(stat -c %Y "$CACHE" 2>/dev/null || stat -f %m "$CACHE" 2>/dev/null)
    ICON_TIME=$(stat -c %Y "$HICOLOR/48x48/apps/canopy.png" 2>/dev/null || echo 0)
    if [ "$CACHE_TIME" -ge "$ICON_TIME" ] 2>/dev/null; then
        echo "   ✓ Cache is up to date"
    else
        echo "   ! Cache may be outdated, run: gtk-update-icon-cache -f -t $HICOLOR"
    fi
else
    echo "   ? No cache file (may be fine on some systems)"
fi
echo ""

# Suggestions
echo "=== Suggestions ==="
echo ""
echo "If icons are not showing:"
echo "1. Restart the application"
echo "2. Run: gtk-update-icon-cache -f -t ~/.local/share/icons/hicolor"
echo "3. Run: update-desktop-database ~/.local/share/applications"
echo "4. Log out and log back in (or restart your panel)"
echo ""
echo "For GNOME: Make sure AppIndicator extension is installed"
echo "   sudo pacman -S gnome-shell-extension-appindicator"
echo ""
