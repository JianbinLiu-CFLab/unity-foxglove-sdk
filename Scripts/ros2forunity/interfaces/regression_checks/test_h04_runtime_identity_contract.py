import pathlib
import unittest
ROOT = pathlib.Path(__file__).resolve().parents[4]
SELECTOR = ROOT / 'Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityRuntimeSelection.cs'
SECTION_ROOT = SELECTOR.parent / 'Decomposed' / 'Ros2ForUnityRuntimeSelection'
SECTION_NAMES = (
    'RuntimeDescriptor.cs',
    'SelectionStatus.cs',
    'SessionAndCommunication.cs',
    'RestartAndEnvironment.cs',
    'Discovery.cs',
    'Manifest.cs',
    'EnvironmentLease.cs',
)

def read_selector_source():
    """Read the facade and its ordered Phase192 source sections."""
    text = SELECTOR.read_text(encoding='utf-8')
    return text + ''.join(
        path.read_text(encoding='utf-8')
        for path in (SECTION_ROOT / name for name in SECTION_NAMES))

class H04RuntimeIdentityContractTests(unittest.TestCase):
    """R4.1 regression contract."""

    def test_descriptor_binds_manifest_identity_to_package_suffix(self):
        """R4.1 regression contract."""
        text = read_selector_source()
        self.assertIn('capabilities.RosDistro', text)
        self.assertIn('packageRosDistro', text)
        self.assertIn('StringComparison.OrdinalIgnoreCase', text)
        self.assertIn('capabilities.Platform', text)
        self.assertIn('packagePlatform', text)

    def test_fastdds_mode_requires_native_rmw_payload(self):
        """R4.1 regression contract."""
        text = read_selector_source()
        self.assertIn('FastDdsRmwImplementation', text)
        self.assertIn('"rmw_fastrtps_cpp"', text)
        self.assertIn('WindowsNativePluginRelativeDirectory', text)
        self.assertIn('HasNativeLibrary', text)
if __name__ == '__main__':
    unittest.main()
