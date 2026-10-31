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
    "Runtime/Ros2ForUnity/Scripts/ROS2ForUnity.cs",
    "Runtime/Ros2ForUnity/Scripts/ROS2Node.cs",
    "Runtime/Ros2ForUnity/Scripts/ROS2UnityComponent.cs",
    "Runtime/Ros2ForUnity/Scripts/ROS2UnityCore.cs",
    "Runtime/Ros2ForUnity/Scripts/Sensor.cs",
    "Runtime/Ros2ForUnity/Scripts/Decomposed/ROS2ForUnity/ROS2ForUnity.Lifecycle.cs",
    "Runtime/Ros2ForUnity/Scripts/Decomposed/ROS2ForUnity/ROS2ForUnity.ProcessEnvironmentLease.cs",
    "Runtime/Ros2ForUnity/Scripts/Time/ROS2ScalableTimeSource.cs",
    "Runtime/Ros2ForUnity/Scripts/Time/ROS2TimeSource.cs",
}
SUPPLEMENTAL_RUNTIME_RELATIVE = "Runtime/Ros2ForUnity/StreamingAssets/Ros2ForUnity/share/ament_index"

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
        return Path.Combine(Application.streamingAssetsPath, ros2ForUnityAssetFolderName);
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
        return Path.Combine(Application.streamingAssetsPath, ros2ForUnityAssetFolderName);
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

def _load_source_sections() -> None:
    """Load the decomposed source sections into the facade namespace."""

    section_dir = Path(__file__).with_name("Decomposed") / "build_r2fu_runtime_package"
    original_path = list(sys.path)
    try:
        lyrical_directory = Path(__file__).resolve().parent
        if str(lyrical_directory) not in sys.path:
            sys.path.insert(0, str(lyrical_directory))
        from section_manifest import load_section_names

        section_names = load_section_names(section_dir)
    finally:
        sys.path[:] = original_path
    for section_name in section_names:
        section_path = section_dir / section_name
        source = section_path.read_text(encoding="utf-8")
        exec(compile(source, str(section_path), "exec"), globals(), globals())

_load_source_sections()
del _load_source_sections
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
