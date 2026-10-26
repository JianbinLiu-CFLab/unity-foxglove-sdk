#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Validate the optional ROS2 For Unity package boundary.
# Usage: python Scripts/ros2forunity/windows/humble/validate_ros2forunity_package.py
# Inputs: Packages/dev.unity2foxglove.ros2forunity package metadata and compliance manifest.
# Outputs: Prints package boundary checks and exits nonzero on failure.

"""Validate the optional ROS2 For Unity package boundary."""

from __future__ import annotations

_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "validate_ros2forunity_package.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE

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

ROOT = Path(__file__).resolve().parents[REPO_ROOT_PARENT_DEPTH]
PACKAGE = ROOT / "Packages" / "dev.unity2foxglove.ros2forunity"
CORE_PACKAGE = ROOT / "Packages" / "dev.unity2foxglove.sdk"
MANIFEST = PACKAGE / "Compliance" / "ros2-for-unity-adoption-manifest.json"
RUNTIME_INVENTORY = PACKAGE / "Compliance" / "r2fu-humble-win64-runtime-inventory.json"
RUNTIME_PACKAGE_NAME = "dev.unity2foxglove.ros2forunity.runtime.humble.win64"
RUNTIME_PACKAGE = ROOT / "Packages" / RUNTIME_PACKAGE_NAME
RUNTIME_NOTICES = RUNTIME_PACKAGE / "THIRD_PARTY_NOTICES.md"
RUNTIME_ID = "r2fu-humble-win64"
RUNTIME_ARTIFACT = "Ros2ForUnity_humble_standalone_windows_x86_64.zip"
RUNTIME_SHA256 = "6937f348b2abdf40614379173bb81ba55090dc1541cab616d1a0f1e248ceb5b0"
ADAPTER_SAMPLE = PACKAGE / "Samples~" / "ROS2 For Unity External Adapter"
RVIZ_SAMPLE = PACKAGE / "Samples~" / "RViz2 Standard Visualization Acceptance"
RVIZ_POINTCLOUD2_SAMPLE = PACKAGE / "Samples~" / "RViz2 PointCloud2 Acceptance"
RVIZ_MARKERARRAY_SAMPLE = PACKAGE / "Samples~" / "RViz2 MarkerArray Acceptance"
RVIZ_V1_SAMPLE = PACKAGE / "Samples~" / "RViz2 Standard Visualization v1"
STANDARD_MESSAGES_SAMPLE = PACKAGE / "Samples~" / "ROS2 Standard Message Expansion"

JSON_CACHE: dict[Path, dict] = {}
TEXT_CACHE: dict[Path, str] = {}

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
RUNTIME_TOKEN_EXEMPT_SUFFIXES = {
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


def clear_caches() -> None:
    """Clear per-run file caches so repeated in-process validation sees fresh files."""
    JSON_CACHE.clear()
    TEXT_CACHE.clear()


def iter_files(root: Path) -> Iterable[Path]:
    """Yield regular files below a root, returning an empty iterable when absent."""
    if not root.exists():
        return ()
    return (path for path in root.rglob("*") if path.is_file())


def _is_runtime_token_exempt(path: Path, runtime_root: Path) -> bool:
    """Return True when a runtime file is intentionally exempt from token scanning."""
    try:
        rel_parts = path.relative_to(runtime_root).parts
    except ValueError:
        return False
    return rel_parts and rel_parts[0] in RUNTIME_TOKEN_EXEMPT_SUFFIXES


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


def load_json(path: Path, results: list[CheckResult], name: str) -> dict:
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
        return {}
    JSON_CACHE[cache_key] = data
    add(results, name, True, rel(path))
    return data


def read_text_cached(path: Path) -> str:
    """Read optional UTF-8 text once per validation run."""
    if not path.exists():
        return ""

    cache_key = path.resolve()
    cached = TEXT_CACHE.get(cache_key)
    if cached is not None:
        return cached

    text = path.read_text(encoding="utf-8", errors="replace")
    TEXT_CACHE[cache_key] = text
    return text


def any_text_contains(texts: Iterable[str], token: str) -> bool:
    """Return true when any cached text contains a token."""
    return any(token in text for text in texts)


def get_humble_runtime(data: dict) -> dict:
    """Return the Humble supported runtime entry from the adoption manifest."""
    packages = data.get("supportedRuntimePackages", [])
    if not isinstance(packages, list):
        return {}
    for package in packages:
        if isinstance(package, dict) and package.get("packageName") == RUNTIME_PACKAGE_NAME:
            return package
    return {}



__all__ = [name for name in globals() if not name.startswith("__")]
