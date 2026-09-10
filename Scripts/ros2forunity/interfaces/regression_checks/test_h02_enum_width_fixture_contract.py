from pathlib import Path
import unittest
ROOT = Path(__file__).resolve().parents[4]
SRC = ROOT / 'Packages/dev.unity2foxglove.sdk/Tests/Unit/Ros2ForUnity/FoxRunRos2CustomMapperGenerationTests.cs'

class H02EnumWidthTests(unittest.TestCase):
    """R4.1 regression contract."""

    def test_fixture_enums_match_ros_uint16(self):
        """R4.1 regression contract."""
        t = SRC.read_text(encoding='utf-8')
        self.assertIn('enum StateKind : ushort', t)
        self.assertIn('enum OptionalKind : ushort', t)
if __name__ == '__main__':
    unittest.main()
