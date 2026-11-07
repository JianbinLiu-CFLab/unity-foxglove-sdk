"""Regression tests for source-text assertion inventory classification."""

import tempfile
import unittest
from pathlib import Path

from Scripts.validation.source_text_inventory import _build_xunit_index, classify, inventory, write_report


class SourceTextInventoryTests(unittest.TestCase):
    """Verify inventory output and the A/B/C authoring classification."""

    def test_classification_distinguishes_boundary_wiring_and_implementation(self):
        """Classify representative target paths."""
        self.assertEqual("A", classify("Runtime/Core/Foo.cs", "if (!source.Contains(\"UnityEngine\"))"))
        self.assertEqual("C", classify("Runtime/Core/Foo.cs", 'source.Contains("model.Types.Count != 1")'))
        self.assertEqual("B", classify(".github/workflows/check.yml", "source.Contains(\"run_ci\")"))
        self.assertEqual("C", classify("Runtime/Core/Foo.cs", "source.Contains(\"privateField\")"))

    def test_inventory_writes_csv_and_summary(self):
        """Scan a fixture package and write deterministic reports."""
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            product = root / "Packages" / "fixture" / "Runtime" / "Foo.cs"
            product.parent.mkdir(parents=True)
            product.write_text("public sealed class Foo {}\n", encoding="utf-8")
            foo_tests = root / "Packages" / "fixture" / "Tests" / "Unit" / "FooTests.cs"
            foo_tests.parent.mkdir(parents=True, exist_ok=True)
            foo_tests.write_text("public sealed class FooTests { private Foo _foo; }\n", encoding="utf-8")
            (foo_tests.parent / "CommentOnlyTests.cs").write_text(
                "// Foo\n/* Foo */\npublic sealed class CommentOnlyTests {}\n",
                encoding="utf-8",
            )
            source = root / "Packages" / "fixture" / "Tests" / "Unit" / "Sample.cs"
            source.parent.mkdir(parents=True, exist_ok=True)
            source.write_text(
                'var source = ReadRequiredRepoText("Runtime/Bar.cs");\n'
                'Assert.Contains("token", source);\n'
                'Assert.Contains("ordinary", exception.Message);\n',
                encoding="utf-8",
            )
            (source.parent / "Nearby.cs").write_text(
                'var first = ReadRequiredRepoText("Runtime/First.cs");\n'
                'var second = ReadRequiredRepoText("Runtime/Second.cs");\n'
                'Assert.Contains("needle", second);\n'
                'var quotedSource = ReadRequiredRepoText("Runtime/String.cs");\n'
                'Assert.Contains("value /* literal // text", quotedSource);\n'
                'const string PackageRoot = "Packages/fixture";\n'
                'var manifest = ReadRequiredRepoText(PackageRoot + "/Runtime/manifest.json");\n'
                'var native = ReadRequiredRepoText(PackageRoot + "/Runtime/Native.cs");\n'
                'Assert.Contains("manifest", manifest);\n',
                encoding="utf-8",
            )
            shared = source.parent / "PartialValidation.Shared.cs"
            shared.write_text(
                'public static partial class PartialValidation {\n'
                '    private const string SharedPath =\n'
                '        "Runtime/Shared.cs";\n'
                '}\n',
                encoding="utf-8",
            )
            (source.parent / "PartialValidation.cs").write_text(
                'public static partial class PartialValidation {\n'
                '    public static void Validate() {\n'
                '        var shared = ReadRequiredRepoText(SharedPath);\n'
                '        Assert.Contains("model.Types.Count != 1", shared);\n'
                '    }\n'
                '}\n',
                encoding="utf-8",
            )
            registry = root / "Packages" / "dev.unity2foxglove.sdk" / "Tests" / "Runtime" / "PhaseValidationRegistry.cs"
            registry.parent.mkdir(parents=True, exist_ok=True)
            registry.write_text(
                'Ci("--phase-fixture",\n'
                '    "fixture", PartialValidation.Validate, evidence: ValidationEvidence.Behavior),\n',
                encoding="utf-8",
            )
            rows = inventory(root)
            self.assertEqual(5, len(rows))
            by_file_line = {(row["file"], row["line"]): row for row in rows}
            self.assertEqual("Runtime/Bar.cs", by_file_line[("Packages/fixture/Tests/Unit/Sample.cs", 2)]["target"])
            self.assertEqual("Runtime/Second.cs", by_file_line[("Packages/fixture/Tests/Unit/Nearby.cs", 3)]["target"])
            self.assertFalse(by_file_line[("Packages/fixture/Tests/Unit/Nearby.cs", 5)]["comment_only"])
            self.assertEqual("Packages/fixture/Runtime/manifest.json", by_file_line[("Packages/fixture/Tests/Unit/Nearby.cs", 9)]["target"])
            self.assertEqual("Runtime/Shared.cs", by_file_line[("Packages/fixture/Tests/Unit/PartialValidation.cs", 4)]["target"])
            self.assertEqual("--phase-fixture", by_file_line[("Packages/fixture/Tests/Unit/PartialValidation.cs", 4)]["validation_flag"])
            self.assertEqual("C", by_file_line[("Packages/fixture/Tests/Unit/PartialValidation.cs", 4)]["class"])
            self.assertEqual("C", by_file_line[("Packages/fixture/Tests/Unit/Nearby.cs", 5)]["class"])
            self.assertEqual(
                ["Packages/fixture/Tests/Unit/FooTests.cs"],
                _build_xunit_index(root).get("Foo"),
            )
            output = root / "build" / "inventory"
            write_report(rows, output)
            self.assertTrue((output / "source_text_inventory.csv").is_file())
            self.assertIn("Class C implementation: 5", (output / "SUMMARY.md").read_text(encoding="utf-8"))


if __name__ == "__main__":
    unittest.main()
