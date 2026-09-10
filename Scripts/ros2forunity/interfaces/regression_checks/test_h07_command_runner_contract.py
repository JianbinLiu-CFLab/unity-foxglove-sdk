from pathlib import Path
import unittest
ROOT = Path(__file__).resolve().parents[4]
SRC = ROOT / 'Packages/dev.unity2foxglove.ros2bridge/Runtime/Ros2Bridge/Diagnostics/IRos2BridgeCommandRunner.cs'

class H07CommandRunnerTests(unittest.TestCase):
    """R4.1 regression contract."""

    def test_success_drain_is_bounded(self):
        """R4.1 regression contract."""
        t = SRC.read_text(encoding='utf-8')
        self.assertIn('process.WaitForExit(Math.Max(1, timeoutMs));', t)

    def test_output_is_capped_and_synchronized(self):
        """R4.1 regression contract."""
        t = SRC.read_text(encoding='utf-8')
        self.assertIn('MaxOutputChars = 1_000_000', t)
        self.assertIn('lock (builder)', t)
        self.assertIn('AppendBounded(stdout', t)
        self.assertIn('AppendBounded(stderr', t)
if __name__ == '__main__':
    unittest.main()
