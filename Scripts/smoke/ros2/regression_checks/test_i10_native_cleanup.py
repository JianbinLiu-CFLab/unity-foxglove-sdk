import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[4]
CONTEXT = ROOT / "Packages/dev.unity2foxglove.ros2forunity/Samples~/ROS2 For Unity External Adapter/Phase110Ros2ForUnityContext.cs"


class I10NativeCleanupTests(unittest.TestCase):
    def test_subscription_cleanup_retains_handle_until_native_remove_succeeds(self):
        source = CONTEXT.read_text(encoding="utf-8")
        self.assertIn("private bool RemoveSubscriptionSafely", source)
        self.assertIn("if (!RemoveSubscriptionSafely(subscription))", source)


if __name__ == "__main__":
    unittest.main()
