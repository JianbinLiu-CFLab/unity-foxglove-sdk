#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Regression tests for local-entrypoint validation diagnostics.

from __future__ import annotations

import importlib.util
import re
import sys
import unittest
from pathlib import Path
from unittest import mock


ROOT = Path(__file__).resolve().parents[3]
VALIDATOR_PATH = ROOT / "Scripts/package/validate_local_entrypoints.py"


def load_module(name: str, path: Path):
    """Load one validation script as an isolated module."""
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


class LocalEntrypointValidationTests(unittest.TestCase):
    """Lock deterministic failures for missing validation dependencies."""

    def test_missing_git_reports_an_actionable_error(self) -> None:
        """A missing git executable should not escape as a raw traceback."""
        validator = load_module(
            "validate_local_entrypoints_under_test",
            VALIDATOR_PATH,
        )
        with mock.patch.object(
            validator.subprocess,
            "run",
            side_effect=FileNotFoundError("git"),
        ):
            with self.assertRaisesRegex(RuntimeError, "git executable"):
                validator.git_grep_failures("label", re.compile("pattern"))


if __name__ == "__main__":
    unittest.main()
