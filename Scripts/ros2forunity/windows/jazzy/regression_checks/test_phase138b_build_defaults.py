#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Regression tests for R2FU Jazzy Windows build root defaults.

from __future__ import annotations

import importlib.util
import inspect
import os
import sys
import tempfile
import time
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[5]
SCRIPT_PATH = ROOT / "Scripts" / "ros2forunity" / "windows" / "jazzy" / "phase138b_r2fu_jazzy_windows_build.py"
EXPECTED_ROOT = ROOT / "build" / "r2fu-jazzy-win64"


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
        self.assertTrue(str(work_root).endswith("build/r2fu-jazzy-win64/work") or str(work_root).endswith("build\\r2fu-jazzy-win64\\work"))
        self.assertTrue(str(temp_root).endswith("build/r2fu-jazzy-win64/tmp") or str(temp_root).endswith("build\\r2fu-jazzy-win64\\tmp"))

    def test_rejects_cmd_percent_expansion_in_vsdev_path(self) -> None:
        """VsDevCmd.bat shell embedding should reject percent expansion."""
        module = load_build_module()

        with self.assertRaises(module.Phase138BError):
            module.reject_cmd_shell_unsafe_path("VsDevCmd.bat", Path(r"C:\Tools\%COMSPEC%\VsDevCmd.bat"))

    def test_rejects_all_cmd_metacharacters_in_vsdev_path(self) -> None:
        """Every cmd.exe metacharacter embedded by the script is rejected."""
        module = load_build_module()

        for character in ("&", "|", "^", "<", ">", "\r", "\n"):
            with self.subTest(character=repr(character)):
                with self.assertRaises(module.Phase138BError):
                    module.reject_cmd_shell_unsafe_path(
                        "VsDevCmd.bat",
                        Path("C:/Tools/unsafe" + character + "path/VsDevCmd.bat"),
                    )

    def test_run_command_timeout_is_defaulted_and_independent_of_newlines(self) -> None:
        """A child stalled after partial output cannot block the wall-clock deadline."""
        module = load_build_module()
        self.assertEqual(
            module.DEFAULT_COMMAND_TIMEOUT_SECONDS,
            inspect.signature(module.run_command).parameters["timeout"].default,
        )
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            started = time.monotonic()
            result = module.run_command(
                [
                    sys.executable,
                    "-c",
                    "import sys,time;sys.stdout.write('partial');sys.stdout.flush();time.sleep(5)",
                ],
                cwd=root,
                env=os.environ.copy(),
                log_file=root / "command.log",
                timeout=0.2,
            )

        self.assertEqual(124, result.exit_code)
        self.assertIn("partial", result.output)
        self.assertIn("COMMAND_TIMEOUT", result.output)
        self.assertLess(time.monotonic() - started, 3.0)


if __name__ == "__main__":
    unittest.main()
