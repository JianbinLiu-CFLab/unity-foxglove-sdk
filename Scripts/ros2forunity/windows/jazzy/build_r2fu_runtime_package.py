#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Build the ROS2 For Unity Jazzy Win64 runtime Unity package from a vetted artifact.
# Usage: python Scripts/ros2forunity/windows/jazzy/build_r2fu_runtime_package.py
# Inputs: r2fu-runtime-artifacts/jazzy/windows_x86_64/Ros2ForUnity_jazzy_standalone_windows_x86_64.zip and compliance inventory.
# Outputs: Packages/dev.unity2foxglove.ros2forunity.runtime.jazzy.win64 package directory.

"""Build the ROS2 For Unity Jazzy Win64 runtime package prototype."""

from __future__ import annotations

import argparse
import hashlib
import inspect
import json
import os
import shutil
import sys
import time
import zipfile
from dataclasses import dataclass
from pathlib import Path, PurePosixPath


REPO_ROOT_PARENT_DEPTH = 4
EXIT_SUCCESS = 0
EXIT_FAILURE = 1

PACKAGE_NAME = "dev.unity2foxglove.ros2forunity.runtime.jazzy.win64"
PACKAGE_VERSION = "0.1.0-preview.1"
RUNTIME_ID = "r2fu-jazzy-win64"
ARTIFACT_NAME = "Ros2ForUnity_jazzy_standalone_windows_x86_64.zip"
EXPECTED_ARTIFACT_SHA256 = "792f3718cb3df464a898947923984e9d51aa4fcf174f33d6278c5f4811495e74"

ROOT = Path(__file__).resolve().parents[REPO_ROOT_PARENT_DEPTH]
DEFAULT_ARTIFACT = ROOT / "r2fu-runtime-artifacts" / "jazzy" / "windows_x86_64" / ARTIFACT_NAME
DEFAULT_ROS2_BIN = ROOT / "ros2-windows" / "ros2_jazzy" / "bin"
DEFAULT_INVENTORY = (
    ROOT
    / "Packages"
    / "dev.unity2foxglove.ros2forunity"
    / "Compliance"
    / "r2fu-jazzy-win64-runtime-inventory.json"
)
DEFAULT_PACKAGE = ROOT / "Packages" / PACKAGE_NAME
UPSTREAM_LICENSE = ROOT / "Packages" / "dev.unity2foxglove.ros2forunity" / "Upstream" / "LICENSE.AL2"

UNITY_PACKAGE_PATH_PATCH_MARKER = "Unity2Foxglove package path support"
LOCAL_PATCH_MARKER = "U2F-LOCAL-PATCH"
MODIFICATIONS_COPYRIGHT = "Modifications Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors."
LOCAL_PATCH_OVERLAY_FILES = {
    "Runtime/Ros2ForUnity/Scripts/ROS2UnityComponent.cs",
    "Runtime/Ros2ForUnity/Scripts/ROS2UnityCore.cs",
    "Runtime/Ros2ForUnity/Scripts/Time/ROS2ScalableTimeSource.cs",
    "Runtime/Ros2ForUnity/Scripts/Time/ROS2TimeSource.cs",
}
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
    "rosidl_dynamic_typesupport_fastrtps.dll",
)
V083_EXCLUDED_TEST_TYPESUPPORT_DLLS = (
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
LEAKY_UPSTREAM_EXAMPLES = (
    "ROS2TalkerExample.cs",
    "ROS2ListenerExample.cs",
    "ROS2ClientExample.cs",
    "ROS2ServiceExample.cs",
    "ROS2PerformanceTest.cs",
    "PostInstall.cs",
)
UPSTREAM_PATH_BLOCK = """    public static string GetRos2ForUnityPath()
    {
        char separator = Path.DirectorySeparatorChar;
        string appDataPath = Application.dataPath;
        string pluginPath = appDataPath;

        if (InEditor()) {
            pluginPath += separator + ros2ForUnityAssetFolderName;
        }
        return pluginPath; 
    }
"""

PACKAGE_PATH_BLOCK = """    public static string GetRos2ForUnityPath()
    {
        char separator = Path.DirectorySeparatorChar;
        string appDataPath = Application.dataPath;
        string pluginPath = appDataPath;

        if (InEditor()) {
            string assetPath = pluginPath + separator + ros2ForUnityAssetFolderName;
            if (Directory.Exists(assetPath)) {
                return assetPath;
            }

            // Unity2Foxglove package path support for local packages installed with
            // Package Manager's "Add package from disk..." flow.
#if UNITY_EDITOR
            UnityEditor.PackageManager.PackageInfo runtimePackage =
                UnityEditor.PackageManager.PackageInfo.FindForAssetPath(unity2FoxgloveRuntimePackageAssetPath);
            if (runtimePackage != null && !string.IsNullOrEmpty(runtimePackage.resolvedPath)) {
                string resolvedPackagePath = Path.Combine(
                    runtimePackage.resolvedPath,
                    "Runtime",
                    ros2ForUnityAssetFolderName);
                if (Directory.Exists(resolvedPackagePath)) {
                    return resolvedPackagePath;
                }
            }
#endif

            DirectoryInfo dataDirectory = Directory.GetParent(appDataPath);
            if (dataDirectory != null) {
                string packagePath = Path.Combine(
                    dataDirectory.FullName,
                    "Packages",
                    unity2FoxgloveRuntimePackageName,
                    "Runtime",
                    ros2ForUnityAssetFolderName);
                if (Directory.Exists(packagePath)) {
                    return packagePath;
                }
            }

            // Unity2Foxglove package path support: keep upstream asset-folder fallback.
            return assetPath;
        }
        return pluginPath; 
    }
"""

UPSTREAM_COMPUTE_PATH_BLOCK = """    private static string ComputeRos2ForUnityPath()
    {
        char separator = Path.DirectorySeparatorChar;
        string appDataPath = Application.dataPath;
        string path = appDataPath;

        if (InEditor()) {
            path += separator + ros2ForUnityAssetFolderName;
        }
        return path;
    }
"""

PACKAGE_COMPUTE_PATH_BLOCK = """    private static string ComputeRos2ForUnityPath()
    {
        char separator = Path.DirectorySeparatorChar;
        string appDataPath = Application.dataPath;
        string path = appDataPath;

        if (InEditor()) {
            string assetPath = path + separator + ros2ForUnityAssetFolderName;
            if (Directory.Exists(assetPath)) {
                return assetPath;
            }

            // Unity2Foxglove package path support for local packages installed with
            // Package Manager's "Add package from disk..." flow.
#if UNITY_EDITOR
            UnityEditor.PackageManager.PackageInfo runtimePackage =
                UnityEditor.PackageManager.PackageInfo.FindForAssetPath(unity2FoxgloveRuntimePackageAssetPath);
            if (runtimePackage != null && !string.IsNullOrEmpty(runtimePackage.resolvedPath)) {
                string resolvedPackagePath = Path.Combine(
                    runtimePackage.resolvedPath,
                    "Runtime",
                    ros2ForUnityAssetFolderName);
                if (Directory.Exists(resolvedPackagePath)) {
                    return resolvedPackagePath;
                }
            }
#endif

            DirectoryInfo dataDirectory = Directory.GetParent(appDataPath);
            if (dataDirectory != null) {
                string packagePath = Path.Combine(
                    dataDirectory.FullName,
                    "Packages",
                    unity2FoxgloveRuntimePackageName,
                    "Runtime",
                    ros2ForUnityAssetFolderName);
                if (Directory.Exists(packagePath)) {
                    return packagePath;
                }
            }

            // Unity2Foxglove package path support: keep upstream asset-folder fallback.
            return assetPath;
        }
        return path;
    }
"""

PACKAGE_CONSTANTS_BLOCK = """    private const string unity2FoxgloveRuntimePackageName = "dev.unity2foxglove.ros2forunity.runtime.jazzy.win64";
    private const string unity2FoxgloveRuntimePackageAssetPath =
        "Packages/dev.unity2foxglove.ros2forunity.runtime.jazzy.win64/Runtime/Ros2ForUnity";
"""


@dataclass(frozen=True)
class BuildPaths:
    """Resolved input and output paths for package generation."""

    artifact: Path
    inventory: Path
    package: Path
    ros2_bin: Path


@dataclass(frozen=True)
class RuntimeArtifact:
    """Identity of the vetted runtime artifact being packaged."""

    name: str
    sha256: str
    size: int
    inventory_file_count: int


def parse_args(argv: list[str]) -> BuildPaths:
    """Parse command-line arguments into build paths."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--zip", type=Path, default=DEFAULT_ARTIFACT, help="Runtime zip artifact to package.")
    parser.add_argument("--inventory", type=Path, default=DEFAULT_INVENTORY, help="Runtime inventory JSON.")
    parser.add_argument("--package", type=Path, default=DEFAULT_PACKAGE, help="Runtime package output directory.")
    parser.add_argument("--ros2-bin", type=Path, default=DEFAULT_ROS2_BIN, help="ROS 2 bin directory for supplemental DLLs.")
    args = parser.parse_args(argv)
    return BuildPaths(args.zip.resolve(), args.inventory.resolve(), args.package.resolve(), args.ros2_bin.resolve())


def rel(path: Path) -> str:
    """Format a path relative to the repository root when possible."""
    try:
        return path.resolve().relative_to(ROOT.resolve()).as_posix()
    except ValueError:
        return str(path)


def sha256_file(path: Path) -> str:
    """Return the SHA-256 digest for a local file."""
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def require_inputs(paths: BuildPaths) -> tuple[dict[str, object], RuntimeArtifact]:
    """Validate inputs and return the parsed runtime inventory and artifact identity."""
    if not paths.artifact.exists():
        raise FileNotFoundError(f"Missing runtime artifact: {paths.artifact}")
    if paths.artifact.name != ARTIFACT_NAME:
        raise ValueError(f"Unexpected artifact name: {paths.artifact.name}")

    artifact_hash = sha256_file(paths.artifact)
    artifact_size = paths.artifact.stat().st_size
    if artifact_hash != EXPECTED_ARTIFACT_SHA256:
        raise ValueError(f"Runtime artifact sha256 does not match pinned Jazzy handoff: {artifact_hash} != {EXPECTED_ARTIFACT_SHA256}")

    if not paths.inventory.exists():
        raise FileNotFoundError(f"Missing runtime inventory: {paths.inventory}")
    inventory = json.loads(paths.inventory.read_text(encoding="utf-8"))
    if inventory.get("runtimeId") != RUNTIME_ID:
        raise ValueError(f"Unexpected inventory runtimeId: {inventory.get('runtimeId')!r}")
    if inventory.get("sha256") != artifact_hash:
        raise ValueError("Inventory sha256 does not match the runtime artifact.")
    if inventory.get("artifactSize") not in (None, artifact_size):
        raise ValueError(f"Inventory artifactSize does not match the runtime artifact: {inventory.get('artifactSize')!r}")
    inventory_file_count = int(inventory.get("fileCount") or 0)
    if inventory_file_count <= 0:
        raise ValueError(f"Unexpected inventory fileCount: {inventory.get('fileCount')!r}")
    artifact = RuntimeArtifact(
        name=paths.artifact.name,
        sha256=artifact_hash,
        size=artifact_size,
        inventory_file_count=inventory_file_count,
    )
    return inventory, artifact


def reset_package_dir(package: Path) -> None:
    """Delete and recreate only the expected generated runtime package directory."""
    expected_parent = (ROOT / "Packages").resolve()
    package = package.resolve()
    if package.name != PACKAGE_NAME or package.parent != expected_parent:
        raise ValueError(f"Refusing to reset unexpected package path: {package}")
    if package.exists():
        last_error: Exception | None = None
        for _ in range(5):
            try:
                rmtree_with_writable_retry(package)
                break
            except OSError as exc:
                last_error = exc
                time.sleep(0.25)
        else:
            remove_tree_manually(package)
            if package.exists():
                raise last_error if last_error is not None else OSError(f"Could not remove {package}")
    package.mkdir(parents=True)


def snapshot_package_dir(package: Path) -> Path | None:
    """Copy the existing package to an ignored rollback root before regeneration."""
    if not package.exists():
        return None

    rollback_root = ROOT / "build" / "r2fu-runtime-package-rollback"
    rollback_root.mkdir(parents=True, exist_ok=True)
    snapshot = rollback_root / f"{package.name}-{os.getpid()}-{time.time_ns()}"
    shutil.copytree(windows_long_path(package), windows_long_path(snapshot), copy_function=shutil.copy2)
    return snapshot


def restore_package_dir(package: Path, snapshot: Path | None) -> None:
    """Restore or remove the generated package after a failed regeneration."""
    if package.exists():
        rmtree_with_writable_retry(package)

    if snapshot is None:
        return

    package.parent.mkdir(parents=True, exist_ok=True)
    shutil.copytree(windows_long_path(snapshot), windows_long_path(package), copy_function=shutil.copy2)


def remove_package_snapshot(snapshot: Path | None) -> None:
    """Delete a temporary package rollback snapshot."""
    if snapshot is None or not snapshot.exists():
        return
    rmtree_with_writable_retry(snapshot)


def rmtree_with_writable_retry(path: Path) -> None:
    """Remove a tree, retrying read-only paths across Python shutil APIs."""
    raw_path = windows_long_path(path)
    if "onexc" in inspect.signature(shutil.rmtree).parameters:
        shutil.rmtree(raw_path, onexc=make_writable_onexc)
    else:
        shutil.rmtree(raw_path, onerror=make_writable_onerror)


def make_writable_onerror(function, path: str, exc_info) -> None:
    """Clear a read-only bit and retry a failed removal operation."""

    os.chmod(path, os.stat(path).st_mode | 0o200)
    function(path)


def make_writable_onexc(function, path: str, exc: BaseException) -> None:
    """Clear a read-only bit and retry a failed removal operation."""

    os.chmod(path, os.stat(path).st_mode | 0o200)
    function(path)


def windows_long_path(path: Path) -> str:
    """Return a Windows extended-length path for deletion-heavy filesystem work."""

    resolved = str(path.resolve())
    if os.name != "nt" or resolved.startswith("\\\\?\\"):
        return resolved
    return "\\\\?\\" + resolved


def path_exists(path: Path) -> bool:
    """Return whether a path exists, including long Windows paths."""
    return os.path.exists(windows_long_path(path))


def remove_tree_manually(root: Path) -> None:
    """Fallback removal for sync folders where rmtree can leave late-arriving files."""

    if not root.exists():
        return
    for _ in range(5):
        for path in sorted(root.rglob("*"), key=lambda item: len(item.parts), reverse=True):
            try:
                raw_path = windows_long_path(path)
                os.chmod(raw_path, os.stat(raw_path).st_mode | 0o200)
                if path.is_dir():
                    os.rmdir(raw_path)
                else:
                    os.unlink(raw_path)
            except (FileNotFoundError, OSError):
                continue
        try:
            os.rmdir(windows_long_path(root))
            return
        except FileNotFoundError:
            return
        except OSError:
            time.sleep(0.25)


def write_text(path: Path, content: str) -> None:
    """Write UTF-8 text with a trailing newline."""
    Path(windows_long_path(path.parent)).mkdir(parents=True, exist_ok=True)
    normalized = content.rstrip() + "\n"
    if path.exists():
        existing = path.read_text(encoding="utf-8", errors="replace")
        if existing == normalized:
            return
    with open(windows_long_path(path), "w", encoding="utf-8", newline="\n") as stream:
        stream.write(normalized)


def write_json(path: Path, data: dict[str, object]) -> None:
    """Write JSON with stable formatting."""
    write_text(path, json.dumps(data, indent=2, ensure_ascii=False))


def sha512_file(path: Path) -> str:
    """Return the SHA-512 hex digest for a file."""
    digest = hashlib.sha512()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def patch_deps_json_sha512(package: Path) -> None:
    """Populate informational deps.json sha512 fields for packaged DLLs."""
    plugin_root = package / "Runtime" / "Ros2ForUnity" / "Plugins"
    inventory_path = package / "RuntimeSupport" / "r2fu-jazzy-win64-runtime-inventory.json"
    inventory = json.loads(inventory_path.read_text(encoding="utf-8")) if inventory_path.exists() else None
    inventory_by_path = {
        str(item.get("path", "")): item
        for item in (inventory or {}).get("files", [])
        if isinstance(item, dict)
    }

    inventory_changed = False
    for deps_path in sorted(plugin_root.glob("*.deps.json")):
        data = json.loads(deps_path.read_text(encoding="utf-8"))
        changed = False
        for library_name, metadata in data.get("libraries", {}).items():
            if not isinstance(metadata, dict):
                continue

            dll_path = plugin_root / (library_name.split("/", 1)[0] + ".dll")
            if not dll_path.exists():
                continue

            digest = sha512_file(dll_path)
            if metadata.get("sha512") != digest:
                metadata["sha512"] = digest
                changed = True

        if not changed:
            continue

        write_json(deps_path, data)
        inventory_item = inventory_by_path.get("Ros2ForUnity/Plugins/" + deps_path.name)
        if inventory_item is not None:
            inventory_item["sha256"] = sha256_file(deps_path)
            inventory_item["size"] = deps_path.stat().st_size
            inventory_changed = True

    if inventory_changed and inventory_path.exists():
        write_json(inventory_path, inventory)


def runtime_asmdef() -> dict[str, object]:
    """Return the runtime assembly definition used by the packaged R2FU copy."""
    return {
        "name": "Unity2Foxglove.Ros2ForUnity.Runtime",
        "rootNamespace": "",
        "references": [],
        "includePlatforms": ["Editor", "WindowsStandalone64"],
        "excludePlatforms": [],
        "allowUnsafeCode": False,
        "overrideReferences": False,
        "precompiledReferences": [],
        "autoReferenced": True,
        "versionDefines": [],
        "noEngineReferences": False,
    }


def collect_local_patch_overlays(package: Path) -> dict[str, str]:
    """Capture committed local patches before regenerating from the upstream artifact."""
    scripts = package / "Runtime" / "Ros2ForUnity" / "Scripts"
    if not scripts.exists():
        return {}

    overlays: dict[str, str] = {}
    for path in scripts.rglob("*.cs"):
        text = path.read_text(encoding="utf-8", errors="replace")
        relative = path.relative_to(package).as_posix()
        if LOCAL_PATCH_MARKER in text or relative in LOCAL_PATCH_OVERLAY_FILES:
            overlays[relative] = text
    return overlays


def collect_meta_overlays(package: Path) -> dict[str, bytes]:
    """Capture existing Unity metadata so regeneration does not churn GUIDs."""
    if not package.exists():
        return {}

    overlays: dict[str, bytes] = {}
    for path in package.rglob("*.meta"):
        with open(windows_long_path(path), "rb") as stream:
            overlays[path.relative_to(package).as_posix()] = stream.read()
    return overlays


def apply_local_patch_overlays(package: Path, overlays: dict[str, str]) -> None:
    """Replay local lifecycle/time/package-path patches onto the regenerated runtime."""
    for relative, text in overlays.items():
        target = package / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        write_text(target, text)


def apply_meta_overlays(package: Path, overlays: dict[str, bytes]) -> None:
    """Replay metadata only when the corresponding generated asset still exists."""
    for relative, data in overlays.items():
        asset_relative = relative.removesuffix(".meta")
        if not path_exists(package / asset_relative):
            continue
        data = normalize_meta_overlay(relative, data)
        target = package / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        with open(windows_long_path(target), "wb") as stream:
            stream.write(data)


def normalize_meta_overlay(relative: str, data: bytes) -> bytes:
    """Preserve legacy GUIDs while upgrading generated DLL metas to PluginImporter."""
    if not relative.lower().endswith(".dll.meta") or b"PluginImporter:" in data:
        return data

    guid = extract_unity_meta_guid(data.decode("utf-8", errors="replace"))
    if not guid:
        return data

    asset_relative = relative.removesuffix(".meta")
    text = generated_meta_text(Path(asset_relative), asset_relative, is_dir=False, guid=guid)
    return text.encode("utf-8")


def extract_unity_meta_guid(text: str) -> str:
    """Extract a Unity meta GUID from a small generated metadata file."""
    for line in text.splitlines():
        stripped = line.strip()
        if stripped.startswith("guid:"):
            value = stripped.split(":", 1)[1].strip()
            if len(value) == 32 and all(c in "0123456789abcdefABCDEF" for c in value):
                return value.lower()
    return ""


def deterministic_guid(relative_path: str) -> str:
    """Return a deterministic Unity GUID for generated metadata."""
    seed = f"{PACKAGE_NAME}:{relative_path.replace(chr(92), '/')}"
    return hashlib.md5(seed.encode("utf-8"), usedforsecurity=False).hexdigest()


def meta_importer_for(path: Path) -> str:
    """Choose the Unity importer block for generated metadata."""
    if path.name == "package.json":
        return "PackageManifestImporter"
    if path.suffix == ".asmdef":
        return "AssemblyDefinitionImporter"
    if path.suffix.lower() == ".dll":
        return "PluginImporter"
    return "TextScriptImporter"


def generated_meta_text(path: Path, relative_path: str, is_dir: bool, guid: str | None = None) -> str:
    """Return deterministic Unity .meta text for a generated path."""
    guid = guid or deterministic_guid(relative_path)
    if is_dir:
        return (
            "fileFormatVersion: 2\n"
            f"guid: {guid}\n"
            "folderAsset: yes\n"
            "DefaultImporter:\n"
            "  externalObjects: {}\n"
            "  userData:\n"
            "  assetBundleName:\n"
            "  assetBundleVariant:\n"
        )

    importer = meta_importer_for(path)
    if importer == "PluginImporter":
        return (
            "fileFormatVersion: 2\n"
            f"guid: {guid}\n"
            "PluginImporter:\n"
            "  externalObjects: {}\n"
            "  serializedVersion: 2\n"
            "  iconMap: {}\n"
            "  executionOrder: {}\n"
            "  defineConstraints: []\n"
            "  isPreloaded: 0\n"
            "  isOverridable: 0\n"
            "  isExplicitlyReferenced: 0\n"
            "  validateReferences: 1\n"
            "  platformData:\n"
            "  - first:\n"
            "      Any:\n"
            "    second:\n"
            "      enabled: 0\n"
            "      settings: {}\n"
            "  - first:\n"
            "      Editor: Editor\n"
            "    second:\n"
            "      enabled: 1\n"
            "      settings:\n"
            "        CPU: x86_64\n"
            "        OS: Windows\n"
            "  - first:\n"
            "      Standalone: Windows\n"
            "    second:\n"
            "      enabled: 1\n"
            "      settings:\n"
            "        CPU: x86_64\n"
            "  userData:\n"
            "  assetBundleName:\n"
            "  assetBundleVariant:\n"
        )

    return (
        "fileFormatVersion: 2\n"
        f"guid: {guid}\n"
        f"{importer}:\n"
        "  externalObjects: {}\n"
        "  userData:\n"
        "  assetBundleName:\n"
        "  assetBundleVariant:\n"
    )


def ensure_generated_meta(package: Path, target: Path, is_dir: bool, existing_paths: set[str]) -> None:
    """Create a deterministic .meta file when the artifact did not provide one."""
    meta = target.with_name(target.name + ".meta")
    meta_key = meta.as_posix()
    if meta_key in existing_paths:
        return
    relative = target.relative_to(package).as_posix()
    write_text(meta, generated_meta_text(target, relative, is_dir))
    existing_paths.add(meta_key)


def write_generated_metas(package: Path) -> None:
    """Generate metadata for package-owned files and directories lacking upstream metadata."""
    keyed_paths = sorted((path.as_posix(), path) for path in package.rglob("*"))
    existing_paths = {key for key, _ in keyed_paths}
    directories = [path for _, path in keyed_paths if path.is_dir()]
    files = [path for _, path in keyed_paths if path.is_file()]
    for directory in directories:
        ensure_generated_meta(package, directory, is_dir=True, existing_paths=existing_paths)
    for path in files:
        if path.name.endswith(".meta") or path.name == ".gitkeep":
            continue
        ensure_generated_meta(package, path, is_dir=False, existing_paths=existing_paths)


def package_json() -> dict[str, object]:
    """Return the Unity package manifest."""
    return {
        "name": PACKAGE_NAME,
        "version": PACKAGE_VERSION,
        "displayName": "Unity2Foxglove ROS2 For Unity Runtime - Jazzy Win64",
        "license": "Apache-2.0",
        "unity": "6000.0",
        "description": "Optional prototype Jazzy Windows x64 runtime package for Unity2Foxglove ROS2 For Unity integration; fresh-project acceptance and legal attribution review are required before production redistribution.",
        "keywords": [
            "unity2foxglove",
            "ros2",
            "ros2-for-unity",
            "jazzy",
            "win64",
        ],
        "unity2foxgloveConflicts": [
            "dev.unity2foxglove.ros2forunity.runtime.humble.win64",
            "dev.unity2foxglove.ros2forunity.runtime.lyrical.win64",
        ],
        "author": {"name": "Unity2Foxglove"},
    }


def runtime_manifest(artifact: RuntimeArtifact) -> dict[str, object]:
    """Return the runtime package manifest."""
    return {
        "schemaVersion": 1,
        "runtimeId": RUNTIME_ID,
        "packageName": PACKAGE_NAME,
        "packageVersion": PACKAGE_VERSION,
        "rosDistro": "jazzy",
        "platform": "win64",
        "unityPlatform": "Windows",
        "architecture": "x86_64",
        "buildType": "standalone",
        "rmwImplementation": "rmw_fastrtps_cpp",
        "artifactName": artifact.name,
        "artifactSha256": artifact.sha256,
        "artifactSize": artifact.size,
        "inventoryFile": "RuntimeSupport/r2fu-jazzy-win64-runtime-inventory.json",
        "inventoryFileCount": artifact.inventory_file_count,
        "runtimeRoot": "Runtime/Ros2ForUnity",
        "pluginPath": "Runtime/Ros2ForUnity/Plugins/Windows/x86_64",
        "sourceBasis": "Local Jazzy rebuild from RobotecAI ROS2 For Unity and ros2cs sources with Windows ROS2 Jazzy dependency closure",
        "supportLevel": "Recommended",
        "distributionLevel": "Prototype",
        "activeRuntimePolicy": "one_runtime_package_per_project",
        "criticalRuntimeFiles": [
            "rcl.dll",
            "yaml.dll",
            "spdlog.dll",
            "fmt.dll",
        ],
        "handoffInventoryDelta": {
            "addedDlls": list(PHASE161_ADDED_DLLS),
            "excludedTestTypesupportDlls": list(V083_EXCLUDED_TEST_TYPESUPPORT_DLLS),
            "assetCriticalBaseline": list(PHASE161_ASSET_CRITICAL_BASELINE),
        },
        "packagePathPatch": {
            "modifiedFile": "Runtime/Ros2ForUnity/Scripts/ROS2ForUnity.cs",
            "reason": "Resolve the runtime root from this Unity package when Assets/Ros2ForUnity is absent.",
            "keepsAssetFolderFallback": True,
        },
        "freshProjectAcceptance": "deferred_to_install_acceptance",
    }


def readme_text(artifact: RuntimeArtifact) -> str:
    """Return the runtime package README."""
    return f"""# Unity2Foxglove ROS2 For Unity Runtime - Jazzy Win64

This package is an optional Windows x64 runtime for the Unity2Foxglove ROS2 For Unity integration. It carries the ROS2 For Unity runtime files, generated message assemblies, native ROS2 Jazzy DLLs, Fast DDS/RMW files, ros2cs files, metadata, inventory, and notices.

## Package Role

Install this package when a Unity project needs to run as a ROS2 node through ROS2 For Unity on Windows x64.

This package is independent from `dev.unity2foxglove.sdk` and can import by itself. It does not provide the high-level Unity2Foxglove facade or samples by itself; those live in `dev.unity2foxglove.ros2forunity`.

Recommended combinations:

- `dev.unity2foxglove.ros2forunity.runtime.jazzy.win64` alone: imports runtime files, manifest, notices, and diagnostics.
- `dev.unity2foxglove.ros2forunity` plus this runtime package: enables adapter-backed ROS2 publish/subscribe.
- `dev.unity2foxglove.sdk` plus adapter plus this runtime package: enables the combined Unity2Foxglove workflow.

## One Runtime Policy

Install only one `dev.unity2foxglove.ros2forunity.runtime.*` package in a Unity project. Multiple ROS2 runtime packages can load conflicting native DLLs or generated message assemblies.

Do not import the old `Assets/Ros2ForUnity` asset folder and this package in the same project. Use either an external asset-folder runtime or this package runtime.

The script assembly is intentionally named `Unity2Foxglove.Ros2ForUnity.Runtime` across all distro runtime packages. The adapter package references that stable assembly name, while the one-runtime policy and package conflict metadata prevent multiple distro runtimes from being active in the same Unity project.

## Runtime Identity

- ROS distro: Jazzy
- Platform: Windows x64
- Build type: standalone
- RMW implementation: `rmw_fastrtps_cpp`
- Runtime id: `r2fu-jazzy-win64`
- Artifact source: `{artifact.name}`
- SHA-256: `{artifact.sha256}`

The runtime manifest is `RuntimeSupport/runtime-manifest.json`. The file inventory is `RuntimeSupport/r2fu-jazzy-win64-runtime-inventory.json`.

## Package Path Patch

The bundled `ROS2ForUnity.cs` keeps the upstream `Assets/Ros2ForUnity` lookup and adds a package-path fallback so Unity Editor can load this runtime from:

```text
Packages/dev.unity2foxglove.ros2forunity.runtime.jazzy.win64/Runtime/Ros2ForUnity
```

This patch is limited to locating runtime files from a Unity package. It does not change ROS2 For Unity node, publisher, subscriber, or DDS behavior.

## Network Acceptance Notes

WSL2 NAT can hide DDS discovery and should be treated as diagnostic-only for Windows package acceptance. Configure Windows Defender Firewall allow rules for Fast DDS UDP ports, then prefer Windows ROS2 Jazzy or a real remote Linux topology for final external-graph acceptance.

## Support Boundary

This is a prototype runtime package. Fresh-project install acceptance and public release readiness are separate gates. Linux, macOS, Humble, and Lyrical runtime packages are not included here.

RobotecAI states that ROS2 For Unity is officially supported for AWSIM/Autoware users and that the Robotec team cannot support and maintain the project for the general community. Unity2Foxglove-specific packaging and support belong to Unity2Foxglove, not RobotecAI.
"""


def notices_text(inventory: dict[str, object], artifact: RuntimeArtifact) -> str:
    """Return third-party notices for the runtime package."""
    file_count = inventory.get("fileCount", artifact.inventory_file_count)
    return f"""# Third-Party Notices

This runtime package redistributes a locally rebuilt ROS2 For Unity Jazzy Windows x64 runtime payload.

Unity2Foxglove does not claim authorship of RobotecAI ROS2 For Unity, ros2cs, generated ROS2 message assemblies, generated native message support libraries, ROS2 Jazzy native libraries, Fast DDS, Fast CDR, RMW FastRTPS, or transitive runtime DLLs.

## Runtime Artifact

| Field | Value |
|---|---|
| Artifact | `{artifact.name}` |
| Runtime id | `r2fu-jazzy-win64` |
| ROS distro | `jazzy` |
| Platform | Windows x64 |
| Build type | standalone |
| RMW | `rmw_fastrtps_cpp` |
| SHA-256 | `{artifact.sha256}` |
| Inventory file count | `{file_count}` |

## Known Upstream Components

| Component | Relationship |
|---|---|
| RobotecAI ROS2 For Unity | Unity integration surface for ROS2 node behavior |
| ros2cs | ROS2 C# binding stack used by ROS2 For Unity |
| ROS2 Jazzy native runtime | `rcl`, `rcutils`, `rmw`, message type support, and related runtime DLLs |
| Fast DDS / Fast CDR | DDS and CDR runtime dependency family used by the FastRTPS RMW path |
| RMW FastRTPS | `rmw_fastrtps_cpp` runtime path used by the current Windows artifact |
| Generated message support | Managed message assemblies plus native ROSIDL/type-support DLLs |

## Critical Runtime Closure

The package includes the transitive runtime DLLs required for Unity to load `rcl.dll`, including:

```text
rcl.dll
yaml.dll
spdlog.dll
fmt.dll
```

If these closure DLLs are removed, Unity can report `UnsatisfiedLinkError: rcl.dll` even when `rcl.dll` itself is present.

## Redistribution Caveats

- This package is a prototype until fresh-project acceptance passes.
- The inventory is an engineering inventory generated from the local runtime artifact, not a complete legal audit.
- Public release should refresh transitive license attribution before registry or binary distribution.
- WSL2 NAT can hide DDS discovery and should be treated as diagnostic-only for Windows package acceptance. Configure Windows Defender Firewall allow rules for Fast DDS UDP ports, then prefer Windows ROS2 Jazzy or a real remote Linux topology for final external-graph acceptance.

RobotecAI states that ROS2 For Unity is officially supported for AWSIM/Autoware users and that the Robotec team cannot support and maintain the project for the general community. Unity2Foxglove must preserve that caveat and must not imply upstream community support for Unity2Foxglove-specific packaging.
"""


def extract_runtime(paths: BuildPaths) -> None:
    """Extract the Ros2ForUnity asset folder into the runtime package layout."""
    runtime_root = paths.package / "Runtime" / "Ros2ForUnity"
    runtime_root.mkdir(parents=True, exist_ok=True)
    runtime_root_resolved = runtime_root.resolve()
    with zipfile.ZipFile(paths.artifact) as archive:
        for info in archive.infolist():
            name = info.filename
            if info.is_dir() or not name.startswith("Ros2ForUnity/"):
                continue
            relative = safe_runtime_zip_relative_path(name)
            target = (runtime_root / relative).resolve()
            try:
                target.relative_to(runtime_root_resolved)
            except ValueError as exc:
                raise ValueError(f"Rejected runtime zip entry outside package root: {name}") from exc
            target.parent.mkdir(parents=True, exist_ok=True)
            with archive.open(info) as source, target.open("wb") as destination:
                shutil.copyfileobj(source, destination)


def copy_supplemental_runtime_dlls(package: Path, ros2_bin: Path) -> None:
    """Copy Jazzy FastRTPS dependencies missing from the pinned R2FU artifact."""
    plugin_root = package / "Runtime" / "Ros2ForUnity" / "Plugins" / "Windows" / "x86_64"
    for name in PHASE161_SUPPLEMENTAL_RUNTIME_DLLS:
        source = ros2_bin / name
        if not source.exists():
            raise FileNotFoundError(
                f"Missing supplemental Jazzy runtime DLL {source}; "
                "install the repo-local ros2-windows/ros2_jazzy entrypoint before rebuilding this package."
            )
        shutil.copy2(source, plugin_root / name)


def safe_runtime_zip_relative_path(name: str) -> Path:
    """Return the path under Runtime/Ros2ForUnity for a trusted zip entry name."""
    zip_path = PurePosixPath(name)
    if zip_path.is_absolute():
        raise ValueError(f"Rejected absolute runtime zip entry: {name}")

    parts = zip_path.parts
    if len(parts) < 2 or parts[0] != "Ros2ForUnity":
        raise ValueError(f"Rejected unexpected runtime zip entry: {name}")
    if any(part in ("", ".", "..") for part in parts):
        raise ValueError(f"Rejected unsafe runtime zip entry: {name}")

    return Path(*parts[1:])


def prune_non_contract_examples(package: Path) -> None:
    """Remove upstream examples whose lifecycle is not part of this runtime package contract."""
    scripts = package / "Runtime" / "Ros2ForUnity" / "Scripts"
    for name in LEAKY_UPSTREAM_EXAMPLES:
        for path in (scripts / name, scripts / (name + ".meta")):
            try:
                path.unlink()
            except FileNotFoundError:
                pass


def patch_ros2_for_unity(package: Path) -> None:
    """Patch ROS2ForUnity.cs so the runtime can live inside a Unity package."""
    source = package / "Runtime" / "Ros2ForUnity" / "Scripts" / "ROS2ForUnity.cs"
    text = source.read_text(encoding="utf-8")
    text = patch_ros2cs_logger_callback_api(text)
    if UNITY_PACKAGE_PATH_PATCH_MARKER in text:
        text = patch_standalone_environment_bootstrap(text)
        text = patch_runtime_lifecycle_safety(text)
        write_text(source, text)
        return
    if "unity2FoxgloveRuntimePackageName" not in text:
        text = text.replace(
            '    private static string ros2ForUnityAssetFolderName = "Ros2ForUnity";\n',
            '    private static string ros2ForUnityAssetFolderName = "Ros2ForUnity";\n' + PACKAGE_CONSTANTS_BLOCK,
        )
    old_copyright = "// Modifications Copyright (c) 2026 Jianbin Liu.\n"
    new_copyright = "// Modifications Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.\n"
    if old_copyright not in text:
        raise ValueError("Could not find upstream modifications copyright line to patch.")
    text = text.replace(old_copyright, new_copyright, 1)
    if UPSTREAM_PATH_BLOCK in text:
        text = text.replace(UPSTREAM_PATH_BLOCK, PACKAGE_PATH_BLOCK)
    elif UPSTREAM_COMPUTE_PATH_BLOCK in text:
        text = text.replace(UPSTREAM_COMPUTE_PATH_BLOCK, PACKAGE_COMPUTE_PATH_BLOCK)
    else:
        raise ValueError("Could not find upstream ROS2ForUnity path block to patch.")
    text = patch_standalone_environment_bootstrap(text)
    text = patch_runtime_lifecycle_safety(text)
    write_text(source, text)


def patch_ros2cs_logger_callback_api(text: str) -> str:
    """Patch obsolete ros2cs logger callback calls emitted by older runtime artifacts."""
    return text.replace("Ros2csLogger.setCallback", "Ros2csLogger.SetCallback")


def patch_runtime_lifecycle_safety(text: str) -> str:
    """Restore local lifecycle guards that a refreshed upstream runtime can omit."""
    register_marker = "        EditorApplication.quitting += ShutdownShared;"
    unregister_marker = "        EditorApplication.quitting -= ShutdownShared;"
    if "AssemblyReloadEvents.beforeAssemblyReload += ShutdownShared" not in text:
        text = text.replace(
            register_marker,
            register_marker + "\n        AssemblyReloadEvents.beforeAssemblyReload += ShutdownShared;",
            1,
        )
    if "AssemblyReloadEvents.beforeAssemblyReload -= ShutdownShared" not in text:
        text = text.replace(
            unregister_marker,
            unregister_marker + "\n        AssemblyReloadEvents.beforeAssemblyReload -= ShutdownShared;",
            1,
        )

    dead_guard = "    private static void ThrowIfUninitialized(string callContext)\n"
    guard_start = text.find(dead_guard)
    if guard_start >= 0:
        guard_end = text.find("\n    }\n\n", guard_start)
        if guard_end < 0:
            raise ValueError("Could not remove the stale ThrowIfUninitialized guard.")
        text = text[:guard_start] + text[guard_end + len("\n    }\n\n"):]

    metadata_prerequisite = "LoadMetadata() must complete before metadata-backed properties are read."
    if metadata_prerequisite not in text:
        text = text.replace(
            '            throw new InvalidOperationException("Metadata document is empty while reading " + valuePath);\n',
            "            throw new InvalidOperationException(\n"
            '                "Metadata document is empty while reading " + valuePath +\n'
            '                ". LoadMetadata() must complete before metadata-backed properties are read.");\n',
            1,
        )
    return text


def patch_unity_time_source_main_thread_guard(text: str) -> str:
    """Restore a clear construction failure when Unity time is initialized off the main thread."""
    if "must be constructed on the Unity main thread" not in text:
        text = text.replace(
            "    mainThreadId = Thread.CurrentThread.ManagedThreadId;\n"
            "    lastReadingSecs = Time.timeAsDouble;\n",
            "    mainThreadId = Thread.CurrentThread.ManagedThreadId;\n"
            "    try\n"
            "    {\n"
            "      lastReadingSecs = Time.timeAsDouble;\n"
            "    }\n"
            "    catch (UnityException exception)\n"
            "    {\n"
            "      throw new InvalidOperationException(\n"
            '        "UnityTimeSource must be constructed on the Unity main thread.", exception);\n'
            "    }\n",
            1,
        )
    return text


def patch_standalone_environment_bootstrap(text: str) -> str:
    """Patch standalone Jazzy environment writes so native ROS 2 getenv callers see them."""
    if "using System.Runtime.InteropServices;" not in text:
        text = text.replace(
            "using System.Reflection;\n",
            "using System.Reflection;\nusing System.Runtime.InteropServices;\n",
            1,
        )

    if "_wputenv_s" not in text:
        text = text.replace(
            "    private bool ownsLifecycle;\n",
            "    private bool ownsLifecycle;\n\n"
            "    // Windows standalone ROS 2 libraries read getenv() through UCRT, so mirror managed env writes there.\n"
            "    [DllImport(\"ucrtbase.dll\", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]\n"
            "    private static extern int _wputenv_s(string name, string value);\n",
            1,
        )

    if "SetProcessEnvironmentVariable" not in text:
        text = text.replace(
            "    private string GetEnvPathVariableValue()\n"
            "    {\n"
            "        return Environment.GetEnvironmentVariable(GetEnvPathVariableName());\n"
            "    }\n\n",
            "    private string GetEnvPathVariableValue()\n"
            "    {\n"
            "        return Environment.GetEnvironmentVariable(GetEnvPathVariableName());\n"
            "    }\n\n"
            "    private static void SetProcessEnvironmentVariable(string name, string value)\n"
            "    {\n"
            "        Environment.SetEnvironmentVariable(name, value);\n"
            "        if (GetOS() == Platform.Windows)\n"
            "        {\n"
            "            // U2F-LOCAL-PATCH: ROS 2 Windows native code reads getenv() from UCRT.\n"
            "            int result = _wputenv_s(name, value);\n"
            "            if (result != 0)\n"
            "            {\n"
            "                throw new InvalidOperationException(\n"
            "                    \"Failed to set Windows CRT environment variable '\" + name + \"' (ucrtbase _wputenv_s returned \" + result + \")\");\n"
            "            }\n"
            "        }\n"
            "    }\n\n",
            1,
        )

    text = text.replace(
        "        Environment.SetEnvironmentVariable(GetEnvPathVariableName(), string.Join(envPathSep.ToString(), entries));",
        "        SetProcessEnvironmentVariable(GetEnvPathVariableName(), string.Join(envPathSep.ToString(), entries));",
        1,
    )
    if "Environment.SetEnvironmentVariable(GetEnvPathVariableName()," in text:
        raise ValueError("Could not patch ROS2ForUnity standalone environment bootstrap call site.")

    if "SetStandalonePrefixPath" not in text:
        text = text.replace(
            "\n    private static string NormalizeEnvPathEntry(string value)\n",
            "\n    private static void SetStandalonePrefixPath()\n"
            "    {\n"
            "        string prefixPath = GetRos2ForUnityPath();\n"
            "        string pluginPrefixPath = GetPluginPath();\n"
            "        if (Directory.Exists(Path.Combine(pluginPrefixPath, \"share\")))\n"
            "        {\n"
            "            prefixPath = pluginPrefixPath;\n"
            "        }\n"
            "        else if (!Directory.Exists(Path.Combine(prefixPath, \"share\")))\n"
            "        {\n"
            "            Debug.LogWarning(\"Standalone AMENT_PREFIX_PATH fallback has no share directory: \" + prefixPath);\n"
            "        }\n\n"
            "        // U2F-LOCAL-PATCH: standalone runtime must not inherit or require a sourced ROS 2 workspace.\n"
            "        SetProcessEnvironmentVariable(\"AMENT_PREFIX_PATH\", prefixPath);\n"
            "    }\n\n"
            "    private static void SetStandaloneRmwImplementation()\n"
            "    {\n"
            "        // U2F-LOCAL-PATCH: standalone Jazzy runtime owns its RMW selection.\n"
            "        SetProcessEnvironmentVariable(\"RMW_IMPLEMENTATION\", expectedRmwImplementation);\n"
            "    }\n\n"
            "    private static void SetStandaloneRosDistro(string ros2Codename)\n"
            "    {\n"
            "        // U2F-LOCAL-PATCH: standalone runtime owns ROS_DISTRO even when Unity was launched from another ROS shell.\n"
            "        SetProcessEnvironmentVariable(\"ROS_DISTRO\", ros2Codename);\n"
            "    }\n\n"
            "    private static string NormalizeEnvPathEntry(string value)\n",
            1,
        )
    elif "SetStandaloneRosDistro" not in text:
        text = text.replace(
            "\n    private static string NormalizeEnvPathEntry(string value)\n",
            "\n    private static void SetStandaloneRosDistro(string ros2Codename)\n"
            "    {\n"
            "        // U2F-LOCAL-PATCH: standalone runtime owns ROS_DISTRO even when Unity was launched from another ROS shell.\n"
            "        SetProcessEnvironmentVariable(\"ROS_DISTRO\", ros2Codename);\n"
            "    }\n\n"
            "    private static string NormalizeEnvPathEntry(string value)\n",
            1,
        )

    old_check_signature = '''    public void CheckIntegrity()
    {
        string ros2SourcedCodename = GetROSVersionSourced();
'''
    new_check_signature = '''    public void CheckIntegrity()
    {
        CheckIntegrity(GetROSVersionSourced());
    }

    private void CheckIntegrity(string ros2SourcedCodename)
    {
'''
    text = text.replace(old_check_signature, new_check_signature)
    if "WarnIfStandaloneRosDistroOverride" not in text:
        text = text.replace(
            "\n    /// <summary>\n    /// Checks if both ros2cs and ros2-for-unity were build for the same ros version as well as\n",
            "\n    private static void WarnIfStandaloneRosDistroOverride(string sourcedRosDistro, string packagedRos2Version)\n"
            "    {\n"
            "        if (string.IsNullOrEmpty(sourcedRosDistro)\n"
            "            || string.Equals(sourcedRosDistro, packagedRos2Version, StringComparison.OrdinalIgnoreCase))\n"
            "        {\n"
            "            return;\n"
            "        }\n\n"
            "        Debug.LogWarning(\n"
            "            \"Ignoring sourced ROS_DISTRO '\" + sourcedRosDistro +\n"
            "            \"' because standalone runtime package provides '\" + packagedRos2Version + \"'.\");\n"
            "    }\n\n"
            "    /// <summary>\n"
            "    /// Checks if both ros2cs and ros2-for-unity were build for the same ros version as well as\n",
            1,
        )

    old_metadata_mismatch = '''        if (ros2FromRos4UMetadata != ros2FromRos2csMetadata) {
            Debug.LogError(
                "ROS2 versions in 'ros2cs' and 'ros2-for-unity' metadata files are not the same. " +
                "This is caused by mixing versions/builds. Plugin might not work correctly."
            );
        }
'''
    new_metadata_mismatch = '''        if (ros2FromRos4UMetadata != ros2FromRos2csMetadata) {
            FailIntegrity(
                "ROS2 versions in 'ros2cs' and 'ros2-for-unity' metadata files are not the same. " +
                "This is caused by mixing versions/builds.");
        }
'''
    text = text.replace(old_metadata_mismatch, new_metadata_mismatch)
    old_non_standalone_mismatch = '''        if(!IsStandalone() && ros2SourcedCodename != ros2FromRos2csMetadata) {
            Debug.LogError(
                "ROS2 version in 'ros2cs' metadata doesn't match currently sourced version. " +
                "This is caused by mixing versions/builds. Plugin might not work correctly."
            );
        }
'''
    new_non_standalone_mismatch = '''        if(!IsStandalone() && ros2SourcedCodename != ros2FromRos2csMetadata) {
            FailIntegrity(
                "ROS2 version in 'ros2cs' metadata doesn't match currently sourced version. " +
                "This is caused by mixing versions/builds.");
        }
'''
    text = text.replace(old_non_standalone_mismatch, new_non_standalone_mismatch)
    old_standalone_sourced = '''        if (IsStandalone() && !string.IsNullOrEmpty(ros2SourcedCodename)) {
            Debug.LogError(
                "You should not source ROS2 in 'ros2-for-unity' standalone build. " +
                "Plugin might not work correctly."
            );
        }
    }
'''
    new_standalone_sourced = '''    }

    private static void FailIntegrity(string errMessage)
    {
        Debug.LogError(errMessage);
#if UNITY_EDITOR
        EditorApplication.isPlaying = false;
        throw new InvalidOperationException(errMessage);
#else
        const int ROS_METADATA_MISMATCH_ERROR_CODE = 35;
        Application.Quit(ROS_METADATA_MISMATCH_ERROR_CODE);
        throw new InvalidOperationException(errMessage);
#endif
    }
'''
    text = text.replace(old_standalone_sourced, new_standalone_sourced)
    if "sourcedRosDistroBeforeStandalonePatch" not in text:
        old_startup = '''        // Load metadata
        LoadMetadata();
        string currentRos2Version = GetROSVersion();
        string standalone = IsStandalone() ? "standalone" : "non-standalone";

        // Self checks
        if (!IsStandalone())
        {
            CheckROSSupport(currentRos2Version);
        }
        CheckIntegrity();
'''
        new_startup = '''        // Load metadata
        LoadMetadata();
        string sourcedRosDistroBeforeStandalonePatch = GetROSVersionSourced();
        bool standaloneBuild = IsStandalone();
        if (standaloneBuild)
        {
            SetStandalonePrefixPath();
            SetStandaloneRmwImplementation();
        }

        string currentRos2Version = standaloneBuild
            ? GetMetadataValue(ros2csMetadata, "/ros2cs/ros2")
            : GetROSVersion();
        if (standaloneBuild)
        {
            SetStandaloneRosDistro(currentRos2Version);
        }
        string standalone = standaloneBuild ? "standalone" : "non-standalone";

        // Self checks
        CheckROSSupport(currentRos2Version);
        WarnIfStandaloneRosDistroOverride(sourcedRosDistroBeforeStandalonePatch, currentRos2Version);
        CheckIntegrity(standaloneBuild ? null : sourcedRosDistroBeforeStandalonePatch);
'''
        text = text.replace(old_startup, new_startup)
        if "sourcedRosDistroBeforeStandalonePatch" not in text:
            refreshed_upstream_startup = '''            // Load metadata
            LoadMetadata();
            string currentRos2Version = GetROSVersion();
            string standalone = IsStandalone() ? "standalone" : "non-standalone";

            // Self checks
            CheckROSSupport(currentRos2Version);
            CheckIntegrity();
            bool standaloneBuild = IsStandalone();
            WarnIfLyricalSpinFallbackUnset(currentRos2Version, standaloneBuild);

            // Library loading
'''
            refreshed_startup_patch = '''            // Load metadata
            LoadMetadata();
            string sourcedRosDistroBeforeStandalonePatch = GetROSVersionSourced();
            bool standaloneBuild = IsStandalone();
            if (standaloneBuild)
            {
                SetStandalonePrefixPath();
                SetStandaloneRmwImplementation();
            }

            string currentRos2Version = standaloneBuild
                ? GetMetadataValue(ros2csMetadata, "/ros2cs/ros2")
                : GetROSVersion();
            if (standaloneBuild)
            {
                SetStandaloneRosDistro(currentRos2Version);
            }
            string standalone = standaloneBuild ? "standalone" : "non-standalone";

            // Self checks
            CheckROSSupport(currentRos2Version);
            WarnIfStandaloneRosDistroOverride(sourcedRosDistroBeforeStandalonePatch, currentRos2Version);
            CheckIntegrity(standaloneBuild ? null : sourcedRosDistroBeforeStandalonePatch);
            WarnIfLyricalSpinFallbackUnset(currentRos2Version, standaloneBuild);

            // Library loading
'''
            text = text.replace(refreshed_upstream_startup, refreshed_startup_patch, 1)
            text = text.replace(
                '''            if (standaloneBuild)
            {
                // For standalone, currentRos2Version comes from metadata, not ROS_DISTRO.
                // SetStandaloneRosDistro must stay after CheckROSSupport/CheckIntegrity.
                SetStandaloneRosDistro(currentRos2Version);
                SetStandaloneRos2csSpinFallback(currentRos2Version);
                SetStandalonePrefixPath();
                SetStandaloneRmwImplementation();
                SetStandaloneRcutilsConsoleMode();
            }
''',
                '''            if (standaloneBuild)
            {
                SetStandaloneRos2csSpinFallback(currentRos2Version);
                SetStandaloneRcutilsConsoleMode();
            }
''',
                1,
            )
    text = text.replace(
        "        CheckIntegrity(" + "sourcedRosDistroBeforeStandalonePatch);\n",
        "        WarnIfStandaloneRosDistroOverride(sourcedRosDistroBeforeStandalonePatch, currentRos2Version);\n"
        "        CheckIntegrity(standaloneBuild ? null : sourcedRosDistroBeforeStandalonePatch);\n",
        1,
    )

    if "SetStandalonePrefixPath();" not in text:
        text = text.replace(
            "        // Library loading\n"
            "        if (GetOS() == Platform.Windows) {\n",
            "        // Library loading\n"
            "        if (IsStandalone())\n"
            "        {\n"
            "            SetStandalonePrefixPath();\n"
            "            SetStandaloneRmwImplementation();\n"
            "        }\n"
            "        if (GetOS() == Platform.Windows) {\n",
            1,
        )

    for token in (
        "_wputenv_s",
        "SetStandalonePrefixPath",
        "AMENT_PREFIX_PATH",
        "SetStandaloneRmwImplementation",
        "sourcedRosDistroBeforeStandalonePatch",
        "FailIntegrity",
    ):
        if token not in text:
            raise ValueError(f"Could not patch ROS2ForUnity standalone environment bootstrap token: {token}")
    return text


def patch_ros_time_source_contract(package: Path) -> None:
    """Patch ROS2 time sources for the bool-returning ITimeSource contract."""
    time_dir = package / "Runtime" / "Ros2ForUnity" / "Scripts" / "Time"
    interface_file = time_dir / "ITimeSource.cs"
    if interface_file.exists():
        interface_text = interface_file.read_text(encoding="utf-8")
        interface_text = interface_text.replace(
            "/// <summary>\n"
            "/// Interface for acquiring time.\n"
            "/// </summary>\n"
            "public interface ITimeSource\n"
            "{\n"
            "  /// <returns>True when a valid timestamp was acquired; false when the source is not currently usable.</returns>\n"
            "  bool GetTime(out int seconds, out uint nanoseconds);\n"
            "}\n",
            "/// <summary>\n"
            "/// Interface for acquiring time as ROS-compatible timestamp fields from a concrete time source.\n"
            "/// </summary>\n"
            "public interface ITimeSource\n"
            "{\n"
            "  /// <summary>\n"
            "  /// Tries to acquire the current timestamp for ROS message headers and clock messages.\n"
            "  /// </summary>\n"
            "  /// <param name=\"seconds\">Whole seconds of the acquired timestamp, or 0 when this method returns false.</param>\n"
            "  /// <param name=\"nanoseconds\">Nanoseconds within the second, or 0 when this method returns false.</param>\n"
            "  /// <returns>True when a valid timestamp was acquired; false when the source is not currently usable.</returns>\n"
            "  /// <remarks>\n"
            "  /// Epoch semantics are source-specific: DotnetTimeSource and ROS2TimeSource report Unix/ROS-aligned time,\n"
            "  /// while UnityTimeSource reports Unity play time. Callers must not use the out values when this method\n"
            "  /// returns false.\n"
            "  /// </remarks>\n"
            "  bool GetTime(out int seconds, out uint nanoseconds);\n"
            "}\n",
            1,
        )
        if "Epoch semantics are source-specific" not in interface_text:
            raise ValueError("ITimeSource.cs is missing the expanded bool-returning time-source contract documentation.")
        write_text(interface_file, interface_text)

    dotnet_time = time_dir / "DotnetTimeSource.cs"
    dotnet_text = dotnet_time.read_text(encoding="utf-8")
    dotnet_text = dotnet_text.replace(
        "// Modifications Copyright (c) 2026 Jianbin Liu.\n",
        "// Modifications Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.\n",
        1,
    )
    if MODIFICATIONS_COPYRIGHT not in dotnet_text:
        raise ValueError("DotnetTimeSource.cs is missing the local modifications copyright line.")
    if "Outputs are clamped to the last emitted timestamp" not in dotnet_text:
        dotnet_text = dotnet_text.replace(
            "/// DateTime.UtcNow provides the epoch alignment, while Stopwatch improves short-term resolution\n"
            "/// between periodic wall-clock resynchronizations.\n",
            "/// DateTime.UtcNow provides the epoch alignment, while Stopwatch improves short-term resolution\n"
            "/// between periodic wall-clock resynchronizations.\n"
            "/// Outputs are clamped to the last emitted timestamp so wall-clock corrections cannot move time backward.\n",
            1,
        )
    if "lastEmittedSeconds" not in dotnet_text:
        dotnet_text = dotnet_text.replace(
            "    private double systemTimeIntervalStart = 0;\n",
            "    private double systemTimeIntervalStart = 0;\n"
            "    private double lastEmittedSeconds = double.NegativeInfinity;\n",
            1,
        )
        dotnet_text = dotnet_text.replace(
            "            TimeUtils.TimeFromTotalSeconds(systemTimeIntervalStart + timeOffset, out seconds, out nanoseconds);\n",
            "            var totalSeconds = systemTimeIntervalStart + timeOffset;\n"
            "            if (totalSeconds < lastEmittedSeconds)\n"
            "            {\n"
            "                totalSeconds = lastEmittedSeconds;\n"
            "            }\n"
            "            else\n"
            "            {\n"
            "                lastEmittedSeconds = totalSeconds;\n"
            "            }\n"
            "\n"
            "            TimeUtils.TimeFromTotalSeconds(totalSeconds, out seconds, out nanoseconds);\n",
            1,
        )
    write_text(dotnet_time, dotnet_text)

    unity_time = time_dir / "UnityTimeSource.cs"
    if unity_time.exists():
        write_text(
            unity_time,
            patch_unity_time_source_main_thread_guard(unity_time.read_text(encoding="utf-8")),
        )

    time_utils = time_dir / "TimeUtils.cs"
    time_utils_text = time_utils.read_text(encoding="utf-8")
    if "Double.IsNaN(secondsIn)" not in time_utils_text:
        time_utils_text = time_utils_text.replace(
            "internal static class TimeUtils\n"
            "{\n"
            "  public static void TimeFromTotalSeconds(in double secondsIn, out int seconds, out uint nanoseconds)\n"
            "  {\n"
            "    seconds = (int)Math.Floor(secondsIn);\n"
            "    double fractionalSeconds = secondsIn - seconds;\n"
            "    long normalizedNanoseconds = (long)Math.Floor(fractionalSeconds * 1000000000.0);\n"
            "    if (normalizedNanoseconds >= 1000000000L)\n"
            "    {\n"
            "      seconds++;\n"
            "      normalizedNanoseconds -= 1000000000L;\n"
            "    }\n"
            "    else if (normalizedNanoseconds < 0)\n"
            "    {\n"
            "      seconds--;\n"
            "      normalizedNanoseconds += 1000000000L;\n"
            "    }\n"
            "    nanoseconds = (uint)normalizedNanoseconds;\n"
            "  }\n"
            "}\n",
            "internal static class TimeUtils\n"
            "{\n"
            "  private const double NanosecondsPerSecondDouble = 1_000_000_000.0;\n"
            "  private const long NanosecondsPerSecond = 1_000_000_000L;\n"
            "\n"
            "  public static void TimeFromTotalSeconds(in double secondsIn, out int seconds, out uint nanoseconds)\n"
            "  {\n"
            "    if (Double.IsNaN(secondsIn) || Double.IsInfinity(secondsIn))\n"
            "    {\n"
            "      throw new ArgumentOutOfRangeException(nameof(secondsIn), \"ROS time cannot be NaN or infinity\");\n"
            "    }\n"
            "\n"
            "    double wholeSeconds = Math.Floor(secondsIn);\n"
            "    double fractionalSeconds = secondsIn - wholeSeconds;\n"
            "    long normalizedNanoseconds = (long)Math.Floor(fractionalSeconds * NanosecondsPerSecondDouble);\n"
            "    if (normalizedNanoseconds >= NanosecondsPerSecond)\n"
            "    {\n"
            "      wholeSeconds += 1.0;\n"
            "      normalizedNanoseconds -= NanosecondsPerSecond;\n"
            "    }\n"
            "    else if (normalizedNanoseconds < 0)\n"
            "    {\n"
            "      wholeSeconds -= 1.0;\n"
            "      normalizedNanoseconds += NanosecondsPerSecond;\n"
            "    }\n"
            "\n"
            "    if (wholeSeconds < Int32.MinValue || wholeSeconds > Int32.MaxValue)\n"
            "    {\n"
            "      throw new OverflowException(\"ROS time seconds exceed Int32 range\");\n"
            "    }\n"
            "\n"
            "    seconds = (int)wholeSeconds;\n"
            "    nanoseconds = (uint)normalizedNanoseconds;\n"
            "  }\n"
            "}\n",
            1,
        )
    if "Double.IsNaN(secondsIn)" not in time_utils_text or "Int32.MaxValue" not in time_utils_text:
        raise ValueError("TimeUtils.cs is missing hardened seconds validation guards.")
    write_text(time_utils, time_utils_text)

    for name in ("ROS2TimeSource.cs", "ROS2ScalableTimeSource.cs"):
        source = time_dir / name
        text = source.read_text(encoding="utf-8")
        if "public void GetTime(out int seconds, out uint nanoseconds)" in text:
            text = text.replace(
                "  public void GetTime(out int seconds, out uint nanoseconds)\n  {\n",
                "  public bool GetTime(out int seconds, out uint nanoseconds)\n  {\n"
                "    // U2F-LOCAL-PATCH: match newer ros2cs bool-returning ITimeSource contract.\n",
                1,
            )
            text = text.replace(
                '      Debug.LogWarning("Cannot acquire valid ros time, ros either not initialized or shut down already");\n'
                "      return;\n",
                '      Debug.LogWarning("Cannot acquire valid ros time, ros either not initialized or shut down already");\n'
                "      return false;\n",
                1,
            )

        if "public bool GetTime(out int seconds, out uint nanoseconds)" not in text:
            raise ValueError(f"{name} does not expose the bool-returning ITimeSource contract.")
        if "bool-returning ITimeSource contract" not in text:
            text = text.replace(
                "  public bool GetTime(out int seconds, out uint nanoseconds)\n  {\n",
                "  public bool GetTime(out int seconds, out uint nanoseconds)\n  {\n"
                "    // U2F-LOCAL-PATCH: match newer ros2cs bool-returning ITimeSource contract.\n",
                1,
            )

        if name == "ROS2TimeSource.cs" and "private readonly object clockMutex = new object();" not in text:
            text = text.replace(
                "  private ROS2.Clock clock;\n",
                "  private readonly object clockMutex = new object();\n"
                "  private ROS2.Clock clock;\n",
                1,
            )
            text = text.replace(
                "    if (clock == null)\n"
                "    { // Create clock which uses system time by default (unless use_sim_time is set in ros2)\n"
                "      if (Volatile.Read(ref disposed) != 0)\n"
                "      {\n"
                "        seconds = 0;\n"
                "        nanoseconds = 0;\n"
                "        return false;\n"
                "      }\n"
                "      clock = new ROS2.Clock();\n"
                "    }\n"
                "  \n"
                "    TimeUtils.TimeFromTotalSeconds(clock.Now.Seconds, out seconds, out nanoseconds);\n",
                "    double nowSeconds;\n"
                "    lock (clockMutex)\n"
                "    {\n"
                "      if (Volatile.Read(ref disposed) != 0)\n"
                "      {\n"
                "        seconds = 0;\n"
                "        nanoseconds = 0;\n"
                "        return false;\n"
                "      }\n"
                "\n"
                "      if (clock == null)\n"
                "      { // Create clock which uses system time by default (unless use_sim_time is set in ros2)\n"
                "        clock = new ROS2.Clock();\n"
                "      }\n"
                "\n"
                "      nowSeconds = clock.Now.Seconds;\n"
                "    }\n"
                "  \n"
                "    TimeUtils.TimeFromTotalSeconds(nowSeconds, out seconds, out nanoseconds);\n",
                1,
            )
            text = text.replace(
                "    // U2F-LOCAL-PATCH: avoid native cleanup from the finalizer thread.\n"
                "    if (clock != null)\n"
                "    {\n"
                "      clock.Dispose();\n"
                "      clock = null;\n"
                "    }\n",
                "    // U2F-LOCAL-PATCH: avoid native cleanup from the finalizer thread.\n"
                "    lock (clockMutex)\n"
                "    {\n"
                "      if (clock != null)\n"
                "      {\n"
                "        clock.Dispose();\n"
                "        clock = null;\n"
                "      }\n"
                "    }\n",
                1,
            )

        if name == "ROS2TimeSource.cs" and "return true;" not in text:
            text = text.replace(
                "    TimeUtils.TimeFromTotalSeconds(clock.Now.Seconds, out seconds, out nanoseconds);\n"
                "  }\n\n"
                "  public void Dispose()",
                "    TimeUtils.TimeFromTotalSeconds(clock.Now.Seconds, out seconds, out nanoseconds);\n"
                "    return true;\n"
                "  }\n\n"
                "  public void Dispose()",
                1,
            )
        elif name == "ROS2ScalableTimeSource.cs" and "return true;" not in text:
            text = text.replace(
                "      TimeUtils.TimeFromTotalSeconds(lastReadingSecs + initialTime, out seconds, out nanoseconds);\n"
                "    }\n"
                "  }\n\n"
                "  private void RefreshUnityTimeCache()",
                "      TimeUtils.TimeFromTotalSeconds(lastReadingSecs + initialTime, out seconds, out nanoseconds);\n"
                "    }\n"
                "    return true;\n"
                "  }\n\n"
                "  private void RefreshUnityTimeCache()",
                1,
            )

        if "return false;" not in text or "return true;" not in text:
            raise ValueError(f"{name} time-source bool contract patch did not apply.")
        write_text(source, text)


def write_package_files(paths: BuildPaths, inventory: dict[str, object], artifact: RuntimeArtifact) -> None:
    """Write package metadata, docs, notices, and support manifests."""
    write_json(paths.package / "package.json", package_json())
    write_text(paths.package / "README.md", readme_text(artifact))
    shutil.copyfile(UPSTREAM_LICENSE, paths.package / "LICENSE")
    write_text(paths.package / "THIRD_PARTY_NOTICES.md", notices_text(inventory, artifact))
    write_json(paths.package / "RuntimeSupport" / "runtime-manifest.json", runtime_manifest(artifact))
    shutil.copyfile(paths.inventory, paths.package / "RuntimeSupport" / "r2fu-jazzy-win64-runtime-inventory.json")
    write_json(
        paths.package / "Runtime" / "Ros2ForUnity" / "Scripts" / "Unity2Foxglove.Ros2ForUnity.Runtime.JazzyWin64.asmdef",
        runtime_asmdef(),
    )


def build_package(paths: BuildPaths) -> RuntimeArtifact:
    """Build the runtime package from the runtime artifact."""
    inventory, artifact = require_inputs(paths)
    snapshot = snapshot_package_dir(paths.package)
    overlays = collect_local_patch_overlays(paths.package)
    meta_overlays = collect_meta_overlays(paths.package)
    try:
        reset_package_dir(paths.package)
        extract_runtime(paths)
        copy_supplemental_runtime_dlls(paths.package, paths.ros2_bin)
        prune_non_contract_examples(paths.package)
        patch_ros2_for_unity(paths.package)
        apply_local_patch_overlays(paths.package, overlays)
        patch_ros_time_source_contract(paths.package)
        write_package_files(paths, inventory, artifact)
        patch_deps_json_sha512(paths.package)
        apply_meta_overlays(paths.package, meta_overlays)
        write_generated_metas(paths.package)
        return artifact
    except Exception:
        restore_package_dir(paths.package, snapshot)
        raise
    finally:
        remove_package_snapshot(snapshot)


def main(argv: list[str]) -> int:
    """Run package generation from command-line arguments."""
    paths = parse_args(argv)
    try:
        artifact = build_package(paths)
    except Exception as exc:
        print(f"[FAIL] {exc}", file=sys.stderr)
        return EXIT_FAILURE
    print(f"[PASS] built {rel(paths.package)}")
    print(f"[PASS] artifact={artifact.name} sha256={artifact.sha256}")
    return EXIT_SUCCESS


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
