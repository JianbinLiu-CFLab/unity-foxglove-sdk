from pathlib import Path
import unittest
ROOT=Path(__file__).resolve().parents[4]
SRC=ROOT/'Tools/ros2_bridge/unity2foxglove_ros2_bridge/src/unity2foxglove_ros2_bridge.cpp'
class H06SigpipeTests(unittest.TestCase):
 def test_posix_send_suppresses_sigpipe(self):
  t=SRC.read_text(encoding='utf-8'); self.assertIn('#ifdef MSG_NOSIGNAL',t); self.assertIn('MSG_NOSIGNAL));',t)
 def test_windows_path_remains_supported(self):
  t=SRC.read_text(encoding='utf-8'); self.assertIn('#ifdef _WIN32',t); self.assertIn('::send(socket',t)
if __name__=='__main__': unittest.main()
