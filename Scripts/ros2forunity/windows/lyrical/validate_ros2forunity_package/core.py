#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Validate the optional ROS2 For Unity package boundary.
# Usage: python Scripts/ros2forunity/windows/lyrical/validate_ros2forunity_package.py
# Inputs: Packages/dev.unity2foxglove.ros2forunity package metadata and compliance manifest.
# Outputs: Prints package boundary checks and exits nonzero on failure.

"""Validate the optional ROS2 For Unity package boundary."""

from __future__ import annotations

from pathlib import Path as _Phase192Path

_PHASE192_FACADE_FILE = _Phase192Path(__file__).resolve().parents[1] / "validate_ros2forunity_package.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
del _Phase192Path


import json
import sys
import re
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable


REPO_ROOT_PARENT_DEPTH = 4
EXIT_SUCCESS = 0
EXIT_FAILURE = 1
MAX_REPORTED_OFFENDERS = 12
DEFAULT_RMW_IMPLEMENTATION = "rmw_fastrtps_cpp"
SUPPORTED_RMW_IMPLEMENTATIONS = ("rmw_fastrtps_cpp", "rmw_zenoh_cpp")

ROOT = Path(__file__).resolve().parents[REPO_ROOT_PARENT_DEPTH]
if not (ROOT / "Packages" / "dev.unity2foxglove.sdk").is_dir():
    raise RuntimeError(f"REPO_ROOT_PARENT_DEPTH={REPO_ROOT_PARENT_DEPTH} resolved to wrong root: {ROOT}")
PACKAGE = ROOT / "Packages" / "dev.unity2foxglove.ros2forunity"
CORE_PACKAGE = ROOT / "Packages" / "dev.unity2foxglove.sdk"
MANIFEST = PACKAGE / "Compliance" / "ros2-for-unity-adoption-manifest.json"
RUNTIME_INVENTORY = PACKAGE / "Compliance" / "r2fu-lyrical-win64-runtime-inventory.json"
RUNTIME_NOTICES = PACKAGE / "Compliance" / "r2fu-lyrical-win64-runtime-notices.md"
ADAPTER_SAMPLE = PACKAGE / "Samples~" / "ROS2 For Unity External Adapter"
RVIZ_SAMPLE = PACKAGE / "Samples~" / "RViz2 Standard Visualization Acceptance"
RVIZ_POINTCLOUD2_SAMPLE = PACKAGE / "Samples~" / "RViz2 PointCloud2 Acceptance"
RVIZ_MARKERARRAY_SAMPLE = PACKAGE / "Samples~" / "RViz2 MarkerArray Acceptance"
RVIZ_V1_SAMPLE = PACKAGE / "Samples~" / "RViz2 Standard Visualization v1"
STANDARD_MESSAGES_SAMPLE = PACKAGE / "Samples~" / "ROS2 Standard Message Expansion"

JSON_CACHE: dict[Path, dict] = {}

RUNTIME_BINARY_SUFFIXES = {
    ".a",
    ".bin",
    ".dll",
    ".so",
    ".dylib",
    ".exe",
    ".exp",
    ".ilk",
    ".lib",
    ".node",
    ".obj",
    ".o",
    ".pdb",
    ".pyd",
    ".wasm",
    ".zip",
    ".unitypackage",
}

FORBIDDEN_RUNTIME_NAMES = {
    "metadata_ros2cs.xml",
    "metadata_ros2_for_unity.xml",
}

ALLOWED_RUNTIME_SUFFIXES = {
    ".cs",
    ".asmdef",
    ".meta",
}

ALLOWED_SAMPLE_SUFFIXES = {
    ".cs",
    ".md",
    ".meta",
    ".rviz",
}

ALLOWED_EDITOR_SUFFIXES = {
    ".asmdef",
    ".cs",
    ".meta",
}

SOURCE_GENERATOR_BUILD_SUFFIXES = {
    ".csproj",
    ".md",
    ".props",
}

SOURCE_GENERATOR_ANALYZER_PATH = (
    "Editor/SourceGenerators/analyzers/dotnet/cs/"
    "Unity2Foxglove.Ros2ForUnity.FoxRunSourceGenerator.dll"
)

FORBIDDEN_PUBLIC_PHASE_PATTERN = re.compile(r"(?<![A-Za-z0-9_])(?:Phase|phase)\s*\d{2,4}[A-Z]?\b")
RUNTIME_TOKEN_EXEMPT_SUBDIRS = {
    "Native",
}

FORBIDDEN_RUNTIME_TOKENS = (
    "using ROS2;",
    "namespace ROS2",
    "ROS2UnityComponent",
    "ROS2Node",
    "IPublisher<",
    "ISubscription<",
    "std_msgs",
    "ros2cs",
)


@dataclass
class CheckResult:
    """Structured result for one optional package validation check."""

    name: str
    ok: bool
    detail: str


def rel(path: Path) -> str:
    """Format a path relative to the repository root when possible."""
    try:
        return path.resolve().relative_to(ROOT.resolve()).as_posix()
    except ValueError:
        return str(path)


def add(results: list[CheckResult], name: str, ok: bool, detail: str = "") -> None:
    """Append one check result to the accumulated report."""
    results.append(CheckResult(name, ok, detail))


def iter_files(root: Path) -> Iterable[Path]:
    """Yield regular files below a root, returning an empty iterable when absent."""
    if not root.exists():
        return ()
    return (path for path in root.rglob("*") if path.is_file())


def _is_runtime_token_exempt(path: Path, runtime_root: Path) -> bool:
    """Return True when a runtime file is under an intentionally exempt subdirectory."""
    try:
        rel_parts = path.relative_to(runtime_root).parts
    except ValueError:
        return False
    return rel_parts and rel_parts[0] in RUNTIME_TOKEN_EXEMPT_SUBDIRS


def _is_editor_source_generator_artifact(path: Path) -> bool:
    """Allow only the package-owned Editor source-generator build surface."""
    try:
        relative = path.relative_to(PACKAGE).as_posix()
    except ValueError:
        return False
    if relative == SOURCE_GENERATOR_ANALYZER_PATH:
        return True
    return (
        relative.startswith("Editor/SourceGenerators/")
        and path.suffix.lower() in SOURCE_GENERATOR_BUILD_SUFFIXES
    )


def load_json(path: Path, results: list[CheckResult], name: str) -> dict | None:
    """Load JSON and record whether parsing succeeded."""
    cache_key = path.resolve()
    cached = JSON_CACHE.get(cache_key)
    if cached is not None:
        add(results, name, True, rel(path))
        return cached

    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except Exception as exc:
        add(results, name, False, f"{rel(path)}: {exc}")
        return None
    JSON_CACHE[cache_key] = data
    add(results, name, True, rel(path))
    return data

__all__ = [name for name in globals() if not name.startswith("__")]
