#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Synchronize Unity2Foxglove package version references.
# Usage: python Scripts/release/bump_version.py 1.3.0 --date 2026-05-12
# Inputs: Target semantic version, optional --date, optional --dry-run.
# Outputs: Updates package metadata, changelog, README, and release-note stubs unless --dry-run.

"""Synchronize Unity2Foxglove package version references.

This script updates the package version, the runtime package-metadata
validation assertion, README badges/notes, and release document stubs.
It intentionally does not create git commits, tags, or GitHub releases.
"""

from __future__ import annotations

import argparse
import json
import os
import re
import time
from contextlib import contextmanager
from dataclasses import dataclass
from datetime import date
from pathlib import Path

from pathlib import Path as _Phase192Path
_PHASE192_FACADE_FILE = _Phase192Path(__file__).resolve().parents[1] / "bump_version.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _Phase192Path, _PHASE192_FACADE_FILE
# Semantic version grammar accepted by the release helper.
VERSION_RE = re.compile(r"[0-9]+\.[0-9]+\.[0-9]+\Z")
SDK_DEPENDENT_PACKAGE_MANIFESTS = (
    "Packages/dev.unity2foxglove.ros2forunity/package.json",
    "Packages/dev.unity2foxglove.ros2bridge/package.json",
    "Packages/dev.unity2foxglove.remotegateway.win64/package.json",
)

# Process exit code for a successful synchronization or dry run.
EXIT_SUCCESS = 0
LOCK_WAIT_SECONDS = 30.0
LOCK_POLL_SECONDS = 0.05

# Number of parent directories between this file and the repository root.
REPO_ROOT_PARENT_DEPTH = 2

# Text replacements that update a single canonical occurrence.
SINGLE_REPLACEMENT = 1

# Regex capture groups for the package.json version replacement pattern.
VERSION_PROPERTY_PREFIX_GROUP = 1
VERSION_PROPERTY_SUFFIX_GROUP = 3


def parse_release_date(value: str) -> str:
    """Return one canonical ISO release date or raise an argparse error."""
    try:
        parsed = date.fromisoformat(value)
    except ValueError as exc:
        raise argparse.ArgumentTypeError(
            f"'{value}' is not a valid YYYY-MM-DD release date"
        ) from exc
    if parsed.isoformat() != value:
        raise argparse.ArgumentTypeError(
            f"'{value}' is not a canonical YYYY-MM-DD release date"
        )
    return value


def resolve_repo_root() -> Path:
    """Return the repository root and fail loudly if the script is moved."""
    root = Path(__file__).resolve().parents[REPO_ROOT_PARENT_DEPTH]
    package_json = root / "Packages/dev.unity2foxglove.sdk/package.json"
    changelog = root / "CHANGELOG.md"
    if not package_json.exists() or not changelog.exists():
        raise SystemExit(f"Unexpected repository root for bump_version.py: {root}")
    return root


@dataclass
class PlannedChange:
    """Records one file that would be changed, or was changed, by the bump."""

    path: Path
    action: str




__all__ = [name for name in globals() if not name.startswith("__")]
