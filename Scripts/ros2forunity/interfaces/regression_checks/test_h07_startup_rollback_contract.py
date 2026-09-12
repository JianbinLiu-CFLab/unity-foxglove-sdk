from pathlib import Path
import unittest
ROOT = Path(__file__).resolve().parents[4]
SRC = ROOT / 'Packages/dev.unity2foxglove.ros2bridge/Runtime/Ros2Bridge/Ros2BridgeConnection.cs'

class H07StartupRollbackTests(unittest.TestCase):
    """R4.1 regression contract."""

    def test_partial_thread_start_reconciles_worker_count(self):
        """R4.1 regression contract."""
        t = SRC.read_text(encoding='utf-8')
        self.assertIn('var readerStarted = false;', t)
        self.assertIn('var writerStarted = false;', t)
        self.assertIn('_workersRemaining = (readerStarted ? 1 : 0)', t)

    def test_startup_failure_is_caught_before_handshake(self):
        """R4.1 regression contract."""
        t = SRC.read_text(encoding='utf-8')
        self.assertIn('catch\n                {\n                    // A Thread constructor/Start failure', t)
if __name__ == '__main__':
    unittest.main()
