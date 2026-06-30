#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Regression tests for R2FU Humble Windows build root defaults.

from __future__ import annotations

import importlib.util
import sys
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[5]
SCRIPT_PATH = ROOT / "Scripts" / "ros2forunity" / "windows" / "humble" / "phase160_r2fu_humble_windows_build.py"
EXPECTED_ROOT = ROOT / "build" / "r2fu-humble-win64"


def load_build_module():
    """Load the Phase 160 build script as a Python module."""
    spec = importlib.util.spec_from_file_location("phase160_r2fu_humble_windows_build", SCRIPT_PATH)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


class Phase160BuildDefaultsTests(unittest.TestCase):
    """Regression coverage for R2FU Humble Windows build defaults."""

    def test_default_roots_stay_under_consolidated_build_root(self) -> None:
        """Default temporary roots stay under the consolidated build directory."""
        module = load_build_module()

        args = module.parse_args([])
        work_root = Path(args.work_root)
        temp_root = Path(args.temp_root)

        self.assertEqual(EXPECTED_ROOT / "work", work_root)
        self.assertEqual(EXPECTED_ROOT / "tmp", temp_root)
        self.assertTrue(str(work_root).endswith("build/r2fu-humble-win64/work") or str(work_root).endswith("build\\r2fu-humble-win64\\work"))
        self.assertTrue(str(temp_root).endswith("build/r2fu-humble-win64/tmp") or str(temp_root).endswith("build\\r2fu-humble-win64\\tmp"))

    def test_rejects_cmd_percent_expansion_in_vsdev_path(self) -> None:
        """VsDevCmd.bat shell embedding should reject percent expansion."""
        module = load_build_module()

        with self.assertRaises(module.Phase160Error):
            module.reject_cmd_shell_unsafe_path("VsDevCmd.bat", Path(r"C:\Tools\%COMSPEC%\VsDevCmd.bat"))


if __name__ == "__main__":
    unittest.main()
