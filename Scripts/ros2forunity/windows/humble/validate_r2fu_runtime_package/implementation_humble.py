#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Validate the ROS2 For Unity Humble Win64 runtime Unity package prototype.
# Usage: python Scripts/ros2forunity/windows/humble/validate_r2fu_runtime_package.py
# Inputs: Packages/dev.unity2foxglove.ros2forunity.runtime.humble.win64 package directory.
# Outputs: Prints runtime package checks and exits nonzero on failure.

"""Validate the ROS2 For Unity Humble Win64 runtime package prototype."""

from __future__ import annotations

import json
import argparse
import os
import hashlib
import re
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
PACKAGE_NAME = "dev.unity2foxglove.ros2forunity.runtime.humble.win64"
PACKAGE = ROOT / "Packages" / PACKAGE_NAME
ADAPTER_PACKAGE = ROOT / "Packages" / "dev.unity2foxglove.ros2forunity"
CORE_PACKAGE = ROOT / "Packages" / "dev.unity2foxglove.sdk"
RUNTIME_ROOT = PACKAGE / "Runtime" / "Ros2ForUnity"
PLUGIN_ROOT = RUNTIME_ROOT / "Plugins" / "Windows" / "x86_64"
MANIFEST = PACKAGE / "RuntimeSupport" / "runtime-manifest.json"
INVENTORY = PACKAGE / "RuntimeSupport" / "r2fu-humble-win64-runtime-inventory.json"

ARTIFACT_NAME = "Ros2ForUnity_humble_standalone_windows_x86_64.zip"
EXPECTED_RMW_IMPLEMENTATION = "rmw_fastrtps_cpp"

CRITICAL_DLLS = (
    "rcl.dll",
    "yaml.dll",
    "spdlog.dll",
    "rosgraph_msgs_assembly.dll",
)

BASELINE_MESSAGE_PACKAGES = (
    "builtin_interfaces",
    "std_msgs",
    "sensor_msgs",
    "tf2_msgs",
    "rosgraph_msgs",
)

HUMBLE_HANDOFF_FAMILIES = (
    "actionlib_msgs",
    "composition_interfaces",
    "lifecycle_msgs",
    "statistics_msgs",
    "stereo_msgs",
)

HUMBLE_HANDOFF_DLLS = (
    "tf2.dll",
    "tf2_ros.dll",
    "static_transform_broadcaster_node.dll",
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




def _load_source_sections() -> None:
    """Load the decomposed source sections into the implementation namespace."""

    section_dir = Path(__file__).with_name("sections")
    for section_name in (
        "01_foundation.py",
        "02_inventory_and_acceptance.py",
        "03_runtime_checks.py",
        "04_docs_boundaries_and_cli.py",
    ):
        section_path = section_dir / section_name
        source = section_path.read_text(encoding="utf-8")
        exec(compile(source, str(section_path), "exec"), globals(), globals())

_load_source_sections()
__all__ = [name for name in globals() if not name.startswith("__") and name != "_load_source_sections"]
