"""Additional source inventory semantic regression fixtures."""

from __future__ import annotations

import csv
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

    def test_generic_delegate_cast_rebinding_fails_closed(self):
        """Reject a generic method-group cast that targets a no-op."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(
                directory,
                validation_text=(
                    "using System;\n"
                    "public static class FixtureValidation {\n"
                    "    private static void Used<T>() {\n"
                    '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                    '        Check(source.Contains("Product"));\n'
                    "    }\n"
                    "    private static void Noop<T>() { }\n"
                    "    public static void Validate() {\n"
                    "        var run = (Action)Used<int>;\n"
                    "    }\n}\n"
                ),
            )
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace(
                    "var run = (Action)Used<int>;", "var run = (Action)Noop<int>;"
                ),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code, output)
            self.assertIn("SOURCE_TEXT_RATCHET_HARNESS_CHANGED", output)

    def test_delegate_invocation_scan_stays_within_containing_method(self):
        """Ignore a same-named invocation in a different method."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(
                directory,
                validation_text=(
                    "public static class FixtureValidation {\n"
                    "    private static void Used() {\n"
                    '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                    '        Check(source.Contains("Product"));\n'
                    "    }\n"
                    "    public static void Validate() {\n"
                    "        var ordinary = 1;\n"
                    "        Used();\n"
                    "    }\n"
                    "    private static void Other() { ordinary(); }\n"
                    "}\n"
                ),
            )
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace(
                    "var ordinary = 1;", "var ordinary = 2;"
                ),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(0, code, output)

    def test_unknown_delegate_state_propagates_through_alias_chain(self):
        """Reject an external delegate rebinding hidden behind two local aliases."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(
                directory,
                validation_text=(
                    "using System;\n"
                    "public static class FixtureValidation {\n"
                    "    private static void Run(Action action) { action(); }\n"
                    "    private static void Used() {\n"
                    '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                    '        Check(source.Contains("Product"));\n'
                    "    }\n"
                    "    private static void Noop() { }\n"
                    "    public static void Validate() {\n"
                    "        var first = External.Factory;\n"
                    "        var run = first;\n"
                    "        Run(run);\n"
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
            subprocess.run(["git", "-C", str(root), "commit", "-qm", "external-alias"], check=True)
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace(
                    "External.Factory", "External.NoopFactory"
                ),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code, output)
            self.assertIn("SOURCE_TEXT_RATCHET_UNSUPPORTED_DELEGATE_FLOW", output)

    def test_unknown_external_delegate_wrapper_fails_closed(self):
        """Reject an external custom delegate wrapper that stops invoking its argument."""
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
                    "        var run = External.Factory;\n"
                    "        External.Run(run);\n"
                    "    }\n}\n"
                ),
            )
            external = root / "Packages/fixture/Tests/Runtime/External.cs"
            external.write_text(
                "public static class External {\n"
                "    public delegate void ValidationDelegate();\n"
                "    public static void Run(ValidationDelegate action) { action(); }\n"
                "    public static ValidationDelegate Factory => FixtureValidation.Used;\n"
                "    public static ValidationDelegate NoopFactory => FixtureValidation.Noop;\n"
                "}\n",
                encoding="utf-8",
            )
            subprocess.run(["git", "-C", str(root), "add", "-A"], check=True)
            subprocess.run(["git", "-C", str(root), "commit", "-qm", "external-wrapper"], check=True)
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace(
                    "External.Factory", "External.NoopFactory"
                ),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code, output)
            self.assertIn("SOURCE_TEXT_RATCHET_UNSUPPORTED_DELEGATE_FLOW", output)

    def test_unknown_delegate_aliases_keep_same_named_locals_separate(self):
        """Keep alias closure scoped when another method reuses the binding name."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(
                directory,
                validation_text=(
                    "using System;\n"
                    "public static class FixtureValidation {\n"
                    "    private static void Run(Action action) { action(); }\n"
                    "    private static void Used() {\n"
                    '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                    '        Check(source.Contains("Product"));\n'
                    "    }\n"
                    "    private static void Noop() { }\n"
                    "    public static void Validate() {\n"
                    "        var first = External.Factory;\n"
                    "        var run = first;\n"
                    "        Run(run);\n"
                    "    }\n"
                    "    private static void Other() {\n"
                    "        var second = 1;\n"
                    "        var run = second;\n"
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
            subprocess.run(["git", "-C", str(root), "commit", "-qm", "shadowed-alias"], check=True)
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace(
                    "External.Factory", "External.NoopFactory"
                ),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code, output)
            self.assertIn("SOURCE_TEXT_RATCHET_UNSUPPORTED_DELEGATE_FLOW", output)

    def test_delegate_boundary_rejects_null_forgiving_alias(self):
        """Reject a delegate-bound alias whose null-forgiving transfer is unsupported."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(
                directory,
                validation_text=(
                    "using System;\n"
                    "public static class FixtureValidation {\n"
                    "    private static void Run(Action action) { action(); }\n"
                    "    private static void Used() {\n"
                    '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
                    '        Check(source.Contains("Product"));\n'
                    "    }\n"
                    "    private static void Noop() { }\n"
                    "    public static void Validate() {\n"
                    "        var first = External.Factory;\n"
                    "        var run = first!;\n"
                    "        Run(run);\n"
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
            subprocess.run(["git", "-C", str(root), "commit", "-qm", "null-forgiving-alias"], check=True)
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace(
                    "External.Factory", "External.NoopFactory"
                ),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code, output)
            self.assertIn("SOURCE_TEXT_RATCHET_UNSUPPORTED_DELEGATE_FLOW", output)

    def test_delegate_boundary_rejects_unknown_qualified_wrapper(self):
        """Reject an unregistered qualified wrapper receiving a possible delegate binding."""
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
                    "        var first = External.Factory;\n"
                    "        var run = first!;\n"
                    "        External.Execute(run);\n"
                    "    }\n}\n"
                ),
            )
            external = root / "Packages/fixture/Tests/Runtime/External.cs"
            external.write_text(
                "using System;\n"
                "public static class External {\n"
                "    public static Action Factory => FixtureValidation.Used;\n"
                "    public static Action NoopFactory => FixtureValidation.Noop;\n"
                "    public static void Execute(Action action) { action(); }\n"
                "}\n",
                encoding="utf-8",
            )
            subprocess.run(["git", "-C", str(root), "add", "-A"], check=True)
            subprocess.run(["git", "-C", str(root), "commit", "-qm", "qualified-wrapper"], check=True)
            validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
            validation.write_text(
                validation.read_text(encoding="utf-8").replace(
                    "External.Factory", "External.NoopFactory"
                ),
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code, output)
            self.assertIn("SOURCE_TEXT_RATCHET_UNSUPPORTED_DELEGATE_FLOW", output)
    def test_qualified_delegate_boundary_normalizes_argument_forms(self):
        """Reject equivalent qualified delegate arguments after target rebinding."""
        cases = (
            ("bare", "External.Execute(run);"),
            ("named", "External.Execute(action: run);"),
            ("generic", "External.Execute<object>(run);"),
            ("cast", "External.Execute((System.Action)run);"),
        )
        for label, invocation in cases:
            with self.subTest(label=label), tempfile.TemporaryDirectory() as directory:
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
                    "    public static void Execute<T>(Action action) { action(); }\n"
                    "}\n",
                    encoding="utf-8",
                )
                subprocess.run(["git", "-C", str(root), "add", "-A"], check=True)
                subprocess.run(["git", "-C", str(root), "commit", "-qm", "qualified-argument-forms"], check=True)
                validation = root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
                validation.write_text(
                    validation.read_text(encoding="utf-8").replace(
                        "External.Factory", "External.NoopFactory"
                    ),
                    encoding="utf-8",
                )
                code, output = self._run_fixture_gate(root)
                self.assertEqual(1, code, output)
                self.assertIn("SOURCE_TEXT_RATCHET_HARNESS_CHANGED", output)
    def test_runtime_method_additions_do_not_change_validation_helper_signatures(self):
        """Ignore unresolved production helper candidates when signing validation harnesses."""
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(directory, validation_text=(
                'public static class FixtureValidation { public static void Validate() { var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs"); dynamic helper = new RuntimeHelper(); Check(source.Contains("Product") && helper.TryMap(source)); } }\n'
            ))
            runtime_helper = root / "Packages/fixture/Runtime/RuntimeHelper.cs"
            base = 'public sealed class RuntimeHelper { public bool TryMap(string source) => source.Contains("Product"); }\n'
            runtime_helper.write_text(base, encoding="utf-8")
            before = _gate_module.inventory(root)
            runtime_helper.write_text(base + "public sealed class UnrelatedHelper { public bool TryMap(string source) => false; }\n", encoding="utf-8")
            self.assertEqual(before[0]["harness_signature"], _gate_module.inventory(root)[0]["harness_signature"])
    def test_task_run_without_wait_fails_closed(self):
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
                    "    }\n}\n"
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
            self.assertEqual(1, code, output)
            self.assertIn("SOURCE_TEXT_RATCHET_HARNESS_CHANGED", output)

    def _reader_gate(
        self,
        helpers: str,
        statements: str,
        extra_files: dict[str, str] | None = None,
        base_statements: str = "",
        prelude: str = "",
    ) -> tuple[int, str]:
        """Add statements to a registered entrypoint behind helper declarations and run the real gate."""
        template = (
            prelude + "public static partial class FixtureValidation {{\n{helpers}\n"
            "    public static void Validate() {{\n{body}\n    }}\n}}\n"
        )
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(directory, validation_text=template.format(helpers=helpers, body=base_statements))
            for relative, text in (extra_files or {}).items():
                path = root / relative if relative.startswith("Packages/") else root / "Packages/fixture/Tests/Runtime" / relative
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text(text, encoding="utf-8")
            subprocess.run(["git", "-C", str(root), "add", "-A"], check=True)
            subprocess.run(["git", "-C", str(root), "commit", "-q", "--allow-empty", "-m", "readers"], check=True)
            (root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs").write_text(
                template.format(helpers=helpers, body=statements), encoding="utf-8"
            )
            return self._run_fixture_gate(root)

    def test_discovered_readers_put_new_assertions_under_the_ratchet(self):
        """Fail the gate when a new assertion reads source through any discovered reader helper."""
        product = '"Packages/fixture/Runtime/Product.cs"'
        check = 'Check(source.Contains("Product"));'
        local = "    private static string Read(string relativePath) => ReadRequiredRepoText(relativePath);"
        cases = {
            "local Read": (local, f"var source = Read({product});\n{check}", None),
            "root and path Read": (
                "    private static string Read(string root, string relativePath)"
                " => System.IO.File.ReadAllText(System.IO.Path.Combine(root, relativePath));",
                f'var source = Read(".", {product});\n{check}',
                None,
            ),
            "arbitrary name": (
                "    private static string LoadThing(string path) => System.IO.File.ReadAllText(path);",
                f"var source = LoadThing({product});\n{check}",
                None,
            ),
            "nested wrappers": (
                "    private static string Inner(string path) => ReadRequiredRepoText(path);\n"
                "    private static string Outer(string path) { return Inner(path); }",
                f"var source = Outer({product});\n{check}",
                None,
            ),
            "partial sibling": ("", f"var source = Read({product});\n{check}", {
                "FixtureValidation.Readers.cs": "public static partial class FixtureValidation {\n" + local + "\n}\n",
            }),
            "qualified type": ("", f"var source = SourceReaders.Read({product});\n{check}", {
                "SourceReaders.cs": "internal static class SourceReaders {\n"
                "    internal static string Read(string relativePath) => System.IO.File.ReadAllText(relativePath);\n}\n",
            }),
            "local propagation": (local, f"var first = Read({product});\nvar source = first;\n{check}", None),
            "direct receiver": (local, f'Check(Read({product}).Contains("Product"));', None),
            "dynamic path": (
                local,
                'var path = System.Environment.GetEnvironmentVariable("X");\n'
                f"var source = Read(path);\n{check}",
                None,
            ),
        }
        for name, (helpers, statements, extra_files) in cases.items():
            with self.subTest(name):
                code, output = self._reader_gate(helpers, statements, extra_files)
                self.assertNotEqual(0, code, output)
                self.assertRegex(output, r"SOURCE_TEXT_RATCHET_(?:CLASS_C_INCREASE|UNRESOLVED_INCREASE)")
        namespaced = {"SourceReaders.cs": "namespace Fixture.Readers {\n    internal static class SourceReaders {\n"
                      "        internal static string Read(string relativePath) => System.IO.File.ReadAllText(relativePath);\n    }\n}\n"}
        variants = {
            "System.String": ("    private static System.String LoadThing(string path) => System.IO.File.ReadAllText(path);", "LoadThing", "", None),
            "global String": ("    private static global::System.String LoadThing(string path) => System.IO.File.ReadAllText(path);", "LoadThing", "", None),
            "nullable string": ("    private static string? LoadThing(string path) => System.IO.File.ReadAllText(path);", "LoadThing", "", None),
            "nullable array": ("    private static string[]? LoadThing(string path) => System.IO.File.ReadAllLines(path);", "LoadThing", "", None),
            "File alias": ("    private static string LoadThing(string path) => F.ReadAllText(path);", "LoadThing", "using F = System.IO.File;\n", None),
            "async read": ("    private static string LoadThing(string path) => System.IO.File.ReadAllTextAsync(path).GetAwaiter().GetResult();", "LoadThing", "", None),
            "FileInfo text": ("    private static string LoadThing(string path) => new System.IO.FileInfo(path).OpenText().ReadToEnd();", "LoadThing", "", None),
            "this call": (local, "this.Read", "", None),
            "type alias": ("", "SR.Read", "using SR = Fixture.Readers.SourceReaders;\n", namespaced),
            "imported namespace": ("", "SourceReaders.Read", "using Fixture.Readers;\n", namespaced),
            "fully qualified": ("", "Fixture.Readers.SourceReaders.Read", "", namespaced),
            "using static": ("", "Read", "using static Fixture.Readers.SourceReaders;\n", namespaced),
            "using static wrapper": ("", "Wrapper.Load", "", {**namespaced, "Wrapper.cs": (
                "using static Fixture.Readers.SourceReaders;\n"
                "internal static class Wrapper {\n    internal static string Load(string path) => Read(path);\n}\n"
            )}),
            "generic reader": (
                "    private static string LoadThing<T>(string path) => System.IO.File.ReadAllText(path);",
                "LoadThing<object>",
                "",
                None,
            ),
            "constrained generic block": (
                "    private static string LoadThing<TKey, TValue>(string path) where TKey : class\n"
                "    {\n        return System.IO.File.ReadAllText(path);\n    }",
                "LoadThing<string, int>",
                "",
                None,
            ),
            "qualified generic": ("", "SourceReaders.Read<int>", "", {
                "SourceReaders.cs": "internal static class SourceReaders {\n"
                "    internal static string Read<T>(string relativePath) => System.IO.File.ReadAllText(relativePath);\n}\n",
            }),
        }
        for name, (helpers, call, prelude, extra_files) in variants.items():
            with self.subTest(name):
                code, output = self._reader_gate(
                    helpers, f"var source = {call}({product});\n{check}", extra_files, prelude=prelude
                )
                self.assertNotEqual(0, code, output)
                self.assertRegex(output, r"SOURCE_TEXT_RATCHET_(?:CLASS_C_INCREASE|UNRESOLVED_INCREASE)")

    def test_qualified_readers_stay_scoped_to_visible_declarations(self):
        """Ignore Type.Read calls that bind to a same-named non-reader type in another namespace or package."""
        product = '"Packages/fixture/Runtime/Product.cs"'
        statements = f'var source = SourceReaders.Read({product});\nCheck(source.Contains("Product"));'
        reader = "        internal static string Read(string relativePath) => System.IO.File.ReadAllText(relativePath);\n"
        plain = "        internal static string Read(string relativePath) => relativePath;\n"
        cases = {
            "foreign namespace": ({
                "Readers.cs": "namespace Fixture.Readers {\n    internal static class SourceReaders {\n" + reader + "    }\n}\n",
                "Plain.cs": "namespace Fixture.Other {\n    internal static class SourceReaders {\n" + plain + "    }\n}\n",
            }, "using Fixture.Other;\n"),
            "foreign package": ({
                "Packages/other/Tests/Runtime/SourceReaders.cs": "internal static class SourceReaders {\n" + reader + "}\n",
                "Plain.cs": "internal static class SourceReaders {\n" + plain + "}\n",
            }, ""),
        }
        for name, (extra_files, prelude) in cases.items():
            with self.subTest(name):
                code, output = self._reader_gate("", statements, extra_files, prelude=prelude)
                self.assertEqual(0, code, output)
        explicit = {
            "Readers.cs": "namespace Good {\n    internal static class SourceReaders {\n" + reader + "    }\n}\n",
            "Plain.cs": "namespace Other {\n    internal static class SourceReaders {\n" + plain + "    }\n}\n",
        }
        member_seed = (
            "    private sealed class Cursor { public string ReadText(string path) => \"Product\"; }\n"
            "    private static string LoadThing(string path) => new Cursor().ReadText(path);"
        )
        transitive = dict(explicit)
        transitive["Wrapper.cs"] = (
            "namespace Other {\n    internal static class Wrapper {\n"
            "        internal static string Load(string relativePath) => SourceReaders.Read(relativePath);\n    }\n}\n"
        )
        for name, (helpers, call, extra_files, prelude) in {
            "explicit foreign namespace": ("", "Other.SourceReaders.Read", explicit, "using Good;\n"),
            "member seed call": (member_seed, "LoadThing", None, ""),
            "transitive foreign namespace": ("", "Wrapper.Load", transitive, "using Other;\n"),
        }.items():
            with self.subTest(name):
                code, output = self._reader_gate(
                    helpers, f'var source = {call}({product});\nCheck(source.Contains("Product"));', extra_files, prelude=prelude
                )
                self.assertEqual(0, code, output)

    def test_non_reader_calls_stay_outside_the_ratchet(self):
        """Keep member calls, non-text helpers, masked or inactive definitions, foreign namesakes and temp reads out."""
        product = '"Packages/fixture/Runtime/Product.cs"'
        check = 'Check(source.Contains("Product"));'
        local = "    private static string Read(string relativePath) => ReadRequiredRepoText(relativePath);"
        plain = "    private static string Read(string relativePath) => relativePath;"
        readers = {
            "SourceReaders.cs": "internal static class SourceReaders {\n"
            "    internal static string Read(string relativePath) => System.IO.File.ReadAllText(relativePath);\n}\n",
        }
        cases = {
            "member call": (
                local + '\n    private sealed class Cursor { public string Read() => "Product"; }',
                f"var cursor = new Cursor();\nvar source = cursor.Read();\n{check}",
                None,
            ),
            "non-text helper": (
                "    private static int ReadLength(string path) => System.IO.File.ReadAllText(path).Length;",
                f'var length = ReadLength({product});\nCheck(length.ToString().Contains("1"));',
                None,
            ),
            "masked definition": (
                "    // private static string Read(string relativePath) => ReadRequiredRepoText(relativePath);\n"
                '    private const string Fake = "static string Read(string p) => ReadRequiredRepoText(p);";\n' + plain,
                f"var source = Read({product});\n{check}",
                None,
            ),
            "inactive definition": (
                "#if false\n" + local + "\n#else\n" + plain + "\n#endif",
                f"var source = Read({product});\n{check}",
                None,
            ),
            "foreign namesake": (plain, f"var source = Read({product});\n{check}", readers),
            "temporary file": (
                "",
                f"var source = System.IO.File.ReadAllText(System.IO.Path.GetTempFileName());\n{check}",
                None,
            ),
        }
        for name, (helpers, statements, extra_files) in cases.items():
            with self.subTest(name):
                code, output = self._reader_gate(helpers, statements, extra_files)
                self.assertEqual(0, code, output)
        code, output = self._reader_gate(
            local,
            f"var source = Read({product});\n{check}\nvar unrelated = 1;",
            base_statements=f"var source = Read({product});\n{check}",
        )
        self.assertEqual(0, code, output)

    def test_discovered_reader_body_changes_fail_the_gate(self):
        """Fail when a discovered reader keeps a dead read but stops returning the source."""
        dead = "using var stream = new System.IO.FileStream(path, System.IO.FileMode.Open);"
        real = "{ " + dead + " return ReadRequiredRepoText(path); }"
        fake = "{ " + dead + ' return "Constant"; }'
        loaders = "internal static class SourceLoaders {\n    internal static string Load(string path) BODY\n}\n"
        cases = {
            "local reader": ("    private static string Load(string path) BODY", "Load", None),
            "generic reader": ("    private static string Load<T>(string path) BODY", "Load<object>", None),
            "external reader": ("", "SourceLoaders.Load", "SourceLoaders.cs"),
        }
        for name, (helper, call, extra) in cases.items():
            with self.subTest(name), tempfile.TemporaryDirectory() as directory:
                validation = (
                    "public static class FixtureValidation {\n" + helper + "\n"
                    "    public static void Validate() {\n"
                    f'        var source = {call}("Packages/fixture/Runtime/Product.cs");\n'
                    '        Check(source.Contains("Product"));\n    }\n}\n'
                )
                root = self._git_fixture(directory, validation_text=validation.replace("BODY", real))
                runtime = root / "Packages/fixture/Tests/Runtime"
                if extra:
                    (runtime / extra).write_text(loaders.replace("BODY", real), encoding="utf-8")
                    subprocess.run(["git", "-C", str(root), "add", "-A"], check=True)
                    subprocess.run(["git", "-C", str(root), "commit", "-q", "-m", "loaders"], check=True)
                    (runtime / extra).write_text(loaders.replace("BODY", fake), encoding="utf-8")
                else:
                    (runtime / "FixtureValidation.cs").write_text(validation.replace("BODY", fake), encoding="utf-8")
                code, output = self._run_fixture_gate(root)
                self.assertIn("SOURCE_TEXT_INVENTORY_BASE_OK base_rows=1 head_rows=1", output)
                self.assertNotEqual(0, code, output)
                self.assertRegex(output, r"SOURCE_TEXT_RATCHET_READER_CHANGED file=\S+ helper=Load\b")

    def test_reader_swaps_through_new_files_fail_the_gate(self):
        """Fail when a reader moves to a new file or the call switches to a new reader with a dead read."""
        dead = "using var stream = new System.IO.FileStream(path, System.IO.FileMode.Open);"
        holder = "internal static class {owner} {{\n    internal static string Load(string path) {{ " + dead + " return {value}; }}\n}}\n"
        real = holder.format(owner="Helpers", value="ReadRequiredRepoText(path)")
        cases = {
            "deleted reader file": ("Helpers", {"Helpers2.cs": holder.format(owner="Helpers", value='"Constant"')}, "READER_CHANGED"),
            "switched reader": ("Fakes", {"Fakes.cs": holder.format(owner="Fakes", value='"Constant"')}, "ASSERTION_CHANGED"),
        }
        for name, (owner, added, failure) in cases.items():
            with self.subTest(name), tempfile.TemporaryDirectory() as directory:
                validation = (
                    "public static class FixtureValidation {\n    public static void Validate() {\n"
                    '        var source = OWNER.Load("Packages/fixture/Runtime/Product.cs");\n'
                    '        Check(source.Contains("Product"));\n    }\n}\n'
                )
                root = self._git_fixture(directory, validation_text=validation.replace("OWNER", "Helpers"))
                runtime = root / "Packages/fixture/Tests/Runtime"
                (runtime / "Helpers.cs").write_text(real, encoding="utf-8")
                subprocess.run(["git", "-C", str(root), "add", "-A"], check=True)
                subprocess.run(["git", "-C", str(root), "commit", "-q", "-m", "helpers"], check=True)
                if name == "deleted reader file":
                    (runtime / "Helpers.cs").unlink()
                for relative, text in added.items():
                    (runtime / relative).write_text(text, encoding="utf-8")
                (runtime / "FixtureValidation.cs").write_text(validation.replace("OWNER", owner), encoding="utf-8")
                code, output = self._run_fixture_gate(root)
                self.assertIn("SOURCE_TEXT_INVENTORY_BASE_OK base_rows=1 head_rows=1", output)
                self.assertNotEqual(0, code, output)
                self.assertIn(f"SOURCE_TEXT_RATCHET_{failure} file=", output)

    def test_trusted_base_approves_exact_reader_body_change(self):
        """Accept a reader body change only when the trusted base lists its exact digests."""
        from Scripts.validation.source_text_inventory_ratchet_helpers import _helper_signature_records

        reader = "\n    private static string ReadRequiredRepoText(string path) {{ return {value}; }}\n"
        validation = (
            "public static class FixtureValidation {{" + reader + "    public static void Validate() {{\n"
            '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
            '        Check(source.Contains("Product"));\n    }}\n}}\n'
        )
        base = validation.format(value="System.IO.File.ReadAllText(path)")
        head = validation.format(value='""')
        digests = [_helper_signature_records(text)["ReadRequiredRepoText"] for text in (base, head)]
        for name, head_digest, expected in (
            ("exact approval", digests[1], 0),
            ("stale approval", "0" * 64, 1),
        ):
            with self.subTest(name), tempfile.TemporaryDirectory() as directory:
                root = self._git_fixture(directory, validation_text=base)
                (root / "Scripts/validation/source_text_ratchet_replacements.json").write_text(json.dumps({"replacements": [{
                    "file": "Packages/fixture/Tests/Runtime/FixtureValidation.cs",
                    "validation_flag": "ReadRequiredRepoText",
                    "class": "READER",
                    "base_signature": digests[0],
                    "head_signature": head_digest,
                    "reason": "Reviewed reader body replacement for the fixture.",
                    "replacement": "fixture",
                }]}), encoding="utf-8")
                subprocess.run(["git", "-C", str(root), "add", "-A"], check=True)
                subprocess.run(["git", "-C", str(root), "commit", "-q", "-m", "approve"], check=True)
                (root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs").write_text(head, encoding="utf-8")
                code, output = self._run_fixture_gate(root)
                self.assertEqual(expected, code, output)
                verdict = "APPROVED" if expected == 0 else "CHANGED"
                self.assertIn(f"SOURCE_TEXT_RATCHET_READER_{verdict} file=", output)

    def test_trusted_reader_approval_cannot_mask_external_harness_change(self):
        """Reject an unrelated external helper change alongside an approved reader edit."""
        from Scripts.validation.source_text_inventory_ratchet_helpers import _helper_signature_records

        base_validation = (
            "public static class FixtureValidation {\n"
            "    private static string ReadRequiredRepoText(string path) { return System.IO.File.ReadAllText(path); }\n"
            "    public static void Validate() {\n"
            '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
            '        Check(ExternalChecks.IsProduct(source) && source.Contains("Product"));\n'
            "    }\n}\n"
        )
        head_validation = base_validation.replace(
            "return System.IO.File.ReadAllText(path);", 'return "";'
        )
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(directory, validation_text=base_validation)
            helper = root / "Packages/fixture/Runtime/ExternalChecks.cs"
            helper.write_text(
                "public static class ExternalChecks { public static bool IsProduct(string source) => source.Contains(\"Product\"); }\n",
                encoding="utf-8",
            )
            replacements = root / "Scripts/validation/source_text_ratchet_replacements.json"
            replacements.write_text(
                json.dumps(
                    {
                        "replacements": [
                            {
                                "file": "Packages/fixture/Tests/Runtime/FixtureValidation.cs",
                                "validation_flag": "ReadRequiredRepoText",
                                "class": "READER",
                                "base_signature": _helper_signature_records(base_validation)["ReadRequiredRepoText"],
                                "head_signature": _helper_signature_records(head_validation)["ReadRequiredRepoText"],
                                "reason": "Reviewed reader body replacement for the fixture.",
                                "replacement": "fixture",
                            }
                        ]
                    }
                )
                + "\n",
                encoding="utf-8",
            )
            subprocess.run(["git", "-C", str(root), "add", "-A"], check=True)
            subprocess.run(["git", "-C", str(root), "commit", "-qm", "approve-reader"], check=True)
            (root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs").write_text(
                head_validation, encoding="utf-8"
            )
            helper.write_text(
                "public static class ExternalChecks { public static bool IsProduct(string source) => true; }\n",
                encoding="utf-8",
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code, output)
            self.assertIn("SOURCE_TEXT_RATCHET_HARNESS_CHANGED", output)

    def test_trusted_reader_approval_cannot_mask_same_file_harness_change(self):
        """Reject a scheduling change combined with an approved reader edit in one file."""
        from Scripts.validation.source_text_inventory_ratchet_helpers import _helper_signature_records

        base_validation = (
            "public static class FixtureValidation {\n"
            "    private static string ReadRequiredRepoText(string path) { return System.IO.File.ReadAllText(path); }\n"
            "    public static void Validate() {\n"
            "        System.Threading.Tasks.Task.Run(() => {\n"
            '            var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
            '            Check(source.Contains("Product"));\n'
            "        }).Wait();\n"
            "    }\n}\n"
        )
        head_validation = base_validation.replace(
            "return System.IO.File.ReadAllText(path);", 'return "";'
        ).replace("        }).Wait();\n", "        });\n")
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(directory, validation_text=base_validation)
            replacements = root / "Scripts/validation/source_text_ratchet_replacements.json"
            replacements.write_text(
                json.dumps(
                    {
                        "replacements": [
                            {
                                "file": "Packages/fixture/Tests/Runtime/FixtureValidation.cs",
                                "validation_flag": "ReadRequiredRepoText",
                                "class": "READER",
                                "base_signature": _helper_signature_records(base_validation)["ReadRequiredRepoText"],
                                "head_signature": _helper_signature_records(head_validation)["ReadRequiredRepoText"],
                                "reason": "Reviewed reader body replacement for the fixture.",
                                "replacement": "fixture",
                            }
                        ]
                    }
                )
                + "\n",
                encoding="utf-8",
            )
            subprocess.run(["git", "-C", str(root), "add", "-A"], check=True)
            subprocess.run(["git", "-C", str(root), "commit", "-qm", "approve-reader"], check=True)
            (root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs").write_text(
                head_validation, encoding="utf-8"
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(1, code, output)
            self.assertIn("SOURCE_TEXT_RATCHET_HARNESS_CHANGED", output)

    def test_trusted_reader_approval_covers_partial_sibling_file(self):
        """Accept an exact reader replacement when the helper lives in a partial sibling file."""
        from Scripts.validation.source_text_inventory_ratchet_helpers import _helper_signature_records

        validation = (
            "public static partial class FixtureValidation {\n"
            "    public static void Validate() {\n"
            '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
            '        Check(source.Contains("Product"));\n'
            "    }\n}\n"
        )
        base_reader = (
            "public static partial class FixtureValidation {\n"
            "    private static string ReadRequiredRepoText(string path) { return System.IO.File.ReadAllText(path); }\n"
            "}\n"
        )
        head_reader = base_reader.replace(
            "return System.IO.File.ReadAllText(path);", 'return "";'
        )
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(directory, validation_text=validation)
            reader = root / "Packages/fixture/Tests/Runtime/FixtureValidation.Readers.cs"
            reader.write_text(base_reader, encoding="utf-8")
            replacements = root / "Scripts/validation/source_text_ratchet_replacements.json"
            replacements.write_text(
                json.dumps(
                    {
                        "replacements": [
                            {
                                "file": "Packages/fixture/Tests/Runtime/FixtureValidation.Readers.cs",
                                "validation_flag": "ReadRequiredRepoText",
                                "class": "READER",
                                "base_signature": _helper_signature_records(base_reader)["ReadRequiredRepoText"],
                                "head_signature": _helper_signature_records(head_reader)["ReadRequiredRepoText"],
                                "reason": "Reviewed reader body replacement for the fixture.",
                                "replacement": "fixture",
                            }
                        ]
                    }
                )
                + "\n",
                encoding="utf-8",
            )
            subprocess.run(["git", "-C", str(root), "add", "-A"], check=True)
            subprocess.run(["git", "-C", str(root), "commit", "-qm", "approve-partial-reader"], check=True)
            reader.write_text(head_reader, encoding="utf-8")
            code, output = self._run_fixture_gate(root)
            self.assertEqual(0, code, output)
            self.assertIn("SOURCE_TEXT_RATCHET_READER_APPROVED", output)

    def test_trusted_generic_reader_approval_is_isolated(self):
        """Accept an exact approval for a generic reader body replacement."""
        from Scripts.validation.source_text_inventory_ratchet_helpers import _helper_signature_records

        reader_names = frozenset({"ReadRequiredRepoText"})
        base_validation = (
            "public static class FixtureValidation {\n"
            "    private static string ReadRequiredRepoText<T>(string path) { return System.IO.File.ReadAllText(path); }\n"
            "    public static void Validate() {\n"
            '        var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs");\n'
            '        Check(source.Contains("Product"));\n'
            "    }\n}\n"
        )
        head_validation = base_validation.replace(
            "return System.IO.File.ReadAllText(path);", 'return "";'
        )
        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(directory, validation_text=base_validation)
            replacements = root / "Scripts/validation/source_text_ratchet_replacements.json"
            replacements.write_text(
                json.dumps(
                    {
                        "replacements": [
                            {
                                "file": "Packages/fixture/Tests/Runtime/FixtureValidation.cs",
                                "validation_flag": "ReadRequiredRepoText",
                                "class": "READER",
                                "base_signature": _helper_signature_records(base_validation, reader_names)["ReadRequiredRepoText"],
                                "head_signature": _helper_signature_records(head_validation, reader_names)["ReadRequiredRepoText"],
                                "reason": "Reviewed generic reader body replacement for the fixture.",
                                "replacement": "fixture",
                            }
                        ]
                    }
                )
                + "\n",
                encoding="utf-8",
            )
            subprocess.run(["git", "-C", str(root), "add", "-A"], check=True)
            subprocess.run(["git", "-C", str(root), "commit", "-qm", "approve-generic-reader"], check=True)
            (root / "Packages/fixture/Tests/Runtime/FixtureValidation.cs").write_text(
                head_validation, encoding="utf-8"
            )
            code, output = self._run_fixture_gate(root)
            self.assertEqual(0, code, output)
            self.assertIn("SOURCE_TEXT_RATCHET_READER_APPROVED", output)

    def test_reader_approval_rejects_case_colliding_helper_paths(self):
        """Reject reader isolation when canonical helper paths are ambiguous."""
        from Scripts.validation.source_text_inventory_reader_ratchet import (
            _approved_reader_change_isolated,
            _ratchet_path,
        )

        approved_path = "Packages/fixture/Tests/Runtime/FixtureValidation.cs"
        colliding_path = "packages/fixture/tests/runtime/fixturevalidation.cs"
        normalized_path = _ratchet_path(approved_path)
        base_helpers = {
            (approved_path, "ReadRequiredRepoText"): "base-reader",
            (colliding_path, "Run"): "base-run",
        }
        head_helpers = {
            (approved_path, "ReadRequiredRepoText"): "head-reader",
            (colliding_path, "Run"): "head-run",
        }
        self.assertFalse(
            _approved_reader_change_isolated(
                Path("."),
                "HEAD",
                {(normalized_path, "ReadRequiredRepoText")},
                base_helpers,
                head_helpers,
                {normalized_path},
            )
        )

    def _declared_type_fixture(self, directory: str) -> Path:
        """Create registered validation types declared outside their stem-named files."""
        root = self._git_fixture(directory)
        (root / "Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs").write_text(
            "namespace Fixture.Tests\n{\n"
            '    Ci("--phase-fixture", "fixture", FixtureValidation.Validate, evidence: ValidationEvidence.Behavior),\n'
            '    Ci("--phase-partial", "partial", PartialValidation.Validate, evidence: ValidationEvidence.Behavior),\n'
            '    Ci("--phase-renamed", "renamed", RenamedValidation.Validate, evidence: ValidationEvidence.Behavior),\n'
            '    Ci("--phase-enclosing", "enclosing", EnclosingValidation.Validate, evidence: ValidationEvidence.Behavior),\n'
            '    Ci("--phase-other", "other", OtherValidation.Validate, includeInDefault: false, evidence: ValidationEvidence.Behavior),\n'
            '    Ci("--phase-multi-a", "multi a", MultiValidation.Validate, evidence: ValidationEvidence.Behavior),\n'
            '    Ci("--phase-multi-b", "multi b", MultiValidation.ValidateOther, evidence: ValidationEvidence.Behavior),\n'
            "}\n",
            encoding="utf-8",
        )
        probe = (
            'static void Probe() { var source = ReadRequiredRepoText("Packages/fixture/Runtime/Product.cs"); '
            'Check(source.Contains("Product")); }'
        )
        files = {
            "Tests/Runtime/Decomposed/PartialValidation/VerifyPartial.cs": "namespace Fixture.Tests\n{\n    public static partial class PartialValidation { " + probe + " }\n}\n",
            "Tests/Runtime/RenamedChecks.cs": "namespace Fixture.Tests;\npublic static class RenamedValidation { " + probe + " }\n",
            "Tests/Runtime/EnclosingChecks.cs": "namespace Fixture\n{\n    public static partial class EnclosingValidation { " + probe + " }\n}\n",
            "Tests/Runtime/OtherValidation.cs": "namespace Fixture.Tests\n{\n    public static partial class RenamedValidation { " + probe + " }\n}\n",
            "Tests/Runtime/CommentForged.cs": "// public static partial class PartialValidation\npublic static class ForgedHolder { const string Name = \"class PartialValidation { }\"; " + probe + " }\n",
            "Tests/Runtime/ForeignNamespace.cs": "namespace Fixture.Tests.Other\n{\n    public static partial class PartialValidation { " + probe + " }\n}\n",
            "Tests/Runtime/NestedType.cs": "namespace Fixture.Tests\n{\n    public static class Holder { public static partial class PartialValidation { " + probe + " } }\n}\n",
            "Tests/Runtime/InactiveDeclaration.cs": "#if false\npublic static partial class PartialValidation\n#else\npublic static class InactiveHolder\n#endif\n{ " + probe + " }\n",
            "Tests/Runtime/AmbiguousTypes.cs": "public static class RenamedValidation { " + probe + " }\npublic static class EnclosingValidation { " + probe + " }\n",
            "Tests/Unit/PartialHelper.cs": "public static partial class PartialValidation { " + probe + " }\n",
            "Tests/Runtime/MultiValidation.cs": "namespace Fixture.Tests\n{\n    public static partial class MultiValidation { " + probe + " }\n}\n",
            "Tests/Runtime/Decomposed/MultiValidation/VerifyMulti.cs": "namespace Fixture.Tests\n{\n    public static partial class MultiValidation { " + probe + " }\n}\n",
            "Tests/Runtime/VerbatimDirective.cs": "public static class VerbatimHolder { const string Text = @\"\n#if false\n\"; " + probe + " }\nnamespace Fixture.Tests\n{\n    public static partial class PartialValidation { " + probe + " }\n}\n",
            "Tests/Runtime/CommentDirective.cs": "/*\n#if false\n*/\nnamespace Fixture.Tests\n{\n    public static partial class PartialValidation { " + probe + " }\n}\n",
        }
        for relative, text in files.items():
            path = root / "Packages/fixture" / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(text, encoding="utf-8")
        subprocess.run(["git", "-C", str(root), "add", "-A"], check=True)
        subprocess.run(["git", "-C", str(root), "commit", "-q", "-m", "declared types"], check=True)
        return root

    def _flags_by_file(self, rows) -> dict[str, set[str]]:
        """Group inventory validation flags by fixture-relative file path."""
        flags: dict[str, set[str]] = {}
        for row in rows:
            flags.setdefault(str(row["file"]).removeprefix("Packages/fixture/"), set()).add(str(row["validation_flag"]))
        return flags

    def test_validation_flag_maps_declared_registered_type_only(self):
        """Use the active top-level declared type when the file stem is not registered."""
        expected = {
            "Tests/Runtime/FixtureValidation.cs": {"--phase-fixture"},
            "Tests/Runtime/Decomposed/PartialValidation/VerifyPartial.cs": {"--phase-partial"},
            "Tests/Runtime/RenamedChecks.cs": {"--phase-renamed"},
            "Tests/Runtime/EnclosingChecks.cs": {"--phase-enclosing"},
            "Tests/Runtime/OtherValidation.cs": {"--phase-other"},
            "Tests/Runtime/CommentForged.cs": {""},
            "Tests/Runtime/ForeignNamespace.cs": {""},
            "Tests/Runtime/NestedType.cs": {""},
            "Tests/Runtime/InactiveDeclaration.cs": {""},
            "Tests/Runtime/AmbiguousTypes.cs": {""},
            "Tests/Unit/PartialHelper.cs": {""},
            "Tests/Runtime/MultiValidation.cs": {"--phase-multi-a"},
            "Tests/Runtime/Decomposed/MultiValidation/VerifyMulti.cs": {""},
            "Tests/Runtime/VerbatimDirective.cs": {"--phase-partial"},
            "Tests/Runtime/CommentDirective.cs": {"--phase-partial"},
        }
        with tempfile.TemporaryDirectory() as directory:
            root = self._declared_type_fixture(directory)
            self.assertEqual(expected, self._flags_by_file(_gate_module.inventory(root)))
            code, output = self._run_fixture_gate(root)
            self.assertEqual(0, code, output)
            with (root / "build/inventory/source_text_inventory.csv").open(encoding="utf-8", newline="") as handle:
                self.assertEqual(expected, self._flags_by_file(csv.DictReader(handle)))
            product = root / "Packages/fixture/Runtime/Product.cs"
            product.write_text(product.read_text(encoding="utf-8") + "// unrelated edit\n", encoding="utf-8")
            self.assertEqual(expected, self._flags_by_file(_gate_module.inventory(root)))
