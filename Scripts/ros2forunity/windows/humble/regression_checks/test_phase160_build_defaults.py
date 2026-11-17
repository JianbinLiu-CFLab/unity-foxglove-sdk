#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Regression tests for R2FU Humble Windows build root defaults.

from __future__ import annotations

import importlib.util
import inspect
import os
import signal
import sys
import tempfile
import time
import unittest
from unittest import mock
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

    def test_rejects_all_cmd_metacharacters_in_vsdev_path(self) -> None:
        """Every cmd.exe metacharacter embedded by the script is rejected."""
        module = load_build_module()

        for character in ("&", "|", "^", "<", ">", "\r", "\n"):
            with self.subTest(character=repr(character)):
                with self.assertRaises(module.Phase160Error):
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
                    "import sys,time;sys.stdout.write('partial');sys.stdout.flush();time.sleep(30)",
                ],
                cwd=root,
                env=os.environ.copy(),
                log_file=root / "command.log",
                timeout=0.2,
            )

        self.assertEqual(124, result.exit_code)
        self.assertIn("partial", result.output)
        self.assertIn("COMMAND_TIMEOUT", result.output)
        self.assertLess(time.monotonic() - started, 10.0)

    def test_run_command_keeps_output_the_reader_delivers_after_exit(self) -> None:
        """Lines still in flight when the child exits must reach the command result."""
        module = load_build_module()
        original_queue = module.queue.Queue

        class SlowQueue(original_queue):
            """Delay delivery so the child exits before its last line is queued."""

            def put(self, item, block=True, timeout=None):
                """Queue one line after a delay."""
                time.sleep(0.3)
                super().put(item, block, timeout)

        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            with mock.patch.object(module.queue, "Queue", SlowQueue):
                result = module.run_command(
                    [sys.executable, "-c", "print('final-line')"],
                    cwd=root,
                    env=os.environ.copy(),
                    log_file=root / "command.log",
                    timeout=30,
                )
            log_text = (root / "command.log").read_text(encoding="utf-8")

        self.assertEqual(0, result.exit_code)
        self.assertIn("final-line", result.output)
        self.assertIn("final-line", log_text)

    def test_non_windows_process_cleanup_uses_the_owned_process_group(self) -> None:
        """Non-Windows timeout cleanup must terminate the owned process group."""
        module = load_build_module()

        with mock.patch.object(module.os, "name", "posix"):
            with mock.patch.object(module.os, "killpg", create=True) as killpg:
                module.kill_process_tree_windows(4321)

        killpg.assert_called_once_with(
            4321,
            getattr(signal, "SIGKILL", signal.SIGTERM),
        )


if __name__ == "__main__":
    unittest.main()
