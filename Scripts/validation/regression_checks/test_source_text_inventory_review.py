"""Exercise source inventory review regressions through real Git fixture gates."""

from __future__ import annotations

import tempfile
import unittest

from .test_source_text_inventory_semantics import _FixtureSupport


class SourceTextInventoryReviewTests(_FixtureSupport, unittest.TestCase):
    """Keep helper arguments and branch exits bound to existing inventory rows."""

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
