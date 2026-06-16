#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Regression tests for Unity package validator hygiene checks.

from __future__ import annotations

import importlib.util
import sys
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[3]
VALIDATE_PACKAGE_PATH = ROOT / "Scripts" / "package" / "validate_unity_package.py"


def load_module(name: str, path: Path):
    """Load a Python script as an isolated module."""
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


class ValidatePackageTests(unittest.TestCase):
    """Regression coverage for package validator hygiene checks."""

    def setUp(self) -> None:
        """Load a fresh validate_unity_package module for each test."""
        self.validator = load_module("validate_unity_package_under_test", VALIDATE_PACKAGE_PATH)

    def test_sample_meta_checks_asmdef_files(self) -> None:
        """Sample asmdef files need stable Unity .meta sidecars."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            sample = root / "Samples~" / "Demo"
            sample.mkdir(parents=True)
            (sample / "Demo.asmdef").write_text("{}", encoding="utf-8")

            self.validator.SAMPLES = root / "Samples~"
            results = []
            self.validator.check_sample_meta(results)

        self.assertFalse(results[-1].ok)
        self.assertIn("Demo.asmdef", results[-1].detail)

    def test_forbidden_sample_artifacts_reports_root_directory_once(self) -> None:
        """A forbidden directory should not flood diagnostics with descendants."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            library = root / "Samples~" / "BasicVisualization" / "Library"
            nested = library / "Sub"
            nested.mkdir(parents=True)
            (nested / "cache.bin").write_bytes(b"cache")

            self.validator.SAMPLES = root / "Samples~"
            results = []
            self.validator.check_forbidden_sample_artifacts(results)

        self.assertFalse(results[-1].ok)
        offenders = [item.strip() for item in results[-1].detail.split(";")]
        self.assertEqual(1, len(offenders))
        self.assertTrue(offenders[0].endswith("Samples~/BasicVisualization/Library"))


if __name__ == "__main__":
    unittest.main()
