#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "$0")" && pwd)"
seed="$root/rollback-copy/FoxgloveBuild.cs"; mod="$root/modified/FoxgloveBuild.cs"; orig="$root/original/FoxgloveBuild.cs"; out="$root/rollback-copy/restored.cs"
seeded=$(cmp -s "$seed" "$mod" && echo true || echo false); cp "$orig" "$out"; restored=$(cmp -s "$out" "$orig" && echo true || echo false); live=$(cmp -s "$seed" "$mod" && echo true || echo false); printf "seeded_equals_modified=%s\nrestored_equals_original=%s\nlive_modified_remains=%s\nROLLBACK_OK\n" "$seeded" "$restored" "$live"; test "$seeded" = true -a "$restored" = true -a "$live" = true; printf "ROLLBACK_NOOP\n"
