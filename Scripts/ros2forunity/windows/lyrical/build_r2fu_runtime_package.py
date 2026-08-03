#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Build the ROS2 For Unity Lyrical Win64 runtime Unity package from a vetted artifact.
# Usage: python Scripts/ros2forunity/windows/lyrical/build_r2fu_runtime_package.py
# Inputs: build/dist/Ros2ForUnity_lyrical_standalone_windows_x86_64.zip and compliance inventory.
# Outputs: Packages/dev.unity2foxglove.ros2forunity.runtime.lyrical.win64 package directory.

"""Build the ROS2 For Unity Lyrical Win64 runtime package prototype."""

from __future__ import annotations

import argparse
import hashlib
import inspect
import json
import os
import re
import shutil
import sys
import time
import zipfile
import xml.etree.ElementTree as ElementTree
from dataclasses import dataclass
from pathlib import Path, PurePosixPath


REPO_ROOT_PARENT_DEPTH = 4
EXIT_SUCCESS = 0
EXIT_FAILURE = 1

PACKAGE_NAME = "dev.unity2foxglove.ros2forunity.runtime.lyrical.win64"
PACKAGE_VERSION = "0.1.0-preview.1"
RUNTIME_ID = "r2fu-lyrical-win64"
ARTIFACT_NAME = "Ros2ForUnity_lyrical_standalone_windows_x86_64.zip"
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

ROOT = Path(__file__).resolve().parents[REPO_ROOT_PARENT_DEPTH]
DEFAULT_ARTIFACT = ROOT / "build" / "dist" / ARTIFACT_NAME
DEFAULT_INVENTORY = (
    ROOT
    / "Packages"
    / "dev.unity2foxglove.ros2forunity"
    / "Compliance"
    / "r2fu-lyrical-win64-runtime-inventory.json"
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

PACKAGE_CONSTANTS_BLOCK = """    private const string unity2FoxgloveRuntimePackageName = "dev.unity2foxglove.ros2forunity.runtime.lyrical.win64";
    private const string unity2FoxgloveRuntimePackageAssetPath =
        "Packages/dev.unity2foxglove.ros2forunity.runtime.lyrical.win64/Runtime/Ros2ForUnity";
"""

RMW_CONSTANT_BLOCK = """    private const string defaultRmwImplementation = "rmw_fastrtps_cpp";
    private const string zenohRmwImplementation = "rmw_zenoh_cpp";
    private const string supportedRmwImplementationsDescription = "rmw_fastrtps_cpp, rmw_zenoh_cpp";
"""

RMW_VALIDATE_BLOCK = """    private static void ValidateRmwImplementation(string rmwImpl)
    {
        if (IsSupportedRmwImplementation(rmwImpl))
        {
            return;
        }

        string errMessage =
            "ROS2 For Unity Lyrical runtime supports RMW implementations '" +
            supportedRmwImplementationsDescription + "' but initialized with '" +
            rmwImpl + "'. Ensure RMW_IMPLEMENTATION is unset or set to one of the supported values.";
        FailIntegrity(errMessage);
    }

    private static bool IsSupportedRmwImplementation(string rmwImpl)
    {
        return string.Equals(rmwImpl, defaultRmwImplementation, StringComparison.Ordinal)
            || string.Equals(rmwImpl, zenohRmwImplementation, StringComparison.Ordinal);
    }

"""


@dataclass(frozen=True)
class BuildPaths:
    """Resolved input and output paths for package generation."""

    artifact: Path
    inventory: Path
    package: Path


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
    args = parser.parse_args(argv)
    return BuildPaths(args.zip.resolve(), args.inventory.resolve(), args.package.resolve())


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

    if not paths.inventory.exists():
        raise FileNotFoundError(f"Missing runtime inventory: {paths.inventory}")
    if not UPSTREAM_LICENSE.exists():
        raise FileNotFoundError(f"Missing upstream ROS2 For Unity license: {UPSTREAM_LICENSE}")
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
    with open(windows_long_path(path), "w", encoding="utf-8", newline="\n") as stream:
        stream.write(content.rstrip() + "\n")


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
    """Correct known generated deps entries and populate DLL integrity hints."""
    plugin_root = package / "Runtime" / "Ros2ForUnity" / "Plugins"
    inventory_path = package / "RuntimeSupport" / "r2fu-lyrical-win64-runtime-inventory.json"
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
        assembly_name = deps_path.name.removesuffix(".deps.json")
        if assembly_name in {"stereo_msgs_assembly", "visualization_msgs_assembly"}:
            target = data.get("targets", {}).get(".NETStandard,Version=v2.0/", {})
            entry = target.get(f"{assembly_name}/1.0.0", {})
            dependencies = entry.get("dependencies", {})
            if "service_msgs_assembly" in dependencies:
                del dependencies["service_msgs_assembly"]
                changed = True
            if "service_msgs_assembly/0.0.0.0" in target:
                del target["service_msgs_assembly/0.0.0.0"]
                changed = True
            libraries = data.get("libraries", {})
            if "service_msgs_assembly/0.0.0.0" in libraries:
                del libraries["service_msgs_assembly/0.0.0.0"]
                changed = True

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
        target = package / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        with open(windows_long_path(target), "wb") as stream:
            stream.write(data)


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
    return "TextScriptImporter"


def generated_meta_text(path: Path, relative_path: str, is_dir: bool) -> str:
    """Return deterministic Unity .meta text for a generated path."""
    guid = deterministic_guid(relative_path)
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
    return (
        "fileFormatVersion: 2\n"
        f"guid: {guid}\n"
        f"{importer}:\n"
        "  externalObjects: {}\n"
        "  userData:\n"
        "  assetBundleName:\n"
        "  assetBundleVariant:\n"
    )


def ensure_generated_meta(package: Path, target: Path, is_dir: bool) -> None:
    """Create a deterministic .meta file when the artifact did not provide one."""
    meta = target.with_name(target.name + ".meta")
    if path_exists(meta):
        return
    relative = target.relative_to(package).as_posix()
    write_text(meta, generated_meta_text(target, relative, is_dir))


def write_generated_metas(package: Path) -> None:
    """Generate metadata for package-owned files and directories lacking upstream metadata."""
    paths = list(package.rglob("*"))
    directories = sorted((path for path in paths if path.is_dir()), key=lambda item: item.as_posix())
    files = sorted((path for path in paths if path.is_file()), key=lambda item: item.as_posix())
    for directory in directories:
        ensure_generated_meta(package, directory, is_dir=True)
    for path in files:
        if path.name.endswith(".meta") or path.name == ".gitkeep":
            continue
        ensure_generated_meta(package, path, is_dir=False)


def package_json() -> dict[str, object]:
    """Return the Unity package manifest."""
    return {
        "name": PACKAGE_NAME,
        "version": PACKAGE_VERSION,
        "displayName": "Unity2Foxglove ROS2 For Unity Runtime - Lyrical Win64",
        "license": "Apache-2.0",
        "unity": "6000.0",
        "description": "Optional Lyrical Windows x64 runtime package for Unity2Foxglove ROS2 For Unity integration.",
        "keywords": [
            "unity2foxglove",
            "ros2",
            "ros2-for-unity",
            "lyrical",
            "win64",
        ],
        "unity2foxgloveConflicts": [
            "dev.unity2foxglove.ros2forunity.runtime.humble.win64",
            "dev.unity2foxglove.ros2forunity.runtime.jazzy.win64",
        ],
        "dependencies": {},
        "author": {"name": "Unity2Foxglove"},
    }


def runtime_manifest(artifact: RuntimeArtifact) -> dict[str, object]:
    """Return the runtime package manifest."""
    return {
        "schemaVersion": 1,
        "runtimeId": RUNTIME_ID,
        "packageName": PACKAGE_NAME,
        "packageVersion": PACKAGE_VERSION,
        "rosDistro": "lyrical",
        "platform": "win64",
        "unityPlatform": "Windows",
        "architecture": "x86_64",
        "buildType": "standalone",
        "rmwImplementation": DEFAULT_RMW_IMPLEMENTATION,
        "defaultRmwImplementation": DEFAULT_RMW_IMPLEMENTATION,
        "supportedRmwImplementations": list(SUPPORTED_RMW_IMPLEMENTATIONS),
        "communicationModes": [
            {
                "id": "fastdds",
                "displayName": "FastDDS (default)",
                "rmwImplementation": DEFAULT_RMW_IMPLEMENTATION,
                "default": True,
            },
            {
                "id": "zenoh",
                "displayName": "Zenoh",
                "rmwImplementation": ZENOH_RMW_IMPLEMENTATION,
                "default": False,
            },
        ],
        "artifactName": artifact.name,
        "artifactSha256": artifact.sha256,
        "artifactSize": artifact.size,
        "inventoryFile": "RuntimeSupport/r2fu-lyrical-win64-runtime-inventory.json",
        "inventoryFileCount": artifact.inventory_file_count,
        "runtimeRoot": "Runtime/Ros2ForUnity",
        "pluginPath": "Runtime/Ros2ForUnity/Plugins/Windows/x86_64",
        "sourceBasis": "Local Lyrical rebuild from RobotecAI ROS2 For Unity and ros2cs sources with Windows ROS2 Lyrical dependency closure",
        "supportLevel": "Supported",
        "distributionLevel": "Prototype",
        "activeRuntimePolicy": "one_runtime_package_per_project",
        "criticalRuntimeFiles": list(CRITICAL_RUNTIME_FILES),
        "packagePathPatch": {
            "modifiedFile": "Runtime/Ros2ForUnity/Scripts/ROS2ForUnity.cs",
            "reason": "Resolve the runtime root from this Unity package when Assets/Ros2ForUnity is absent.",
            "keepsAssetFolderFallback": True,
        },
        "freshProjectAcceptance": "deferred_to_install_acceptance",
    }


def readme_text(artifact: RuntimeArtifact) -> str:
    """Return the runtime package README."""
    return f"""# Unity2Foxglove ROS2 For Unity Runtime - Lyrical Win64

This package is an optional Windows x64 runtime for the Unity2Foxglove ROS2 For Unity integration. It carries the ROS2 For Unity runtime files, generated message assemblies, native ROS2 Lyrical DLLs, Fast DDS/RMW files, optional Zenoh RMW files, ros2cs files, metadata, inventory, and notices.

## Package Role

Install this package when a Unity project needs to run as a ROS2 node through ROS2 For Unity on Windows x64.

This package is independent from `dev.unity2foxglove.sdk` and can import by itself. It does not provide the high-level Unity2Foxglove facade or samples by itself; those live in `dev.unity2foxglove.ros2forunity`.

Recommended combinations:

- `dev.unity2foxglove.ros2forunity.runtime.lyrical.win64` alone: imports runtime files, manifest, notices, and diagnostics.
- `dev.unity2foxglove.ros2forunity` plus this runtime package: enables adapter-backed ROS2 publish/subscribe.
- `dev.unity2foxglove.sdk` plus adapter plus this runtime package: enables the combined Unity2Foxglove workflow.

The runtime package intentionally declares no UPM dependency on the facade package. It is a binary/runtime payload that must remain importable for diagnostics and artifact validation even when the adapter facade is not installed.

## One Runtime Policy

Install only one `dev.unity2foxglove.ros2forunity.runtime.*` package in a Unity project. Multiple ROS2 runtime packages can load conflicting native DLLs or generated message assemblies.

Do not import the old `Assets/Ros2ForUnity` asset folder and this package in the same project. Use either an external asset-folder runtime or this package runtime.

The script assembly is intentionally named `Unity2Foxglove.Ros2ForUnity.Runtime` across all distro runtime packages. The adapter package references that stable assembly name, while the one-runtime policy and package conflict metadata prevent multiple distro runtimes from being active in the same Unity project.

## Runtime Identity

- ROS distro: Lyrical
- Platform: Windows x64
- Build type: standalone
- Default RMW implementation: `rmw_fastrtps_cpp`
- Supported RMW implementations: `rmw_fastrtps_cpp`, `rmw_zenoh_cpp`
- Runtime id: `r2fu-lyrical-win64`
- Artifact source: `{artifact.name}`
- SHA-256: `{artifact.sha256}`

The runtime manifest is `RuntimeSupport/runtime-manifest.json`. The file inventory is `RuntimeSupport/r2fu-lyrical-win64-runtime-inventory.json`.

## Package Path Patch

The bundled `ROS2ForUnity.cs` keeps the upstream `Assets/Ros2ForUnity` lookup and adds a package-path fallback so Unity Editor can load this runtime from:

```text
Packages/dev.unity2foxglove.ros2forunity.runtime.lyrical.win64/Runtime/Ros2ForUnity
```

This patch is limited to locating runtime files from a Unity package. It does not change ROS2 For Unity node, publisher, subscriber, or DDS behavior.

## Network Acceptance Notes

WSL2 NAT can hide DDS discovery and should be treated as diagnostic-only for Windows package acceptance. Configure Windows Defender Firewall allow rules for Fast DDS UDP ports, then prefer Windows ROS2 Lyrical or a real remote Linux topology for final external-graph acceptance. Zenoh mode is Lyrical-only and requires selecting `rmw_zenoh_cpp` before ROS2 For Unity initializes, plus a reachable Zenoh router for routed topologies. Zenoh config files are mirrored under `Plugins/Windows/x86_64/share` for native runtime closure and `StreamingAssets/Ros2ForUnity/share` for Unity player access; package validation requires the mirrored files to stay byte-identical.

The bundled Zenoh router config is a development profile. It listens on `tcp/[::]:7447`, exits if port `7447` is already bound, has no authentication or ACLs, enables read-only Zenoh adminspace for topology inspection, and keeps high pending/session limits for large ROS2 graph startup bursts. Use it only on trusted lab networks. For CI, shared office networks, or production-like deployments, copy the router config to a localhost-only or ACL-protected profile with lower connection limits and disabled adminspace.

## Support Boundary

This is a prototype runtime package. Fresh-project install acceptance and public release readiness are separate gates. Linux, macOS, Jazzy, Humble, and Ubuntu Lyrical runtime packages are not included here.

RobotecAI states that ROS2 For Unity is officially supported for AWSIM/Autoware users and that the Robotec team cannot support and maintain the project for the general community. Unity2Foxglove-specific packaging and support belong to Unity2Foxglove, not RobotecAI.
"""


def notices_text(inventory: dict[str, object], artifact: RuntimeArtifact) -> str:
    """Return third-party notices for the runtime package."""
    file_count = inventory.get("fileCount", artifact.inventory_file_count)
    return f"""# Third-Party Notices

This runtime package redistributes a locally rebuilt ROS2 For Unity Lyrical Windows x64 runtime payload.

Unity2Foxglove does not claim authorship of RobotecAI ROS2 For Unity, ros2cs, generated ROS2 message assemblies, generated native message support libraries, ROS2 Lyrical native libraries, Fast DDS, Fast CDR, RMW FastRTPS, or transitive runtime DLLs.

## Runtime Artifact

| Field | Value |
|---|---|
| Artifact | `{artifact.name}` |
| Runtime id | `r2fu-lyrical-win64` |
| ROS distro | `lyrical` |
| Platform | Windows x64 |
| Build type | standalone |
| Default RMW | `rmw_fastrtps_cpp` |
| Supported RMW | `rmw_fastrtps_cpp`, `rmw_zenoh_cpp` |
| SHA-256 | `{artifact.sha256}` |
| Inventory file count | `{file_count}` |

## Known Upstream Components

| Component | Relationship |
|---|---|
| RobotecAI ROS2 For Unity | Unity integration surface for ROS2 node behavior |
| ros2cs | ROS2 C# binding stack used by ROS2 For Unity |
| ROS2 Lyrical native runtime | `rcl`, `rcutils`, `rmw`, message type support, and related runtime DLLs |
| Fast DDS / Fast CDR | DDS and CDR runtime dependency family used by the default FastRTPS RMW path |
| RMW FastRTPS | `rmw_fastrtps_cpp` default runtime path used by this Windows artifact |
| RMW Zenoh | `rmw_zenoh_cpp` optional runtime path for Lyrical-only routed communication |
| Generated message support | Managed message assemblies plus native ROSIDL/type-support DLLs |

## Critical Runtime Closure

The package includes the transitive runtime DLLs required for Unity to load `rcl.dll`, including:

```text
rcl.dll
yaml.dll
spdlog.dll
fmt.dll
fastdds-3.6.dll
rosidl_buffer_backend_registry.dll
rosidl_dynamic_typesupport_fastrtps.dll
rmw_zenoh_cpp.dll
zenohc.dll
rosgraph_msgs_assembly.dll
```

If these closure DLLs are removed, Unity can report `UnsatisfiedLinkError: rcl.dll` even when `rcl.dll` itself is present.

## Redistribution Caveats

- This package is a prototype until fresh-project acceptance passes.
- The inventory is an engineering inventory generated from the local runtime artifact, not a complete legal audit.
- Public release should refresh transitive license attribution before registry or binary distribution.
- WSL2 NAT can hide DDS discovery and should be treated as diagnostic-only for Windows package acceptance. Configure Windows Defender Firewall allow rules for Fast DDS UDP ports, then prefer Windows ROS2 Lyrical or a real remote Linux topology for final external-graph acceptance.

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
            if info.is_dir() or (
                not name.startswith("Ros2ForUnity/")
                and not name.startswith("StreamingAssets/")
            ):
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


def normalize_ros2cs_plugin_roots(package: Path) -> None:
    """Replace artifact-producer plugin roots with package-relative metadata."""
    metadata_files = (
        package / "Runtime" / "Ros2ForUnity" / "Plugins" / "metadata_ros2cs.xml",
        package / "Runtime" / "Ros2ForUnity" / "Plugins" / "Windows" / "x86_64" / "metadata_ros2cs.xml",
    )
    for path in metadata_files:
        text = path.read_text(encoding="utf-8", errors="strict")
        try:
            root = ElementTree.fromstring(text)
        except ElementTree.ParseError as error:
            raise ValueError(f"Invalid ros2cs metadata XML: {path}") from error
        plugins = root.find("plugins") if root.tag == "ros2cs" else None
        if plugins is None or plugins.get("root") is None:
            raise ValueError(f"Missing ros2cs plugin root: {path}")
        normalized, replacements = re.subn(
            r'(<plugins\b[^>]*\broot=")[^"]*(")',
            r"\g<1>.\2",
            text,
            count=1,
        )
        if replacements != 1:
            raise ValueError(f"Ambiguous ros2cs plugin root: {path}")
        write_text(path, normalized)


def safe_runtime_zip_relative_path(name: str) -> Path:
    """Return the path under Runtime/Ros2ForUnity for a trusted zip entry name."""
    zip_path = PurePosixPath(name)
    if zip_path.is_absolute():
        raise ValueError(f"Rejected absolute runtime zip entry: {name}")

    parts = zip_path.parts
    if len(parts) < 2 or parts[0] not in ("Ros2ForUnity", "StreamingAssets"):
        raise ValueError(f"Rejected unexpected runtime zip entry: {name}")
    if any(part in ("", ".", "..") for part in parts):
        raise ValueError(f"Rejected unsafe runtime zip entry: {name}")

    if parts[0] == "StreamingAssets":
        return Path(*parts)
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
        text = patch_rmw_guard(text)
        text = patch_standalone_environment_isolation(text)
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
    text = patch_rmw_guard(text)
    text = patch_standalone_environment_isolation(text)
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


def patch_rmw_guard(text: str) -> str:
    """Patch ROS2ForUnity.cs to fail fast unless an explicitly supported RMW is active."""
    legacy_constant = '    private const string expectedRmwImplementation = "rmw_fastrtps_cpp";\n'
    if legacy_constant in text:
        text = text.replace(legacy_constant, RMW_CONSTANT_BLOCK, 1)

    if "defaultRmwImplementation" not in text:
        text = text.replace(
            "    private static ConsoleCancelEventHandler consoleCancelHandler;\n",
            "    private static ConsoleCancelEventHandler consoleCancelHandler;\n" + RMW_CONSTANT_BLOCK,
            1,
        )

    marker = "    private void RegisterCtrlCHandler()\n"
    if marker not in text:
        raise ValueError("Could not find RegisterCtrlCHandler marker for RMW guard patch.")

    if "ValidateRmwImplementation" in text:
        method_pattern = (
            r"    private static void ValidateRmwImplementation\(string rmwImpl\)\n"
            r"    \{\n.*?\n"
            r"    \}\n\n"
            r"(?:    private static bool IsSupportedRmwImplementation\(string rmwImpl\)\n"
            r"    \{\n.*?\n"
            r"    \}\n\n)?"
        )
        text, replacements = re.subn(method_pattern, RMW_VALIDATE_BLOCK, text, count=1, flags=re.S)
        if replacements != 1:
            raise ValueError("Could not replace existing ValidateRmwImplementation block.")
    else:
        text = text.replace(marker, RMW_VALIDATE_BLOCK + marker, 1)

    if "ValidateRmwImplementation(rmwImpl);" not in text:
        marker = "            string rmwImpl = Ros2cs.GetRMWImplementation();\n"
        if marker not in text:
            raise ValueError("Could not find RMW implementation read for RMW guard patch.")
        text = text.replace(marker, marker + "            ValidateRmwImplementation(rmwImpl);\n", 1)
    return text


def patch_standalone_environment_isolation(text: str) -> str:
    """Patch standalone startup so sourced ROS2 shells do not poison Unity."""
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
    text = text.replace(old_check_signature, new_check_signature, 1)

    old_prefix = '''        string currentPrefixPath = Environment.GetEnvironmentVariable("AMENT_PREFIX_PATH");
        char envPathSep = GetOS() == Platform.Windows ? ';' : ':';

        if (String.IsNullOrEmpty(currentPrefixPath))
        {
            SetProcessEnvironmentVariable("AMENT_PREFIX_PATH", prefixPath);
            return;
        }

        StringComparison comparison = GetOS() == Platform.Windows
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        foreach (string entry in currentPrefixPath.Split(envPathSep))
        {
            if (String.Equals(entry.Trim(), prefixPath, comparison))
            {
                return;
            }
        }

        SetProcessEnvironmentVariable("AMENT_PREFIX_PATH", prefixPath + envPathSep + currentPrefixPath);
'''
    new_prefix = '''        // U2F-LOCAL-PATCH: standalone runtime must not inherit a sourced ROS2 workspace.
        SetProcessEnvironmentVariable("AMENT_PREFIX_PATH", prefixPath);
'''
    text = text.replace(old_prefix, new_prefix)

    old_rmw = '''        if (String.IsNullOrEmpty(Environment.GetEnvironmentVariable("RMW_IMPLEMENTATION")))
        {
            SetProcessEnvironmentVariable("RMW_IMPLEMENTATION", "rmw_fastrtps_cpp");
        }
'''
    new_rmw = '''        // U2F-LOCAL-PATCH: standalone runtime owns its RMW selection while allowing Lyrical Zenoh.
        string requestedRmwImplementation = Environment.GetEnvironmentVariable("RMW_IMPLEMENTATION");
        string selectedRmwImplementation = IsSupportedRmwImplementation(requestedRmwImplementation)
            ? requestedRmwImplementation
            : defaultRmwImplementation;
        SetProcessEnvironmentVariable("RMW_IMPLEMENTATION", selectedRmwImplementation);
'''
    text = text.replace(old_rmw, new_rmw)

    old_owned_rmw = '''        // U2F-LOCAL-PATCH: standalone runtime owns its RMW selection.
        SetProcessEnvironmentVariable("RMW_IMPLEMENTATION", "rmw_fastrtps_cpp");
'''
    text = text.replace(old_owned_rmw, new_rmw)

    text = text.replace(
        '''    [DllImport("ucrtbase.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    private static extern int _wputenv_s(string name, string value);
''',
        '''#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    [DllImport("ucrtbase.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    private static extern int _wputenv_s(string name, string value);
#endif
''',
        1,
    )
    text = text.replace(
        '''        if (GetOS() == Platform.Windows)
        {
            int result = _wputenv_s(name, value);
            if (result != 0)
            {
                throw new InvalidOperationException(
                    "Failed to set Windows CRT environment variable '" + name + "' (ucrtbase _wputenv_s returned " + result + ")");
            }
        }
''',
        '''        if (GetOS() == Platform.Windows)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            int result = _wputenv_s(name, value);
            if (result != 0)
            {
                throw new InvalidOperationException(
                    "Failed to set Windows CRT environment variable '" + name + "' (ucrtbase _wputenv_s returned " + result + ")");
            }
#else
            throw new PlatformNotSupportedException("Windows CRT environment updates require a Windows Unity build target.");
#endif
        }
''',
        1,
    )
    if "internal static void PrewarmUnityPaths()" not in text:
        text = text.replace(
            "\n    public static string GetRos2ForUnityPath()\n",
            "\n    internal static void PrewarmUnityPaths()\n"
            "    {\n"
            "        _ = GetRos2ForUnityPath();\n"
            "        _ = GetPluginPath();\n"
            "    }\n\n"
            "    public static string GetRos2ForUnityPath()\n",
            1,
        )

    old_distro = '''        if (String.IsNullOrEmpty(Environment.GetEnvironmentVariable("ROS_DISTRO")))
        {
            SetProcessEnvironmentVariable("ROS_DISTRO", ros2Codename);
        }
'''
    new_distro = '''        // U2F-LOCAL-PATCH: standalone runtime owns ROS_DISTRO even when Unity was launched from another ROS shell.
        SetProcessEnvironmentVariable("ROS_DISTRO", ros2Codename);
'''
    text = text.replace(old_distro, new_distro)
    if "WarnIfStandaloneRosDistroOverride" not in text:
        text = text.replace(
            "\n    private static void SetStandaloneRcutilsConsoleMode()\n",
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
            "    private static void SetStandaloneRcutilsConsoleMode()\n",
            1,
        )

    old_integrity = '''        if (IsStandalone() && !string.IsNullOrEmpty(ros2SourcedCodename)) {
            string errMessage = "You should not source ROS2 in 'ros2-for-unity' standalone build.";
            FailIntegrity(errMessage);
        }
'''
    text = text.replace(old_integrity, "")

    startup_marker = "            // Load metadata\n            LoadMetadata();\n"
    startup_patch = '''            // Load metadata
            LoadMetadata();
            string sourcedRosDistroBeforeStandalonePatch = GetROSVersionSourced();
            if (IsStandalone())
            {
                string packagedRos2Version = GetMetadataValue(ros2csMetadata, "/ros2cs/ros2");
                SetStandaloneRosDistro(packagedRos2Version);
                SetStandalonePrefixPath();
                SetStandaloneRmwImplementation();
                SetStandaloneRcutilsConsoleMode();
            }
'''
    if "sourcedRosDistroBeforeStandalonePatch" not in text:
        if "packagedRos2Version = GetMetadataValue" not in text and startup_marker in text:
            text = text.replace(startup_marker, startup_patch, 1)
        else:
            text = text.replace(
                startup_marker,
                startup_marker + "            string sourcedRosDistroBeforeStandalonePatch = GetROSVersionSourced();\n",
                1,
            )
    if "SetStandalonePrefixPath();" not in text or "SetStandaloneRmwImplementation();" not in text:
        raise ValueError("Standalone environment isolation patch is missing required setup calls.")
    text = text.replace(
        '            string standalone = IsStandalone() ? "standalone" : "non-standalone";\n',
        '            bool standaloneBuild = IsStandalone();\n'
        '            string standalone = standaloneBuild ? "standalone" : "non-standalone";\n',
        1,
    )

    text = text.replace(
        "            CheckIntegrity();\n",
        "            WarnIfStandaloneRosDistroOverride(sourcedRosDistroBeforeStandalonePatch, currentRos2Version);\n"
        "            CheckIntegrity(standaloneBuild ? null : sourcedRosDistroBeforeStandalonePatch);\n",
        1,
    )
    text = text.replace(
        "            CheckIntegrity(" + "sourcedRosDistroBeforeStandalonePatch);\n",
        "            WarnIfStandaloneRosDistroOverride(sourcedRosDistroBeforeStandalonePatch, currentRos2Version);\n"
        "            CheckIntegrity(standaloneBuild ? null : sourcedRosDistroBeforeStandalonePatch);\n",
        1,
    )
    text = text.replace(
        "                if (IsStandalone())\n",
        "                if (standaloneBuild)\n",
        1,
    )
    text = text.replace(
        '''                if (standaloneBuild)
                {
                    SetStandaloneRosDistro(currentRos2Version);
                    SetStandalonePrefixPath();
                    SetStandaloneRmwImplementation();
                    SetStandaloneRcutilsConsoleMode();
                }
''',
        "",
        1,
    )

    return text


def patch_component_main_thread_prewarm(package: Path) -> None:
    """Patch ROS2UnityComponent so Unity API backed Lazy paths are warmed on the main thread."""
    component = package / "Runtime" / "Ros2ForUnity" / "Scripts" / "ROS2UnityComponent.cs"
    text = component.read_text(encoding="utf-8")
    if "ROS2ForUnity.PrewarmUnityPaths();" not in text:
        text = text.replace(
            "    private readonly object mutex = new object();\n    private double spinTimeout = 0.0001;\n\n",
            "    private readonly object mutex = new object();\n"
            "    private double spinTimeout = 0.0001;\n\n"
            "    private void Awake()\n"
            "    {\n"
            "        ROS2ForUnity.PrewarmUnityPaths();\n"
            "    }\n\n"
            "    /// <summary>\n"
            "    /// Checks ROS2 availability. The first call must happen on Unity's main thread,\n"
            "    /// or after Awake has prewarmed Unity API backed package paths.\n"
            "    /// </summary>\n",
            1,
        )
    text = text.replace("            runtimeShutdownRequested = false;\n", "", 1)
    component.write_text(text, encoding="utf-8", newline="\n")


def patch_ros_time_source_contract(package: Path) -> None:
    """Patch ROS2 time sources for the bool-returning ITimeSource contract."""
    time_dir = package / "Runtime" / "Ros2ForUnity" / "Scripts" / "Time"
    interface_file = time_dir / "ITimeSource.cs"
    if interface_file.exists():
        interface_text = interface_file.read_text(encoding="utf-8")
        interface_text = interface_text.replace(
            "/// Interface for acquiring ROS-compatible timestamp fields from a concrete time source.",
            "/// Interface for acquiring time.",
            1,
        )
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


def patch_zenoh_router_config_notes(package: Path) -> None:
    """Document the bundled Zenoh router config as a trusted-lab development profile."""
    runtime_root = package / "Runtime" / "Ros2ForUnity"
    config_relatives = (
        Path("Plugins/Windows/x86_64/share/rmw_zenoh_cpp/config/DEFAULT_RMW_ZENOH_ROUTER_CONFIG.json5"),
        Path("StreamingAssets/Ros2ForUnity/share/rmw_zenoh_cpp/config/DEFAULT_RMW_ZENOH_ROUTER_CONFIG.json5"),
    )
    header_note = (
        "/// Unity2Foxglove package note: this is the upstream ROS2 Zenoh router development profile.\n"
        "/// It listens on tcp/[::]:7447 without authentication or ACLs so routed ROS2 peers on\n"
        "/// the local network can connect during lab acceptance. Do not use this profile on an\n"
        "/// untrusted network; copy it to a localhost-only or ACL-protected deployment profile.\n"
    )
    limit_note = (
        "      /// Unity2Foxglove package note: this high development default is unsuitable for\n"
        "      /// untrusted networks; use a hardened deployment profile with lower limits.\n"
    )

    for relative in config_relatives:
        path = runtime_root / relative
        if not path.exists():
            continue

        text = path.read_text(encoding="utf-8")
        if "Unity2Foxglove package note: this is the upstream ROS2 Zenoh router development profile." not in text:
            marker = "/// Note that the values here are correctly typed, but may not be sensible, so copying this file to change only the parts that matter to you is not good practice.\n"
            if marker not in text:
                raise ValueError(f"Could not find Zenoh router header in {relative.as_posix()}.")
            text = text.replace(marker, marker + header_note, 1)

        if "Unity2Foxglove package note: this high development default is unsuitable for" not in text:
            text = text.replace(
                "      /// ROS setting: increase the value to support a large number of Nodes starting all together\n"
                "      accept_pending: 10000,\n",
                "      /// ROS setting: increase the value to support a large number of Nodes starting all together.\n"
                + limit_note +
                "      accept_pending: 10000,\n",
                1,
            )
            text = text.replace(
                "      /// ROS setting: increase the value to support a large number of Nodes starting all together\n"
                "      max_sessions: 10000,\n",
                "      /// ROS setting: increase the value to support a large number of Nodes starting all together.\n"
                + limit_note +
                "      max_sessions: 10000,\n",
                1,
            )

        write_text(path, text)


def patch_zenoh_session_config_safety(package: Path) -> None:
    """Reapply bounded, non-fatal Zenoh session defaults to both packaged mirrors."""
    runtime_root = package / "Runtime" / "Ros2ForUnity"
    config_relatives = (
        Path("Plugins/Windows/x86_64/share/rmw_zenoh_cpp/config/DEFAULT_RMW_ZENOH_SESSION_CONFIG.json5"),
        Path("StreamingAssets/Ros2ForUnity/share/rmw_zenoh_cpp/config/DEFAULT_RMW_ZENOH_SESSION_CONFIG.json5"),
    )
    old_rx_block = (
        "        /// Maximum size of the defragmentation buffer at receiver end.\n"
        "        /// Fragmented messages that are larger than the configured size will be dropped.\n"
        "        /// The default value is 1GiB. This would work in most scenarios.\n"
        "        /// NOTE: reduce the value if you are operating on a memory constrained device.\n"
        "        max_message_size: 1073741824,\n"
    )
    new_rx_block = (
        "        /// Maximum size of the defragmentation buffer at receiver end.\n"
        "        /// Fragmented messages that are larger than the configured size will be dropped.\n"
        "        /// Unity2Foxglove package safety: cap the receiver buffer to 128MiB\n"
        "        /// so Unity player processes do not reserve the upstream 1GiB worst case.\n"
        "        max_message_size: 134217728,\n"
    )
    old_adminspace_block = (
        "  adminspace: {\n"
        "    /// Enables the admin space\n"
        "    enabled: true,\n"
        "    /// read and/or write permissions on the admin space\n"
        "    permissions: {\n"
        "      read: true,\n"
        "      write: false,\n"
        "    },\n"
        "  },\n"
    )
    new_adminspace_block = (
        "  adminspace: {\n"
        "    /// Enables the admin space\n"
        "    enabled: false,\n"
        "    /// read and/or write permissions on the admin space\n"
        "    permissions: {\n"
        "      read: false,\n"
        "      write: false,\n"
        "    },\n"
        "  },\n"
    )

    for relative in config_relatives:
        path = runtime_root / relative
        if not path.exists():
            raise ValueError(f"Missing Zenoh session config: {relative.as_posix()}")

        text = path.read_text(encoding="utf-8")
        if text.count("    exit_on_failure: true,\n") != 1:
            raise ValueError(f"Could not find the Zenoh listen exit policy in {relative.as_posix()}.")
        if text.count(old_rx_block) != 1:
            raise ValueError(f"Could not find the Zenoh RX buffer policy in {relative.as_posix()}.")
        if text.count(old_adminspace_block) != 1:
            raise ValueError(f"Could not find the Zenoh adminspace policy in {relative.as_posix()}.")

        text = text.replace(
            "    exit_on_failure: true,\n",
            "    /// Unity2Foxglove package safety: a busy local router port is non-fatal.\n"
            "    exit_on_failure: false,\n",
            1,
        )
        text = text.replace(old_rx_block, new_rx_block, 1)
        text = text.replace(old_adminspace_block, new_adminspace_block, 1)
        write_text(path, text)


def update_zenoh_config_inventory_hashes(package: Path) -> None:
    """Refresh inventory hashes for package-patched Zenoh config mirrors."""
    inventory_path = package / "RuntimeSupport" / "r2fu-lyrical-win64-runtime-inventory.json"
    if not inventory_path.exists():
        return

    data = json.loads(inventory_path.read_text(encoding="utf-8"))
    changed = False
    for item in data.get("files", []):
        relative = str(item.get("path", ""))
        if "DEFAULT_RMW_ZENOH" not in relative:
            continue

        source_relative = "Ros2ForUnity/" + relative if relative.startswith("StreamingAssets/") else relative
        source_path = package / "Runtime" / source_relative
        if not source_path.exists():
            continue

        item["sha256"] = sha256_file(source_path)
        item["size"] = source_path.stat().st_size
        changed = True

    if changed:
        write_json(inventory_path, data)


def write_package_files(paths: BuildPaths, inventory: dict[str, object], artifact: RuntimeArtifact) -> None:
    """Write package metadata, docs, notices, and support manifests."""
    write_json(paths.package / "package.json", package_json())
    write_text(paths.package / "README.md", readme_text(artifact))
    shutil.copyfile(UPSTREAM_LICENSE, paths.package / "LICENSE")
    write_text(paths.package / "THIRD_PARTY_NOTICES.md", notices_text(inventory, artifact))
    write_json(paths.package / "RuntimeSupport" / "runtime-manifest.json", runtime_manifest(artifact))
    shutil.copyfile(paths.inventory, paths.package / "RuntimeSupport" / "r2fu-lyrical-win64-runtime-inventory.json")
    update_zenoh_config_inventory_hashes(paths.package)
    write_json(
        paths.package / "Runtime" / "Ros2ForUnity" / "Scripts" / "Unity2Foxglove.Ros2ForUnity.Runtime.LyricalWin64.asmdef",
        runtime_asmdef(),
    )


def validate_ros2cs_metadata_descriptions(package: Path) -> None:
    """Require each ros2cs metadata document to declare the Lyrical runtime."""
    metadata_files = (
        package / "Runtime" / "Ros2ForUnity" / "metadata_ros2cs.xml",
        package / "Runtime" / "Ros2ForUnity" / "Plugins" / "metadata_ros2cs.xml",
        package / "Runtime" / "Ros2ForUnity" / "Plugins" / "Windows" / "x86_64" / "metadata_ros2cs.xml",
    )
    for path in metadata_files:
        text = path.read_text(encoding="utf-8", errors="replace")
        try:
            root = ElementTree.fromstring(text)
        except ElementTree.ParseError as error:
            raise ValueError(f"Invalid ros2cs metadata XML: {path}") from error
        distro = (root.findtext("ros2") or "").strip() if root.tag == "ros2cs" else ""
        if distro != "lyrical":
            raise ValueError(f"Unexpected ros2cs distro in {path}: expected lyrical")


def build_package(paths: BuildPaths) -> None:
    """Build the runtime package from the runtime artifact."""
    inventory, artifact = require_inputs(paths)
    snapshot = snapshot_package_dir(paths.package)
    overlays = collect_local_patch_overlays(paths.package)
    meta_overlays = collect_meta_overlays(paths.package)
    snapshot_safe_to_remove = False
    try:
        reset_package_dir(paths.package)
        extract_runtime(paths)
        normalize_ros2cs_plugin_roots(paths.package)
        prune_non_contract_examples(paths.package)
        apply_local_patch_overlays(paths.package, overlays)
        patch_ros2_for_unity(paths.package)
        patch_component_main_thread_prewarm(paths.package)
        patch_ros_time_source_contract(paths.package)
        patch_zenoh_session_config_safety(paths.package)
        patch_zenoh_router_config_notes(paths.package)
        validate_ros2cs_metadata_descriptions(paths.package)
        write_package_files(paths, inventory, artifact)
        patch_deps_json_sha512(paths.package)
        apply_meta_overlays(paths.package, meta_overlays)
        write_generated_metas(paths.package)
        snapshot_safe_to_remove = True
    except Exception as generation_error:
        try:
            restore_package_dir(paths.package, snapshot)
        except Exception as rollback_error:
            snapshot_path = str(snapshot) if snapshot is not None else "<not available>"
            raise RuntimeError(
                "Runtime package generation failed "
                f"({type(generation_error).__name__}: {generation_error}); rollback also failed "
                f"({type(rollback_error).__name__}: {rollback_error}). "
                f"Rollback snapshot preserved for manual recovery: {snapshot_path}"
            ) from rollback_error
        snapshot_safe_to_remove = True
        raise
    finally:
        if snapshot_safe_to_remove:
            remove_package_snapshot(snapshot)


def main(argv: list[str]) -> int:
    """Run package generation from command-line arguments."""
    paths = parse_args(argv)
    try:
        build_package(paths)
    except Exception as exc:
        print(f"[FAIL] {exc}", file=sys.stderr)
        return EXIT_FAILURE
    print(f"[PASS] built {rel(paths.package)}")
    artifact_hash = sha256_file(paths.artifact)
    print(f"[PASS] artifact={paths.artifact.name} sha256={artifact_hash}")
    return EXIT_SUCCESS


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
