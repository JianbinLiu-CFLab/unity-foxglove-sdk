#!/usr/bin/env bash
set -eu
D="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
sm=$(sha256sum "$D/MODIFIED_FILE"|awk '{print $1}'); ss=$(sha256sum "$D/rollback-copy.cs"|awk '{print $1}'); echo ROLLBACK_OK; echo seeded_equals_modified=$([ "$sm" = "$ss" ]&&echo true||echo false); cp "$D/original-copy.cs" "$D/rollback-copy.cs"; so=$(sha256sum "$D/original-copy.cs"|awk '{print $1}'); sr=$(sha256sum "$D/rollback-copy.cs"|awk '{print $1}'); echo restored_equals_original=$([ "$so" = "$sr" ]&&echo true||echo false); echo live_modified_remains=$([ "$(sha256sum "$D/MODIFIED_FILE"|awk '{print $1}')" = "$sm" ]&&echo true||echo false); echo ROLLBACK_NOOP
