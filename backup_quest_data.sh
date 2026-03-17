#!/bin/bash
# =============================================================================
# backup_quest_data.sh
# Purpose: Pull data from Quest Pro to computer after each participant session
# Usage: ./backup_quest_data.sh P001
#        ./backup_quest_data.sh P001 /path/to/backup/folder
# =============================================================================

set -e  # Exit on any error

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# ---- Configuration ----
# The package name must match what you set in Unity Player Settings
PACKAGE_NAME="com.research.vrdatacollector"
QUEST_DATA_PATH="/sdcard/Android/data/${PACKAGE_NAME}/files/DataCollection/"

# Default backup directory (change this to your preferred location)
DEFAULT_BACKUP_DIR="${HOME}/STAR_VP_Data"

# ---- Parse arguments ----
PARTICIPANT_ID="${1:-}"
BACKUP_BASE="${2:-$DEFAULT_BACKUP_DIR}"

if [ -z "$PARTICIPANT_ID" ]; then
    echo -e "${RED}ERROR: Please provide participant ID${NC}"
    echo "Usage: $0 <PARTICIPANT_ID> [BACKUP_DIR]"
    echo "Example: $0 P001"
    echo "Example: $0 P001 /media/user/HDD3/Shini/STAR_VP/quest-pro-data/Raw"
    exit 1
fi

# ---- Create backup directory ----
DATE=$(date +%Y%m%d_%H%M%S)
BACKUP_DIR="${BACKUP_BASE}/${PARTICIPANT_ID}"
mkdir -p "$BACKUP_DIR"

echo "================================================"
echo " Quest Pro Data Backup"
echo " Participant: $PARTICIPANT_ID"
echo " Date: $DATE"
echo " Backup to: $BACKUP_DIR"
echo "================================================"

# ---- Check ADB connection ----
echo ""
echo -e "${YELLOW}[1/4] Checking Quest Pro connection...${NC}"

if ! command -v adb &> /dev/null; then
    echo -e "${RED}ERROR: adb not found!${NC}"
    echo "Install Android platform tools:"
    echo "  Ubuntu/Debian: sudo apt install android-tools-adb"
    echo "  Mac: brew install android-platform-tools"
    echo "  Or add Unity's Android SDK to PATH"
    exit 1
fi

DEVICES=$(adb devices | grep -c "device$")
if [ "$DEVICES" -eq 0 ]; then
    echo -e "${RED}ERROR: No Quest Pro connected!${NC}"
    echo "1. Connect Quest Pro via USB-C cable"
    echo "2. Put on headset and accept 'Allow USB Debugging' dialog"
    echo "3. Try again"
    exit 1
fi

echo -e "${GREEN}Quest Pro connected ✓${NC}"

# ---- Check data exists on device ----
echo ""
echo -e "${YELLOW}[2/4] Checking data on Quest Pro...${NC}"

FILE_COUNT=$(adb shell "ls ${QUEST_DATA_PATH}${PARTICIPANT_ID}/ 2>/dev/null | wc -l" || echo "0")
FILE_COUNT=$(echo "$FILE_COUNT" | tr -d '[:space:]')

if [ "$FILE_COUNT" = "0" ] || [ -z "$FILE_COUNT" ]; then
    # Try without participant subdirectory
    echo "  No participant folder found, checking root data directory..."
    FILE_COUNT=$(adb shell "ls ${QUEST_DATA_PATH} 2>/dev/null | wc -l" || echo "0")
    FILE_COUNT=$(echo "$FILE_COUNT" | tr -d '[:space:]')

    if [ "$FILE_COUNT" = "0" ] || [ -z "$FILE_COUNT" ]; then
        echo -e "${RED}ERROR: No data found on Quest Pro!${NC}"
        echo "  Checked: ${QUEST_DATA_PATH}"
        echo "  Make sure the data collection app ran and recorded data."
        exit 1
    fi

    echo "  Found $FILE_COUNT files in root data directory"
    echo ""
    echo -e "${YELLOW}[3/4] Pulling ALL data from Quest Pro...${NC}"
    adb pull "${QUEST_DATA_PATH}" "${BACKUP_DIR}/"
else
    echo "  Found $FILE_COUNT files for $PARTICIPANT_ID"
    echo ""
    echo -e "${YELLOW}[3/4] Pulling data for $PARTICIPANT_ID...${NC}"
    adb pull "${QUEST_DATA_PATH}${PARTICIPANT_ID}/" "${BACKUP_DIR}/"
fi

# ---- Verify backup ----
echo ""
echo -e "${YELLOW}[4/4] Verifying backup...${NC}"

LOCAL_FILES=$(find "$BACKUP_DIR" -name "*.csv" | wc -l)
if [ "$LOCAL_FILES" -gt 0 ]; then
    echo -e "${GREEN}Backup successful! ✓${NC}"
    echo ""
    echo "Files backed up:"
    find "$BACKUP_DIR" -name "*.csv" -exec echo "  {}" \;
    echo ""
    echo "Total CSV files: $LOCAL_FILES"
    echo "Backup location: $BACKUP_DIR"

    # Show file sizes
    echo ""
    echo "File sizes:"
    find "$BACKUP_DIR" -name "*.csv" -exec ls -lh {} \; | awk '{print "  " $5 "\t" $9}'

    # Quick data quality check
    echo ""
    echo "Quick data check (first file):"
    FIRST_CSV=$(find "$BACKUP_DIR" -name "head_*.csv" | head -1)
    if [ -n "$FIRST_CSV" ]; then
        ROW_COUNT=$(wc -l < "$FIRST_CSV")
        echo "  File: $(basename $FIRST_CSV)"
        echo "  Rows: $ROW_COUNT (including header)"
        echo "  Header: $(head -1 $FIRST_CSV)"
        echo "  First data row: $(sed -n '2p' $FIRST_CSV)"

        if [ "$ROW_COUNT" -lt 100 ]; then
            echo -e "  ${YELLOW}⚠️  Only $ROW_COUNT rows — seems short!${NC}"
        else
            echo -e "  ${GREEN}Row count looks good ✓${NC}"
        fi
    fi
else
    echo -e "${RED}WARNING: No CSV files found in backup!${NC}"
    echo "  The pull may have failed or data format is unexpected."
    echo "  Check: $BACKUP_DIR"
fi

echo ""
echo "================================================"
echo " Backup complete: $DATE"
echo " Run validation: python validate_collected_data.py $BACKUP_DIR"
echo "================================================"
