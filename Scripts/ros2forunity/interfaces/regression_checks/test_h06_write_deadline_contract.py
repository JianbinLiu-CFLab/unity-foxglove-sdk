from pathlib import Path
import unittest
ROOT = Path(__file__).resolve().parents[4]
SRC = ROOT / 'Tools/ros2_bridge/unity2foxglove_ros2_bridge/src/unity2foxglove_ros2_bridge.cpp'

class H06WriteDeadlineTests(unittest.TestCase):
    """R4.1 regression contract."""

    def test_partial_send_does_not_reset_frame_clock(self):
        """R4.1 regression contract."""
        t = SRC.read_text(encoding='utf-8')
        self.assertIn('Keep the frame-start clock monotonic', t)
        self.assertNotIn('clock.stalled_since = std::chrono::steady_clock::now();', t)

    def test_accounted_loop_still_enforces_timeout(self):
        """R4.1 regression contract."""
        t = SRC.read_text(encoding='utf-8')
        self.assertIn('enforce_accounted_write_timeout(limits, clock)', t)
if __name__ == '__main__':
    unittest.main()
