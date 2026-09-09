from pathlib import Path
import unittest
ROOT = Path.cwd()
REFLECTION = ROOT / "Packages/dev.unity2foxglove.ros2forunity/Editor/Native/FoxRun/FoxRunReflectionRos2CustomDtoShapeBuilder.cs"
ROSLYN = ROOT / "Packages/dev.unity2foxglove.ros2forunity/Editor/SourceGenerators/src/FoxRunRoslynRos2CustomDtoShapeBuilder.cs"
REFLECTION_MSG = ROOT / "Packages/dev.unity2foxglove.ros2forunity/Editor/Native/FoxRun/FoxRunReflectionRos2MessageShapeBuilder.cs"
ROSLYN_MSG = ROOT / "Packages/dev.unity2foxglove.ros2forunity/Editor/SourceGenerators/src/FoxRunRoslynRos2MessageShapeBuilder.cs"
class H02019InheritedMemberContractTests(unittest.TestCase):
    def test_reflection_builder_walks_public_base_chain(self):
        source = REFLECTION.read_text(encoding="utf-8"); self.assertIn("for (var current = type; current != null && current != typeof(object); current = current.BaseType)", source); self.assertIn("var seen = new HashSet<string>(StringComparer.Ordinal);", source)
    def test_roslyn_builder_walks_public_base_chain(self):
        source = ROSLYN.read_text(encoding="utf-8"); self.assertIn("for (var current = type; current != null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)", source); self.assertIn("var seen = new HashSet<string>(StringComparer.Ordinal);", source)
    def test_reflection_message_builder_walks_public_base_chain(self):
        source = REFLECTION_MSG.read_text(encoding="utf-8"); self.assertIn("for (var current = type; current != null && current != typeof(object); current = current.BaseType)", source); self.assertIn("var seen = new HashSet<string>(StringComparer.Ordinal);", source)
    def test_roslyn_message_builder_walks_public_base_chain(self):
        source = ROSLYN_MSG.read_text(encoding="utf-8"); self.assertIn("for (var current = type; current != null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)", source); self.assertIn("var names = new HashSet<string>(StringComparer.Ordinal);", source)
if __name__ == '__main__': unittest.main()
