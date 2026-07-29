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
REPO_ROOT_PARENT_DEPTH = 2

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
ROS2_BRIDGE_PACKAGE = (
    ROOT / "Packages" / "dev.unity2foxglove.ros2bridge"
)
REMOTE_GATEWAY_PACKAGE = ROOT / "Packages" / "dev.unity2foxglove.remotegateway.win64"
ROS2_RUNTIME_PACKAGES = (
    ROOT / "Packages" / "dev.unity2foxglove.ros2forunity.runtime.humble.win64",
    ROOT / "Packages" / "dev.unity2foxglove.ros2forunity.runtime.jazzy.win64",
    ROOT / "Packages" / "dev.unity2foxglove.ros2forunity.runtime.lyrical.win64",
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
    add(results, "package.json parses", True, rel(path))
    return data


def check_ros2_bridge_package(results: list[CheckResult]) -> None:
    """Validate the extracted optional Bridge package and its one sample."""
    manifest = ROS2_BRIDGE_PACKAGE / "package.json"
    try:
        data = json.loads(manifest.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        add(
            results,
            "ROS2 Bridge package.json parses",
            False,
            f"{rel(manifest)}: {exc}",
        )
        return

    dependencies = data.get("dependencies")
    samples = data.get("samples")
    sample = samples[0] if isinstance(samples, list) and len(samples) == 1 else {}
    add(
        results,
        "ROS2 Bridge package identity",
        data.get("name") == "dev.unity2foxglove.ros2bridge"
        and data.get("version") == "0.1.0-preview.1",
        f"name={data.get('name')}, version={data.get('version')}",
    )
    add(
        results,
        "ROS2 Bridge dependency boundary",
        isinstance(dependencies, dict)
        and set(dependencies) == {"dev.unity2foxglove.sdk"},
        f"dependencies={sorted(dependencies) if isinstance(dependencies, dict) else 'n/a'}",
    )
    add(
        results,
        "ROS2 Bridge sample declaration",
        sample.get("displayName") == "ROS2 Bridge Sample"
        and sample.get("path") == "Samples~/Ros2BridgeSample"
        and (ROS2_BRIDGE_PACKAGE / "Samples~/Ros2BridgeSample").is_dir(),
        f"sample={sample}",
    )

    required = (
        "Runtime/Unity2Foxglove.Ros2Bridge.asmdef",
        "Editor/Unity2Foxglove.Ros2Bridge.Editor.asmdef",
        "Tests/Unity2Foxglove.Ros2Bridge.Tests.asmdef",
        "Samples~/Ros2BridgeSample/Scenes/Ros2BridgeSample.unity",
        "Samples~/Ros2BridgeSample/Scripts/Unity2Foxglove.Ros2Bridge.Sample.asmdef",
        "Editor/SourceGenerators/analyzers/dotnet/cs/Unity2Foxglove.Ros2Bridge.FoxRunSourceGenerator.dll",
    )
    missing = [
        path
        for path in required
        if not (ROS2_BRIDGE_PACKAGE / path).is_file()
        or not Path(str(ROS2_BRIDGE_PACKAGE / path) + ".meta").is_file()
    ]
    add(
        results,
        "ROS2 Bridge required assets and metas",
        not missing,
        "complete" if not missing else f"missing={missing}",
    )

    cross_references = []
    for asmdef in ROS2_BRIDGE_PACKAGE.rglob("*.asmdef"):
        if "Unity2Foxglove.Ros2ForUnity" in asmdef.read_text(
            encoding="utf-8"
        ):
            cross_references.append(rel(asmdef))
    add(
        results,
        "ROS2 Bridge has no R2FU assembly reference",
        not cross_references,
        "none" if not cross_references else f"offenders={cross_references}",
    )


def check_package_identity(results: list[CheckResult], data: dict) -> None:
    """Validate package identity fields and sample declarations."""
    expected = {
        "name": "dev.unity2foxglove.sdk",
        "displayName": "Unity2Foxglove SDK",
        "license": "Apache-2.0",
    }
    for key, value in expected.items():
        actual = data.get(key)
        add(results, f"package {key}", actual == value, f"expected {value!r}, got {actual!r}")

    version = data.get("version")
    add(results, "package version", isinstance(version, str) and VERSION_RE.match(version) is not None, f"version={version!r}")

    samples = data.get("samples")
    add(
        results,
        "package samples list",
        isinstance(samples, list) and len(samples) == EXPECTED_SAMPLE_COUNT,
        f"count={len(samples) if isinstance(samples, list) else 'n/a'}",
    )
    if not isinstance(samples, list):
        return

    for display_name, sample_path in EXPECTED_SAMPLES.items():
        match = next((s for s in samples if s.get("displayName") == display_name), None)
        add(
            results,
            f"sample declared: {display_name}",
            match is not None,
            "declared" if match is not None else "missing from package.json samples",
        )
        if match is None:
            continue
        actual_path = match.get("path")
        add(results, f"sample path: {display_name}", actual_path == sample_path, f"expected {sample_path!r}, got {actual_path!r}")
        add(results, f"sample path exists: {display_name}", (PACKAGE / actual_path).exists(), rel(PACKAGE / str(actual_path)))


def check_dependent_package_versions(results: list[CheckResult], sdk_data: dict) -> None:
    """Ensure optional repository packages depend on the current SDK package version."""
    sdk_version = sdk_data.get("version")
    if not isinstance(sdk_version, str) or VERSION_RE.match(sdk_version) is None:
        add(results, "dependent package SDK version pins", False, f"invalid SDK version={sdk_version!r}")
        return

    gateway_manifest = REMOTE_GATEWAY_PACKAGE / "package.json"
    if not gateway_manifest.exists():
        add(results, "dependent package SDK version pins", True, "remote gateway package not present")
        return

    try:
        gateway_data = json.loads(gateway_manifest.read_text(encoding="utf-8"))
    except Exception as exc:
        add(results, "dependent package SDK version pins", False, f"{rel(gateway_manifest)}: {exc}")
        return

    dependency = (
        gateway_data.get("dependencies", {})
        .get("dev.unity2foxglove.sdk")
    )
    add(
        results,
        "dependent package SDK version pins",
        dependency == sdk_version,
        f"remote gateway depends on {dependency!r}, SDK version is {sdk_version!r}",
    )


def check_optional_package_boundaries(results: list[CheckResult]) -> None:
    """Validate optional package release boundaries that public package checks can see."""
    notice_path = REMOTE_GATEWAY_PACKAGE / "THIRD_PARTY_NOTICES.md"
    if notice_path.exists():
        notice = notice_path.read_text(encoding="utf-8", errors="replace")
        add(
            results,
            "remote gateway notice publish sentinel",
            "Before publishing this package" not in notice,
            rel(notice_path),
        )

    runtime_manifests = [path / "package.json" for path in ROS2_RUNTIME_PACKAGES if (path / "package.json").exists()]
    runtime_names: list[str] = []
    runtime_data: list[tuple[Path, dict]] = []
    for manifest in runtime_manifests:
        try:
            data = json.loads(manifest.read_text(encoding="utf-8"))
        except Exception as exc:
            add(results, "ROS2 runtime package conflict metadata", False, f"{rel(manifest)}: {exc}")
            return
        name = data.get("name")
        if isinstance(name, str):
            runtime_names.append(name)
        runtime_data.append((manifest, data))

    offenders: list[str] = []
    for manifest, data in runtime_data:
        name = data.get("name")
        expected = sorted(item for item in runtime_names if item != name)
        actual = data.get("unity2foxgloveConflicts")
        actual_conflicts = sorted(actual) if isinstance(actual, list) else []
        if actual_conflicts != expected:
            offenders.append(f"{rel(manifest)} expected conflicts {expected!r}")

    add(
        results,
        "ROS2 runtime package conflict metadata",
        not offenders,
        "; ".join(offenders) if offenders else "all runtime packages declare sibling conflicts",
    )

    demo_link = UNITY_DEMO_ASSETS / "link.xml"
    add(
        results,
        "demo project avoids duplicate package link.xml",
        not demo_link.exists(),
        rel(demo_link) if demo_link.exists() else "package Runtime/link.xml is authoritative",
    )


def check_required_files(results: list[CheckResult]) -> None:
    """Validate release-critical files that must be present in the package."""
    required = [
        PACKAGE / "README.md",
        PACKAGE / "LICENSE",
        PACKAGE / "Runtime" / "Unity.FoxgloveSDK.asmdef",
        PACKAGE / "Editor" / "Unity.FoxgloveSDK.Editor.asmdef",
        PACKAGE / "Editor" / "SourceGenerators" / "src" / "Unity.FoxgloveSDK.SourceGenerators.asmdef",
        PACKAGE / "Runtime" / "Schemas" / "Proto" / "Unity.FoxgloveSDK.Proto.asmdef",
        PACKAGE / "Runtime" / "link.xml",
        PACKAGE / "Plugins" / "Google.Protobuf" / "Google.Protobuf.dll",
    ]
    for path in required:
        add(results, f"required file: {path.name}", path.exists(), rel(path))


def check_sample_meta(results: list[CheckResult], samples_files: list[Path] | None = None) -> None:
    """Ensure Unity sample assets have matching .meta sidecars."""
    samples_files = samples_files if samples_files is not None else list(iter_files(SAMPLES))
    missing: list[str] = []
    for path in samples_files:
        if path.suffix == ".meta" or path.name == "README.md":
            continue
        if path.suffix.lower() not in UNITY_META_EXTENSIONS:
            continue
        if not Path(str(path) + ".meta").exists():
            missing.append(rel(path))
    add(
        results,
        "sample Unity asset .meta files",
        not missing,
        "; ".join(missing[:MAX_REPORTED_MISSING_META]) if missing else "all checked sample assets have .meta",
    )


def check_sample_boundaries(results: list[CheckResult]) -> None:
    """Verify Basic and FullDemo sample boundaries remain intentional."""
    basic = SAMPLES / "BasicVisualization"
    full = SAMPLES / "FullDemoVisualization"

    forbidden_basic = [
        basic / "Scripts",
        basic / "Settings",
        basic / "InputSystem_Actions.inputactions",
        basic / "FoxgloveFullLayout.json",
        basic / "Scenes" / "FullDemoVisualization.unity",
    ]
    leaks = [rel(p) for p in forbidden_basic if p.exists()]
    add(results, "Basic sample remains minimal", not leaks, "; ".join(leaks) if leaks else "no FullDemo-only files")

    required_full = [
        full / "FoxgloveFullLayout.json",
        full / "FoxgloveFullLayout.json.meta",
        full / "Scenes" / "FullDemoVisualization.unity",
        full / "Scripts" / "FoxgloveDemoSetup.cs",
        full / "Scripts" / "MouseDragCube.cs",
        full / "Scripts" / "TestLog.cs",
        full / "Settings" / "DefaultVolumeProfile.asset",
        full / "Settings" / "UniversalRenderPipelineGlobalSettings.asset",
    ]
    missing = [rel(p) for p in required_full if not p.exists()]
    add(results, "FullDemo required files", not missing, "; ".join(missing) if missing else "all required files present")

    forbidden_full = [
        full / "InputSystem_Actions.inputactions",
        full / "InputSystem_Actions.inputactions.meta",
    ]
    conflicts = [rel(p) for p in forbidden_full if p.exists()]
    add(results, "FullDemo avoids project-level input action assets", not conflicts, "; ".join(conflicts) if conflicts else "no InputSystem_Actions asset")


def check_forbidden_public_content(
    results: list[CheckResult],
    samples_files: list[Path] | None = None,
    docs_files: list[Path] | None = None,
) -> None:
    """Scan public docs and samples for local-only markers."""
    samples_files = samples_files if samples_files is not None else list(iter_files(SAMPLES))
    docs_files = docs_files if docs_files is not None else list(iter_files(DOCS))
    package_readme = PACKAGE / "README.md"
    offenders: list[str] = []
    paths = samples_files + docs_files
    if package_readme.is_file():
        paths.append(package_readme)
    for path in paths:
        if path.suffix.lower() not in {".md", ".json", ".cs", ".unity", ".asset", ".inputactions", ".xml"}:
            continue
        text = path.read_text(encoding="utf-8", errors="replace")
        for match in FORBIDDEN_PUBLIC_SCAN_PATTERN.finditer(text):
            label = FORBIDDEN_PUBLIC_LABELS_BY_GROUP.get(match.lastgroup or "", "forbidden marker")
            offenders.append(f"{rel(path)} ({label})")
    add(
        results,
        "public docs/samples have no forbidden markers",
        not offenders,
        "; ".join(offenders[:MAX_REPORTED_OFFENDERS]) if offenders else "no forbidden markers found",
    )


def check_forbidden_sample_artifacts(results: list[CheckResult], samples_entries: list[Path] | None = None) -> None:
    """Reject generated, local, or benchmark files from package samples."""
    samples_entries = samples_entries if samples_entries is not None else list(SAMPLES.rglob("*"))
    offenders: set[Path] = set()
    for path in samples_entries:
        relative_parts = path.relative_to(SAMPLES).parts
        forbidden_index = next(
            (index for index, part in enumerate(relative_parts) if part in FORBIDDEN_SAMPLE_PARTS),
            None,
        )
        if forbidden_index is not None:
            offenders.add(SAMPLES.joinpath(*relative_parts[: forbidden_index + 1]))
            continue
        if path.name in FORBIDDEN_SAMPLE_NAMES:
            offenders.add(path)
            continue
        if any(pattern.match(path.name) for pattern in FORBIDDEN_SAMPLE_NAME_PATTERNS):
            offenders.add(path)
    offender_list = sorted(rel(path) for path in offenders)
    add(
        results,
        "samples contain no generated/local artifacts",
        not offender_list,
        "; ".join(offender_list[:MAX_REPORTED_OFFENDERS]) if offender_list else "no forbidden sample artifacts",
    )


def check_package_build_artifacts(results: list[CheckResult], package_entries: list[Path] | None = None) -> None:
    """Reject build/cache directories from the release package tree."""
    package_entries = package_entries if package_entries is not None else list(PACKAGE.rglob("*"))
    forbidden_dirs = {"bin", "obj", "__pycache__"}
    offenders: list[str] = []
    for path in package_entries:
        if path.name in forbidden_dirs and path.is_dir():
            offenders.append(rel(path))
    add(
        results,
        "package contains no build/cache directories",
        not offenders,
        "; ".join(offenders[:MAX_REPORTED_OFFENDERS]) if offenders else "no build/cache directories",
    )


def check_manual_phase_service_guards(results: list[CheckResult], demo_entries: list[Path] | None = None) -> None:
    """Reject active phase-only FoxService demo endpoints in committed Unity project scripts."""
    demo_entries = demo_entries if demo_entries is not None else list(UNITY_DEMO_SCRIPTS.rglob("*.cs"))
    offenders: list[str] = []
    for path in demo_entries:
        if not path.is_file():
            continue
        text = path.read_text(encoding="utf-8", errors="replace")
        for line_number, line in enumerate(text.splitlines(), start=1):
            stripped = line.lstrip()
            if stripped.startswith("//"):
                continue
            if '[FoxService("/phase141d/' in stripped:
                offenders.append(f"{rel(path)}:{line_number}")
    add(
        results,
        "manual phase FoxService demos stay disabled",
        not offenders,
        "; ".join(offenders[:MAX_REPORTED_OFFENDERS]) if offenders else "no active phase-only FoxService demos",
    )


def check_validation_naming(results: list[CheckResult], package_files: list[Path] | None = None) -> None:
    """Reject new Phase-number-prefixed runtime validation source filenames."""
    runtime_tests = PACKAGE / "Tests" / "Runtime"
    if package_files is None:
        runtime_files = list(iter_files(runtime_tests))
    else:
        runtime_files = [
            path
            for path in package_files
            if path.is_file() and path_is_relative_to(path, runtime_tests)
        ]

    offenders: list[str] = []
    for path in runtime_files:
        match = VALIDATION_PHASE_FILENAME_RE.match(path.name)
        if match is None:
            continue

        phase = int(match.group("phase"))
        trailing = match.group("trailing")
        index_match = VALIDATION_PHASE_FILENAME_INDEX_RE.match(trailing)
        index = int(index_match.group("index")) if index_match is not None else None
        if phase > LEGACY_VALIDATION_FILENAME_CUTOFF_PHASE:
            offenders.append(rel(path))
        elif phase == LEGACY_VALIDATION_FILENAME_CUTOFF_PHASE and (
            trailing == "" or index is None or index >= LEGACY_VALIDATION_FILENAME_CUTOFF_INDEX
        ):
            offenders.append(rel(path))

    add(
        results,
        "runtime validation source filenames are descriptive",
        not offenders,
        "; ".join(offenders[:MAX_REPORTED_OFFENDERS])
        if offenders
        else "no new Phase-number-prefixed validation filenames",
    )


def check_google_protobuf_collision(results: list[CheckResult]) -> None:
    """Ensure Google.Protobuf plugin asmdefs do not collide with DLL names."""
    plugin_dir = PACKAGE / "Plugins" / "Google.Protobuf"
    dll_stems = {p.stem for p in plugin_dir.glob("*.dll")}
    asmdef_files = list(plugin_dir.glob("*.asmdef"))
    filename_collisions = [rel(p) for p in asmdef_files if p.stem in dll_stems]

    name_collisions: list[str] = []
    for asmdef in asmdef_files:
        try:
            name = json.loads(asmdef.read_text(encoding="utf-8")).get("name")
        except Exception:
            continue
        if name in dll_stems:
            name_collisions.append(f"{rel(asmdef)} name={name}")

    offenders = filename_collisions + name_collisions
    add(results, "Google.Protobuf DLL/asmdef naming", not offenders, "; ".join(offenders) if offenders else "no collision")


def check_third_party_notices(results: list[CheckResult]) -> None:
    """Ensure every bundled binary dependency has a matching license notice."""
    if not THIRD_PARTY_NOTICES.exists():
        add(results, "third-party notices exist", False, rel(THIRD_PARTY_NOTICES))
        return

    notices = THIRD_PARTY_NOTICES.read_text(encoding="utf-8", errors="replace")
    missing: list[str] = []
    absent_artifacts: list[str] = []
    for artifact, required_tokens in THIRD_PARTY_NOTICE_REQUIREMENTS:
        if not artifact.exists():
            absent_artifacts.append(rel(artifact))
            continue
        absent = [token for token in required_tokens if token not in notices]
        if absent:
            missing.append(f"{rel(artifact)} missing {', '.join(absent)}")

    add(
        results,
        "third-party notice artifact scope visible",
        True,
        "all listed artifacts are bundled" if not absent_artifacts else "not bundled: " + "; ".join(absent_artifacts),
    )

    add(
        results,
        "third-party notices cover bundled binaries",
        not missing,
        "; ".join(missing) if missing else "all bundled binary notices present",
    )


def print_results(results: list[CheckResult]) -> None:
    """Print check results as aligned PASS/FAIL lines."""
    name_width = max(len(r.name) for r in results) if results else EMPTY_RESULT_NAME_WIDTH
    for result in results:
        status = "PASS" if result.ok else "FAIL"
        print(f"[{status}] {result.name:<{name_width}}  {result.detail}")


def main() -> int:
    """Run all release package checks and return a process exit code."""
    results: list[CheckResult] = []
    package_entries = list(PACKAGE.rglob("*")) if PACKAGE.exists() else []
    package_files = [path for path in package_entries if path.is_file()]
    samples_entries = [path for path in package_entries if path_is_relative_to(path, SAMPLES)]
    samples_files = [path for path in samples_entries if path.is_file()]
    docs_files = [path for path in package_files if path_is_relative_to(path, DOCS)]
    data = load_package_json(results)
    if data:
        check_package_identity(results, data)
        check_dependent_package_versions(results, data)
    check_ros2_bridge_package(results)
    check_optional_package_boundaries(results)
    check_required_files(results)
    check_sample_meta(results, samples_files)
    check_sample_boundaries(results)
    check_forbidden_public_content(results, samples_files, docs_files)
    check_forbidden_sample_artifacts(results, samples_entries)
    check_package_build_artifacts(results, package_entries)
    check_manual_phase_service_guards(results)
    check_validation_naming(results, package_files)
    check_google_protobuf_collision(results)
    check_third_party_notices(results)

    print_results(results)
    failed = [r for r in results if not r.ok]
    if failed:
        print(f"\nvalidate_unity_package: {len(failed)} check(s) failed.", file=sys.stderr)
        return EXIT_FAILURE

    print(f"\nvalidate_unity_package: {len(results)} check(s) passed.")
    return EXIT_SUCCESS


if __name__ == "__main__":
    raise SystemExit(main())
