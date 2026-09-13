import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[4]
SOURCE = ROOT / "Unity2Foxglove/Assets/Scripts/ManualAcceptance/Phase106Ros2ForUnityAcceptance.cs"

class Phase106CleanupTests(unittest.TestCase):
    def test_cleanup_retains_handles_on_remove_failure(self):
        source = SOURCE.read_text(encoding="utf-8")
        self.assertIn("var cleanupFailed = false;", source)
        self.assertIn("if (cleanupFailed)", source)
        self.assertIn("retaining handle for retry", source)
        self.assertIn("if (!cleanupFailed && _ros2Unity != null && _ros2Node != null)", source)

if __name__ == "__main__":
    unittest.main()
