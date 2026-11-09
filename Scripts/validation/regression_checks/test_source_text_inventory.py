"""Regression tests for source-text assertion inventory classification."""

import tempfile
import unittest
from pathlib import Path

from Scripts.validation.source_text_inventory import (
    _build_xunit_index,
    _checked_expression_signature,
    _match_base_rows,
    classify,
    inventory,
    write_report,
)


class SourceTextInventoryTests(unittest.TestCase):
    """Verify inventory output and the A/B/C authoring classification."""

    def test_classification_distinguishes_boundary_wiring_and_implementation(self):
        """Classify representative target paths."""
        self.assertEqual("A", classify("Runtime/Core/Foo.cs", "if (!source.Contains(\"UnityEngine\"))"))
        self.assertEqual("C", classify("Runtime/Core/Foo.cs", 'source.Contains("model.Types.Count != 1")'))
        self.assertEqual("B", classify(".github/workflows/check.yml", "source.Contains(\"run_ci\")"))
        self.assertEqual("C", classify("Runtime/Core/Foo.cs", "source.Contains(\"privateField\")"))

    def test_base_matching_ignores_line_shifts_and_legacy_token_shape(self):
        """Match unchanged rows after line insertion and parser token expansion."""
        base = [{"file": "Validation.cs", "validation_flag": "--phase", "label": "", "target": "Runtime/Foo.cs", "token": 'source.Contains("needle", StringComparison.Ordinal)'}]
        head = [{"file": "Validation.cs", "validation_flag": "--phase", "label": "", "target": "Runtime/Foo.cs", "token": 'Assert.Contains("needle", source, StringComparison.Ordinal)'}]
        matches, missing = _match_base_rows(base, head)
        self.assertEqual(1, len(matches))
        self.assertFalse(missing)

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

    def test_scope_partial_coverage_and_comment_only_detection(self):
        """Resolve repeated local names, partial product types, and comment-only literals."""
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            runtime = root / "Packages" / "fixture" / "Tests" / "Runtime"
            runtime.mkdir(parents=True)
            product = root / "Packages" / "fixture" / "Runtime" / "Foo.Part.cs"
            product.parent.mkdir(parents=True)
            product.write_text("public partial class Foo {}\n", encoding="utf-8")
            (root / "Packages" / "fixture" / "Runtime" / "Bar.cs").write_text("public sealed class Bar {}\n", encoding="utf-8")
            (root / "Packages" / "fixture" / "Runtime" / "Comment.cs").write_text("// comment-only marker\npublic sealed class Comment {}\n", encoding="utf-8")
            unit = root / "Packages" / "fixture" / "Tests" / "Unit"
            unit.mkdir(parents=True)
            (unit / "FooTests.cs").write_text("public sealed class FooTests { private Foo _foo; }\n", encoding="utf-8")
            (runtime / "ScopeValidation.cs").write_text(
                "public static class ScopeValidation {\n"
                "    public static void Validate() {\n"
                '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Foo.Part.cs");\n'
                '        Check(source.Contains("Foo"), "scope first");\n'
                "    }\n"
                "    public static void ValidateAgain() {\n"
                '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Bar.cs");\n'
                '        Check(source.Contains("Bar"), "scope second");\n'
                "    }\n"
                "    public static void ValidateComment() {\n"
                '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Comment.cs");\n'
                '        Check(source.Contains("comment-only marker"), "comment");\n'
                "    }\n"
                "}\n",
                encoding="utf-8",
            )
            registry = root / "Packages" / "dev.unity2foxglove.sdk" / "Tests" / "Runtime" / "PhaseValidationRegistry.cs"
            registry.parent.mkdir(parents=True, exist_ok=True)
            registry.write_text('Ci("--phase-scope", "fixture", ScopeValidation.Validate, evidence: ValidationEvidence.Behavior),\n', encoding="utf-8")
            rows = inventory(root)
            by_line = {row["line"]: row for row in rows if row["file"].endswith("ScopeValidation.cs")}
            self.assertEqual("Packages/fixture/Runtime/Foo.Part.cs", by_line[4]["target"])
            self.assertEqual("Packages/fixture/Runtime/Bar.cs", by_line[8]["target"])
            self.assertIn("FooTests.cs", by_line[4]["xunit_coverage"])
            self.assertEqual("Packages/fixture/Runtime/Comment.cs", by_line[12]["target"])
            self.assertTrue(by_line[12]["comment_only"])

    def test_source_assertion_forms_are_filtered_and_classified(self):
        """Include source assertions while excluding ordinary string operations."""
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            runtime = root / "Packages" / "fixture" / "Tests" / "Runtime"
            runtime.mkdir(parents=True)
            product = root / "Packages" / "fixture" / "Runtime" / "Sensors" / "LidarModelRegistry.cs"
            product.parent.mkdir(parents=True)
            product.write_text("public sealed class LidarModelRegistry {}\n", encoding="utf-8")
            (runtime / "FormsValidation.cs").write_text(
                "public static class FormsValidation {\n"
                "    public static void Validate() {\n"
                '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Sensors/LidarModelRegistry.cs");\n'
                '        var message = "ordinary";\n'
                '        Check(source.Contains("LidarModelRegistry"), "positive");\n'
                '        Assert.DoesNotContain("obsolete", source);\n'
                '        Check(!source.Contains("forbidden"), "negative");\n'
                '        Check(source.IndexOf("LidarModelRegistry") >= 0, "source index");\n'
                '        Assert.Contains("ordinary", message);\n'
                '        Check(message.IndexOf("ordinary") >= 0, "ordinary index");\n'
                "    }\n"
                "}\n",
                encoding="utf-8",
            )
            registry = root / "Packages" / "dev.unity2foxglove.sdk" / "Tests" / "Runtime" / "PhaseValidationRegistry.cs"
            registry.parent.mkdir(parents=True, exist_ok=True)
            registry.write_text(
                'Ci("--phase-forms", "fixture", FormsValidation.Validate, evidence: ValidationEvidence.Behavior),\n',
                encoding="utf-8",
            )
            rows = inventory(root)
            self.assertEqual(4, len(rows))
            self.assertEqual({"C", "A"}, {row["class"] for row in rows})
            self.assertTrue(all(row["target"].endswith("LidarModelRegistry.cs") for row in rows))
            self.assertTrue(any(row["class"] == "A" and "DoesNotContain" in row["token"] for row in rows))
            self.assertTrue(any(row["class"] == "A" and "forbidden" in row["token"] for row in rows))
            self.assertTrue(any("source.IndexOf" in row["token"] for row in rows))

    def test_multiline_loaders_and_unresolved_targets_are_explicit(self):
        """Resolve complete multiline reader calls and mark unresolved paths explicitly."""
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            runtime = root / "Packages" / "fixture" / "Tests" / "Runtime"
            runtime.mkdir(parents=True)
            for name in ("First.cs", "Second.cs"):
                product = root / "Packages" / "fixture" / "Runtime" / name
                product.parent.mkdir(parents=True, exist_ok=True)
                product.write_text(f"public sealed class {product.stem} {{}}\n", encoding="utf-8")
            (root / "Packages" / "fixture" / "Runtime" / "DirectoryTarget").mkdir(parents=True)
            (runtime / "MultilineValidation.cs").write_text(
                "public static class MultilineValidation {\n"
                "    public static void Validate() {\n"
                "        var first = TestSources.Text(\n"
                '            "Packages/fixture/Runtime/First.cs");\n'
                "        var second = TestSources.Text(\n"
                '            "Packages/fixture/Runtime/Second.cs");\n'
                '        Assert.Contains("First", first);\n'
                '        Assert.Contains("Second", second);\n'
                "        var unresolved = TestSources.Text(\n"
                "            ResolvePath());\n"
                '        Assert.Contains("unknown", unresolved);\n'
                "        var directory = TestSources.Text(\n"
                '            "Packages/fixture/Runtime/DirectoryTarget");\n'
                '        Assert.Contains("directory", directory);\n'
                '        Assert.Contains("inline", TestSources.Text(ResolvePath()));\n'
                "    }\n"
                "}\n",
                encoding="utf-8",
            )
            registry = root / "Packages" / "dev.unity2foxglove.sdk" / "Tests" / "Runtime" / "PhaseValidationRegistry.cs"
            registry.parent.mkdir(parents=True, exist_ok=True)
            registry.write_text(
                'Ci("--phase-multiline", "fixture", MultilineValidation.Validate, evidence: ValidationEvidence.Behavior),\n',
                encoding="utf-8",
            )
            rows = inventory(root)
            by_line = {row["line"]: row for row in rows if row["file"].endswith("MultilineValidation.cs")}
            self.assertEqual("Packages/fixture/Runtime/First.cs", by_line[7]["target"])
            self.assertEqual("Packages/fixture/Runtime/Second.cs", by_line[8]["target"])
            self.assertEqual("UNRESOLVED", by_line[11]["class"])
            self.assertEqual("<unresolved>", by_line[11]["target"])
            self.assertEqual("UNKNOWN", by_line[14]["class"])
            self.assertEqual("<directory>", by_line[14]["target"])
            self.assertTrue(any(row["class"] == "UNRESOLVED" and "inline" in str(row["token"]) for row in rows))

    def test_scope_and_multiline_receiver_resolution_are_fail_closed(self):
        """Keep local symbols scoped, preserve combined targets, and inspect multiline literals."""
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            runtime = root / "Packages" / "fixture" / "Tests" / "Runtime"
            runtime.mkdir(parents=True)
            for name in ("First.cs", "Second.cs", "Comment.cs"):
                product = root / "Packages" / "fixture" / "Runtime" / name
                product.parent.mkdir(parents=True, exist_ok=True)
                content = "// comment marker\n" if name == "Comment.cs" else ""
                product.write_text(content + f"public sealed class {product.stem} {{}}\n", encoding="utf-8")
            (runtime / "ScopeValidation.cs").write_text(
                "public static class ScopeValidation {\n"
                "    private string source;\n"
                "    public static void ValidateFirst() {\n"
                '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/First.cs");\n'
                '        Check(source.Contains("First"), "first");\n'
                "    }\n"
                "    public static void ValidateCombined() {\n"
                '        var first = ReadRequiredRepoText("Packages/fixture/Runtime/First.cs");\n'
                '        var second = ReadRequiredRepoText("Packages/fixture/Runtime/Second.cs");\n'
                "        var combined = first + second;\n"
                '        Check(combined.Contains("combined"), "combined");\n'
                "    }\n"
                "    public static void ValidateComment() {\n"
                '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Comment.cs");\n'
                "        Check(source.Contains(\n"
                '            "comment marker"), "comment");\n'
                "        Check(!source.Contains(\n"
                '            "comment marker"), "negative comment");\n'
                "    }\n"
                "    public static void ValidateField() {\n"
                '        Check(source.Contains("field"), "field");\n'
                "    }\n"
                "}\n",
                encoding="utf-8",
            )
            registry = root / "Packages" / "dev.unity2foxglove.sdk" / "Tests" / "Runtime" / "PhaseValidationRegistry.cs"
            registry.parent.mkdir(parents=True, exist_ok=True)
            registry.write_text(
                'Ci("--phase-scope-fail-closed", "fixture", ScopeValidation.ValidateFirst, evidence: ValidationEvidence.Behavior),\n',
                encoding="utf-8",
            )
            rows = inventory(root)
            first = next(row for row in rows if "First" in str(row["token"]))
            combined = next(row for row in rows if "combined" in str(row["token"]))
            comment = next(row for row in rows if "comment marker" in str(row["token"]))
            self.assertEqual("Packages/fixture/Runtime/First.cs", first["target"])
            self.assertEqual(
                "Packages/fixture/Runtime/First.cs;Packages/fixture/Runtime/Second.cs",
                combined["target"],
            )
            self.assertTrue(comment["comment_only"])
            self.assertTrue(any(row["class"] == "A" for row in rows if "comment marker" in str(row["token"])))
            field_rows = [row for row in rows if "field" in str(row["token"])]
            self.assertEqual(1, len(field_rows))
            self.assertEqual("UNRESOLVED", field_rows[0]["class"])

    def test_derived_receivers_and_pseudo_readers_are_handled(self):
        """Keep source-derived receiver assertions and ignore reader text in comments or strings."""
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            runtime = root / "Packages" / "fixture" / "Tests" / "Runtime"
            runtime.mkdir(parents=True)
            for name in ("A.cs", "B.cs", "C.cs"):
                product = root / "Packages" / "fixture" / "Runtime" / name
                product.parent.mkdir(parents=True, exist_ok=True)
                product.write_text(f"public sealed class {product.stem} {{}}\n", encoding="utf-8")
            (runtime / "DerivedValidation.cs").write_text(
                "public static class DerivedValidation {\n"
                "    public static void Validate() {\n"
                '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/A.cs");\n'
                '        Check(source.Substring(0, 1).Contains("A"), "substring");\n'
                '        Check(ExtractMethodBody(source, "Method").Contains("A"), "helper");\n'
                '        Check(Slice(source, "start", "}").Contains("A"), "slice");\n'
                '        Check(ReadRequiredRepoText("Packages/fixture/Runtime/B.cs").Contains("B"), "inline");\n'
                '        var text = "TestSources.Text(\\"Packages/fixture/Runtime/C.cs\\")";\n'
                '        Assert.Contains("text", text);\n'
                "        // var fake = TestSources.Text(\"Packages/fixture/Runtime/C.cs\");\n"
                "    }\n"
                "}\n",
                encoding="utf-8",
            )
            registry = root / "Packages" / "dev.unity2foxglove.sdk" / "Tests" / "Runtime" / "PhaseValidationRegistry.cs"
            registry.parent.mkdir(parents=True, exist_ok=True)
            registry.write_text(
                'Ci("--phase-derived", "fixture", DerivedValidation.Validate, evidence: ValidationEvidence.Behavior),\n',
                encoding="utf-8",
            )
            rows = inventory(root)
            self.assertEqual(4, len(rows))
            targets = [row["target"] for row in rows]
            self.assertEqual("Packages/fixture/Runtime/A.cs", targets[0])
            self.assertEqual("Packages/fixture/Runtime/A.cs", targets[1])
            self.assertEqual("Packages/fixture/Runtime/A.cs", targets[2])
            self.assertEqual("Packages/fixture/Runtime/B.cs", targets[3])

    def test_multiple_source_calls_on_one_line_are_all_reported(self):
        """Emit each source-backed call even when ordinary calls share its expression."""
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            runtime = root / "Packages" / "fixture" / "Tests" / "Runtime"
            runtime.mkdir(parents=True)
            product = root / "Packages" / "fixture" / "Runtime" / "Source.cs"
            product.parent.mkdir(parents=True)
            product.write_text("public sealed class Source {}\n", encoding="utf-8")
            (runtime / "MultipleValidation.cs").write_text(
                "public static class MultipleValidation {\n"
                "    public static void Validate() {\n"
                '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Source.cs");\n'
                '        var ordinary = "ordinary";\n'
                '        Check(ordinary.Contains("ordinary") && source.Contains("one") && source.Contains("two"));\n'
                '        Check(source.Contains("three") || source.Contains("four"));\n'
                '        Check(!source.Contains("negative") && source.Contains("positive"));\n'
                "    }\n"
                "}\n",
                encoding="utf-8",
            )
            registry = root / "Packages" / "dev.unity2foxglove.sdk" / "Tests" / "Runtime" / "PhaseValidationRegistry.cs"
            registry.parent.mkdir(parents=True, exist_ok=True)
            registry.write_text(
                'Ci("--phase-multiple", "fixture", MultipleValidation.Validate, evidence: ValidationEvidence.Behavior),\n',
                encoding="utf-8",
            )
            rows = inventory(root)
            tokens = [str(row["token"]) for row in rows]
            self.assertEqual(6, len(rows))
            self.assertTrue(any('Contains("one")' in token for token in tokens))
            self.assertTrue(any('Contains("two")' in token for token in tokens))
            self.assertTrue(any('Contains("three")' in token for token in tokens))
            self.assertTrue(any('Contains("four")' in token for token in tokens))
            self.assertEqual("A", next(row["class"] for row in rows if 'Contains("negative")' in row["token"]))
            self.assertEqual("C", next(row["class"] for row in rows if 'Contains("positive")' in row["token"]))

    def test_comment_only_ignores_assertion_message_literals(self):
        """Only literals passed to the source assertion are checked against target comments."""
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            runtime = root / "Packages" / "fixture" / "Tests" / "Runtime"
            runtime.mkdir(parents=True)
            product = root / "Packages" / "fixture" / "Runtime" / "Product.cs"
            product.parent.mkdir(parents=True)
            product.write_text(
                'public sealed class Product { public const string Real = "real"; }\n'
                '// assertion message only\n',
                encoding="utf-8",
            )
            (runtime / "CommentLiteralValidation.cs").write_text(
                "public static class CommentLiteralValidation {\n"
                "    public static void Validate() {\n"
                '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                '        Check(source.Contains("real"), "assertion message only");\n'
                "    }\n"
                "}\n",
                encoding="utf-8",
            )
            registry = root / "Packages" / "dev.unity2foxglove.sdk" / "Tests" / "Runtime" / "PhaseValidationRegistry.cs"
            registry.parent.mkdir(parents=True, exist_ok=True)
            registry.write_text(
                'Ci("--phase-comment-literal", "fixture", CommentLiteralValidation.Validate, evidence: ValidationEvidence.Behavior),\n',
                encoding="utf-8",
            )
            rows = inventory(root)
            self.assertEqual(1, len(rows))
            self.assertFalse(rows[0]["comment_only"])

    def test_ambiguous_parameter_targets_fail_closed(self):
        """Do not merge different reader paths under one helper parameter name."""
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            runtime = root / "Packages" / "fixture" / "Tests" / "Runtime"
            runtime.mkdir(parents=True)
            for name in ("A.cs", "B.cs"):
                product = root / "Packages" / "fixture" / "Runtime" / name
                product.parent.mkdir(parents=True, exist_ok=True)
                product.write_text(f"public sealed class {product.stem} {{}}\n", encoding="utf-8")
            (runtime / "ParameterValidation.cs").write_text(
                "public static class ParameterValidation {\n"
                '    private static void CheckSource(string source) { Check(source.Contains("marker")); }\n'
                '    public static void Validate() {\n'
                '        var first = ReadRequiredRepoText("Packages/fixture/Runtime/A.cs");\n'
                '        var second = ReadRequiredRepoText("Packages/fixture/Runtime/B.cs");\n'
                "        CheckSource(first);\n"
                "        CheckSource(second);\n"
                "    }\n"
                "}\n",
                encoding="utf-8",
            )
            registry = root / "Packages" / "dev.unity2foxglove.sdk" / "Tests" / "Runtime" / "PhaseValidationRegistry.cs"
            registry.parent.mkdir(parents=True, exist_ok=True)
            registry.write_text(
                'Ci("--phase-parameter-scope", "fixture", ParameterValidation.Validate, evidence: ValidationEvidence.Behavior),\n',
                encoding="utf-8",
            )
            rows = inventory(root)
            parameter_rows = [row for row in rows if "marker" in str(row["token"])]
            self.assertEqual(1, len(parameter_rows))
            self.assertEqual("UNRESOLVED", parameter_rows[0]["class"])

    def test_assertion_expression_signature_tracks_checked_argument_changes(self):
        """Treat a changed checked expression as a changed inventory row."""
        original = _checked_expression_signature('Assert.Contains("Secure", source)')
        spaced = _checked_expression_signature('Assert.Contains( "Secure", source)')
        weakened = _checked_expression_signature(
            'Assert.Contains("Secure", "Secure" + source.GetHashCode().ToString())'
        )
        self.assertEqual(original, spaced)
        self.assertNotEqual(original, weakened)

    def test_reachability_tracks_preprocessor_and_unbraced_branches(self):
        """Expose preprocessor and unbraced control-flow changes in row metadata."""
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            runtime = root / "Packages" / "fixture" / "Tests" / "Runtime"
            runtime.mkdir(parents=True)
            product = root / "Packages" / "fixture" / "Runtime" / "Product.cs"
            product.parent.mkdir(parents=True)
            product.write_text("public sealed class Product {}\n", encoding="utf-8")
            validation = runtime / "ReachabilityValidation.cs"
            validation.write_text(
                "public static class ReachabilityValidation {\n"
                '    public static void Validate() {\n'
                '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                "#if false\n"
                '        Check(source.Contains("preprocessor"));\n'
                "#endif\n"
                "        if (false)\n"
                '            Check(source.Contains("unbraced"));\n'
                '        Check(source.Contains("live"));\n'
                "    }\n"
                "}\n",
                encoding="utf-8",
            )
            registry = root / "Packages" / "dev.unity2foxglove.sdk" / "Tests" / "Runtime" / "PhaseValidationRegistry.cs"
            registry.parent.mkdir(parents=True, exist_ok=True)
            registry.write_text(
                'Ci("--phase-reachability", "fixture", ReachabilityValidation.Validate, evidence: ValidationEvidence.Behavior),\n',
                encoding="utf-8",
            )
            rows = inventory(root)
            by_token = {str(row["token"]): row for row in rows}
            self.assertIn('source.Contains("preprocessor")', by_token)
            self.assertIn('source.Contains("unbraced")', by_token)
            self.assertIn('source.Contains("live")', by_token)
            preprocessor = by_token['source.Contains("preprocessor")']
            unbraced = by_token['source.Contains("unbraced")']
            live = by_token['source.Contains("live")']
            self.assertNotEqual(preprocessor["reachability"], live["reachability"])
            self.assertNotEqual(unbraced["reachability"], live["reachability"])

    def test_source_reassignment_invalidates_original_reader_target(self):
        """Do not retain a source-file target after its validation variable is overwritten."""
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            runtime = root / "Packages" / "fixture" / "Tests" / "Runtime"
            runtime.mkdir(parents=True)
            product = root / "Packages" / "fixture" / "Runtime" / "Product.cs"
            product.parent.mkdir(parents=True)
            product.write_text("public sealed class Product {}\n", encoding="utf-8")
            (runtime / "ReassignmentValidation.cs").write_text(
                "public static class ReassignmentValidation {\n"
                '    public static void Validate() {\n'
                '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                '        source = "needle";\n'
                '        Check(source.Contains("needle"));\n'
                "    }\n"
                "}\n",
                encoding="utf-8",
            )
            registry = root / "Packages" / "dev.unity2foxglove.sdk" / "Tests" / "Runtime" / "PhaseValidationRegistry.cs"
            registry.parent.mkdir(parents=True, exist_ok=True)
            registry.write_text(
                'Ci("--phase-reassignment", "fixture", ReassignmentValidation.Validate, evidence: ValidationEvidence.Behavior),\n',
                encoding="utf-8",
            )
            rows = inventory(root)
            self.assertEqual(1, len(rows))
            self.assertEqual("<unresolved>", rows[0]["target"])
            self.assertEqual("UNRESOLVED", rows[0]["class"])

if __name__ == "__main__":
    unittest.main()
