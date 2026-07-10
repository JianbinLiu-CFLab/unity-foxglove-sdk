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
ANALYZER_DIRECTORY = REPO_ROOT / "Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/analyzers/dotnet/cs"
CHECKED_IN_ARTIFACTS = {
    "FoxgloveLogSourceGenerator.dll": ANALYZER_DIRECTORY / "FoxgloveLogSourceGenerator.dll",
    "Google.Protobuf.dll": ANALYZER_DIRECTORY / "Google.Protobuf.dll",
}
BUILD_OUTPUT_DIR = REPO_ROOT / "build/SourceGenerators/Release/netstandard2.0"


def sha256(path: Path) -> str:
    """Return the SHA-256 hex digest for a file."""
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def run_build(build_output_dir: Path = BUILD_OUTPUT_DIR, msbuild_props: list[str] | None = None) -> bool:
    """Build the source generator project in Release mode."""
    msbuild_props = msbuild_props or []
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
    """Validate or update the checked-in analyzer assembly and its dependencies."""
    if not PROJECT.exists():
        print(f"[FAIL] Source generator project missing: {PROJECT}", file=sys.stderr)
        return 1

    if not run_build(build_output_dir, msbuild_props):
        return 1

    built_artifacts = {}
    for name in CHECKED_IN_ARTIFACTS:
        built = build_output_dir / name
        if not built.exists():
            print(f"[FAIL] Release build did not produce {built}", file=sys.stderr)
            return 1
        built_artifacts[name] = built

    if update:
        for name, checked_in in CHECKED_IN_ARTIFACTS.items():
            shutil.copy2(built_artifacts[name], checked_in)
            print(f"[PASS] Updated checked-in source generator artifact: {checked_in.relative_to(REPO_ROOT)}")
            print(f"       sha256={sha256(checked_in)}")
        return 0

    for name, checked_in in CHECKED_IN_ARTIFACTS.items():
        if not checked_in.exists():
            print(f"[FAIL] Checked-in analyzer artifact missing: {checked_in.relative_to(REPO_ROOT)}", file=sys.stderr)
            return 1

        built_hash = sha256(built_artifacts[name])
        checked_hash = sha256(checked_in)
        if built_hash != checked_hash:
            print(f"[FAIL] Checked-in source generator artifact is stale: {name}", file=sys.stderr)
            print(
                f"       built:   {built_artifacts[name].relative_to(REPO_ROOT)} sha256={built_hash}",
                file=sys.stderr,
            )
            print(f"       checked: {checked_in.relative_to(REPO_ROOT)} sha256={checked_hash}", file=sys.stderr)
            print("       Run: python Scripts/package/validate_source_generator_dll.py --update", file=sys.stderr)
            return 1

    print("[PASS] Checked-in source generator artifacts match a fresh Release build.")
    for checked_in in CHECKED_IN_ARTIFACTS.values():
        print(f"       {checked_in.name}: sha256={sha256(checked_in)}")
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
