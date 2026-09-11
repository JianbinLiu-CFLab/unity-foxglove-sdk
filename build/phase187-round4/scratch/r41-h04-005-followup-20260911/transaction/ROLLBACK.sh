#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
WORK="$SCRIPT_DIR/rollback-copy"
rm -rf "$WORK"
mkdir -p "$WORK"
cp "$SCRIPT_DIR/MODIFIED_FILE" "$WORK/target.py"
cp "$SCRIPT_DIR/MODIFIED_FILE" "$WORK/seeded.py"
if cmp -s "$WORK/target.py" "$WORK/seeded.py"; then echo "seeded_equals_modified=true"; else echo "seeded_equals_modified=false"; exit 1; fi
cp "$SCRIPT_DIR/ORIGINAL_FILE" "$WORK/target.py"
if cmp -s "$WORK/target.py" "$SCRIPT_DIR/ORIGINAL_FILE"; then echo "ROLLBACK_OK"; echo "restored_equals_original=true"; else echo "ROLLBACK_OK"; echo "restored_equals_original=false"; exit 1; fi
if cmp -s "$WORK/target.py" "$SCRIPT_DIR/ORIGINAL_FILE"; then echo "ROLLBACK_NOOP"; else echo "ROLLBACK_NOOP"; exit 1; fi
if cmp -s "$SCRIPT_DIR/MODIFIED_FILE" "$WORK/seeded.py"; then echo "live_modified_remains=true"; else echo "live_modified_remains=false"; exit 1; fi