#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Regression tests for performance helper diagnostics.

from __future__ import annotations

import contextlib
import importlib.util
import io
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock


ROOT = Path(__file__).resolve().parents[3]


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
    finally:
        sys.path[:] = original_path
    return module


class PerformanceToolingTests(unittest.TestCase):
    """Regression coverage for performance helper tooling."""

    def test_performance_runner_reports_malformed_result_json_cleanly(self) -> None:
        """Malformed performance output should return failure without traceback noise."""
        module = load_module("performance_runner_under_test", "Scripts/performance/run_baseline.py")

        with tempfile.TemporaryDirectory() as temp:
            output = Path(temp)
            (output / "phase35_performance_999.json").write_text("{not-json", encoding="utf-8")
            argv = ["run_baseline.py", "--quick", "--output", str(output), "--timeout-minutes", "0"]
            stdout = io.StringIO()
            completed = subprocess.CompletedProcess(args=["dotnet"], returncode=0)
            run_calls = []

            def fake_run(cmd, **kwargs):
                run_calls.append(cmd)
                return completed

            with mock.patch.object(module.sys, "argv", argv):
                with mock.patch.object(module, "_free_disk_bytes", return_value=10 * module.BYTES_PER_GIB):
                    with mock.patch.object(module, "_setup_nuget_cache", return_value={}):
                        with mock.patch.object(module.subprocess, "run", side_effect=fake_run):
                            with contextlib.redirect_stdout(stdout):
                                result = module.main()

        self.assertEqual(module.EXIT_FAILURE, result)
        self.assertIn("malformed result JSON", stdout.getvalue())
        self.assertIn("--result-prefix", run_calls[0])
        self.assertIn(module.RESULT_FILE_PREFIX, run_calls[0])


if __name__ == "__main__":
    unittest.main()
