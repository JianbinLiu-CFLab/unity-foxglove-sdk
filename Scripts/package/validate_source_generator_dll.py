#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Rebuild the checked-in Roslyn source generator DLL and verify freshness.
# Usage: python Scripts/package/validate_source_generator_dll.py
#        python Scripts/package/validate_source_generator_dll.py --update

"""Validate that the checked-in source generator DLL matches a fresh Release build."""

from __future__ import annotations

import argparse
import hashlib
import shutil
import subprocess
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
PROJECT = REPO_ROOT / "Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/FoxgloveLogSourceGenerator.csproj"
CHECKED_IN_DLL = (
    REPO_ROOT
    / "Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/analyzers/dotnet/cs/FoxgloveLogSourceGenerator.dll"
)
BUILD_OUTPUT_DIR = REPO_ROOT / "build/SourceGenerators/Release/netstandard2.0"


def sha256(path: Path) -> str:
    """Return the SHA-256 hex digest for a file."""
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def run_build(build_output_dir: Path, msbuild_props: list[str]) -> bool:
    """Build the source generator project in Release mode."""
    command = [
        "dotnet",
        "build",
        str(PROJECT),
        *msbuild_props,
        "-c",
        "Release",
        "-o",
        str(build_output_dir),
        "-v:minimal",
    ]
    try:
        subprocess.run(command, cwd=REPO_ROOT, check=True)
    except subprocess.CalledProcessError as exc:
        print(f"[FAIL] Source generator Release build failed with exit code {exc.returncode}.", file=sys.stderr)
        print(f"       command: {' '.join(command)}", file=sys.stderr)
        return False
    return True


def validate_or_update(update: bool, build_output_dir: Path, msbuild_props: list[str]) -> int:
    """Validate or update the checked-in analyzer DLL."""
    if not PROJECT.exists():
        print(f"[FAIL] Source generator project missing: {PROJECT}", file=sys.stderr)
        return 1

    if not run_build(build_output_dir, msbuild_props):
        return 1

    built_dll = build_output_dir / "FoxgloveLogSourceGenerator.dll"
    if not built_dll.exists():
        print(f"[FAIL] Release build did not produce {built_dll}", file=sys.stderr)
        return 1

    if update:
        shutil.copy2(built_dll, CHECKED_IN_DLL)
        print(f"[PASS] Updated checked-in source generator DLL: {CHECKED_IN_DLL.relative_to(REPO_ROOT)}")
        print(f"       sha256={sha256(CHECKED_IN_DLL)}")
        return 0

    if not CHECKED_IN_DLL.exists():
        print(f"[FAIL] Checked-in analyzer DLL missing: {CHECKED_IN_DLL.relative_to(REPO_ROOT)}", file=sys.stderr)
        return 1

    built_hash = sha256(built_dll)
    checked_hash = sha256(CHECKED_IN_DLL)
    if built_dll.read_bytes() != CHECKED_IN_DLL.read_bytes():
        print("[FAIL] Checked-in source generator DLL is stale.", file=sys.stderr)
        print(f"       built:   {built_dll.relative_to(REPO_ROOT)} sha256={built_hash}", file=sys.stderr)
        print(f"       checked: {CHECKED_IN_DLL.relative_to(REPO_ROOT)} sha256={checked_hash}", file=sys.stderr)
        print("       Run: python Scripts/package/validate_source_generator_dll.py --update", file=sys.stderr)
        return 1

    print("[PASS] Checked-in source generator DLL matches a fresh Release build.")
    print(f"       sha256={checked_hash}")
    return 0


def main() -> int:
    """Parse command-line arguments and return a process exit code."""
    parser = argparse.ArgumentParser(description="Validate the checked-in source generator DLL.")
    parser.add_argument(
        "--update",
        action="store_true",
        help="Copy the fresh Release build over the checked-in analyzer DLL.",
    )
    parser.add_argument(
        "--build-output-dir",
        type=Path,
        default=BUILD_OUTPUT_DIR,
        help="Directory for the fresh Release build output.",
    )
    parser.add_argument(
        "--msbuild-prop",
        action="append",
        default=[],
        help="Additional MSBuild property argument to pass to dotnet build, such as -p:BaseOutputPath=...",
    )
    args = parser.parse_args()
    return validate_or_update(args.update, args.build_output_dir, args.msbuild_prop)


if __name__ == "__main__":
    raise SystemExit(main())
