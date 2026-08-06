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
        sys.modules.pop(spec.name, None)
    return module


class PerformanceToolingTests(unittest.TestCase):
    """Regression coverage for performance helper tooling."""

    def test_performance_runner_reports_malformed_result_json_cleanly(self) -> None:
        """Malformed performance output should return failure without traceback noise."""
        module = load_module("performance_runner_under_test", "Scripts/performance/run_baseline.py")

        with tempfile.TemporaryDirectory() as temp:
            output = Path(temp)
            (output / "phase35_performance_999.json").write_text(
                '{"scenarios":[]}',
                encoding="utf-8",
            )
            argv = ["run_baseline.py", "--quick", "--output", str(output), "--timeout-minutes", "0"]
            stdout = io.StringIO()
            stderr = io.StringIO()
            completed = subprocess.CompletedProcess(args=["dotnet"], returncode=0)
            run_calls = []

            def fake_run(cmd, **kwargs):
                """Capture subprocess invocations without launching dotnet."""
                run_calls.append((cmd, kwargs))
                (output / "phase35_performance_999.json").write_text(
                    "{not-json",
                    encoding="utf-8",
                )
                return completed

            with mock.patch.object(module.sys, "argv", argv):
                with mock.patch.object(module, "_free_disk_bytes", return_value=10 * module.BYTES_PER_GIB):
                    with mock.patch.object(module, "_setup_nuget_cache", return_value={}):
                        with mock.patch.object(module, "_run_owned", side_effect=fake_run):
                            with contextlib.redirect_stdout(stdout), contextlib.redirect_stderr(stderr):
                                result = module.main()

        self.assertEqual(module.EXIT_FAILURE, result)
        self.assertIn("malformed result JSON", stdout.getvalue())
        self.assertNotIn("Traceback", stderr.getvalue())
        command, kwargs = run_calls[0]
        self.assertIn("--result-prefix", command)
        self.assertIn(module.RESULT_FILE_PREFIX, command)
        self.assertEqual(module.REPO_ROOT, kwargs["cwd"])
        self.assertEqual({}, kwargs["env"])
        self.assertIsNone(kwargs["timeout"])

    def test_performance_runner_ignores_stale_lexicographically_later_json(self) -> None:
        """Summary selection must use output from the current invocation only."""
        module = load_module(
            "performance_runner_stale_result_under_test",
            "Scripts/performance/run_baseline.py",
        )
        with tempfile.TemporaryDirectory() as temp:
            output = Path(temp)
            stale = output / "phase35_performance_zzzz.json"
            stale.write_text("{not-json", encoding="utf-8")
            argv = ["run_baseline.py", "--quick", "--output", str(output)]

            def fake_run(cmd, **kwargs):
                """Create the current result with an earlier lexical name."""
                current = output / "phase35_performance_aaaa.json"
                current.write_text(
                    '{"scenarios":[{"name":"current","passed":true}]}',
                    encoding="utf-8",
                )
                return subprocess.CompletedProcess(cmd, 0)

            with mock.patch.object(module.sys, "argv", argv), mock.patch.object(
                module,
                "_free_disk_bytes",
                return_value=10 * module.BYTES_PER_GIB,
            ), mock.patch.object(
                module,
                "_setup_nuget_cache",
                return_value={},
            ), mock.patch.object(
                module,
                "_run_owned",
                side_effect=fake_run,
            ), contextlib.redirect_stdout(io.StringIO()):
                result = module.main()

        self.assertEqual(module.EXIT_SUCCESS, result)

    def test_owned_run_terminates_the_process_tree_on_timeout(self) -> None:
        """A benchmark timeout must clean up descendants, not only dotnet."""
        module = load_module(
            "performance_runner_timeout_under_test",
            "Scripts/performance/run_baseline.py",
        )
        process = mock.Mock()
        process.wait.side_effect = subprocess.TimeoutExpired(["dotnet"], 1)
        process.pid = 4242
        with mock.patch.object(module.subprocess, "Popen", return_value=process), mock.patch.object(
            module,
            "_terminate_owned_process",
        ) as terminate:
            with self.assertRaises(subprocess.TimeoutExpired):
                module._run_owned(
                    ["dotnet"],
                    cwd=module.REPO_ROOT,
                    env={},
                    timeout=1,
                )

        terminate.assert_called_once_with(process)


if __name__ == "__main__":
    unittest.main()
