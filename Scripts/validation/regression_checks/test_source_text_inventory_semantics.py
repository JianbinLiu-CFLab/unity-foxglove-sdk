"""Additional source inventory semantic regression fixtures."""

from __future__ import annotations

import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

from . import test_source_text_inventory_gate as _gate_module

class _FixtureSupport:
    """Reuse the Git fixture helpers without inheriting the large gate test class."""

    _git_fixture = _gate_module.SourceTextInventoryGateTests._git_fixture
    _run_fixture_gate = _gate_module.SourceTextInventoryGateTests._run_fixture_gate
class SourceTextInventorySemanticTests(_FixtureSupport, unittest.TestCase):
    """Cover parser schema, control flow, overload, and lambda semantics."""

    def test_alternate_wrapper_paths_remain_bound(self):
        """Reject disabling the effective path when an earlier wrapper path is inactive."""
        for forwarding in ("RunChecks();", "Intermediate();"):
            with self.subTest(forwarding=forwarding), tempfile.TemporaryDirectory() as directory:
                root = self._git_fixture(directory, validation_text=(
                    "public static class FixtureValidation {\n"
                    "public static void Validate() { First(false); Second(true); }\n"
                    f"private static void First(bool enabled) {{ if (enabled) {forwarding} }}\n"
                    f"private static void Second(bool enabled) {{ if (enabled) {forwarding} }}\n"
                    "private static void Intermediate() { RunChecks(); }\n"
                    "private static void RunChecks() {\n"
                    'var source=ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                    'Check(source.Contains("Product"));\n}\n'
                    "private static string ReadRequiredRepoText(string p) => System.IO.File.ReadAllText(p);\n"
                    "private static void Check(bool ok) { if (!ok) throw new System.Exception(); }\n}\n"
                ))
                rows = _gate_module.inventory(root)
                self.assertEqual(1, len(rows))
                self.assertIn("->First", rows[0]["call_path"])
                validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
                text = validation.read_text(encoding="utf-8").replace("Second(true);", "Second(false);")
                validation.write_text(text, encoding="utf-8")
                (root / "Packages/fixture/Runtime/Product.cs").write_text("public class Other {}\n", encoding="utf-8")
                code, output = self._run_fixture_gate(root)
                self.assertEqual(1, code, output)
                self.assertIn('field": "call_path"', output)

    def test_head_acknowledgement_schema_is_validated(self):
        """Reject malformed proposed manifests without granting head entries authority."""
        entry = {
            "file": "old.cs", "validation_flag": "--old", "label": "old", "field": "row_removed",
            "base_sha256": "a" * 64, "head_sha256": "b" * 64,
            "reason": "Reviewed replacement behavior is recorded before the later removal.",
        }
        for content, expected in (
            (json.dumps({"acknowledgements": [entry]}), 0),
            (json.dumps({"acknowledgements": [{**entry, "reason": ""}]}), 1),
            (json.dumps({"acknowledgements": "invalid"}), 1),
            ("{invalid-json", 1),
        ):
            with self.subTest(content=content), tempfile.TemporaryDirectory() as directory:
                root = self._git_fixture(directory)
                path = root / "Scripts/validation/source_text_inventory_acknowledgements.json"
                path.write_text(content, encoding="utf-8")
                code, output = self._run_fixture_gate(root)
                self.assertEqual(expected, code, output)
                if not expected:
                    self.assertIn("ack_unused=0", output)

    def test_shadowing_imported_harness_addition_requires_ack(self):
        """Reject local assertion or reader helpers shadowing imported throwing behavior."""
        for helper in (
            "private static void Check(bool ok) { }",
            'private static string ReadRequiredRepoText(string path) { return "Product"; }',
        ):
            with self.subTest(helper=helper), tempfile.TemporaryDirectory() as directory:
                root = self._git_fixture(directory, validation_text=(
                    "using static SharedAssertions;\npublic static class FixtureValidation {\n"
                    "public static void Validate() {\n"
                    'var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                    'Check(source.Contains("Product"));\n}\n}\n'
                ))
                shared = root / "Packages/fixture/Tests/Runtime/SharedAssertions.cs"
                shared.write_text(
                    "public static class SharedAssertions {\n"
                    "public static string ReadRequiredRepoText(string path) => System.IO.File.ReadAllText(path);\n"
                    "public static void Check(bool ok) { if (!ok) throw new System.Exception(); }\n}\n",
                    encoding="utf-8",
                )
                subprocess.run(["git", "-C", str(root), "add", str(shared)], check=True)
                subprocess.run(["git", "-C", str(root), "commit", "-q", "-m", "shared-harness-base"], check=True)
                rows = _gate_module.inventory(root)
                self.assertEqual(1, len(rows))
                self.assertRegex(rows[0]["harness_signature"], r"^[0-9a-f]{64}$")
                validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
                text = validation.read_text(encoding="utf-8")
                closing = text.rfind("}")
                validation.write_text(text[:closing] + helper + "\n" + text[closing:], encoding="utf-8")
                code, output = self._run_fixture_gate(root)
                self.assertEqual(1, code, output)
                self.assertIn('field": "harness_signature"', output)




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

    def test_method_group_delegate_change_requires_ack(self):
        """Reject switching a delegate method group to a no-op method."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(
                directory,
                validation_text=(
                    "public static class FixtureValidation {\n"
                    "    private static void Used() {\n"
                    '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                    '        Check(source.Contains("Product"));\n'
                    "    }\n"
                    "    private static void Noop() { }\n"
                    "    public static void Validate() {\n"
                    "        System.Action run = Used;\n"
                    "        run();\n"
                    "    }\n"
                    "}\n"
                ),
            )
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace(
                    "System.Action run = Used", "System.Action run = Noop"
                ),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code)
            self.assertIn('field": "harness_signature"', output)

    def test_inline_delegate_forwarding_change_requires_ack(self):
        """Reject changing an inline lambda receiver into a no-op helper."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(
                directory,
                validation_text=(
                    "public static class FixtureValidation {\n"
                    "    private static void Run(System.Action action) { action(); }\n"
                    "    public static void Validate() {\n"
                    "        Run(() => {\n"
                    '            var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                    '            Check(source.Contains("Product"));\n'
                    "        });\n"
                    "    }\n"
                    "}\n"
                ),
            )
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace(
                    "private static void Run(System.Action action) { action(); }",
                    "private static void Run(System.Action action) { }",
                ),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code)
            self.assertIn('field": "harness_signature"', output)

    def test_delegate_factory_change_requires_ack(self):
        """Reject switching a delegate factory to an empty delegate factory."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(
                directory,
                validation_text=(
                    "public static class FixtureValidation {\n"
                    "    private static System.Action Make() => () => {\n"
                    '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                    '        Check(source.Contains("Product"));\n'
                    "    };\n"
                    "    private static System.Action Noop() => () => { };\n"
                    "    public static void Validate() {\n"
                    "        var run = Make();\n"
                    "        run();\n"
                    "    }\n"
                    "}\n"
                ),
            )
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace(
                    "var run = Make()", "var run = Noop()"
                ),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code)
            self.assertIn('field": "harness_signature"', output)

    def test_method_group_forwarding_argument_change_requires_ack(self):
        """Reject switching a method-group argument to a no-op helper."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(
                directory,
                validation_text=(
                    "public static class FixtureValidation {\n"
                    "    private static void Used() {\n"
                    '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                    '        Check(source.Contains("Product"));\n'
                    "    }\n"
                    "    private static void Noop() { }\n"
                    "    private static void Run(System.Action action) { action(); }\n"
                    "    public static void Validate() { Run(Used); }\n"
                    "}\n"
                ),
            )
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace("Run(Used)", "Run(Noop)"),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code)
            self.assertIn('field": "harness_signature"', output)

    def test_conditional_delegate_binding_change_requires_ack(self):
        """Reject changing the selected branch of a conditional delegate binding."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(
                directory,
                validation_text=(
                    "public static class FixtureValidation {\n"
                    "    private static void Used() {\n"
                    '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                    '        Check(source.Contains("Product"));\n'
                    "    }\n"
                    "    private static void Noop() { }\n"
                    "    public static void Validate() {\n"
                    "        System.Action run = true ? Used : Noop;\n"
                    "        run();\n"
                    "    }\n"
                    "}\n"
                ),
            )
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace("true ? Used", "false ? Used"),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code)
            self.assertIn('field": "harness_signature"', output)

    def test_delegate_reassignment_change_requires_ack(self):
        """Reject changing a later delegate reassignment to a no-op helper."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(
                directory,
                validation_text=(
                    "public static class FixtureValidation {\n"
                    "    private static void Used() {\n"
                    '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                    '        Check(source.Contains("Product"));\n'
                    "    }\n"
                    "    private static void Noop() { }\n"
                    "    public static void Validate() {\n"
                    "        System.Action run = Used;\n"
                    "        run = Used;\n"
                    "        run();\n"
                    "    }\n"
                    "}\n"
                ),
            )
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace("run = Used;\n        run();", "run = Noop;\n        run();"),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code)
            self.assertIn('field": "harness_signature"', output)

    def test_multilevel_delegate_forwarding_change_requires_ack(self):
        """Reject removing a second-level delegate forwarding call."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(
                directory,
                validation_text=(
                    "public static class FixtureValidation {\n"
                    "    private static void Invoke(System.Action action) { action(); }\n"
                    "    private static void Run(System.Action action) { Invoke(action); }\n"
                    "    public static void Validate() {\n"
                    "        Run(() => {\n"
                    '            var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                    '            Check(source.Contains("Product"));\n'
                    "        });\n"
                    "    }\n"
                    "}\n"
                ),
            )
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace(
                    "private static void Invoke(System.Action action) { action(); }",
                    "private static void Invoke(System.Action action) { }",
                ),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code)
            self.assertIn('field": "harness_signature"', output)

    def test_conditional_overload_change_requires_ack(self):
        """Reject an unresolved conditional expression that changes overload targets."""
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
                    "        bool enabled = true;\n"
                    '        var value = enabled ? "run" : 1;\n'
                    "        Helper(value);\n"
                    "    }\n"
                    "}\n"
                ),
            )
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace(
                    "bool enabled = true", "bool enabled = false"
                ),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code)
            self.assertIn('field": "call_binding"', output)

    def test_local_function_invocation_change_requires_ack(self):
        """Reject deleting a local function invocation that owns a source assertion."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(
                directory,
                validation_text=(
                    "public static class FixtureValidation {\n"
                    "    public static void Validate() {\n"
                    "        void Local() {\n"
                    '            var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                    '            Check(source.Contains("Product"));\n'
                    "        }\n"
                    "        Local();\n"
                    "    }\n"
                    "}\n"
                ),
            )
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace("        Local();\n", ""),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code)
            self.assertTrue(
                'field": "call_path"' in output or 'field": "harness_signature"' in output,
                output,
            )

    def test_local_function_condition_change_requires_ack(self):
        """Reject guarding a local source assertion function with a new condition."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(
                directory,
                validation_text=(
                    "public static class FixtureValidation {\n"
                    "    public static void Validate() {\n"
                    "        void Local() {\n"
                    '            var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                    '            Check(source.Contains("Product"));\n'
                    "        }\n"
                    "        Local();\n"
                    "    }\n"
                    "}\n"
                ),
            )
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace(
                    "        Local();\n", "        if (source.Length < 0) Local();\n"
                ),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code)
            self.assertIn('field": "call_path"', output)

    def test_task_run_wait_change_requires_ack(self):
        """Reject dropping task completion when a background lambda owns an assertion."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(
                directory,
                validation_text=(
                    "public static class FixtureValidation {\n"
                    "    public static void Validate() {\n"
                    "        System.Threading.Tasks.Task.Run(() => {\n"
                    '            var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                    '            Check(source.Contains("Product"));\n'
                    "        }).Wait();\n"
                    "    }\n"
                    "}\n"
                ),
            )
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace(
                    "        }).Wait();\n", "        });\n"
                ),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code)
            self.assertIn('field": "harness_signature"', output)

    def test_catch_when_filter_change_requires_ack(self):
        """Reject changing a catch filter that controls whether an assertion failure is swallowed."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(
                directory,
                validation_text=(
                    "public static class FixtureValidation {\n"
                    "    public static void Validate() {\n"
                    '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                    "        try {\n"
                    '            Check(source.Contains("Product"));\n'
                    "        } catch (System.Exception e) when (e.Message.Length < 0) {\n"
                    "        }\n"
                    "    }\n"
                    "}\n"
                ),
            )
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace(
                    "when (e.Message.Length < 0)", "when (true)"
                ),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code)
            self.assertIn('field": "reachability"', output)

    def test_if_selector_value_change_requires_ack(self):
        """Reject disabling a source assertion through a changed boolean selector."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(
                directory,
                validation_text=(
                    "public static class FixtureValidation {\n"
                    "    public static void Validate() {\n"
                    '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                    "        bool enabled = true;\n"
                    "        if (enabled) {\n"
                    '            Check(source.Contains("Product"));\n'
                    "        }\n"
                    "    }\n"
                    "}\n"
                ),
            )
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace(
                    "bool enabled = true", "bool enabled = false"
                ),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code)
            self.assertIn('field": "reachability"', output)

    def test_switch_selector_value_change_requires_ack(self):
        """Reject changing a switch selector so the asserted case is no longer selected."""
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
                    "        }\n"
                    "    }\n"
                    "}\n"
                ),
            )
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace(
                    "int mode = 1", "int mode = 2"
                ),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code)
            self.assertIn('field": "reachability"', output)

    def test_switch_break_before_assertion_requires_ack(self):
        """Reject adding a switch break that skips a source assertion."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(
                directory,
                validation_text=(
                    "public static class FixtureValidation {\n"
                    "    public static void Validate() {\n"
                    '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                    "        switch (1) {\n"
                    "            case 1:\n"
                    '                Check(source.Contains("Product"));\n'
                    "                break;\n"
                    "        }\n"
                    "    }\n"
                    "}\n"
                ),
            )
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace(
                    '                Check(source.Contains("Product"));\n',
                    "                if (source.Length >= 0) break;\n"
                    '                Check(source.Contains("Product"));\n',
                ),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code)
            self.assertIn('field": "reachability"', output)

    def test_lambda_invoke_and_forwarding_change_requires_ack(self):
        """Reject deleting Invoke or forwarding calls for a source-bearing lambda."""
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
                    "        run.Invoke();\n"
                    "    }\n"
                    "}\n"
                ),
            )
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace("        run.Invoke();\n", ""),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code)
            self.assertIn('field": "harness_signature"', output)

    def test_lambda_outer_selector_change_requires_ack(self):
        """Reject disabling a recognized lambda invocation through an outer branch."""
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
                    "        bool enabled = true;\n"
                    "        if (enabled) { run(); }\n"
                    "    }\n"
                    "}\n"
                ),
            )
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace(
                    "bool enabled = true", "bool enabled = false"
                ),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code)
            self.assertIn('field": "harness_signature"', output)

    def test_inline_lambda_owner_change_requires_ack(self):
        """Reject changing the helper that receives an inline source-bearing lambda."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(
                directory,
                validation_text=(
                    "public static class FixtureValidation {\n"
                    "    private static void Run(System.Action action) { action(); }\n"
                    "    private static void Noop(System.Action action) { }\n"
                    "    public static void Validate() {\n"
                    "        Run(() => {\n"
                    '            var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                    '            Check(source.Contains("Product"));\n'
                    "        });\n"
                    "    }\n"
                    "}\n"
                ),
            )
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace("Run(() =>", "Noop(() =>"),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code)
            self.assertIn('field": "harness_signature"', output)

    def test_uninitialized_field_overload_change_requires_ack(self):
        """Reject an overload switch through an uninitialized typed field."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(
                directory,
                validation_text=(
                    "public static class FixtureValidation {\n"
                    "    private static string value;\n"
                    "    private static void Helper(string argument) {\n"
                    '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                    '        Check(source.Contains("Product"));\n'
                    "    }\n"
                    "    private static void Helper(int argument) { }\n"
                    "    public static void Validate() { Helper(value); }\n"
                    "}\n"
                ),
            )
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace(
                    "private static string value;", "private static int value;"
                ),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code)
            self.assertTrue(
                'field": "call_path"' in output or 'field": "call_binding"' in output,
                output,
            )

    def test_property_overload_change_requires_ack(self):
        """Reject an overload switch through a typed auto-property."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(
                directory,
                validation_text=(
                    "public static class FixtureValidation {\n"
                    '    private static string Value { get; } = "run";\n'
                    "    private static void Helper(string argument) {\n"
                    '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                    '        Check(source.Contains("Product"));\n'
                    "    }\n"
                    "    private static void Helper(int argument) { }\n"
                    "    public static void Validate() { Helper(Value); }\n"
                    "}\n"
                ),
            )
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace(
                    'private static string Value { get; } = "run";',
                    'private static int Value { get; } = 1;',
                ),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code)
            self.assertTrue(
                'field": "call_path"' in output or 'field": "call_binding"' in output,
                output,
            )

    def test_second_catch_change_requires_ack(self):
        """Reject changing a later catch body in a multi-catch chain."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(
                directory,
                validation_text=(
                    "public static class FixtureValidation {\n"
                    "    public static void Validate() {\n"
                    '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                    "        try {\n"
                    '            Check(source.Contains("Product"));\n'
                    "        } catch (System.InvalidOperationException) {\n"
                    "            throw;\n"
                    "        } catch (System.Exception) {\n"
                    "            throw;\n"
                    "        }\n"
                    "    }\n"
                    "}\n"
                ),
            )
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace(
                    "        } catch (System.Exception) {\n            throw;",
                    "        } catch (System.Exception) {\n",
                ),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code)
            self.assertIn('field": "reachability"', output)

    def test_finally_exit_change_requires_ack(self):
        """Reject adding a finally return that changes exception propagation."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(
                directory,
                validation_text=(
                    "public static class FixtureValidation {\n"
                    "    public static void Validate() {\n"
                    '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                    "        try {\n"
                    '            Check(source.Contains("Product"));\n'
                    "        } finally {\n"
                    "        }\n"
                    "    }\n"
                    "}\n"
                ),
            )
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace(
                    "        } finally {\n        }", "        } finally {\n            return;\n        }"
                ),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code)
            self.assertIn('field": "reachability"', output)

    def test_partial_class_sibling_helper_change_requires_ack(self):
        """Reject changing a helper implemented in a sibling partial-class file."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(
                directory,
                validation_text=(
                    "public static partial class FixtureValidation {\n"
                    "    public static void Validate() {\n"
                    '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                    '        Check(source.Contains("Product"));\n'
                    "    }\n"
                    "}\n"
                ),
            )
            sibling = root / "Packages/fixture/Tests/Runtime/FixtureValidation.Check.cs"
            sibling.write_text(
                "public static partial class FixtureValidation {\n"
                "    private static void Check(bool condition) {\n"
                "        if (!condition) throw new System.Exception();\n"
                "    }\n"
                "}\n",
                encoding="utf-8",
            )
            subprocess.run(["git", "-C", str(root), "add", "-A"], check=True)
            subprocess.run(["git", "-C", str(root), "commit", "-q", "-m", "partial-base"], check=True)
            sibling.write_text(
                "public static partial class FixtureValidation {\n"
                "    private static void Check(bool condition) { }\n"
                "}\n",
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code)
            self.assertIn('field": "harness_signature"', output)
