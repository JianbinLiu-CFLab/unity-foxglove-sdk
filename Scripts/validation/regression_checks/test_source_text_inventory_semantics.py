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
        code, output = self._reader_gate(
            "",
            f"var source = SourceReaders.Read({product});\n{check}",
            {"SourceReaders.cs": "namespace Fixture.Readers {\n    internal static class SourceReaders {\n"
             "        internal static string Read(string relativePath) => System.IO.File.ReadAllText(relativePath);\n    }\n}\n"},
            prelude="using Fixture.Readers;\n",
        )
        self.assertNotEqual(0, code, output)
        self.assertRegex(output, r"SOURCE_TEXT_RATCHET_CLASS_C_INCREASE")

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
