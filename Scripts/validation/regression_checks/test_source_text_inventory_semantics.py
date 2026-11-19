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
