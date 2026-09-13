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


if __name__ == "__main__":
    unittest.main()
