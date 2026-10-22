import pathlib
import unittest
ROOT = pathlib.Path(__file__).resolve().parents[4]
SELECTOR = ROOT / 'Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityRuntimeSelection.cs'
SELECTOR_SECTIONS = (
    'SessionAndCommunication.cs',
    'RestartAndEnvironment.cs',
    'Discovery.cs',
    'Manifest.cs',
    'EnvironmentLease.cs',
)

def selector_text():
    section_dir = SELECTOR.parent / 'Decomposed' / 'Ros2ForUnityRuntimeSelection'
    actual = tuple(sorted(path.name for path in section_dir.glob('*.cs')))
    expected = tuple(sorted(SELECTOR_SECTIONS))
    if actual != expected:
        raise AssertionError(f'Unexpected runtime-selection sections: {actual!r}')
    return SELECTOR.read_text(encoding='utf-8') + ''.join(
        (section_dir / name).read_text(encoding='utf-8') for name in SELECTOR_SECTIONS
    )

class H04RuntimeIdentityContractTests(unittest.TestCase):
    """R4.1 regression contract."""

    def test_descriptor_binds_manifest_identity_to_package_suffix(self):
        """R4.1 regression contract."""
        text = selector_text()
        self.assertIn('capabilities.RosDistro', text)
        self.assertIn('packageRosDistro', text)
        self.assertIn('StringComparison.OrdinalIgnoreCase', text)
        self.assertIn('capabilities.Platform', text)
        self.assertIn('packagePlatform', text)

    def test_fastdds_mode_requires_native_rmw_payload(self):
        """R4.1 regression contract."""
        text = selector_text()
        self.assertIn('FastDdsRmwImplementation', text)
        self.assertIn('"rmw_fastrtps_cpp"', text)
        self.assertIn('WindowsNativePluginRelativeDirectory', text)
        self.assertIn('HasNativeLibrary', text)
if __name__ == '__main__':
    unittest.main()
