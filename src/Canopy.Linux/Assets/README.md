# Canopy Linux Assets

Place your application icon here:

- `canopy.png` - Application icon (256x256 PNG recommended)

## Required for:
- Window icon (title bar)
- Taskbar/dock icon
- System tray icon
- Desktop entry icon

## How to create/obtain the icon:

### Option 1: Copy from Windows build
If you have the Windows version, copy the icon:
```bash
# From the Windows project
cp ../Canopy.Windows/Assets/canopy.png ./canopy.png
```

### Option 2: Generate a placeholder icon
```bash
# Using ImageMagick
convert -size 256x256 xc:#1a1a2e -fill '#4ade80' \
    -draw "circle 128,128 128,40" \
    -fill '#1a1a2e' -draw "circle 128,128 128,60" \
    -fill '#4ade80' -pointsize 80 -gravity center \
    -annotate +0+0 "C" canopy.png

# Or using a simple colored square
convert -size 256x256 xc:'#4ade80' canopy.png
```

### Option 3: Download or create
Create a 256x256 PNG icon with transparent background and save as `canopy.png`.

## After adding the icon:

The icon will be automatically:
1. Copied to output during build
2. Used for all windows in the application
3. Installed to system icon directories when the app runs
4. Used for desktop notifications

## Troubleshooting

If icons don't appear:
1. Ensure `canopy.png` exists in this directory
2. Rebuild the application
3. Check logs for "icon not found" warnings
4. On GNOME, you may need the AppIndicator extension for tray icons
