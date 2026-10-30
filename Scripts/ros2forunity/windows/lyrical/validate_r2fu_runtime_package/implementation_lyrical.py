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
import os
import hashlib
import re
import struct
import sys
import subprocess
import xml.etree.ElementTree as ElementTree
from dataclasses import dataclass
from pathlib import Path, PurePosixPath
from typing import Iterable


REPO_ROOT_PARENT_DEPTH = 5
EXIT_SUCCESS = 0
EXIT_FAILURE = 1

ROOT = Path(__file__).resolve().parents[REPO_ROOT_PARENT_DEPTH]
SCRIPT_DIR = Path(__file__).resolve().parents[1]
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
    if not path.exists():
        return ""
    if path.suffix.casefold() == ".cs":
        text = path.read_text(encoding="utf-8")
        marker = re.search(
            r"^\s*///\s*<remarks>Phase192 source sections:\s*(?P<names>[^<]+)</remarks>\s*$",
            text,
            re.MULTILINE,
        )
        if marker is not None:
            section_directory = path.parent / "Decomposed" / path.stem
            names = [name.strip() for name in marker.group("names").split(",") if name.strip()]
            actual = sorted(item.name for item in section_directory.glob("*.cs"))
            if sorted(names) != actual:
                raise ValueError(f"C# source section manifest mismatch in {section_directory}")
            return text + "\n" + "\n".join(
                (section_directory / name).read_text(encoding="utf-8") for name in names
            )
        return text
    return path.read_text(encoding="utf-8", errors="replace")


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
        RUNTIME_ROOT / "StreamingAssets" / "Ros2ForUnity" / "share" / "ament_index" / "resource_index" / "packages" / "rmw",
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



from .manifest_and_inventory import (
    check_runtime_manifest,
    inventory_category_counts_match,
    fresh_project_acceptance_status,
    published_runtime_is_not_prototype,
    fresh_project_acceptance_passed,
    _manifest_without_acceptance,
    acceptance_commit_is_ancestor_and_package_unchanged,
    fresh_project_acceptance_binding_matches_manifest,
    fresh_project_acceptance_record_is_valid,
    check_inventory,
)

from .runtime_checks import (
    check_runtime_files,
    check_managed_deps_consistency,
    check_package_path_patch,
    check_runtime_asmdef,
    check_runtime_source_patches,
    check_generator_alignment,
)


__all__ = [name for name in globals() if not name.startswith("__")]
