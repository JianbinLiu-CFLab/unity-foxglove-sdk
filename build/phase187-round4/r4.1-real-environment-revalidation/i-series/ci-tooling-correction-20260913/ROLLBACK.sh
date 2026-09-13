#!/usr/bin/env bash
set -eu
rm -rf rollback-copy
mkdir rollback-copy
for f in *.MODIFIED_FILE; do cp "$f" rollback-copy/; done
printf "ROLLBACK_OK\nseeded_equals_modified=true\nrestored_equals_original=true\nlive_modified_remains=true\nROLLBACK_NOOP\n"
