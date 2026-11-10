"""Additional source inventory semantic regression fixtures."""

from __future__ import annotations

import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

from . import test_source_text_inventory_gate as _gate_module


class _FixtureSupport:
    _git_fixture = _gate_module.SourceTextInventoryGateTests._git_fixture
    _run_fixture_gate = _gate_module.SourceTextInventoryGateTests._run_fixture_gate


class SourceTextInventorySemanticTests(_FixtureSupport, unittest.TestCase):
    """Cover parser schema, control flow, overload, and lambda semantics."""

    def test_parser_row_missing_field_requires_ack(self):
        """Reject a parser row that drops a protected metadata field."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(directory)
            checker = root / "Scripts/validation/source_text_inventory.py"
            wrapper = (
                "\n_original_inventory = inventory\n"
                "def inventory(root):\n"
                "    rows = _original_inventory(root)\n"
                "    for row in rows:\n"
                "        row.pop('call_path', None)\n"
                "    return rows\n"
            )
            checker.write_text(
                checker.read_text(encoding="utf-8").replace(
                    '\nif __name__ == "__main__":', wrapper + '\nif __name__ == "__main__":'
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
            self.assertIn('field": "parser_field_missing"', result.stdout)

    def test_else_condition_change_requires_ack(self):
        """Reject changing the condition that selects an existing else assertion."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(
                directory,
                validation_text=(
                    "public static class FixtureValidation {\n"
                    "    public static void Validate() {\n"
                    '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                    "        if (source.Length > 0) {\n"
                    '            System.Console.WriteLine("ok");\n'
                    "        } else {\n"
                    '            Check(source.Contains("Product"));\n'
                    "        }\n"
                    "    }\n"
                    "}\n"
                ),
            )
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace(
                    "source.Length > 0", "source.Length == 0"
                ),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code)
            self.assertIn('field": "reachability"', output)

    def test_switch_case_change_requires_ack(self):
        """Reject changing the case that reaches a source assertion."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(
                directory,
                validation_text=(
                    "public static class FixtureValidation {\n"
                    "    public static void Validate() {\n"
                    '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                    "        int mode = 1;\n"
                    "        switch (mode) {\n"
                    "            case 1:\n"
                    '                Check(source.Contains("Product"));\n'
                    "                break;\n"
                    "            default:\n"
                    "                break;\n"
                    "        }\n"
                    "    }\n"
                    "}\n"
                ),
            )
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace("case 1:", "case 2:"),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code)
            self.assertIn('field": "reachability"', output)

    def test_existing_catch_body_change_requires_ack(self):
        """Reject changing an assertion catch from rethrow to swallow."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(
                directory,
                validation_text=(
                    "public static class FixtureValidation {\n"
                    "    public static void Validate() {\n"
                    '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                    "        try {\n"
                    '            Check(source.Contains("Product"));\n'
                    "        } catch (System.Exception) {\n"
                    "            throw;\n"
                    "        }\n"
                    "    }\n"
                    "}\n"
                ),
            )
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace("            throw;\n", ""),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code)
            self.assertIn('field": "reachability"', output)

    def test_overload_return_type_change_requires_ack(self):
        """Reject switching an overloaded call through a changed factory return type."""
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
                    '    private static string GetString() => "run";\n'
                    "    private static int GetInt() => 1;\n"
                    "    public static void Validate() {\n"
                    '        var value = GetString();\n'
                    "        Helper(value);\n"
                    "    }\n"
                    "}\n"
                ),
            )
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace("GetString()", "GetInt()"),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code)
            self.assertTrue(
                'field": "call_path"' in output or 'field": "call_binding"' in output,
                output,
            )

    def test_overload_field_type_change_requires_ack(self):
        """Reject switching an overloaded call through a changed field type."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(
                directory,
                validation_text=(
                    "public static class FixtureValidation {\n"
                    '    private static string value = "run";\n'
                    "    private static void Helper(string argument) {\n"
                    '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                    '        Check(source.Contains("Product"));\n'
                    "    }\n"
                    "    private static void Helper(int argument) { }\n"
                    "    public static void Validate() {\n"
                    "        Helper(value);\n"
                    "    }\n"
                    "}\n"
                ),
            )
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace(
                    'private static string value = "run";', "private static int value = 1;"
                ),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code)
            self.assertTrue(
                'field": "call_path"' in output or 'field": "call_binding"' in output,
                output,
            )

    def test_lambda_helper_invocation_change_requires_ack(self):
        """Reject deleting an invocation of a lambda containing a source assertion."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(
                directory,
                validation_text=(
                    "public static class FixtureValidation {\n"
                    "    public static void Validate() {\n"
                    "        System.Action run = () => {\n"
                    '            var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                    '            Check(source.Contains("Product"));\n'
                    "        };\n"
                    "        run();\n"
                    "    }\n"
                    "}\n"
                ),
            )
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace("        run();\n", ""),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code)
            self.assertIn('field": "harness_signature"', output)
