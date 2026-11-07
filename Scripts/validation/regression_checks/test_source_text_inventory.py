"""Regression tests for source-text assertion inventory classification."""

import tempfile
import unittest
from pathlib import Path

from Scripts.validation.source_text_inventory import classify, inventory, write_report


class SourceTextInventoryTests(unittest.TestCase):
    """Verify inventory output and the A/B/C authoring classification."""

    def test_classification_distinguishes_boundary_wiring_and_implementation(self):
        """Classify representative target paths."""
        self.assertEqual("A", classify("Runtime/Core/Foo.cs", "if (!source.Contains(\"UnityEngine\"))"))
        self.assertEqual("B", classify(".github/workflows/check.yml", "source.Contains(\"run_ci\")"))
        self.assertEqual("C", classify("Runtime/Core/Foo.cs", "source.Contains(\"privateField\")"))

    def test_inventory_writes_csv_and_summary(self):
        """Scan a fixture package and write deterministic reports."""
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            source = root / "Packages" / "fixture" / "Tests" / "Unit" / "Sample.cs"
            source.parent.mkdir(parents=True)
            source.write_text(
                'var source = ReadRequiredRepoText("Runtime/Foo.cs");\n'
                'Assert.Contains("token", source);\n'
                'Assert.Contains("ordinary", exception.Message);\n',
                encoding="utf-8",
            )
            rows = inventory(root)
            self.assertEqual(2, len(rows))
            self.assertEqual("C", rows[0]["class"])
            self.assertEqual("Runtime/Foo.cs", rows[1]["target"])
            self.assertEqual("", rows[1]["validation_flag"])
            output = root / "build" / "inventory"
            write_report(rows, output)
            self.assertTrue((output / "source_text_inventory.csv").is_file())
            self.assertIn("Class C implementation: 2", (output / "SUMMARY.md").read_text(encoding="utf-8"))


if __name__ == "__main__":
    unittest.main()
