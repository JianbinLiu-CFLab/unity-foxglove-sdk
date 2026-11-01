from pathlib import Path
import unittest
from Scripts.phase192.source_layout import read_included_source
ROOT = Path(__file__).resolve().parents[4]
SRC = ROOT / 'Tools/ros2_bridge/unity2foxglove_ros2_bridge/src/unity2foxglove_ros2_bridge.cpp'


class H06SigpipeTests(unittest.TestCase):
    """R4.1 regression contract."""

    def test_posix_send_suppresses_sigpipe(self):
        """R4.1 regression contract."""
        t = read_included_source(SRC)
        self.assertIn('#ifdef MSG_NOSIGNAL', t)
        self.assertIn('MSG_NOSIGNAL));', t)

    def test_windows_path_remains_supported(self):
        """R4.1 regression contract."""
        t = read_included_source(SRC)
        self.assertIn('#ifdef _WIN32', t)
        self.assertIn('::send(socket', t)
if __name__ == '__main__':
    unittest.main()
