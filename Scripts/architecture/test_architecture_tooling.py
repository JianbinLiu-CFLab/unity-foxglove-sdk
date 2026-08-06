#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Regression tests for architecture analysis helpers.

from __future__ import annotations

import contextlib
import importlib.util
import io
import sys
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]


def load_module(name: str, relative: str):
    """Load one repository helper script as an isolated module."""
    path = ROOT / relative
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    sys.modules[spec.name] = module
    original_path = list(sys.path)
    sys.path.insert(0, str(path.parent))
    try:
        spec.loader.exec_module(module)
    except Exception:
        sys.modules.pop(spec.name, None)
        raise
    finally:
        sys.path[:] = original_path
    return module


class ArchitectureToolingTests(unittest.TestCase):
    """Regression coverage for architecture helper tooling."""

    def test_asmdef_cycle_detection_handles_deep_graphs_without_recursion_error(self) -> None:
        """Architecture coupling analysis should tolerate deep acyclic graphs."""
        module = load_module("analyze_coupling_under_test", "Scripts/architecture/analyze_coupling.py")
        metrics = [
            module.AsmdefMetric(path=f"{index}.asmdef", name=f"A{index}", references=[f"A{index + 1}"])
            for index in range(1100)
        ]
        metrics.append(module.AsmdefMetric(path="1100.asmdef", name="A1100", references=[]))

        cycles = module.find_asmdef_cycles(metrics)

        self.assertEqual([], cycles)

    def test_registry_default_test_parse_warns_when_registry_shape_is_unrecognized(self) -> None:
        """A registry parse miss should be visible rather than disabling boundary checks."""
        module = load_module("analyze_coupling_registry_under_test", "Scripts/architecture/analyze_coupling.py")

        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            registry = root / "Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs"
            registry.parent.mkdir(parents=True)
            registry.write_text("DefaultValidation(typeof(Phase1Validation));\n", encoding="utf-8")
            stderr = io.StringIO()
            with contextlib.redirect_stderr(stderr):
                files = module.find_registry_default_test_files(root)

        self.assertEqual(set(), files)
        self.assertIn("warning", stderr.getvalue().lower())

    def test_asmdef_collection_reports_non_object_json_without_crashing(self) -> None:
        """Syntactically valid non-object JSON is still an invalid asmdef input."""
        module = load_module("analyze_coupling_asmdef_under_test", "Scripts/architecture/analyze_coupling.py")

        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            asmdef = root / "Packages" / "Broken.asmdef"
            asmdef.parent.mkdir(parents=True)
            asmdef.write_text("[]\n", encoding="utf-8")

            metrics = module.collect_asmdef_metrics(root, ["Packages/Broken.asmdef"])

        self.assertEqual(1, len(metrics))
        self.assertEqual("<invalid-json-object>", metrics[0].name)
        self.assertEqual([], metrics[0].references)


if __name__ == "__main__":
    unittest.main()
