#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Synchronize the Full Demo Visualization source files between
# the live Unity2Foxglove demo project, the package Samples~ source, and
# an optional Unity project that has imported the sample.

from __future__ import annotations

import argparse
import filecmp
import shutil
import sys
from dataclasses import dataclass
from pathlib import Path


ROOT = Path(__file__).resolve().parent.parent

DEMO_ASSETS = ROOT / "Unity2Foxglove" / "Assets"
DEMO_CONFIGS = ROOT / "Unity2Foxglove" / "Configs"
PACKAGE_SAMPLE = ROOT / "Packages" / "dev.unity2foxglove.sdk" / "Samples~" / "FullDemoVisualization"


@dataclass(frozen=True)
class FileMap:
    demo: Path
    sample: Path


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
    try:
        return path.resolve().relative_to(root.resolve()).as_posix()
    except ValueError:
        return str(path)


def resolve_path(path: str | None, base: Path = ROOT) -> Path | None:
    if path is None:
        return None
    p = Path(path)
    if not p.is_absolute():
        p = base / p
    return p.resolve()


def imported_sample_root(target_project: Path, explicit_path: Path | None) -> Path:
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
    if len(candidates) > 1:
        joined = ", ".join(str(p) for p in candidates)
        raise RuntimeError(f"Multiple imported Full Demo samples found; pass --imported-sample-path. Candidates: {joined}")
    return candidates[0]


def sample_relative(sample_path: Path) -> Path:
    return sample_path.relative_to(PACKAGE_SAMPLE)


def imported_maps(imported_root: Path, source: str) -> list[tuple[Path, Path]]:
    pairs = []
    for item in FILE_MAPS:
        sample_rel = sample_relative(item.sample)
        src = item.sample if source == "package" else item.demo
        dst = imported_root / sample_rel
        pairs.append((src, dst))
    return pairs


def build_pairs(args: argparse.Namespace) -> list[tuple[Path, Path]]:
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
    if not src.exists():
        raise FileNotFoundError(f"Source file missing: {rel(src)}")

    if dst.exists() and filecmp.cmp(src, dst, shallow=False):
        return "unchanged"

    if dry_run:
        return "would copy"

    dst.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(src, dst)
    return "copied"


def parse_args() -> argparse.Namespace:
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
    args = parse_args()
    try:
        pairs = build_pairs(args)
        changed = 0
        for src, dst in pairs:
            status = copy_file(src, dst, args.dry_run)
            if status != "unchanged":
                changed += 1
            print(f"[{status}] {rel(src)} -> {rel(dst)}")
        verb = "would update" if args.dry_run else "updated"
        print(f"\nsync_full_demo_sample: {verb} {changed} file(s); checked {len(pairs)} file(s).")
        return 0
    except Exception as exc:
        print(f"sync_full_demo_sample: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
