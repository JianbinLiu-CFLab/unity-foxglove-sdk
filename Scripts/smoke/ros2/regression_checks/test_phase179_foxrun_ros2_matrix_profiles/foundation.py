"""Shared loaders and profile paths for Phase179 matrix tests."""

from __future__ import annotations

import contextlib
import importlib.util
import json
import sys
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest import mock
from Scripts.phase192.source_layout import load_fresh_module as __load_fresh_module

ROOT = Path(__file__).resolve().parents[5]
PROFILE_PATH = ROOT / "Scripts" / "smoke" / "ros2" / "phase179_foxrun_ros2_matrix_profiles.py"
WINDOWS_ENDPOINT_PROBE_PATH = ROOT / "Scripts" / "smoke" / "ros2" / "phase179_windows_rclpy_endpoint_probe.py"


def load_profile_module():
    """Load the profile engine without requiring a ROS2 installation."""
    smoke_dir = str(PROFILE_PATH.parent)
    if smoke_dir not in sys.path:
        sys.path.insert(0, smoke_dir)
    return __load_fresh_module("phase179_foxrun_ros2_matrix_profiles", PROFILE_PATH)


__all__ = [
    "Path",
    "PROFILE_PATH",
    "ROOT",
    "SimpleNamespace",
    "WINDOWS_ENDPOINT_PROBE_PATH",
    "__load_fresh_module",
    "contextlib",
    "importlib",
    "json",
    "load_profile_module",
    "mock",
    "sys",
    "tempfile",
    "unittest",
]
