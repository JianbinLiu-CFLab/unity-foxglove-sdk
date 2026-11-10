"""Regression tests for trusted parser migration compatibility."""

import io
import json
import subprocess
import sys
import tarfile
import tempfile
import unittest

from Scripts.validation.source_text_inventory import inventory, _legacy_validation_harness_signature
from Scripts.validation.source_text_inventory_report import write_report
from Scripts.validation.source_text_inventory_ratchet import (
    _classify_change,
    _legacy_parser_change_is_additive,
    _parser_semantic_changes,
)
from .test_source_text_inventory_semantics import _FixtureSupport


class SourceTextInventoryMigrationTests(_FixtureSupport, unittest.TestCase):
    """Verify additive legacy metadata and reject parser semantic rewrites."""

    def test_import_cli_archive_and_exec_module_produce_identical_rows(self):
        """Preserve inventory data through every supported loading mode."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(directory)
            expected = inventory(root)
            write_report(expected, root / "build/imported")
            checker = root / "Scripts/validation/source_text_inventory.py"
            command = [sys.executable, "-B", str(checker), "--root", str(root),
                       "--output", str(root / "build/cli")]
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
                check=True, capture_output=True, text=True,
            )
            self.assertEqual(expected, json.loads(loaded.stdout))
            archived = subprocess.run(
                ["git", "-C", str(root), "archive", "HEAD"], check=True, capture_output=True,
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
                    wrapper = legacy.replace(
                        "row.pop('parser_version', None)", f"row[{field!r}] = {value!r}"
                    )
                    checker.write_text(
                        original.replace('\nif __name__ == "__main__":', wrapper + '\nif __name__ == "__main__":'),
                        encoding="utf-8",
                    )
                    result = subprocess.run(
                        [sys.executable, "-B", str(checker), "--root", str(root), "--base-ref", "HEAD",
                         "--output", str(root / "build/inventory")],
                        capture_output=True, text=True, check=False,
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

    def test_added_harness_sensitivity_is_strengthening(self):
        """Treat a newly populated harness signature as added parser sensitivity."""
        base = {
            "file": "FixtureValidation.cs",
            "validation_flag": "--fixture",
            "label": "",
            "token": 'Check(source.Contains("Product"));',
            "harness_signature": "",
        }
        head = {**base, "harness_signature": "new-signature"}
        strengthened, weakening = _classify_change(
            base, head, ("harness_signature",)
        )
        self.assertEqual([], weakening)
        self.assertEqual(["harness_signature"], [item["field"] for item in strengthened])

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
                + '\n\ndef _extract_argument_path(argument, paths):\n'
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

    def test_legacy_migration_only_accepts_additive_parser_values(self):
        """Reject non-empty target and class rewrites during legacy migration."""
        self.assertTrue(
            _legacy_parser_change_is_additive("target", "<directory>", "<directory>:Packages/x", {})
        )
        self.assertTrue(_legacy_parser_change_is_additive("reachability", "exit:return", "exit:return:x", {}))
        self.assertFalse(_legacy_parser_change_is_additive("target", "Product.cs", "Benign.cs", {}))
        self.assertFalse(_legacy_parser_change_is_additive("class", "C", "UNRESOLVED", {}))
