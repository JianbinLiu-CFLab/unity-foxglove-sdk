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
        """Reject removal of a Class C row instead of treating it as an improvement."""
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
            self.assertEqual(1, code, output)
            self.assertIn("SOURCE_TEXT_RATCHET_CLASS_C_DECREASE", output)

    def test_class_c_noop_substitution_fails_closed(self):
        """Reject replacing a Class C assertion with an unconditional truth value."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(directory)
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace(
                    'source.Contains("Product")', "true"
                ),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code, output)
            self.assertIn("SOURCE_TEXT_RATCHET_CLASS_C_DECREASE", output)

    def test_control_dependency_change_fails_closed(self):
        """Reject changing a local control dependency that can bypass an assertion."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(
                directory,
                validation_text=(
                    "public static class FixtureValidation {\n"
                    "    public static void Validate() {\n"
                    '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                    "        bool bypass = false;\n"
                    '        Check(bypass || source.Contains("Product"));\n'
                    "    }\n}\n"
                ),
            )
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace(
                    "bool bypass = false", "bool bypass = true"
                ),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code, output)
            self.assertIn("SOURCE_TEXT_RATCHET_ASSERTION_CHANGED", output)

    def test_dispatch_change_fails_closed(self):
        """Reject moving a validation out of the default dispatch lane."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(directory)
            registry = root / "Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs"
            registry.write_text(
                registry.read_text(encoding="utf-8").replace(
                    "evidence: ValidationEvidence.Behavior",
                    "includeInDefault: false, evidence: ValidationEvidence.Behavior",
                ),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code, output)
            self.assertIn("SOURCE_TEXT_RATCHET_ASSERTION_CHANGED", output)

    def test_in_place_class_c_edit_passes_and_reports_changed_key(self):
        """Reject an in-place Class C assertion edit while reporting its changed key."""
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
            self.assertEqual(1, code, output)
            self.assertIn("SOURCE_TEXT_RATCHET_CHANGED", output)
            self.assertIn("SOURCE_TEXT_RATCHET_ASSERTION_CHANGED", output)

    def test_unreachable_assertion_fails_closed(self):
        """Reject an assertion moved into an unreachable constant branch."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(directory)
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace(
                    '        Check(source.Contains("Product"));\n',
                    '        if (false) { Check(source.Contains("Product")); }\n',
                ),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code, output)
            self.assertIn("SOURCE_TEXT_RATCHET_ASSERTION_CHANGED", output)

    def test_swallowed_exception_assertion_fails_closed(self):
        """Reject an assertion moved into a swallowing exception handler."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(directory)
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace(
                    '        Check(source.Contains("Product"));\n',
                    '        try { throw new System.Exception(); } catch { Check(source.Contains("Product")); }\n',
                ),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code, output)
            self.assertIn("SOURCE_TEXT_RATCHET_ASSERTION_CHANGED", output)

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

    def _class_c_replacement_fixture(self, directory: str, head_signature: str | None = None) -> Path:
        """Create a fixture whose trusted base approves one exact Class C replacement."""
        from Scripts.validation.source_text_inventory_ratchet_helpers import (
            _ratchet_assertion_semantics,
            _ratchet_change_tokens,
            _ratchet_key_signature,
        )

        root = self._git_fixture(directory)
        validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
        original = validation.read_text(encoding="utf-8")
        key = ("packages/fixture/tests/runtime/fixturevalidation.cs", "--phase-fixture", "C")

        def signature() -> str:
            """Return the fixture key signature for the current working tree."""
            rows = inventory(root)
            return _ratchet_key_signature(
                _ratchet_change_tokens(rows).get(key),
                _ratchet_assertion_semantics(rows).get(key),
            )

        base_signature = signature()
        validation.write_text(original.replace('Contains("Product")', 'Contains("Prod")'), encoding="utf-8")
        approved_head = head_signature or signature()
        validation.write_text(original, encoding="utf-8")
        replacements = root / "Scripts/validation/source_text_ratchet_replacements.json"
        replacements.write_text(
            json.dumps(
                {
                    "replacements": [
                        {
                            "file": "Packages/fixture/Tests/Runtime/FixtureValidation.cs",
                            "validation_flag": "--phase-fixture",
                            "class": "C",
                            "base_signature": base_signature,
                            "head_signature": approved_head,
                            "reason": "The guarded product token moved behind a reviewed helper.",
                            "replacement": "behavior-test",
                        }
                    ]
                }
            )
            + "\n",
            encoding="utf-8",
        )
        subprocess.run(["git", "-C", str(root), "add", "-A"], check=True)
        subprocess.run(["git", "-C", str(root), "commit", "-qm", "trusted-replacement"], check=True)
        validation.write_text(original.replace('Contains("Product")', 'Contains("Prod")'), encoding="utf-8")
        return root

    def test_class_c_replacement_passes_with_exact_trusted_signatures(self):
        """Allow an in-place Class C edit only when the trusted base pins both signatures."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._class_c_replacement_fixture(directory)
            code, output = self._run_fixture_gate(root)
            self.assertEqual(0, code, output)
            self.assertIn("SOURCE_TEXT_RATCHET_REPLACEMENT_APPROVED", output)
            self.assertNotIn("SOURCE_TEXT_RATCHET_ASSERTION_CHANGED", output)

    def test_class_c_replacement_with_different_head_signature_fails(self):
        """Reject a replacement approval that pins a different head assertion."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._class_c_replacement_fixture(directory, head_signature="0" * 64)
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code, output)
            self.assertIn("SOURCE_TEXT_RATCHET_REPLACEMENT_MISMATCH", output)
            self.assertIn("SOURCE_TEXT_RATCHET_ASSERTION_CHANGED", output)

    def test_class_c_replacement_outside_trusted_base_fails(self):
        """Ignore replacement approvals that exist only in the reviewed head."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._class_c_replacement_fixture(directory)
            subprocess.run(
                ["git", "-C", str(root), "rm", "-q", "Scripts/validation/source_text_ratchet_replacements.json"],
                check=True,
            )
            subprocess.run(["git", "-C", str(root), "commit", "-qm", "drop-trusted-replacement"], check=True)
            subprocess.run(
                ["git", "-C", str(root), "checkout", "-q", "HEAD~1", "--", "Scripts/validation/source_text_ratchet_replacements.json"],
                check=True,
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code, output)
            self.assertIn("SOURCE_TEXT_RATCHET_ASSERTION_CHANGED", output)
            self.assertNotIn("SOURCE_TEXT_RATCHET_REPLACEMENT_APPROVED", output)

    def test_class_c_replacement_rejects_count_changes_and_other_classes(self):
        """Keep Class C count changes fail-closed and reject non-C replacement entries."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._class_c_replacement_fixture(directory)
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace(
                    "    }\n}\n", '        Check(source.Contains("Product"));\n    }\n}\n'
                ),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code, output)
            self.assertIn("SOURCE_TEXT_RATCHET_CLASS_C_INCREASE", output)

        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(directory)
            replacements = root / "Scripts/validation/source_text_ratchet_replacements.json"
            replacements.write_text(
                json.dumps(
                    {
                        "replacements": [
                            {
                                "file": "Packages/fixture/Tests/Runtime/FixtureValidation.cs",
                                "validation_flag": "--phase-fixture",
                                "class": "A",
                                "base_signature": "1" * 64,
                                "head_signature": "2" * 64,
                                "reason": "A forged class must not be accepted as a replacement.",
                                "replacement": "behavior-test",
                            }
                        ]
                    }
                )
                + "\n",
                encoding="utf-8",
            )
            subprocess.run(["git", "-C", str(root), "add", "-A"], check=True)
            subprocess.run(["git", "-C", str(root), "commit", "-qm", "forged-replacement"], check=True)
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code, output)
            self.assertIn("replacement 0 has invalid class", output)

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

    def test_private_check_body_change_fails_closed(self):
        """Reject a private Check helper change that can disable source assertions."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(directory)
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            text = validation.read_text(encoding="utf-8")
            text += "\nprivate static void Check(bool condition) { }\n"
            validation.write_text(text, encoding="utf-8")
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code, output)
            self.assertIn("SOURCE_TEXT_RATCHET_HARNESS_CHANGED", output)
            self.assertIn("helper=Check", output)

    def test_private_reader_body_change_fails_the_ratchet(self):
        """Fail when a reader starts returning an empty string."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(directory)
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            text = validation.read_text(encoding="utf-8")
            text += '\nprivate static string ReadRequiredRepoText(string path) { return ""; }\n'
            validation.write_text(text, encoding="utf-8")
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code, output)
            self.assertIn("SOURCE_TEXT_RATCHET_READER_CHANGED", output)
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

    def test_delegate_rebinding_to_noop_fails_closed(self):
        """Reject a delegate rebinding that removes the source assertion from execution."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(
                directory,
                validation_text=(
                    "using System;\n"
                    "public static class FixtureValidation {\n"
                    "    private static void Used() {\n"
                    '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                    '        Check(source.Contains("Product"));\n'
                    "    }\n"
                    "    private static void Noop() { }\n"
                    "    public static void Validate() {\n"
                    "        Action run = Used;\n"
                    "        run();\n"
                    "    }\n}\n"
                ),
            )
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace(
                    "Action run = Used;", "Action run = Noop;"
                ),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code, output)
            self.assertIn("SOURCE_TEXT_RATCHET_HARNESS_CHANGED", output)

    def test_casted_var_delegate_rebinding_to_noop_fails_closed(self):
        """Reject a var binding whose explicit delegate cast targets a no-op."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(
                directory,
                validation_text=(
                    "using System;\n"
                    "public static class FixtureValidation {\n"
                    "    private static void Used() {\n"
                    '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                    '        Check(source.Contains("Product"));\n'
                    "    }\n"
                    "    private static void Noop() { }\n"
                    "    public static void Validate() {\n"
                    "        var run = (System.Action)Used;\n"
                    "        run();\n"
                    "    }\n}\n"
                ),
            )
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace(
                    "var run = (System.Action)Used;", "var run = (System.Action)Noop;"
                ),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code, output)
            self.assertIn("SOURCE_TEXT_RATCHET_HARNESS_CHANGED", output)
            self.assertIn("helper=binding:run", output)

    def test_casted_var_delegate_qualified_method_rebinding_fails_closed(self):
        """Reject a qualified method-group delegate cast that targets a no-op."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(
                directory,
                validation_text=(
                    "using System;\n"
                    "public static class FixtureValidation {\n"
                    "    private static void Used() {\n"
                    '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                    '        Check(source.Contains("Product"));\n'
                    "    }\n"
                    "    private static void Noop() { }\n"
                    "    public static void Validate() {\n"
                    "        var run = (System.Action)FixtureValidation.Used;\n"
                    "        run();\n"
                    "    }\n}\n"
                ),
            )
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace(
                    "var run = (System.Action)FixtureValidation.Used;",
                    "var run = (System.Action)FixtureValidation.Noop;",
                ),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code, output)
            self.assertIn("SOURCE_TEXT_RATCHET_HARNESS_CHANGED", output)
            self.assertIn("helper=binding:run", output)

    def test_casted_var_custom_delegate_rebinding_fails_closed(self):
        """Reject a custom delegate cast that targets a no-op."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(
                directory,
                validation_text=(
                    "using System;\n"
                    "public static class FixtureValidation {\n"
                    "    private delegate void ValidationDelegate();\n"
                    "    private static void Used() {\n"
                    '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                    '        Check(source.Contains("Product"));\n'
                    "    }\n"
                    "    private static void Noop() { }\n"
                    "    public static void Validate() {\n"
                    "        var run = (ValidationDelegate)Used;\n"
                    "        run();\n"
                    "    }\n}\n"
                ),
            )
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace(
                    "var run = (ValidationDelegate)Used;",
                    "var run = (ValidationDelegate)Noop;",
                ),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code, output)
            self.assertIn("SOURCE_TEXT_RATCHET_HARNESS_CHANGED", output)
            self.assertIn("helper=binding:run", output)

    def test_unknown_delegate_binding_state_is_stable_and_fail_closed_on_change(self):
        """Keep an unknown dispatch stable, then reject target changes and deletion."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(
                directory,
                validation_text=(
                    "using System;\n"
                    "public static class FixtureValidation {\n"
                    "    private static void Used() {\n"
                    '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                    '        Check(source.Contains("Product"));\n'
                    "    }\n"
                    "    private static void Noop() { }\n"
                    "    public static void Validate() {\n"
                    "        var run = true ? Used : Noop;\n"
                    "        run();\n"
                    "    }\n}\n"
                ),
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code, output)
            self.assertIn("SOURCE_TEXT_RATCHET_UNSUPPORTED_DELEGATE_FLOW", output)
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            text = validation.read_text(encoding="utf-8")
            validation.write_text(text.replace("var run = true ? Used : Noop", "var run = true ? Noop : Used"), encoding="utf-8")
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code, output)
            self.assertIn("SOURCE_TEXT_RATCHET_HARNESS_CHANGED", output)
            validation.write_text(text.replace("var run = true ? Used : Noop;\n        run();\n", ""), encoding="utf-8")
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code, output)

    def test_unknown_binding_passed_to_delegate_wrapper_fails_closed_on_change(self):
        """Reject an unknown binding rebinding before a known delegate-parameter wrapper."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(
                directory,
                validation_text=(
                    "using System;\n"
                    "public static class FixtureValidation {\n"
                    "    private static void Run(int mode, Action action) { action(); }\n"
                    "    private static void Used() {\n"
                    '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                    '        Check(source.Contains("Product"));\n'
                    "    }\n"
                    "    private static void Noop() { }\n"
                    "    public static void Validate() {\n"
                    "        var run = External.Factory;\n"
                    "        Run(action: run, mode: 1);\n"
                    "    }\n}\n"
                ),
            )
            external = root / "Packages/fixture/Tests/Runtime/External.cs"
            external.write_text(
                "using System;\n"
                "public static class External {\n"
                "    public static Action Factory => FixtureValidation.Used;\n"
                "    public static Action NoopFactory => FixtureValidation.Noop;\n"
                "}\n",
                encoding="utf-8",
            )
            subprocess.run(["git", "-C", str(root), "add", "-A"], check=True)
            subprocess.run(["git", "-C", str(root), "commit", "-qm", "external-delegate"], check=True)
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace(
                    "var run = External.Factory", "var run = External.NoopFactory"
                ),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code, output)
            self.assertIn("SOURCE_TEXT_RATCHET_UNSUPPORTED_DELEGATE_FLOW", output)

    def test_custom_delegate_wrapper_body_change_fails_closed(self):
        """Include custom delegate-wrapper bodies in the harness signature."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(
                directory,
                validation_text=(
                    "using System;\n"
                    "public static class FixtureValidation {\n"
                    "    private delegate void ValidationDelegate();\n"
                    "    private static void Run(ValidationDelegate action) { action(); }\n"
                    "    private static void Used() {\n"
                    '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                    '        Check(source.Contains("Product"));\n'
                    "    }\n"
                    "    public static void Validate() { Run(Used); }\n"
                    "}\n"
                ),
            )
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace(
                    "private static void Run(ValidationDelegate action) { action(); }",
                    "private static void Run(ValidationDelegate action) { }",
                ),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code, output)
            self.assertIn("SOURCE_TEXT_RATCHET_HARNESS_CHANGED", output)
            self.assertIn("helper=Run", output)

    def test_unknown_external_delegate_invocation_forms_fail_closed(self):
        """Reject external delegate rebinding through null-forgiving and DynamicInvoke calls."""
        for invocation in ("run!();", "run.DynamicInvoke();", "Check(run());"):
            with self.subTest(invocation=invocation), tempfile.TemporaryDirectory() as directory:
                root = self._git_fixture(
                    directory,
                    validation_text=(
                        "using System;\n"
                        "public static class FixtureValidation {\n"
                        "    private static void Used() {\n"
                        '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                        '        Check(source.Contains("Product"));\n'
                        "    }\n"
                        "    private static void Noop() { }\n"
                        "    public static void Validate() {\n"
                        "        var run = External.Factory;\n"
                        f"        {invocation}\n"
                        "    }\n}\n"
                    ),
                )
                external = root / "Packages/fixture/Tests/Runtime/External.cs"
                external.write_text(
                    "using System;\n"
                    "public static class External {\n"
                    "    public static Action Factory => FixtureValidation.Used;\n"
                    "    public static Action NoopFactory => FixtureValidation.Noop;\n"
                    "}\n",
                    encoding="utf-8",
                )
                subprocess.run(["git", "-C", str(root), "add", "-A"], check=True)
                subprocess.run(["git", "-C", str(root), "commit", "-qm", "external-delegate"], check=True)
                validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
                validation.write_text(
                    validation.read_text(encoding="utf-8").replace(
                        "var run = External.Factory", "var run = External.NoopFactory"
                    ),
                    encoding="utf-8",
                )
                code, output = self._run_fixture_gate(root)
                self.assertEqual(1, code, output)
                self.assertIn("SOURCE_TEXT_RATCHET_HARNESS_CHANGED", output)

    def test_new_unknown_delegate_binding_fails_closed(self):
        """Reject a newly introduced unknown dispatch binding."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(directory)
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            text = validation.read_text(encoding="utf-8").replace(
                '        Check(source.Contains("Product"));',
                '        Check(source.Contains("Product"));\n'
                "        var run = true ? FixtureValidation.Validate : FixtureValidation.Validate;\n"
                "        run();",
            )
            validation.write_text(text, encoding="utf-8")
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code, output)
            self.assertIn("SOURCE_TEXT_RATCHET_HARNESS_CHANGED", output)

    def test_inline_delegate_forwarding_to_noop_fails_closed(self):
        """Reject a forwarding helper that stops invoking an assertion lambda."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(
                directory,
                validation_text=(
                    "using System;\n"
                    "public static class FixtureValidation {\n"
                    "    private static void Run(Action action) { action(); }\n"
                    "    public static void Validate() {\n"
                    "        Run(() => {\n"
                    '            var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                    '            Check(source.Contains("Product"));\n'
                    "        });\n"
                    "    }\n}\n"
                ),
            )
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace(
                    "private static void Run(Action action) { action(); }",
                    "private static void Run(Action action) { }",
                ),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code, output)
            self.assertIn("SOURCE_TEXT_RATCHET_HARNESS_CHANGED", output)

    def test_delegate_factory_rebinding_to_noop_fails_closed(self):
        """Reject a delegate factory change that removes its source assertion."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(
                directory,
                validation_text=(
                    "using System;\n"
                    "public static class FixtureValidation {\n"
                    "    private static Action Make() => () => {\n"
                    '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                    '        Check(source.Contains("Product"));\n'
                    "    };\n"
                    "    private static Action Noop() => () => { };\n"
                    "    public static void Validate() {\n"
                    "        Action run = Make();\n"
                    "        run();\n"
                    "    }\n}\n"
                ),
            )
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace(
                    "Action run = Make();", "Action run = Noop();"
                ),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code, output)
            self.assertIn("SOURCE_TEXT_RATCHET_HARNESS_CHANGED", output)

    def test_external_helper_body_change_fails_closed(self):
        """Reject a production helper change that weakens a source assertion operand."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(
                directory,
                validation_text=(
                    "public static class FixtureValidation {\n"
                    "    public static void Validate() {\n"
                    '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                    '        Check(ExternalChecks.IsProduct(source) && source.Contains("Product"));\n'
                    "    }\n}\n"
                ),
            )
            helper = root / "Packages/fixture/Runtime/ExternalChecks.cs"
            helper.write_text(
                'public static class ExternalChecks { public static bool IsProduct(string source) => source.Contains("Product"); }\n',
                encoding="utf-8",
            )
            subprocess.run(["git", "-C", str(root), "add", "-A"], check=True)
            subprocess.run(["git", "-C", str(root), "commit", "-q", "-m", "external-helper"], check=True)
            helper.write_text(
                "public static class ExternalChecks { public static bool IsProduct(string source) => true; }\n",
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code, output)
            self.assertIn("SOURCE_TEXT_RATCHET_HARNESS_CHANGED", output)

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
        from Scripts.validation.source_text_inventory import inventory
        from Scripts.validation.source_text_inventory_ratchet import _verify_count_ratchet

        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(directory)
            base_rows = inventory(root)
            code = _verify_count_ratchet(
                root,
                base_rows,
                [],
                "HEAD",
                [],
                [],
            )
            self.assertEqual(1, code)
