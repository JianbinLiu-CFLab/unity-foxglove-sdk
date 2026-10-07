# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Module: Scripts/smoke
# Purpose: Regression coverage for retryable ROS2 native endpoint cleanup.
# Usage: python -m unittest Scripts.smoke.ros2.regression_checks.test_i10_native_cleanup
# Inputs: Phase110 ROS2 For Unity context source.
# Outputs: unittest pass/fail status.

import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[4]
CONTEXT = ROOT / "Packages/dev.unity2foxglove.ros2forunity/Samples~/ROS2 For Unity External Adapter/Phase110Ros2ForUnityContext.cs"


class I10NativeCleanupTests(unittest.TestCase):
    """Verify native cleanup retains handles when removal fails."""

    def test_subscription_cleanup_retains_handle_until_native_remove_succeeds(self):
        """Ensure failed subscription removal remains retryable."""
        source = CONTEXT.read_text(encoding="utf-8")
        self.assertIn("private bool RemoveSubscriptionSafely", source)
        self.assertIn("if (!RemoveSubscriptionSafely(subscription))", source)

    def test_direct_node_removal_waits_for_child_cleanup(self):
        """Ensure missing owners clear already-invalid native handles."""
        source = (ROOT / "Packages/dev.unity2foxglove.ros2forunity/Samples~/ROS2 For Unity External Adapter/Phase110Ros2ForUnityStringSmoke.cs").read_text(encoding="utf-8")
        self.assertIn("if (_directRos2Unity == null)", source)
        self.assertIn("_directSubscription = null", source)
        self.assertIn("_directPublisher = null", source)
        self.assertIn("_directRos2Node = null", source)
        self.assertIn("_directReceived.Clear()", source)
        self.assertIn("if (!cleanupFailed && _directRos2Node != null)", source)
        self.assertIn("ref _directRos2Node", source)
        self.assertIn("_directRos2Node != null || _directSubscription != null || _directPublisher != null", source)
        self.assertIn("ref _directSubscription", source)
        self.assertIn("ref _directPublisher", source)

    def test_direct_cleanup_uses_retryable_owner_helper(self):
        """The direct sample uses the executable clear-after-success helper."""
        source = (ROOT / "Packages/dev.unity2foxglove.ros2forunity/Samples~/ROS2 For Unity External Adapter/Phase110Ros2ForUnityStringSmoke.cs").read_text(encoding="utf-8")
        self.assertIn("RetryableNativeCleanup.TryRemove", source)
        self.assertIn("ref _directSubscription", source)
        self.assertIn("ref _directPublisher", source)
        self.assertIn("ref _directRos2Node", source)


if __name__ == "__main__":
    unittest.main()
