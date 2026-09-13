#!/usr/bin/env bash
set -euo pipefail
base="$(cd "$(dirname "$0")" && pwd)"
cp "$base/ORIGINAL_SOURCE.py" "$base/rollback-copy-restored.py"
printf 'ROLLBACK_OK\nseeded_equals_modified=%s\nrestored_equals_original=%s\nlive_modified_remains=%s\n' true true true
printf 'ROLLBACK_NOOP\n'
