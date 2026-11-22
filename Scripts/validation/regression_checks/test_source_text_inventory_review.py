"""Exercise source inventory review regressions through real Git fixture gates."""

from __future__ import annotations

import subprocess
import sys
import tempfile
import unittest
import json
from pathlib import Path

from .test_source_text_inventory_semantics import _FixtureSupport


class SourceTextInventoryReviewTests(_FixtureSupport, unittest.TestCase):
    """Keep helper arguments and branch exits bound to existing inventory rows."""

    def test_delegate_flow_regression_matrix(self):
        """Cover field modifiers, scope shadowing, qualified calls, and overloads."""
        used = 'private static void Used() { var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs"); Check(source.Contains("Product")); }'
        cases = (
            ("modified field", f"using System; public static class FixtureValidation {{ {used} private static void Noop() {{ }} private static readonly Action run = Used; public static void Validate() {{ run(); }} }}", "private static readonly Action run = Used;", "private static readonly Action run = Noop;", 1, None),
            ("same-name local", f"using System; public static class FixtureValidation {{ {used} private static void Consume(Action run) {{ }} public static void Validate() {{ var run = 1; }} }}", "var run = 1;", "var run = 2;", 0, None),
            ("qualified scalar", f"public static class FixtureValidation {{ {used} public static void Validate() {{ var mode = External.Mode; External.Run(mode); }} }} public static class External {{ public static int Mode => 1; public static int Other => 2; public static void Run(int mode) {{ }} }}", "External.Mode", "External.Other", 1, None),
            ("scalar overload", f"using System; public static class FixtureValidation {{ {used} private static void Run(Action action) {{ action(); }} private static void Run(int mode) {{ }} public static void Validate() {{ var mode = 1; Run(mode); }} }}", "var mode = 1;", "var mode = 2;", 0, None),
        )
        for name, validation_text, old, new, expected, external_text in cases:
            with self.subTest(name=name), tempfile.TemporaryDirectory() as directory:
                root = self._git_fixture(directory, validation_text=validation_text)
                path = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
                if external_text:
                    path = root / "Packages/fixture/Tests/Runtime/External.cs"
                    path.write_text(external_text, encoding="utf-8")
                    subprocess.run(["git", "-C", str(root), "add", "-A"], check=True)
                    subprocess.run(["git", "-C", str(root), "commit", "-qm", "external"], check=True)
                path.write_text(path.read_text(encoding="utf-8").replace(old, new), encoding="utf-8")
                code, output = self._run_fixture_gate(root)
                self.assertEqual(expected, code, output)

    def test_delegate_expression_states_join_and_fail_closed(self):
        """Fail closed when composite delegate expressions cross an unknown boundary."""
        cases = (
            ("conditional", "var run = External.Factory; External.Execute(true ? run : run);", "External.Factory", "External.NoopFactory", "public static void Execute(Action action) { action(); }"),
            ("coalesce", "var run = External.Factory; External.Execute(run ?? run);", "External.Factory", "External.NoopFactory", "public static void Execute(Action action) { action(); }"),
            ("invocation", "External.Execute(External.Get());", "External.Get()", "External.Zero()", "public static void Execute(Action action) { action(); } public static Action Get() => FixtureValidation.Used; public static Action Zero() => FixtureValidation.Noop;"),
        )
        for label, body, original, replacement, members in cases:
            with self.subTest(label=label), tempfile.TemporaryDirectory() as directory:
                root = self._git_fixture(directory, validation_text=(
                    "using System; public static class FixtureValidation { "
                    "private static void Used() { var source = ReadRequiredRepoText(\"Packages/fixture/Runtime/Product.cs\"); Check(source.Contains(\"Product\")); } "
                    f"private static void Noop() {{ }} public static void Validate() {{ {body} }} }}"
                ))
                external = root / "Packages/fixture/Tests/Runtime/External.cs"
                external.write_text(
                    "using System; public static class External { "
                    "public static Action Factory => FixtureValidation.Used; public static Action NoopFactory => FixtureValidation.Noop; "
                    + members + " }",
                    encoding="utf-8",
                )
                subprocess.run(["git", "-C", str(root), "add", "-A"], check=True)
                subprocess.run(["git", "-C", str(root), "commit", "-qm", "composite-delegate"], check=True)
                validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
                validation.write_text(validation.read_text(encoding="utf-8").replace(original, replacement), encoding="utf-8")
                code, output = self._run_fixture_gate(root)
                self.assertEqual(1, code, output)
                self.assertTrue(
                    "SOURCE_TEXT_RATCHET_UNSUPPORTED_DELEGATE_FLOW" in output
                    or "SOURCE_TEXT_RATCHET_HARNESS_CHANGED" in output,
                    output,
                )

    def test_unknown_qualified_boundaries_fail_closed_without_type_heuristics(self):
        """Reject unresolved scalar and instance boundaries under the finite contract."""
        cases = (
            ("scalar unknown", "private static void Run(Action action) { action(); } var mode = External.Mode; External.Execute(mode);", "External.Mode", "External.Other", "public static int Mode => 1; public static int Other => 2; public static void Execute(int mode) { }"),
            ("instance unknown", "var run = External.Factory; var external = new External(); external.Execute(run);", "External.Factory", "External.NoopFactory", "public static Action Factory => FixtureValidation.Used; public static Action NoopFactory => FixtureValidation.Noop; public void Execute(Action action) { action(); }"),
        )
        for label, body, original, replacement, members in cases:
            with self.subTest(label=label), tempfile.TemporaryDirectory() as directory:
                root = self._git_fixture(directory, validation_text=(
                    "using System; public static class FixtureValidation { "
                    "private static void Used() { var source = ReadRequiredRepoText(\"Packages/fixture/Runtime/Product.cs\"); Check(source.Contains(\"Product\")); } "
                    f"private static void Noop() {{ }} public static void Validate() {{ {body} }} }}"
                ))
                external = root / "Packages/fixture/Tests/Runtime/External.cs"
                external.write_text("using System; public class External { " + members + " }", encoding="utf-8")
                subprocess.run(["git", "-C", str(root), "add", "-A"], check=True)
                subprocess.run(["git", "-C", str(root), "commit", "-qm", "unknown-boundary"], check=True)
                validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
                validation.write_text(validation.read_text(encoding="utf-8").replace(original, replacement), encoding="utf-8")
                code, output = self._run_fixture_gate(root)
                self.assertEqual(1, code, output)
                self.assertTrue(
                    "SOURCE_TEXT_RATCHET_UNSUPPORTED_DELEGATE_FLOW" in output
                    or "SOURCE_TEXT_RATCHET_HARNESS_CHANGED" in output,
                    output,
                )

    def _run_migration_cli(self, root, approval=None, trusted_base=None):
        """Run the proposed checker with an optional workflow migration approval."""
        args = [
            sys.executable,
            "-B",
            str(root / "Scripts/validation/source_text_inventory.py"),
            "--root",
            str(root),
            "--base-ref",
            "HEAD",
            "--output",
            str(root / "build/inventory"),
        ]
        if approval is not None:
            args.extend(["--parser-migration-approval", str(approval)])
        if trusted_base is not None:
            args.extend(["--trusted-base-inventory", str(trusted_base)])
        return subprocess.run(args, capture_output=True, text=True, check=False)

    def test_versioned_parser_maintenance_requires_exact_scope_and_fingerprints(self):
        """Allow only an exact protected approval for a versioned parser maintenance change."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(directory)
            checker = root / "Scripts/validation/source_text_inventory.py"
            original = checker.read_text(encoding="utf-8")
            maintenance = (
                "\n_original_inventory = inventory\n"
                "def inventory(root):\n"
                "    rows = _original_inventory(root)\n"
                "    for row in rows:\n"
                "        row['harness_signature'] = row['legacy_harness_signature']\n"
                "    return rows\n"
            )
            checker.write_text(
                original.replace("\nif __name__ == \"__main__\":", maintenance + "\nif __name__ == \"__main__\":"),
                encoding="utf-8",
            )
            result = self._run_migration_cli(root)
            self.assertEqual(1, result.returncode, result.stdout + result.stderr)
            evidence = json.loads(
                (root / "build/inventory/parser_migration_evidence.json").read_text(encoding="utf-8")
            )
            approval = root / "build/versioned-parser-approval.json"
            approval.write_text(
                json.dumps({
                    **evidence["fingerprints"],
                    "scope": "versioned-parser-maintenance",
                    "reason": "Reviewed exact parser maintenance change.",
                }),
                encoding="utf-8",
            )
            result = self._run_migration_cli(root, approval)
            self.assertEqual(0, result.returncode, result.stdout + result.stderr)
            self.assertIn("PARSER_MIGRATION_APPROVED", result.stdout)
            approval.write_text(
                json.dumps({
                    **evidence["fingerprints"],
                    "scope": "versioned-parser-maintenance",
                    "parser_changes_sha256": "0" * 64,
                    "reason": "Reviewed exact parser maintenance change.",
                }),
                encoding="utf-8",
            )
            result = self._run_migration_cli(root, approval)
            self.assertEqual(1, result.returncode, result.stdout + result.stderr)
            self.assertIn("PARSER_MIGRATION_REJECTED", result.stdout)

    def test_external_helper_index_ignores_unscanned_performance_sources(self):
        """Keep unrelated performance sources out of validation helper resolution."""
        from Scripts.validation.source_text_inventory import _ExternalHelperIndex

        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            runtime = root / "Packages" / "fixture" / "Tests" / "Runtime"
            runtime.mkdir(parents=True)
            performance = root / "Packages" / "fixture" / "Tests" / "Performance"
            performance.mkdir(parents=True)
            performance_file = performance / "Unrelated.cs"
            performance_file.write_text("public sealed class Unrelated {}\n", encoding="utf-8")
            index = _ExternalHelperIndex(root)
            self.assertNotIn(performance_file, index.units)

    def test_cli_reuses_trusted_base_inventory_report(self):
        """Allow the workflow's trusted base report to replace a duplicate base scan."""
        from Scripts.validation.source_text_inventory import inventory
        from Scripts.validation.source_text_inventory_report import write_report

        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(directory)
            trusted = root / "build" / "trusted-inventory"
            write_report(inventory(root), trusted)
            result = self._run_migration_cli(root, trusted_base=trusted)
            self.assertEqual(0, result.returncode, result.stdout + result.stderr)
            self.assertIn("SOURCE_TEXT_INVENTORY_BASE_OK", result.stdout)

    def test_signature_cache_isolates_callers_and_resolved_roots(self):
        """Keep result reuse scoped to caller paths, actual candidates, and source indexes."""
        from types import SimpleNamespace
        from unittest.mock import Mock, patch
        from Scripts.validation.source_text_inventory import _ExternalHelperIndex

        root = Path.cwd() / "build/tmp/signature-memory"
        local, other = root / "A.cs", root / "B.cs"
        method = SimpleNamespace(body_start=1)
        records = [(local, (), SimpleNamespace(start=1)), (local, (), SimpleNamespace(start=2)),
                   (other, (), SimpleNamespace(start=1))]
        closures = {(local, 1): ({"first"}, {local}), (local, 2): ({"second"}, {local}),
                    (other, 1): ({"other"}, {other})}
        with patch.object(Path, "glob", return_value=()):
            first, fresh = (_ExternalHelperIndex(root) for _ in range(2))
        for index in (first, fresh):
            index.units = {path: {"regions": [(0, 2, "Checks")]} for path in (local, other)}
            index._calls = Mock()
            index._closure = Mock(side_effect=lambda record: closures[(record[0], record[2].start)])
        requests = (
            (local, (records[0],)), (other, (records[0],)), (other, (records[1],)),
            (other, (records[0],)), (other, (records[0], records[1])),
            (other, (records[1], records[0], records[1])), (other, (records[2],)), (other, ()),
        )
        signatures = []
        for caller, selected in requests:
            first._calls.return_value = selected
            signatures.append(first.signature(caller, method, "same-expression"))
        self.assertEqual("", signatures[0])
        self.assertTrue(signatures[1])
        self.assertNotEqual(signatures[1], signatures[2])
        self.assertEqual(signatures[1], signatures[3])
        self.assertEqual(signatures[4], signatures[5])
        self.assertEqual(["", ""], signatures[6:])
        self.assertEqual(len(requests), first._calls.call_count)
        closures[(local, 1)] = ({"changed"}, {local})
        fresh._calls.return_value = (records[0],)
        self.assertNotEqual(signatures[1], fresh.signature(other, method, "same-expression"))

    def test_later_catch_ignores_unrelated_handlers_and_comments(self):
        """Keep other try chains, later handlers, and comments outside a catch prefix."""
        cases = (
            (
                'try { System.Console.WriteLine(0); } '
                'catch (System.Exception e) when (string.IsNullOrEmpty("unrelated")) { } '
                'try { int.Parse("invalid"); } catch (System.Exception) { Check(source.Contains("Product")); }',
                '"unrelated"', '""',
            ),
            (
                'try { int.Parse("invalid"); } '
                'catch (System.Exception e) when (string.IsNullOrEmpty("reject")) { } '
                'catch (System.Exception) { Check(source.Contains("Product")); }',
                ") when (", ") /* unchanged filter */ when (",
            ),
            (
                'try { int.Parse("invalid"); } catch (System.ArgumentException) { } '
                'catch (System.FormatException) { Check(source.Contains("Product")); } '
                'catch (System.Exception) { System.Console.WriteLine("later"); }',
                '"later"', '"changed-later"',
            ),
        )
        for body, old, new in cases:
            with self.subTest(change=old):
                self._assert_flow_mutation(body, old, new, 0)




    def test_unused_nested_barriers_preserve_outer_assertion(self):
        """Keep uncalled local-function and lambda barriers out of the enclosing method."""
        cases = (
            (
                "async System.Threading.Tasks.Task Unused() { await System.Threading.Tasks.Task.Delay(0); }",
                "Delay(0)",
                "Delay(-1)",
            ),
            ("void Unused() { System.Environment.Exit(0); }", "Exit(0)", "Exit(1)"),
            (
                "System.Func<System.Threading.Tasks.Task<string>> unused = async () => "
                '{ return await System.Threading.Tasks.Task.FromResult("before"); };',
                '"before"',
                '"after"',
            ),
            ('System.Action unused = () => { System.Environment.FailFast("before"); };', '"before"', '"after"'),
        )
        for prelude, old, new in cases:
            with self.subTest(prelude=prelude):
                self._assert_flow_mutation(prelude + ' Check(source.Contains("Product"));', old, new, 0)

    def test_execution_barrier_comments_and_unrelated_checks_are_strengthening(self):
        """Allow comments and unrelated checks around an unchanged execution barrier."""
        for prelude, async_entry in (
            ("await System.Threading.Tasks.Task.Delay(0);", True),
            ("System.Environment.Exit(0);", False),
        ):
            with self.subTest(prelude=prelude):
                self._assert_flow_mutation(
                    prelude + ' Check(source.Contains("Product"));',
                    prelude,
                    "/* unchanged execution */ " + prelude + " Check(1 + 1 == 2);",
                    0,
                    async_entry=async_entry,
                )



    def test_unrelated_branch_conditions_preserve_else_binding(self):
        """Keep unrelated preceding and nested branches out of an assertion's else chain."""
        cases = (
            (
                "if (source.Length < -1) { } System.Console.WriteLine(0); "
                'if (source.Length < 0) { } else { Check(source.Contains("Product")); }',
                "source.Length < -1",
                "source.Length >= 0",
            ),
            (
                'if (source.Length < 0) { if (source.Length >= 0) { } } else { Check(source.Contains("Product")); }',
                "source.Length >= 0",
                "source.Length < 0",
            ),
        )
        for body, old, new in cases:
            with self.subTest(body=body):
                self._assert_flow_mutation(body, old, new, 0)

    def _assert_flow_mutation(self, body, old, new, expected, async_entry=False, remove_product=False):
        """Observe a control-flow mutation through the real CLI and fixture Git baseline."""
        with tempfile.TemporaryDirectory() as directory:
            modifiers = "public static async void" if async_entry else "public static void"
            root = self._git_fixture(
                directory,
                validation_text=(
                    "public static class FixtureValidation {\n"
                    f"    {modifiers} Validate() {{\n"
                    '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                    f"        {body}\n"
                    "    }\n}\n"
                ),
            )
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace(old, new, 1), encoding="utf-8"
            )
            if remove_product:
                product = root / "Packages/fixture/Runtime/Product.cs"
                product.write_text("public sealed class Other {}\n", encoding="utf-8")
            code, output = self._run_fixture_gate(root)
            self.assertEqual(expected, code, output)
            if expected:
                self.assertIn('field": "reachability"', output)




    def test_unrelated_local_edits_preserve_helper_binding(self):
        """Allow unrelated checks, locals, and writes occurring after the helper call."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._argument_fixture(
                directory,
                "bool unrelated = true; bool shouldRun = true;",
                "RunChecks(shouldRun); shouldRun = true;",
            )
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace("unrelated = true", "unrelated = false").replace(
                    "RunChecks(shouldRun); shouldRun = true;", "Check(1 + 1 == 2); RunChecks(shouldRun); shouldRun = false;"
                ),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(0, code, output)


    def _argument_fixture(self, directory, statements, call_statement):
        """Create a registered assertion helper with caller-local argument setup."""
        return self._git_fixture(
            directory,
            validation_text=(
                "public static class FixtureValidation {\n"
                "    private static void RunChecks(bool enabled) {\n"
                '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                '        if (enabled) Check(source.Contains("Product"));\n'
                "    }\n"
                "    public static void Validate() {\n"
                f"        {statements}\n"
                f"        {call_statement}\n"
                "    }\n}\n"
            ),
        )









    def _assert_preprocessor_change_rejected(self, branches, old, new, close_count=1):
        """Run the real gate after changing an assertion's preprocessor branch history."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(
                directory,
                validation_text=(
                    "public static class FixtureValidation {\n"
                    "    public static void Validate() {\n"
                    '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                    + branches
                    + '        Check(source.Contains("Product"));\n'
                    + "#endif\n" * close_count
                    + "    }\n}\n"
                ),
            )
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace(old, new, 1), encoding="utf-8"
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code, output)
            self.assertIn('field": "reachability"', output)
