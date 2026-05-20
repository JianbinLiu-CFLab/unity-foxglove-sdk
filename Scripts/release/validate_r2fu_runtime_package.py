#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Validate the ROS2 For Unity Jazzy Win64 runtime Unity package prototype.
# Usage: python Scripts/release/validate_r2fu_runtime_package.py
# Inputs: Packages/dev.unity2foxglove.ros2forunity.runtime.jazzy.win64 package directory.
# Outputs: Prints runtime package checks and exits nonzero on failure.

"""Validate the ROS2 For Unity Jazzy Win64 runtime package prototype."""

from __future__ import annotations

import json
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Iterable


REPO_ROOT_PARENT_DEPTH = 2
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

ARTIFACT_NAME = "Ros2ForUnity_Jazzy_standalone_windows10.zip"
ARTIFACT_SHA256 = "ac06054e05282b4ebd53b31ff4a48b815ebadc7f6985a5cebcbe35e01c830936"
ARTIFACT_SIZE = 16858288
INVENTORY_FILE_COUNT = 1045

CRITICAL_DLLS = (
    "rcl.dll",
    "yaml.dll",
    "spdlog.dll",
    "fmt.dll",
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


def load_json(path: Path, results: list[CheckResult], name: str) -> dict:
    """Load JSON and record whether parsing succeeded."""
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except Exception as exc:
        add(results, name, False, f"{rel(path)}: {exc}")
        return {}
    add(results, name, True, rel(path))
    return data


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
        "description": "Optional Jazzy Windows x64 runtime package for Unity2Foxglove ROS2 For Unity integration.",
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
        RUNTIME_ROOT / "Plugins" / "ros2cs_core.dll",
        RUNTIME_ROOT / "Plugins" / "ros2cs_common.dll",
        RUNTIME_ROOT / "Plugins" / "std_msgs_assembly.dll",
    ]
    for path in required:
        add(results, f"required file: {path.name}", path.exists(), rel(path))


def check_runtime_manifest(results: list[CheckResult]) -> None:
    """Validate the runtime support manifest."""
    data = load_json(MANIFEST, results, "runtime manifest parses")
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
        "rmwImplementation": "rmw_fastrtps_cpp",
        "artifactName": ARTIFACT_NAME,
        "artifactSha256": ARTIFACT_SHA256,
        "artifactSize": ARTIFACT_SIZE,
        "inventoryFile": "RuntimeSupport/r2fu-jazzy-win64-runtime-inventory.json",
        "inventoryFileCount": INVENTORY_FILE_COUNT,
        "runtimeRoot": "Runtime/Ros2ForUnity",
        "pluginPath": "Runtime/Ros2ForUnity/Plugins/Windows/x86_64",
        "supportLevel": "Recommended",
        "distributionLevel": "Prototype",
        "activeRuntimePolicy": "one_runtime_package_per_project",
        "freshProjectAcceptance": "deferred_to_install_acceptance",
    }
    for key, value in expected.items():
        add(results, f"runtime manifest {key}", data.get(key) == value, f"expected {value!r}, got {data.get(key)!r}")

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

    patch = data.get("packagePathPatch", {})
    add(
        results,
        "runtime manifest package path patch",
        isinstance(patch, dict)
        and patch.get("modifiedFile") == "Runtime/Ros2ForUnity/Scripts/ROS2ForUnity.cs"
        and patch.get("keepsAssetFolderFallback") is True,
        f"packagePathPatch={patch!r}",
    )


def check_inventory(results: list[CheckResult]) -> None:
    """Validate the copied runtime inventory."""
    data = load_json(INVENTORY, results, "runtime inventory parses")
    if not data:
        return

    expected = {
        "schemaVersion": 1,
        "runtimeId": "r2fu-jazzy-win64",
        "artifactName": ARTIFACT_NAME,
        "artifactSize": ARTIFACT_SIZE,
        "sha256": ARTIFACT_SHA256,
        "rosDistro": "jazzy",
        "rmw": "rmw_fastrtps_cpp",
        "platform": "win64",
        "buildType": "standalone",
        "fileCount": INVENTORY_FILE_COUNT,
    }
    for key, value in expected.items():
        add(results, f"runtime inventory {key}", data.get(key) == value, f"expected {value!r}, got {data.get(key)!r}")

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


def check_runtime_files(results: list[CheckResult]) -> None:
    """Validate critical runtime files and package layout."""
    for dll in CRITICAL_DLLS:
        path = PLUGIN_ROOT / dll
        add(results, f"critical DLL present: {dll}", path.exists(), rel(path))

    dlls = list(PLUGIN_ROOT.glob("*.dll")) if PLUGIN_ROOT.exists() else []
    add(results, "Windows x86_64 DLL payload", len(dlls) >= 900, f"dll_count={len(dlls)}")
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


def check_package_path_patch(results: list[CheckResult]) -> None:
    """Validate the ROS2ForUnity.cs package path patch."""
    source = RUNTIME_ROOT / "Scripts" / "ROS2ForUnity.cs"
    text = source.read_text(encoding="utf-8", errors="replace") if source.exists() else ""
    required = [
        "Unity2Foxglove package path support",
        PACKAGE_NAME,
        'Path.Combine(',
        '"Packages"',
        '"Runtime"',
        "Directory.Exists(packagePath)",
        "return assetPath;",
    ]
    for token in required:
        add(results, f"ROS2ForUnity.cs contains {token}", token in text, token)


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
        "runtime.jazzy.win64" in readme and "adapter" in readme and "combined Unity2Foxglove workflow" in readme,
        "README.md",
    )
    add(
        results,
        "README documents one-runtime policy",
        "Install only one" in readme and "runtime.*" in readme,
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

    sdk_text = "\n".join(path.read_text(encoding="utf-8", errors="ignore") for path in iter_files(CORE_PACKAGE / "Runtime"))
    add(
        results,
        "core SDK runtime remains ROS2 For Unity free",
        "ROS2UnityComponent" not in sdk_text and "ros2forunity.runtime" not in sdk_text,
        "core runtime scan",
    )


def run_checks() -> list[CheckResult]:
    """Run all runtime package checks."""
    results: list[CheckResult] = []
    check_package_metadata(results)
    check_required_files(results)
    check_runtime_manifest(results)
    check_inventory(results)
    check_runtime_files(results)
    check_package_path_patch(results)
    check_public_docs(results)
    check_package_boundaries(results)
    return results


def print_results(results: list[CheckResult]) -> None:
    """Print validation results in a compact PASS/FAIL format."""
    for result in results:
        status = "PASS" if result.ok else "FAIL"
        detail = f": {result.detail}" if result.detail else ""
        print(f"[{status}] {result.name}{detail}")


def main() -> int:
    """Run validation and return a process exit code."""
    results = run_checks()
    print_results(results)
    failures = [result for result in results if not result.ok]
    if failures:
        print(f"\n{len(failures)} check(s) failed.", file=sys.stderr)
        return EXIT_FAILURE
    print(f"\nRuntime package validation passed: {len(results)} checks.")
    return EXIT_SUCCESS


if __name__ == "__main__":
    raise SystemExit(main())
