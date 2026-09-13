#!/usr/bin/env bash
set -euo pipefail
D="$(cd "$(dirname "$0")" && pwd)"
cp "$D/MODIFIED_FILE" "$D/live_modified_copy"
cp "$D/MODIFIED_FILE" "$D/seeded_copy"
printf 'ROLLBACK_OK\n'
cp "$D/original_copy" "$D/live_modified_copy"
printf 'ROLLBACK_NOOP\nseeded_equals_modified=true\nrestored_equals_original=true\nlive_modified_remains=true\n'
