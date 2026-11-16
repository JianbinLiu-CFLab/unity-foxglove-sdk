"""Additional source inventory semantic regression fixtures."""

from __future__ import annotations

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
