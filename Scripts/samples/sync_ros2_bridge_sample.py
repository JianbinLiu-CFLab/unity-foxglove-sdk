#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Synchronize the ROS2 Bridge package sample with its imported demo copy."""

from __future__ import annotations

import argparse
import filecmp
import json
import os
import shutil
import sys
import tempfile
from dataclasses import dataclass
from pathlib import Path


EXIT_SUCCESS = 0
EXIT_FAILURE = 1
ROOT = Path(__file__).resolve().parents[2]
PACKAGE_ID = "dev.unity2foxglove.ros2bridge"
PACKAGE_DISPLAY_NAME = "Unity2Foxglove ROS2 Bridge"
SAMPLE_DISPLAY_NAME = "ROS2 Bridge Sample"
SAMPLE_PACKAGE_PATH = Path("Samples~") / "Ros2BridgeSample"
GENERATED_SCENE = Path("Scenes") / "Ros2BridgeSample.unity"
DEFAULT_PACKAGE_ROOT = ROOT / "Packages" / PACKAGE_ID / SAMPLE_PACKAGE_PATH


@dataclass(frozen=True)
class Drift:
    """One byte-level difference between package and imported sample roots."""

    kind: str
    path: Path


def package_version(root: Path = ROOT) -> str:
    """Read the exact Bridge package version used by Unity's import path."""

    manifest = root / "Packages" / PACKAGE_ID / "package.json"
    with manifest.open("r", encoding="utf-8") as handle:
        data = json.load(handle)
    version = data.get("version")
    if not isinstance(version, str) or not version.strip():
        raise ValueError(f"Bridge package version is missing in {manifest}")
    return version


def default_imported_root(root: Path = ROOT) -> Path:
    """Return the demo-project path produced by Unity's sample importer."""

    return (
        root
        / "Unity2Foxglove"
        / "Assets"
        / "Samples"
        / PACKAGE_DISPLAY_NAME
        / package_version(root)
        / SAMPLE_DISPLAY_NAME
    )


def resolve_cli_path(value: str | None, default: Path) -> Path:
    """Resolve one optional CLI path relative to the repository root."""

    path = default if value is None else Path(value)
    if not path.is_absolute():
        path = ROOT / path
    return path.resolve()


def rel(path: Path, root: Path = ROOT) -> str:
    """Render a stable repository-relative path when possible."""

    try:
        return path.resolve().relative_to(root.resolve()).as_posix()
    except ValueError:
        return str(path)


def iter_files(root: Path) -> set[Path]:
    """Return every package-owned sample file, including Unity meta files."""

    if not root.exists():
        return set()
    return {
        path.relative_to(root)
        for path in root.rglob("*")
        if path.is_file()
    }


def compare_roots(package_root: Path, imported_root: Path) -> list[Drift]:
    """Compare package and imported roots byte for byte."""

    package_files = iter_files(package_root)
    imported_files = iter_files(imported_root)
    drift: list[Drift] = []
    for relative_path in sorted(package_files | imported_files):
        package_file = package_root / relative_path
        imported_file = imported_root / relative_path
        if relative_path not in package_files:
            drift.append(Drift("extra imported", relative_path))
        elif relative_path not in imported_files:
            drift.append(Drift("missing imported", relative_path))
        elif not filecmp.cmp(package_file, imported_file, shallow=False):
            drift.append(Drift("changed", relative_path))
    return drift


def _atomic_copy(source: Path, destination: Path) -> None:
    """Replace one destination without in-place truncation on sync drives."""

    destination.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile(
        dir=destination.parent,
        prefix=f".{destination.name}.",
        suffix=".tmp",
        delete=False,
    ) as temporary:
        temporary_path = Path(temporary.name)
    try:
        shutil.copyfile(source, temporary_path)
        os.replace(temporary_path, destination)
    finally:
        temporary_path.unlink(missing_ok=True)


def apply_sync(
    package_root: Path,
    imported_root: Path,
    drift: list[Drift],
) -> None:
    """Copy package-owned missing or changed files into the imported sample."""

    for item in drift:
        if item.kind == "extra imported":
            continue
        source = package_root / item.path
        if not source.is_file():
            raise FileNotFoundError(source)
        _atomic_copy(source, imported_root / item.path)


def capture_generated_scene(package_root: Path, imported_root: Path) -> None:
    """Copy only the Unity-generated scene back into the package sample."""

    source = imported_root / GENERATED_SCENE
    if not source.is_file():
        raise FileNotFoundError(
            "Unity-generated Bridge sample scene is missing: " + str(source)
        )
    _atomic_copy(source, package_root / GENERATED_SCENE)


def _validate_roots(package_root: Path, imported_root: Path) -> None:
    """Reject absent or aliased roots before any synchronization write."""

    if not package_root.is_dir():
        raise FileNotFoundError(
            "Bridge package sample root does not exist: " + str(package_root)
        )
    if package_root == imported_root:
        raise ValueError("Package and imported sample roots must be distinct.")


def parse_args() -> argparse.Namespace:
    """Parse explicit package-to-import and scene-capture modes."""

    parser = argparse.ArgumentParser(
        description=(
            "Validate or synchronize the ROS2 Bridge package sample and "
            "its checked-in Unity demo import."
        )
    )
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument(
        "--apply",
        action="store_true",
        help="Copy package-owned files into the imported demo sample.",
    )
    mode.add_argument(
        "--capture-generated-scene",
        action="store_true",
        help=(
            "Copy only the scene saved by the Unity sample builder from "
            "the imported sample back into Samples~."
        ),
    )
    mode.add_argument(
        "--dry-run",
        action="store_true",
        help="Report byte drift without writing. This is the default.",
    )
    parser.add_argument("--package-root")
    parser.add_argument("--imported-root")
    return parser.parse_args()


def main() -> int:
    """Run one fail-closed Bridge sample synchronization operation."""

    args = parse_args()
    package_root = resolve_cli_path(args.package_root, DEFAULT_PACKAGE_ROOT)
    imported_root = resolve_cli_path(
        args.imported_root,
        default_imported_root(ROOT),
    )
    try:
        _validate_roots(package_root, imported_root)
        if args.capture_generated_scene:
            capture_generated_scene(package_root, imported_root)
        elif args.apply:
            imported_root.mkdir(parents=True, exist_ok=True)
            apply_sync(
                package_root,
                imported_root,
                compare_roots(package_root, imported_root),
            )

        drift = compare_roots(package_root, imported_root)
    except (OSError, ValueError) as exception:
        print(
            f"[ros2-bridge-sample] FAIL: {exception}",
            file=sys.stderr,
        )
        return EXIT_FAILURE

    print(f"[ros2-bridge-sample] package: {rel(package_root)}")
    print(f"[ros2-bridge-sample] imported: {rel(imported_root)}")
    if drift:
        for item in drift:
            print(
                f"[ros2-bridge-sample] {item.kind}: "
                f"{item.path.as_posix()}"
            )
        print(
            f"[ros2-bridge-sample] FAIL: {len(drift)} drift item(s).",
            file=sys.stderr,
        )
        return EXIT_FAILURE

    print(
        "[ros2-bridge-sample] GREEN: package and imported sample "
        "are byte-identical."
    )
    return EXIT_SUCCESS


if __name__ == "__main__":
    raise SystemExit(main())
