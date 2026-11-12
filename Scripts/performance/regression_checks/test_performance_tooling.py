#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Regression tests for performance helper diagnostics.

from __future__ import annotations

import contextlib
import importlib.util
import io
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

from Scripts.validation.plan_audit import performance_evidence


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

    def test_performance_runner_rejects_empty_or_missing_scenarios(self) -> None:
        """A successful process with no scenarios must fail closed."""
        for case, payload in (
            ("empty", {"scenarios": []}),
            ("missing", {}),
        ):
            with self.subTest(case=case):
                module = load_module(
                    f"performance_runner_no_scenarios_{case}",
                    "Scripts/performance/run_baseline.py",
                )
                with tempfile.TemporaryDirectory() as temp:
                    output = Path(temp)
                    result_path = output / "phase35_performance_current.json"
                    argv = [
                        "run_baseline.py",
                        "--quick",
                        "--output",
                        str(output),
                        "--timeout-minutes",
                        "0",
                    ]

                    def fake_run(cmd, **kwargs):
                        """Emit the selected structurally incomplete result."""
                        result_path.write_text(json.dumps(payload), encoding="utf-8")
                        return subprocess.CompletedProcess(cmd, 0)

                    stdout = io.StringIO()
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
                    ), contextlib.redirect_stdout(stdout):
                        result = module.main()

                self.assertEqual(module.EXIT_FAILURE, result)
                self.assertIn("No scenarios", stdout.getvalue())

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

    def test_performance_runner_rejects_malformed_scenario_values(self) -> None:
        """Scenario entries must retain typed status and finite numeric metrics."""
        cases = (
            ("string-status", {"name": "bad", "passed": "false"}),
            ("null-entry", None),
            ("non-finite", {"name": "bad", "passed": True, "messagesPerSecond": float("nan")}),
            ("oversized", {"name": "bad", "passed": True, "messagesPerSecond": 10**400}),
            ("negative-throughput", {"name": "bad", "passed": True, "messagesPerSecond": -1}),
            ("negative-collections", {"name": "bad", "passed": True, "gen0Collections": -1}),
            ("bad-component", {"name": "bad", "passed": True, "components": [{"name": "component", "dropRatio": 2}]}),
            (
                "bad-frame-evidence",
                {
                    "name": "WsSendQueueLargeFrame",
                    "passed": True,
                    "frameEvidence": [
                        {
                            "sizeBytes": "bad",
                            "queuedBytes": -1,
                            "controlDequeuedFirst": "yes",
                            "droppedDataFrames": 0,
                            "droppedDataFramesSnapshot": 0,
                            "dropRatio": 0,
                        }
                    ],
                },
            ),
        )
        for case, scenario in cases:
            with self.subTest(case=case):
                module = load_module(f"performance_runner_malformed_{case}", "Scripts/performance/run_baseline.py")
                with tempfile.TemporaryDirectory() as temp:
                    output = Path(temp)
                    result_path = output / "phase35_performance_current.json"
                    argv = ["run_baseline.py", "--quick", "--output", str(output), "--timeout-minutes", "0"]

                    def fake_run(cmd, **kwargs):
                        """Write the controlled malformed result for this case."""
                        result_path.write_text(
                            json.dumps({"scenarios": [scenario]}, allow_nan=True),
                            encoding="utf-8",
                        )
                        return subprocess.CompletedProcess(cmd, 0)

                    stdout = io.StringIO()
                    with mock.patch.object(module.sys, "argv", argv), mock.patch.object(
                        module, "_free_disk_bytes", return_value=10 * module.BYTES_PER_GIB
                    ), mock.patch.object(module, "_setup_nuget_cache", return_value={}), mock.patch.object(
                        module, "_run_owned", side_effect=fake_run
                    ), contextlib.redirect_stdout(stdout):
                        result = module.main()

                self.assertEqual(module.EXIT_FAILURE, result)
                self.assertIn("invalid scenario", stdout.getvalue())

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

    def test_performance_workflow_pins_ci_thresholds_and_baseline_command(self) -> None:
        """Keep the report-only performance lane wired to its CI thresholds."""
        workflow = (ROOT / ".github" / "workflows" / "performance.yml").read_text(encoding="utf-8")
        self.assertIn("Scripts/performance/run_baseline.py --quick", workflow)
        self.assertIn("performance-thresholds.ci.json", workflow)
        self.assertIn("upload-artifact", workflow)
        self.assertIn("Runtime/Transport/**", workflow)
        self.assertIn("Runtime/Components/FoxRun/**", workflow)
        self.assertIn("Runtime/Core/**", workflow)
        self.assertIn("Runtime/Components/Manager/**", workflow)
        self.assertIn("Runtime/Protocol/**", workflow)
        self.assertIn("Runtime/Schemas/**", workflow)
        self.assertIn("Runtime/IO/**", workflow)
        self.assertIn("Editor/Shared/FoxgloveSourceEmitter/**", workflow)
        self.assertIn("Editor/SourceGenerators/**", workflow)
        self.assertIn("permissions:", workflow)
        self.assertIn("contents: read", workflow)
        self.assertIn("persist-credentials: false", workflow)

    def test_plan_audit_requires_protocol_trigger_path(self) -> None:
        """The audit must fail if a benchmark-relevant workflow path is removed."""
        workflow = (ROOT / ".github" / "workflows" / "performance.yml").read_text(encoding="utf-8")
        self.assertTrue(performance_evidence._workflow_contract(workflow))
        mutated = workflow.replace(
            "      - 'Packages/dev.unity2foxglove.sdk/Runtime/Protocol/**'\n",
            "",
        )
        self.assertFalse(performance_evidence._workflow_contract(mutated))

    def test_all_plan199_scenarios_have_explicit_quick_and_full_thresholds(self) -> None:
        """Keep every high-bandwidth scenario explicitly bounded in both modes."""
        expected = {
            "FoxRunStreamIngest",
            "FoxRunPublishPayload",
            "WsSendQueueLargeFrame",
            "LargePayloadFanout",
            "FoxRunStreamIngest.Default",
            "FoxRunStreamIngest.Unbounded",
            "FoxRunPublishPayload.Json.1",
            "FoxRunPublishPayload.Json.4",
            "FoxRunPublishPayload.Protobuf.1",
            "FoxRunPublishPayload.Protobuf.4",
            "FoxRunPublishPayload.MessagePack.1",
            "FoxRunPublishPayload.MessagePack.4",
            "LargePayloadFanout.WebSocket.1",
            "LargePayloadFanout.WebSocket.4",
            "LargePayloadFanout.Mcap",
        }
        for filename in ("performance-thresholds.json", "performance-thresholds.ci.json"):
            data = json.loads((ROOT / "Packages/dev.unity2foxglove.sdk/Tests/Performance" / filename).read_text(encoding="utf-8"))
            self.assertTrue(expected.issubset(data["scenarios"]))
            for mode in ("quick", "full"):
                self.assertTrue(expected.issubset(data["modes"][mode]["scenarios"]))


if __name__ == "__main__":
    unittest.main()
