#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Synchronize ROS2 For Unity package Samples~ files into the
# checked-in Unity demo project's imported sample copies.
# Usage: python Scripts/samples/sync_ros2_samples.py --dry-run
# Inputs: Packages/dev.unity2foxglove.ros2forunity/Samples~ and the
# imported Unity2Foxglove/Assets/Samples copy.
# Outputs: Reports drift, or copies package sample files when --apply is set.

from __future__ import annotations

import argparse
import filecmp
import shutil
import sys
from dataclasses import dataclass
from pathlib import Path


REPO_ROOT_PARENT_DEPTH = 2
EXIT_SUCCESS = 0
EXIT_FAILURE = 1

ROOT = Path(__file__).resolve().parents[REPO_ROOT_PARENT_DEPTH]
DEFAULT_PACKAGE_ROOT = ROOT / "Packages" / "dev.unity2foxglove.ros2forunity" / "Samples~"
DEFAULT_IMPORTED_ROOT = (
    ROOT
    / "Unity2Foxglove"
    / "Assets"
    / "Samples"
    / "Unity2Foxglove ROS2 For Unity"
    / "0.1.0-preview.1"
)

# The imported demo project extends the original string smoke with batch
# acceptance hooks. Keep that file intentionally imported-owned.
ALLOWLISTED_RELATIVE_FILES = {
    Path("ROS2 For Unity External Adapter") / "Phase110Ros2ForUnityStringSmoke.cs",
}

IGNORED_SUFFIXES = {
    ".meta",
}


@dataclass(frozen=True)
class Drift:
    """A single package/imported sample synchronization difference."""

    kind: str
    path: Path


def rel(path: Path, root: Path = ROOT) -> str:
    """Return a repository-relative display path when possible."""

    try:
        return path.resolve().relative_to(root.resolve()).as_posix()
    except ValueError:
        return str(path)


def resolve_cli_path(value: str | None, default: Path) -> Path:
    """Resolve an optional CLI path relative to the repository root."""

    if value is None:
        return default.resolve()
    path = Path(value)
    if not path.is_absolute():
        path = ROOT / path
    return path.resolve()


def is_ignored(path: Path) -> bool:
    """Return whether a sample-relative path is ignored for drift checks."""

    return path.suffix.lower() in IGNORED_SUFFIXES


def is_allowlisted(relative_path: Path) -> bool:
    """Return whether an imported sample drift is intentionally owned locally."""

    return relative_path in ALLOWLISTED_RELATIVE_FILES


def iter_files(root: Path) -> set[Path]:
    """Return all non-ignored file paths relative to a sample root."""

    if not root.exists():
        return set()
    files = set()
    for path in root.rglob("*"):
        if path.is_file():
            relative_path = path.relative_to(root)
            if not is_ignored(relative_path):
                files.add(relative_path)
    return files


def compare_roots(package_root: Path, imported_root: Path) -> list[Drift]:
    """Compare package and imported sample roots and return drift entries."""

    package_files = iter_files(package_root)
    imported_files = iter_files(imported_root)
    all_files = sorted(package_files | imported_files)
    drift: list[Drift] = []

    for relative_path in all_files:
        if is_allowlisted(relative_path):
            continue

        package_file = package_root / relative_path
        imported_file = imported_root / relative_path
        if relative_path not in package_files:
            drift.append(Drift("extra imported", relative_path))
            continue
        if relative_path not in imported_files:
            drift.append(Drift("missing imported", relative_path))
            continue
        if not filecmp.cmp(package_file, imported_file, shallow=False):
            drift.append(Drift("changed", relative_path))

    return drift


def apply_sync(package_root: Path, imported_root: Path, drift: list[Drift]) -> None:
    """Copy package-owned sample files for every fixable drift entry."""

    for item in drift:
        if item.kind == "extra imported":
            continue
        package_file = package_root / item.path
        imported_file = imported_root / item.path
        imported_file.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(package_file, imported_file)


def parse_args() -> argparse.Namespace:
    """Parse command-line arguments for sample synchronization."""

    parser = argparse.ArgumentParser(
        description="Validate or synchronize ROS2 For Unity package Samples~ into the imported Unity demo sample copy."
    )
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--apply", action="store_true", help="Copy package sample files into the imported sample copy.")
    mode.add_argument(
        "--dry-run",
        action="store_true",
        help="Only report drift. This is the default mode and exits non-zero when drift is found.",
    )
    parser.add_argument("--package-root", help="Package Samples~ root. Defaults to the repository package path.")
    parser.add_argument("--imported-root", help="Imported Unity sample root. Defaults to Unity2Foxglove/Assets/Samples.")
    return parser.parse_args()


def main() -> int:
    """Run the ROS2 sample synchronization check or apply mode."""

    args = parse_args()
    package_root = resolve_cli_path(args.package_root, DEFAULT_PACKAGE_ROOT)
    imported_root = resolve_cli_path(args.imported_root, DEFAULT_IMPORTED_ROOT)
    dry_run = not args.apply

    if not package_root.exists():
        print(f"[ros2-samples] FAIL: package root does not exist: {package_root}", file=sys.stderr)
        return EXIT_FAILURE
    if not imported_root.exists():
        print(f"[ros2-samples] FAIL: imported root does not exist: {imported_root}", file=sys.stderr)
        return EXIT_FAILURE

    print(f"[ros2-samples] package root: {rel(package_root)}")
    print(f"[ros2-samples] imported root: {rel(imported_root)}")
    print(f"[ros2-samples] mode: {'dry-run' if dry_run else 'apply'}")
    print("[ros2-samples] ignoring: *.meta")
    for allowlisted in sorted(ALLOWLISTED_RELATIVE_FILES):
        print(f"[ros2-samples] allowlisted imported drift: {allowlisted.as_posix()}")

    drift = compare_roots(package_root, imported_root)
    if not drift:
        print("[ros2-samples] GREEN: package and imported ROS2 samples are in sync.")
        return EXIT_SUCCESS

    for item in drift:
        print(f"[ros2-samples] {item.kind}: {item.path.as_posix()}")

    if dry_run:
        print(f"[ros2-samples] FAIL: {len(drift)} drift item(s) found. Re-run with --apply to synchronize.")
        return EXIT_FAILURE

    apply_sync(package_root, imported_root, drift)
    post_drift = compare_roots(package_root, imported_root)
    if post_drift:
        for item in post_drift:
            print(f"[ros2-samples] remaining {item.kind}: {item.path.as_posix()}")
        print(f"[ros2-samples] FAIL: {len(post_drift)} drift item(s) remain after apply.", file=sys.stderr)
        return EXIT_FAILURE

    print(f"[ros2-samples] GREEN: synchronized {len(drift)} drift item(s).")
    return EXIT_SUCCESS


if __name__ == "__main__":
    raise SystemExit(main())
