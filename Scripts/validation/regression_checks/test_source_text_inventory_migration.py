"""Regression tests for trusted parser migration compatibility."""

import io
import json
import re
import subprocess
import sys
import tarfile
import tempfile
import textwrap
import unittest
from pathlib import Path

from Scripts.validation.source_text_inventory import (
    inventory,
    _legacy_validation_harness_signature,
    _normalize_csharp_expression,
)
from Scripts.validation.source_text_inventory_report import write_report
from Scripts.validation.source_text_inventory_ratchet import (
    _classify_change,
    _checker_digest,
    _inventory_fingerprint,
    _parser_semantic_changes,
)
from .test_source_text_inventory_semantics import _FixtureSupport


class SourceTextInventoryMigrationTests(_FixtureSupport, unittest.TestCase):
    """Verify additive legacy metadata and reject parser semantic rewrites."""

    def test_same_file_termination_helper_call_requires_ack(self):
        """Reject direct and transitive exit helpers while allowing unrelated calls."""
        scratch = Path(__file__).resolve().parents[3] / "build/tmp/scratch"
        scratch.mkdir(parents=True, exist_ok=True)
        exit_helper = "private static void ExitSuccessfully() { System.Environment.Exit(0); }\n"
        cases = (
            ("direct", "ExitSuccessfully();", exit_helper, 1),
            ("expression", "ExitSuccessfully();", "private static void ExitSuccessfully() => System.Environment.Exit(0);\n", 1),
            ("transitive", "ExitSuccessfully();", "private static void ExitSuccessfully() { Finish(); }\n"
             + "private static void Finish() { System.Environment.Exit(0); }\n", 1),
            ("unrelated", "Log();", exit_helper + 'private static void Log() { System.Console.WriteLine("log"); }\n', 0),
        )
        for name, invocation, helpers, expected in cases:
            with self.subTest(name=name), tempfile.TemporaryDirectory(dir=scratch) as directory:
                assertion = 'Check(source.Contains("Product"));'
                root = self._git_fixture(directory, validation_text=(
                    "public static class FixtureValidation { public static void Validate() {\n"
                    'var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                    + assertion + "\n}\n" + helpers
                    + "private static string ReadRequiredRepoText(string path) => System.IO.File.ReadAllText(path);\n"
                    + "private static void Check(bool ok) { if (!ok) throw new System.Exception(); }\n}\n"
                ))
                baseline = self._run_migration_cli(root)
                self.assertEqual(0, baseline.returncode, baseline.stdout + baseline.stderr)
                self.assertIn("SOURCE_TEXT_INVENTORY_OK rows=1 ", baseline.stdout)
                validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
                validation.write_text(validation.read_text(encoding="utf-8").replace(
                    assertion, invocation + " " + assertion
                ), encoding="utf-8")
                if expected:
                    (root / "Packages/fixture/Runtime/Product.cs").write_text("public sealed class Other {}\n", encoding="utf-8")
                result = self._run_migration_cli(root)
                self.assertEqual(expected, result.returncode, result.stdout + result.stderr)
                if expected:
                    self.assertIn("SOURCE_TEXT_INVENTORY_UNACKNOWLEDGED", result.stdout)

    def test_complete_inventory_hash_is_independent_of_row_order(self):
        """Preserve all values and duplicate rows while normalizing platform sorting."""
        rows = [{"file": "a.cs", "line": 1, "token": "first"}, {"file": "B.cs", "line": 2, "token": "second"}]
        self.assertEqual(_inventory_fingerprint(rows), _inventory_fingerprint(list(reversed(rows))))
        self.assertNotEqual(_inventory_fingerprint(rows), _inventory_fingerprint(rows + [rows[0]]))
        self.assertNotEqual(
            _inventory_fingerprint(rows), _inventory_fingerprint([{**rows[0], "token": "changed"}, rows[1]])
        )

    def test_expression_normalization_preserves_literal_values(self):
        """Ignore external comments and retain semantic whitespace inside C# literals."""
        self.assertEqual(
            _normalize_csharp_expression("bool seed = true;"),
            _normalize_csharp_expression("bool seed /* unchanged */ = true;"),
        )
        for before, after in (
            ('"two  spaces"', '"two spaces"'),
            ('"/* first */"', '"/* second */"'),
            ('@"a""  b"', '@"a"" b"'),
        ):
            with self.subTest(literal=before):
                self.assertNotEqual(_normalize_csharp_expression(before), _normalize_csharp_expression(after))

    def test_inline_assertion_operands_retain_local_dependencies(self):
        """Reject bypass operands and aliases while allowing unrelated local checks."""
        scratch = Path(__file__).resolve().parents[3] / "build/tmp/scratch"
        scratch.mkdir(parents=True, exist_ok=True)
        cases = (
            ("direct", "bool bypass = false;", 'Check(bypass || source.Contains("Product"));',
             "bool bypass = false;", "bool bypass = true;", 1),
            ("alias", "bool seed = false; bool bypass = seed;", 'Check(bypass || source.Contains("Product"));',
             "bool seed = false;", "bool seed = true;", 1),
            ("unrelated-local", "bool unused = false;", 'Check(source.Contains("Product"));',
             "bool unused = false;", "bool unused = true;", 0),
            ("unrelated-check", "bool unrelated = false;", 'Check(unrelated); Check(source.Contains("Product"));',
             "bool unrelated = false;", "bool unrelated = true;", 0),
            ("added-check", "", 'Check(source.Contains("Product"));',
             'Check(source.Contains("Product"));', 'Check(true); Check(source.Contains("Product"));', 0),
        )
        for name, declarations, checks, before, after, expected in cases:
            with self.subTest(name=name), tempfile.TemporaryDirectory(dir=scratch) as directory:
                root = self._git_fixture(
                    directory,
                    validation_text=(
                        "public static class FixtureValidation { public static void Validate() {\n"
                        'var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                        + declarations + "\n" + checks + "\n} }\n"
                    ),
                )
                self.assertEqual(1, len(inventory(root)))
                baseline = self._run_migration_cli(root)
                self.assertEqual(0, baseline.returncode, baseline.stdout + baseline.stderr)
                validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
                validation.write_text(
                    validation.read_text(encoding="utf-8").replace(before, after), encoding="utf-8"
                )
                if expected:
                    (root / "Packages/fixture/Runtime/Product.cs").write_text(
                        "public sealed class Other {}\n", encoding="utf-8"
                    )
                result = self._run_migration_cli(root)
                self.assertEqual(expected, result.returncode, result.stdout + result.stderr)
                if expected:
                    self.assertIn('"field": "assertion_signature"', result.stdout)

    def test_byref_invocations_retain_assertion_dependencies(self):
        """Bind ref/out writes and invocation inputs without binding unrelated checks."""
        scratch = Path(__file__).resolve().parents[3] / "build/tmp/scratch"
        scratch.mkdir(parents=True, exist_ok=True)
        checked = 'Check(bypass || source.Contains("Product"));'
        direct = 'Check(source.Contains("Product"));'
        cases = (
            ("out", 'bool bypass = false; bool.TryParse("false", out bypass);',
             '"false"', '"true"', checked, 1),
            ("ref", "bool bypass = false; System.Threading.Volatile.Write(ref bypass, false);",
             "ref bypass, false", "ref bypass, true", checked, 1),
            ("out-var", 'bool.TryParse("false", out var bypass);',
             '"false"', '"true"', checked, 1),
            ("alias", 'string seed = "false"; string input = seed; bool bypass = false; '
             'bool.TryParse(input, out bypass);', 'seed = "false"', 'seed = "true"', checked, 1),
            ("guard", 'bool enabled = false; bool bypass = false; '
             'if (enabled) bool.TryParse("true", out bypass);', "enabled = false", "enabled = true", checked, 1),
            ("unrelated-out", 'bool unused = false; bool.TryParse("false", out unused);',
             '"false"', '"true"', direct, 0),
            ("unrelated-ref", "bool unused = false; System.Threading.Volatile.Write(ref unused, false);",
             "ref unused, false", "ref unused, true", direct, 0),
            ("unrelated-check", 'bool unused = false; bool.TryParse("false", out unused);',
             '"false"', '"true"', "Check(unused); " + direct, 0),
        )
        for name, prelude, before, after, checks, expected in cases:
            with self.subTest(name=name), tempfile.TemporaryDirectory(dir=scratch) as directory:
                root = self._git_fixture(
                    directory,
                    validation_text=(
                        "public static class FixtureValidation { public static void Validate() {\n"
                        'var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                        + prelude + "\n" + checks + "\n} }\n"
                    ),
                )
                self.assertEqual(1, len(inventory(root)))
                baseline = self._run_migration_cli(root)
                self.assertEqual(0, baseline.returncode, baseline.stdout + baseline.stderr)
                validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
                validation.write_text(
                    validation.read_text(encoding="utf-8").replace(before, after), encoding="utf-8"
                )
                if expected:
                    (root / "Packages/fixture/Runtime/Product.cs").write_text(
                        "public sealed class Other {}\n", encoding="utf-8"
                    )
                result = self._run_migration_cli(root)
                self.assertEqual(expected, result.returncode, result.stdout + result.stderr)
                if expected:
                    self.assertIn('"field": "assertion_signature"', result.stdout)

    def test_external_helper_dependencies_remain_bound(self):
        """Bind external helper chains while allowing unrelated methods and comments."""
        scratch = Path(__file__).resolve().parents[3] / "build/tmp/scratch"
        scratch.mkdir(parents=True, exist_ok=True)
        cases = (
            ("instance", "reader", "Check", "Sources.cs", "System.IO.File.ReadAllText(path)", '"Product"', 1),
            ("direct", "Sources", "Check", "Sources.cs", "System.IO.File.ReadAllText(path)", '"Product"', 1),
            ("initializer", "Sources", "Check", "State.cs", "Spoof = false", "Spoof = true", 1),
            ("chain", "Sources", "Check", "Leaf.cs", "System.IO.File.ReadAllText(path)", '"Product"', 1),
            ("static", "", "Check", "Sources.cs", "System.IO.File.ReadAllText(path)", '"Product"', 1),
            ("assertion", "Sources", "SharedChecks.Check", "SharedChecks.cs", "if (!ok) throw new System.Exception();", "", 1),
            ("unrelated-method", "Sources", "Check", "Sources.cs", "=> 1;", "=> 2;", 0),
            ("other-namespace", "Sources", "Check", "Other.cs", "System.IO.File.ReadAllText(path)", '"Product"', 0),
            ("unrelated-check", "Sources", "Check", "Noise.cs", "throw new System.Exception();", "return;", 0),
            ("comment", "Sources", "Check", "Sources.cs", "System.IO.File.ReadAllText(path)",
             "/* unchanged reader */ System.IO.File.ReadAllText(path)", 0),
        )
        for name, reader, check, changed_file, before, after, expected in cases:
            with self.subTest(name=name), tempfile.TemporaryDirectory(dir=scratch) as directory:
                imports = "using Helpers;" if reader else "using static Helpers.Sources;"
                reader_call = (reader + "." if reader else "") + "ReadRequiredRepoText"
                declarations = "var reader = new Sources();\n" if name == "instance" else ""
                root = self._git_fixture(directory, validation_text=(
                    imports + "\nnamespace Checks { public static class FixtureValidation { public static void Validate() {\n"
                    + declarations
                    + f'var source = {reader_call}("Packages/fixture/Runtime/Product.cs");\n'
                    f'{check}(source.Contains("Product"));\n}} }} }}\n'
                ))
                shared = root / "Packages/fixture/Tests/Runtime/Shared"
                shared.mkdir()
                read_body = "Middle.Load(path)" if name == "chain" else "System.IO.File.ReadAllText(path)"
                if name == "initializer":
                    read_body = 'Spoof ? "Product" : System.IO.File.ReadAllText(path)'
                helpers = {
                    "Sources.cs": "namespace Helpers { public static partial class Sources { "
                    + "public static string ReadRequiredRepoText(string path) => " + read_body
                    + "; public static int Unrelated() => 1; } }\n",
                    "State.cs": "namespace Helpers { public static partial class Sources { "
                    "private const bool Spoof = false; } }\n",
                    "Middle.cs": "namespace Helpers { public static class Middle { "
                    "public static string Load(string path) => Leaf.Read(path); } }\n",
                    "Leaf.cs": "namespace Helpers { public static class Leaf { "
                    "public static string Read(string path) => System.IO.File.ReadAllText(path); } }\n",
                    "SharedChecks.cs": "namespace Helpers { public static class SharedChecks { "
                    "public static void Check(bool ok) { Ensure(ok); } "
                    "private static void Ensure(bool ok) { if (!ok) throw new System.Exception(); } } }\n",
                    "Other.cs": "namespace Other { public static class Sources { "
                    "public static string ReadRequiredRepoText(string path) => System.IO.File.ReadAllText(path); } }\n",
                    "Noise.cs": "namespace Helpers { public static class Noise { "
                    "public static void Check(bool ok) { throw new System.Exception(); } } }\n",
                }
                for filename, source in helpers.items():
                    if name == "instance":
                        source = source.replace("static partial class Sources", "partial class Sources")
                        source = source.replace("static string ReadRequiredRepoText", "string ReadRequiredRepoText")
                    (shared / filename).write_text(source, encoding="utf-8")
                subprocess.run(["git", "-C", str(root), "add", str(shared)], check=True)
                subprocess.run(["git", "-C", str(root), "commit", "-qm", "external-helper-base"], check=True)
                self.assertEqual(1, len(inventory(root)))
                baseline = self._run_migration_cli(root)
                self.assertEqual(0, baseline.returncode, baseline.stdout + baseline.stderr)
                changed = shared / changed_file
                changed.write_text(changed.read_text(encoding="utf-8").replace(before, after), encoding="utf-8")
                if expected:
                    (root / "Packages/fixture/Runtime/Product.cs").write_text(
                        "public sealed class Other {}\n", encoding="utf-8"
                    )
                result = self._run_migration_cli(root)
                self.assertEqual(expected, result.returncode, result.stdout + result.stderr)
                if expected:
                    self.assertIn('"field": "harness_signature"', result.stdout)

    def test_inline_guard_definition_and_goto_destination_remain_bound(self):
        """Reject changed guard values and goto targets before an inline assertion."""
        for before, after in (
            (
                "bool skip = source.Length < 0; if (skip) return;",
                "bool skip = source.Length >= 0; if (skip) return;",
            ),
            ("goto CheckLabel;", "goto EndLabel;"),
        ):
            with self.subTest(change=after), tempfile.TemporaryDirectory() as directory:
                root = self._git_fixture(
                    directory,
                    validation_text=(
                        "public static class FixtureValidation { public static void Validate() {\n"
                        'var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                        + before
                        + '\nCheckLabel: Check(source.Contains("Product"));\nEndLabel: return;\n} }\n'
                    ),
                )
                validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
                validation.write_text(
                    validation.read_text(encoding="utf-8").replace(before, after), encoding="utf-8"
                )
                result = self._run_migration_cli(root)
                self.assertEqual(1, result.returncode, result.stdout + result.stderr)

    def test_workflow_hashes_checker_without_trusting_checker_code(self):
        """Detect checker mutations even when the checker forges its own digest."""
        workflow = Path(__file__).resolve().parents[3] / ".github/workflows/dotnet-tests.yml"
        workflow_text = workflow.read_text(encoding="utf-8")
        self.assertIn(
            "--trusted-base-inventory build/review/source-text-inventory-trusted",
            workflow_text,
        )
        self.assertIn("build/review/source-text-inventory-trusted/", workflow_text)
        match = re.search(
            r"<<'MIGRATION_CHECKER'\n(.*?)\n\s*MIGRATION_CHECKER", workflow_text, re.S
        )
        self.assertIsNotNone(match, "Workflow must independently hash the proposed checker")
        program = textwrap.dedent(match.group(1))
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(directory)
            staged = root / "build/reviewed-checker"
            expected = _checker_digest(root)
            result = subprocess.run(
                [sys.executable, "-I", "-B", "-c", program, str(staged)],
                cwd=root,
                capture_output=True,
                text=True,
                check=True,
            )
            self.assertEqual(expected, result.stdout.strip())
            checker = staged / "source_text_inventory.py"
            self.assertTrue(checker.is_file(), "Only hashed modules may enter the execution directory")
            (root / "Scripts/validation/dataclasses.py").write_text("raise SystemExit(0)\n", encoding="utf-8")
            result = subprocess.run(
                [sys.executable, "-I", "-B", str(checker), "--help"],
                cwd=root,
                capture_output=True,
                text=True,
                check=True,
            )
            self.assertIn("usage:", result.stdout)
            write_report(inventory(root), root / "build/imported")
            subprocess.run(
                [
                    sys.executable,
                    "-I",
                    "-B",
                    str(checker),
                    "--root",
                    str(root),
                    "--output",
                    str(root / "build/staged"),
                ],
                cwd=root,
                capture_output=True,
                text=True,
                check=True,
            )
            self.assertEqual(
                (root / "build/imported/source_text_inventory.csv").read_bytes(),
                (root / "build/staged/source_text_inventory.csv").read_bytes(),
            )
            ratchet = root / "Scripts/validation/source_text_inventory_ratchet.py"
            ratchet.write_text(
                ratchet.read_text(encoding="utf-8")
                + f'\n\ndef _checker_digest(*args):\n    return "{expected}"\n',
                encoding="utf-8",
            )
            result = subprocess.run(
                [sys.executable, "-I", "-B", "-c", program, str(staged)],
                cwd=root,
                capture_output=True,
                text=True,
                check=True,
            )
            self.assertNotEqual(expected, result.stdout.strip())

    def test_migration_approval_requires_a_base_revision(self):
        """Reject an approval flag that would otherwise be silently ignored."""
        checker = Path(__file__).resolve().parents[1] / "source_text_inventory.py"
        with tempfile.TemporaryDirectory() as directory:
            result = subprocess.run(
                [
                    sys.executable,
                    "-B",
                    str(checker),
                    "--root",
                    directory,
                    "--output",
                    str(Path(directory) / "build"),
                    "--parser-migration-approval",
                    "missing.json",
                ],
                capture_output=True,
                text=True,
                check=False,
            )
        self.assertEqual(2, result.returncode, result.stdout + result.stderr)
        self.assertIn("--parser-migration-approval requires --base-ref", result.stderr)

    def _run_migration_cli(self, root, approval=None):
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
        return subprocess.run(args, capture_output=True, text=True, check=False)

    def test_established_parser_version_cannot_be_emptied(self):
        """Reject loss of a version instead of resetting the legacy migration state."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(directory)
            checker = root / "Scripts/validation/source_text_inventory.py"
            checker.write_text(
                checker.read_text(encoding="utf-8").replace(
                    'PARSER_SEMANTICS_VERSION = "2"', 'PARSER_SEMANTICS_VERSION = ""'
                ),
                encoding="utf-8",
            )
            result = self._run_migration_cli(root)
            self.assertEqual(1, result.returncode, result.stdout + result.stderr)
            self.assertIn('"field": "parser_migration"', result.stdout)

    def test_empty_or_mixed_versions_do_not_gain_legacy_status(self):
        """Reserve the legacy transition for historical rows without a version field."""
        with tempfile.TemporaryDirectory() as directory:
            row = inventory(self._git_fixture(directory))[0]
            for base, current in (
                ([{**row, "parser_version": ""}], [{**row, "parser_version": "3"}]),
                (
                    [{key: value for key, value in row.items() if key != "parser_version"}],
                    [{**row, "parser_version": "2"}, {**row, "parser_version": "3"}],
                ),
            ):
                with self.subTest(versions=[item.get("parser_version") for item in current]):
                    changes, _ = _parser_semantic_changes(base, current)
                    self.assertIn("parser_migration", {item["field"] for item in changes})

    def test_effective_reader_reassignment_requires_acknowledgement(self):
        """Reject injected literals that replace the source consumed by an assertion."""
        for before, after in (
            (
                'source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");',
                'source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs") + "Product";',
            ),
            ('source += "Benign";', 'source += "Product";'),
        ):
            with self.subTest(assignment=after), tempfile.TemporaryDirectory() as directory:
                root = self._git_fixture(
                    directory,
                    validation_text=(
                        "public static class FixtureValidation {\n"
                        "    public static void Validate() {\n"
                        '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                        f"        {before}\n"
                        '        Check(source.Contains("Product"));\n'
                        "    }\n}\n"
                    ),
                )
                validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
                validation.write_text(
                    validation.read_text(encoding="utf-8").replace(before, after), encoding="utf-8"
                )
                (root / "Packages/fixture/Runtime/Product.cs").write_text(
                    "public sealed class Benign {}\n", encoding="utf-8"
                )
                result = self._run_migration_cli(root)
                self.assertEqual(1, result.returncode, result.stdout + result.stderr)
                self.assertIn('"field": "reader_signature"', result.stdout)

    def test_conditional_reader_assignment_retains_initializer(self):
        """Reject injected initializer literals despite a later conditional read."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(
                directory,
                validation_text=(
                    "public static class FixtureValidation { public static void Validate() {\n"
                    'var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                    'if (System.Environment.TickCount == int.MinValue) source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                    'Check(source.Contains("Product"));\n} }\n'
                ),
            )
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            text = validation.read_text(encoding="utf-8")
            validation.write_text(
                text.replace('Product.cs");', 'Product.cs") + "Product";', 1), encoding="utf-8"
            )
            (root / "Packages/fixture/Runtime/Product.cs").write_text(
                "public sealed class Other {}\n", encoding="utf-8"
            )
            result = self._run_migration_cli(root)
            self.assertEqual(1, result.returncode, result.stdout + result.stderr)
            self.assertIn('"field": "reader_signature"', result.stdout)

    def test_upstream_reader_alias_definition_remains_bound(self):
        """Reject an injected source literal before one or more reader aliases."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(directory, validation_text=(
                "public static class FixtureValidation { public static void Validate() {\n"
                'var input = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                'var intermediate = input;\nvar source = intermediate;\nCheck(source.Contains("Product"));\n} }\n'
            ))
            self.assertEqual(1, len(inventory(root)))
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(validation.read_text(encoding="utf-8").replace('Product.cs");', 'Product.cs") + "Product";'), encoding="utf-8")
            (root / "Packages/fixture/Runtime/Product.cs").write_text("public sealed class Other {}\n", encoding="utf-8")
            result = self._run_migration_cli(root)
            self.assertEqual(1, result.returncode, result.stdout + result.stderr)
            self.assertIn('"field": "reader_signature"', result.stdout)

    def test_delayed_consumer_retains_actual_reachability(self):
        """Reject an exit inserted between a source predicate and its consumer."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(directory, validation_text=(
                "public static class FixtureValidation { public static void Validate() {\n"
                'var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                'bool ok = source.Contains("Product");\nCheck(ok);\n} }\n'
            ))
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(validation.read_text(encoding="utf-8").replace("Check(ok);", "if (source.Length >= 0) return;\nCheck(ok);"), encoding="utf-8")
            result = self._run_migration_cli(root)
            self.assertEqual(1, result.returncode, result.stdout + result.stderr)
            self.assertIn('"field": "assertion_signature"', result.stdout)

    def test_separate_assertion_result_remains_bound_to_its_consumer(self):
        """Reject bypassed direct and forwarded predicate consumers without blocking additions."""
        for declarations, before, after, expected in (
            ("", "Check(ok);", "Check(true);", 1),
            ("bool forwarded = ok;\n", "Check(forwarded);", "Check(true);", 1),
            ("", "Check(ok);", "Check(ok); Check(true);", 0),
            ("", "Check(ok);", "{ Check(true); }\nCheck(ok);", 0),
            ("", "Check(ok);", "{ Check(true); }\n // unchanged consumer\n    Check(ok);", 0),
            ("", "Check(ok);", "// unchanged consumer\nCheck(ok);", 0),
            (
                "",
                'Check(ok || "two  spaces" == "two spaces");',
                'Check(ok || "two spaces" == "two spaces");',
                1,
            ),
            (
                "",
                "bool enabled = true; if (enabled) Check(ok);",
                "bool enabled = false; if (enabled) Check(ok);",
                1,
            ),
        ):
            with self.subTest(consumer=after), tempfile.TemporaryDirectory() as directory:
                root = self._git_fixture(
                    directory,
                    validation_text=(
                        "public static class FixtureValidation { public static void Validate() {\n"
                        'var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                        'bool ok = source.Contains("Product");\n' + declarations + before + "\n} }\n"
                    ),
                )
                validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
                validation.write_text(
                    validation.read_text(encoding="utf-8").replace(before, after), encoding="utf-8"
                )
                result = self._run_migration_cli(root)
                self.assertEqual(expected, result.returncode, result.stdout + result.stderr)
                if expected:
                    self.assertIn('"field": "assertion_signature"', result.stdout)

    def test_workflow_approval_cannot_authorize_schema_or_row_loss(self):
        """Preserve schema and row invariants even with exact reviewed fingerprints."""
        for mutation, expected in (
            ("    for row in rows:\n        row.pop('call_path')\n", "parser_field_missing"),
            ("    rows.clear()\n", "parser_row_removed"),
        ):
            with self.subTest(expected=expected), tempfile.TemporaryDirectory() as directory:
                root = self._git_fixture(directory)
                checker = root / "Scripts/validation/source_text_inventory.py"
                original = checker.read_text(encoding="utf-8")
                legacy = (
                    "\n_original_inventory = inventory\ndef inventory(root):\n"
                    "    rows = _original_inventory(root)\n"
                    "    for row in rows:\n        row.pop('parser_version')\n"
                    "    return rows\n"
                )
                checker.write_text(
                    original.replace('\nif __name__ == "__main__":', legacy + '\nif __name__ == "__main__":'),
                    encoding="utf-8",
                )
                subprocess.run(["git", "-C", str(root), "add", "-A"], check=True)
                subprocess.run(["git", "-C", str(root), "commit", "-qm", "legacy-base"], check=True)
                wrapper = (
                    "\n_original_inventory = inventory\ndef inventory(root):\n    rows = _original_inventory(root)\n"
                    + mutation
                    + "    return rows\n"
                )
                checker.write_text(
                    original.replace(
                        '\nif __name__ == "__main__":', wrapper + '\nif __name__ == "__main__":'
                    ),
                    encoding="utf-8",
                )
                self._run_migration_cli(root)
                evidence = json.loads(
                    (root / "build/inventory/parser_migration_evidence.json").read_text(encoding="utf-8")
                )
                approval = root / "build/approval.json"
                approval.write_text(
                    json.dumps({**evidence["fingerprints"], "reason": "Exact fixture review."}),
                    encoding="utf-8",
                )
                result = self._run_migration_cli(root, approval)
                self.assertEqual(1, result.returncode, result.stdout + result.stderr)
                self.assertIn(f'"field": "{expected}"', result.stdout)

    def test_workflow_approval_is_rejected_after_legacy_migration(self):
        """Require trusted-base acknowledgements for later versioned parser changes."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(directory)
            checker = root / "Scripts/validation/source_text_inventory.py"
            checker.write_text(checker.read_text(encoding="utf-8") + "\n", encoding="utf-8")
            self._run_migration_cli(root)
            evidence = json.loads(
                (root / "build/inventory/parser_migration_evidence.json").read_text(encoding="utf-8")
            )
            approval = root / "build/approval.json"
            approval.write_text(
                json.dumps({**evidence["fingerprints"], "reason": "Exact fixture review."}), encoding="utf-8"
            )
            result = self._run_migration_cli(root, approval)
            self.assertEqual(1, result.returncode, result.stdout + result.stderr)
            self.assertIn("not an approved parser migration", result.stdout)

    def test_exact_workflow_migration_approval_preserves_other_gates(self):
        """Bind migration approval to the base, checker, and both complete inventories."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(directory)
            checker = root / "Scripts/validation/source_text_inventory.py"
            original = checker.read_text(encoding="utf-8")
            legacy = (
                "\n_original_inventory = inventory\n"
                "def inventory(root):\n"
                "    rows = _original_inventory(root)\n"
                "    for row in rows:\n"
                "        row.pop('parser_version', None)\n"
                "        row['harness_signature'] = row['legacy_harness_signature']\n"
                "    return rows\n"
            )
            checker.write_text(
                original.replace('\nif __name__ == "__main__":', legacy + '\nif __name__ == "__main__":'),
                encoding="utf-8",
            )
            subprocess.run(["git", "-C", str(root), "add", "-A"], check=True)
            subprocess.run(["git", "-C", str(root), "commit", "-qm", "legacy-migration-base"], check=True)
            checker.write_text(original, encoding="utf-8")
            result = self._run_migration_cli(root)
            self.assertEqual(1, result.returncode, result.stdout + result.stderr)
            evidence_path = root / "build/inventory/parser_migration_evidence.json"
            self.assertTrue(evidence_path.is_file(), "Migration fingerprints must be reviewable")
            evidence = json.loads(evidence_path.read_text(encoding="utf-8"))
            approved = {**evidence["fingerprints"], "reason": "Reviewed exact legacy parser migration."}
            approval = root / "build/workflow-approval.json"
            approval.write_text(json.dumps(approved), encoding="utf-8")
            result = self._run_migration_cli(root, approval)
            self.assertEqual(0, result.returncode, result.stdout + result.stderr)
            self.assertIn("PARSER_MIGRATION_APPROVED", result.stdout)
            self.assertIn(" acknowledged=0 unacknowledged=0", result.stdout)
            for field in approved:
                with self.subTest(field=field):
                    approval.write_text(
                        json.dumps({**approved, field: "" if field == "reason" else "*"}), encoding="utf-8"
                    )
                    result = self._run_migration_cli(root, approval)
                    self.assertEqual(1, result.returncode, result.stdout + result.stderr)
                    self.assertIn("PARSER_MIGRATION_REJECTED", result.stdout)
            approval.write_text(json.dumps(approved), encoding="utf-8")
            product = root / "Packages/fixture/Runtime/Product.cs"
            product.write_text("// Product\npublic sealed class Other {}\n", encoding="utf-8")
            result = self._run_migration_cli(root, approval)
            self.assertEqual(1, result.returncode, result.stdout + result.stderr)
            self.assertIn('"field": "comment_only"', result.stdout)
            product.write_text("public sealed class Product {}\n", encoding="utf-8")
            flow = root / "Scripts/validation/source_text_inventory_flow.py"
            flow.write_text(
                flow.read_text(encoding="utf-8")
                + '\n\ndef _validation_harness_signature(*args):\n    return "0" * 64\n',
                encoding="utf-8",
            )
            result = self._run_migration_cli(root, approval)
            self.assertEqual(1, result.returncode, result.stdout + result.stderr)
            self.assertIn("PARSER_MIGRATION_REJECTED", result.stdout)

    def test_legacy_harness_parser_cannot_return_a_constant(self):
        """Reject lost harness sensitivity even when a compatibility hash is unchanged."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(
                directory,
                validation_text=(
                    "public static class FixtureValidation {\n"
                    "    private static void Check(bool ok) { if (!ok) throw new System.Exception(); }\n"
                    "    public static void Validate() {\n"
                    '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                    '        Check(source.Contains("Product"));\n'
                    "    }\n}\n"
                ),
            )
            checker = root / "Scripts/validation/source_text_inventory.py"
            original = checker.read_text(encoding="utf-8")
            legacy = (
                "\n_original_inventory = inventory\n"
                "def inventory(root):\n"
                "    rows = _original_inventory(root)\n"
                "    for row in rows:\n"
                "        row.pop('parser_version', None)\n"
                "        row['harness_signature'] = row['legacy_harness_signature']\n"
                "    return rows\n"
            )
            checker.write_text(
                original.replace('\nif __name__ == "__main__":', legacy + '\nif __name__ == "__main__":'),
                encoding="utf-8",
            )
            subprocess.run(["git", "-C", str(root), "add", "-A"], check=True)
            subprocess.run(["git", "-C", str(root), "commit", "-qm", "legacy-harness"], check=True)
            checker.write_text(original, encoding="utf-8")
            flow = root / "Scripts/validation/source_text_inventory_flow.py"
            flow.write_text(
                flow.read_text(encoding="utf-8")
                + '\n\ndef _validation_harness_signature(*args):\n    return "0" * 64\n',
                encoding="utf-8",
            )
            result = subprocess.run(
                [
                    sys.executable,
                    "-B",
                    str(checker),
                    "--root",
                    str(root),
                    "--base-ref",
                    "HEAD",
                    "--output",
                    str(root / "build/inventory"),
                ],
                check=False,
                capture_output=True,
                text=True,
            )
            self.assertEqual(1, result.returncode, result.stdout + result.stderr)
            self.assertIn('"field": "parser_harness_signature"', result.stdout)

    def test_import_cli_archive_and_exec_module_produce_identical_rows(self):
        """Preserve inventory data through every supported loading mode."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(directory)
            expected = inventory(root)
            write_report(expected, root / "build/imported")
            checker = root / "Scripts/validation/source_text_inventory.py"
            command = [
                sys.executable,
                "-B",
                str(checker),
                "--root",
                str(root),
                "--output",
                str(root / "build/cli"),
            ]
            subprocess.run(command, check=True, capture_output=True)
            self.assertEqual(
                (root / "build/imported/source_text_inventory.csv").read_bytes(),
                (root / "build/cli/source_text_inventory.csv").read_bytes(),
            )
            runner = (
                "import importlib.util,json,sys; from pathlib import Path; "
                "p=Path(sys.argv[1]); spec=importlib.util.spec_from_file_location('probe_inventory',p); "
                "module=importlib.util.module_from_spec(spec); spec.loader.exec_module(module); "
                "print(json.dumps(module.inventory(Path(sys.argv[2]))))"
            )
            loaded = subprocess.run(
                [sys.executable, "-I", "-B", "-c", runner, str(checker), str(root)],
                check=True,
                capture_output=True,
                text=True,
            )
            self.assertEqual(expected, json.loads(loaded.stdout))
            archived = subprocess.run(
                ["git", "-C", str(root), "archive", "HEAD"],
                check=True,
                capture_output=True,
            )
            tree = root / "build/archive"
            tree.mkdir()
            with tarfile.open(fileobj=io.BytesIO(archived.stdout)) as archive:
                archive.extractall(tree, filter="data")
            command[2] = str(tree / "Scripts/validation/source_text_inventory.py")
            command[4] = str(tree)
            command[6] = str(tree / "build/cli")
            subprocess.run(command, check=True, capture_output=True)
            self.assertEqual(
                (root / "build/cli/source_text_inventory.csv").read_bytes(),
                (tree / "build/cli/source_text_inventory.csv").read_bytes(),
            )

    def test_legacy_parser_rewrites_fail_the_real_cli(self):
        """Reject non-empty metadata substitutions by the proposed parser at the CLI."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(
                directory,
                validation_text=(
                    "public static class FixtureValidation {\n"
                    "    public static void Validate() {\n"
                    '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                    '        if (source.Length > 0) { Check(source.Contains("Product")); }\n'
                    "    }\n}\n"
                ),
            )
            checker = root / "Scripts/validation/source_text_inventory.py"
            original = checker.read_text(encoding="utf-8")
            legacy = (
                "\n_original_inventory = inventory\n"
                "def inventory(root):\n"
                "    rows = _original_inventory(root)\n"
                "    for row in rows:\n"
                "        row.pop('parser_version', None)\n"
                "    return rows\n"
            )
            checker.write_text(
                original.replace('\nif __name__ == "__main__":', legacy + '\nif __name__ == "__main__":'),
                encoding="utf-8",
            )
            subprocess.run(["git", "-C", str(root), "add", "-A"], check=True)
            subprocess.run(["git", "-C", str(root), "commit", "-qm", "legacy-parser"], check=True)
            for field, value in (
                ("class", "UNRESOLVED"),
                ("call_path", "UNKNOWN"),
                ("reachability", "always"),
                ("reader_signature", "ReadBenign()"),
                ("checked_expression", "true"),
            ):
                with self.subTest(field=field):
                    wrapper = legacy.replace("row.pop('parser_version', None)", f"row[{field!r}] = {value!r}")
                    checker.write_text(
                        original.replace(
                            '\nif __name__ == "__main__":', wrapper + '\nif __name__ == "__main__":'
                        ),
                        encoding="utf-8",
                    )
                    result = subprocess.run(
                        [
                            sys.executable,
                            "-B",
                            str(checker),
                            "--root",
                            str(root),
                            "--base-ref",
                            "HEAD",
                            "--output",
                            str(root / "build/inventory"),
                        ],
                        capture_output=True,
                        text=True,
                        check=False,
                    )
                    self.assertEqual(1, result.returncode, result.stdout + result.stderr)
                    self.assertIn(f'"field": "parser_{field}"', result.stdout)

    def test_legacy_nonempty_semantic_rewrites_remain_weakening(self):
        """Preserve field-level failures for rewritten non-empty legacy metadata."""
        with tempfile.TemporaryDirectory() as directory:
            original = inventory(self._git_fixture(directory))[0]
            cases = (
                ("target", "Product.cs", "Benign.cs"),
                ("target", "Product.cs", "Product.cs:Benign.cs"),
                ("class", "C", "UNRESOLVED"),
                ("call_path", "Validate->Used", "UNKNOWN"),
                ("call_binding", "ambiguous:a", "ambiguous:b"),
                ("reachability", "if(enabled)", "if(false)"),
                ("reachability", "exit:return:!ok", "exit:return:ok"),
                ("checked_expression", 'source.Contains("Product")', "true"),
                ("reader_signature", "ReadProduct()", "ReadBenign()"),
                ("assertion_context", "checked", "bare"),
                ("assertion_literal", "Product", "Other"),
                ("assertion_polarity", "Check", "bare"),
                ("assertion_signature", "Check(source)", "source"),
                ("dispatch", "Ci|true|Validate", "Ci|false|Validate"),
                ("comment_only", False, True),
                ("xunit_coverage", "One;Two", "One"),
                ("harness_signature", "trusted-helper", "weaker-helper"),
            )
            for field, before, after in cases:
                with self.subTest(field=field, before=before, after=after):
                    base = {**original, field: before}
                    base.pop("parser_version")
                    current = {**original, field: after}
                    changes, _ = _parser_semantic_changes([base], [current])
                    self.assertIn(f"parser_{field}", {item["field"] for item in changes})

    def test_partial_legacy_signature_preserves_file_scope(self):
        """Keep compatibility hashes file-local while current helper hashes span partials."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(directory)
            path = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            path.write_text(
                path.read_text(encoding="utf-8").replace("static class", "static partial class"),
                encoding="utf-8",
            )
            sibling = path.with_name("FixtureValidation.Check.cs")
            sibling.write_text(
                "public static partial class FixtureValidation {\n"
                "    private static void Check(bool ok) { if (!ok) throw new System.Exception(); }\n"
                "}\n",
                encoding="utf-8",
            )
            row = inventory(root)[0]
            self.assertEqual(
                _legacy_validation_harness_signature(path.read_text(encoding="utf-8")),
                row["legacy_harness_signature"],
            )

    def test_new_opaque_harness_signature_requires_acknowledgement(self):
        """Require review when a new helper may shadow imported behavior."""
        base = {
            "file": "FixtureValidation.cs",
            "validation_flag": "--fixture",
            "label": "",
            "token": 'Check(source.Contains("Product"));',
            "harness_signature": "",
        }
        head = {**base, "harness_signature": "new-signature"}
        strengthened, weakening = _classify_change(base, head, ("harness_signature",))
        self.assertEqual(["harness_signature"], weakening)
        self.assertEqual([], strengthened)

    def test_legacy_unversioned_parser_migration_is_inventory_scoped(self):
        """Report new version metadata without acknowledging semantic weakening."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(directory)
            checker = root / "Scripts/validation/source_text_inventory.py"
            current = checker.read_text(encoding="utf-8")
            legacy_wrapper = (
                "\n_original_inventory = inventory\n"
                "def inventory(root):\n"
                "    rows = _original_inventory(root)\n"
                "    for row in rows:\n"
                "        row.pop('parser_version', None)\n"
                "    return rows\n"
            )
            checker.write_text(
                current.replace(
                    '\nif __name__ == "__main__":',
                    legacy_wrapper + '\nif __name__ == "__main__":',
                ),
                encoding="utf-8",
            )
            subprocess.run(["git", "-C", str(root), "add", "-A"], check=True)
            subprocess.run(["git", "-C", str(root), "commit", "-q", "-m", "legacy-parser"], check=True)
            checker.write_text(current, encoding="utf-8")
            code, output = self._run_fixture_gate(root)
            self.assertEqual(0, code, output)
            self.assertIn("PARSER_MIGRATION", output)
            self.assertIn("unacknowledged=0", output)
            self.assertIn(" acknowledged=0 ", output)
            (root / "Packages/fixture/Runtime/Benign.cs").write_text(
                "public sealed class Benign {}\n", encoding="utf-8"
            )
            (root / "Packages/fixture/Runtime/Product.cs").write_text(
                "public sealed class Other {}\n", encoding="utf-8"
            )
            text_module = root / "Scripts/validation/source_text_inventory_text.py"
            text_module.write_text(
                text_module.read_text(encoding="utf-8")
                + "\n\ndef _extract_argument_path(argument, paths):\n"
                + '    return "Packages/fixture/Runtime/Benign.cs"\n',
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

    def test_legacy_parser_migration_does_not_hide_signature_loss(self):
        """Keep a legacy parser signature loss as a parser weakening change."""
        base = {
            "file": "FixtureValidation.cs",
            "validation_flag": "--fixture",
            "label": "",
            "token": 'Check(source.Contains("Product"));',
            "harness_signature": "old-signature",
        }
        current = {**base, "parser_version": "2", "harness_signature": ""}
        changes, _ = _parser_semantic_changes([base], [current])
        self.assertIn("parser_harness_signature", {item["field"] for item in changes})

    def test_legacy_metadata_detail_requires_explicit_approval(self):
        """Keep even representation changes visible until exact migration approval."""
        with tempfile.TemporaryDirectory() as directory:
            original = inventory(self._git_fixture(directory))[0]
            for field, before, after in (
                ("target", "<directory>", "<directory>:Packages/x"),
                ("reachability", "exit:return", "exit:return:x"),
            ):
                with self.subTest(field=field):
                    base = {**original, field: before}
                    base.pop("parser_version")
                    changes, _ = _parser_semantic_changes([base], [{**original, field: after}])
                    self.assertIn(f"parser_{field}", {item["field"] for item in changes})
