import pathlib
import unittest
ROOT = pathlib.Path(__file__).resolve().parents[4]
SELECTOR = ROOT / 'Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityRuntimeSelection.cs'
SECTION_ROOT = SELECTOR.parent / 'Decomposed' / 'Ros2ForUnityRuntimeSelection'
def section_names():
    """Read and validate the facade's ordered Phase192 source-section marker."""
    marker = next(
        (line for line in SELECTOR.read_text(encoding='utf-8').splitlines()
         if 'Phase192 source sections:' in line),
        None,
    )
    if marker is None:
        raise AssertionError('Missing Phase192 source-section marker')
    names = tuple(name.strip() for name in marker.split(':', 1)[1].split('</remarks>', 1)[0].split(',') if name.strip())
    actual = tuple(sorted(path.name for path in SECTION_ROOT.glob('*.cs')))
    if set(names) != set(actual) or len(names) != len(actual):
        raise AssertionError(f'Source-section marker mismatch: expected {names}, actual {actual}')
    return names

def read_selector_source():
    """Read the facade and its ordered Phase192 source sections."""
    text = SELECTOR.read_text(encoding='utf-8')
    return text + ''.join(
        (SECTION_ROOT / name).read_text(encoding='utf-8')
        for name in section_names())

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
