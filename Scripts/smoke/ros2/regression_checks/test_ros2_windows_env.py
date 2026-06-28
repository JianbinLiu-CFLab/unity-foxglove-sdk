#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Module: Scripts/smoke
# Purpose: Regression tests for shared Windows ROS2 smoke environment helpers.

from __future__ import annotations

import os
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock


SMOKE_ROS2_DIR = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(SMOKE_ROS2_DIR))

import _ros2_windows_env as ros2env  # noqa: E402


class Ros2WindowsEnvTests(unittest.TestCase):
    """Regression coverage for Windows ROS2 smoke environment helpers."""

    def test_build_ros_env_includes_ros2_opt_vendor_bin_paths(self) -> None:
        """ROS2 vendor DLL directories under opt must be visible to RMW DLL loading."""
        with tempfile.TemporaryDirectory() as temp:
            ros2_root = Path(temp) / "ros2_lyrical"
            vendor_bin = ros2_root / "opt" / "zenoh_cpp_vendor" / "bin"
            vendor_bin.mkdir(parents=True)
            (vendor_bin / "zenohc.dll").write_bytes(b"")

            env = ros2env.build_ros_env(
                ros2_root,
                rmw_implementation="rmw_zenoh_cpp",
                ros_distro="lyrical",
            )

        self.assertIn(str(vendor_bin), env["PATH"].split(os.pathsep))

    def test_build_ros_env_warns_when_inheriting_non_default_rmw(self) -> None:
        """Implicit inherited Zenoh/FastDDS choices should be visible in acceptance logs."""
        with tempfile.TemporaryDirectory() as temp:
            ros2_root = Path(temp) / "ros2_lyrical"
            with mock.patch.dict(os.environ, {"RMW_IMPLEMENTATION": "rmw_zenoh_cpp"}, clear=False):
                with mock.patch.object(ros2env, "log_event") as log_event:
                    env = ros2env.build_ros_env(ros2_root, ros_distro="lyrical")

        self.assertEqual("rmw_zenoh_cpp", env["RMW_IMPLEMENTATION"])
        log_event.assert_called_once()
        self.assertIn("rmw_zenoh_cpp", log_event.call_args.args[1])

    def test_launch_rviz_includes_ros2_opt_vendor_bin_paths(self) -> None:
        """RViz2 must see opt vendor DLL directories such as zenoh_cpp_vendor/bin."""
        with tempfile.TemporaryDirectory() as temp:
            ros2_root = Path(temp) / "ros2_lyrical"
            vendor_bin = ros2_root / "opt" / "zenoh_cpp_vendor" / "bin"
            vendor_bin.mkdir(parents=True)
            (vendor_bin / "zenohc.dll").write_bytes(b"")
            rviz_exe = ros2_root / "bin" / "rviz2.exe"
            rviz_exe.parent.mkdir(parents=True)
            rviz_exe.write_bytes(b"")
            config = Path(temp) / "view.rviz"
            config.write_text("Visualization Manager: {}\n", encoding="utf-8")
            captured_env: dict[str, str] = {}

            class FakeProcess:
                """Tiny process double returned by the patched RViz launcher."""

                pid = 1234

                def poll(self):
                    """Report that the fake RViz process is still running."""
                    return None

            def capture_popen(_command, **kwargs):
                """Capture the environment passed to the patched process launcher."""
                captured_env.update(kwargs["env"])
                return FakeProcess()

            with mock.patch.object(ros2env.subprocess, "Popen", side_effect=capture_popen):
                ros2env.launch_rviz(
                    ros2_root,
                    config,
                    {},
                    "test",
                    startup_check_seconds=0.0,
                    window_wait_seconds=0.0,
                )

        self.assertIn(str(vendor_bin), captured_env["PATH"].split(os.pathsep))


if __name__ == "__main__":
    unittest.main()
