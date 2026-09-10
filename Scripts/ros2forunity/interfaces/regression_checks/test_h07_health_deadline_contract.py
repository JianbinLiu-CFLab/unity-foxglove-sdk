from pathlib import Path
import unittest
ROOT = Path(__file__).resolve().parents[4]
SRC = ROOT / 'Packages/dev.unity2foxglove.ros2bridge/Runtime/Ros2Bridge/Diagnostics/Ros2BridgeU2R2HealthProbe.cs'

class H07HealthDeadlineTests(unittest.TestCase):
    """R4.1 regression contract."""

    def test_read_exact_has_total_deadline(self):
        """R4.1 regression contract."""
        t = SRC.read_text(encoding='utf-8')
        self.assertIn('var deadline = Stopwatch.StartNew();', t)
        self.assertIn('stream.ReadTimeout =', t)
        self.assertIn('Timed out reading ROS2 Bridge health response.', t)
if __name__ == '__main__':
    unittest.main()
