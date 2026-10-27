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

PACKAGE_NAME = "dev.unity2foxglove.ros2forunity.runtime.jazzy.win64"
PACKAGE_VERSION = "0.1.0-preview.1"
RUNTIME_ID = "r2fu-jazzy-win64"
ARTIFACT_NAME = "Ros2ForUnity_jazzy_standalone_windows_x86_64.zip"
EXPECTED_ARTIFACT_SHA256 = "4e5cb8b0073d4a34d194b9a6ce0b3449220085f3cfd041b2fd33622e6442ff5d"

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
    "Runtime/Ros2ForUnity/Scripts/ROS2ForUnity.cs",
    "Runtime/Ros2ForUnity/Scripts/ROS2Node.cs",
    "Runtime/Ros2ForUnity/Scripts/ROS2UnityComponent.cs",
    "Runtime/Ros2ForUnity/Scripts/ROS2UnityCore.cs",
    "Runtime/Ros2ForUnity/Scripts/Sensor.cs",
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

PACKAGE_CONSTANTS_BLOCK = """    private const string unity2FoxgloveRuntimePackageName = "dev.unity2foxglove.ros2forunity.runtime.jazzy.win64";
    private const string unity2FoxgloveRuntimePackageAssetPath =
        "Packages/dev.unity2foxglove.ros2forunity.runtime.jazzy.win64/Runtime/Ros2ForUnity";
"""




def _load_source_sections() -> None:
    """Load the decomposed source sections into the facade namespace."""

    section_dir = Path(__file__).with_name("Decomposed") / "build_r2fu_runtime_package"
    for section_name in (
        "01_types_and_inputs.py",
        "02_package_files.py",
        "03_runtime_extraction_and_patching.py",
        "04_build_pipeline.py",
    ):
        section_path = section_dir / section_name
        source = section_path.read_text(encoding="utf-8")
        exec(compile(source, str(section_path), "exec"), globals(), globals())

_load_source_sections()
del _load_source_sections

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
