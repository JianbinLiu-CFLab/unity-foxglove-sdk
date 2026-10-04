from pathlib import Path
import unittest
ROOT = Path(__file__).resolve().parents[4]
SRC = ROOT / 'Packages/dev.unity2foxglove.ros2bridge/Runtime/Ros2Bridge/Diagnostics/Ros2BridgeU2R2HealthProbe.cs'

class H07HealthDeadlineTests(unittest.TestCase):
    """The probe carries one absolute deadline through every I/O stage."""

    def test_read_exact_has_total_deadline(self):
        """The probe carries one absolute deadline through every I/O stage."""
        t = SRC.read_text(encoding='utf-8')
        self.assertIn('var stopwatch = Stopwatch.StartNew();', t)
        self.assertIn('RemainingMilliseconds(stopwatch, totalTimeoutMs)', t)
        self.assertIn('ReadExact(stream, 16, timeoutMs, deadline, cancellationToken)', t)
        self.assertNotIn('Stopwatch.StartNew();\n            var bytes = new byte[count];', t)
        self.assertIn('Timed out reading ROS2 Bridge health response.', t)
if __name__ == '__main__':
    unittest.main()
