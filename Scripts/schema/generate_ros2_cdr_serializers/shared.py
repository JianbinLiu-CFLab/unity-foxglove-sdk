#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Shared imports, paths, and generation constants for ROS 2 CDR tooling."""

from __future__ import annotations

import argparse
import hashlib
import re
from dataclasses import dataclass
from pathlib import Path

from Scripts.schema.generate_ros2_msg_schema_catalog import CATEGORIES, CSHARP_NAMESPACE, EXPECTED_FILE_COUNT


REPO_ROOT = Path(__file__).resolve().parents[3]
DEFAULT_INPUT = REPO_ROOT / "third-party" / "foxglove-sdk" / "schemas" / "ros2"
PROTO_MESSAGES = REPO_ROOT / "Packages" / "dev.unity2foxglove.sdk" / "Runtime" / "Schemas" / "Proto" / "Generated" / "Messages"
DEFAULT_OUTPUT_DIR = REPO_ROOT / "Packages" / "dev.unity2foxglove.ros2bridge" / "Runtime" / "Schemas" / "Ros2Msg" / "Generated"
PRIMITIVE_TYPES = {"bool", "int16", "uint16", "int32", "uint32", "int64", "uint64", "uint8", "float32", "float64", "string"}
SCALAR_TYPE_MAP = {"bool": "bool", "int16": "int", "uint16": "uint", "int32": "int", "uint32": "uint", "int64": "long", "uint64": "ulong", "float32": "float", "float64": "double", "string": "string"}
REPEATED_TYPE_MAP = {"int16": "int", "uint16": "uint", "int32": "int", "uint32": "uint", "int64": "long", "uint64": "ulong", "float32": "float", "float64": "double"}
STANDARD_TYPES = {"builtin_interfaces/Time", "builtin_interfaces/Duration", "geometry_msgs/Point", "geometry_msgs/Quaternion", "geometry_msgs/Vector3", "geometry_msgs/Pose"}

__all__ = [name for name in globals() if not name.startswith("__")]
