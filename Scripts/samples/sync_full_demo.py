#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Synchronize the Full Demo Visualization source files between
# the live Unity2Foxglove demo project, the package Samples~ source, and
# an optional Unity project that has imported the sample.
# Usage: python Scripts/samples/sync_full_demo.py --dry-run
# Inputs: Live demo assets, package Samples~ target, optional Unity project/sample path.
# Outputs: Copies or reports changed Full Demo sample files.

from __future__ import annotations

import argparse
import filecmp
import shutil
import sys
from dataclasses import dataclass
from pathlib import Path


# Number of parent directories between this script and the repository root.
REPO_ROOT_PARENT_DEPTH = 2

# Process exit codes returned by this synchronization CLI.
EXIT_SUCCESS = 0
EXIT_FAILURE = 1

# Candidate list handling for imported sample auto-discovery.
MAX_AUTODETECTED_IMPORTED_SAMPLE_ROOTS = 1
FIRST_CANDIDATE_INDEX = 0

# Counters used in the final sync summary.
INITIAL_CHANGED_COUNT = 0
CHANGE_INCREMENT = 1

ROOT = Path(__file__).resolve().parents[REPO_ROOT_PARENT_DEPTH]

# Source roots in the live Unity demo project.
DEMO_ASSETS = ROOT / "Unity2Foxglove" / "Assets"
DEMO_CONFIGS = ROOT / "Unity2Foxglove" / "Configs"

# Destination root for the package sample that is shipped to Unity users.
PACKAGE_SAMPLE = ROOT / "Packages" / "dev.unity2foxglove.sdk" / "Samples~" / "FullDemoVisualization"

# Fields that are useful in the live demo during local acceptance testing but
# must stay portable in the packaged/imported sample.
PORTABLE_FULL_DEMO_SCENE_OVERRIDES = (
    ("  _transportMode:", "  _transportMode: 0"),
    ("  _replayFilePath:", "  _replayFilePath:"),
    ("  _recordingDirectory:", "  _recordingDirectory:"),
    ("  _certificatePfxPath:", "  _certificatePfxPath:"),
    ("  _certificatePassword:", "  _certificatePassword:"),
    ("  _rootCaDistributorEnabled:", "  _rootCaDistributorEnabled: 0"),
    ("  _rootCaFilePath:", "  _rootCaFilePath:"),
    ("  _sharedToken:", "  _sharedToken:"),
)


@dataclass(frozen=True)
class FileMap:
    """Maps one live-demo source file to its packaged sample destination."""

    demo: Path
    sample: Path


# Canonical list of files mirrored between the live demo and package sample.
FILE_MAPS = (
    FileMap(DEMO_CONFIGS / "FoxgloveFullLayout.json", PACKAGE_SAMPLE / "FoxgloveFullLayout.json"),
    FileMap(DEMO_ASSETS / "InputSystem_Actions.inputactions", PACKAGE_SAMPLE / "InputSystem_Actions.inputactions"),
    FileMap(DEMO_ASSETS / "InputSystem_Actions.inputactions.meta", PACKAGE_SAMPLE / "InputSystem_Actions.inputactions.meta"),
    FileMap(DEMO_ASSETS / "Scenes" / "SampleScene.unity", PACKAGE_SAMPLE / "Scenes" / "FullDemoVisualization.unity"),
    FileMap(DEMO_ASSETS / "Scenes" / "SampleScene.unity.meta", PACKAGE_SAMPLE / "Scenes" / "FullDemoVisualization.unity.meta"),
    FileMap(DEMO_ASSETS / "Scripts" / "FoxgloveDemoSetup.cs", PACKAGE_SAMPLE / "Scripts" / "FoxgloveDemoSetup.cs"),
    FileMap(DEMO_ASSETS / "Scripts" / "FoxgloveDemoSetup.cs.meta", PACKAGE_SAMPLE / "Scripts" / "FoxgloveDemoSetup.cs.meta"),
    FileMap(DEMO_ASSETS / "Scripts" / "MouseDragCube.cs", PACKAGE_SAMPLE / "Scripts" / "MouseDragCube.cs"),
    FileMap(DEMO_ASSETS / "Scripts" / "MouseDragCube.cs.meta", PACKAGE_SAMPLE / "Scripts" / "MouseDragCube.cs.meta"),
    FileMap(DEMO_ASSETS / "Scripts" / "TestLog.cs", PACKAGE_SAMPLE / "Scripts" / "TestLog.cs"),
    FileMap(DEMO_ASSETS / "Scripts" / "TestLog.cs.meta", PACKAGE_SAMPLE / "Scripts" / "TestLog.cs.meta"),
    FileMap(
        DEMO_ASSETS / "Scripts" / "FoxRunTriggerTelemetrySmoke.cs",
        PACKAGE_SAMPLE / "Scripts" / "FoxRunTriggerTelemetrySmoke.cs",
    ),
    FileMap(
        DEMO_ASSETS / "Scripts" / "FoxRunTriggerTelemetrySmoke.cs.meta",
        PACKAGE_SAMPLE / "Scripts" / "FoxRunTriggerTelemetrySmoke.cs.meta",
    ),
    FileMap(DEMO_ASSETS / "Settings" / "DefaultVolumeProfile.asset", PACKAGE_SAMPLE / "Settings" / "DefaultVolumeProfile.asset"),
    FileMap(DEMO_ASSETS / "Settings" / "DefaultVolumeProfile.asset.meta", PACKAGE_SAMPLE / "Settings" / "DefaultVolumeProfile.asset.meta"),
    FileMap(DEMO_ASSETS / "Settings" / "Mobile_Renderer.asset", PACKAGE_SAMPLE / "Settings" / "Mobile_Renderer.asset"),
    FileMap(DEMO_ASSETS / "Settings" / "Mobile_Renderer.asset.meta", PACKAGE_SAMPLE / "Settings" / "Mobile_Renderer.asset.meta"),
    FileMap(DEMO_ASSETS / "Settings" / "Mobile_RPAsset.asset", PACKAGE_SAMPLE / "Settings" / "Mobile_RPAsset.asset"),
    FileMap(DEMO_ASSETS / "Settings" / "Mobile_RPAsset.asset.meta", PACKAGE_SAMPLE / "Settings" / "Mobile_RPAsset.asset.meta"),
    FileMap(DEMO_ASSETS / "Settings" / "PC_Renderer.asset", PACKAGE_SAMPLE / "Settings" / "PC_Renderer.asset"),
    FileMap(DEMO_ASSETS / "Settings" / "PC_Renderer.asset.meta", PACKAGE_SAMPLE / "Settings" / "PC_Renderer.asset.meta"),
    FileMap(DEMO_ASSETS / "Settings" / "PC_RPAsset.asset", PACKAGE_SAMPLE / "Settings" / "PC_RPAsset.asset"),
    FileMap(DEMO_ASSETS / "Settings" / "PC_RPAsset.asset.meta", PACKAGE_SAMPLE / "Settings" / "PC_RPAsset.asset.meta"),
    FileMap(DEMO_ASSETS / "Settings" / "SampleSceneProfile.asset", PACKAGE_SAMPLE / "Settings" / "SampleSceneProfile.asset"),
    FileMap(DEMO_ASSETS / "Settings" / "SampleSceneProfile.asset.meta", PACKAGE_SAMPLE / "Settings" / "SampleSceneProfile.asset.meta"),
    FileMap(DEMO_ASSETS / "Settings" / "UniversalRenderPipelineGlobalSettings.asset", PACKAGE_SAMPLE / "Settings" / "UniversalRenderPipelineGlobalSettings.asset"),
    FileMap(DEMO_ASSETS / "Settings" / "UniversalRenderPipelineGlobalSettings.asset.meta", PACKAGE_SAMPLE / "Settings" / "UniversalRenderPipelineGlobalSettings.asset.meta"),
)


def rel(path: Path, root: Path = ROOT) -> str:
    """Format a path relative to a root when possible."""
    try:
        return path.resolve().relative_to(root.resolve()).as_posix()
    except ValueError:
        return str(path)


def resolve_path(path: str | None, base: Path = ROOT) -> Path | None:
    """Resolve an optional CLI path relative to a base directory."""
    if path is None:
        return None
    p = Path(path)
    if not p.is_absolute():
        p = base / p
    return p.resolve()


def imported_sample_root(target_project: Path, explicit_path: Path | None) -> Path:
    """Locate the imported Full Demo sample root in an external Unity project."""
    if explicit_path is not None:
        return explicit_path

    samples_root = target_project / "Assets" / "Samples"
    if not samples_root.exists():
        raise FileNotFoundError(f"Imported sample root was not found: {samples_root}")

    candidates = []
    for scene in samples_root.rglob("FullDemoVisualization.unity"):
        root = scene.parent.parent
        if (root / "Scripts" / "MouseDragCube.cs").exists():
            candidates.append(root)

    if not candidates:
        raise FileNotFoundError(
            "Could not find an imported Full Demo Visualization sample under "
            f"{samples_root}. Re-import the sample or pass --imported-sample-path."
        )
    if len(candidates) > MAX_AUTODETECTED_IMPORTED_SAMPLE_ROOTS:
        joined = ", ".join(str(p) for p in candidates)
        raise RuntimeError(f"Multiple imported Full Demo samples found; pass --imported-sample-path. Candidates: {joined}")
    return candidates[FIRST_CANDIDATE_INDEX]


def sample_relative(sample_path: Path) -> Path:
    """Return a package-sample path relative to the sample root."""
    return sample_path.relative_to(PACKAGE_SAMPLE)


def imported_maps(imported_root: Path, source: str) -> list[tuple[Path, Path]]:
    """Build copy pairs for syncing files into an imported sample."""
    pairs = []
    for item in FILE_MAPS:
        sample_rel = sample_relative(item.sample)
        src = item.sample if source == "package" else item.demo
        dst = imported_root / sample_rel
        pairs.append((src, dst))
    return pairs


def is_demo_scene_to_sample_copy(src: Path, dst: Path) -> bool:
    """Return true when copying the live demo scene into a sample location."""
    demo_scene = DEMO_ASSETS / "Scenes" / "SampleScene.unity"
    return src.resolve() == demo_scene.resolve() and dst.name == "FullDemoVisualization.unity"


def with_line_ending(line: str, content: str) -> str:
    """Return content with the original line ending from line."""
    if line.endswith("\r\n"):
        return content + "\r\n"
    if line.endswith("\n"):
        return content + "\n"
    return content


def portable_full_demo_scene_payload(src: Path) -> bytes:
    """Read the demo scene and rewrite local-only defaults for sample users."""
    lines = src.read_text(encoding="utf-8").splitlines(keepends=True)
    rewritten = []
    for line in lines:
        body = line.rstrip("\r\n").rstrip()
        replacement = None
        for prefix, value in PORTABLE_FULL_DEMO_SCENE_OVERRIDES:
            if body.startswith(prefix):
                replacement = value
                break
        rewritten.append(with_line_ending(line, replacement if replacement is not None else body))
    return "".join(rewritten).encode("utf-8")


def build_pairs(args: argparse.Namespace) -> list[tuple[Path, Path]]:
    """Build ordered source/destination pairs for the selected sync mode."""
    mode = args.mode
    if mode == "demo-to-package":
        return [(m.demo, m.sample) for m in FILE_MAPS]
    if mode == "package-to-demo":
        return [(m.sample, m.demo) for m in FILE_MAPS]

    target_project = resolve_path(args.target_project)
    if target_project is None:
        raise ValueError(f"--target-project is required for mode {mode}")
    explicit = resolve_path(args.imported_sample_path)
    imported_root = imported_sample_root(target_project, explicit)

    if mode == "package-to-imported":
        return imported_maps(imported_root, "package")
    if mode == "demo-to-imported":
        return imported_maps(imported_root, "demo")

    raise ValueError(f"Unsupported mode: {mode}")


def copy_file(src: Path, dst: Path, dry_run: bool) -> str:
    """Copy one file if content differs, returning a printable status label."""
    if not src.exists():
        raise FileNotFoundError(f"Source file missing: {rel(src)}")

    portable_payload = portable_full_demo_scene_payload(src) if is_demo_scene_to_sample_copy(src, dst) else None
    if portable_payload is not None:
        if dst.exists() and dst.read_bytes() == portable_payload:
            return "unchanged"
        if dry_run:
            return "would copy"
        dst.parent.mkdir(parents=True, exist_ok=True)
        dst.write_bytes(portable_payload)
        return "copied"

    if dst.exists() and filecmp.cmp(src, dst, shallow=False):
        return "unchanged"

    if dry_run:
        return "would copy"

    dst.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(src, dst)
    return "copied"


def parse_args() -> argparse.Namespace:
    """Parse CLI arguments for sample synchronization."""
    parser = argparse.ArgumentParser(
        description="Synchronize Full Demo Visualization files using repo-relative paths by default."
    )
    parser.add_argument(
        "--mode",
        choices=("demo-to-package", "package-to-demo", "package-to-imported", "demo-to-imported"),
        default="demo-to-package",
        help="Synchronization direction. Default: demo-to-package.",
    )
    parser.add_argument(
        "--target-project",
        help="Unity project root for imported-sample modes. Relative paths are resolved from the repo root.",
    )
    parser.add_argument(
        "--imported-sample-path",
        help="Exact imported Full Demo sample root. Relative paths are resolved from the repo root.",
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Print planned copies without writing files.",
    )
    return parser.parse_args()


def main() -> int:
    """Run the selected sample synchronization mode."""
    args = parse_args()
    try:
        pairs = build_pairs(args)
        changed = INITIAL_CHANGED_COUNT
        for src, dst in pairs:
            status = copy_file(src, dst, args.dry_run)
            if status != "unchanged":
                changed += CHANGE_INCREMENT
            print(f"[{status}] {rel(src)} -> {rel(dst)}")
        verb = "would update" if args.dry_run else "updated"
        print(f"\nsync_full_demo: {verb} {changed} file(s); checked {len(pairs)} file(s).")
        return EXIT_SUCCESS
    except Exception as exc:
        print(f"sync_full_demo: {exc}", file=sys.stderr)
        return EXIT_FAILURE


if __name__ == "__main__":
    raise SystemExit(main())
