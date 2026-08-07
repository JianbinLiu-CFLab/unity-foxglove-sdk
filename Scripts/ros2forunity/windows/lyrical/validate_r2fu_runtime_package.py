#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Validate the ROS2 For Unity Lyrical Win64 runtime Unity package prototype.
# Usage: python Scripts/ros2forunity/windows/lyrical/validate_r2fu_runtime_package.py
# Inputs: Packages/dev.unity2foxglove.ros2forunity.runtime.lyrical.win64 package directory.
# Outputs: Prints runtime package checks and exits nonzero on failure.

"""Validate the ROS2 For Unity Lyrical Win64 runtime package prototype."""

from __future__ import annotations

import json
import argparse
import hashlib
import re
import struct
import sys
import xml.etree.ElementTree as ElementTree
from dataclasses import dataclass
from pathlib import Path, PurePosixPath
from typing import Iterable


REPO_ROOT_PARENT_DEPTH = 4
EXIT_SUCCESS = 0
EXIT_FAILURE = 1

ROOT = Path(__file__).resolve().parents[REPO_ROOT_PARENT_DEPTH]
SCRIPT_DIR = Path(__file__).resolve().parent
if str(SCRIPT_DIR) not in sys.path:
    sys.path.insert(0, str(SCRIPT_DIR))

from lyrical_artifact_config import ARTIFACT_NAME, EXPECTED_ARTIFACT_SHA256

if len(EXPECTED_ARTIFACT_SHA256) != 64:
    raise ValueError("EXPECTED_ARTIFACT_SHA256 must be a 64-character SHA-256 hex digest.")

PACKAGE_NAME = "dev.unity2foxglove.ros2forunity.runtime.lyrical.win64"
PACKAGE = ROOT / "Packages" / PACKAGE_NAME
ADAPTER_PACKAGE = ROOT / "Packages" / "dev.unity2foxglove.ros2forunity"
CORE_PACKAGE = ROOT / "Packages" / "dev.unity2foxglove.sdk"
RUNTIME_ROOT = PACKAGE / "Runtime" / "Ros2ForUnity"
PLUGIN_ROOT = RUNTIME_ROOT / "Plugins" / "Windows" / "x86_64"
MANIFEST = PACKAGE / "RuntimeSupport" / "runtime-manifest.json"
INVENTORY = PACKAGE / "RuntimeSupport" / "r2fu-lyrical-win64-runtime-inventory.json"

DEFAULT_RMW_IMPLEMENTATION = "rmw_fastrtps_cpp"
ZENOH_RMW_IMPLEMENTATION = "rmw_zenoh_cpp"
SUPPORTED_RMW_IMPLEMENTATIONS = (DEFAULT_RMW_IMPLEMENTATION, ZENOH_RMW_IMPLEMENTATION)

CRITICAL_RUNTIME_FILES = (
    "rcl.dll",
    "yaml.dll",
    "spdlog.dll",
    "fmt.dll",
    "fastdds-3.6.dll",
    "rosidl_buffer_backend_registry.dll",
    "rosidl_dynamic_typesupport_fastrtps.dll",
    "rmw_zenoh_cpp.dll",
    "zenohc.dll",
    "rosgraph_msgs_assembly.dll",
    "rosgraph_msgs__rosidl_typesupport_fastrtps_c.dll",
    "rosgraph_msgs__rosidl_typesupport_fastrtps_cpp.dll",
)

CRITICAL_PLUGIN_DLLS = (
    "rcl.dll",
    "yaml.dll",
    "spdlog.dll",
    "fmt.dll",
    "fastdds-3.6.dll",
    "rosidl_buffer_backend_registry.dll",
    "rosidl_dynamic_typesupport_fastrtps.dll",
    "rmw_zenoh_cpp.dll",
    "zenohc.dll",
    "rosgraph_msgs__rosidl_typesupport_fastrtps_c.dll",
    "rosgraph_msgs__rosidl_typesupport_fastrtps_cpp.dll",
)

ZENOH_CONFIG_FILES = (
    RUNTIME_ROOT / "Plugins" / "Windows" / "x86_64" / "share" / "rmw_zenoh_cpp" / "config" / "DEFAULT_RMW_ZENOH_SESSION_CONFIG.json5",
    RUNTIME_ROOT / "Plugins" / "Windows" / "x86_64" / "share" / "rmw_zenoh_cpp" / "config" / "DEFAULT_RMW_ZENOH_ROUTER_CONFIG.json5",
    RUNTIME_ROOT / "StreamingAssets" / "Ros2ForUnity" / "share" / "rmw_zenoh_cpp" / "config" / "DEFAULT_RMW_ZENOH_SESSION_CONFIG.json5",
    RUNTIME_ROOT / "StreamingAssets" / "Ros2ForUnity" / "share" / "rmw_zenoh_cpp" / "config" / "DEFAULT_RMW_ZENOH_ROUTER_CONFIG.json5",
)

ZENOH_CONFIG_MIRRORS = (
    (
        RUNTIME_ROOT / "Plugins" / "Windows" / "x86_64" / "share" / "rmw_zenoh_cpp" / "config" / "DEFAULT_RMW_ZENOH_SESSION_CONFIG.json5",
        RUNTIME_ROOT / "StreamingAssets" / "Ros2ForUnity" / "share" / "rmw_zenoh_cpp" / "config" / "DEFAULT_RMW_ZENOH_SESSION_CONFIG.json5",
    ),
    (
        RUNTIME_ROOT / "Plugins" / "Windows" / "x86_64" / "share" / "rmw_zenoh_cpp" / "config" / "DEFAULT_RMW_ZENOH_ROUTER_CONFIG.json5",
        RUNTIME_ROOT / "StreamingAssets" / "Ros2ForUnity" / "share" / "rmw_zenoh_cpp" / "config" / "DEFAULT_RMW_ZENOH_ROUTER_CONFIG.json5",
    ),
)

RMW_DEPENDENCY_CLOSURE_SEEDS = (
    "rmw_fastrtps_cpp.dll",
    "rmw_zenoh_cpp.dll",
)

WINDOWS_SYSTEM_DLL_NAMES = {
    "advapi32.dll",
    "bcrypt.dll",
    "bcryptprimitives.dll",
    "cfgmgr32.dll",
    "crypt32.dll",
    "dnsapi.dll",
    "gdi32.dll",
    "iphlpapi.dll",
    "kernel32.dll",
    "mswsock.dll",
    "ntdll.dll",
    "ole32.dll",
    "rpcrt4.dll",
    "secur32.dll",
    "setupapi.dll",
    "shell32.dll",
    "shlwapi.dll",
    "user32.dll",
    "ws2_32.dll",
}

WINDOWS_SYSTEM_DLL_PREFIXES = (
    "api-ms-win-",
    "ext-ms-win-",
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
    "110",
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


def guarded_unity_editor_using(text: str) -> bool:
    """Return whether using UnityEditor appears only inside its UNITY_EDITOR guard."""
    pattern = r"#if\s+UNITY_EDITOR\s+using UnityEditor;\s+#endif"
    return re.search(pattern, text) is not None and "using UnityEditor;" not in re.sub(pattern, "", text)


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


def is_windows_system_dll(name: str) -> bool:
    """Return whether a DLL import is expected to be provided by Windows."""
    lower = name.lower()
    return lower in WINDOWS_SYSTEM_DLL_NAMES or lower.startswith(WINDOWS_SYSTEM_DLL_PREFIXES)


def rva_to_file_offset(sections: list[tuple[int, int, int, int]], rva: int) -> int | None:
    """Translate a PE RVA to a file offset using section headers."""
    for virtual_address, virtual_size, raw_pointer, raw_size in sections:
        size = max(virtual_size, raw_size)
        if virtual_address <= rva < virtual_address + size:
            return raw_pointer + (rva - virtual_address)
    return None


def read_c_string(data: bytes, offset: int) -> str | None:
    """Read a null-terminated ASCII string from a PE byte buffer."""
    if offset < 0 or offset >= len(data):
        return None
    end = data.find(b"\0", offset)
    if end < 0:
        return None
    return data[offset:end].decode("ascii", errors="replace")


def read_pe_imports(path: Path) -> list[str]:
    """Read direct DLL imports from a PE image without external tools."""
    data = path.read_bytes()
    if len(data) < 0x40 or data[:2] != b"MZ":
        return []

    pe_offset = struct.unpack_from("<I", data, 0x3C)[0]
    if pe_offset + 24 > len(data) or data[pe_offset:pe_offset + 4] != b"PE\0\0":
        return []

    coff_offset = pe_offset + 4
    _machine, section_count, _timestamp, _symbols, _symbol_count, optional_size, _flags = struct.unpack_from(
        "<HHIIIHH",
        data,
        coff_offset,
    )
    optional_offset = coff_offset + 20
    magic = struct.unpack_from("<H", data, optional_offset)[0]
    data_directory_offset = optional_offset + (112 if magic == 0x20B else 96)
    import_directory_rva, _import_directory_size = struct.unpack_from("<II", data, data_directory_offset + 8)
    if import_directory_rva == 0:
        return []

    section_offset = optional_offset + optional_size
    sections: list[tuple[int, int, int, int]] = []
    for index in range(section_count):
        header_offset = section_offset + index * 40
        virtual_size, virtual_address, size_of_raw_data, pointer_to_raw_data = struct.unpack_from("<IIII", data, header_offset + 8)
        sections.append((virtual_address, virtual_size, pointer_to_raw_data, size_of_raw_data))

    import_offset = rva_to_file_offset(sections, import_directory_rva)
    if import_offset is None:
        return []

    imports: list[str] = []
    descriptor_offset = import_offset
    while descriptor_offset + 20 <= len(data):
        original_first_thunk, timestamp, forwarder_chain, name_rva, first_thunk = struct.unpack_from(
            "<IIIII",
            data,
            descriptor_offset,
        )
        if original_first_thunk == timestamp == forwarder_chain == name_rva == first_thunk == 0:
            break

        name_offset = rva_to_file_offset(sections, name_rva)
        if name_offset is not None:
            imported = read_c_string(data, name_offset)
            if imported:
                imports.append(imported)
        descriptor_offset += 20

    return imports


def build_package_dll_index(root: Path) -> dict[str, Path]:
    """Index packaged DLLs using the case-insensitive names Windows resolves."""
    index: dict[str, Path] = {}
    ambiguous: set[str] = set()
    if not root.exists():
        return index

    for path in root.iterdir():
        if not path.is_file() or path.suffix.casefold() != ".dll":
            continue
        key = path.name.casefold()
        previous = index.get(key)
        if previous is not None and previous != path:
            ambiguous.add(key)
            continue
        index[key] = path

    for key in ambiguous:
        index.pop(key, None)
    return index


def missing_package_dll_imports(seed: Path) -> dict[str, list[str]]:
    """Return non-system DLL imports missing from PLUGIN_ROOT, grouped by importer."""
    pending = [seed]
    visited: set[str] = set()
    missing: dict[str, list[str]] = {}
    package_dlls = build_package_dll_index(PLUGIN_ROOT)

    while pending:
        current = pending.pop()
        key = current.name.casefold()
        if key in visited:
            continue
        visited.add(key)
        if not current.exists():
            missing.setdefault(current.name, []).append("<missing seed>")
            continue

        for imported in read_pe_imports(current):
            imported_key = imported.casefold()
            if is_windows_system_dll(imported_key):
                continue
            imported_path = package_dlls.get(imported_key)
            if imported_path is not None:
                if imported_key not in visited:
                    pending.append(imported_path)
            else:
                missing.setdefault(current.name, []).append(imported)

    return missing


def check_package_metadata(results: list[CheckResult]) -> None:
    """Validate Unity package metadata."""
    add(results, "runtime package folder exists", PACKAGE.is_dir(), rel(PACKAGE))
    data = load_json(PACKAGE / "package.json", results, "package.json parses")
    if not data:
        return

    expected = {
        "name": PACKAGE_NAME,
        "version": "0.1.0-preview.1",
        "displayName": "Unity2Foxglove ROS2 For Unity Runtime - Lyrical Win64",
        "license": "Apache-2.0",
        "unity": "6000.0",
        "description": "Optional Lyrical Windows x64 runtime package for Unity2Foxglove ROS2 For Unity integration.",
    }
    for key, value in expected.items():
        add(results, f"package {key}", data.get(key) == value, f"expected {value!r}, got {data.get(key)!r}")

    add(results, "package declares no external dependencies", data.get("dependencies") == {}, f"dependencies={data.get('dependencies')!r}")
    keywords = data.get("keywords", [])
    add(
        results,
        "package keywords",
        isinstance(keywords, list) and {"ros2", "ros2-for-unity", "lyrical", "win64"}.issubset(set(keywords)),
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
        RUNTIME_ROOT / "Scripts" / "Unity2Foxglove.Ros2ForUnity.Runtime.LyricalWin64.asmdef",
        RUNTIME_ROOT / "Plugins" / "ros2cs_core.dll",
        RUNTIME_ROOT / "Plugins" / "ros2cs_common.dll",
        RUNTIME_ROOT / "Plugins" / "std_msgs_assembly.dll",
    ]
    for path in required:
        add(results, f"required file: {path.name}", path.exists(), rel(path))


def check_ros2cs_metadata_descriptions(results: list[CheckResult]) -> None:
    """Validate the ros2cs metadata's semantic runtime-distro field."""
    for path in (
        RUNTIME_ROOT / "metadata_ros2cs.xml",
        RUNTIME_ROOT / "Plugins" / "metadata_ros2cs.xml",
        PLUGIN_ROOT / "metadata_ros2cs.xml",
    ):
        text = read_optional_text(path)
        try:
            root = ElementTree.fromstring(text)
            distro = (root.findtext("ros2") or "").strip() if root.tag == "ros2cs" else ""
        except ElementTree.ParseError:
            distro = ""
        add(
            results,
            f"{rel(path)} declares lyrical ros2cs distro",
            distro == "lyrical",
            rel(path),
        )


def check_ros2cs_metadata_portability(results: list[CheckResult]) -> None:
    """Require package-relative roots in both shipped plugin inventories."""
    for path in (
        RUNTIME_ROOT / "Plugins" / "metadata_ros2cs.xml",
        PLUGIN_ROOT / "metadata_ros2cs.xml",
    ):
        text = read_optional_text(path)
        try:
            root = ElementTree.fromstring(text)
            plugins = root.find("plugins") if root.tag == "ros2cs" else None
            plugin_root = plugins.get("root") if plugins is not None else None
        except ElementTree.ParseError:
            plugin_root = None
        add(
            results,
            f"{rel(path)} uses portable plugin root",
            plugin_root == ".",
            f"root={plugin_root!r}",
        )


def check_runtime_manifest(results: list[CheckResult]) -> None:
    """Validate the runtime support manifest."""
    data = load_json(MANIFEST, results, "runtime manifest parses")
    if not data:
        return

    expected = {
        "schemaVersion": 1,
        "runtimeId": "r2fu-lyrical-win64",
        "packageName": PACKAGE_NAME,
        "packageVersion": "0.1.0-preview.1",
        "rosDistro": "lyrical",
        "platform": "win64",
        "unityPlatform": "Windows",
        "architecture": "x86_64",
        "buildType": "standalone",
        "rmwImplementation": DEFAULT_RMW_IMPLEMENTATION,
        "defaultRmwImplementation": DEFAULT_RMW_IMPLEMENTATION,
        "artifactName": ARTIFACT_NAME,
        "inventoryFile": "RuntimeSupport/r2fu-lyrical-win64-runtime-inventory.json",
        "runtimeRoot": "Runtime/Ros2ForUnity",
        "pluginPath": "Runtime/Ros2ForUnity/Plugins/Windows/x86_64",
        "supportLevel": "Supported",
        "distributionLevel": "Prototype",
        "activeRuntimePolicy": "one_runtime_package_per_project",
        "freshProjectAcceptance": "deferred_to_install_acceptance",
    }
    for key, value in expected.items():
        add(results, f"runtime manifest {key}", data.get(key) == value, f"expected {value!r}, got {data.get(key)!r}")

    supported_rmw = data.get("supportedRmwImplementations", [])
    add(
        results,
        "runtime manifest supported RMW implementations",
        isinstance(supported_rmw, list) and set(SUPPORTED_RMW_IMPLEMENTATIONS).issubset(set(supported_rmw)),
        f"supportedRmwImplementations={supported_rmw!r}",
    )
    modes = data.get("communicationModes", [])
    mode_rmw = {
        item.get("rmwImplementation")
        for item in modes
        if isinstance(item, dict)
    }
    add(
        results,
        "runtime manifest communication modes include FastDDS and Zenoh",
        isinstance(modes, list)
        and set(SUPPORTED_RMW_IMPLEMENTATIONS).issubset(mode_rmw)
        and any(isinstance(item, dict) and item.get("id") == "fastdds" and item.get("default") is True for item in modes),
        f"communicationModes={modes!r}",
    )

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
        "Lyrical" in source_basis and "Phase" not in source_basis and "phase" not in source_basis,
        source_basis,
    )

    critical = data.get("criticalRuntimeFiles", [])
    add(
        results,
        "runtime manifest critical runtime files",
        isinstance(critical, list) and set(CRITICAL_RUNTIME_FILES).issubset(set(critical)),
        f"criticalRuntimeFiles={critical!r}",
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


def inventory_category_counts_match(categories: object, files: object) -> bool:
    """Return whether declared inventory categories exactly match file rows."""
    if not isinstance(categories, dict) or not isinstance(files, list):
        return False

    declared: dict[str, int] = {}
    for category, count in categories.items():
        if (
            not isinstance(category, str)
            or not category
            or not isinstance(count, int)
            or isinstance(count, bool)
            or count < 0
        ):
            return False
        declared[category] = count

    actual: dict[str, int] = {}
    for item in files:
        if not isinstance(item, dict):
            return False
        category = item.get("category")
        if not isinstance(category, str) or not category:
            return False
        actual[category] = actual.get(category, 0) + 1
    return actual == declared


def check_inventory(results: list[CheckResult], release_gate: bool = False, skip_dll_hash: bool = False) -> None:
    """Validate the copied runtime inventory."""
    data = load_json(INVENTORY, results, "runtime inventory parses")
    if not data:
        return
    manifest = load_json(MANIFEST, results, "runtime manifest parses for inventory cross-check")

    expected = {
        "schemaVersion": 1,
        "runtimeId": "r2fu-lyrical-win64",
        "artifactName": ARTIFACT_NAME,
        "rosDistro": "lyrical",
        "defaultRmwImplementation": DEFAULT_RMW_IMPLEMENTATION,
        "platform": "win64",
        "buildType": "standalone",
    }
    for key, value in expected.items():
        add(results, f"runtime inventory {key}", data.get(key) == value, f"expected {value!r}, got {data.get(key)!r}")

    supported_rmw = data.get("supportedRmwImplementations", [])
    add(
        results,
        "runtime inventory supported RMW implementations",
        isinstance(supported_rmw, list) and set(SUPPORTED_RMW_IMPLEMENTATIONS).issubset(set(supported_rmw)),
        f"supportedRmwImplementations={supported_rmw!r}",
    )

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
    native_library_count = int(categories.get("native_libraries", 0)) if isinstance(categories, dict) else 0
    add(
        results,
        "runtime inventory native library count",
        native_library_count >= 700,
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
        set(CRITICAL_RUNTIME_FILES).issubset(present),
        f"present={sorted(present)!r}",
    )

    files = data.get("files", [])
    add(
        results,
        "runtime inventory category counts match file entries",
        inventory_category_counts_match(categories, files),
        f"categoryCounts={categories!r}",
    )
    malformed: list[str] = []
    missing: list[str] = []
    mismatched: list[str] = []
    config_mismatched: list[str] = []
    checked_zenoh_configs = 0
    checked_dlls = 0
    should_hash_dlls = release_gate or not skip_dll_hash
    if isinstance(files, list):
        for item in files:
            if not isinstance(item, dict):
                malformed.append(repr(item))
                continue
            path_text = str(item.get("path", ""))
            if "DEFAULT_RMW_ZENOH" in path_text and path_text.endswith(".json5"):
                expected_hash = str(item.get("sha256", "")).lower()
                parts = PurePosixPath(path_text).parts
                package_path = (
                    RUNTIME_ROOT.joinpath(*parts)
                    if parts and parts[0] == "StreamingAssets"
                    else RUNTIME_ROOT.joinpath(*parts[1:])
                    if len(parts) >= 2 and parts[0] == "Ros2ForUnity"
                    else None
                )
                checked_zenoh_configs += 1
                if package_path is None or not package_path.is_file():
                    config_mismatched.append(path_text + " (missing)")
                elif expected_hash and file_sha256(package_path) != expected_hash:
                    config_mismatched.append(path_text)

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
            if should_hash_dlls and expected_hash and file_sha256(package_path) != expected_hash:
                mismatched.append(path_text)

    add(
        results,
        "runtime inventory DLL files exist on disk",
        isinstance(files, list) and checked_dlls >= native_library_count >= 700 and not malformed and not missing,
        f"checked_dlls={checked_dlls} malformed={malformed[:8]!r} missing={missing[:8]!r}",
    )
    add(
        results,
        "runtime inventory DLL hashes match disk",
        isinstance(files, list) and checked_dlls >= native_library_count >= 700 and (not should_hash_dlls or not mismatched),
        (
            "skipped by fast validation; use --release-gate for full DLL hash verification"
            if not should_hash_dlls
            else f"checked_dlls={checked_dlls} mismatched={mismatched[:8]!r}"
        ),
    )
    add(
        results,
        "runtime inventory Zenoh config hashes match disk",
        checked_zenoh_configs >= 4 and not config_mismatched,
        f"checked_zenoh_configs={checked_zenoh_configs} mismatched={config_mismatched[:8]!r}",
    )


def check_runtime_files(results: list[CheckResult]) -> None:
    """Validate critical runtime files and package layout."""
    for dll in CRITICAL_PLUGIN_DLLS:
        path = PLUGIN_ROOT / dll
        add(results, f"critical DLL present: {dll}", path.exists(), rel(path))
    add(
        results,
        "critical managed assembly present: rosgraph_msgs_assembly.dll",
        (RUNTIME_ROOT / "Plugins" / "rosgraph_msgs_assembly.dll").exists(),
        rel(RUNTIME_ROOT / "Plugins" / "rosgraph_msgs_assembly.dll"),
    )
    for path in ZENOH_CONFIG_FILES:
        add(results, f"Zenoh config present: {path.name}", path.exists(), rel(path))
    for plugin_config, streaming_assets_config in ZENOH_CONFIG_MIRRORS:
        add(
            results,
            f"Zenoh config mirror matches StreamingAssets: {plugin_config.name}",
            plugin_config.exists()
            and streaming_assets_config.exists()
            and plugin_config.read_bytes() == streaming_assets_config.read_bytes(),
            f"{rel(plugin_config)} <-> {rel(streaming_assets_config)}",
        )

    for path in [item for item in ZENOH_CONFIG_FILES if item.name == "DEFAULT_RMW_ZENOH_SESSION_CONFIG.json5"]:
        text = path.read_text(encoding="utf-8") if path.exists() else ""
        add(
            results,
            f"Zenoh session listen failure is non-fatal: {path.parent.parent.name}/{path.name}",
            "exit_on_failure: false" in text,
            rel(path),
        )
        add(
            results,
            f"Zenoh session RX defragmentation buffer is bounded: {path.parent.parent.name}/{path.name}",
            "max_message_size: 134217728" in text and "max_message_size: 1073741824" not in text,
            rel(path),
        )
        adminspace_index = text.find("adminspace:")
        adminspace = text[adminspace_index:] if adminspace_index >= 0 else ""
        add(
            results,
            f"Zenoh session adminspace disabled by default: {path.parent.parent.name}/{path.name}",
            "enabled: false" in adminspace and "read: false" in adminspace,
            rel(path),
        )

    for path in [item for item in ZENOH_CONFIG_FILES if item.name == "DEFAULT_RMW_ZENOH_ROUTER_CONFIG.json5"]:
        text = path.read_text(encoding="utf-8") if path.exists() else ""
        add(
            results,
            f"Zenoh router open-listen profile is documented: {path.parent.parent.name}/{path.name}",
            "tcp/[::]:7447" in text
            and "without authentication or ACLs" in text
            and "localhost-only or ACL-protected deployment profile" in text,
            rel(path),
        )
        add(
            results,
            f"Zenoh router high connection limits are documented: {path.parent.parent.name}/{path.name}",
            "accept_pending: 10000" in text
            and "max_sessions: 10000" in text
            and "high development default is unsuitable" in text,
            rel(path),
        )

    dlls = list(PLUGIN_ROOT.glob("*.dll")) if PLUGIN_ROOT.exists() else []
    add(results, "Windows x86_64 DLL payload", len(dlls) >= 700, f"dll_count={len(dlls)}")

    for seed in RMW_DEPENDENCY_CLOSURE_SEEDS:
        seed_path = PLUGIN_ROOT / seed
        missing = missing_package_dll_imports(seed_path)
        detail = "; ".join(
            f"{parent} -> {', '.join(children)}"
            for parent, children in sorted(missing.items())
        )
        add(
            results,
            f"native DLL dependency closure: {seed}",
            not missing,
            detail or rel(seed_path),
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


def check_managed_deps_consistency(results: list[CheckResult]) -> None:
    """Validate known managed .deps.json dependency corrections."""
    managed_plugin_root = RUNTIME_ROOT / "Plugins"
    for name in ("stereo_msgs_assembly", "visualization_msgs_assembly"):
        path = managed_plugin_root / f"{name}.deps.json"
        data = load_json(path, results, f"{name}.deps.json parses")
        target = data.get("targets", {}).get(".NETStandard,Version=v2.0/", {})
        entry = target.get(f"{name}/1.0.0", {})
        dependencies = entry.get("dependencies", {})
        libraries = data.get("libraries", {})
        add(
            results,
            f"{name}.deps.json does not declare spurious service_msgs dependency",
            "service_msgs_assembly" not in dependencies
            and "service_msgs_assembly/0.0.0.0" not in target
            and "service_msgs_assembly/0.0.0.0" not in libraries,
            rel(path),
        )


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
        guarded_unity_editor_using(text),
        "ROS2ForUnity.cs",
    )
    add(
        results,
        "PackageManager lookup guarded",
        re.search(
            r"#if\s+UNITY_EDITOR[\s\S]{0,1200}UnityEditor\.PackageManager\.PackageInfo\.FindForAssetPath[\s\S]{0,1200}#endif",
            text,
        ) is not None,
        "ROS2ForUnity.cs",
    )


def check_runtime_asmdef(results: list[CheckResult]) -> None:
    """Validate the runtime assembly definition is safe for Editor and Player."""
    path = RUNTIME_ROOT / "Scripts" / "Unity2Foxglove.Ros2ForUnity.Runtime.LyricalWin64.asmdef"
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
        "private volatile bool quitting",
        "OnDestroy()",
        "OnApplicationQuit()",
        "node.Dispose()",
        "StopExecutor()",
        "private int shutdownInProgress = 0",
        "Interlocked.CompareExchange(ref shutdownInProgress, 1, 0)",
        "Volatile.Write(ref shutdownInProgress, 0)",
        "MarkRuntimeShutdownPendingExecutor()",
    ):
        add(results, f"ROS2UnityComponent lifecycle token: {token}", token in component, token)
    add(
        results,
        "ROS2UnityComponent uses snapshot spin path",
        "nodesSnapshot" in component
        and "actionsSnapshot" in component
        and "collectionVersion" in component
        and "Ros2cs.SpinOnce(nodesSnapshot, spinTimeout)" in component,
        "ROS2UnityComponent.cs",
    )
    add(
        results,
        "ROS2UnityComponent disposes nodes before runtime release",
        "DisposeNodes()" in component and "instance.DestroyROS2ForUnity()" in component,
        "ROS2UnityComponent.cs",
    )
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
        "IDisposable",
        "private volatile bool quitting",
        "public void Dispose()",
        "StopExecutor()",
    ):
        add(results, f"ROS2UnityCore lifecycle token: {token}", token in core, token)
    add(
        results,
        "ROS2UnityCore uses snapshot spin path",
        "nodesSnapshot" in core
        and "actionsSnapshot" in core
        and "collectionVersion" in core
        and "Ros2cs.SpinOnce(nodesSnapshot, spinTimeout)" in core,
        "ROS2UnityCore.cs",
    )
    add(
        results,
        "ROS2UnityCore disposes nodes before runtime release",
        "DisposeNodes()" in core and "instance.DestroyROS2ForUnity()" in core,
        "ROS2UnityCore.cs",
    )
    add(
        results,
        "ROS2UnityCore executor thread",
        "private Thread spinThread" in core or "private Thread executorThread" in core,
        "ROS2UnityCore.cs",
    )
    add(results, "ROS2UnityCore bounded join", core_join, "ROS2UnityCore.cs")

    runtime = read_optional_text(scripts / "ROS2ForUnity.cs")
    old_lifecycle = all(token in runtime for token in ("ownerCount", "ownsLifecycle", "lifecycleGate", "UnregisterCallbacks()", "editorCallbacksRegistered"))
    current_lifecycle = all(token in runtime for token in ("referenceCount", "ownsReference", "initMutex", "ShutdownShared()", "editorHandlersRegistered"))
    add(results, "ROS2ForUnity deterministic lifecycle", old_lifecycle or current_lifecycle, "ROS2ForUnity.cs")
    add(results, "ROS2ForUnity avoids finalizer shutdown", "~ROS2ForUnity" not in runtime, "ROS2ForUnity.cs")
    add(
        results,
        "ROS2ForUnity uses non-obsolete ros2cs logger callback API",
        "Ros2csLogger.SetCallback" in runtime and "Ros2csLogger.setCallback" not in runtime,
        "ROS2ForUnity.cs",
    )
    add(
        results,
        "ROS2ForUnity enforces supported RMW implementations",
        "defaultRmwImplementation" in runtime
        and "zenohRmwImplementation" in runtime
        and "supportedRmwImplementationsDescription" in runtime
        and "ValidateRmwImplementation" in runtime
        and "IsSupportedRmwImplementation" in runtime
        and DEFAULT_RMW_IMPLEMENTATION in runtime
        and ZENOH_RMW_IMPLEMENTATION in runtime,
        "ROS2ForUnity.cs",
    )
    add(
        results,
        "ROS2ForUnity standalone isolates sourced ROS2 environment",
        "standalone runtime must not inherit a sourced ROS2 workspace" in runtime
        and "standalone runtime owns its RMW selection while allowing Lyrical Zenoh" in runtime
        and "selectedRmwImplementation" in runtime
        and "standalone runtime owns ROS_DISTRO" in runtime
        and "WarnIfStandaloneRosDistroOverride" in runtime
        and "sourcedRosDistroBeforeStandalonePatch" in runtime
        and "CheckIntegrity(standaloneBuild ? null : sourcedRosDistroBeforeStandalonePatch)" in runtime
        and "packagedRos2Version = GetMetadataValue" in runtime
        and "ROS2 version in standalone process environment does not match this runtime package" not in runtime,
        "ROS2ForUnity.cs",
    )
    add(
        results,
        "ROS2ForUnity Windows CRT environment import is Windows-symbol guarded",
        "#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN" in runtime
        and "PlatformNotSupportedException(\"Windows CRT environment updates require a Windows Unity build target.\")" in runtime,
        "ROS2ForUnity.cs",
    )
    constructor = runtime[runtime.find("internal ROS2ForUnity()") :]
    windows_block = constructor[constructor.find("if (GetOS() == Platform.Windows)") : constructor.find("} else {")]
    add(
        results,
        "ROS2ForUnity standalone environment setup is not repeated in the Windows PATH block",
        "SetStandaloneRosDistro(currentRos2Version)" not in windows_block
        and "SetStandalonePrefixPath();" not in windows_block
        and "SetStandaloneRmwImplementation();" not in windows_block
        and "SetStandaloneRcutilsConsoleMode();" not in windows_block,
        "ROS2ForUnity.cs",
    )
    add(
        results,
        "ROS2UnityComponent prevents restart during shared ROS shutdown",
        "runtimeShutdownRequested" in component
        and "MarkRuntimeShutdown()" in component
        and "component.MarkRuntimeShutdown();" in component
        and "throw new ObjectDisposedException(nameof(ROS2UnityComponent))" in component
        and "ros2forUnity == null" in component,
        "ROS2UnityComponent.cs",
    )
    add(
        results,
        "ROS2UnityComponent prewarms Unity path Lazy values on the main thread",
        "private void Awake()" in component
        and "ROS2ForUnity.PrewarmUnityPaths();" in component
        and "            runtimeShutdownRequested = false;" not in component,
        "ROS2UnityComponent.cs",
    )

    dotnet_time = read_optional_text(scripts / "Time" / "DotnetTimeSource.cs")
    add(
        results,
        "DotnetTimeSource converts Stopwatch duration to seconds",
        ("Stopwatch.Frequency" in dotnet_time and "ElapsedTicks" in dotnet_time)
        or "stopwatch.Elapsed.TotalSeconds" in dotnet_time,
        "DotnetTimeSource.cs",
    )
    add(
        results,
        "DotnetTimeSource clamps backward wall-clock corrections",
        "lastEmittedSeconds" in dotnet_time
        and "wall-clock corrections cannot move time backward" in dotnet_time,
        "DotnetTimeSource.cs",
    )
    itime_source = read_optional_text(scripts / "Time" / "ITimeSource.cs")
    add(
        results,
        "ITimeSource summary uses stable wording",
        "Interface for acquiring time" in itime_source,
        "ITimeSource.cs",
    )
    unity_time = read_optional_text(scripts / "Time" / "UnityTimeSource.cs")
    add(
        results,
        "UnityTimeSource reports off-main-thread construction clearly",
        "must be constructed on the Unity main thread" in unity_time,
        "UnityTimeSource.cs",
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
    generator_path = ROOT / "Scripts" / "ros2forunity" / "windows" / "lyrical" / "build_r2fu_runtime_package.py"
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
        "validate_ros2cs_metadata_descriptions",
        "make_writable",
        "windows_long_path",
        "PackageInfo.FindForAssetPath",
        "UNITY_EDITOR",
    )
    for token in required:
        add(results, f"runtime package generator token: {token}", token in generator, token)


def check_public_docs(results: list[CheckResult]) -> None:
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
        "runtime.lyrical.win64" in readme and "adapter" in readme and "combined Unity2Foxglove workflow" in readme,
        "README.md",
    )
    add(
        results,
        "README documents one-runtime policy",
        "Install only one" in readme and re.search(r"dev\.unity2foxglove\.ros2forunity\.runtime\.\S+", readme) is not None,
        "README.md",
    )
    add(
        results,
        "README documents artifact SHA-256",
        str(load_json(MANIFEST, results, "runtime manifest parses for docs cross-check").get("artifactSha256", "")) in readme,
        "README.md",
    )
    notices = (PACKAGE / "THIRD_PARTY_NOTICES.md").read_text(encoding="utf-8", errors="replace") if (PACKAGE / "THIRD_PARTY_NOTICES.md").exists() else ""
    add(
        results,
        "THIRD_PARTY_NOTICES documents artifact SHA-256",
        str(load_json(MANIFEST, results, "runtime manifest parses for notices cross-check").get("artifactSha256", "")) in notices,
        "THIRD_PARTY_NOTICES.md",
    )
    add(
        results,
        "README documents WSL2 NAT topology limit",
        "WSL2 NAT" in readme and "diagnostic-only" in readme and "Windows Defender Firewall" in readme,
        "README.md",
    )
    add(
        results,
        "README documents runtime package has no facade dependency",
        "intentionally declares no UPM dependency on the facade package" in readme
        and "binary/runtime payload" in readme,
        "README.md",
    )
    add(
        results,
        "README documents Zenoh router development security boundary",
        "listens on `tcp/[::]:7447`" in readme
        and "exits if port `7447` is already bound" in readme
        and "no authentication or ACLs" in readme
        and "read-only Zenoh adminspace" in readme
        and "trusted lab networks" in readme,
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

    core_runtime = CORE_PACKAGE / "Runtime"
    add(results, "core Runtime folder exists", core_runtime.is_dir(), rel(core_runtime))
    add(
        results,
        "core SDK runtime remains ROS2 For Unity free",
        core_runtime.is_dir() and not core_runtime_has_forbidden_tokens(),
        "core runtime scan",
    )


def core_runtime_has_forbidden_tokens() -> bool:
    """Return True when the core SDK Runtime contains R2FU-only tokens."""
    core_runtime = CORE_PACKAGE / "Runtime"
    if not core_runtime.is_dir():
        raise FileNotFoundError(f"Missing core Runtime folder: {core_runtime}")

    tokens = ("ROS2UnityComponent", "ros2forunity.runtime")
    for path in iter_files(core_runtime):
        text = path.read_text(encoding="utf-8", errors="ignore")
        if any(token in text for token in tokens):
            return True
    return False


def run_checks(release_gate: bool = False, skip_dll_hash: bool = False) -> list[CheckResult]:
    """Run all runtime package checks."""
    results: list[CheckResult] = []
    check_package_metadata(results)
    check_required_files(results)
    check_ros2cs_metadata_descriptions(results)
    check_ros2cs_metadata_portability(results)
    check_runtime_manifest(results)
    check_inventory(results, release_gate=release_gate, skip_dll_hash=skip_dll_hash)
    check_runtime_files(results)
    check_managed_deps_consistency(results)
    check_package_path_patch(results)
    check_runtime_asmdef(results)
    check_runtime_source_patches(results)
    check_generator_alignment(results)
    check_public_docs(results)
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
        help="Skip per-DLL SHA-256 verification during routine validation; ignored by --release-gate.",
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
