from pathlib import Path
import subprocess, sys, unittest
ROOT = Path.cwd()
REF = ROOT / 'Packages/dev.unity2foxglove.ros2forunity/Editor/Native/FoxRun/FoxRunReflectionRos2CustomDtoShapeBuilder.cs'
ROS = ROOT / 'Packages/dev.unity2foxglove.ros2forunity/Editor/SourceGenerators/src/FoxRunRoslynRos2CustomDtoShapeBuilder.cs'

class H02022NullSequenceContractTests(unittest.TestCase):
    """R4.1 regression contract."""

    def test_reflection_rejects_reference_sequence_elements(self):
        """R4.1 regression contract."""
        s = REF.read_text(encoding='utf8')
        self.assertIn('if (!sequenceElement.IsValueType)', s)
        self.assertIn('per-element presence', s)

    def test_roslyn_rejects_reference_sequence_elements(self):
        """R4.1 regression contract."""
        s = ROS.read_text(encoding='utf8')
        self.assertIn('if (sequenceElement.IsReferenceType)', s)
        self.assertIn('per-element presence', s)
if __name__ == '__main__':
    unittest.main()
