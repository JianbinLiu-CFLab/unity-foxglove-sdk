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
from dataclasses import dataclass
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
PROJECT = REPO_ROOT / "Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/FoxgloveLogSourceGenerator.csproj"
ANALYZER_DIRECTORY = REPO_ROOT / "Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/analyzers/dotnet/cs"
UNITY_PLUGIN_GOOGLE_PROTOBUF = REPO_ROOT / "Packages/dev.unity2foxglove.sdk/Plugins/Google.Protobuf/Google.Protobuf.dll"
CHECKED_IN_ARTIFACTS = {
    "FoxgloveLogSourceGenerator.dll": ANALYZER_DIRECTORY / "FoxgloveLogSourceGenerator.dll",
    "Google.Protobuf.dll": ANALYZER_DIRECTORY / "Google.Protobuf.dll",
}
BUILD_OUTPUT_DIR = REPO_ROOT / "build/SourceGenerators/Release/netstandard2.0"


@dataclass(frozen=True)
class AnalyzerTarget:
    """One independently packaged controlled analyzer."""

    name: str
    project: Path
    checked_in_artifacts: dict[str, Path]
    build_output_dir: Path
    validate_runtime_protobuf: bool = False


TARGETS = {
    "core": AnalyzerTarget(
        "core",
        PROJECT,
        CHECKED_IN_ARTIFACTS,
        BUILD_OUTPUT_DIR,
        validate_runtime_protobuf=True,
    ),
    "r2fu": AnalyzerTarget(
        "r2fu",
        REPO_ROOT
        / "Packages/dev.unity2foxglove.ros2forunity/Editor/SourceGenerators/FoxRunR2fuSourceGenerator.csproj",
        {
            "Unity2Foxglove.Ros2ForUnity.FoxRunSourceGenerator.dll":
                REPO_ROOT
                / "Packages/dev.unity2foxglove.ros2forunity/Editor/SourceGenerators/analyzers/dotnet/cs/Unity2Foxglove.Ros2ForUnity.FoxRunSourceGenerator.dll",
        },
        REPO_ROOT / "build/SourceGenerators/R2FU/validator/Release/netstandard2.0",
    ),
    "ros2bridge": AnalyzerTarget(
        "ros2bridge",
        REPO_ROOT
        / "Packages/dev.unity2foxglove.ros2bridge/Editor/SourceGenerators/FoxRunBridgeSourceGenerator.csproj",
        {
            "Unity2Foxglove.Ros2Bridge.FoxRunSourceGenerator.dll":
                REPO_ROOT
                / "Packages/dev.unity2foxglove.ros2bridge/Editor/SourceGenerators/analyzers/dotnet/cs/Unity2Foxglove.Ros2Bridge.FoxRunSourceGenerator.dll",
        },
        REPO_ROOT / "build/SourceGenerators/Ros2Bridge/validator/Release/netstandard2.0",
    ),
}


def sha256(path: Path) -> str:
    """Return the SHA-256 hex digest for a file."""
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def run_build(
    build_output_dir: Path = BUILD_OUTPUT_DIR,
    msbuild_props: list[str] | None = None,
    project: Path = PROJECT,
) -> bool:
    """Build the source generator project in Release mode."""
    msbuild_props = msbuild_props or []
    command = [
        "dotnet",
        "build",
        str(project),
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


def validate_unity_plugin_protobuf_match(analyzer_dependency: Path) -> bool:
    """Ensure the Unity runtime plug-in matches the supplied Protobuf dependency exactly."""
    if not UNITY_PLUGIN_GOOGLE_PROTOBUF.exists():
        print(
            "[FAIL] Unity runtime Google.Protobuf plug-in is missing: "
            f"{UNITY_PLUGIN_GOOGLE_PROTOBUF}",
            file=sys.stderr,
        )
        return False

    if sha256(analyzer_dependency) != sha256(UNITY_PLUGIN_GOOGLE_PROTOBUF):
        print(
            "[FAIL] Unity runtime Google.Protobuf plug-in differs from checked-in analyzer dependency.",
            file=sys.stderr,
        )
        print(f"       analyzer: {analyzer_dependency} sha256={sha256(analyzer_dependency)}", file=sys.stderr)
        print(
            f"       runtime:  {UNITY_PLUGIN_GOOGLE_PROTOBUF} "
            f"sha256={sha256(UNITY_PLUGIN_GOOGLE_PROTOBUF)}",
            file=sys.stderr,
        )
        return False

    return True


def validate_or_update(
    update: bool,
    build_output_dir: Path,
    msbuild_props: list[str],
    target: str = "core",
) -> int:
    """Validate or update the checked-in analyzer assembly and its dependencies."""
    selected = TARGETS[target]
    project = selected.project
    checked_in_artifacts = (
        CHECKED_IN_ARTIFACTS
        if target == "core"
        else selected.checked_in_artifacts
    )
    if not project.exists():
        print(f"[FAIL] Source generator project missing: {project}", file=sys.stderr)
        return 1

    if not run_build(build_output_dir, msbuild_props, project):
        return 1

    built_artifacts = {}
    for name in checked_in_artifacts:
        built = build_output_dir / name
        if not built.exists():
            print(f"[FAIL] Release build did not produce {built}", file=sys.stderr)
            return 1
        built_artifacts[name] = built

    if update:
        if (selected.validate_runtime_protobuf
                and not validate_unity_plugin_protobuf_match(
                    built_artifacts["Google.Protobuf.dll"])):
            return 1
        for name, checked_in in checked_in_artifacts.items():
            shutil.copy2(built_artifacts[name], checked_in)
            print(f"[PASS] Updated checked-in source generator artifact: {checked_in.relative_to(REPO_ROOT)}")
            print(f"       sha256={sha256(checked_in)}")
        return 0

    for name, checked_in in checked_in_artifacts.items():
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

    if (selected.validate_runtime_protobuf
            and not validate_unity_plugin_protobuf_match(
                checked_in_artifacts["Google.Protobuf.dll"])):
        return 1

    print(
        f"[PASS] Checked-in {selected.name} source generator artifacts "
        "match a fresh Release build."
    )
    for checked_in in checked_in_artifacts.values():
        print(f"       {checked_in.name}: sha256={sha256(checked_in)}")
    return 0


def main() -> int:
    """Parse command-line arguments and return a process exit code."""
    parser = argparse.ArgumentParser(description="Validate the checked-in source generator DLL.")
    parser.add_argument(
        "--target",
        choices=tuple(TARGETS),
        default="core",
        help="Controlled analyzer package to build and validate.",
    )
    parser.add_argument(
        "--update",
        action="store_true",
        help="Copy the fresh Release build over the checked-in analyzer DLL.",
    )
    parser.add_argument(
        "--build-output-dir",
        type=Path,
        default=None,
        help="Directory for the fresh Release build output.",
    )
    parser.add_argument(
        "--msbuild-prop",
        action="append",
        default=[],
        help="Additional MSBuild property argument to pass to dotnet build, such as -p:BaseOutputPath=...",
    )
    args = parser.parse_args()
    build_output_dir = (
        args.build_output_dir
        if args.build_output_dir is not None
        else TARGETS[args.target].build_output_dir
    )
    return validate_or_update(
        args.update,
        build_output_dir,
        args.msbuild_prop,
        args.target,
    )


if __name__ == "__main__":
    raise SystemExit(main())
