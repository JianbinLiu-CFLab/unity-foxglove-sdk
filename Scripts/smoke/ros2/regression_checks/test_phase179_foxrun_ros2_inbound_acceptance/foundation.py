#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Regression tests for the Phase179 Linux ROS2 inbound acceptance helper.

"""Regression coverage for Phase179 Linux-to-Unity ROS2 acceptance evidence."""
from __future__ import annotations
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "test_phase179_foxrun_ros2_inbound_acceptance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
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
from Scripts.phase192.source_layout import load_fresh_module
ROOT = Path(__file__).resolve().parents[4]
SMOKE_PATH = ROOT / "Scripts" / "smoke" / "ros2" / "phase179_foxrun_ros2_inbound_acceptance.py"
def load_smoke_module():
    """Load the helper under test without requiring a ROS2 Python installation."""

    smoke_dir = str(SMOKE_PATH.parent)
    if smoke_dir not in sys.path:
        sys.path.insert(0, smoke_dir)
    return load_fresh_module(
        "phase179_foxrun_ros2_inbound_acceptance",
        SMOKE_PATH,
    )
class _Phase179FoxRunRos2InboundAcceptanceTests_support:
    """Decomposed Phase192 implementation component."""
    def setUp(self) -> None:
        """Load a fresh module for each isolated test."""

        self.smoke = load_smoke_module()
    def test_message_set_rejects_unknown_and_duplicate_types(self) -> None:
        """The CLI must not silently accept a misspelled or duplicate contract."""

        with self.assertRaises(ValueError):
            self.smoke.parse_message_set("string,unknown")
        with self.assertRaises(ValueError):
            self.smoke.parse_message_set("string,string")
    def test_message_set_requires_string_for_twist_and_canonicalizes_execution_order(self) -> None:
        """Twist cannot establish a correlation token itself, so String must precede it."""

        self.assertEqual(
            ("string", "twist", "joy"),
            self.smoke.parse_message_set("joy,twist,string"),
        )
        with self.assertRaises(ValueError):
            self.smoke.parse_message_set("twist,joy")
        with contextlib.redirect_stderr(io.StringIO()):
            with self.assertRaises(SystemExit):
                self.smoke.parse_args(["--message-set", "twist"])
    def test_ready_marker_can_verify_runtime_and_rmw_before_any_editor_message_exists(self) -> None:
        """A fresh Editor READY marker may legitimately use its pre-publication manual token."""

        marker = self.smoke.find_matching_unity_ready_marker(
            "PHASE179_ROS2_INBOUND_READY runtime=lyrical rmw=rmw_fastrtps_cpp token=manual\n",
            "lyrical",
            "rmw_fastrtps_cpp",
            None,
        )

        self.assertEqual("manual", marker.token)
    def test_main_runs_selected_contracts_in_canonical_correlation_order(self) -> None:
        """Even a user-supplied reordered set runs String before Twist and preserves the remaining canonical order."""

        command_result = self.smoke.CommandResult(("ros2", "placeholder"), 0, "", False)

        def endpoint_for_spec(
            _ros2: Path,
            _env: dict[str, str],
            _topic: str,
            spec,
            _timeout: float,
        ):
            """Return matching endpoint evidence for each canonical test contract."""
            return self.smoke.EndpointEvidence(
                spec.message_type,
                1,
                spec.qos_reliability,
                spec.qos_history,
                spec.qos_depth,
                spec.qos_durability,
            )

        with tempfile.TemporaryDirectory() as temp:
            summary_path = Path(temp) / "canonical-order-summary.json"
            with mock.patch.object(self.smoke, "build_linux_environment", return_value={}):
                with mock.patch.object(self.smoke, "collect_optional_windows_peer_diagnostic", return_value="not-requested"):
                    with mock.patch.object(self.smoke, "configure_zenoh_topology", return_value="not-applicable"):
                        with mock.patch.object(self.smoke, "find_ros2_executable", return_value=Path("/usr/bin/ros2")):
                            with mock.patch.object(self.smoke, "wait_for_unity_subscription_topic"):
                                with mock.patch.object(self.smoke, "query_unity_subscription_endpoint", side_effect=endpoint_for_spec):
                                    with mock.patch.object(self.smoke, "run_bounded_command", return_value=command_result) as command:
                                        exit_code = self.smoke.main(
                                            [
                                                "--message-set",
                                                "joy,twist,string",
                                                "--token",
                                                "phase179-canonical-order",
                                                "--summary-json",
                                                str(summary_path),
                                            ]
                                        )
            summary = json.loads(summary_path.read_text(encoding="utf-8"))

        self.assertEqual(2, exit_code)
        self.assertEqual(["string", "twist", "joy"], summary["messageSet"])
        self.assertEqual(["string", "twist", "joy"], [result["name"] for result in summary["messageResults"]])
        self.assertEqual("std_msgs/msg/String", command.call_args_list[0].args[0][3])
        first_publish = command.call_args_list[1].args[0]
        self.assertIn("/foxrun/phase179/string", first_publish)
    def test_arguments_reject_ambiguous_zenoh_topology_and_unsafe_token(self) -> None:
        """One run must name one topology and use a marker-safe correlation token."""

        with contextlib.redirect_stderr(io.StringIO()):
            with self.assertRaises(SystemExit):
                self.smoke.parse_args(
                    [
                        "--rmw",
                        "rmw_zenoh_cpp",
                        "--zenoh-router",
                        "router.exe",
                        "--no-zenoh-router",
                    ]
                )
            with self.assertRaises(SystemExit):
                self.smoke.parse_args(["--token", "contains whitespace"])
            with self.assertRaises(SystemExit):
                self.smoke.parse_args(["--token", "a" * 97])
            with self.assertRaises(SystemExit):
                self.smoke.parse_args(["--unity-ready-token", "manual"])
    def test_linux_profile_envelope_requires_a_surface_and_rejects_cross_transport_topology(self) -> None:
        """A pending Linux half-evidence record must name its target Unity surface exactly."""

        args = self.smoke.parse_args(
            [
                "--distro",
                "humble",
                "--rmw",
                "rmw_fastrtps_cpp",
                "--profile-id",
                "humble-fastrtps",
                "--surface",
                "editor",
                "--topic-prefix",
                "/foxrun/phase179",
                "--message-set",
                "string,twist,joy",
            ]
        )

        self.assertEqual("humble-fastrtps", args.profile_id)
        self.assertEqual("editor", args.surface)
        self.assertEqual("/foxrun/phase179", args.topic_prefix)

        with contextlib.redirect_stderr(io.StringIO()):
            with self.assertRaises(SystemExit):
                self.smoke.parse_args(["--profile-id", "humble-fastrtps"])
            with self.assertRaises(SystemExit):
                self.smoke.parse_args(
                    [
                        "--rmw",
                        "rmw_fastrtps_cpp",
                        "--zenoh-topology-id",
                        "phase179-lyrical-zenoh",
                    ]
                )
    def test_linux_zenoh_configuration_uses_shared_ready_topology_lifecycle(self) -> None:
        """The Linux peer may probe only after the shared owned-router helper has reported readiness."""

        args = SimpleNamespace(
            rmw="rmw_zenoh_cpp",
            zenoh_router=Path("/certified/rmw_zenohd"),
            no_zenoh_router=False,
            zenoh_topology_id="phase179-lyrical-zenoh",
            summary_json=Path("/tmp/linux-peer-summary.json"),
            timeout_seconds=15.0,
            zenoh_router_ready_marker="Started",
        )
        options = object()
        handle = SimpleNamespace(mode="owned-router", readiness="owned-router-ready")

        with mock.patch.object(self.smoke.zenoh_topology, "validate_topology_options", return_value=options) as validate:
            with mock.patch.object(self.smoke.zenoh_topology, "start_topology", return_value=handle) as start:
                configured = self.smoke.configure_zenoh_topology(args, {})

        self.assertIs(handle, configured)
        validate.assert_called_once_with(
            "rmw_zenoh_cpp",
            router=args.zenoh_router,
            no_router=False,
            topology_id="phase179-lyrical-zenoh",
        )
        self.assertEqual("Started", start.call_args.kwargs["ready_marker"])
    def test_timeout_arguments_reject_non_finite_values(self) -> None:
        """A bounded smoke command cannot accept NaN or infinity as a timeout or rate."""

        with contextlib.redirect_stderr(io.StringIO()):
            for option, value in (
                ("--timeout-seconds", "nan"),
                ("--timeout-seconds", "inf"),
                ("--string-burst-rate-hz", "-inf"),
            ):
                with self.subTest(option=option, value=value):
                    with self.assertRaises(SystemExit):
                        self.smoke.parse_args([option, value])
    def test_arguments_require_string_when_burst_is_requested(self) -> None:
        """The latest-wins burst is deliberately a deterministic String-only probe."""

        with contextlib.redirect_stderr(io.StringIO()):
            with self.assertRaises(SystemExit):
                self.smoke.parse_args(
                    [
                        "--message-set",
                        "twist,joy",
                        "--string-burst-final-sequence",
                        "8",
                    ]
                )
            with self.assertRaises(SystemExit):
                self.smoke.parse_args(["--string-burst-final-sequence", "0"])
    def test_negative_case_arguments_require_one_eligible_contract_and_explicit_peer_rmw(self) -> None:
        """Negative probes stay single-purpose and never choose a fallback transport."""

        args = self.smoke.parse_args(
            [
                "--negative-case",
                "rmw-mismatch",
                "--rmw",
                "rmw_fastrtps_cpp",
                "--negative-peer-rmw",
                "rmw_zenoh_cpp",
                "--message-set",
                "string",
            ]
        )
        self.assertEqual("rmw-mismatch", args.negative_case)
        self.assertEqual("rmw_zenoh_cpp", args.negative_peer_rmw)

        with contextlib.redirect_stderr(io.StringIO()):
            with self.assertRaises(SystemExit):
                self.smoke.parse_args(["--negative-case", "rmw-mismatch", "--message-set", "string"])
            with self.assertRaises(SystemExit):
                self.smoke.parse_args(
                    [
                        "--negative-case",
                        "rmw-mismatch",
                        "--rmw",
                        "rmw_fastrtps_cpp",
                        "--negative-peer-rmw",
                        "rmw_fastrtps_cpp",
                        "--message-set",
                        "string",
                    ]
                )
            with self.assertRaises(SystemExit):
                self.smoke.parse_args(
                    [
                        "--negative-case",
                        "qos-incompatible",
                        "--message-set",
                        "joy",
                    ]
                )
            with self.assertRaises(SystemExit):
                self.smoke.parse_args(
                    [
                        "--negative-case",
                        "type-mismatch",
                        "--message-set",
                        "string,twist",
                    ]
                )
    def test_selected_linux_environment_must_match_requested_distro_and_rmw(self) -> None:
        """The helper must not source or substitute a different ROS installation."""

        args = self.smoke.parse_args(["--distro", "humble", "--rmw", "rmw_fastrtps_cpp"])
        with self.assertRaises(self.smoke.AcceptanceFailure) as context:
            self.smoke.validate_selected_linux_environment(
                args,
                {"ROS_DISTRO": "jazzy", "RMW_IMPLEMENTATION": "rmw_fastrtps_cpp"},
            )

        self.assertEqual("ENVIRONMENT", context.exception.category)
    def test_publish_commands_are_argument_arrays_with_contract_qos_and_deterministic_values(self) -> None:
        """Every message type has an explicit QoS-matched, shell-free ros2 argv usable by Lyrical."""

        token = "phase179-test-token"
        expected_reliability = {
            "string": "reliable",
            "twist": "reliable",
            "joy": "best_effort",
            "imu": "best_effort",
        }
        expected_depth = {
            "string": "10",
            "twist": "10",
            "joy": "5",
            "imu": "5",
        }
        for name, reliability in expected_reliability.items():
            spec = self.smoke.MESSAGE_SPECS[name]
            command = self.smoke.build_publish_command(Path("/usr/bin/ros2"), spec, token)

            self.assertIsInstance(command, list)
            self.assertEqual(str(Path("/usr/bin/ros2")), command[0])
            self.assertIn("--once", command)
            self.assertNotIn("--no-daemon", command)
            self.assertIn("--qos-reliability", command)
            topic_index = command.index(self.smoke.topic_for_spec("/foxrun/phase179", spec))
            self.assertLess(command.index("--qos-reliability"), topic_index)
            self.assertLess(command.index("--qos-history"), topic_index)
            self.assertLess(command.index("--qos-depth"), topic_index)
            self.assertLess(command.index("--qos-durability"), topic_index)
            self.assertEqual(reliability, command[command.index("--qos-reliability") + 1])
            self.assertEqual("keep_last", command[command.index("--qos-history") + 1])
            self.assertEqual(expected_depth[name], command[command.index("--qos-depth") + 1])
            self.assertEqual("volatile", command[command.index("--qos-durability") + 1])
            payload = json.loads(command[-1])
            if name == "string":
                self.assertEqual(token, payload["data"])
            elif name == "twist":
                self.assertEqual(1.25, payload["linear"]["x"])
                self.assertEqual(-0.5, payload["angular"]["z"])
            elif name == "joy":
                self.assertEqual(token, payload["header"]["frame_id"])
                self.assertEqual([0.125, -0.5, 1.0], payload["axes"])
            else:
                self.assertEqual(token, payload["header"]["frame_id"])
                self.assertNotEqual(0.0, payload["angular_velocity"]["z"])
    def test_marker_parser_requires_matching_counters_and_canonical_value_proof(self) -> None:
        """A copied Unity value, not only a publish attempt, qualifies for PASS."""

        token = "phase179-proof"
        expected_value = {"type": "String", "data": token}
        text = (
            "PHASE179_ROS2_INBOUND_APPLIED\n"
            f"session=5 topic=/foxrun/phase179/string token={token} received=2 applied=1 replaced=1 "
            f"value={json.dumps(expected_value, separators=(',', ':'))}\n"
        )

        marker = self.smoke.find_matching_unity_marker(
            text,
            "/foxrun/phase179/string",
            token,
            expected_value,
        )

        self.assertEqual(5, marker.session)
        self.assertEqual(2, marker.received)
        self.assertEqual(expected_value, marker.value)
    def test_marker_without_copied_value_is_not_treated_as_full_unity_proof(self) -> None:
        """Counters alone cannot make an unobserved Unity value into a PASS."""

        text = (
            "PHASE179_ROS2_INBOUND_APPLIED\n"
            "session=5 topic=/foxrun/phase179/string token=phase179-proof received=1 applied=1 replaced=0\n"
        )
        with self.assertRaises(self.smoke.AcceptanceFailure) as context:
            self.smoke.find_matching_unity_marker(
                text,
                "/foxrun/phase179/string",
                "phase179-proof",
                {"type": "String", "data": "phase179-proof"},
            )

        self.assertEqual("VALUE_MISMATCH", context.exception.category)
    def test_marker_wait_after_publish_offset_cannot_reuse_a_stale_token(self) -> None:
        """A matching marker written before publication cannot satisfy a later reused token run."""

        token = "phase179-reused-token"
        expected_value = {"type": "String", "data": token}
        stale = (
            "PHASE179_ROS2_INBOUND_APPLIED\n"
            f"session=8 topic=/foxrun/phase179/string token={token} received=4 applied=4 replaced=0 "
            f"value={json.dumps(expected_value, separators=(',', ':'))}\n"
        )
        with tempfile.TemporaryDirectory() as temp:
            log = Path(temp) / "Unity.log"
            log.write_text(stale, encoding="utf-8")
            offset = self.smoke.unity_log_offset(log)
            with self.assertRaises(self.smoke.AcceptanceFailure) as context:
                self.smoke.wait_for_unity_marker(
                    log,
                    "/foxrun/phase179/string",
                    token,
                    expected_value,
                    timeout_seconds=0.0,
                    start_offset=offset,
                )

        self.assertEqual("UNITY_TIMEOUT", context.exception.category)
    def test_ready_marker_requires_runtime_rmw_and_token_identity(self) -> None:
        """A negative proof must bind to the active Unity runtime identity, not an arbitrary old line."""

        token = "phase179-ready-token"
        text = f"PHASE179_ROS2_INBOUND_READY runtime=lyrical rmw=rmw_zenoh_cpp token={token}\n"
        ready = self.smoke.find_matching_unity_ready_marker(text, "lyrical", "rmw_zenoh_cpp", token)

        self.assertEqual("lyrical", ready.runtime)
        self.assertEqual("rmw_zenoh_cpp", ready.rmw)
        self.assertEqual(token, ready.token)
        with self.assertRaises(self.smoke.AcceptanceFailure) as context:
            self.smoke.find_matching_unity_ready_marker(text, "lyrical", "rmw_fastrtps_cpp", token)
        self.assertEqual("READY_MISMATCH", context.exception.category)


__all__ = [name for name in globals() if not name.startswith("__")]
