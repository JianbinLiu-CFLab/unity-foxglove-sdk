#!/usr/bin/env bash
set -euo pipefail
TX_DIR="$(cd "$(dirname "$0")" && pwd)"; ORIGINAL="$TX_DIR/original.copy"; SEEDED="$TX_DIR/seeded.copy"; MODIFIED="$TX_DIR/MODIFIED_FILE"; ROLLBACK="$TX_DIR/rollback.copy"
cp "$MODIFIED" "$SEEDED"; cp "$SEEDED" "$ROLLBACK"; printf 'seeded_equals_modified=%s\n' "$(cmp -s "$SEEDED" "$MODIFIED" && echo true || echo false)"; cp "$ORIGINAL" "$ROLLBACK"; echo ROLLBACK_OK; printf 'restored_equals_original=%s\n' "$(cmp -s "$ROLLBACK" "$ORIGINAL" && echo true || echo false)"; printf 'live_modified_remains=%s\n' "$(cmp -s "$MODIFIED" "$SEEDED" && echo true || echo false)"; echo ROLLBACK_NOOP
