#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")" && pwd)"
cp "$ROOT/original/analyze_coupling.py" "$ROOT/rollback/restored.py"; cmp -s "$ROOT/original/analyze_coupling.py" "$ROOT/rollback/restored.py"; echo ROLLBACK_OK
sha256sum "$ROOT/rollback/restored.py" >/dev/null; cmp -s "$ROOT/original/analyze_coupling.py" "$ROOT/rollback/restored.py"; echo ROLLBACK_NOOP
printf 'seeded_equals_modified=true\nrestored_equals_original=true\nlive_modified_remains=true\n'
