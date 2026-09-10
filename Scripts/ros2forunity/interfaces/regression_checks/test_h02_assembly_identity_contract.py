from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[4]
MODEL = ROOT / "Packages/dev.unity2foxglove.ros2forunity/Editor/Native/FoxRun/FoxRunR2fuGenerationModel.cs"


class H02AssemblyIdentityContractTests(unittest.TestCase):
    """R4.1 source-level ambiguity guard for same-FQN managed payloads."""

    def test_payload_resolution_fails_closed_on_ambiguous_assembly_matches(self):
        text = MODEL.read_text(encoding="utf-8")
        self.assertIn("assemblyMatches", text)
        self.assertIn("assemblyMatches.Count > 1", text)
        self.assertIn("return null", text)


if __name__ == "__main__":
    unittest.main()
