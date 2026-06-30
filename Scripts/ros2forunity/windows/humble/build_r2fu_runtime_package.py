#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Build the ROS2 For Unity Humble Win64 runtime Unity package from a vetted artifact.
# Usage: python Scripts/ros2forunity/windows/humble/build_r2fu_runtime_package.py
# Inputs: build/dist/Ros2ForUnity_humble_standalone_windows_x86_64.zip and compliance inventory.
# Outputs: Packages/dev.unity2foxglove.ros2forunity.runtime.humble.win64 package directory.

"""Build the ROS2 For Unity Humble Win64 runtime package prototype."""

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

PACKAGE_NAME = "dev.unity2foxglove.ros2forunity.runtime.humble.win64"
PACKAGE_VERSION = "0.1.0-preview.1"
RUNTIME_ID = "r2fu-humble-win64"
ARTIFACT_NAME = "Ros2ForUnity_humble_standalone_windows_x86_64.zip"

ROOT = Path(__file__).resolve().parents[REPO_ROOT_PARENT_DEPTH]
DEFAULT_ARTIFACT = ROOT / "build" / "dist" / ARTIFACT_NAME
DEFAULT_INVENTORY = (
    ROOT
    / "Packages"
    / "dev.unity2foxglove.ros2forunity"
    / "Compliance"
    / "r2fu-humble-win64-runtime-inventory.json"
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

PACKAGE_CONSTANTS_BLOCK = """    private const string unity2FoxgloveRuntimePackageName = "dev.unity2foxglove.ros2forunity.runtime.humble.win64";
    private const string unity2FoxgloveRuntimePackageAssetPath =
        "Packages/dev.unity2foxglove.ros2forunity.runtime.humble.win64/Runtime/Ros2ForUnity";
"""

RMW_CONSTANT_BLOCK = """    private const string expectedRmwImplementation = "rmw_fastrtps_cpp";
"""

RMW_VALIDATE_BLOCK = """    private static void ValidateRmwImplementation(string rmwImpl)
    {
        if (string.Equals(rmwImpl, expectedRmwImplementation, StringComparison.Ordinal))
        {
            return;
        }

        string errMessage =
            "ROS2 For Unity runtime was built for RMW implementation '" +
            expectedRmwImplementation + "' but initialized with '" + rmwImpl +
            "'. Ensure RMW_IMPLEMENTATION is unset or set to '" +
            expectedRmwImplementation + "'.";
        FailIntegrity(errMessage);
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
    inventory = json.loads(paths.inventory.read_text(encoding="utf-8"))
    if inventory.get("runtimeId") != RUNTIME_ID:
        raise ValueError(f"Unexpected inventory runtimeId: {inventory.get('runtimeId')!r}")
    if inventory.get("sha256") != artifact_hash:
        raise ValueError("Inventory sha256 does not match the runtime artifact.")
    if inventory.get("artifactSize") not in (None, artifact_size) and inventory.get("artifactSize") != artifact_size:
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
        if relative == "Runtime/Ros2ForUnity/Scripts/ROS2ForUnity.cs":
            continue
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
    paths = list(package.rglob("*"))
    existing_paths = {path.as_posix() for path in paths}
    directories = sorted((path for path in paths if path.is_dir()), key=lambda item: item.as_posix())
    files = sorted((path for path in paths if path.is_file()), key=lambda item: item.as_posix())
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
        "displayName": "Unity2Foxglove ROS2 For Unity Runtime - Humble Win64",
        "license": "Apache-2.0",
        "unity": "6000.0",
        "description": "Optional Humble Windows x64 runtime package for Unity2Foxglove ROS2 For Unity integration.",
        "keywords": [
            "unity2foxglove",
            "ros2",
            "ros2-for-unity",
            "humble",
            "win64",
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
        "rosDistro": "humble",
        "platform": "win64",
        "unityPlatform": "Windows",
        "architecture": "x86_64",
        "buildType": "standalone",
        "rmwImplementation": "rmw_fastrtps_cpp",
        "artifactName": artifact.name,
        "artifactSha256": artifact.sha256,
        "artifactSize": artifact.size,
        "inventoryFile": "RuntimeSupport/r2fu-humble-win64-runtime-inventory.json",
        "inventoryFileCount": artifact.inventory_file_count,
        "runtimeRoot": "Runtime/Ros2ForUnity",
        "pluginPath": "Runtime/Ros2ForUnity/Plugins/Windows/x86_64",
        "sourceBasis": "Local Humble rebuild from RobotecAI ROS2 For Unity and ros2cs sources with Windows ROS2 Humble dependency closure",
        "supportLevel": "Recommended",
        "distributionLevel": "Prototype",
        "activeRuntimePolicy": "one_runtime_package_per_project",
        "criticalRuntimeFiles": [
            "rcl.dll",
            "yaml.dll",
            "spdlog.dll",
        ],
        "packagePathPatch": {
            "modifiedFile": "Runtime/Ros2ForUnity/Scripts/ROS2ForUnity.cs",
            "reason": "Resolve the runtime root from this Unity package when Assets/Ros2ForUnity is absent.",
            "keepsAssetFolderFallback": True,
        },
        "freshProjectAcceptance": "deferred_to_install_acceptance",
    }


def readme_text(artifact: RuntimeArtifact) -> str:
    """Return the runtime package README."""
    return f"""# Unity2Foxglove ROS2 For Unity Runtime - Humble Win64

This package is an optional Windows x64 runtime for the Unity2Foxglove ROS2 For Unity integration. It carries the ROS2 For Unity runtime files, generated message assemblies, native ROS2 Humble DLLs, Fast DDS/RMW files, ros2cs files, metadata, inventory, and notices.

## Package Role

Install this package when a Unity project needs to run as a ROS2 node through ROS2 For Unity on Windows x64.

This package is independent from `dev.unity2foxglove.sdk` and can import by itself. It does not provide the high-level Unity2Foxglove facade or samples by itself; those live in `dev.unity2foxglove.ros2forunity`.

Recommended combinations:

- `dev.unity2foxglove.ros2forunity.runtime.humble.win64` alone: imports runtime files, manifest, notices, and diagnostics.
- `dev.unity2foxglove.ros2forunity` plus this runtime package: enables adapter-backed ROS2 publish/subscribe.
- `dev.unity2foxglove.sdk` plus adapter plus this runtime package: enables the combined Unity2Foxglove workflow.

## One Runtime Policy

Install only one `dev.unity2foxglove.ros2forunity.runtime.*` package in a Unity project. Multiple ROS2 runtime packages can load conflicting native DLLs or generated message assemblies.

Do not import the old `Assets/Ros2ForUnity` asset folder and this package in the same project. Use either an external asset-folder runtime or this package runtime.

## Runtime Identity

- ROS distro: Humble
- Platform: Windows x64
- Build type: standalone
- RMW implementation: `rmw_fastrtps_cpp`
- Runtime id: `r2fu-humble-win64`
- Artifact source: `{artifact.name}`
- SHA-256: `{artifact.sha256}`

The runtime manifest is `RuntimeSupport/runtime-manifest.json`. The file inventory is `RuntimeSupport/r2fu-humble-win64-runtime-inventory.json`.

## Package Path Patch

The bundled `ROS2ForUnity.cs` keeps the upstream `Assets/Ros2ForUnity` lookup and adds a package-path fallback so Unity Editor can load this runtime from:

```text
Packages/dev.unity2foxglove.ros2forunity.runtime.humble.win64/Runtime/Ros2ForUnity
```

This patch is limited to locating runtime files from a Unity package. It does not change ROS2 For Unity node, publisher, subscriber, or DDS behavior.

## Network Acceptance Notes

WSL2 NAT can hide DDS discovery and should be treated as diagnostic-only for Windows package acceptance. Configure Windows Defender Firewall allow rules for Fast DDS UDP ports, then prefer Windows ROS2 Humble or a real remote Linux topology for final external-graph acceptance.

## Support Boundary

This is a prototype runtime package. Fresh-project install acceptance and public release readiness are separate gates. Linux, macOS, Jazzy, and Lyrical runtime packages are not included here.

RobotecAI states that ROS2 For Unity is officially supported for AWSIM/Autoware users and that the Robotec team cannot support and maintain the project for the general community. Unity2Foxglove-specific packaging and support belong to Unity2Foxglove, not RobotecAI.
"""


def notices_text(inventory: dict[str, object], artifact: RuntimeArtifact) -> str:
    """Return third-party notices for the runtime package."""
    file_count = inventory.get("fileCount", artifact.inventory_file_count)
    return f"""# Third-Party Notices

This runtime package redistributes a locally rebuilt ROS2 For Unity Humble Windows x64 runtime payload.

Unity2Foxglove does not claim authorship of RobotecAI ROS2 For Unity, ros2cs, generated ROS2 message assemblies, generated native message support libraries, ROS2 Humble native libraries, Fast DDS, Fast CDR, RMW FastRTPS, or transitive runtime DLLs.

## Runtime Artifact

| Field | Value |
|---|---|
| Artifact | `{artifact.name}` |
| Runtime id | `r2fu-humble-win64` |
| ROS distro | `humble` |
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
| ROS2 Humble native runtime | `rcl`, `rcutils`, `rmw`, message type support, and related runtime DLLs |
| Fast DDS / Fast CDR | DDS and CDR runtime dependency family used by the FastRTPS RMW path |
| RMW FastRTPS | `rmw_fastrtps_cpp` runtime path used by the current Windows artifact |
| Generated message support | Managed message assemblies plus native ROSIDL/type-support DLLs |

## Critical Runtime Closure

The package includes the transitive runtime DLLs required for Unity to load `rcl.dll`, including:

```text
rcl.dll
yaml.dll
spdlog.dll
```

If these closure DLLs are removed, Unity can report `UnsatisfiedLinkError: rcl.dll` even when `rcl.dll` itself is present.

## Redistribution Caveats

- This package is a prototype until fresh-project acceptance passes.
- The inventory is an engineering inventory generated from the local runtime artifact, not a complete legal audit.
- Public release should refresh transitive license attribution before registry or binary distribution.
- WSL2 NAT can hide DDS discovery and should be treated as diagnostic-only for Windows package acceptance. Configure Windows Defender Firewall allow rules for Fast DDS UDP ports, then prefer Windows ROS2 Humble or a real remote Linux topology for final external-graph acceptance.

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
    text = patch_standalone_environment_isolation(text)
    if UNITY_PACKAGE_PATH_PATCH_MARKER in text:
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
    write_text(source, text)


def patch_ros2cs_logger_callback_api(text: str) -> str:
    """Patch obsolete ros2cs logger callback calls emitted by older runtime artifacts."""
    return text.replace("Ros2csLogger.setCallback", "Ros2csLogger.SetCallback")


def patch_rmw_guard(text: str) -> str:
    """Patch ROS2ForUnity.cs to fail fast when a different RMW is active."""
    if "expectedRmwImplementation" not in text:
        text = text.replace(
            "    private static ConsoleCancelEventHandler consoleCancelHandler;\n",
            "    private static ConsoleCancelEventHandler consoleCancelHandler;\n" + RMW_CONSTANT_BLOCK,
            1,
        )
    if "ValidateRmwImplementation" not in text:
        marker = "    private void RegisterCtrlCHandler()\n"
        if marker not in text:
            raise ValueError("Could not find RegisterCtrlCHandler marker for RMW guard patch.")
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
    text = text.replace(old_check_signature, new_check_signature)

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
    old_prefix_with_source = '''        string currentPrefixPath = Environment.GetEnvironmentVariable("AMENT_PREFIX_PATH");
        char envPathSep = GetOS() == Platform.Windows ? ';' : ':';

        if (String.IsNullOrEmpty(currentPrefixPath))
        {
            SetProcessEnvironmentVariable("AMENT_PREFIX_PATH", prefixPath);
            Debug.Log("AMENT_PREFIX_PATH set to: " + prefixPath + " (source: " + prefixSource + ")");
            return;
        }

        StringComparison comparison = GetOS() == Platform.Windows
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        foreach (string entry in currentPrefixPath.Split(envPathSep))
        {
            if (String.Equals(entry.Trim(), prefixPath, comparison))
            {
                Debug.Log("AMENT_PREFIX_PATH already contains: " + prefixPath + " (source: " + prefixSource + ")");
                return;
            }
        }

        SetProcessEnvironmentVariable("AMENT_PREFIX_PATH", prefixPath + envPathSep + currentPrefixPath);
        Debug.Log("AMENT_PREFIX_PATH prepended with: " + prefixPath + " (source: " + prefixSource + ")");
'''
    text = text.replace(old_prefix_with_source, new_prefix)
    if "standalone runtime must not inherit a sourced ROS2 workspace" not in text:
        prefix_start = text.find('        string currentPrefixPath = Environment.GetEnvironmentVariable("AMENT_PREFIX_PATH");')
        prefix_end = text.find("\n    }\n\n    private static void SetStandaloneRmwImplementation()", prefix_start)
        if prefix_start >= 0 and prefix_end > prefix_start:
            text = text[:prefix_start] + new_prefix.rstrip("\n") + text[prefix_end:]

    old_rmw = '''        if (String.IsNullOrEmpty(Environment.GetEnvironmentVariable("RMW_IMPLEMENTATION")))
        {
            SetProcessEnvironmentVariable("RMW_IMPLEMENTATION", "rmw_fastrtps_cpp");
        }
'''
    new_rmw = '''        // U2F-LOCAL-PATCH: standalone runtime owns its RMW selection.
        SetProcessEnvironmentVariable("RMW_IMPLEMENTATION", "rmw_fastrtps_cpp");
'''
    text = text.replace(old_rmw, new_rmw)
    old_rmw_with_comment = '''        if (String.IsNullOrEmpty(Environment.GetEnvironmentVariable("RMW_IMPLEMENTATION")))
        {
            // Fast-RTPS is the bundled standalone RMW; callers may override before ROS2ForUnity initializes.
            SetProcessEnvironmentVariable("RMW_IMPLEMENTATION", "rmw_fastrtps_cpp");
        }
'''
    text = text.replace(old_rmw_with_comment, new_rmw)
    if "standalone runtime owns its RMW selection" not in text:
        rmw_start = text.find('        if (String.IsNullOrEmpty(Environment.GetEnvironmentVariable("RMW_IMPLEMENTATION")))')
        rmw_end = text.find("\n    }\n\n    private static void SetStandaloneRosDistro", rmw_start)
        if rmw_start >= 0 and rmw_end > rmw_start:
            text = text[:rmw_start] + new_rmw.rstrip("\n") + text[rmw_end:]

    old_distro = '''        if (String.IsNullOrEmpty(Environment.GetEnvironmentVariable("ROS_DISTRO")))
        {
            SetProcessEnvironmentVariable("ROS_DISTRO", ros2Codename);
        }
'''
    new_distro = '''        // U2F-LOCAL-PATCH: hide any externally sourced ROS_DISTRO from standalone checks.
        SetProcessEnvironmentVariable("ROS_DISTRO", ros2Codename);
'''
    text = text.replace(old_distro, new_distro)

    old_integrity = '''        if (IsStandalone() && !string.IsNullOrEmpty(ros2SourcedCodename)) {
            string errMessage = "You should not source ROS2 in 'ros2-for-unity' standalone build.";
            FailIntegrity(errMessage);
        }
'''
    new_integrity = '''        if (IsStandalone()
            && !string.IsNullOrEmpty(ros2SourcedCodename)
            && ros2SourcedCodename != ros2FromRos2csMetadata) {
            string errMessage =
                "ROS2 version in standalone process environment does not match this runtime package. " +
                "Sourced: " + ros2SourcedCodename + ", packaged: " + ros2FromRos2csMetadata + ".";
            FailIntegrity(errMessage);
        }
'''
    text = text.replace(old_integrity, new_integrity)

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
    if "packagedRos2Version = GetMetadataValue" not in text and startup_marker in text:
        text = text.replace(startup_marker, startup_patch, 1)
    elif "packagedRos2Version = GetMetadataValue" in text and "sourcedRosDistroBeforeStandalonePatch" not in text:
        text = text.replace(
            "            LoadMetadata();\n            if (IsStandalone())",
            "            LoadMetadata();\n            string sourcedRosDistroBeforeStandalonePatch = GetROSVersionSourced();\n            if (IsStandalone())",
            1,
        )
    text = text.replace("            CheckIntegrity();\n", "            CheckIntegrity(sourcedRosDistroBeforeStandalonePatch);\n", 1)

    return text


def patch_ros_time_source_contract(package: Path) -> None:
    """Patch ROS2 time sources for the bool-returning ITimeSource contract."""
    time_dir = package / "Runtime" / "Ros2ForUnity" / "Scripts" / "Time"
    dotnet_time = time_dir / "DotnetTimeSource.cs"
    write_text(dotnet_time, dotnet_time.read_text(encoding="utf-8"))

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


def write_package_files(paths: BuildPaths, inventory: dict[str, object], artifact: RuntimeArtifact) -> None:
    """Write package metadata, docs, notices, and support manifests."""
    write_json(paths.package / "package.json", package_json())
    write_text(paths.package / "README.md", readme_text(artifact))
    shutil.copyfile(UPSTREAM_LICENSE, paths.package / "LICENSE")
    write_text(paths.package / "THIRD_PARTY_NOTICES.md", notices_text(inventory, artifact))
    write_json(paths.package / "RuntimeSupport" / "runtime-manifest.json", runtime_manifest(artifact))
    shutil.copyfile(paths.inventory, paths.package / "RuntimeSupport" / "r2fu-humble-win64-runtime-inventory.json")
    write_json(
        paths.package / "Runtime" / "Ros2ForUnity" / "Scripts" / "Unity2Foxglove.Ros2ForUnity.Runtime.HumbleWin64.asmdef",
        runtime_asmdef(),
    )


def build_package(paths: BuildPaths) -> None:
    """Build the runtime package from the runtime artifact."""
    inventory, artifact = require_inputs(paths)
    snapshot = snapshot_package_dir(paths.package)
    overlays = collect_local_patch_overlays(paths.package)
    meta_overlays = collect_meta_overlays(paths.package)
    try:
        reset_package_dir(paths.package)
        extract_runtime(paths)
        prune_non_contract_examples(paths.package)
        patch_ros2_for_unity(paths.package)
        apply_local_patch_overlays(paths.package, overlays)
        patch_ros_time_source_contract(paths.package)
        write_package_files(paths, inventory, artifact)
        apply_meta_overlays(paths.package, meta_overlays)
        write_generated_metas(paths.package)
    except Exception:
        restore_package_dir(paths.package, snapshot)
        raise
    finally:
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
