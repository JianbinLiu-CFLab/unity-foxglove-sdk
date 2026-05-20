#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Build the ROS2 For Unity Jazzy Win64 runtime Unity package from a vetted artifact.
# Usage: python Scripts/release/build_r2fu_runtime_package.py
# Inputs: build/dist/Ros2ForUnity_Jazzy_standalone_windows10.zip and compliance inventory.
# Outputs: Packages/dev.unity2foxglove.ros2forunity.runtime.jazzy.win64 package directory.

"""Build the ROS2 For Unity Jazzy Win64 runtime package prototype."""

from __future__ import annotations

import argparse
import hashlib
import json
import shutil
import sys
import zipfile
from dataclasses import dataclass
from pathlib import Path


REPO_ROOT_PARENT_DEPTH = 2
EXIT_SUCCESS = 0
EXIT_FAILURE = 1

PACKAGE_NAME = "dev.unity2foxglove.ros2forunity.runtime.jazzy.win64"
PACKAGE_VERSION = "0.1.0-preview.1"
RUNTIME_ID = "r2fu-jazzy-win64"
ARTIFACT_NAME = "Ros2ForUnity_Jazzy_standalone_windows10.zip"
ARTIFACT_SHA256 = "ac06054e05282b4ebd53b31ff4a48b815ebadc7f6985a5cebcbe35e01c830936"
ARTIFACT_SIZE = 16858288
INVENTORY_FILE_COUNT = 1045

ROOT = Path(__file__).resolve().parents[REPO_ROOT_PARENT_DEPTH]
DEFAULT_ARTIFACT = ROOT / "build" / "dist" / ARTIFACT_NAME
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

            DirectoryInfo dataDirectory = Directory.GetParent(appDataPath);
            if (dataDirectory != null) {
                string packagePath = Path.Combine(
                    dataDirectory.FullName,
                    "Packages",
                    "dev.unity2foxglove.ros2forunity.runtime.jazzy.win64",
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


@dataclass(frozen=True)
class BuildPaths:
    """Resolved input and output paths for package generation."""

    artifact: Path
    inventory: Path
    package: Path


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


def require_inputs(paths: BuildPaths) -> dict[str, object]:
    """Validate inputs and return the parsed runtime inventory."""
    if not paths.artifact.exists():
        raise FileNotFoundError(f"Missing runtime artifact: {paths.artifact}")
    if paths.artifact.name != ARTIFACT_NAME:
        raise ValueError(f"Unexpected artifact name: {paths.artifact.name}")
    if paths.artifact.stat().st_size != ARTIFACT_SIZE:
        raise ValueError(f"Unexpected artifact size: {paths.artifact.stat().st_size}")

    artifact_hash = sha256_file(paths.artifact)
    if artifact_hash != ARTIFACT_SHA256:
        raise ValueError(f"Unexpected artifact sha256: {artifact_hash}")

    if not paths.inventory.exists():
        raise FileNotFoundError(f"Missing runtime inventory: {paths.inventory}")
    inventory = json.loads(paths.inventory.read_text(encoding="utf-8"))
    if inventory.get("runtimeId") != RUNTIME_ID:
        raise ValueError(f"Unexpected inventory runtimeId: {inventory.get('runtimeId')!r}")
    if inventory.get("sha256") != ARTIFACT_SHA256:
        raise ValueError("Inventory sha256 does not match the runtime artifact.")
    if inventory.get("fileCount") != INVENTORY_FILE_COUNT:
        raise ValueError(f"Unexpected inventory fileCount: {inventory.get('fileCount')!r}")
    return inventory


def reset_package_dir(package: Path) -> None:
    """Delete and recreate only the expected generated runtime package directory."""
    expected_parent = (ROOT / "Packages").resolve()
    package = package.resolve()
    if package.name != PACKAGE_NAME or package.parent != expected_parent:
        raise ValueError(f"Refusing to reset unexpected package path: {package}")
    if package.exists():
        shutil.rmtree(package)
    package.mkdir(parents=True)


def write_text(path: Path, content: str) -> None:
    """Write UTF-8 text with a trailing newline."""
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(content.rstrip() + "\n", encoding="utf-8")


def write_json(path: Path, data: dict[str, object]) -> None:
    """Write JSON with stable formatting."""
    write_text(path, json.dumps(data, indent=2, ensure_ascii=False))


def deterministic_guid(relative_path: str) -> str:
    """Return a deterministic Unity GUID for generated metadata."""
    seed = f"{PACKAGE_NAME}:{relative_path.replace(chr(92), '/')}"
    return hashlib.md5(seed.encode("utf-8")).hexdigest()


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
    if meta.exists():
        return
    relative = target.relative_to(package).as_posix()
    write_text(meta, generated_meta_text(target, relative, is_dir))


def write_generated_metas(package: Path) -> None:
    """Generate metadata for package-owned files and directories lacking upstream metadata."""
    package_owned_dirs = [
        package / "RuntimeSupport",
        package / "Runtime",
        package / "Runtime" / "Ros2ForUnity",
    ]
    package_owned_files = [
        package / "package.json",
        package / "README.md",
        package / "LICENSE",
        package / "THIRD_PARTY_NOTICES.md",
        package / "RuntimeSupport" / "runtime-manifest.json",
        package / "RuntimeSupport" / "r2fu-jazzy-win64-runtime-inventory.json",
    ]
    for directory in package_owned_dirs:
        ensure_generated_meta(package, directory, is_dir=True)
    for path in package_owned_files:
        ensure_generated_meta(package, path, is_dir=False)


def package_json() -> dict[str, object]:
    """Return the Unity package manifest."""
    return {
        "name": PACKAGE_NAME,
        "version": PACKAGE_VERSION,
        "displayName": "Unity2Foxglove ROS2 For Unity Runtime - Jazzy Win64",
        "license": "Apache-2.0",
        "unity": "6000.0",
        "description": "Optional Jazzy Windows x64 runtime package for Unity2Foxglove ROS2 For Unity integration.",
        "keywords": [
            "unity2foxglove",
            "ros2",
            "ros2-for-unity",
            "jazzy",
            "win64",
        ],
        "author": {"name": "Unity2Foxglove"},
    }


def runtime_manifest() -> dict[str, object]:
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
        "artifactName": ARTIFACT_NAME,
        "artifactSha256": ARTIFACT_SHA256,
        "artifactSize": ARTIFACT_SIZE,
        "inventoryFile": "RuntimeSupport/r2fu-jazzy-win64-runtime-inventory.json",
        "inventoryFileCount": INVENTORY_FILE_COUNT,
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
        "packagePathPatch": {
            "modifiedFile": "Runtime/Ros2ForUnity/Scripts/ROS2ForUnity.cs",
            "reason": "Resolve the runtime root from this Unity package when Assets/Ros2ForUnity is absent.",
            "keepsAssetFolderFallback": True,
        },
        "freshProjectAcceptance": "deferred_to_install_acceptance",
    }


def readme_text() -> str:
    """Return the runtime package README."""
    return """# Unity2Foxglove ROS2 For Unity Runtime - Jazzy Win64

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

## Runtime Identity

- ROS distro: Jazzy
- Platform: Windows x64
- Build type: standalone
- RMW implementation: `rmw_fastrtps_cpp`
- Runtime id: `r2fu-jazzy-win64`
- Artifact source: `Ros2ForUnity_Jazzy_standalone_windows10.zip`
- SHA-256: `ac06054e05282b4ebd53b31ff4a48b815ebadc7f6985a5cebcbe35e01c830936`

The runtime manifest is `RuntimeSupport/runtime-manifest.json`. The file inventory is `RuntimeSupport/r2fu-jazzy-win64-runtime-inventory.json`.

## Package Path Patch

The bundled `ROS2ForUnity.cs` keeps the upstream `Assets/Ros2ForUnity` lookup and adds a package-path fallback so Unity Editor can load this runtime from:

```text
Packages/dev.unity2foxglove.ros2forunity.runtime.jazzy.win64/Runtime/Ros2ForUnity
```

This patch is limited to locating runtime files from a Unity package. It does not change ROS2 For Unity node, publisher, subscriber, or DDS behavior.

## Support Boundary

This is a prototype runtime package. Fresh-project install acceptance and public release readiness are separate gates. Linux, macOS, Humble, and Lyrical runtime packages are not included here.

RobotecAI states that ROS2 For Unity is officially supported for AWSIM/Autoware users and that the Robotec team cannot support and maintain the project for the general community. Unity2Foxglove-specific packaging and support belong to Unity2Foxglove, not RobotecAI.
"""


def notices_text(inventory: dict[str, object]) -> str:
    """Return third-party notices for the runtime package."""
    file_count = inventory.get("fileCount", INVENTORY_FILE_COUNT)
    return f"""# Third-Party Notices

This runtime package redistributes a locally rebuilt ROS2 For Unity Jazzy Windows x64 runtime payload.

Unity2Foxglove does not claim authorship of RobotecAI ROS2 For Unity, ros2cs, generated ROS2 message assemblies, generated native message support libraries, ROS2 Jazzy native libraries, Fast DDS, Fast CDR, RMW FastRTPS, or transitive runtime DLLs.

## Runtime Artifact

| Field | Value |
|---|---|
| Artifact | `Ros2ForUnity_Jazzy_standalone_windows10.zip` |
| Runtime id | `r2fu-jazzy-win64` |
| ROS distro | `jazzy` |
| Platform | Windows x64 |
| Build type | standalone |
| RMW | `rmw_fastrtps_cpp` |
| SHA-256 | `ac06054e05282b4ebd53b31ff4a48b815ebadc7f6985a5cebcbe35e01c830936` |
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
- DDS acceptance should use Windows ROS2 Jazzy or a real remote Linux topology; WSL2 NAT remains diagnostic-only.

RobotecAI states that ROS2 For Unity is officially supported for AWSIM/Autoware users and that the Robotec team cannot support and maintain the project for the general community. Unity2Foxglove must preserve that caveat and must not imply upstream community support for Unity2Foxglove-specific packaging.
"""


def extract_runtime(paths: BuildPaths) -> None:
    """Extract the Ros2ForUnity asset folder into the runtime package layout."""
    runtime_root = paths.package / "Runtime" / "Ros2ForUnity"
    runtime_root.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(paths.artifact) as archive:
        for info in archive.infolist():
            name = info.filename
            if info.is_dir() or not name.startswith("Ros2ForUnity/"):
                continue
            relative = Path(name).relative_to("Ros2ForUnity")
            target = runtime_root / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            with archive.open(info) as source, target.open("wb") as destination:
                shutil.copyfileobj(source, destination)


def patch_ros2_for_unity(package: Path) -> None:
    """Patch ROS2ForUnity.cs so the runtime can live inside a Unity package."""
    source = package / "Runtime" / "Ros2ForUnity" / "Scripts" / "ROS2ForUnity.cs"
    text = source.read_text(encoding="utf-8")
    if UNITY_PACKAGE_PATH_PATCH_MARKER in text:
        return
    if UPSTREAM_PATH_BLOCK not in text:
        raise ValueError("Could not find upstream GetRos2ForUnityPath block to patch.")
    source.write_text(text.replace(UPSTREAM_PATH_BLOCK, PACKAGE_PATH_BLOCK), encoding="utf-8")


def write_package_files(paths: BuildPaths, inventory: dict[str, object]) -> None:
    """Write package metadata, docs, notices, and support manifests."""
    write_json(paths.package / "package.json", package_json())
    write_text(paths.package / "README.md", readme_text())
    shutil.copyfile(UPSTREAM_LICENSE, paths.package / "LICENSE")
    write_text(paths.package / "THIRD_PARTY_NOTICES.md", notices_text(inventory))
    write_json(paths.package / "RuntimeSupport" / "runtime-manifest.json", runtime_manifest())
    shutil.copyfile(paths.inventory, paths.package / "RuntimeSupport" / "r2fu-jazzy-win64-runtime-inventory.json")


def build_package(paths: BuildPaths) -> None:
    """Build the runtime package from the runtime artifact."""
    inventory = require_inputs(paths)
    reset_package_dir(paths.package)
    extract_runtime(paths)
    patch_ros2_for_unity(paths.package)
    write_package_files(paths, inventory)
    write_generated_metas(paths.package)


def main(argv: list[str]) -> int:
    """Run package generation from command-line arguments."""
    paths = parse_args(argv)
    try:
        build_package(paths)
    except Exception as exc:
        print(f"[FAIL] {exc}", file=sys.stderr)
        return EXIT_FAILURE
    print(f"[PASS] built {rel(paths.package)}")
    print(f"[PASS] artifact={ARTIFACT_NAME} sha256={ARTIFACT_SHA256}")
    return EXIT_SUCCESS


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
