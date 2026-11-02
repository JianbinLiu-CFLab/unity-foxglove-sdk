#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Validate Unity package structure, public docs, and sample hygiene.
# Usage: python Scripts/package/validate_unity_package.py
# Inputs: Repository package files under Packages/dev.unity2foxglove.sdk.
# Outputs: Prints release validation checks and exits nonzero on failure.

"""Validate Unity package structure, public docs, and sample hygiene."""

from __future__ import annotations

import json
import re
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable


# Number of parent directories between this script and the repository root.
REPO_ROOT_PARENT_DEPTH = 3

# Process exit codes returned by the validation CLI.
EXIT_SUCCESS = 0
EXIT_FAILURE = 1

# Maximum problem count printed for long offender lists.
MAX_REPORTED_OFFENDERS = 12
MAX_REPORTED_MISSING_META = 10

# Column width used when no checks were collected.
EMPTY_RESULT_NAME_WIDTH = 0

ROOT = Path(__file__).resolve().parents[REPO_ROOT_PARENT_DEPTH]
PACKAGE = ROOT / "Packages" / "dev.unity2foxglove.sdk"
ROS2_FOR_UNITY_PACKAGE = ROOT / "Packages" / "dev.unity2foxglove.ros2forunity"
ROS2_BRIDGE_PACKAGE = (
    ROOT / "Packages" / "dev.unity2foxglove.ros2bridge"
)
REMOTE_GATEWAY_PACKAGE = ROOT / "Packages" / "dev.unity2foxglove.remotegateway.win64"
CONSUMER_PACKAGES_LOCK = ROOT / "Unity2Foxglove" / "Packages" / "packages-lock.json"
ROS2_RUNTIME_PACKAGES = (
    ROOT / "Packages" / "dev.unity2foxglove.ros2forunity.runtime.humble.win64",
    ROOT / "Packages" / "dev.unity2foxglove.ros2forunity.runtime.jazzy.win64",
    ROOT / "Packages" / "dev.unity2foxglove.ros2forunity.runtime.lyrical.win64",
)
ROS2_TYPESUPPORT_PACKAGES = (
    ROOT / "Packages" / "dev.unity2foxglove.foxrun.ros2.interfaces.typesupport.humble.win64",
    ROOT / "Packages" / "dev.unity2foxglove.foxrun.ros2.interfaces.typesupport.jazzy.win64",
    ROOT / "Packages" / "dev.unity2foxglove.foxrun.ros2.interfaces.typesupport.lyrical.win64",
)
CONSUMER_LOCAL_DEPENDENCIES = (
    "dev.unity2foxglove.sdk",
    "dev.unity2foxglove.ros2bridge",
    "dev.unity2foxglove.ros2forunity",
    "dev.unity2foxglove.foxrun.ros2.interfaces.typesupport.lyrical.win64",
    "dev.unity2foxglove.foxrun.ros2.interfaces",
    "dev.unity2foxglove.ros2forunity.runtime.lyrical.win64",
)
CONSUMER_LOCAL_DEPENDENCY_PREFIXES = (
    "dev.unity2foxglove.foxrun.ros2.interfaces.typesupport.",
    "dev.unity2foxglove.ros2forunity.runtime.",
)
SAMPLES = PACKAGE / "Samples~"
DOCS = PACKAGE / "Documentation~"
THIRD_PARTY_NOTICES = ROOT / "THIRD_PARTY_NOTICES.md"
UNITY_DEMO_SCRIPTS = ROOT / "Unity2Foxglove" / "Assets" / "Scripts"
UNITY_DEMO_ASSETS = ROOT / "Unity2Foxglove" / "Assets"

EXPECTED_SAMPLES = {
    "Basic Visualization": "Samples~/BasicVisualization",
    "Full Demo Visualization": "Samples~/FullDemoVisualization",
    "Virtual LiDAR Maze Demo": "Samples~/Virtual LiDAR Maze Demo",
}
EXPECTED_SAMPLE_COUNT = len(EXPECTED_SAMPLES)

# File extensions that Unity tracks with .meta sidecar files in samples.
UNITY_META_EXTENSIONS = {
    ".asmdef",
    ".anim",
    ".asset",
    ".controller",
    ".compute",
    ".cs",
    ".dll",
    ".fbx",
    ".fontsettings",
    ".guiskin",
    ".inputactions",
    ".jpeg",
    ".jpg",
    ".json",
    ".mat",
    ".otf",
    ".physicmaterial",
    ".png",
    ".prefab",
    ".preset",
    ".rendertexture",
    ".shader",
    ".shadergraph",
    ".spriteatlas",
    ".ttf",
    ".unity",
    ".uss",
    ".uxml",
    ".vfx",
    ".wav",
    ".xml",
}

# Text patterns that must not leak into public docs or sample files.
FORBIDDEN_PUBLIC_PATTERNS = (
    ("local Windows path", re.compile(r"\b[A-Za-z]:[\\/]")),
    ("private docs reference", re.compile(r"Dev" r"eloper[\\/]")),
    ("Obsidian pasted image", re.compile(r"Pasted image", re.IGNORECASE)),
    ("Obsidian embed", re.compile(r"!\[\[")),
    ("to-do marker", re.compile(r"\bTO" r"DO\b")),
    ("TBD marker", re.compile(r"\bTBD\b")),
    ("fix-me marker", re.compile(r"\bFIX" r"ME\b")),
    ("removed Unity IL2CPP build script path", re.compile(r"Scripts[\\/]+build_unity_il2cpp\.py")),
    ("Unity Editor.Tests component", re.compile(r"Unity\.RenderPipelines\.Core\.Editor\.Tests")),
    ("stale Phase scene class identifier", re.compile(r"Assembly-CSharp::Phase\d+")),
)
FORBIDDEN_PUBLIC_PATTERN_GROUPS = tuple(
    (f"P{index}", label, pattern)
    for index, (label, pattern) in enumerate(FORBIDDEN_PUBLIC_PATTERNS)
)
FORBIDDEN_PUBLIC_SCAN_PATTERN = re.compile(
    "|".join(
        f"(?P<{group}>{'(?i:' + pattern.pattern + ')' if pattern.flags & re.IGNORECASE else '(?:' + pattern.pattern + ')'})"
        for group, _, pattern in FORBIDDEN_PUBLIC_PATTERN_GROUPS
    )
)
FORBIDDEN_PUBLIC_LABELS_BY_GROUP = {
    group: label
    for group, label, _ in FORBIDDEN_PUBLIC_PATTERN_GROUPS
}

# Bundled binary dependencies that must be named in the third-party notice file.
THIRD_PARTY_NOTICE_REQUIREMENTS = (
    (
        PACKAGE / "Plugins" / "Google.Protobuf" / "Google.Protobuf.dll",
        ("Google.Protobuf", "BSD-3-Clause", "Plugins/Google.Protobuf/Google.Protobuf.dll"),
    ),
    (
        PACKAGE / "Runtime" / "Plugins" / "compression" / "K4os.Compression.LZ4.dll",
        ("K4os.Compression.LZ4", "MIT", "Runtime/Plugins/compression/K4os.Compression.LZ4.dll"),
    ),
    (
        PACKAGE / "Runtime" / "Plugins" / "compression" / "K4os.Compression.LZ4.Streams.dll",
        ("K4os.Compression.LZ4.Streams", "MIT", "Runtime/Plugins/compression/K4os.Compression.LZ4.Streams.dll"),
    ),
    (
        PACKAGE / "Runtime" / "Plugins" / "compression" / "K4os.Hash.xxHash.dll",
        ("K4os.Hash.xxHash", "MIT", "Runtime/Plugins/compression/K4os.Hash.xxHash.dll"),
    ),
    (
        PACKAGE / "Runtime" / "Plugins" / "compression" / "System.IO.Pipelines.dll",
        ("System.IO.Pipelines", "MIT", "Runtime/Plugins/compression/System.IO.Pipelines.dll"),
    ),
    (
        PACKAGE / "Runtime" / "Plugins" / "compression" / "ZstdSharp.dll",
        ("ZstdSharp.Port", "MIT", "Runtime/Plugins/compression/ZstdSharp.dll"),
    ),
    (
        PACKAGE / "Runtime" / "Plugins" / "StbImageWriteSharp.dll",
        ("StbImageWriteSharp", "Public Domain", "Runtime/Plugins/StbImageWriteSharp.dll"),
    ),
    (
        PACKAGE / "Runtime" / "Plugins" / "Windows" / "x86_64" / "Unity2FoxgloveDracoNative.dll",
        ("Google Draco", "Apache-2.0", "Runtime/Plugins/Windows/x86_64/Unity2FoxgloveDracoNative.dll"),
    ),
)

VERSION_RE = re.compile(r"^\d+\.\d+\.\d+$")
META_GUID_RE = re.compile(r"(?mi)^guid:\s*([0-9a-f]{32})\s*$")
VALIDATION_PHASE_FILENAME_RE = re.compile(r"^Phase(?P<phase>\d+)(?P<trailing>[A-Za-z0-9_-]*)Validation\.cs$")
VALIDATION_PHASE_FILENAME_INDEX_RE = re.compile(r"^[_-](?P<index>\d+)")
LEGACY_VALIDATION_FILENAME_CUTOFF_PHASE = 164
LEGACY_VALIDATION_FILENAME_CUTOFF_INDEX = 58

# Directory path parts that indicate local/generated sample artifacts.
FORBIDDEN_SAMPLE_PARTS = {
    "Library",
    "Logs",
    "Recordings",
    "Generated",
}
PACKAGE_BUILD_DIRECTORY_NAMES = frozenset({"bin", "obj", "__pycache__"})

# Exact filenames that should never be shipped in package samples.
FORBIDDEN_SAMPLE_NAMES = {
    "FoxRun_link.xml",
}

# Filename patterns for generated or benchmark artifacts excluded from samples.
FORBIDDEN_SAMPLE_NAME_PATTERNS = (
    re.compile(r".*_FoxRun\.g\.cs$", re.IGNORECASE),
    re.compile(r"PerformanceTestRun.*", re.IGNORECASE),
)


@dataclass
class CheckResult:
    """Structured result for one release-validation check."""

    name: str
    ok: bool
    detail: str


def rel(path: Path) -> str:
    """Format a path relative to the repository root when possible."""
    try:
        return path.resolve().relative_to(ROOT.resolve()).as_posix()
    except ValueError:
        return path.as_posix()


def iter_files(root: Path) -> Iterable[Path]:
    """Yield regular files below a root, returning an empty iterable when absent."""
    if not root.exists():
        return ()
    return (p for p in root.rglob("*") if p.is_file())


def iter_bridge_asset_files(package: Path | None = None) -> list[Path]:
    """Return Bridge package assets while excluding local build outputs."""
    root = package if package is not None else ROS2_BRIDGE_PACKAGE
    if not root.exists():
        return []
    return [
        path
        for path in root.rglob("*")
        if path.is_file()
        and path.suffix.lower() in UNITY_META_EXTENSIONS
        and not any(
            part.casefold() in PACKAGE_BUILD_DIRECTORY_NAMES
            for part in path.relative_to(root).parts
        )
    ]


def path_is_relative_to(path: Path, root: Path) -> bool:
    """Return whether a path is under a root without requiring Python 3.9 Path.is_relative_to."""
    try:
        path.relative_to(root)
        return True
    except ValueError:
        return False


def add(results: list[CheckResult], name: str, ok: bool, detail: str = "") -> None:
    """Append one check result to the accumulated report."""
    results.append(CheckResult(name, ok, detail))


def load_package_json(results: list[CheckResult]) -> dict:
    """Load package.json and record whether it parsed successfully."""
    path = PACKAGE / "package.json"
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except Exception as exc:
        add(results, "package.json parses", False, f"{rel(path)}: {exc}")
        return {}
    if not isinstance(data, dict):
        add(results, "package.json parses", False, f"{rel(path)}: JSON root is not an object")
        return {}
    add(results, "package.json parses", True, rel(path))
    return data



__all__ = [
    'CONSUMER_LOCAL_DEPENDENCIES',
    'CONSUMER_LOCAL_DEPENDENCY_PREFIXES',
    'CONSUMER_PACKAGES_LOCK',
    'CheckResult',
    'DOCS',
    'EMPTY_RESULT_NAME_WIDTH',
    'EXIT_FAILURE',
    'EXIT_SUCCESS',
    'EXPECTED_SAMPLES',
    'EXPECTED_SAMPLE_COUNT',
    'FORBIDDEN_PUBLIC_LABELS_BY_GROUP',
    'FORBIDDEN_PUBLIC_PATTERNS',
    'FORBIDDEN_PUBLIC_PATTERN_GROUPS',
    'FORBIDDEN_PUBLIC_SCAN_PATTERN',
    'FORBIDDEN_SAMPLE_NAMES',
    'FORBIDDEN_SAMPLE_NAME_PATTERNS',
    'FORBIDDEN_SAMPLE_PARTS',
    'Iterable',
    'LEGACY_VALIDATION_FILENAME_CUTOFF_INDEX',
    'LEGACY_VALIDATION_FILENAME_CUTOFF_PHASE',
    'MAX_REPORTED_MISSING_META',
    'MAX_REPORTED_OFFENDERS',
    'META_GUID_RE',
    'PACKAGE',
    'PACKAGE_BUILD_DIRECTORY_NAMES',
    'Path',
    'REMOTE_GATEWAY_PACKAGE',
    'REPO_ROOT_PARENT_DEPTH',
    'ROOT',
    'ROS2_BRIDGE_PACKAGE',
    'ROS2_FOR_UNITY_PACKAGE',
    'ROS2_RUNTIME_PACKAGES',
    'ROS2_TYPESUPPORT_PACKAGES',
    'SAMPLES',
    'THIRD_PARTY_NOTICES',
    'THIRD_PARTY_NOTICE_REQUIREMENTS',
    'UNITY_DEMO_ASSETS',
    'UNITY_DEMO_SCRIPTS',
    'UNITY_META_EXTENSIONS',
    'VALIDATION_PHASE_FILENAME_INDEX_RE',
    'VALIDATION_PHASE_FILENAME_RE',
    'VERSION_RE',
    'add',
    'annotations',
    'dataclass',
    'iter_bridge_asset_files',
    'iter_files',
    'json',
    'load_package_json',
    'path_is_relative_to',
    're',
    'rel',
    'sys',
]
