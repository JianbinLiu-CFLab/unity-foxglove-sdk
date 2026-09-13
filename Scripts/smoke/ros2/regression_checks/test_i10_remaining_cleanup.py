# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Module: Scripts/smoke
# Purpose: Regression coverage for retryable RViz and PointCloud2 cleanup.
# Usage: python -m unittest Scripts.smoke.ros2.regression_checks.test_i10_remaining_cleanup
# Inputs: Phase128-138 ROS2 For Unity sample sources.
# Outputs: unittest pass/fail status.

import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[4]
SOURCE_FILES = (
    ROOT / "Packages/dev.unity2foxglove.ros2forunity/Samples~/RViz2 Standard Visualization Acceptance/Phase128Rviz2TfLaserScanSmoke.cs",
    ROOT / "Packages/dev.unity2foxglove.ros2forunity/Samples~/RViz2 PointCloud2 Acceptance/Phase129Rviz2PointCloud2Smoke.cs",
    ROOT / "Packages/dev.unity2foxglove.ros2forunity/Samples~/RViz2 MarkerArray Acceptance/Phase130Rviz2MarkerArraySmoke.cs",
    ROOT / "Packages/dev.unity2foxglove.ros2forunity/Samples~/ROS2 Standard Message Expansion/Phase132StandardMessagesSmoke.cs",
    ROOT / "Packages/dev.unity2foxglove.ros2forunity/Samples~/Virtual LiDAR PointCloud2 Digital Twin/Phase138VirtualLidarPointCloud2Smoke.cs",
)


class I10RemainingCleanupTests(unittest.TestCase):
    """Verify each remaining owner retains handles after native failures."""

    def test_remaining_owners_gate_teardown_on_success(self):
        """Require cleanup failure aggregation and retryable ownership."""
        for path in SOURCE_FILES:
            source = path.read_text(encoding="utf-8")
            with self.subTest(path=path.name):
                self.assertIn("var cleanupFailed = false;", source)
                self.assertIn("if (cleanupFailed)\n            return;", source)

    def test_standard_message_publishers_clear_by_reference_after_success(self):
        """Require the multi-publisher owner to clear only successful removals."""
        source = SOURCE_FILES[3].read_text(encoding="utf-8")
        self.assertIn("RemovePublisherIfPresent(ref _cameraInfoPublisher)", source)
        self.assertIn("private bool RemovePublisherIfPresent<T>", source)
        self.assertIn("publisher = null;", source)

    def test_rviz_publishers_clear_after_success_before_node_teardown(self):
        """Require RViz publisher handles to remain available for retry."""
        source = SOURCE_FILES[0].read_text(encoding="utf-8")
        self.assertIn("if (!cleanupFailed && _ros2Unity != null && _node != null)", source)
        self.assertIn("_node.RemovePublisher<tf2_msgs.msg.TFMessage>(_tfPublisher);\n                _tfPublisher = null;", source)


if __name__ == "__main__":
    unittest.main()
