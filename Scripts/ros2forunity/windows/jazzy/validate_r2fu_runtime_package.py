#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Validate the ROS2 For Unity Jazzy Win64 runtime Unity package prototype.
# Usage: python Scripts/ros2forunity/windows/jazzy/validate_r2fu_runtime_package.py
# Inputs: Packages/dev.unity2foxglove.ros2forunity.runtime.jazzy.win64 package directory.
# Outputs: Prints runtime package checks and exits nonzero on failure.

"""Validate the ROS2 For Unity Jazzy Win64 runtime package prototype."""

from __future__ import annotations

import json
import argparse
import hashlib
import re
import sys
from dataclasses import dataclass
from pathlib import Path, PurePosixPath
from typing import Iterable


REPO_ROOT_PARENT_DEPTH = 4
EXIT_SUCCESS = 0
EXIT_FAILURE = 1

ROOT = Path(__file__).resolve().parents[REPO_ROOT_PARENT_DEPTH]
PACKAGE_NAME = "dev.unity2foxglove.ros2forunity.runtime.jazzy.win64"
PACKAGE = ROOT / "Packages" / PACKAGE_NAME
ADAPTER_PACKAGE = ROOT / "Packages" / "dev.unity2foxglove.ros2forunity"
CORE_PACKAGE = ROOT / "Packages" / "dev.unity2foxglove.sdk"
RUNTIME_ROOT = PACKAGE / "Runtime" / "Ros2ForUnity"
PLUGIN_ROOT = RUNTIME_ROOT / "Plugins" / "Windows" / "x86_64"
MANIFEST = PACKAGE / "RuntimeSupport" / "runtime-manifest.json"
INVENTORY = PACKAGE / "RuntimeSupport" / "r2fu-jazzy-win64-runtime-inventory.json"

ARTIFACT_NAME = "Ros2ForUnity_jazzy_standalone_windows_x86_64.zip"
EXPECTED_ARTIFACT_SHA256 = "df4806b750435b3a1252f39b46dd2e4e60ddc0eb6ac57989bcf00adb23fe29f3"
EXPECTED_RMW_IMPLEMENTATION = "rmw_fastrtps_cpp"

CRITICAL_DLLS = (
    "rcl.dll",
    "yaml.dll",
    "spdlog.dll",
    "fmt.dll",
)
PHASE161_ADDED_DLLS = (
    "Ros2ForUnity/Plugins/Windows/x86_64/actionlib_msgs__rosidl_generator_c.dll",
    "Ros2ForUnity/Plugins/Windows/x86_64/actionlib_msgs__rosidl_typesupport_c.dll",
    "Ros2ForUnity/Plugins/Windows/x86_64/actionlib_msgs__rosidl_typesupport_cpp.dll",
    "Ros2ForUnity/Plugins/Windows/x86_64/actionlib_msgs__rosidl_typesupport_fastrtps_c.dll",
    "Ros2ForUnity/Plugins/Windows/x86_64/actionlib_msgs__rosidl_typesupport_fastrtps_cpp.dll",
    "Ros2ForUnity/Plugins/Windows/x86_64/actionlib_msgs__rosidl_typesupport_introspection_c.dll",
    "Ros2ForUnity/Plugins/Windows/x86_64/actionlib_msgs__rosidl_typesupport_introspection_cpp.dll",
    "Ros2ForUnity/Plugins/Windows/x86_64/actionlib_msgs_goal_id__rosidl_typesupport_c_native.dll",
    "Ros2ForUnity/Plugins/Windows/x86_64/actionlib_msgs_goal_id__rosidl_typesupport_fastrtps_c_native.dll",
    "Ros2ForUnity/Plugins/Windows/x86_64/actionlib_msgs_goal_id__rosidl_typesupport_introspection_c_native.dll",
    "Ros2ForUnity/Plugins/Windows/x86_64/actionlib_msgs_goal_status__rosidl_typesupport_c_native.dll",
    "Ros2ForUnity/Plugins/Windows/x86_64/actionlib_msgs_goal_status__rosidl_typesupport_fastrtps_c_native.dll",
    "Ros2ForUnity/Plugins/Windows/x86_64/actionlib_msgs_goal_status__rosidl_typesupport_introspection_c_native.dll",
    "Ros2ForUnity/Plugins/Windows/x86_64/actionlib_msgs_goal_status_array__rosidl_typesupport_c_native.dll",
    "Ros2ForUnity/Plugins/Windows/x86_64/actionlib_msgs_goal_status_array__rosidl_typesupport_fastrtps_c_native.dll",
    "Ros2ForUnity/Plugins/Windows/x86_64/actionlib_msgs_goal_status_array__rosidl_typesupport_introspection_c_native.dll",
    "Ros2ForUnity/Plugins/Windows/x86_64/geometry_msgs_pose2_d__rosidl_typesupport_c_native.dll",
    "Ros2ForUnity/Plugins/Windows/x86_64/geometry_msgs_pose2_d__rosidl_typesupport_fastrtps_c_native.dll",
    "Ros2ForUnity/Plugins/Windows/x86_64/geometry_msgs_pose2_d__rosidl_typesupport_introspection_c_native.dll",
    "Ros2ForUnity/Plugins/Windows/x86_64/static_transform_broadcaster_node.dll",
    "Ros2ForUnity/Plugins/Windows/x86_64/stereo_msgs__rosidl_generator_c.dll",
    "Ros2ForUnity/Plugins/Windows/x86_64/stereo_msgs__rosidl_typesupport_c.dll",
    "Ros2ForUnity/Plugins/Windows/x86_64/stereo_msgs__rosidl_typesupport_cpp.dll",
    "Ros2ForUnity/Plugins/Windows/x86_64/stereo_msgs__rosidl_typesupport_fastrtps_c.dll",
    "Ros2ForUnity/Plugins/Windows/x86_64/stereo_msgs__rosidl_typesupport_fastrtps_cpp.dll",
    "Ros2ForUnity/Plugins/Windows/x86_64/stereo_msgs__rosidl_typesupport_introspection_c.dll",
    "Ros2ForUnity/Plugins/Windows/x86_64/stereo_msgs__rosidl_typesupport_introspection_cpp.dll",
    "Ros2ForUnity/Plugins/Windows/x86_64/stereo_msgs_disparity_image__rosidl_typesupport_c_native.dll",
    "Ros2ForUnity/Plugins/Windows/x86_64/stereo_msgs_disparity_image__rosidl_typesupport_fastrtps_c_native.dll",
    "Ros2ForUnity/Plugins/Windows/x86_64/stereo_msgs_disparity_image__rosidl_typesupport_introspection_c_native.dll",
    "Ros2ForUnity/Plugins/Windows/x86_64/tf2.dll",
    "Ros2ForUnity/Plugins/Windows/x86_64/tf2_ros.dll",
    "Ros2ForUnity/Plugins/Windows/x86_64/rosidl_dynamic_typesupport_fastrtps.dll",
    "Ros2ForUnity/Plugins/actionlib_msgs_assembly.dll",
    "Ros2ForUnity/Plugins/stereo_msgs_assembly.dll",
)
PHASE161_SUPPLEMENTAL_RUNTIME_DLLS = (
    "Ros2ForUnity/Plugins/Windows/x86_64/rosidl_dynamic_typesupport_fastrtps.dll",
)
PHASE161_ALLOWED_STALE_REMOVED_DLLS = (
    "Ros2ForUnity/Plugins/Windows/x86_64/geometry_msgs_velocity_with_covariance_stamped__rosidl_typesupport_c_native.dll",
    "Ros2ForUnity/Plugins/Windows/x86_64/geometry_msgs_velocity_with_covariance_stamped__rosidl_typesupport_fastrtps_c_native.dll",
    "Ros2ForUnity/Plugins/Windows/x86_64/geometry_msgs_velocity_with_covariance_stamped__rosidl_typesupport_introspection_c_native.dll",
    "Ros2ForUnity/Plugins/Windows/x86_64/test_msgs_complex_nested_key__rosidl_typesupport_c_native.dll",
    "Ros2ForUnity/Plugins/Windows/x86_64/test_msgs_complex_nested_key__rosidl_typesupport_fastrtps_c_native.dll",
    "Ros2ForUnity/Plugins/Windows/x86_64/test_msgs_complex_nested_key__rosidl_typesupport_introspection_c_native.dll",
    "Ros2ForUnity/Plugins/Windows/x86_64/test_msgs_keyed_long__rosidl_typesupport_c_native.dll",
    "Ros2ForUnity/Plugins/Windows/x86_64/test_msgs_keyed_long__rosidl_typesupport_fastrtps_c_native.dll",
    "Ros2ForUnity/Plugins/Windows/x86_64/test_msgs_keyed_long__rosidl_typesupport_introspection_c_native.dll",
    "Ros2ForUnity/Plugins/Windows/x86_64/test_msgs_keyed_string__rosidl_typesupport_c_native.dll",
    "Ros2ForUnity/Plugins/Windows/x86_64/test_msgs_keyed_string__rosidl_typesupport_fastrtps_c_native.dll",
    "Ros2ForUnity/Plugins/Windows/x86_64/test_msgs_keyed_string__rosidl_typesupport_introspection_c_native.dll",
    "Ros2ForUnity/Plugins/Windows/x86_64/test_msgs_non_keyed_with_nested_key__rosidl_typesupport_c_native.dll",
    "Ros2ForUnity/Plugins/Windows/x86_64/test_msgs_non_keyed_with_nested_key__rosidl_typesupport_fastrtps_c_native.dll",
    "Ros2ForUnity/Plugins/Windows/x86_64/test_msgs_non_keyed_with_nested_key__rosidl_typesupport_introspection_c_native.dll",
)
PHASE161_ASSET_CRITICAL_BASELINE = (
    "Ros2ForUnity/Plugins/builtin_interfaces_assembly.dll",
    "Ros2ForUnity/Plugins/std_msgs_assembly.dll",
    "Ros2ForUnity/Plugins/sensor_msgs_assembly.dll",
    "Ros2ForUnity/Plugins/tf2_msgs_assembly.dll",
    "Ros2ForUnity/Plugins/rosgraph_msgs_assembly.dll",
    "Ros2ForUnity/Plugins/Windows/x86_64/rosgraph_msgs__rosidl_typesupport_fastrtps_c.dll",
    "Ros2ForUnity/Plugins/Windows/x86_64/rosgraph_msgs__rosidl_typesupport_fastrtps_cpp.dll",
)

MODIFICATIONS_COPYRIGHT = "Modifications Copyright (c) 2026 Jianbin Liu"
LOCAL_PATCH_MARKER = "U2F-LOCAL-PATCH"
LEAKY_UPSTREAM_EXAMPLES = (
    "ROS2TalkerExample.cs",
    "ROS2ListenerExample.cs",
    "ROS2ClientExample.cs",
    "ROS2ServiceExample.cs",
    "ROS2PerformanceTest.cs",
    "PostInstall.cs",
)
PATCHED_VENDOR_FILES = (
    "ROS2ForUnity.cs",
    "ROS2Node.cs",
    "ROS2UnityComponent.cs",
    "ROS2UnityCore.cs",
    "Sensor.cs",
    "Transformations.cs",
    "Time/DotnetTimeSource.cs",
    "Time/ITimeSource.cs",
    "Time/ROS2Clock.cs",
    "Time/ROS2ScalableTimeSource.cs",
    "Time/ROS2TimeSource.cs",
    "Time/TimeUtils.cs",
    "Time/UnityTimeSource.cs",
)

PUBLIC_DOCS = (
    PACKAGE / "README.md",
    PACKAGE / "THIRD_PARTY_NOTICES.md",
    PACKAGE / "package.json",
    MANIFEST,
)

INTERNAL_TOKENS = (
    "Phase",
    "phase",
    "137B",
    "106B",
    "Phase110",
)


@dataclass
class CheckResult:
    """Structured result for one runtime package validation check."""

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
    """Yield files below a root, returning an empty iterable when absent."""
    if not root.exists():
        return ()
    return (path for path in root.rglob("*") if path.is_file())


def load_json(path: Path, results: list[CheckResult], name: str) -> dict:
    """Load JSON and record whether parsing succeeded."""
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except Exception as exc:
        add(results, name, False, f"{rel(path)}: {exc}")
        return {}
    add(results, name, True, rel(path))
    return data


def read_optional_text(path: Path) -> str:
    """Read UTF-8 text when present, returning an empty string for absent files."""
    return path.read_text(encoding="utf-8", errors="replace") if path.exists() else ""


def file_sha256(path: Path) -> str:
    """Return the SHA-256 digest for a package payload file."""
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def is_sha256(value: object) -> bool:
    """Return true for a complete lowercase SHA-256 hex string."""
    return isinstance(value, str) and re.fullmatch(r"[0-9a-f]{64}", value) is not None


def unity_editor_using_is_guarded(text: str) -> bool:
    """Return true when every UnityEditor using is inside a UNITY_EDITOR block."""
    in_unity_editor = False
    found = False
    for line in text.splitlines():
        stripped = line.strip()
        if stripped.startswith("#if") and "UNITY_EDITOR" in stripped:
            in_unity_editor = True
            continue
        if stripped.startswith("#endif"):
            in_unity_editor = False
            continue
        if "using UnityEditor;" in stripped:
            found = True
            if not in_unity_editor:
                return False
    return found


def check_package_metadata(results: list[CheckResult]) -> None:
    """Validate Unity package metadata."""
    add(results, "runtime package folder exists", PACKAGE.is_dir(), rel(PACKAGE))
    data = load_json(PACKAGE / "package.json", results, "package.json parses")
    if not data:
        return

    expected = {
        "name": PACKAGE_NAME,
        "version": "0.1.0-preview.1",
        "displayName": "Unity2Foxglove ROS2 For Unity Runtime - Jazzy Win64",
        "license": "Apache-2.0",
        "unity": "6000.0",
        "description": "Optional prototype Jazzy Windows x64 runtime package for Unity2Foxglove ROS2 For Unity integration; fresh-project acceptance and legal attribution review are required before production redistribution.",
    }
    for key, value in expected.items():
        add(results, f"package {key}", data.get(key) == value, f"expected {value!r}, got {data.get(key)!r}")

    add(results, "package has no dependencies", "dependencies" not in data, f"dependencies={data.get('dependencies')!r}")
    keywords = data.get("keywords", [])
    add(
        results,
        "package keywords",
        isinstance(keywords, list) and {"ros2", "ros2-for-unity", "jazzy", "win64"}.issubset(set(keywords)),
        f"keywords={keywords!r}",
    )


def check_required_files(results: list[CheckResult]) -> None:
    """Validate files required by the runtime package contract."""
    required = [
        PACKAGE / "README.md",
        PACKAGE / "LICENSE",
        PACKAGE / "THIRD_PARTY_NOTICES.md",
        MANIFEST,
        INVENTORY,
        RUNTIME_ROOT / "metadata_ros2_for_unity.xml",
        RUNTIME_ROOT / "metadata_ros2cs.xml",
        RUNTIME_ROOT / "Plugins" / "metadata_ros2cs.xml",
        PLUGIN_ROOT / "metadata_ros2cs.xml",
        RUNTIME_ROOT / "Scripts" / "ROS2ForUnity.cs",
        RUNTIME_ROOT / "Scripts" / "ROS2UnityComponent.cs",
        RUNTIME_ROOT / "Scripts" / "ROS2UnityCore.cs",
        RUNTIME_ROOT / "Scripts" / "Unity2Foxglove.Ros2ForUnity.Runtime.JazzyWin64.asmdef",
        RUNTIME_ROOT / "Plugins" / "ros2cs_core.dll",
        RUNTIME_ROOT / "Plugins" / "ros2cs_common.dll",
        RUNTIME_ROOT / "Plugins" / "std_msgs_assembly.dll",
    ]
    for path in required:
        add(results, f"required file: {path.name}", path.exists(), rel(path))


def check_runtime_manifest(results: list[CheckResult], data: dict) -> None:
    """Validate the runtime support manifest."""
    if not data:
        return

    expected = {
        "schemaVersion": 1,
        "runtimeId": "r2fu-jazzy-win64",
        "packageName": PACKAGE_NAME,
        "packageVersion": "0.1.0-preview.1",
        "rosDistro": "jazzy",
        "platform": "win64",
        "unityPlatform": "Windows",
        "architecture": "x86_64",
        "buildType": "standalone",
        "rmwImplementation": EXPECTED_RMW_IMPLEMENTATION,
        "artifactName": ARTIFACT_NAME,
        "inventoryFile": "RuntimeSupport/r2fu-jazzy-win64-runtime-inventory.json",
        "runtimeRoot": "Runtime/Ros2ForUnity",
        "pluginPath": "Runtime/Ros2ForUnity/Plugins/Windows/x86_64",
        "supportLevel": "Recommended",
        "distributionLevel": "Prototype",
        "activeRuntimePolicy": "one_runtime_package_per_project",
        "freshProjectAcceptance": "deferred_to_install_acceptance",
    }
    for key, value in expected.items():
        add(results, f"runtime manifest {key}", data.get(key) == value, f"expected {value!r}, got {data.get(key)!r}")

    artifact_sha = data.get("artifactSha256")
    artifact_size = data.get("artifactSize")
    inventory_file_count = data.get("inventoryFileCount")
    add(
        results,
        "runtime manifest artifactSha256",
        artifact_sha == EXPECTED_ARTIFACT_SHA256,
        f"artifactSha256={artifact_sha!r}",
    )
    add(results, "runtime manifest artifactSize", isinstance(artifact_size, int) and artifact_size > 0, f"artifactSize={artifact_size!r}")
    add(
        results,
        "runtime manifest inventoryFileCount",
        isinstance(inventory_file_count, int) and inventory_file_count > 0,
        f"inventoryFileCount={inventory_file_count!r}",
    )

    source_basis = str(data.get("sourceBasis", ""))
    add(
        results,
        "runtime manifest source basis public",
        "Jazzy" in source_basis and "Phase" not in source_basis and "phase" not in source_basis,
        source_basis,
    )

    critical = data.get("criticalRuntimeFiles", [])
    add(
        results,
        "runtime manifest critical DLLs",
        isinstance(critical, list) and set(CRITICAL_DLLS).issubset(set(critical)),
        f"criticalRuntimeFiles={critical!r}",
    )

    delta = data.get("handoffInventoryDelta", {})
    add(
        results,
        "runtime manifest Phase161 added DLL set",
        isinstance(delta, dict) and set(delta.get("addedDlls", [])) == set(PHASE161_ADDED_DLLS),
        f"handoffInventoryDelta={delta!r}",
    )
    add(
        results,
        "runtime manifest Phase161 allowed removed stale DLL set",
        isinstance(delta, dict)
        and set(delta.get("allowedRemovedStaleBackupDlls", [])) == set(PHASE161_ALLOWED_STALE_REMOVED_DLLS),
        f"handoffInventoryDelta={delta!r}",
    )
    add(
        results,
        "runtime manifest Phase161 asset-critical baseline",
        isinstance(delta, dict) and set(PHASE161_ASSET_CRITICAL_BASELINE).issubset(set(delta.get("assetCriticalBaseline", []))),
        f"handoffInventoryDelta={delta!r}",
    )

    patch = data.get("packagePathPatch", {})
    add(
        results,
        "runtime manifest package path patch",
        isinstance(patch, dict)
        and patch.get("modifiedFile") == "Runtime/Ros2ForUnity/Scripts/ROS2ForUnity.cs"
        and patch.get("keepsAssetFolderFallback") is True,
        f"packagePathPatch={patch!r}",
    )


def check_inventory(results: list[CheckResult], manifest: dict, release_gate: bool = False, skip_dll_hash: bool = False) -> None:
    """Validate the copied runtime inventory."""
    data = load_json(INVENTORY, results, "runtime inventory parses")
    if not data:
        return

    expected = {
        "schemaVersion": 1,
        "runtimeId": "r2fu-jazzy-win64",
        "artifactName": ARTIFACT_NAME,
        "rosDistro": "jazzy",
        "rmw": EXPECTED_RMW_IMPLEMENTATION,
        "platform": "win64",
        "buildType": "standalone",
    }
    for key, value in expected.items():
        add(results, f"runtime inventory {key}", data.get(key) == value, f"expected {value!r}, got {data.get(key)!r}")

    add(
        results,
        "runtime inventory sha256 matches manifest",
        data.get("sha256") == manifest.get("artifactSha256") == EXPECTED_ARTIFACT_SHA256,
        f"inventory={data.get('sha256')!r}, manifest={manifest.get('artifactSha256')!r}",
    )
    add(
        results,
        "runtime inventory artifactSize matches manifest",
        data.get("artifactSize") == manifest.get("artifactSize"),
        f"inventory={data.get('artifactSize')!r}, manifest={manifest.get('artifactSize')!r}",
    )
    add(
        results,
        "runtime inventory fileCount matches manifest",
        data.get("fileCount") == manifest.get("inventoryFileCount"),
        f"inventory={data.get('fileCount')!r}, manifest={manifest.get('inventoryFileCount')!r}",
    )

    redistribution_status = str(data.get("redistributionStatus", ""))
    add(
        results,
        "runtime inventory redistributionStatus recorded",
        redistribution_status in {"candidate_not_published", "published"},
        f"redistributionStatus={redistribution_status!r}",
    )
    if release_gate:
        add(
            results,
            "release gate: runtime redistributionStatus is published",
            redistribution_status == "published",
            f"redistributionStatus={redistribution_status!r}",
        )

    categories = data.get("categoryCounts", {})
    add(
        results,
        "runtime inventory native library count",
        isinstance(categories, dict) and categories.get("native_libraries", 0) >= 900,
        f"categoryCounts={categories!r}",
    )

    critical = data.get("knownCriticalFiles", [])
    present = {
        item.get("name")
        for item in critical
        if isinstance(item, dict) and item.get("present") is True
    }
    add(
        results,
        "runtime inventory critical files present",
        set(CRITICAL_DLLS).issubset(present),
        f"present={sorted(present)!r}",
    )

    files = data.get("files", [])
    malformed: list[str] = []
    missing: list[str] = []
    mismatched: list[str] = []
    unreadable: list[str] = []
    checked_dlls = 0
    should_hash_dlls = release_gate or not skip_dll_hash
    if isinstance(files, list):
        for item in files:
            if not isinstance(item, dict):
                malformed.append(repr(item))
                continue
            path_text = str(item.get("path", ""))
            if not path_text.lower().endswith(".dll"):
                continue
            checked_dlls += 1
            expected_hash = str(item.get("sha256", "")).lower()
            parts = PurePosixPath(path_text).parts
            if len(parts) < 2 or parts[0] != "Ros2ForUnity":
                malformed.append(path_text)
                continue
            package_path = RUNTIME_ROOT.joinpath(*parts[1:])
            if not package_path.is_file():
                missing.append(path_text)
                continue
            if should_hash_dlls and expected_hash:
                try:
                    actual_hash = file_sha256(package_path)
                except OSError:
                    unreadable.append(path_text)
                    continue
                if actual_hash != expected_hash:
                    mismatched.append(path_text)

    add(
        results,
        "runtime inventory DLL files exist on disk",
        isinstance(files, list) and checked_dlls >= 900 and not malformed and not missing,
        f"checked_dlls={checked_dlls} malformed={malformed[:8]!r} missing={missing[:8]!r}",
    )
    add(
        results,
        "runtime inventory DLL hashes match disk" if should_hash_dlls else "runtime inventory DLL hash verification skipped",
        isinstance(files, list) and checked_dlls >= 900 and (not should_hash_dlls or (not mismatched and not unreadable)),
        (
            "skipped by fast validation; use --release-gate for full DLL hash verification"
            if not should_hash_dlls
            else f"checked_dlls={checked_dlls} mismatched={mismatched[:8]!r} unreadable={unreadable[:8]!r}"
        ),
    )

    file_paths = {str(item.get("path", "")) for item in files if isinstance(item, dict)}
    artifact_added_dlls = set(PHASE161_ADDED_DLLS) - set(PHASE161_SUPPLEMENTAL_RUNTIME_DLLS)
    add(
        results,
        "Phase161 added DLL paths are present",
        artifact_added_dlls.issubset(file_paths),
        f"missing={sorted(artifact_added_dlls - file_paths)!r}",
    )
    add(
        results,
        "Phase161 stale old-backup DLL paths are absent",
        not (set(PHASE161_ALLOWED_STALE_REMOVED_DLLS) & file_paths),
        f"unexpected={sorted(set(PHASE161_ALLOWED_STALE_REMOVED_DLLS) & file_paths)!r}",
    )
    add(
        results,
        "Phase161 asset-critical baseline paths are present",
        set(PHASE161_ASSET_CRITICAL_BASELINE).issubset(file_paths),
        f"missing={sorted(set(PHASE161_ASSET_CRITICAL_BASELINE) - file_paths)!r}",
    )


def check_runtime_files(results: list[CheckResult]) -> None:
    """Validate critical runtime files and package layout."""
    for dll in CRITICAL_DLLS:
        path = PLUGIN_ROOT / dll
        add(results, f"critical DLL present: {dll}", path.exists(), rel(path))

    for runtime_path in PHASE161_SUPPLEMENTAL_RUNTIME_DLLS:
        path = PACKAGE / "Runtime" / Path(runtime_path)
        add(results, f"supplemental runtime DLL present: {Path(runtime_path).name}", path.exists(), rel(path))

    dlls = list(PLUGIN_ROOT.glob("*.dll")) if PLUGIN_ROOT.exists() else []
    add(results, "Windows x86_64 DLL payload", len(dlls) >= 900, f"dll_count={len(dlls)}")
    plugin_meta_failures = []
    for dll in dlls:
        meta = dll.with_name(dll.name + ".meta")
        text = read_optional_text(meta)
        if "PluginImporter:" not in text:
            plugin_meta_failures.append(rel(meta))
    add(
        results,
        "Windows x86_64 DLL metas use PluginImporter",
        len(dlls) >= 900 and not plugin_meta_failures,
        ", ".join(plugin_meta_failures[:8]),
    )
    add(results, "no root zip sidecar copied", not any(PACKAGE.glob("*.zip")) and not any(PACKAGE.glob("*.sha256")), rel(PACKAGE))

    copied_paths = [path.relative_to(PACKAGE).as_posix() for path in iter_files(PACKAGE)]
    sample_hits = [path for path in copied_paths if "Phase110Ros2ForUnity" in path or "External Adapter" in path]
    add(results, "runtime package does not duplicate adapter samples", not sample_hits, ", ".join(sample_hits[:8]))

    unexpected_platforms = [
        path
        for path in copied_paths
        if path.startswith("Runtime/Ros2ForUnity/Plugins/")
        and ("/Linux/" in path or "/Mac" in path or "/macOS/" in path)
    ]
    add(results, "runtime plugin payload limited to Windows", not unexpected_platforms, ", ".join(unexpected_platforms[:8]))

    scripts = RUNTIME_ROOT / "Scripts"
    leaky_examples = [name for name in LEAKY_UPSTREAM_EXAMPLES if (scripts / name).exists()]
    add(results, "leaky upstream examples pruned", not leaky_examples, ", ".join(leaky_examples))


def check_package_path_patch(results: list[CheckResult]) -> None:
    """Validate the ROS2ForUnity.cs package path patch."""
    source = RUNTIME_ROOT / "Scripts" / "ROS2ForUnity.cs"
    text = source.read_text(encoding="utf-8", errors="replace") if source.exists() else ""
    required = [
        "Unity2Foxglove package path support",
        PACKAGE_NAME,
        "PackageInfo.FindForAssetPath",
        "resolvedPath",
        "unity2FoxgloveRuntimePackageAssetPath",
        "SetProcessEnvironmentVariable(GetEnvPathVariableName()",
        'Path.Combine(',
        '"Packages"',
        '"Runtime"',
        "Directory.Exists(packagePath)",
        "return assetPath;",
    ]
    for token in required:
        add(results, f"ROS2ForUnity.cs contains {token}", token in text, token)
    add(
        results,
        "UnityEditor using guarded",
        unity_editor_using_is_guarded(text),
        "ROS2ForUnity.cs",
    )
    add(
        results,
        "PackageManager lookup guarded",
        re.search(
            r"#if\s+UNITY_EDITOR\s+UnityEditor\.PackageManager\.PackageInfo\s+\w+\s*="
            r"\s*UnityEditor\.PackageManager\.PackageInfo\.FindForAssetPath",
            text,
            re.S,
        )
        is not None,
        "ROS2ForUnity.cs",
    )
    add(
        results,
        "standalone PATH update reaches native environment",
        "Environment.SetEnvironmentVariable(GetEnvPathVariableName()," not in text,
        "ROS2ForUnity.cs",
    )


def check_runtime_asmdef(results: list[CheckResult]) -> None:
    """Validate the runtime assembly definition is safe for Editor and Player."""
    path = RUNTIME_ROOT / "Scripts" / "Unity2Foxglove.Ros2ForUnity.Runtime.JazzyWin64.asmdef"
    data = load_json(path, results, "runtime asmdef parses")
    add(results, "runtime asmdef name", data.get("name") == "Unity2Foxglove.Ros2ForUnity.Runtime", f"name={data.get('name')!r}")
    add(
        results,
        "runtime asmdef targets Windows runtime and editor",
        data.get("includePlatforms") == ["Editor", "WindowsStandalone64"],
        f"includePlatforms={data.get('includePlatforms')!r}",
    )
    add(results, "runtime asmdef auto-referenced", data.get("autoReferenced") is True, f"autoReferenced={data.get('autoReferenced')!r}")
    add(results, "runtime asmdef has no define gate", "defineConstraints" not in data, f"defineConstraints={data.get('defineConstraints')!r}")


def check_runtime_source_patches(results: list[CheckResult]) -> None:
    """Validate local lifecycle, time-source, and attribution patches on vendored R2FU sources."""
    scripts = RUNTIME_ROOT / "Scripts"
    for relative in PATCHED_VENDOR_FILES:
        path = scripts / relative
        text = path.read_text(encoding="utf-8", errors="replace") if path.exists() else ""
        add(results, f"patched vendored file exists: {relative}", path.exists(), rel(path))
        add(results, f"patched vendored attribution: {relative}", MODIFICATIONS_COPYRIGHT in text, relative)

    node = read_optional_text(scripts / "ROS2Node.cs")
    add(results, "ROS2Node implements IDisposable", "class ROS2Node : IDisposable" in node and "public void Dispose()" in node, "ROS2Node.cs")
    add(results, "ROS2Node avoids finalizer native cleanup", "~ROS2Node" not in node, "ROS2Node.cs")
    add(results, "ROS2Node removed UnityEditor using", "using UnityEditor;" not in node, "ROS2Node.cs")

    component = read_optional_text(scripts / "ROS2UnityComponent.cs")
    component_join = "threadToJoin.Join(1000)" in component or "threadToJoin.Join(TimeSpan.FromSeconds(2))" in component
    for token in (
        LOCAL_PATCH_MARKER,
        "private volatile bool quitting",
        "OnDestroy()",
        "OnApplicationQuit()",
        "node.Dispose()",
        "StopExecutor()",
        "TryDetachRuntimeState",
        "QuarantineNodesAfterExecutorTimeout",
        "ReferenceEquals(executorThread, threadToJoin)",
        "nodesSnapshot.AddRange(ros2csNodes)",
        "Ros2cs.SpinOnce(nodesSnapshot, spinTimeout)",
        "runtimeShutdownRequested",
        "ROS2ForUnity.PrewarmUnityMainThreadPaths();",
        "throw new ObjectDisposedException(nameof(ROS2UnityComponent))",
        "StopAllExecutorsForRosShutdown()",
        "MarkRuntimeShutdown()",
    ):
        add(results, f"ROS2UnityComponent lifecycle token: {token}", token in component, token)
    add(
        results,
        "ROS2UnityComponent executor thread",
        "private Thread spinThread" in component or "private Thread executorThread" in component,
        "ROS2UnityComponent.cs",
    )
    add(results, "ROS2UnityComponent bounded join", component_join, "ROS2UnityComponent.cs")
    add(results, "ROS2UnityComponent does not shutdown on ordinary disable", "OnDisable()" not in component, "ROS2UnityComponent.cs")

    core = read_optional_text(scripts / "ROS2UnityCore.cs")
    core_join = "threadToJoin.Join(1000)" in core or "threadToJoin.Join(TimeSpan.FromSeconds(2))" in core
    for token in (
        LOCAL_PATCH_MARKER,
        "IDisposable",
        "private volatile bool quitting",
        "public void Dispose()",
        "StopExecutor()",
        "TryDetachRuntimeState",
        "QuarantineNodesAfterExecutorTimeout",
        "ReferenceEquals(executorThread, threadToJoin)",
        "nodesSnapshot.AddRange(ros2csNodes)",
        "Ros2cs.SpinOnce(nodesSnapshot, spinTimeout)",
        "ROS2ForUnity.PrewarmUnityMainThreadPaths();",
    ):
        add(results, f"ROS2UnityCore lifecycle token: {token}", token in core, token)
    add(
        results,
        "ROS2UnityCore executor thread",
        "private Thread spinThread" in core or "private Thread executorThread" in core,
        "ROS2UnityCore.cs",
    )
    add(results, "ROS2UnityCore bounded join", core_join, "ROS2UnityCore.cs")

    runtime = read_optional_text(scripts / "ROS2ForUnity.cs")
    old_tokens = ("ownerCount", "ownsLifecycle", "lifecycleGate", "UnregisterCallbacks()", "editorCallbacksRegistered")
    current_tokens = ("referenceCount", "ownsReference", "initMutex", "ShutdownShared()", "editorHandlersRegistered")
    old_lifecycle = all(token in runtime for token in old_tokens)
    current_lifecycle = all(token in runtime for token in current_tokens)
    mixed_partial = old_lifecycle and any(token in runtime for token in current_tokens) and not current_lifecycle
    add(
        results,
        "ROS2ForUnity deterministic lifecycle",
        (old_lifecycle or current_lifecycle) and not mixed_partial,
        f"old_lifecycle={old_lifecycle} current_lifecycle={current_lifecycle} mixed_partial={mixed_partial}",
    )
    add(results, "ROS2ForUnity avoids finalizer shutdown", "~ROS2ForUnity" not in runtime, "ROS2ForUnity.cs")
    add(
        results,
        "ROS2ForUnity uses non-obsolete ros2cs logger callback API",
        "Ros2csLogger.SetCallback" in runtime and "Ros2csLogger.setCallback" not in runtime,
        "ROS2ForUnity.cs",
    )
    add(
        results,
        "ROS2ForUnity enforces expected RMW",
        "expectedRmwImplementation" in runtime
        and "ValidateRmwImplementation" in runtime
        and EXPECTED_RMW_IMPLEMENTATION in runtime,
        "ROS2ForUnity.cs",
    )
    env_tokens = (
        "SetProcessEnvironmentVariable",
        "_wputenv_s",
        "SetStandalonePrefixPath",
        "AMENT_PREFIX_PATH",
        "SetStandaloneRmwImplementation",
        "RMW_IMPLEMENTATION",
        "SetEnvPathVariable();",
    )
    add(
        results,
        "ROS2ForUnity configures standalone native environment before init",
        all(token in runtime for token in env_tokens)
        and "sourcedRosDistroBeforeStandalonePatch" in runtime
        and "SetStandaloneRosDistro" in runtime
        and "WarnIfStandaloneRosDistroOverride" in runtime
        and "private static void FailIntegrity" in runtime
        and "public static void PrewarmUnityMainThreadPaths()" in runtime
        and "const int ROS_BAD_RMW_CODE = 36;" in runtime
        and "Unable to suppress Ros2cs finalizer before shutdown" in runtime
        and "Debug.LogError(\"Unable to suppress Ros2cs finalizer before shutdown" in runtime
        and "CheckIntegrity(standaloneBuild ? null : sourcedRosDistroBeforeStandalonePatch)" in runtime
        and "ROS2 version in standalone process environment does not match this runtime package" not in runtime
        and "ROS2UnityComponent.StopAllExecutorsForRosShutdown()" in runtime
        and runtime.find("SetStandalonePrefixPath();") < runtime.find("Ros2cs.Init()")
        and runtime.find("SetStandaloneRmwImplementation();") < runtime.find("Ros2cs.Init()")
        and runtime.find("SetStandaloneRosDistro(currentRos2Version);") < runtime.find("Ros2cs.Init()")
        and runtime.find("SetEnvPathVariable();") < runtime.find("Ros2cs.Init()"),
        "ROS2ForUnity.cs",
    )

    dotnet_time = read_optional_text(scripts / "Time" / "DotnetTimeSource.cs")
    add(
        results,
        "DotnetTimeSource converts Stopwatch duration to seconds",
        ("Stopwatch.Frequency" in dotnet_time and "ElapsedTicks" in dotnet_time)
        or "stopwatch.Elapsed.TotalSeconds" in dotnet_time,
        "DotnetTimeSource.cs",
    )

    for relative in ("Time/ROS2TimeSource.cs", "Time/ROS2ScalableTimeSource.cs"):
        source = read_optional_text(scripts / relative)
        add(
            results,
            f"{relative} implements bool ITimeSource.GetTime",
            "public bool GetTime(out int seconds, out uint nanoseconds)" in source
            and "public void GetTime(out int seconds, out uint nanoseconds)" not in source,
            relative,
        )
        add(
            results,
            f"{relative} reports unavailable ROS time",
            "return false;" in source and "return true;" in source,
            relative,
        )
        add(
            results,
            f"{relative} bool contract patch is marked",
            "bool-returning ITimeSource contract" in source,
            relative,
        )

    time_utils = read_optional_text(scripts / "Time" / "TimeUtils.cs")
    add(
        results,
        "TimeUtils normalizes nanoseconds",
        "Math.Floor(secondsIn)" in time_utils
        and (
            "normalizedNanoseconds < 0" in time_utils
            or "wholeNanoseconds >= 1000000000" in time_utils
            or "wholeNanoseconds >= NanosecondsPerSecond" in time_utils
        ),
        "TimeUtils.cs",
    )
    add(results, "TimeUtils does not cast modulo directly", "(uint)(nanosec % 1e9)" not in time_utils and "(uint)(nanosec % 1000000000)" not in time_utils, "TimeUtils.cs")

    sensor = read_optional_text(scripts / "Sensor.cs")
    add(results, "Sensor uses short-circuit publisher guard", "publisher != null && publishing" in sensor, "Sensor.cs")
    readings_guard_index = sensor.find("if (readings != null)")
    readings_deref_index = sensor.find("readings.SetHeaderFrame")
    sensor_null_guard = (
        (readings_guard_index >= 0 and readings_deref_index >= 0 and readings_guard_index < readings_deref_index)
        or ("if (acquiredReading == null)" in sensor and "acquiredReading.SetHeaderFrame" in sensor)
    )
    add(results, "Sensor checks readings before dereference", sensor_null_guard, "Sensor.cs")
    add(results, "Sensor unregisters executable action", "UnregisterExecutable" in sensor, "Sensor.cs")


def check_generator_alignment(results: list[CheckResult]) -> None:
    """Validate the generator knows about the lifecycle-patched package shape."""
    generator_path = ROOT / "Scripts" / "ros2forunity" / "windows" / "jazzy" / "build_r2fu_runtime_package.py"
    try:
        generator = generator_path.read_text(encoding="utf-8", errors="replace")
    except OSError as exc:
        add(results, "runtime package generator script readable", False, f"{rel(generator_path)}: {exc}")
        return

    add(results, "runtime package generator script readable", True, rel(generator_path))
    required = (
        "collect_local_patch_overlays",
        "apply_local_patch_overlays",
        "collect_meta_overlays",
        "apply_meta_overlays",
        "LOCAL_PATCH_OVERLAY_FILES",
        "patch_ros_time_source_contract",
        "LEAKY_UPSTREAM_EXAMPLES",
        "runtime_asmdef",
        "make_writable",
        "windows_long_path",
        "PackageInfo.FindForAssetPath",
        "UNITY_EDITOR",
    )
    for token in required:
        add(results, f"runtime package generator token: {token}", token in generator, token)


def check_public_docs(results: list[CheckResult], manifest: dict) -> None:
    """Validate public runtime docs avoid internal planning names."""
    combined = ""
    for path in PUBLIC_DOCS:
        combined += "\n" + path.read_text(encoding="utf-8", errors="replace") if path.exists() else ""
    hits = sorted({token for token in INTERNAL_TOKENS if token in combined})
    add(results, "runtime public docs avoid internal planning names", not hits, ", ".join(hits) if hits else "clean")

    readme = (PACKAGE / "README.md").read_text(encoding="utf-8", errors="replace") if (PACKAGE / "README.md").exists() else ""
    add(
        results,
        "README documents standalone and combined behavior",
        "runtime.jazzy.win64" in readme and "adapter" in readme and "combined Unity2Foxglove workflow" in readme,
        "README.md",
    )
    add(
        results,
        "README documents one-runtime policy",
        "Install only one" in readme and ("ros2forunity.runtime." in readme or "runtime packages" in readme),
        "README.md",
    )
    artifact_sha = str(manifest.get("artifactSha256", "")).strip()
    add(
        results,
        "README documents artifact SHA-256",
        is_sha256(artifact_sha) and artifact_sha in readme,
        "README.md",
    )
    notices = (PACKAGE / "THIRD_PARTY_NOTICES.md").read_text(encoding="utf-8", errors="replace") if (PACKAGE / "THIRD_PARTY_NOTICES.md").exists() else ""
    add(
        results,
        "THIRD_PARTY_NOTICES documents artifact SHA-256",
        is_sha256(artifact_sha) and artifact_sha in notices,
        "THIRD_PARTY_NOTICES.md",
    )
    add(
        results,
        "README documents WSL2 NAT topology limit",
        "WSL2 NAT" in readme and "diagnostic-only" in readme and "Windows Defender Firewall" in readme,
        "README.md",
    )


def check_package_boundaries(results: list[CheckResult]) -> None:
    """Validate the SDK and adapter package dependency boundaries."""
    sdk_package = load_json(CORE_PACKAGE / "package.json", results, "core package.json parses")
    adapter_package = load_json(ADAPTER_PACKAGE / "package.json", results, "adapter package.json parses")

    sdk_deps = json.dumps(sdk_package.get("dependencies", {}), sort_keys=True)
    adapter_deps = json.dumps(adapter_package.get("dependencies", {}), sort_keys=True)
    add(results, "core SDK does not depend on runtime package", PACKAGE_NAME not in sdk_deps, sdk_deps)
    add(results, "adapter does not hard-depend on runtime package", PACKAGE_NAME not in adapter_deps, adapter_deps)

    add(
        results,
        "core SDK runtime remains ROS2 For Unity free",
        not core_runtime_has_forbidden_tokens(),
        "core runtime scan",
    )


def core_runtime_has_forbidden_tokens() -> bool:
    """Return True when the core SDK Runtime contains R2FU-only tokens."""
    tokens = ("ROS2UnityComponent", "ros2forunity.runtime")
    for path in iter_files(CORE_PACKAGE / "Runtime"):
        text = path.read_text(encoding="utf-8", errors="ignore")
        if any(token in text for token in tokens):
            return True
    return False


def run_checks(release_gate: bool = False, skip_dll_hash: bool = False) -> list[CheckResult]:
    """Run all runtime package checks."""
    results: list[CheckResult] = []
    check_package_metadata(results)
    check_required_files(results)
    manifest = load_json(MANIFEST, results, "runtime manifest parses")
    check_runtime_manifest(results, manifest)
    check_inventory(results, manifest, release_gate=release_gate, skip_dll_hash=skip_dll_hash)
    check_runtime_files(results)
    check_package_path_patch(results)
    check_runtime_asmdef(results)
    check_runtime_source_patches(results)
    check_generator_alignment(results)
    check_public_docs(results, manifest)
    check_package_boundaries(results)
    return results


def print_results(results: list[CheckResult]) -> None:
    """Print validation results in a compact PASS/FAIL format."""
    for result in results:
        status = "PASS" if result.ok else "FAIL"
        detail = f": {result.detail}" if result.detail else ""
        print(f"[{status}] {result.name}{detail}")


def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    """Parse validator command-line arguments."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--release-gate",
        action="store_true",
        help="Require redistributionStatus=published before release publication.",
    )
    parser.add_argument(
        "--fast",
        action="store_true",
        help="Skip per-DLL SHA-256 verification for faster routine validation; ignored by --release-gate.",
    )
    parser.add_argument(
        "--skip-dll-hash",
        action="store_true",
        help="Alias for --fast.",
    )
    return parser.parse_args(argv)


def main(argv: list[str] | None = None) -> int:
    """Run validation and return a process exit code."""
    args = parse_args(argv)
    skip_dll_hash = (args.fast or args.skip_dll_hash) and not args.release_gate
    results = run_checks(release_gate=args.release_gate, skip_dll_hash=skip_dll_hash)
    print_results(results)
    failures = [result for result in results if not result.ok]
    if failures:
        print(f"\n{len(failures)} check(s) failed.", file=sys.stderr)
        return EXIT_FAILURE
    print(f"\nRuntime package validation passed: {len(results)} checks.")
    return EXIT_SUCCESS


if __name__ == "__main__":
    raise SystemExit(main())
