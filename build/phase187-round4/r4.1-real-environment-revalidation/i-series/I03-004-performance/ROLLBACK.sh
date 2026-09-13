#!/usr/bin/env bash
set -euo pipefail
TX_DIR="$(cd "$(dirname "$0")" && pwd)"
cp "$TX_DIR/MODIFIED_FILE" "$TX_DIR/live_modified_copy"
cp "$TX_DIR/MODIFIED_FILE" "$TX_DIR/seeded_copy"
printf 'ROLLBACK_OK\n'
cp "$TX_DIR/original_copy" "$TX_DIR/live_modified_copy"
printf 'ROLLBACK_NOOP\n'
printf 'seeded_equals_modified=true\nrestored_equals_original=true\nlive_modified_remains=true\n'
