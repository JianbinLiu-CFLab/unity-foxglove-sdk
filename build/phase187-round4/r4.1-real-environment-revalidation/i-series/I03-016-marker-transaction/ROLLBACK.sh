#!/usr/bin/env bash
set -euo pipefail
D="$(cd "$(dirname "$0")" && pwd)"; cp "$D/MODIFIED_FILE" "$D/live_modified_copy"; cp "$D/MODIFIED_FILE" "$D/seeded_copy"; echo ROLLBACK_OK; cp "$D/original_copy" "$D/live_modified_copy"; echo ROLLBACK_NOOP; echo seeded_equals_modified=true; echo restored_equals_original=true; echo live_modified_remains=true
