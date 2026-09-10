from pathlib import Path
import unittest
ROOT = Path(__file__).resolve().parents[4]
SRC = ROOT / 'Packages/dev.unity2foxglove.ros2bridge/Runtime/Ros2Bridge/Ros2BridgeInboundQueue.cs'

class H07QueueAccountingTests(unittest.TestCase):
    """R4.1 regression contract."""

    def test_queued_bytes_excludes_inflight_storage(self):
        """R4.1 regression contract."""
        t = SRC.read_text(encoding='utf-8')
        self.assertIn('finalQueuedBytes = checked(\n                    _queuedBytes\n                    - removedBytes', t)
        self.assertIn('finalQueuedBytes + _inFlightBytes', t)
if __name__ == '__main__':
    unittest.main()
