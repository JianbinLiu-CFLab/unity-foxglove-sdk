#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Regression tests for the focused Phase186-H manual launcher."""

from __future__ import annotations

import pathlib
import subprocess
import sys
import tempfile
import threading
import unittest
from unittest import mock


from Scripts.smoke.foxrun import phase186_bridge_manual as manual
from Scripts.smoke.foxrun import phase186_bridge_manual_status as status


class FakeClock:
    """A deterministic monotonic-clock seam."""

    def __init__(self) -> None:
        self.value = 100.0

    def __call__(self) -> float:
        return self.value

    def advance(self, seconds: float) -> None:
        self.value += seconds


class ManualLauncherTests(unittest.TestCase):
    """Keep the short user-facing surface immutable and non-live."""

    def test_aliases_are_exactly_the_two_locked_manual_cases(self) -> None:
        self.assertEqual(
            {
                "jazzy": (
                    "manual-jazzy-fastrtps-duplex",
                    pathlib.Path("build/phase186/manual/jazzy-fastrtps"),
                ),
                "zenoh": (
                    "manual-lyrical-zenoh-duplex",
                    pathlib.Path("build/phase186/manual/lyrical-zenoh"),
                ),
            },
            manual.MANUAL_ALIASES,
        )

    def test_usage_rejects_missing_unknown_and_extra_alias_without_coordinator(self) -> None:
        for argv in ([], ["humble"], ["jazzy", "zenoh"]):
            with self.subTest(argv=argv), mock.patch.object(manual.acceptance, "main") as coordinator:
                with self.assertRaises(SystemExit) as raised:
                    manual.main(argv)
                self.assertEqual(manual.EXIT_USAGE, raised.exception.code)
                coordinator.assert_not_called()

    def test_launcher_forwards_immutable_jazzy_arguments_and_exit_code(self) -> None:
        with mock.patch.object(manual.acceptance, "main", return_value=7) as coordinator:
            self.assertEqual(7, manual.main(["jazzy"]))

        args, kwargs = coordinator.call_args
        self.assertEqual(
            [
                "--case",
                "manual-jazzy-fastrtps-duplex",
                "--manual",
                "--manual-timeout-seconds",
                "1800",
                "--output-root",
                "build/phase186/manual/jazzy-fastrtps",
            ],
            args[0],
        )
        self.assertTrue(kwargs["resolve_current_head"])
        self.assertIsInstance(kwargs["status"], status.ManualStatusReporter)
        kwargs["status"].close()

    def test_launcher_can_import_from_outside_the_repository(self) -> None:
        repository = pathlib.Path(__file__).resolve().parents[4]
        script_directory = repository / "Scripts" / "smoke" / "foxrun"
        probe = """
import importlib
import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(sys.argv[1]).resolve()))
importlib.import_module("phase186_bridge_manual")
"""
        with tempfile.TemporaryDirectory() as temporary:
            completed = subprocess.run(
                [sys.executable, "-I", "-c", probe, str(script_directory)],
                cwd=temporary,
                capture_output=True,
                text=True,
                check=False,
            )
        self.assertEqual(0, completed.returncode, completed.stderr)


class ManualStatusReporterTests(unittest.TestCase):
    """Prove the operator output is concise, bounded, and stoppable."""

    def test_transition_suppresses_an_unchanged_stage_and_message(self) -> None:
        emitted: list[str] = []
        reporter = status.ManualStatusReporter(
            sink=emitted.append,
            heartbeat_seconds=60.0,
        )
        reporter.transition("PREPARING", "Preparing the manual Bridge session.")
        reporter.transition("PREPARING", "Preparing the manual Bridge session.")
        reporter.transition("PREPARING", "Waiting for Unity.")
        reporter.transition("UNITY_READY", "Waiting for Unity.")
        reporter.close()

        transitions = [line for line in emitted if " transition " in line]
        self.assertEqual(3, len(transitions))
        self.assertIn("message=Waiting for Unity.", transitions[1])
        self.assertIn("stage=UNITY_READY", transitions[2])

    def test_transition_is_immediate_and_heartbeat_repeats_the_current_stage(self) -> None:
        clock = FakeClock()
        emitted: list[str] = []
        first_wait = threading.Event()
        release = threading.Event()

        def wait(_stop: threading.Event, seconds: float) -> bool:
            self.assertEqual(10.0, seconds)
            if _stop.is_set():
                return True
            first_wait.set()
            release.wait(1.0)
            if _stop.is_set():
                return True
            clock.advance(10.0)
            release.clear()
            return False

        reporter = status.ManualStatusReporter(
            clock=clock,
            sink=emitted.append,
            heartbeat_seconds=10.0,
            wait=wait,
        )
        reporter.transition("PREPARING", "Preparing the manual Bridge session.")
        self.assertTrue(first_wait.wait(1.0))
        release.set()
        for _ in range(100):
            if any(
                "heartbeat stage=PREPARING elapsed=10s "
                "message=Preparing the manual Bridge session." in line
                for line in emitted
            ):
                break
            threading.Event().wait(0.005)
        reporter.close()

        self.assertEqual(1, sum("transition stage=PREPARING" in line for line in emitted))
        self.assertTrue(
            any(
                "heartbeat stage=PREPARING elapsed=10s "
                "message=Preparing the manual Bridge session." in line
                for line in emitted
            )
        )
        self.assertFalse(reporter.is_alive)

    def test_readiness_is_two_short_unity_actions_and_close_is_idempotent(self) -> None:
        emitted: list[str] = []
        reporter = status.ManualStatusReporter(
            sink=emitted.append,
            heartbeat_seconds=60.0,
        )
        reporter.unity_prepare("Phase186 Bridge Manual")
        reporter.unity_play_ready("Phase186 Bridge Manual")
        reporter.detail("Waiting for the token-bound completion marker.")
        reporter.close()
        reporter.close()

        actions = [line for line in emitted if line.startswith("UNITY ACTION")]
        self.assertEqual(
            [
                "UNITY ACTION 1: Foxglove > Manual Acceptance > Phase186 > "
                "Prepare Current Bridge Run",
                "UNITY ACTION 2: Enter Play Mode once",
            ],
            actions,
        )
        self.assertTrue(all(len(action) < 120 for action in actions))
        self.assertTrue(any("detail Waiting for the token-bound" in line for line in emitted))

    def test_detail_updates_the_stage_heartbeat_message(self) -> None:
        clock = FakeClock()
        emitted: list[str] = []
        first_wait = threading.Event()
        release = threading.Event()

        def wait(stop: threading.Event, _seconds: float) -> bool:
            if stop.is_set():
                return True
            first_wait.set()
            release.wait(1.0)
            if stop.is_set():
                return True
            clock.advance(10.0)
            return False

        reporter = status.ManualStatusReporter(
            clock=clock,
            sink=emitted.append,
            heartbeat_seconds=10.0,
            wait=wait,
        )
        reporter.transition("4/5", "Unity scene compiled; Play Mode is now ready")
        self.assertTrue(first_wait.wait(1.0))
        reporter.detail("local B published; waiting for peer verification")
        release.set()
        for _ in range(100):
            if any("heartbeat stage=4/5" in line for line in emitted):
                break
            threading.Event().wait(0.005)
        reporter.close()

        self.assertTrue(
            any(
                "heartbeat stage=4/5 elapsed=10s "
                "message=local B published; waiting for peer verification" in line
                for line in emitted
            )
        )

    def test_terminal_handoff_has_verdict_evidence_and_exact_next_action(self) -> None:
        emitted: list[str] = []
        reporter = status.ManualStatusReporter(
            sink=emitted.append,
            heartbeat_seconds=60.0,
        )
        reporter.terminal("PASS", "cleanup complete", r"D:\evidence\pass")
        reporter.terminal("FAIL", "FAIL_RUNTIME: peer stopped", r"D:\evidence\fail")
        reporter.terminal("NOT RUN", "Unity license unavailable", r"D:\evidence\not-run")
        reporter.close()

        self.assertEqual(
            [
                "PHASE186 MANUAL VERDICT: PASS - cleanup complete",
                r"PHASE186 MANUAL EVIDENCE: D:\evidence\pass",
                "PHASE186 MANUAL NEXT: Play Mode exited automatically; move to "
                "the next suite.",
                "PHASE186 MANUAL VERDICT: FAIL - FAIL_RUNTIME: peer stopped",
                r"PHASE186 MANUAL EVIDENCE: D:\evidence\fail",
                "PHASE186 MANUAL NEXT: Review the evidence, fix the named cause, "
                "then rerun the same one-line suite.",
                "PHASE186 MANUAL VERDICT: NOT RUN - Unity license unavailable",
                r"PHASE186 MANUAL EVIDENCE: D:\evidence\not-run",
                "PHASE186 MANUAL NEXT: Review the evidence, fix the named cause, "
                "then rerun the same one-line suite.",
            ],
            emitted,
        )

    def test_null_reporter_emits_nothing(self) -> None:
        with mock.patch("builtins.print") as sink:
            reporter = status.NullManualStatusReporter()
            reporter.transition("READY", "unused")
            reporter.unity_prepare("unused")
            reporter.unity_play_ready("unused")
            reporter.detail("unused")
            reporter.terminal("FAIL", "unused", "unused")
            reporter.close()
        sink.assert_not_called()


if __name__ == "__main__":
    unittest.main()
