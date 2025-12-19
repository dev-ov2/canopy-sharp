# Canopy Linux Assets

Place your application icon here:

- `canopy.png` - Application icon (any size 256x256+)

The icon will be automatically scaled to all required sizes (16x16 through 512x512).

## Required for:
- Window title bar icon
- Taskbar/dock icon
- System tray icon (AppIndicator)
- Desktop entry icon
- Notification icons

## Supported formats:
- PNG (recommended, supports transparency)
- Any size 256x256 or larger works best
- 512x512 is ideal as it scales down well

## How to add the icon:

Simply copy your `canopy.png` file to this directory (`src/Canopy.Linux/Assets/`).

The application will automatically:
1. Find the icon at runtime
2. Scale it to all required sizes
3. Install it to `~/.local/share/icons/hicolor/` for system integration
4. Update the GTK icon cache

## Troubleshooting

If icons don't appear:

1. **Check the icon exists**: `ls -la Assets/canopy.png`
2. **Check logs**: Look for "icon" or "Found icon" messages in the log file
3. **Restart after first run**: The first run installs icons; a restart may be needed for the tray
4. **On GNOME**: Install the AppIndicator extension:
   ```bash
   sudo pacman -S gnome-shell-extension-appindicator  # Arch
   sudo apt install gnome-shell-extension-appindicator  # Ubuntu
   ```
5. **Manually refresh icon cache**:
   ```bash
   gtk-update-icon-cache -f -t ~/.local/share/icons/hicolor
   ```

## Icon search paths

The application looks for the icon in these locations (in order):
1. `{app_directory}/Assets/canopy.png` (development/runtime)
2. `{app_directory}/canopy.png`
3. `~/.local/share/icons/hicolor/256x256/apps/canopy.png` (installed)
4. `/usr/share/icons/hicolor/256x256/apps/canopy.png` (system-wide)
5. `/usr/share/pixmaps/canopy.png`
