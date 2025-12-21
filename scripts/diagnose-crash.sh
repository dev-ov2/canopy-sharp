#!/bin/bash
# Canopy Crash Diagnosis Script
# Extracts coredump information with full symbol resolution for debugging

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

# Check for required tools
echo -e "${YELLOW}Checking tools...${NC}"

HAVE_COREDUMPCTL=false
HAVE_DOTNET_DUMP=false
HAVE_LLDB=false
HAVE_GDB=false

if command -v coredumpctl &> /dev/null; then
    HAVE_COREDUMPCTL=true
    echo -e "  coredumpctl: ${GREEN}found${NC}"
else
    echo -e "  coredumpctl: ${RED}not found${NC}"
fi

if command -v dotnet-dump &> /dev/null; then
    HAVE_DOTNET_DUMP=true
    echo -e "  dotnet-dump: ${GREEN}found${NC}"
else
    echo -e "  dotnet-dump: ${YELLOW}not found${NC} (install: dotnet tool install -g dotnet-dump)"
fi

if command -v lldb &> /dev/null; then
    HAVE_LLDB=true
    echo -e "  lldb: ${GREEN}found${NC}"
else
    echo -e "  lldb: ${YELLOW}not found${NC}"
fi

if command -v gdb &> /dev/null; then
    HAVE_GDB=true
    echo -e "  gdb: ${GREEN}found${NC}"
else
    echo -e "  gdb: ${YELLOW}not found${NC}"
fi

echo ""

if [ "$HAVE_COREDUMPCTL" = false ]; then
    echo -e "${RED}Error: coredumpctl not found${NC}"
    echo "Install systemd-coredump:"
    echo "  Arch: sudo pacman -S systemd"
    echo "  Ubuntu: sudo apt install systemd-coredump"
    exit 1
fi

# Find Canopy coredumps
echo -e "${YELLOW}Looking for Canopy coredumps...${NC}"
COREDUMPS=$(coredumpctl list 2>/dev/null | grep -iE "canopy|Canopy\.Linux" | head -10 || true)

if [ -z "$COREDUMPS" ]; then
    echo -e "${RED}No Canopy coredumps found.${NC}"
    echo ""
    echo "If Canopy crashed but no coredump exists:"
    echo "  1. Check if coredumps are enabled:"
    echo "     cat /proc/sys/kernel/core_pattern"
    echo "  2. Enable systemd coredump handler:"
    echo "     echo '|/usr/lib/systemd/systemd-coredump %P %u %g %s %t %c %h' | sudo tee /proc/sys/kernel/core_pattern"
    echo "  3. Or set: ulimit -c unlimited"
    echo "  4. Run Canopy again and let it crash"
    exit 1
fi

echo ""
echo "Recent coredumps:"
echo "$COREDUMPS"
echo ""

# Create output directory
mkdir -p "$OUTPUT_DIR"
REPORT_FILE="$OUTPUT_DIR/crash-report-$TIMESTAMP.txt"
CORE_FILE="$OUTPUT_DIR/canopy-$TIMESTAMP.core"

echo -e "${YELLOW}Generating crash report...${NC}"

# Write header
cat > "$REPORT_FILE" << EOF
================================================================================
                         CANOPY CRASH REPORT
================================================================================
Generated: $(date)
Hostname: $(hostname)
User: $(whoami)

================================================================================
                            SYSTEM INFO
================================================================================
OS: $(cat /etc/os-release 2>/dev/null | grep PRETTY_NAME | cut -d'"' -f2 || uname -s)
Kernel: $(uname -r)
Arch: $(uname -m)
Uptime: $(uptime)

================================================================================
                             .NET INFO
================================================================================
EOF

dotnet --info 2>/dev/null >> "$REPORT_FILE" || echo ".NET SDK not installed" >> "$REPORT_FILE"

cat >> "$REPORT_FILE" << EOF

================================================================================
                          GTK/WEBKIT INFO
================================================================================
GTK3: $(pkg-config --modversion gtk+-3.0 2>/dev/null || echo "not found")
WebKitGTK: $(pkg-config --modversion webkit2gtk-4.0 2>/dev/null || pkg-config --modversion webkit2gtk-4.1 2>/dev/null || echo "not found")
libayatana-appindicator: $(pkg-config --modversion ayatana-appindicator-0.1 2>/dev/null || echo "not found")
libayatana-appindicator3: $(pkg-config --modversion ayatana-appindicator3-0.1 2>/dev/null || echo "not found")

================================================================================
                           ENVIRONMENT
================================================================================
XDG_SESSION_TYPE: ${XDG_SESSION_TYPE:-not set}
XDG_CURRENT_DESKTOP: ${XDG_CURRENT_DESKTOP:-not set}
DESKTOP_SESSION: ${DESKTOP_SESSION:-not set}
DISPLAY: ${DISPLAY:-not set}
WAYLAND_DISPLAY: ${WAYLAND_DISPLAY:-not set}
LANG: ${LANG:-not set}
LC_ALL: ${LC_ALL:-not set}

================================================================================
                        CANOPY INSTALLATION
================================================================================
Install Dir: $INSTALL_DIR
EOF

if [ -d "$INSTALL_DIR" ]; then
    echo "Installed: Yes" >> "$REPORT_FILE"
    echo "" >> "$REPORT_FILE"
    echo "Executable:" >> "$REPORT_FILE"
    ls -la "$INSTALL_DIR/Canopy.Linux" 2>/dev/null >> "$REPORT_FILE" || echo "  Not found" >> "$REPORT_FILE"
    echo "" >> "$REPORT_FILE"
    echo "Debug symbols (PDB files):" >> "$REPORT_FILE"
    find "$INSTALL_DIR" -name "*.pdb" -exec ls -la {} \; 2>/dev/null >> "$REPORT_FILE" || echo "  No PDB files found - stack traces will show '??'" >> "$REPORT_FILE"
    echo "" >> "$REPORT_FILE"
    echo "All files:" >> "$REPORT_FILE"
    ls -la "$INSTALL_DIR" 2>/dev/null >> "$REPORT_FILE" || echo "  Could not list files" >> "$REPORT_FILE"
else
    echo "Installed: No (directory not found)" >> "$REPORT_FILE"
    echo "" >> "$REPORT_FILE"
    echo "WARNING: Installation directory not found. Debug symbols may be missing." >> "$REPORT_FILE"
fi

cat >> "$REPORT_FILE" << EOF

================================================================================
                         RECENT COREDUMPS
================================================================================
EOF
coredumpctl list 2>/dev/null | grep -iE "canopy|Canopy\.Linux" | head -10 >> "$REPORT_FILE" || echo "None found" >> "$REPORT_FILE"

cat >> "$REPORT_FILE" << EOF

================================================================================
                       COREDUMP DETAILS
================================================================================
EOF
echo "Running: coredumpctl info" >> "$REPORT_FILE"
coredumpctl info 2>&1 >> "$REPORT_FILE" || echo "Could not get coredump info" >> "$REPORT_FILE"

# Extract coredump
echo -e "${YELLOW}Extracting coredump...${NC}"
if coredumpctl -o "$CORE_FILE" dump 2>/dev/null; then
    CORE_SIZE=$(du -h "$CORE_FILE" | cut -f1)
    echo -e "  Coredump extracted: ${GREEN}$CORE_FILE${NC} ($CORE_SIZE)"
    
    cat >> "$REPORT_FILE" << EOF

================================================================================
                      MANAGED STACK TRACES
================================================================================
EOF

    if [ "$HAVE_DOTNET_DUMP" = true ]; then
        echo -e "${YELLOW}Analyzing with dotnet-dump (this may take a moment)...${NC}"
        
        echo "" >> "$REPORT_FILE"
        echo "--- CLR Threads ---" >> "$REPORT_FILE"
        echo "Command: dotnet-dump analyze $CORE_FILE -c 'clrthreads'" >> "$REPORT_FILE"
        echo "" >> "$REPORT_FILE"
        timeout 60 dotnet-dump analyze "$CORE_FILE" -c "clrthreads" 2>&1 >> "$REPORT_FILE" || echo "Failed or timed out" >> "$REPORT_FILE"
        
        echo "" >> "$REPORT_FILE"
        echo "--- Exception Info ---" >> "$REPORT_FILE"
        echo "Command: dotnet-dump analyze $CORE_FILE -c 'pe'" >> "$REPORT_FILE"
        echo "" >> "$REPORT_FILE"
        timeout 60 dotnet-dump analyze "$CORE_FILE" -c "pe" 2>&1 >> "$REPORT_FILE" || echo "Failed or timed out" >> "$REPORT_FILE"
        
        echo "" >> "$REPORT_FILE"
        echo "--- Print Exception (verbose) ---" >> "$REPORT_FILE"
        echo "Command: dotnet-dump analyze $CORE_FILE -c 'pe -nested'" >> "$REPORT_FILE"
        echo "" >> "$REPORT_FILE"
        timeout 60 dotnet-dump analyze "$CORE_FILE" -c "pe -nested" 2>&1 >> "$REPORT_FILE" || echo "Failed or timed out" >> "$REPORT_FILE"
        
        echo "" >> "$REPORT_FILE"
        echo "--- All Managed Stacks ---" >> "$REPORT_FILE"
        echo "Command: dotnet-dump analyze $CORE_FILE -c 'clrstack -all'" >> "$REPORT_FILE"
        echo "" >> "$REPORT_FILE"
        timeout 120 dotnet-dump analyze "$CORE_FILE" -c "clrstack -all" 2>&1 >> "$REPORT_FILE" || echo "Failed or timed out" >> "$REPORT_FILE"
        
        echo "" >> "$REPORT_FILE"
        echo "--- Heap Statistics ---" >> "$REPORT_FILE"
        echo "Command: dotnet-dump analyze $CORE_FILE -c 'dumpheap -stat'" >> "$REPORT_FILE"
        echo "" >> "$REPORT_FILE"
        timeout 120 dotnet-dump analyze "$CORE_FILE" -c "dumpheap -stat" 2>&1 | head -100 >> "$REPORT_FILE" || echo "Failed or timed out" >> "$REPORT_FILE"
        
    else
        echo "dotnet-dump not installed. Install with: dotnet tool install -g dotnet-dump" >> "$REPORT_FILE"
    fi

    cat >> "$REPORT_FILE" << EOF

================================================================================
                       NATIVE STACK TRACES
================================================================================
EOF

    # Try LLDB first (better for .NET on Linux)
    if [ "$HAVE_LLDB" = true ]; then
        echo -e "${YELLOW}Getting native stack trace with lldb...${NC}"
        echo "" >> "$REPORT_FILE"
        echo "--- LLDB Backtrace (all threads) ---" >> "$REPORT_FILE"
        echo "Command: lldb -c $CORE_FILE -o 'bt all' -o 'quit'" >> "$REPORT_FILE"
        echo "" >> "$REPORT_FILE"
        
        # Try with the executable for better symbols
        if [ -f "$INSTALL_DIR/Canopy.Linux" ]; then
            timeout 60 lldb "$INSTALL_DIR/Canopy.Linux" -c "$CORE_FILE" -o "settings set target.load-script-from-symbol-file true" -o "bt all" -o "quit" 2>&1 >> "$REPORT_FILE" || true
        else
            timeout 60 lldb -c "$CORE_FILE" -o "bt all" -o "quit" 2>&1 >> "$REPORT_FILE" || true
        fi
    fi

    # Also try GDB
    if [ "$HAVE_GDB" = true ]; then
        echo -e "${YELLOW}Getting native stack trace with gdb...${NC}"
        echo "" >> "$REPORT_FILE"
        echo "--- GDB Backtrace (all threads) ---" >> "$REPORT_FILE"
        echo "Command: gdb -batch -ex 'thread apply all bt full'" >> "$REPORT_FILE"
        echo "" >> "$REPORT_FILE"
        
        if [ -f "$INSTALL_DIR/Canopy.Linux" ]; then
            timeout 60 gdb -batch \
                -ex "set pagination off" \
                -ex "set print pretty on" \
                -ex "thread apply all bt full" \
                "$INSTALL_DIR/Canopy.Linux" "$CORE_FILE" 2>&1 >> "$REPORT_FILE" || true
        else
            timeout 60 gdb -batch \
                -ex "set pagination off" \
                -ex "thread apply all bt" \
                -c "$CORE_FILE" 2>&1 >> "$REPORT_FILE" || true
        fi
    fi

else
    echo -e "${RED}Failed to extract coredump${NC}"
    echo "Could not extract coredump" >> "$REPORT_FILE"
fi

# Get Canopy logs
cat >> "$REPORT_FILE" << EOF

================================================================================
                          CANOPY LOGS
================================================================================
EOF

LOG_FILE="$HOME/.config/canopy/canopy.log"
if [ -f "$LOG_FILE" ]; then
    echo "Log file: $LOG_FILE" >> "$REPORT_FILE"
    echo "Size: $(du -h "$LOG_FILE" | cut -f1)" >> "$REPORT_FILE"
    echo "" >> "$REPORT_FILE"
    echo "--- Last 500 lines ---" >> "$REPORT_FILE"
    tail -500 "$LOG_FILE" >> "$REPORT_FILE"
else
    echo "No log file found at $LOG_FILE" >> "$REPORT_FILE"
fi

# System journal
cat >> "$REPORT_FILE" << EOF

================================================================================
                        SYSTEM JOURNAL
================================================================================
EOF

echo "--- Journal entries for canopy ---" >> "$REPORT_FILE"
journalctl --user -n 100 2>/dev/null | grep -i canopy >> "$REPORT_FILE" || true
journalctl -b -0 -n 200 2>/dev/null | grep -iE "canopy|segfault|SIGSEGV|SIGABRT" >> "$REPORT_FILE" || true

# Summary
echo ""
echo -e "${GREEN}=================================================================================${NC}"
echo -e "${GREEN}                        CRASH REPORT COMPLETE${NC}"
echo -e "${GREEN}=================================================================================${NC}"
echo ""
echo -e "Report: ${CYAN}$REPORT_FILE${NC}"
if [ -f "$CORE_FILE" ]; then
    echo -e "Coredump: ${CYAN}$CORE_FILE${NC} ($CORE_SIZE)"
fi
echo ""
echo -e "${YELLOW}Next steps:${NC}"
echo ""
echo "1. Review the report:"
echo "   less $REPORT_FILE"
echo ""
echo "2. If you see '??' in stack traces, debug symbols are missing."
echo "   Make sure PDB files exist in $INSTALL_DIR"
echo ""
echo "3. For interactive debugging with dotnet-dump:"
echo "   dotnet-dump analyze $CORE_FILE"
echo "   > help          # list commands"
echo "   > clrstack      # managed stack"
echo "   > pe            # print exception"
echo "   > threads       # list threads"
echo ""
echo "4. Create a GitHub issue with the report:"
echo "   https://github.com/dev-ov2/canopy-sharp/issues/new"
echo ""
echo -e "${YELLOW}Quick preview of crash:${NC}"
echo ""
# Show the most relevant part of the report
grep -A 50 "Exception Info" "$REPORT_FILE" 2>/dev/null | head -30 || \
grep -A 30 "SIGSEGV\|SIGABRT\|segfault" "$REPORT_FILE" 2>/dev/null | head -20 || \
tail -50 "$REPORT_FILE" | head -30
