#!/bin/bash
# Canopy Crash Diagnosis Script
# Extracts coredump information for debugging

set -e

# Colors
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m'

INSTALL_DIR="${INSTALL_DIR:-$HOME/.local/share/canopy}"
OUTPUT_DIR="${OUTPUT_DIR:-$HOME/canopy-crash-report}"
TIMESTAMP=$(date +%Y%m%d_%H%M%S)

echo -e "${CYAN}=== Canopy Crash Diagnosis ===${NC}"
echo ""

# Check if coredumpctl is available
if ! command -v coredumpctl &> /dev/null; then
    echo -e "${RED}Error: coredumpctl not found${NC}"
    echo "Install systemd-coredump:"
    echo "  Arch: sudo pacman -S systemd"
    echo "  Ubuntu: sudo apt install systemd-coredump"
    exit 1
fi

# Find Canopy coredumps
echo -e "${YELLOW}Looking for Canopy coredumps...${NC}"
COREDUMPS=$(coredumpctl list 2>/dev/null | grep -i "canopy\|Canopy" | head -5 || true)

if [ -z "$COREDUMPS" ]; then
    echo -e "${RED}No Canopy coredumps found.${NC}"
    echo ""
    echo "If Canopy crashed but no coredump exists:"
    echo "  1. Enable coredumps: ulimit -c unlimited"
    echo "  2. Or install systemd-coredump"
    echo "  3. Run Canopy again and let it crash"
    exit 1
fi

echo ""
echo "Found coredumps:"
echo "$COREDUMPS"
echo ""

# Create output directory
mkdir -p "$OUTPUT_DIR"
REPORT_FILE="$OUTPUT_DIR/crash-report-$TIMESTAMP.txt"

echo -e "${YELLOW}Generating crash report...${NC}"

# Write header
cat > "$REPORT_FILE" << EOF
=== Canopy Crash Report ===
Generated: $(date)
Hostname: $(hostname)
User: $(whoami)

=== System Info ===
OS: $(cat /etc/os-release 2>/dev/null | grep PRETTY_NAME | cut -d'"' -f2 || uname -s)
Kernel: $(uname -r)
Arch: $(uname -m)

=== .NET Info ===
$(dotnet --info 2>/dev/null || echo ".NET not found")

=== GTK Info ===
$(pkg-config --modversion gtk+-3.0 2>/dev/null || echo "GTK3 not found")
$(pkg-config --modversion webkit2gtk-4.0 2>/dev/null || echo "WebKitGTK not found")

=== Environment ===
XDG_SESSION_TYPE: ${XDG_SESSION_TYPE:-not set}
XDG_CURRENT_DESKTOP: ${XDG_CURRENT_DESKTOP:-not set}
DISPLAY: ${DISPLAY:-not set}
WAYLAND_DISPLAY: ${WAYLAND_DISPLAY:-not set}

=== Canopy Installation ===
Install Dir: $INSTALL_DIR
EOF

if [ -d "$INSTALL_DIR" ]; then
    echo "Installed: Yes" >> "$REPORT_FILE"
    echo "Files:" >> "$REPORT_FILE"
    ls -la "$INSTALL_DIR" 2>/dev/null >> "$REPORT_FILE" || echo "Could not list files" >> "$REPORT_FILE"
    echo "" >> "$REPORT_FILE"
    echo "PDB files (debug symbols):" >> "$REPORT_FILE"
    find "$INSTALL_DIR" -name "*.pdb" 2>/dev/null >> "$REPORT_FILE" || echo "No PDB files found" >> "$REPORT_FILE"
else
    echo "Installed: No (directory not found)" >> "$REPORT_FILE"
fi

echo "" >> "$REPORT_FILE"
echo "=== Recent Coredumps ===" >> "$REPORT_FILE"
coredumpctl list 2>/dev/null | grep -i "canopy\|Canopy" | head -10 >> "$REPORT_FILE" || true

echo "" >> "$REPORT_FILE"
echo "=== Latest Coredump Info ===" >> "$REPORT_FILE"
coredumpctl info 2>/dev/null | head -100 >> "$REPORT_FILE" || echo "Could not get coredump info" >> "$REPORT_FILE"

# Get stack trace if possible
echo "" >> "$REPORT_FILE"
echo "=== Stack Trace ===" >> "$REPORT_FILE"

# Try to get managed stack trace with dotnet-dump
if command -v dotnet-dump &> /dev/null; then
    echo "Extracting coredump for analysis..." >> "$REPORT_FILE"
    CORE_FILE="$OUTPUT_DIR/canopy-$TIMESTAMP.core"
    
    if coredumpctl -o "$CORE_FILE" dump 2>/dev/null; then
        echo "" >> "$REPORT_FILE"
        echo "--- Managed Threads ---" >> "$REPORT_FILE"
        timeout 30 dotnet-dump analyze "$CORE_FILE" -c "clrthreads" 2>&1 >> "$REPORT_FILE" || true
        
        echo "" >> "$REPORT_FILE"
        echo "--- Exception Info ---" >> "$REPORT_FILE"
        timeout 30 dotnet-dump analyze "$CORE_FILE" -c "pe" 2>&1 >> "$REPORT_FILE" || true
        
        echo "" >> "$REPORT_FILE"
        echo "--- All Stacks ---" >> "$REPORT_FILE"
        timeout 60 dotnet-dump analyze "$CORE_FILE" -c "clrstack -all" 2>&1 >> "$REPORT_FILE" || true
        
        echo "" >> "$REPORT_FILE"
        echo "Coredump saved to: $CORE_FILE" >> "$REPORT_FILE"
    else
        echo "Could not extract coredump" >> "$REPORT_FILE"
    fi
else
    echo "dotnet-dump not installed. Install with:" >> "$REPORT_FILE"
    echo "  dotnet tool install -g dotnet-dump" >> "$REPORT_FILE"
    echo "" >> "$REPORT_FILE"
    
    # Fallback to gdb if available
    if command -v gdb &> /dev/null; then
        echo "--- GDB Backtrace ---" >> "$REPORT_FILE"
        CORE_FILE="$OUTPUT_DIR/canopy-$TIMESTAMP.core"
        if coredumpctl -o "$CORE_FILE" dump 2>/dev/null; then
            timeout 30 gdb -batch -ex "thread apply all bt" "$INSTALL_DIR/Canopy.Linux" "$CORE_FILE" 2>&1 >> "$REPORT_FILE" || true
        fi
    fi
fi

# Get recent logs
echo "" >> "$REPORT_FILE"
echo "=== Recent Canopy Logs ===" >> "$REPORT_FILE"
LOG_FILE="$HOME/.config/canopy/canopy.log"
if [ -f "$LOG_FILE" ]; then
    echo "Log file: $LOG_FILE" >> "$REPORT_FILE"
    tail -200 "$LOG_FILE" >> "$REPORT_FILE"
else
    echo "No log file found at $LOG_FILE" >> "$REPORT_FILE"
fi

# Get journal logs
echo "" >> "$REPORT_FILE"
echo "=== System Journal (Canopy) ===" >> "$REPORT_FILE"
journalctl --user -u canopy 2>/dev/null | tail -50 >> "$REPORT_FILE" || true
journalctl -b -0 | grep -i canopy 2>/dev/null | tail -50 >> "$REPORT_FILE" || true

echo ""
echo -e "${GREEN}=== Crash Report Generated ===${NC}"
echo ""
echo -e "Report saved to: ${CYAN}$REPORT_FILE${NC}"
if [ -f "$OUTPUT_DIR/canopy-$TIMESTAMP.core" ]; then
    CORE_SIZE=$(du -h "$OUTPUT_DIR/canopy-$TIMESTAMP.core" | cut -f1)
    echo -e "Coredump saved to: ${CYAN}$OUTPUT_DIR/canopy-$TIMESTAMP.core${NC} ($CORE_SIZE)"
fi
echo ""
echo -e "${YELLOW}To share this report:${NC}"
echo "  1. Review $REPORT_FILE for sensitive info"
echo "  2. Create a GitHub issue at:"
echo "     https://github.com/dev-ov2/canopy-sharp/issues/new"
echo "  3. Attach the report file (and coredump if requested)"
echo ""
echo -e "${YELLOW}Quick view of crash:${NC}"
echo ""
tail -50 "$REPORT_FILE" | head -30
