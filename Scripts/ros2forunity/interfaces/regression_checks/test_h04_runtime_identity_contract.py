import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[4]
SELECTOR = ROOT / "Packages/dev.unity2foxglove.ros2forunity/Editor/Ros2ForUnityRuntimeSelection.cs"

class H04RuntimeIdentityContractTests(unittest.TestCase):
    """R4.1 regression contract."""
    def test_descriptor_binds_manifest_identity_to_package_suffix(self):
        """R4.1 regression contract."""
        text = SELECTOR.read_text(encoding="utf-8")
        self.assertIn("capabilities.RosDistro", text)
        self.assertIn("packageRosDistro", text)
        self.assertIn("StringComparison.OrdinalIgnoreCase", text)
        self.assertIn("capabilities.Platform", text)
        self.assertIn("packagePlatform", text)

    def test_fastdds_mode_requires_native_rmw_payload(self):
        """R4.1 regression contract."""
        text = SELECTOR.read_text(encoding="utf-8")
        self.assertIn("FastDdsRmwImplementation", text)
        self.assertIn('"rmw_fastrtps_cpp"', text)
        self.assertIn("WindowsNativePluginRelativeDirectory", text)
        self.assertIn("HasNativeLibrary", text)

if __name__ == "__main__":
    unittest.main()
