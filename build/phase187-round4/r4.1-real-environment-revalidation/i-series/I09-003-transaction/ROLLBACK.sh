#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "$0")" && pwd)"
orig="$root/original/fetch_asset_smoke.py"
seed="$root/rollback-copy/fetch_asset_smoke.py"
out="$root/rollback-copy/restored.py"
cp "$seed" "$out"
seeded=$(cmp -s "$seed" "$root/modified/fetch_asset_smoke.py" && echo true || echo false)
cp "$orig" "$out"
restored=$(cmp -s "$out" "$orig" && echo true || echo false)
live=$(cmp -s "$seed" "$root/modified/fetch_asset_smoke.py" && echo true || echo false)
printf "seeded_equals_modified=%s\nrestored_equals_original=%s\nlive_modified_remains=%s\nROLLBACK_OK\n" "$seeded" "$restored" "$live"
if [ "$seeded" != true ] || [ "$restored" != true ] || [ "$live" != true ]; then exit 1; fi
printf "ROLLBACK_NOOP\n"
