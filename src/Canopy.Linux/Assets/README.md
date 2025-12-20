# Canopy Linux Assets

Place your application icon here:

- `canopy.png` - Application icon (256x256 or larger, PNG format)

The icon is used for:
- Window title bar
- Taskbar/dock
- System tray (AppIndicator)
- Desktop entry
- Notifications

## Setup

Copy your `canopy.png` to this directory. The app will automatically:
1. Scale it to required sizes
2. Install to `~/.local/share/icons/hicolor/`
3. Update the icon cache

## Troubleshooting

If icons don't appear:

```bash
# Check icon exists
ls -la Assets/canopy.png

# Refresh icon cache
gtk-update-icon-cache -f -t ~/.local/share/icons/hicolor

# On GNOME, install AppIndicator extension
sudo pacman -S gnome-shell-extension-appindicator  # Arch
sudo apt install gnome-shell-extension-appindicator  # Ubuntu
```

## TODO

- [ ] Add overlay window support (see Windows implementation for reference)
