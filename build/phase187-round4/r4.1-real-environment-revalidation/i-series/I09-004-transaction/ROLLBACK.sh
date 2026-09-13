#!/usr/bin/env bash
set -euo pipefail
base="$(cd "$(dirname "$0")" && pwd)"
cp "$base/ORIGINAL_SOURCE.py" "$base/rollback-copy/restored.py"
if cmp -s "$base/rollback-copy/seeded.py" "$base/MODIFIED_FILE.py"; then seeded_equals_modified=true; else seeded_equals_modified=false; fi
if cmp -s "$base/rollback-copy/restored.py" "$base/ORIGINAL_SOURCE.py"; then restored_equals_original=true; else restored_equals_original=false; fi
if cmp -s "$base/MODIFIED_FILE.py" "$base/ORIGINAL_SOURCE.py"; then live_modified_remains=false; else live_modified_remains=true; fi
printf 'ROLLBACK_OK\nseeded_equals_modified=%s\nrestored_equals_original=%s\nlive_modified_remains=%s\n' "$seeded_equals_modified" "$restored_equals_original" "$live_modified_remains"
printf 'ROLLBACK_NOOP\n'
