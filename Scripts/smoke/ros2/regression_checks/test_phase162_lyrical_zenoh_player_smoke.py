#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Regression tests for the Phase162 Lyrical Zenoh smoke helper.

from __future__ import annotations

import importlib.util
import sys
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest import mock


ROOT = Path(__file__).resolve().parents[4]
SMOKE_PATH = ROOT / "Scripts" / "smoke" / "ros2" / "phase162_lyrical_zenoh_player_smoke.py"


def load_smoke_module():
    """Load the Phase162 smoke helper module under test."""
    smoke_dir = str(SMOKE_PATH.parent)
    if smoke_dir not in sys.path:
        sys.path.insert(0, smoke_dir)
    spec = importlib.util.spec_from_file_location("phase162_lyrical_zenoh_player_smoke", SMOKE_PATH)
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    assert spec.loader is not None
    spec.loader.exec_module(module)
    return module


class Phase162LyricalZenohSmokeTests(unittest.TestCase):
    """Regression coverage for the Phase162 Lyrical Zenoh smoke helper."""

    def setUp(self) -> None:
        """Load a fresh copy of the smoke helper for each test."""
        self.smoke = load_smoke_module()

    def test_default_zenoh_router_uses_ros2_root_rmw_zenohd(self) -> None:
        """Bare Zenoh smoke should auto-discover the router executable from the ROS2 root."""
        with tempfile.TemporaryDirectory() as temp:
            ros2_root = Path(temp) / "ros2_lyrical"
            router = ros2_root / "Lib" / "rmw_zenoh_cpp" / "rmw_zenohd.exe"
            router.parent.mkdir(parents=True)
            router.write_bytes(b"")

            self.assertEqual(router, self.smoke.default_zenoh_router(ros2_root))

    def test_bare_smoke_defaults_to_phase138_pointcloud2_topic(self) -> None:
        """Bare smoke should match the manual Phase138/Phase162 PointCloud2 scene."""
        self.assertEqual("/unity/point_cloud2", self.smoke.DEFAULT_TOPIC)
        self.assertEqual("sensor_msgs/msg/PointCloud2", self.smoke.DEFAULT_MESSAGE_TYPE)

    def test_pointcloud2_echo_uses_sensor_data_qos(self) -> None:
        """PointCloud2 smoke should subscribe without Reliable queue buildup."""
        args = SimpleNamespace(
            topic="/unity/point_cloud2",
            message_type="sensor_msgs/msg/PointCloud2",
            spin_seconds=5.0,
        )

        command = self.smoke.build_echo_command(Path("python.exe"), Path("ros2-script.py"), args)

        self.assertIn("--qos-reliability", command)
        self.assertIn("best_effort", command)
        self.assertIn("--qos-history", command)
        self.assertIn("keep_last", command)
        self.assertIn("--qos-depth", command)
        self.assertIn("1", command)

    def test_bare_phase162_builds_rviz2_acceptance_args(self) -> None:
        """Bare Phase162 is a Zenoh RViz2 PointCloud2 acceptance, not only echo."""
        args = SimpleNamespace(
            ros2_root=Path("ros2-windows/ros2_lyrical"),
            topic="/unity/point_cloud2",
            deskewed_topic="/unity/point_cloud2_deskewed",
            expected_frame_id="os_lidar",
            fixed_frame="map",
            spin_seconds=12.0,
            rmw_implementation="rmw_zenoh_cpp",
            domain_id="0",
            discovery_range="SUBNET",
            rviz_display_mode="both",
            launch_rviz=True,
            skip_topic_probe=False,
            require_motion=False,
        )

        command = self.smoke.build_rviz_acceptance_args(args)

        self.assertIn("--rmw", command)
        self.assertIn("rmw_zenoh_cpp", command)
        self.assertIn("--raw-topic", command)
        self.assertIn("/unity/point_cloud2", command)
        self.assertIn("--deskewed-topic", command)
        self.assertIn("/unity/point_cloud2_deskewed", command)
        self.assertIn("--rviz-display-mode", command)
        self.assertIn("both", command)
        self.assertIn("--allow-static", command)
        self.assertNotIn("--no-rviz", command)

    def test_main_preserves_phase138u_inconclusive_verdict(self) -> None:
        """Motion-deskew inconclusive should not be collapsed into a hard failure."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            ros2_root = root / "ros2"
            summary = root / "summary.json"
            echo = root / "echo.log"
            args = [
                "--ros2-root",
                str(ros2_root),
                "--summary-output",
                str(summary),
                "--echo-output",
                str(echo),
                "--no-zenoh-router",
                "--no-rviz",
            ]

            with mock.patch.object(self.smoke, "build_phase162_env", return_value=({}, Path("python.exe"), Path("ros2.py"))):
                with mock.patch.object(self.smoke.phase138u, "main", side_effect=self.smoke.phase138u.InconclusiveError("static capture")):
                    code = self.smoke.main(args)

            payload = __import__("json").loads(summary.read_text(encoding="utf-8"))

        self.assertEqual(2, code)
        self.assertEqual("PHASE162_LYRICAL_ZENOH_RVIZ2_POINTCLOUD2_INCONCLUSIVE", payload["verdict"])
        self.assertIn("static capture", payload["error"])


if __name__ == "__main__":
    unittest.main()
