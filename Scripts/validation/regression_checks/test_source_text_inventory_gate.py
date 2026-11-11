"""End-to-end regression tests for source-text inventory gate semantics."""

import contextlib
import io
import json
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

from Scripts.validation.source_text_inventory import inventory, main


class SourceTextInventoryGateTests(unittest.TestCase):
    """Verify gate semantics, parser binding, and cross-platform source scanning."""

    def test_ratchet_requires_exact_ack_for_weakening(self):
        """Require an exact acknowledgement for a target becoming unresolved."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(directory)
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            original = validation.read_text(encoding="utf-8")
            validation.write_text(
                original.replace(
                    'ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs")',
                    "ReadRequiredRepoText(ResolvePath())",
                ),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code)
            changes = [
                json.loads(item.split(" ", 1)[1])
                for item in output.splitlines()
                if item.startswith("SOURCE_TEXT_INVENTORY_UNACKNOWLEDGED ")
            ]
            for change in changes:
                change["reason"] = "The behavior test keeps the product path assertion executable."
            validation.write_text(original, encoding="utf-8")
            (root / "Scripts/validation/source_text_inventory_acknowledgements.json").write_text(
                json.dumps({"acknowledgements": changes}, indent=2) + "\n",
                encoding="utf-8",
            )
            subprocess.run(["git", "-C", str(root), "add", "-A"], check=True)
            subprocess.run(["git", "-C", str(root), "commit", "-q", "-m", "trusted-ack"], check=True)
            validation.write_text(
                original.replace(
                    'ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs")',
                    "ReadRequiredRepoText(ResolvePath())",
                ),
                encoding="utf-8",
            )
            code, _ = self._run_fixture_gate(root)
            self.assertEqual(0, code)
            validation.write_text(original, encoding="utf-8")
            validation.write_text(
                original.replace(
                    'ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs")',
                    "ReadRequiredRepoText(ResolvePath())",
                ),
                encoding="utf-8",
            )
            (root / "Scripts/validation/source_text_inventory_acknowledgements.json").write_text(
                json.dumps({"acknowledgements": [{**changes[0], "head_sha256": "0" * 64}]}, indent=2) + "\n",
                encoding="utf-8",
            )
            validation.write_text(original, encoding="utf-8")
            subprocess.run(["git", "-C", str(root), "add", "-A"], check=True)
            subprocess.run(["git", "-C", str(root), "commit", "-q", "-m", "invalid-trusted-ack"], check=True)
            validation.write_text(
                original.replace(
                    'ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs")',
                    "ReadRequiredRepoText(ResolvePath())",
                ),
                encoding="utf-8",
            )
            code, _ = self._run_fixture_gate(root)
            self.assertEqual(1, code)

    def test_checker_and_product_changes_do_not_reuse_head_rows(self):
        """Scan the base tree when checker and product inputs change together."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(directory)
            product = root / "Packages/fixture/Runtime/Product.cs"
            product.write_text(
                "public sealed class Renamed {\n" "    // Product\n" "}\n",
                encoding="utf-8",
            )
            checker = root / "Scripts/validation/source_text_inventory_text.py"
            checker.write_text(
                checker.read_text(encoding="utf-8") + "\n# checker change\n",
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code)
            self.assertIn("unacknowledged=1", output)

    def test_braced_early_return_requires_ack(self):
        """Reject a braced early return before a source assertion."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(directory)
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace(
                    '        Check(source.Contains("Product"));\n',
                    '        if (source.Length > 0) { return; }\n'
                    '        Check(source.Contains("Product"));\n',
                ),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code)
            self.assertIn('field": "reachability"', output)

    def test_else_branch_reachability_requires_ack(self):
        """Reject moving a source assertion into an else branch."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(directory)
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace(
                    '        Check(source.Contains("Product"));\n',
                    "        if (Environment.TickCount >= int.MinValue) { } else {\n"
                    '            Check(source.Contains("Product"));\n'
                    "        }\n",
                ),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code)
            self.assertIn('field": "reachability"', output)

    def test_unrelated_check_is_strengthening(self):
        """Allow an unrelated executable check added to an entrypoint."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(directory)
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace(
                    '        Check(source.Contains("Product"));\n',
                    '        Check(source.Contains("Product"));\n'
                    '        Check(1 + 1 == 2);\n',
                ),
                encoding="utf-8",
            )
            code, _ = self._run_fixture_gate(root)
            self.assertEqual(0, code)

    def test_xunit_coverage_addition_is_strengthening(self):
        """Allow a new unit test that covers the target product type."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(directory)
            unit = root / "Packages/fixture/Tests/Unit/ProductTests.cs"
            unit.parent.mkdir(parents=True)
            unit.write_text(
                "public class ProductTests {\n"
                "    public void ProductSmoke() { var value = new Product(); }\n"
                "}\n",
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(0, code)
            self.assertIn("strengthened=1", output)

    def test_helper_body_change_requires_ack(self):
        """Reject changing a source assertion helper to a no-op."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(
                directory,
                validation_text=(
                    "public static class FixtureValidation {\n"
                    "    private static void Check(bool condition) {\n"
                    "        if (!condition) throw new System.Exception();\n"
                    "    }\n"
                    "    public static void Validate() {\n"
                    '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                    '        Check(source.Contains("Product"));\n'
                    "    }\n"
                    "}\n"
                ),
            )
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace(
                    "        if (!condition) throw new System.Exception();\n",
                    "        return;\n",
                ),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code)
            self.assertIn('field": "harness_signature"', output)

    def test_unreachable_helper_requires_ack(self):
        """Reject source assertions left only in a helper the entrypoint cannot call."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(
                directory,
                validation_text=(
                    "public static class FixtureValidation {\n"
                    "    private static void Used() {\n"
                    '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                    '        Check(source.Contains("Product"));\n'
                    "    }\n"
                    "    public static void Validate() {\n"
                    "        Used();\n"
                    "    }\n"
                    "}\n"
                ),
            )
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace(
                    "        Used();\n",
                    '        System.Console.WriteLine("ok");\n'
                    "        return;\n",
                ),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code)
            self.assertIn('field": "call_path"', output)

    def test_unused_acknowledgement_is_reported_without_failure(self):
        """Report a trusted acknowledgement that matches no current change."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(directory)
            acknowledgement = root / "Scripts/validation/source_text_inventory_acknowledgements.json"
            acknowledgement.write_text(
                json.dumps(
                    {
                        "acknowledgements": [
                            {
                                "file": "old.cs",
                                "validation_flag": "--old",
                                "label": "old",
                                "field": "row_removed",
                                "base_sha256": "a" * 64,
                                "head_sha256": "b" * 64,
                                "reason": "Retained for historical review traceability.",
                            }
                        ]
                    }
                )
                + "\n",
                encoding="utf-8",
            )
            subprocess.run(["git", "-C", str(root), "add", "-A"], check=True)
            subprocess.run(["git", "-C", str(root), "commit", "-q", "-m", "unused-ack"], check=True)
            code, output = self._run_fixture_gate(root)
            self.assertEqual(0, code)
            self.assertIn("ack_unused=1", output)

    def test_include_in_default_false_requires_ack(self):
        """Reject moving a registered validation out of the default lane."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(directory)
            registry = root / "Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs"
            registry.write_text(
                registry.read_text(encoding="utf-8").replace(
                    "FixtureValidation.Validate, evidence:",
                    "FixtureValidation.Validate, includeInDefault: false, evidence:",
                ),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code)
            self.assertIn('field": "dispatch"', output)

    def test_new_unresolved_row_requires_ack(self):
        """Reject a newly added unresolved source assertion row."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(directory)
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace(
                    '        Check(source.Contains("Product"));\n',
                    '        Check(source.Contains("Product"));\n'
                    '        var missing = ReadRequiredRepoText(ResolvePath());\n'
                    '        Check(missing.Contains("Missing"));\n',
                ),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code)
            self.assertIn('field": "row_added_unresolved"', output)

    def test_new_unknown_row_requires_ack(self):
        """Reject a newly added directory-backed source assertion row."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(directory)
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace(
                    '        Check(source.Contains("Product"));\n',
                    '        Check(source.Contains("Product"));\n'
                    '        var directoryText = ReadRepoDirectoryText("Packages/fixture/Runtime", "*.cs");\n'
                    '        Check(directoryText.Contains("Product"));\n',
                ),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code)
            self.assertIn('field": "row_added_unknown"', output)

    def test_unresolved_reader_expression_change_requires_ack(self):
        """Reject changing an unresolved reader expression in place."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(
                directory,
                validation_text=(
                    "public static class FixtureValidation {\n"
                    "    public static void Validate() {\n"
                    '        var source = ReadRequiredRepoText(ResolvePathA());\n'
                    '        Check(source.Contains("Product"));\n'
                    "    }\n"
                    "}\n"
                ),
            )
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace("ResolvePathA()", "ResolvePathB()"),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code)
            self.assertIn('field": "reader_signature"', output)

    def test_unit_test_entrypoint_binds_helper(self):
        """Reject deleting a helper call from an attributed unit test entrypoint."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(directory)
            unit = root / "Packages/fixture/Tests/Unit/FixtureTests.cs"
            unit.parent.mkdir(parents=True)
            unit.write_text(
                "public static class FixtureTests {\n"
                "    private static void Helper() {\n"
                '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                '        Check(source.Contains("Product"));\n'
                "    }\n"
                "    [Fact] public static void ProductTest() { Helper(); }\n"
                "}\n",
                encoding="utf-8",
            )
            subprocess.run(["git", "-C", str(root), "add", "-A"], check=True)
            subprocess.run(["git", "-C", str(root), "commit", "-q", "-m", "unit-base"], check=True)
            unit.write_text(unit.read_text(encoding="utf-8").replace("Helper();", ""), encoding="utf-8")
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code)
            self.assertIn('field": "call_path"', output)

    def test_overloaded_helper_call_binding_requires_ack(self):
        """Reject deleting the source-bearing overload while keeping another overload."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(
                directory,
                validation_text=(
                    "public static class FixtureValidation {\n"
                    "    private static void Helper(string value) {\n"
                    '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                    '        Check(source.Contains("Product"));\n'
                    "    }\n"
                    "    private static void Helper(int value) { }\n"
                    "    public static void Validate() {\n"
                    '        Helper("run");\n'
                    "        Helper(1);\n"
                    "    }\n"
                    "}\n"
                ),
            )
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace('        Helper("run");\n', ""),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code)
            self.assertIn('field": "call_binding"', output)

    def test_assertion_overload_change_requires_ack(self):
        """Reject changing an assertion overload's comparison semantics."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(
                directory,
                validation_text=(
                    "public static class FixtureValidation {\n"
                    "    public static void Validate() {\n"
                    '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                    '        Assert.Contains("Product", source);\n'
                    "    }\n"
                    "}\n"
                ),
            )
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace(
                    'Assert.Contains("Product", source);',
                    'Assert.Contains("Product", source, StringComparison.OrdinalIgnoreCase);',
                ),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code)
            self.assertIn('field": "assertion_signature"', output)

    def test_checker_classification_change_requires_ack(self):
        """Reject a checker change that downgrades a trusted classification."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(directory)
            checker = root / "Scripts/validation/source_text_inventory_text.py"
            checker.write_text(
                checker.read_text(encoding="utf-8")
                + "\n\ndef classify(target, line):\n"
                + "    return \"A\"\n",
                encoding="utf-8",
            )
            result = subprocess.run(
                [
                    sys.executable,
                    str(root / "Scripts/validation/source_text_inventory.py"),
                    "--root",
                    str(root),
                    "--base-ref",
                    "HEAD",
                    "--output",
                    str(root / "build" / "inventory"),
                ],
                check=False,
                capture_output=True,
                text=True,
            )
            self.assertEqual(1, result.returncode, result.stdout + result.stderr)
            self.assertIn('field": "parser_class"', result.stdout)

    def test_checker_target_change_requires_ack(self):
        """Reject a checker change that retargets a trusted source assertion."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(directory)
            checker = root / "Scripts/validation/source_text_inventory_text.py"
            checker.write_text(
                checker.read_text(encoding="utf-8")
                + "\n\ndef _extract_argument_path(argument, paths):\n"
                + "    return \"Packages/fixture/Runtime/Benign.cs\"\n",
                encoding="utf-8",
            )
            (root / "Packages/fixture/Runtime/Benign.cs").write_text(
                "public sealed class Benign {}\n",
                encoding="utf-8",
            )
            result = subprocess.run(
                [
                    sys.executable,
                    str(root / "Scripts/validation/source_text_inventory.py"),
                    "--root",
                    str(root),
                    "--base-ref",
                    "HEAD",
                    "--output",
                    str(root / "build" / "inventory"),
                ],
                check=False,
                capture_output=True,
                text=True,
            )
            self.assertEqual(1, result.returncode, result.stdout + result.stderr)
            self.assertIn('field": "parser_target"', result.stdout)

    def test_parser_version_change_does_not_authorize_downgrade(self):
        """Require acknowledgement when a parser version changes with weaker semantics."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(directory)
            checker = root / "Scripts/validation/source_text_inventory.py"
            checker.write_text(
                checker.read_text(encoding="utf-8").replace(
                    'PARSER_SEMANTICS_VERSION = "2"',
                    'PARSER_SEMANTICS_VERSION = "3"',
                ),
                encoding="utf-8",
            )
            parser = root / "Scripts/validation/source_text_inventory_text.py"
            parser.write_text(
                parser.read_text(encoding="utf-8")
                + '\n\ndef classify(target, line):\n    return "A"\n',
                encoding="utf-8",
            )
            result = subprocess.run(
                [
                    sys.executable,
                    str(checker),
                    "--root",
                    str(root),
                    "--base-ref",
                    "HEAD",
                    "--output",
                    str(root / "build" / "inventory"),
                ],
                check=False,
                capture_output=True,
                text=True,
            )
            self.assertEqual(1, result.returncode, result.stdout + result.stderr)
            self.assertIn('field": "parser_class"', result.stdout)

    def test_unrelated_overload_does_not_hide_call_path(self):
        """Reject deleting a source helper call when another method is overloaded."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(
                directory,
                validation_text=(
                    "public static class FixtureValidation {\n"
                    "    private static void Noop(string value) { }\n"
                    "    private static void Noop(int value) { }\n"
                    "    private static void Used() {\n"
                    '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                    '        Check(source.Contains("Product"));\n'
                    "    }\n"
                    "    public static void Validate() {\n"
                    "        Used();\n"
                    "    }\n"
                    "}\n"
                ),
            )
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace("        Used();\n", ""),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code)
            self.assertIn('field": "call_path"', output)

    def test_overload_argument_type_change_requires_ack(self):
        """Reject changing a call to a source-bearing overload through a local type."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(
                directory,
                validation_text=(
                    "public static class FixtureValidation {\n"
                    "    private static void Helper(string value) {\n"
                    '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                    '        Check(source.Contains("Product"));\n'
                    "    }\n"
                    "    private static void Helper(int value) { }\n"
                    "    public static void Validate() {\n"
                    '        string value = "run";\n'
                    "        Helper(value);\n"
                    "    }\n"
                    "}\n"
                ),
            )
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace(
                    '        string value = "run";\n',
                    "        int value = 1;\n",
                ),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code)
            self.assertIn('field": "call_binding"', output)

    def test_unit_entrypoint_with_trait_binds_helper(self):
        """Reject deleting a helper call from a Fact entrypoint with a Trait."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(directory)
            unit = root / "Packages/fixture/Tests/Unit/FixtureTests.cs"
            unit.parent.mkdir(parents=True)
            unit.write_text(
                "public static class FixtureTests {\n"
                "    private static void Helper() {\n"
                '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                '        Check(source.Contains("Product"));\n'
                "    }\n"
                "    [Fact]\n"
                '    [Trait("Phase", "X")]\n'
                "    public static void ProductTest() { Helper(); }\n"
                "}\n",
                encoding="utf-8",
            )
            subprocess.run(["git", "-C", str(root), "add", "-A"], check=True)
            subprocess.run(["git", "-C", str(root), "commit", "-q", "-m", "unit-trait-base"], check=True)
            unit.write_text(unit.read_text(encoding="utf-8").replace("Helper();", ""), encoding="utf-8")
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code)
            self.assertIn('field": "call_path"', output)

    def test_try_catch_swallow_requires_ack(self):
        """Reject wrapping a source assertion in a swallowing try/catch."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(directory)
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            original = validation.read_text(encoding="utf-8")
            validation.write_text(
                original.replace(
                    '        Check(source.Contains("Product"));\n',
                    "        try {\n"
                    '            Check(source.Contains("Product"));\n'
                    "        } catch (System.Exception) { }\n",
                ),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code)
            self.assertIn('field": "reachability"', output)

    def test_head_acknowledgement_is_not_trusted(self):
        """Reject a weakening acknowledgement added only in the proposed head."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(directory)
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace(
                    '        Check(source.Contains("Product"));\n', ""
                ),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code)
            change = next(
                json.loads(item.split(" ", 1)[1])
                for item in output.splitlines()
                if item.startswith("SOURCE_TEXT_INVENTORY_UNACKNOWLEDGED ")
            )
            change["reason"] = "This head-only acknowledgement must not be trusted."
            (root / "Scripts/validation/source_text_inventory_acknowledgements.json").write_text(
                json.dumps({"acknowledgements": [change]}, indent=2) + "\n",
                encoding="utf-8",
            )
            code, _ = self._run_fixture_gate(root)
            self.assertEqual(1, code)

    def test_expression_bodied_helper_reachability_requires_ack(self):
        """Reject removing a call to an expression-bodied source assertion helper."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(
                directory,
                validation_text=(
                    "public static class FixtureValidation {\n"
                    "    private static bool HasProduct() => "
                    'ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs").Contains("Product");\n'
                    "    public static void Validate() {\n"
                    '        Check(HasProduct());\n'
                    "    }\n"
                    "}\n"
                ),
            )
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace(
                    "        Check(HasProduct());\n", ""
                ),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code)
            self.assertIn('field": "call_path"', output)

    def test_discarded_receiver_expression_requires_ack(self):
        """Reject a source assertion changed from Check to a discarded expression."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(directory)
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace(
                    '        Check(source.Contains("Product"));\n',
                    '        source.Contains("Product");\n',
                ),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code)
            self.assertIn('field": "assertion_context"', output)

    def test_expected_assertion_literal_requires_ack(self):
        """Reject changing the expected literal while retaining the same source target."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(directory)
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8")
                .replace(
                    '        Check(source.Contains("Product"));\n',
                    '        Assert.Contains("Product", source);\n',
                ),
                encoding="utf-8",
            )
            subprocess.run(["git", "-C", str(root), "add", "-A"], check=True)
            subprocess.run(["git", "-C", str(root), "commit", "-q", "-m", "assertion-base"], check=True)
            validation.write_text(
                validation.read_text(encoding="utf-8").replace(
                    '        Assert.Contains("Product", source);\n',
                    '        Assert.Contains("Other", source);\n',
                ),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code)
            self.assertIn('field": "assertion_literal"', output)

    def test_checker_row_loss_requires_ack_without_package_changes(self):
        """Reject a checker-only change that hides every trusted inventory row."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(directory)
            checker = root / "Scripts/validation/source_text_inventory.py"
            checker.write_text(
                checker.read_text(encoding="utf-8").replace(
                    "\nif __name__ == \"__main__\":",
                    "\n\ndef inventory(root):\n    return []\n\nif __name__ == \"__main__\":",
                ),
                encoding="utf-8",
            )
            result = subprocess.run(
                [
                    sys.executable,
                    str(checker),
                    "--root",
                    str(root),
                    "--base-ref",
                    "HEAD",
                    "--output",
                    str(root / "build" / "inventory"),
                ],
                check=False,
                capture_output=True,
                text=True,
            )
            self.assertEqual(1, result.returncode, result.stdout + result.stderr)
            self.assertIn('field": "parser_row_removed"', result.stdout)

    def test_trusted_parser_isolated_from_head_split_helpers(self):
        """Reject a split-helper change that hides every trusted inventory row."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(directory)
            helper = root / "Scripts/validation/source_text_inventory_text.py"
            helper.write_text(
                helper.read_text(encoding="utf-8")
                + "\n\ndef _call_spans(lines, pattern, text=None, searchable=None):\n"
                + "    return []\n",
                encoding="utf-8",
            )
            checker = root / "Scripts/validation/source_text_inventory.py"
            result = subprocess.run(
                [
                    sys.executable,
                    str(checker),
                    "--root",
                    str(root),
                    "--base-ref",
                    "HEAD",
                    "--output",
                    str(root / "build" / "inventory"),
                ],
                check=False,
                capture_output=True,
                text=True,
            )
            self.assertEqual(1, result.returncode, result.stdout + result.stderr)
            self.assertIn('field": "parser_row_removed"', result.stdout)

    def test_assertion_polarity_change_requires_ack(self):
        """Reject inverting an assertion while retaining its source expression."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(directory)
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace(
                    '        Check(source.Contains("Product"));\n',
                    '        Assert.True(source.Contains("Product"));\n',
                ),
                encoding="utf-8",
            )
            subprocess.run(["git", "-C", str(root), "add", "-A"], check=True)
            subprocess.run(["git", "-C", str(root), "commit", "-q", "-m", "assertion-base"], check=True)
            validation.write_text(
                validation.read_text(encoding="utf-8").replace(
                    "Assert.True(source.Contains(\"Product\"))",
                    "Assert.False(source.Contains(\"Product\"))",
                ),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code)
            self.assertIn('field": "assertion_polarity"', output)

    def test_arbitrary_helper_exit_condition_is_bound_to_inventory_row(self):
        """Detect changes to an arbitrary helper's exit condition."""
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            runtime = root / "Packages" / "fixture" / "Tests" / "Runtime"
            runtime.mkdir(parents=True)
            product = root / "Packages" / "fixture" / "Runtime" / "Product.cs"
            product.parent.mkdir(parents=True)
            product.write_text("public sealed class Product {}\n", encoding="utf-8")
            validation = runtime / "ArbitraryHelperValidation.cs"
            original = (
                "public static class ArbitraryHelperValidation {\n"
                "    private static void Run(string source, bool skip) {\n"
                "        if (skip) return;\n"
                '        Check(source.Contains("needle"));\n'
                "    }\n"
                "    public static void Validate() {\n"
                '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                "        Run(source, skip: false);\n"
                "    }\n"
                "}\n"
            )
            validation.write_text(original, encoding="utf-8")
            registry = (
                root
                / "Packages"
                / "dev.unity2foxglove.sdk"
                / "Tests"
                / "Runtime"
                / "PhaseValidationRegistry.cs"
            )
            registry.parent.mkdir(parents=True, exist_ok=True)
            registry.write_text(
                'Ci("--phase-arbitrary-helper", "fixture", ArbitraryHelperValidation.Validate, evidence: ValidationEvidence.Behavior),\n',
                encoding="utf-8",
            )
            base_row = next(row for row in inventory(root) if "needle" in row["token"])
            validation.write_text(original.replace("if (skip)", "if (true)"), encoding="utf-8")
            head_row = next(row for row in inventory(root) if "needle" in row["token"])
            self.assertNotEqual(base_row["harness_signature"], head_row["harness_signature"])
            self.assertNotEqual(base_row["reachability"], head_row["reachability"])

    def test_crlf_and_lf_inventory_rows_match(self):
        """Keep inventory rows identical for LF and CRLF validation sources."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(directory)
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            lf_rows = inventory(root)
            payload = validation.read_bytes().replace(b"\r\n", b"\n")
            validation.write_bytes(payload.replace(b"\n", b"\r\n"))
            crlf_rows = inventory(root)
            self.assertEqual(lf_rows, crlf_rows)

    def _git_fixture(self, directory: str, validation_text: str | None = None) -> Path:
        """Create a small Git fixture containing the current checker modules."""
        root = Path(directory)
        for source in (
            Path(__file__).resolve().parents[3].glob("Scripts/validation/source_text_inventory*.py")
        ):
            destination = root / source.relative_to(Path(__file__).resolve().parents[3])
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(source, destination)
        acknowledgement = root / "Scripts/validation/source_text_inventory_acknowledgements.json"
        acknowledgement.write_text('{"acknowledgements": []}\n', encoding="utf-8")
        runtime = root / "Packages/fixture/Tests/Runtime"
        runtime.mkdir(parents=True)
        product = root / "Packages/fixture/Runtime/Product.cs"
        product.parent.mkdir(parents=True)
        product.write_text("public sealed class Product {}\n", encoding="utf-8")
        validation = runtime / "FixtureValidation.cs"
        validation.write_text(
            validation_text
            or (
                "public static class FixtureValidation {\n"
                "    public static void Validate() {\n"
                '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                '        Check(source.Contains("Product"));\n'
                "    }\n"
                "}\n"
            ),
            encoding="utf-8",
        )
        registry = root / "Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs"
        registry.parent.mkdir(parents=True, exist_ok=True)
        registry.write_text(
            'Ci("--phase-fixture", "fixture", FixtureValidation.Validate, evidence: ValidationEvidence.Behavior),\n',
            encoding="utf-8",
        )
        subprocess.run(["git", "init", "-q", str(root)], check=True)
        subprocess.run(["git", "-C", str(root), "config", "user.name", "fixture"], check=True)
        subprocess.run(
            ["git", "-C", str(root), "config", "user.email", "fixture@example.invalid"], check=True
        )
        subprocess.run(["git", "-C", str(root), "add", "-A"], check=True)
        subprocess.run(["git", "-C", str(root), "commit", "-q", "-m", "base"], check=True)
        return root

    def test_ratchet_allows_removed_row_only_with_ack(self):
        """Allow a removed row only when a non-empty exact acknowledgement exists."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(directory)
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            original = validation.read_text(encoding="utf-8")
            validation.write_text(
                original.replace(
                    '        Check(source.Contains("Product"));\n', ""
                ),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code)
            line = next(
                item
                for item in output.splitlines()
                if item.startswith("SOURCE_TEXT_INVENTORY_UNACKNOWLEDGED ")
            )
            change = json.loads(line.split(" ", 1)[1])
            change["reason"] = "A behavior test replaces the removed source assertion."
            validation.write_text(original, encoding="utf-8")
            (root / "Scripts/validation/source_text_inventory_acknowledgements.json").write_text(
                json.dumps({"acknowledgements": [change]}, indent=2) + "\n",
                encoding="utf-8",
            )
            subprocess.run(["git", "-C", str(root), "add", "-A"], check=True)
            subprocess.run(["git", "-C", str(root), "commit", "-q", "-m", "trusted-ack"], check=True)
            validation.write_text(
                original.replace('        Check(source.Contains("Product"));\n', ""),
                encoding="utf-8",
            )
            code, _ = self._run_fixture_gate(root)
            self.assertEqual(0, code)
            validation.write_text(original, encoding="utf-8")
            change["reason"] = ""
            (root / "Scripts/validation/source_text_inventory_acknowledgements.json").write_text(
                json.dumps({"acknowledgements": [change]}, indent=2) + "\n",
                encoding="utf-8",
            )
            subprocess.run(["git", "-C", str(root), "add", "-A"], check=True)
            subprocess.run(["git", "-C", str(root), "commit", "-q", "-m", "invalid-trusted-ack"], check=True)
            validation.write_text(
                original.replace('        Check(source.Contains("Product"));\n', ""),
                encoding="utf-8",
            )
            code, _ = self._run_fixture_gate(root)
            self.assertEqual(1, code)

    def _run_fixture_gate(self, root: Path) -> tuple[int, str]:
        """Run the real CLI entrypoint against a fixture Git repository."""
        output = io.StringIO()
        with contextlib.redirect_stdout(output):
            code = main(
                [
                    "--root",
                    str(root),
                    "--base-ref",
                    "HEAD",
                    "--output",
                    str(root / "build" / "inventory"),
                ]
            )
        return code, output.getvalue()

    def test_two_argument_read_text_resolves_file_constants(self):
        """Resolve a multiline const path passed as ReadText's second argument."""
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            runtime = root / "Packages" / "fixture" / "Tests" / "Runtime"
            runtime.mkdir(parents=True)
            product = root / "Packages" / "fixture" / "Runtime" / "Product.cs"
            product.parent.mkdir(parents=True)
            product.write_text("public sealed class Product {}\n", encoding="utf-8")
            validation = runtime / "ConstReadTextValidation.cs"
            validation.write_text(
                "public static class ConstReadTextValidation {\n"
                "    private const string ProductPath =\n"
                '        "Packages/fixture/Runtime/Product.cs";\n'
                "    public static void Validate() {\n"
                "        var source = ReadText(\n"
                "            repoRoot, ProductPath);\n"
                '        Check(source.Contains("Product"));\n'
                "    }\n"
                "}\n",
                encoding="utf-8",
            )
            registry = (
                root
                / "Packages"
                / "dev.unity2foxglove.sdk"
                / "Tests"
                / "Runtime"
                / "PhaseValidationRegistry.cs"
            )
            registry.parent.mkdir(parents=True, exist_ok=True)
            registry.write_text(
                'Ci("--phase-const-read-text", "fixture", ConstReadTextValidation.Validate, evidence: ValidationEvidence.Behavior),\n',
                encoding="utf-8",
            )
            rows = inventory(root)
            self.assertEqual(1, len(rows))
            self.assertEqual("Packages/fixture/Runtime/Product.cs", rows[0]["target"])
