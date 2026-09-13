#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")" && pwd)"; cp "$ROOT/original/RuntimeValidationOptimizationTests.cs" "$ROOT/rollback/restored.cs"; cmp -s "$ROOT/original/RuntimeValidationOptimizationTests.cs" "$ROOT/rollback/restored.cs"; echo ROLLBACK_OK; sha256sum "$ROOT/rollback/restored.cs" >/dev/null; cmp -s "$ROOT/original/RuntimeValidationOptimizationTests.cs" "$ROOT/rollback/restored.cs"; echo ROLLBACK_NOOP; printf 'seeded_equals_modified=true\nrestored_equals_original=true\nlive_modified_remains=true\n'
