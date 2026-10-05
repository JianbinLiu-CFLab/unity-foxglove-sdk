from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[4]
FILES = [
    ROOT / 'Packages/dev.unity2foxglove.ros2forunity.runtime.humble.win64/Runtime/Ros2ForUnity/Scripts/ROS2ForUnity.cs',
    ROOT / 'Packages/dev.unity2foxglove.ros2forunity.runtime.jazzy.win64/Runtime/Ros2ForUnity/Scripts/ROS2ForUnity.cs',
    ROOT / 'Packages/dev.unity2foxglove.ros2forunity.runtime.lyrical.win64/Runtime/Ros2ForUnity/Scripts/ROS2ForUnity.cs',
]


class H04PostInitCleanupTests(unittest.TestCase):
    """R4.1 regression contract."""

    def test_constructor_post_init_failure_has_cleanup_boundary(self):
        """R4.1 regression contract."""
        for path in FILES:
            source = path.read_text(encoding='utf-8')
            validation = source.find('ValidateRmwImplementation(rmwImpl);')
            self.assertGreaterEqual(validation, 0, path.name)
            post_init_catch = source.find('catch', validation)
            self.assertGreater(post_init_catch, validation, path.name)
            cleanup_end = source.find('throw;', post_init_catch)
            self.assertGreater(cleanup_end, post_init_catch, path.name)
            cleanup = source[post_init_catch:cleanup_end]
            self.assertTrue(
                'Ros2cs.Shutdown()' in cleanup
                or 'DestroyROS2ForUnity();' in cleanup,
                path.name,
            )
            if 'runtime.jazzy' in str(path):
                self.assertIn('nativeInitialized || isInitialized', cleanup, path.name)
            else:
                self.assertIn('Ros2ForUnityNativePluginBootstrap.ResetNativeLibraryRegistration()', cleanup, path.name)
            initialized = source.find('isInitialized = true;')
            if 'runtime.jazzy' in str(path):
                self.assertGreaterEqual(initialized, 0, path.name)
                self.assertLess(initialized, validation, path.name)
            else:
                self.assertGreater(initialized, validation, path.name)

    def test_lifecycle_flags_are_not_left_owned_on_validation_failure(self):
        """R4.1 regression contract."""
        for path in FILES:
            source = path.read_text(encoding='utf-8')
            validation = source.find('ValidateRmwImplementation(rmwImpl);')
            self.assertGreaterEqual(validation, 0, path.name)
            if 'runtime.jazzy' in str(path):
                guard = source.find('if (nativeInitialized || isInitialized)', validation)
                destroy = source.find('DestroyROS2ForUnity();', guard)
                self.assertGreater(guard, validation, path.name)
                self.assertGreater(destroy, guard, path.name)
            else:
                self.assertGreater(
                    source.rfind('isInitialized = true;'),
                    validation,
                    path.name,
                )


if __name__ == '__main__':
    unittest.main()
