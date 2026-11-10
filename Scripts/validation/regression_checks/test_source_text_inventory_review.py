"""Exercise source inventory review regressions through real Git fixture gates."""

from __future__ import annotations

import subprocess
import tempfile
import unittest

from .test_source_text_inventory_semantics import _FixtureSupport


class SourceTextInventoryReviewTests(_FixtureSupport, unittest.TestCase):
    """Keep helper arguments and branch exits bound to existing inventory rows."""

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
