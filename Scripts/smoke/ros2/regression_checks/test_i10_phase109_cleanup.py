# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Module: Scripts/smoke
# Purpose: Regression coverage for retryable Phase109 ROS2 cleanup.
# Usage: python -m unittest Scripts.smoke.ros2.regression_checks.test_i10_phase109_cleanup
# Inputs: Phase109 ROS2 For Unity context source.
# Outputs: unittest pass/fail status.

import re
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[4]
CONTEXT = ROOT / "Unity2Foxglove/Assets/Scripts/ManualAcceptance/Phase109Ros2ForUnityContext.cs"


class I10Phase109CleanupTests(unittest.TestCase):
    """Verify Phase109 retains native ownership after cleanup failures."""

    def test_context_and_node_remain_retryable_until_native_cleanup_succeeds(self):
        """Require child, node, and context state to commit only after success."""
        source = CONTEXT.read_text(encoding="utf-8")
        self.assertIn("bool IsDisposed { get; }", source)

        publisher = re.search(
            r"private sealed class StringPublisher.*?public void Dispose\(\).*?\n    }",
            source,
            re.DOTALL,
        )
        self.assertIsNotNone(publisher)
        publisher_body = publisher.group(0)
        self.assertRegex(
            publisher_body,
            r"try\s*\{\s*_ros2Node\.RemovePublisher.*?\s*_disposed\s*=\s*true;",
        )
        self.assertNotRegex(
            publisher_body,
            r"_disposed\s*=\s*true;\s*try\s*\{",
        )

        subscription = re.search(
            r"private sealed class StringSubscription.*?public void Dispose\(\).*?\n    }\n#endif",
            source,
            re.DOTALL,
        )
        self.assertIsNotNone(subscription)
        subscription_body = subscription.group(0)
        self.assertRegex(
            subscription_body,
            r"(?s)try\s*\{.*?_ros2Node\.RemoveSubscription.*?_subscription\s*=\s*null;.*?_disposed\s*=\s*true;",
        )
        self.assertNotRegex(
            subscription_body,
            r"_disposed\s*=\s*true;\s*_pending\.Clear\(\);\s*}\s*\n\s*var subscription",
        )

        node = re.search(
            r"private sealed class Phase109Ros2ForUnityNode.*?public void Dispose\(\).*?\n    }\n\n    private interface",
            source,
            re.DOTALL,
        )
        self.assertIsNotNone(node)
        node_body = node.group(0)
        self.assertRegex(node_body, r"if \(!.*?IsDisposed.*?\)\s*return;")
        self.assertRegex(node_body, r"(?s)RemoveNode\(_ros2Node\).*?IsDisposed\s*=\s*true;")


if __name__ == "__main__":
    unittest.main()
