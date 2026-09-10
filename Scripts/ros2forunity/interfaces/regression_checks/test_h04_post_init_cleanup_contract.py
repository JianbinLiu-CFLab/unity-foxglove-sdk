from pathlib import Path
import unittest
ROOT = Path(__file__).resolve().parents[4]
FILES = [ROOT / 'Packages/dev.unity2foxglove.ros2forunity.runtime.humble.win64/Runtime/Ros2ForUnity/Scripts/ROS2ForUnity.cs', ROOT / 'Packages/dev.unity2foxglove.ros2forunity.runtime.jazzy.win64/Runtime/Ros2ForUnity/Scripts/ROS2ForUnity.cs', ROOT / 'Packages/dev.unity2foxglove.ros2forunity.runtime.lyrical.win64/Runtime/Ros2ForUnity/Scripts/ROS2ForUnity.cs']

class H04PostInitCleanupTests(unittest.TestCase):
    """R4.1 regression contract."""

    def test_constructor_post_init_failure_has_cleanup_boundary(self):
        """R4.1 regression contract."""
        for path in FILES:
            text = path.read_text(encoding='utf-8')
            self.assertTrue('post-init validation failure' in text or 'Constructor failure after Ros2cs.Init' in text, path.name)
            self.assertIn('Ros2cs.Shutdown()', text, path.name)
            self.assertTrue('UnregisterCtrlCHandlerStatic()' in text or 'DestroyROS2ForUnity();' in text, path.name)

    def test_lifecycle_flags_are_not_left_owned_on_validation_failure(self):
        """R4.1 regression contract."""
        for path in FILES:
            text = path.read_text(encoding='utf-8')
            self.assertIn('ValidateRmwImplementation(rmwImpl);', text, path.name)
            if 'runtime.jazzy' in str(path):
                self.assertIn('try { DestroyROS2ForUnity(); } catch { }', text)
            else:
                self.assertGreater(text.rfind('isInitialized = true;'), text.rfind('ValidateRmwImplementation(rmwImpl);'))
if __name__ == '__main__':
    unittest.main()
