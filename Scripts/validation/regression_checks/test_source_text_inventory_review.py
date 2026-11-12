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

    def test_csv_legacy_baseline_preserves_parser_migration_status(self):
        """Treat an empty CSV parser version as legacy rather than versioned."""
        from Scripts.validation.source_text_inventory import inventory
        from Scripts.validation.source_text_inventory_report import write_report
        from Scripts.validation.source_text_inventory_ratchet import (
            _load_inventory_report,
            _parser_semantic_changes,
        )

        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(directory)
            row = inventory(root)[0]
            row.pop("parser_version", None)
            report = root / "build" / "trusted-inventory"
            write_report([row], report)
            loaded = _load_inventory_report(report)
            changes, strengthened = _parser_semantic_changes(
                loaded,
                [{**row, "parser_version": "2"}],
            )
        self.assertEqual([], changes)
        self.assertIn("parser_schema_added", {item["field"] for item in strengthened})

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

    def test_clr_builder_binding_preserves_repository_helpers(self):
        """Separate CLR instance members while retaining repository types and extensions."""
        scratch = Path(__file__).resolve().parents[3] / "build/tmp/scratch"
        scratch.mkdir(parents=True, exist_ok=True)
        cases = (
            ("import", "using System.Text;", "StringBuilder", 0),
            ("alias", "using Builder = global::System.Text.StringBuilder;", "Builder", 0),
            ("qualified", "", "global::System.Text.StringBuilder", 0),
            ("repo-type", "using System.Text;", "StringBuilder", 1),
            ("repo-type-fake", "using System.Text;", "StringBuilder", 1),
            ("repo-factory-fake", "using System.Text;", "StringBuilder", 1),
            ("repo-sibling", "using System.Text;", "StringBuilder", 1),
            ("extension", "using System.Text;", "StringBuilder", 1),
        )
        for name, imports, constructor, expected in cases:
            with self.subTest(name=name), tempfile.TemporaryDirectory(dir=scratch) as directory:
                root = self._git_fixture(directory, validation_text=(
                    "using Helpers; public static class FixtureValidation { public static void Validate() {\n"
                    'var source = Sources.ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                    'Check(source.Contains("Product"));\n} }\n'
                ))
                folder = root / "Packages/fixture/Tests/Runtime"
                member = "ReadFixtureText" if name == "extension" else "ToString"
                creation = f"new {constructor}(System.IO.File.ReadAllText(path))"
                extra = ""
                if name.endswith("fake"):
                    extra = 'var unused = "; var builder = new global::System.Text.StringBuilder();"; '
                factory = ""
                if name == "repo-factory-fake":
                    creation = "MakeBuilder(path)"
                    factory = "private static StringBuilder MakeBuilder(string path) => new StringBuilder(System.IO.File.ReadAllText(path)); "
                read_body = f'var builder = {creation}; ' + extra + 'builder.Append(""); ' + f"return builder.{member}(); "
                if name == "repo-sibling":
                    read_body = (
                        f"string result; {{ var builder = {creation}; result = builder.ToString(); }} "
                        '{ var builder = new global::System.Text.StringBuilder(); builder.Append(""); } return result; '
                    )
                (folder / "Sources.cs").write_text(
                    imports + "\nnamespace Helpers { public static class Sources { "
                    "public static string ReadRequiredRepoText(string path) { "
                    + read_body + "} " + factory + "} }\n", encoding="utf-8",
                )
                noise = folder / "Noise.cs"
                noise.write_text(
                    "namespace Noise { public sealed class Unrelated { "
                    'public override string ToString() => "before"; '
                    'public void Append(string value) { System.Console.WriteLine("before"); } } }\n', encoding="utf-8",
                )
                changed = noise
                before, after = '"before"', '"after"'
                if name.startswith("repo-"):
                    changed = folder / "Builder.cs"
                    changed.write_text(
                        "namespace Helpers { public sealed class StringBuilder { private readonly string text; "
                        "public StringBuilder(string value) { text = value; } public void Append(string value) { } "
                        "public override string ToString() => text; } }\n", encoding="utf-8",
                    )
                    before, after = "=> text;", '=> "Product";'
                elif name == "extension":
                    changed = folder / "Extensions.cs"
                    changed.write_text(
                        "using System.Text; namespace Helpers { public static class BuilderExtensions { "
                        "public static string ReadFixtureText(this StringBuilder builder) => builder.ToString(); } }\n",
                        encoding="utf-8",
                    )
                    before, after = "=> builder.ToString();", '=> "Product";'
                subprocess.run(["git", "-C", str(root), "add", "-A"], check=True)
                subprocess.run(["git", "-C", str(root), "commit", "-qm", "clr-builder-base"], check=True)
                command = [sys.executable, "-B", str(root / "Scripts/validation/source_text_inventory.py"),
                           "--root", str(root), "--base-ref", "HEAD", "--output", str(root / "build/inventory")]
                baseline = subprocess.run(command, capture_output=True, text=True, check=False)
                self.assertEqual(0, baseline.returncode, baseline.stdout + baseline.stderr)
                self.assertIn("SOURCE_TEXT_INVENTORY_OK rows=1 ", baseline.stdout)
                changed.write_text(changed.read_text(encoding="utf-8").replace(before, after), encoding="utf-8")
                if expected:
                    (root / "Packages/fixture/Runtime/Product.cs").write_text(
                        "public sealed class Other {}\n", encoding="utf-8"
                    )
                result = subprocess.run(command, capture_output=True, text=True, check=False)
                self.assertEqual(expected, result.returncode, result.stdout + result.stderr)
                if expected:
                    self.assertIn('"field": "harness_signature"', result.stdout)

    def test_package_local_helper_identity_precedes_foreign_namesakes(self):
        """Keep same-name package helpers distinct without excluding external-only types."""
        scratch = Path(__file__).resolve().parents[3] / "build/tmp/scratch"
        scratch.mkdir(parents=True, exist_ok=True)
        cases = (
            ("foreign-method", "foreign", "Sources.cs", 0),
            ("foreign-field", "foreign", "State.cs", 0),
            ("own-method", "own", "Sources.cs", 1),
            ("own-field", "own", "State.cs", 1),
            ("cross-package", "foreign", "Sources.cs", 1),
            ("bare-import", "own", "Fallback.cs", 1),
        )
        for name, selected, filename, expected in cases:
            with self.subTest(name=name), tempfile.TemporaryDirectory(dir=scratch) as directory:
                imported = "using static Helpers.Fallback;" if name == "bare-import" else "using Helpers;"
                reader = "ReadRequiredRepoText" if name == "bare-import" else "Sources.ReadRequiredRepoText"
                root = self._git_fixture(directory, validation_text=(
                    imported + "\nnamespace Checks { public static class FixtureValidation { public static void Validate() {\n"
                    + f'var source = {reader}("Packages/fixture/Runtime/Product.cs");\n'
                    + 'Check(source.Contains("Product"));\n} } }\n'
                ))
                own = root / "Packages/fixture/Tests/Runtime"
                foreign = root / "Packages/aaa-other/Tests/Runtime"
                foreign.mkdir(parents=True)
                sources = (
                    "namespace Helpers { public static partial class Sources { "
                    "public static string ReadRequiredRepoText(string path) => "
                    'Spoof ? "Product" : System.IO.File.ReadAllText(path); } }\n'
                )
                state = "namespace Helpers { public static partial class Sources { private const bool Spoof = false; } }\n"
                for folder in (own, foreign):
                    if folder == own and name == "cross-package":
                        continue
                    (folder / "Sources.cs").write_text(sources, encoding="utf-8")
                    (folder / "State.cs").write_text(state, encoding="utf-8")
                (own / "Fallback.cs").write_text(
                    "namespace Helpers { public static class Fallback { "
                    "public static string ReadRequiredRepoText(string path) => System.IO.File.ReadAllText(path); } }\n",
                    encoding="utf-8",
                )
                (foreign / "FixtureValidation.cs").write_text(
                    "namespace Checks { public static class FixtureValidation { "
                    'public static string ReadRequiredRepoText(string path) => "Foreign"; } }\n', encoding="utf-8",
                )
                subprocess.run(["git", "-C", str(root), "add", "-A"], check=True)
                subprocess.run(["git", "-C", str(root), "commit", "-qm", "package-helper-base"], check=True)
                command = [sys.executable, "-B", str(root / "Scripts/validation/source_text_inventory.py"),
                           "--root", str(root), "--base-ref", "HEAD", "--output", str(root / "build/inventory")]
                baseline = subprocess.run(command, capture_output=True, text=True, check=False)
                self.assertEqual(0, baseline.returncode, baseline.stdout + baseline.stderr)
                changed = (own if selected == "own" else foreign) / filename
                before, after = ("Spoof = false", "Spoof = true") if filename == "State.cs" else (
                    "System.IO.File.ReadAllText(path)", '"Product"'
                )
                changed.write_text(changed.read_text(encoding="utf-8").replace(before, after), encoding="utf-8")
                if expected:
                    (root / "Packages/fixture/Runtime/Product.cs").write_text(
                        "public sealed class Other {}\n", encoding="utf-8"
                    )
                result = subprocess.run(command, capture_output=True, text=True, check=False)
                self.assertEqual(expected, result.returncode, result.stdout + result.stderr)
                if expected:
                    self.assertIn('"field": "harness_signature"', result.stdout)

    def test_unconditional_helper_call_survives_parallel_context_binding(self):
        """Reject losing an effective call while preserving unrelated call-site additions."""
        for remove_call in (True, False):
            with self.subTest(remove_call=remove_call), tempfile.TemporaryDirectory() as directory:
                root = self._git_fixture(
                    directory,
                    validation_text=(
                        "public static class FixtureValidation {\n"
                        "    private static void RunChecks() {\n"
                        '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                        '        Check(source.Contains("Product"));\n'
                        "    }\n"
                        "    public static void Validate() {\n"
                        '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                        "        RunChecks();\n"
                        "        if (source.Length < 0) RunChecks();\n"
                        "    }\n}\n"
                    ),
                )
                validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
                replacement = "" if remove_call else "        // unchanged effective call\n        Check(true);\n        RunChecks();\n"
                validation.write_text(
                    validation.read_text(encoding="utf-8").replace("        RunChecks();\n", replacement, 1), encoding="utf-8"
                )
                if remove_call:
                    product = root / "Packages/fixture/Runtime/Product.cs"
                    product.write_text("public sealed class Other {}\n", encoding="utf-8")
                code, output = self._run_fixture_gate(root)
                self.assertEqual(1 if remove_call else 0, code, output)
                if remove_call:
                    self.assertIn('field": "call_path"', output)

    def test_catch_assertion_retains_protected_try_body(self):
        """Reject removing the exception that reaches an existing catch assertion."""
        body = 'try { int.Parse("invalid"); } catch (System.FormatException) { Check(source.Contains("Product")); }'
        self._assert_flow_mutation(body, '"invalid"', '"123"', 1, remove_product=True)
        for replacement in ('/* same exception */ int.Parse("invalid");', 'int.Parse("invalid"); // same exception\n'):
            with self.subTest(replacement=replacement):
                self._assert_flow_mutation(body, 'int.Parse("invalid");', replacement, 0)

    def test_state_writer_call_order_controls_assertion_helper(self):
        """Reject reordered state writes while allowing unrelated calls and comments."""
        original = "SetEnabled(true); RunChecks(); SetEnabled(false);"
        cases = (
            ("enabled", " static", "SetEnabled(true); SetEnabled(false); RunChecks();", 1, False),
            ("FixtureValidation.enabled", " static", "SetEnabled(true); SetEnabled(false); RunChecks();", 1, False),
            ("this.enabled", "", "SetEnabled(true); SetEnabled(false); RunChecks();", 1, False),
            ("enabled", " static", "SetEnabled(true); /* same state */ Check(true); RunChecks(); SetEnabled(false);", 0, False),
            ("enabled", " static", "Log(); SetEnabled(true); RunChecks(); SetEnabled(false);", 0, False),
            ("enabled", " static", "SetEnabled(true); Check(true); RunChecks(); SetEnabled(false);", 0, True),
        )
        for field, modifier, replacement, expected, counted in cases:
            with self.subTest(field=field, replacement=replacement, counted=counted), tempfile.TemporaryDirectory() as directory:
                counter = (
                    "    private static int checks;\n"
                    "    private static void Check(bool ok) { checks++; if (!ok) throw new System.Exception(); }\n"
                ) if counted else ""
                root = self._git_fixture(directory, validation_text=(
                    f"public{modifier} class FixtureValidation {{\n"
                    + counter
                    + f"    private{modifier} bool enabled;\n"
                    f"    private{modifier} void SetEnabled(bool value) {{ {field} = value; }}\n"
                    f"    private{modifier} void Log() {{ System.Console.WriteLine(0); }}\n"
                    f"    private{modifier} void RunChecks() {{\n"
                    '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                    '        if (enabled) { Check(source.Contains("Product")); }\n'
                    "    }\n"
                    f"    public{modifier} void Validate() {{ {original} }}\n"
                    "}\n"
                ))
                validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
                validation.write_text(validation.read_text(encoding="utf-8").replace(original, replacement), encoding="utf-8")
                if expected:
                    (root / "Packages/fixture/Runtime/Product.cs").write_text("public sealed class Other {}\n", encoding="utf-8")
                code, output = self._run_fixture_gate(root)
                self.assertEqual(expected, code, output)

    def test_later_catch_retains_ordered_preceding_handlers(self):
        """Reject earlier handler changes that can divert a later catch assertion."""
        cases = (
            (
                'try { int.Parse("invalid"); } '
                'catch (System.Exception e) when (string.IsNullOrEmpty("reject")) { } '
                'catch (System.Exception) { Check(source.Contains("Product")); }',
                '"reject"', '""', True,
            ),
            (
                'try { int.Parse("invalid"); } '
                'catch (System.ArgumentException e) when (!string.IsNullOrEmpty("keep")) { } '
                'catch (System.Exception) { Check(source.Contains("Product")); }',
                "System.ArgumentException", "System.Exception", True,
            ),
            (
                'try { int.Parse("invalid"); } '
                'catch (System.Exception e) when (string.IsNullOrEmpty("reject")) { } '
                'catch (System.FormatException e) when (string.IsNullOrEmpty("also-reject")) { } '
                'catch (System.Exception) { Check(source.Contains("Product")); }',
                '"reject"', '""', True,
            ),
            (
                'try { int.Parse("invalid"); } '
                'catch (System.Exception e) when (string.IsNullOrEmpty("reject")) { System.Console.WriteLine("before"); } '
                'catch (System.Exception) { Check(source.Contains("Product")); }',
                '"before"', '"after"', False,
            ),
        )
        for body, old, new, remove_product in cases:
            with self.subTest(change=old):
                self._assert_flow_mutation(body, old, new, 1, remove_product=remove_product)

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

    def test_preceding_await_expression_change_requires_ack(self):
        """Reject an await that stops completing through literal or caller-local changes."""
        cases = (
            ("await System.Threading.Tasks.Task.Delay(0);", "Delay(0)", "Delay(-1)"),
            ("int delay = 0; await System.Threading.Tasks.Task.Delay(delay);", "delay = 0", "delay = -1"),
            (
                "int initial = 0; int delay = initial; await System.Threading.Tasks.Task.Delay(delay);",
                "initial = 0",
                "initial = -1",
            ),
        )
        for prelude, old, new in cases:
            with self.subTest(prelude=prelude):
                body = prelude + ' Check(source.Contains("Product"));'
                self._assert_flow_mutation(body, old, new, 1, async_entry=True)

    def test_direct_process_termination_before_assertion_requires_ack(self):
        """Reject direct Exit and FailFast calls that terminate the validation runner."""
        for qualifier in ("Environment", "System.Environment", "global::System.Environment"):
            for invocation in ('Exit(0)', 'FailFast("failure")'):
                with self.subTest(qualifier=qualifier, invocation=invocation):
                    assertion = 'Check(source.Contains("Product"));'
                    self._assert_flow_mutation(assertion, assertion, f"{qualifier}.{invocation}; " + assertion, 1)

    def test_partial_sibling_termination_helper_requires_ack(self):
        """Reject a terminating helper implemented in a sibling partial-class file."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(directory, validation_text=(
                "public static partial class FixtureValidation {\n"
                "    public static void Validate() {\n"
                '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                '        Check(source.Contains("Product"));\n'
                "    }\n}\n"
            ))
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            (validation.with_name("FixtureValidation.Part2.cs")).write_text(
                "public static partial class FixtureValidation {\n"
                "    private static void ExitSuccessfully() { System.Environment.Exit(0); }\n"
                "}\n",
                encoding="utf-8",
            )
            subprocess.run(["git", "-C", str(root), "add", "-A"], check=True)
            subprocess.run(["git", "-C", str(root), "commit", "-qm", "partial-helper-base"], check=True)
            baseline = self._run_fixture_gate(root)
            self.assertEqual(0, baseline[0], baseline[1])
            validation.write_text(
                validation.read_text(encoding="utf-8").replace(
                    '        Check(source.Contains("Product"));',
                    '        ExitSuccessfully();\n        Check(source.Contains("Product"));',
                ),
                encoding="utf-8",
            )
            (root / "Packages/fixture/Runtime/Product.cs").write_text(
                "public sealed class Other {}\n", encoding="utf-8"
            )
            result = self._run_fixture_gate(root)
            self.assertEqual(1, result[0], result[1])
            self.assertIn('field": "reachability"', result[1])

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

    def test_handler_literal_change_requires_ack(self):
        """Reject literal changes in filters, rethrows, and finally exception replacement."""
        bodies = (
            'try { Check(source.Contains("Product")); } '
            'catch (System.Exception e) when (string.IsNullOrEmpty("reject")) { }',
            'try { throw new System.Exception(); } '
            'catch (System.Exception e) when (!string.IsNullOrEmpty("reject")) { Check(source.Contains("Product")); } '
            'catch (System.Exception) { }',
            'try { Check(source.Contains("Product")); } '
            'catch (System.Exception) { if (!string.IsNullOrEmpty("reject")) throw; }',
            'try { try { Check(source.Contains("Product")); } '
            'finally { if (string.IsNullOrEmpty("reject")) throw new System.OperationCanceledException(); } } '
            'catch (System.OperationCanceledException) { }',
        )
        for body in bodies:
            with self.subTest(body=body):
                self._assert_flow_mutation(body, '"reject"', '""', 1)

    def test_runtime_else_chain_condition_change_requires_ack(self):
        """Reject suppressing chained assertions through any prior or enclosing condition."""
        cases = (
            (
                "braced-final-else",
                'if (source.Length < -1) { } else if (source.Length < 0) { } else { Check(source.Contains("Product")); }',
                "source.Length < -1",
                "source.Length >= 0",
            ),
            (
                "braced-else-if",
                'if (source.Length < 0) { } else if (source.Length >= 0) { Check(source.Contains("Product")); }',
                "source.Length < 0",
                "source.Length >= 0",
            ),
            (
                "multiple-else-if",
                "if (source.Length < -2) { } else if (source.Length < -1) { } "
                'else if (source.Length < 0) { } else { Check(source.Contains("Product")); }',
                "source.Length < -1",
                "source.Length >= 0",
            ),
            (
                "unbraced-final-else",
                'if (source.Length < -1) ; else if (source.Length < 0) ; else Check(source.Contains("Product"));',
                "source.Length < -1",
                "source.Length >= 0",
            ),
            (
                "unbraced-outer-chain",
                "if (source.Length >= 0) if (source.Length < -1) ; "
                'else if (source.Length < 0) ; else Check(source.Contains("Product"));',
                "source.Length >= 0",
                "source.Length < 0",
            ),
            (
                "dangling-else",
                'if (source.Length >= 0) if (source.Length < 0) ; else Check(source.Contains("Product"));',
                "source.Length < 0",
                "source.Length >= 0",
            ),
            (
                "nested-braced-chain",
                "if (source.Length < -1) { if (source.Length >= 0) { } } /* branch gap */ "
                'else if (source.Length < 0) { } else { Check(source.Contains("Product")); }',
                "source.Length < -1",
                "source.Length >= 0",
            ),
        )
        for name, body, old, new in cases:
            with self.subTest(name=name):
                self._assert_flow_mutation(body, old, new, 1)

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

    def test_helper_argument_local_definition_change_requires_ack(self):
        """Reject argument changes through local initializers, aliases, and writes."""
        cases = (
            ("initializer", "bool shouldRun = true;", "shouldRun = true", "shouldRun = false"),
            ("alias", "bool seed = true; bool shouldRun = seed;", "seed = true", "seed = false"),
            (
                "forwarded-alias",
                "bool seed = true; bool forwarded = seed; bool shouldRun = forwarded;",
                "seed = true",
                "seed = false",
            ),
            ("reassignment", "bool shouldRun = false; shouldRun = true;", "shouldRun = true", "shouldRun = false"),
            (
                "conditional-write",
                "bool shouldRun = false; bool permit = true; if (permit && true) { shouldRun = true; }",
                "permit = true",
                "permit = false",
            ),
            (
                "compound-write",
                "bool shouldRun = true; bool permit = true; shouldRun &= permit;",
                "permit = true",
                "permit = false",
            ),
        )
        for name, statements, old, new in cases:
            with self.subTest(name=name), tempfile.TemporaryDirectory() as directory:
                root = self._argument_fixture(directory, statements, "RunChecks(shouldRun);")
                validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
                validation.write_text(
                    validation.read_text(encoding="utf-8").replace(old, new), encoding="utf-8"
                )
                code, output = self._run_fixture_gate(root)
                self.assertEqual(1, code, output)
                self.assertIn('field": "call_path"', output)

    def test_helper_call_guard_local_initializer_change_requires_ack(self):
        """Reject disabling a literal-argument helper call through a guard's local value."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._argument_fixture(
                directory, "bool permit = true;", "if (permit && true) RunChecks(true);"
            )
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace("permit = true", "permit = false"), encoding="utf-8"
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code, output)
            self.assertIn('field": "call_path"', output)

    def test_qualified_helper_argument_definition_change_requires_ack(self):
        """Reject local argument changes even when a qualified dispatch stays unbound."""
        for qualifier in ("FixtureValidation.", "global::FixtureValidation."):
            with self.subTest(qualifier=qualifier), tempfile.TemporaryDirectory() as directory:
                root = self._argument_fixture(
                    directory, "bool seed = true; bool shouldRun = seed;", f"{qualifier}RunChecks(shouldRun);"
                )
                validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
                validation.write_text(
                    validation.read_text(encoding="utf-8").replace("seed = true", "seed = false"), encoding="utf-8"
                )
                code, output = self._run_fixture_gate(root)
                self.assertEqual(1, code, output)
                self.assertIn('field": "harness_signature"', output)

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

    def test_qualified_helper_receiver_definition_change_requires_ack(self):
        """Reject replacing the local receiver with an external no-op implementation."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(
                directory,
                validation_text=(
                    "public class FixtureValidation {\n"
                    "    private void RunChecks() {\n"
                    '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                    '        Check(source.Contains("Product"));\n'
                    "    }\n"
                    "    public static void Validate() {\n"
                    "        var target = new FixtureValidation();\n"
                    "        target.RunChecks();\n"
                    "    }\n}\n"
                ),
            )
            external = root / "Packages/fixture/Tests/Runtime/Other.cs"
            external.write_text("public class Other { public void RunChecks() { } }\n", encoding="utf-8")
            subprocess.run(["git", "-C", str(root), "add", "-A"], check=True)
            subprocess.run(["git", "-C", str(root), "commit", "-q", "-m", "receiver-base"], check=True)
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace("new FixtureValidation()", "new Other()"), encoding="utf-8"
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code, output)
            self.assertIn('field": "harness_signature"', output)

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

    def test_nested_exit_outer_condition_change_requires_ack(self):
        """Reject changing any enclosing branch or loop that enables an early return."""
        cases = (
            ("nested-if", "if (source.Length < 0) { if (source.Length >= 0) return; }", "< 0", ">= 0"),
            ("nested-else", "if (source.Length >= 0) { } else { if (source.Length >= 0) return; }", ">= 0", "< 0"),
            (
                "nested-else-if",
                "if (source.Length < 0) { } else if (source.Length == 0) { if (source.Length >= 0) return; }",
                "== 0",
                ">= 0",
            ),
            ("nested-while", "while (source.Length < 0) { if (source.Length >= 0) return; }", "< 0", ">= 0"),
        )
        for name, exit_statement, old, new in cases:
            with self.subTest(name=name), tempfile.TemporaryDirectory() as directory:
                root = self._git_fixture(
                    directory,
                    validation_text=(
                        "public static class FixtureValidation {\n"
                        "    public static void Validate() {\n"
                        '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                        f"        {exit_statement}\n"
                        '        Check(source.Contains("Product"));\n'
                        "    }\n}\n"
                    ),
                )
                validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
                validation.write_text(
                    validation.read_text(encoding="utf-8").replace(old, new, 1), encoding="utf-8"
                )
                code, output = self._run_fixture_gate(root)
                self.assertEqual(1, code, output)
                self.assertIn('field": "reachability"', output)

    def test_external_qualified_helper_call_requires_ack(self):
        """Reject redirecting a local assertion helper to a separate class's no-op."""
        for original, replacement in (
            ("RunChecks()", "Other.RunChecks()"),
            ("FixtureValidation.RunChecks()", "Other.RunChecks()"),
            ("global::FixtureValidation.RunChecks()", "global::Other.RunChecks()"),
        ):
            with self.subTest(original=original), tempfile.TemporaryDirectory() as directory:
                root = self._git_fixture(
                    directory,
                    validation_text=(
                        "public static class FixtureValidation {\n"
                        "    private static void RunChecks() {\n"
                        '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                        '        Check(source.Contains("Product"));\n'
                        "    }\n"
                        f"    public static void Validate() {{ {original}; }}\n"
                        "}\n"
                    ),
                )
                external = root / "Packages/fixture/Tests/Runtime/Other.cs"
                external.write_text(
                    "public static class Other { public static void RunChecks() { } }\n", encoding="utf-8"
                )
                subprocess.run(["git", "-C", str(root), "add", "-A"], check=True)
                subprocess.run(["git", "-C", str(root), "commit", "-q", "-m", "external-base"], check=True)
                validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
                validation.write_text(
                    validation.read_text(encoding="utf-8").replace(
                        f"Validate() {{ {original}; }}", f"Validate() {{ {replacement}; }}"
                    ),
                    encoding="utf-8",
                )
                code, output = self._run_fixture_gate(root)
                self.assertEqual(1, code, output)
                self.assertTrue(
                    'field": "call_path"' in output or 'field": "harness_signature"' in output,
                    output,
                )

    def test_validate_named_helper_noop_requires_ack(self):
        """Reject a no-op Validate helper that is not the registered entrypoint."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(
                directory,
                validation_text=(
                    "public static class FixtureValidation {\n"
                    "    private static void ValidateResult(bool ok) {\n"
                    "        if (!ok) throw new System.Exception();\n"
                    "    }\n"
                    "    public static void Validate() {\n"
                    '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                    '        ValidateResult(source.Contains("Product"));\n'
                    "    }\n"
                    "}\n"
                ),
            )
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace(
                    "        if (!ok) throw new System.Exception();\n", ""
                ),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code, output)
            self.assertIn('field": "harness_signature"', output)

    def test_helper_call_argument_change_requires_ack(self):
        """Reject disabling a source assertion by changing the helper argument."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(
                directory,
                validation_text=(
                    "public static class FixtureValidation {\n"
                    "    private static void RunChecks(bool enabled) {\n"
                    '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                    '        if (enabled) Check(source.Contains("Product"));\n'
                    "    }\n"
                    "    public static void Validate() { RunChecks(true); }\n"
                    "}\n"
                ),
            )
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace("RunChecks(true)", "RunChecks(false)"),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code, output)
            self.assertIn('field": "call_path"', output)

    def test_preprocessor_else_prior_condition_change_requires_ack(self):
        """Reject suppressing an else assertion by changing its prior if condition."""
        self._assert_preprocessor_change_rejected("#if false\n#else\n", "#if false", "#if true")

    def test_preprocessor_elif_prior_condition_change_requires_ack(self):
        """Reject suppressing an elif assertion by changing its prior if condition."""
        self._assert_preprocessor_change_rejected("#if false\n#elif true\n", "#if false", "#if true")

    def test_preprocessor_else_prior_elif_change_requires_ack(self):
        """Reject suppressing an else assertion by changing an earlier elif condition."""
        self._assert_preprocessor_change_rejected(
            "#if false\n#elif false\n#else\n", "#elif false", "#elif true"
        )

    def test_nested_preprocessor_prior_condition_change_requires_ack(self):
        """Reject an outer preprocessor change around a nested else assertion."""
        self._assert_preprocessor_change_rejected(
            "#if false\n#else\n#if false\n#else\n", "#if false", "#if true", close_count=2
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

    def test_do_loop_exit_before_assertion_requires_ack(self):
        """Reject break and continue additions that skip a do-loop source assertion."""
        for exit_kind in ("break", "continue"):
            with self.subTest(exit_kind=exit_kind), tempfile.TemporaryDirectory() as directory:
                root = self._git_fixture(
                    directory,
                    validation_text=(
                        "public static class FixtureValidation {\n"
                        "    public static void Validate() {\n"
                        '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                        "        do {\n"
                        '            Check(source.Contains("Product"));\n'
                        "        } while (false);\n"
                        "    }\n"
                        "}\n"
                    ),
                )
                validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
                validation.write_text(
                    validation.read_text(encoding="utf-8").replace(
                        '            Check(source.Contains("Product"));\n',
                        f"            if (source.Length >= 0) {exit_kind};\n"
                        '            Check(source.Contains("Product"));\n',
                    ),
                    encoding="utf-8",
                )
                code, output = self._run_fixture_gate(root)
                self.assertEqual(1, code, output)
                self.assertIn('field": "reachability"', output)
