#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Regression tests for R2FU Jazzy Windows build root defaults.

from __future__ import annotations

import importlib.util
import sys
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
SCRIPT_PATH = ROOT / "Scripts" / "smoke" / "phase138b_r2fu_jazzy_windows_build.py"
EXPECTED_ROOT = Path(r"D:\ros2unity\.build\r2fu-jazzy-win64")


def load_build_module():
    """Load the Phase 138B build script as a Python module."""
    spec = importlib.util.spec_from_file_location("phase138b_r2fu_jazzy_windows_build", SCRIPT_PATH)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


class Phase138BBuildDefaultsTests(unittest.TestCase):
    """Regression coverage for R2FU Jazzy Windows build defaults."""

    def test_default_roots_stay_under_consolidated_build_root(self) -> None:
        """Default temporary roots stay under the consolidated build directory."""
        module = load_build_module()

        args = module.parse_args([])
        work_root = Path(args.work_root)
        temp_root = Path(args.temp_root)

        self.assertEqual(EXPECTED_ROOT / "work", work_root)
        self.assertEqual(EXPECTED_ROOT / "tmp", temp_root)
        self.assertNotEqual(Path(r"D:\r"), work_root)
        self.assertNotEqual(Path(r"D:\t"), temp_root)


if __name__ == "__main__":
    unittest.main()
