#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Regression tests for the Phase179 Windows Player host helper.

"""Regression coverage for Phase179 WindowsStandalone64 host evidence."""

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
from types import SimpleNamespace
from unittest import mock


ROOT = Path(__file__).resolve().parents[4]
SMOKE_PATH = ROOT / "Scripts" / "smoke" / "ros2" / "phase179_foxrun_ros2_player_host.py"


def load_smoke_module():
    """Load the Player host without launching a Unity executable."""

    smoke_dir = str(SMOKE_PATH.parent)
    if smoke_dir not in sys.path:
        sys.path.insert(0, smoke_dir)
    spec = importlib.util.spec_from_file_location("phase179_foxrun_ros2_player_host", SMOKE_PATH)
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    assert spec.loader is not None
    spec.loader.exec_module(module)
    return module


class Phase179FoxRunRos2PlayerHostTests(unittest.TestCase):
    """Ensure the Windows helper proves a real Player lifecycle rather than launch success."""

    def setUp(self) -> None:
        """Load a fresh module for each test."""

        self.smoke = load_smoke_module()

    def test_player_environment_sets_requested_ros_values_before_launch(self) -> None:
        """The Player gets its exact distro/RMW/domain/discovery selection in its process environment."""

        args = SimpleNamespace(
            distro="lyrical",
            rmw="rmw_zenoh_cpp",
            domain_id=37,
            discovery_range="SUBNET",
            zenoh_router=Path("C:/certified/session.json5"),
        )

        env = self.smoke.build_player_environment(
            args,
            {"ROS_LOCALHOST_ONLY": "1", "ROS_DISCOVERY_SERVER": "stale", "UNRELATED": "ok"},
        )

        self.assertEqual("lyrical", env["ROS_DISTRO"])
        self.assertEqual("rmw_zenoh_cpp", env["RMW_IMPLEMENTATION"])
        self.assertEqual("37", env["ROS_DOMAIN_ID"])
        self.assertEqual("SUBNET", env["ROS_AUTOMATIC_DISCOVERY_RANGE"])
        self.assertEqual(str(args.zenoh_router), env["ZENOH_SESSION_CONFIG_URI"])
        self.assertNotIn("ROS_LOCALHOST_ONLY", env)
        self.assertNotIn("ROS_DISCOVERY_SERVER", env)

    def test_player_arguments_reject_ambiguous_zenoh_topology(self) -> None:
        """The Player host must not silently choose between two incompatible topology inputs."""

        with contextlib.redirect_stderr(io.StringIO()):
            with self.assertRaises(SystemExit):
                self.smoke.parse_args(
                    [
                        "--player",
                        "C:/build/Phase179.exe",
                        "--distro",
                        "lyrical",
                        "--rmw",
                        "rmw_zenoh_cpp",
                        "--token",
                        "phase179-token",
                        "--player-log",
                        "C:/logs/player.log",
                        "--zenoh-router",
                        "router.exe",
                        "--no-zenoh-router",
                    ]
                )

    def test_player_launch_command_is_explicit_batchmode_and_carries_shared_token(self) -> None:
        """The helper uses a direct argv and forwards the single Linux/Player correlation token."""

        command = self.smoke.build_player_command(
            Path("C:/build/Phase179.exe"),
            Path("C:/logs/Player.log"),
            "phase179-shared-token",
            string_burst_final_sequence=8,
        )

        self.assertEqual(str(Path("C:/build/Phase179.exe")), command[0])
        self.assertIn("-batchmode", command)
        self.assertIn("-nographics", command)
        self.assertIn("-logFile", command)
        self.assertIn("--phase179-player-auto-quit", command)
        self.assertIn("--phase179-token", command)
        self.assertEqual("phase179-shared-token", command[command.index("--phase179-token") + 1])
        self.assertIn("--phase179-player-burst-final-sequence", command)
        self.assertEqual("8", command[command.index("--phase179-player-burst-final-sequence") + 1])

    def test_ready_marker_must_report_actual_matching_runtime_and_rmw(self) -> None:
        """Requested CLI options cannot substitute for a Player's own active-runtime evidence."""

        marker = self.smoke.find_ready_marker(
            "PHASE179_ROS2_INBOUND_READY runtime=lyrical rmw=rmw_zenoh_cpp token=phase179-shared-token\n",
            "lyrical",
            "rmw_zenoh_cpp",
            "phase179-shared-token",
        )
        self.assertEqual("lyrical", marker["runtime"])
        with self.assertRaises(self.smoke.PlayerHostFailure) as context:
            self.smoke.find_ready_marker(
                "PHASE179_ROS2_INBOUND_READY runtime=lyrical rmw=rmw_fastrtps_cpp token=phase179-shared-token\n",
                "lyrical",
                "rmw_zenoh_cpp",
                "phase179-shared-token",
            )

        self.assertEqual("READY_MISMATCH", context.exception.category)

    def test_completion_marker_requires_matching_token_success_outcome_and_zero_exit_code(self) -> None:
        """The Player's own final marker must agree with its operating-system exit code."""

        completed = self.smoke.find_completion_marker(
            "PHASE179_ROS2_INBOUND_COMPLETE token=phase179-shared-token outcome=success exitCode=0\n",
            "phase179-shared-token",
        )
        self.assertEqual("success", completed["outcome"])
        with self.assertRaises(self.smoke.PlayerHostFailure) as context:
            self.smoke.find_completion_marker(
                "PHASE179_ROS2_INBOUND_COMPLETE token=phase179-shared-token outcome=timeout exitCode=2\n",
                "phase179-shared-token",
            )

        self.assertEqual("COMPLETE_MISMATCH", context.exception.category)

    def test_player_verdict_requires_ready_all_applied_markers_and_zero_exit(self) -> None:
        """A started process or partial message set is never a Player interop pass."""

        pass_verdict = self.smoke.classify_player_verdict(
            ready=True,
            all_applied=True,
            exit_code=0,
            failure=None,
        )
        partial_verdict = self.smoke.classify_player_verdict(
            ready=True,
            all_applied=False,
            exit_code=0,
            failure=None,
        )

        self.assertEqual("PLAYER_PROOF_COMPLETE_LINUX_PEER_CORRELATION_PENDING", pass_verdict)
        self.assertEqual("FAIL_APPLIED_MARKERS", partial_verdict)

    def test_player_host_optionally_validates_the_same_latest_wins_string_burst_marker(self) -> None:
        """Separate Windows logs still carry enough token-correlated burst proof to combine with Linux evidence."""

        token = "phase179-burst"
        standard_value = json.dumps({"type": "String", "data": token}, separators=(",", ":"))
        final_value = json.dumps(
            {"type": "String", "data": f"{token}|seq=8|total=9"},
            separators=(",", ":"),
        )
        log = (
            "PHASE179_ROS2_INBOUND_APPLIED session=3 topic=/foxrun/phase179/string "
            f"token={token} received=1 applied=1 replaced=0 value={standard_value}\n"
            "PHASE179_ROS2_INBOUND_APPLIED session=3 topic=/foxrun/phase179/string "
            f"token={token} received=9 applied=2 replaced=7 value={final_value}\n"
            "PHASE179_ROS2_INBOUND_APPLIED session=3 topic=/foxrun/phase179/twist "
            f"token={token} received=1 applied=1 replaced=0 "
            "value={\"type\":\"Twist\",\"linear\":{\"x\":1.25,\"y\":-0.25},\"angular\":{\"z\":-0.5}}\n"
            "PHASE179_ROS2_INBOUND_APPLIED session=3 topic=/foxrun/phase179/joy "
            f"token={token} received=1 applied=1 replaced=0 "
            f"value={{\"type\":\"Joy\",\"frameId\":\"{token}\",\"axes\":[0.125,-0.5,1.0],\"buttons\":[1,0,1]}}\n"
        )

        evidence = self.smoke.verify_required_applied_markers(log, token, string_burst_final_sequence=8)

        self.assertEqual(8, evidence["stringBurst"]["finalSequence"])
        self.assertEqual(7, evidence["stringBurst"]["replaced"])

    def test_exit_timeout_terminates_only_owned_player_process(self) -> None:
        """Timeout cleanup must not touch unrelated Unity or router processes."""

        process = mock.Mock()
        process.pid = 456
        process.communicate.side_effect = [
            subprocess.TimeoutExpired(["Phase179.exe"], 0.1),
            ("", None),
        ]
        process.returncode = -9
        with mock.patch.object(self.smoke.subprocess, "Popen", return_value=process):
            with mock.patch.object(self.smoke, "terminate_owned_process") as terminate:
                result = self.smoke.run_owned_process(
                    ["Phase179.exe"],
                    Path.cwd(),
                    {},
                    timeout_seconds=0.1,
                )

        self.assertTrue(result.timed_out)
        terminate.assert_called_once_with(process)

    def test_player_summary_redacts_zenoh_router_location_and_exception_text(self) -> None:
        """Portable Player evidence preserves the token but never configuration credentials."""

        payload = self.smoke.sanitize_summary(
            {
                "token": "phase179-shared-token",
                "zenohRouterPath": "C:/private/router.json5?password=super-secret",
                "error": "router password=super-secret",
            }
        )
        serialized = json.dumps(payload)

        self.assertIn("phase179-shared-token", serialized)
        self.assertNotIn("super-secret", serialized)
        self.assertNotIn("zenohRouterPath", payload)


if __name__ == "__main__":
    unittest.main()
