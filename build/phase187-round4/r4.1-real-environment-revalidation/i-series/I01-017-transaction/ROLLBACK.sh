# ROLLBACK.sh - independent-copy rollback verification for I01-017
set -eu
ROOT="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
cp "$ROOT/MODIFIED_FILE" "$ROOT/rollback-copy.cs"
sha_mod=$(sha256sum "$ROOT/MODIFIED_FILE" | awk '{print $1}')
sha_seed=$(sha256sum "$ROOT/rollback-copy.cs" | awk '{print $1}')
printf 'ROLLBACK_OK\nseeded_equals_modified=%s\n' "$([ "$sha_seed" = "$sha_mod" ] && echo true || echo false)"
cp "$ROOT/original-copy.cs" "$ROOT/rollback-copy.cs"
sha_restored=$(sha256sum "$ROOT/rollback-copy.cs" | awk '{print $1}')
sha_orig=$(sha256sum "$ROOT/original-copy.cs" | awk '{print $1}')
printf 'restored_equals_original=%s\nlive_modified_remains=%s\n' "$([ "$sha_restored" = "$sha_orig" ] && echo true || echo false)" "$([ "$(sha256sum "$ROOT/MODIFIED_FILE" | awk '{print $1}')" = "$sha_mod" ] && echo true || echo false)"
printf 'ROLLBACK_NOOP\n'
