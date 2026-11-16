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

































    def _git_fixture(self, directory: str, validation_text: str | None = None) -> Path:
        """Create a small Git fixture containing the current checker modules."""
        root = Path(directory)
        for source in (
            Path(__file__).resolve().parents[3].glob("Scripts/validation/source_text_inventory*.py")
        ):
            destination = root / source.relative_to(Path(__file__).resolve().parents[3])
            destination.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(source, destination)
        approvals = root / "Scripts/validation/source_text_ratchet_approvals.json"
        approvals.write_text('{"approvals": []}\n', encoding="utf-8")
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


class SourceTextRatchetTests(unittest.TestCase):
    """Verify count-ratchet behavior independently from parser semantics."""

    _git_fixture = SourceTextInventoryGateTests._git_fixture
    _run_fixture_gate = SourceTextInventoryGateTests._run_fixture_gate

    def test_new_class_c_row_in_existing_key_fails_without_ack(self):
        """Reject a new resolved implementation assertion in an existing key."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(directory)
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            original = validation.read_text(encoding="utf-8")
            validation.write_text(
                original.replace(
                    "    }\n}\n",
                    '        Check(source.Contains("Product"));\n    }\n}\n',
                ),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code, output)
            self.assertIn("SOURCE_TEXT_RATCHET_CLASS_C_INCREASE", output)

    def test_new_class_c_row_in_new_file_fails_without_ack(self):
        """Reject a resolved implementation assertion in a newly scanned file."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(directory)
            runtime = root / "Packages/fixture/Tests/Runtime"
            (runtime / "SecondValidation.cs").write_text(
                "public static class SecondValidation {\n"
                "    public static void Validate() {\n"
                '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                '        Check(source.Contains("Product"));\n'
                "    }\n}\n",
                encoding="utf-8",
            )
            registry = root / "Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs"
            registry.write_text(
                registry.read_text(encoding="utf-8")
                + 'Ci("--phase-second", "fixture", SecondValidation.Validate, evidence: ValidationEvidence.Behavior),\n',
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code, output)
            self.assertIn("SOURCE_TEXT_RATCHET_CLASS_C_INCREASE", output)

    def test_removed_class_c_row_passes_as_improved(self):
        """Allow removal of a Class C row and report an improvement."""
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
            self.assertEqual(0, code, output)
            self.assertIn("SOURCE_TEXT_RATCHET_IMPROVED class=C", output)

    def test_in_place_class_c_edit_passes_and_reports_changed_key(self):
        """Allow an in-place Class C edit while making the key visible to review."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(directory)
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace(
                    'source.Contains("Product")', 'source.Contains("Other")'
                ),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(0, code, output)
            self.assertIn("SOURCE_TEXT_RATCHET_CHANGED", output)

    def test_polarity_flip_fails_class_c_ratchet(self):
        """Reject a DoesNotContain-style boundary flipped into a positive check."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(
                directory,
                validation_text=(
                    "public static class FixtureValidation {\n"
                    "    public static void Validate() {\n"
                    '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                    '        Check(!source.Contains("Product"));\n'
                    "    }\n}\n"
                ),
            )
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace(
                    '!source.Contains("Product")', 'source.Contains("Product")'
                ),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code, output)
            self.assertIn("SOURCE_TEXT_RATCHET_CLASS_C_INCREASE", output)
            self.assertIn("SOURCE_TEXT_RATCHET_CLASS_A_DECREASE", output)

    def test_removed_class_a_requires_exact_trusted_approval(self):
        """Require an exact trusted-base approval when a Class A row is removed."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(
                directory,
                validation_text=(
                    "public static class FixtureValidation {\n"
                    "    public static void Validate() {\n"
                    '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                    '        Check(!source.Contains("Product"));\n'
                    "    }\n}\n"
                ),
            )
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            approvals = root / "Scripts/validation/source_text_ratchet_approvals.json"
            approvals.write_text(
                '{"approvals": [{"file": "Packages/fixture/Tests/Runtime/FixtureValidation.cs", '
                '"validation_flag": "--phase-fixture", "class": "A", "base_count": 1, '
                '"head_count": 0, "reason": "A behavior test replaces the removed absence boundary.", '
                '"replacement": "behavior-test"}]}\n',
                encoding="utf-8",
            )
            subprocess.run(["git", "-C", str(root), "add", "-A"], check=True)
            subprocess.run(["git", "-C", str(root), "commit", "-qm", "trusted-ratchet-approval"], check=True)
            validation.write_text(
                validation.read_text(encoding="utf-8").replace(
                    '        Check(!source.Contains("Product"));\n', ""
                ),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(0, code, output)
            self.assertIn("SOURCE_TEXT_RATCHET_APPROVED", output)

    def test_unknown_or_unresolved_increase_fails(self):
        """Reject increases in unresolved and unknown status totals."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(directory)
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace(
                    'Packages/fixture/Runtime/Product.cs', 'Packages/fixture/Runtime/Missing.cs'
                ),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code, output)
            self.assertIn("SOURCE_TEXT_RATCHET_UNRESOLVED_INCREASE", output)

    def test_invalid_base_revision_fails_closed(self):
        """Reject a missing base revision instead of scanning an implicit fallback."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(directory)
            output = io.StringIO()
            with contextlib.redirect_stdout(output):
                code = main(
                    [
                        "--root", str(root), "--base-ref", "missing-origin-main",
                        "--output", str(root / "build" / "inventory"),
                    ]
                )
            self.assertEqual(1, code, output.getvalue())
            self.assertIn("SOURCE_TEXT_RATCHET_BASE_ERROR", output.getvalue())

    def test_private_check_body_change_is_non_blocking_but_reported(self):
        """Report an emptied private Check helper without blocking the ratchet."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(directory)
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            text = validation.read_text(encoding="utf-8")
            text += "\nprivate static void Check(bool condition) { }\n"
            validation.write_text(text, encoding="utf-8")
            code, output = self._run_fixture_gate(root)
            self.assertEqual(0, code, output)
            self.assertIn("SOURCE_TEXT_RATCHET_HELPER_CHANGED", output)
            self.assertIn("helper=Check", output)

    def test_private_reader_body_change_is_non_blocking_but_reported(self):
        """Report a reader returning an empty string without blocking the ratchet."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(directory)
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            text = validation.read_text(encoding="utf-8")
            text += '\nprivate static string ReadRequiredRepoText(string path) { return ""; }\n'
            validation.write_text(text, encoding="utf-8")
            code, output = self._run_fixture_gate(root)
            self.assertEqual(0, code, output)
            self.assertIn("SOURCE_TEXT_RATCHET_HELPER_CHANGED", output)
            self.assertIn("helper=ReadRequiredRepoText", output)

    def test_ordinary_test_method_body_change_does_not_report_helper(self):
        """Ignore ordinary test method body edits in helper-change diagnostics."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(directory)
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            text = validation.read_text(encoding="utf-8").replace(
                "public static void Validate() {", "public static void Validate() {\n        var ordinary = 1;"
            )
            validation.write_text(text, encoding="utf-8")
            code, output = self._run_fixture_gate(root)
            self.assertEqual(0, code, output)
            self.assertNotIn("SOURCE_TEXT_RATCHET_HELPER_CHANGED", output)

    def test_delegate_rebinding_is_non_blocking_but_reported(self):
        """Report a delegate rebinding to a no-op without blocking the ratchet."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(
                directory,
                validation_text=(
                    "using System;\n"
                    "public static class FixtureValidation {\n"
                    "    private static void Real() { }\n"
                    "    private static void Noop() { }\n"
                    "    private static void Run(Action action) { action(); }\n"
                    "    public static void Validate() {\n"
                    '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                    "        Action callback = Real;\n"
                    "        Run(callback);\n"
                    '        Check(source.Contains("Product"));\n'
                    "    }\n}\n"
                ),
            )
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace(
                    "Action callback = Real;", "Action callback = Noop;"
                ),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(0, code, output)
            self.assertIn("SOURCE_TEXT_RATCHET_HELPER_CHANGED", output)
            self.assertIn("helper=binding:callback", output)

    def test_forged_class_and_path_rows_fail_closed(self):
        """Reject forged class labels and path aliases before comparing counts."""
        from Scripts.validation.source_text_inventory_ratchet import _validate_ratchet_rows

        errors = _validate_ratchet_rows(
            [
                {"file": "../Product.cs", "validation_flag": "--phase", "class": "c"},
                {"file": "Tests.cs", "validation_flag": "--phase", "class": " C "},
            ],
            "head",
        )
        self.assertEqual(2, len(errors))

    def test_path_aliases_share_one_count_key(self):
        """Normalize slash, dot, and case aliases before counting keys."""
        from Scripts.validation.source_text_inventory_ratchet import _ratchet_key

        first = _ratchet_key({"file": "./Packages/Fixture/Test.cs", "validation_flag": "--phase", "class": "C"})
        second = _ratchet_key({"file": "packages\\fixture\\test.cs", "validation_flag": "--phase", "class": "C"})
        self.assertEqual(first, second)

    def test_malformed_trusted_approval_fails_closed(self):
        """Reject extra approval fields even when no count would otherwise fail."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(directory)
            approval = root / "Scripts/validation/source_text_ratchet_approvals.json"
            approval.write_text(
                '{"approvals": [{"file": "x", "validation_flag": "--phase", "class": "A", '
                '"base_count": 1, "head_count": 0, "reason": "A sufficiently long approval reason.", '
                '"replacement": "behavior", "extra": true}]}\n',
                encoding="utf-8",
            )
            subprocess.run(["git", "-C", str(root), "add", "-A"], check=True)
            subprocess.run(["git", "-C", str(root), "commit", "-qm", "bad-approval"], check=True)
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace(
                    'source.Contains("Product")', 'source.Contains("Other")'
                ),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code, output)
            self.assertIn("SOURCE_TEXT_RATCHET_APPROVAL_ERROR", output)

    def test_negative_trusted_approval_counts_fail_closed(self):
        """Reject negative count approvals before they can authorize a weakening."""
        from Scripts.validation.source_text_inventory_ratchet import _load_ratchet_approvals

        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(directory)
            approval = root / "Scripts/validation/source_text_ratchet_approvals.json"
            approval.write_text(
                '{"approvals": [{"file": "x", "validation_flag": "--phase", "class": "A", '
                '"base_count": -1, "head_count": 0, "reason": "A sufficiently long approval reason.", '
                '"replacement": "behavior"}]}\n',
                encoding="utf-8",
            )
            approvals, errors = _load_ratchet_approvals(root)
            self.assertEqual([], approvals)
            self.assertEqual(["approval 0 has invalid counts"], errors)

    def test_live_file_with_missing_inventory_rows_fails_closed(self):
        """Reject a live assertion file that disappears from the head inventory."""
        from Scripts.validation.source_text_inventory_ratchet import _verify_count_ratchet

        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(directory)
            code = _verify_count_ratchet(
                root,
                [{"file": "Packages/fixture/Tests/Runtime/FixtureValidation.cs", "validation_flag": "--phase-fixture", "class": "C", "token": "x"}],
                [],
                "HEAD",
                [],
                [],
            )
            self.assertEqual(1, code)
