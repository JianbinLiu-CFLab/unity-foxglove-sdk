#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Regression tests for the Phase179 Linux ROS2 inbound acceptance helper.

"""Regression coverage for Phase179 Linux-to-Unity ROS2 acceptance evidence."""

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
SMOKE_PATH = ROOT / "Scripts" / "smoke" / "ros2" / "phase179_foxrun_ros2_inbound_acceptance.py"


def load_smoke_module():
    """Load the helper under test without requiring a ROS2 Python installation."""

    smoke_dir = str(SMOKE_PATH.parent)
    if smoke_dir not in sys.path:
        sys.path.insert(0, smoke_dir)
    spec = importlib.util.spec_from_file_location("phase179_foxrun_ros2_inbound_acceptance", SMOKE_PATH)
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    assert spec.loader is not None
    spec.loader.exec_module(module)
    return module


class Phase179FoxRunRos2InboundAcceptanceTests(unittest.TestCase):
    """Keep the Linux helper honest, bounded, and transport-specific."""

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

    def test_ready_marker_baseline_requires_a_new_unseen_token_when_editor_log_is_reused(self) -> None:
        """A reused Editor.log may overwrite below EOF, so local acceptance must reject its pre-run READY token."""

        stale_token = "phase179-ready-before-play"
        fresh_token = "phase179-ready-after-play"
        stale = f"PHASE179_ROS2_INBOUND_READY runtime=humble rmw=rmw_fastrtps_cpp token={stale_token}\n"
        fresh = f"PHASE179_ROS2_INBOUND_READY runtime=humble rmw=rmw_fastrtps_cpp token={fresh_token}\n"
        with tempfile.TemporaryDirectory() as temp:
            log = Path(temp) / "Editor.log"
            log.write_text(stale, encoding="utf-8")
            baseline = self.smoke.capture_unity_ready_marker_tokens(log, "humble", "rmw_fastrtps_cpp")

        self.assertEqual(frozenset({stale_token}), baseline)
        ready = self.smoke.find_matching_unity_ready_marker(
            stale + fresh,
            "humble",
            "rmw_fastrtps_cpp",
            None,
            excluded_tokens=baseline,
        )
        self.assertEqual(fresh_token, ready.token)
        with self.assertRaises(self.smoke.AcceptanceFailure) as context:
            self.smoke.find_matching_unity_ready_marker(
                stale,
                "humble",
                "rmw_fastrtps_cpp",
                None,
                excluded_tokens=baseline,
            )
        self.assertEqual("READY_STALE", context.exception.category)

    def test_ready_wait_after_offset_cannot_reuse_a_stale_identity(self) -> None:
        """An Editor host must not accept a READY marker written before it started observing the log."""

        token = "phase179-ready-reused-token"
        stale = f"PHASE179_ROS2_INBOUND_READY runtime=humble rmw=rmw_fastrtps_cpp token={token}\n"
        with tempfile.TemporaryDirectory() as temp:
            log = Path(temp) / "Editor.log"
            log.write_text(stale, encoding="utf-8")
            offset = self.smoke.unity_log_offset(log)
            with self.assertRaises(self.smoke.AcceptanceFailure) as context:
                self.smoke.wait_for_unity_ready_marker(
                    log,
                    "humble",
                    "rmw_fastrtps_cpp",
                    token,
                    timeout_seconds=0.0,
                    start_offset=offset,
                )

        self.assertEqual("READY_TIMEOUT", context.exception.category)

    def test_endpoint_validation_requires_subscriber_type_and_contract_qos(self) -> None:
        """Discovery must reject an endpoint that reports the wrong native QoS."""

        spec = self.smoke.MESSAGE_SPECS["string"]
        wrong_qos = (
            "Type: std_msgs/msg/String\n"
            "Subscription count: 1\n"
            "Subscription #0:\n"
            "  Reliability: BEST_EFFORT\n"
            "  History (Depth): KEEP_LAST (10)\n"
            "  Durability: VOLATILE\n"
        )
        with self.assertRaises(self.smoke.AcceptanceFailure) as context:
            self.smoke.validate_unity_subscription_endpoint(wrong_qos, spec)

        self.assertEqual("ENDPOINT", context.exception.category)

    def test_endpoint_validation_returns_safe_type_count_and_qos_evidence(self) -> None:
        """Summary evidence records validated graph facts, not raw machine-specific CLI output."""

        spec = self.smoke.MESSAGE_SPECS["twist"]
        info = (
            "Type: geometry_msgs/msg/Twist\n"
            "Subscription count: 2\n"
            "Subscription #0:\n"
            "  Reliability: RELIABLE\n"
            "  History (Depth): KEEP_LAST (10)\n"
            "  Durability: VOLATILE\n"
        )

        evidence = self.smoke.validate_unity_subscription_endpoint(info, spec)

        self.assertEqual("geometry_msgs/msg/Twist", evidence.message_type)
        self.assertEqual(2, evidence.subscription_count)
        self.assertEqual("reliable", evidence.qos_reliability)
        self.assertEqual("keep_last", evidence.qos_history)
        self.assertEqual(10, evidence.qos_depth)
        self.assertEqual("volatile", evidence.qos_durability)

    def test_endpoint_validation_requires_keep_last_depth_and_volatile_durability(self) -> None:
        """A matching reliability alone cannot hide an incompatible native endpoint profile."""

        spec = self.smoke.MESSAGE_SPECS["joy"]
        valid = (
            "Type: sensor_msgs/msg/Joy\n"
            "Subscription count: 1\n"
            "Subscription #0:\n"
            "  Reliability: BEST_EFFORT\n"
            "  History (Depth): KEEP_LAST (5)\n"
            "  Durability: VOLATILE\n"
        )
        evidence = self.smoke.validate_unity_subscription_endpoint(valid, spec)
        self.assertEqual("keep_last", evidence.qos_history)
        self.assertEqual(5, evidence.qos_depth)
        self.assertEqual("volatile", evidence.qos_durability)

        for label, invalid in (
            ("history", valid.replace("KEEP_LAST", "KEEP_ALL")),
            ("depth", valid.replace("(5)", "(10)")),
            ("durability", valid.replace("VOLATILE", "TRANSIENT_LOCAL")),
        ):
            with self.subTest(label=label):
                with self.assertRaises(self.smoke.AcceptanceFailure) as context:
                    self.smoke.validate_unity_subscription_endpoint(invalid, spec)
                self.assertEqual("ENDPOINT", context.exception.category)

    def test_endpoint_probe_waits_through_graph_lag_until_unity_qos_is_visible(self) -> None:
        """A listed topic may precede verbose endpoint QoS discovery by one probe."""

        spec = self.smoke.MESSAGE_SPECS["string"]
        unavailable = self.smoke.CommandResult(
            ("ros2", "topic", "info"),
            0,
            "Type: std_msgs/msg/String\nSubscription count: 0\n",
            False,
        )
        ready = self.smoke.CommandResult(
            ("ros2", "topic", "info"),
            0,
            "Type: std_msgs/msg/String\nSubscription count: 1\nSubscription #0:\n"
            "  Reliability: RELIABLE\n  History (Depth): KEEP_LAST (10)\n  Durability: VOLATILE\n",
            False,
        )
        with mock.patch.object(self.smoke, "run_bounded_command", side_effect=[unavailable, ready]) as probe:
            with mock.patch.object(self.smoke.time, "sleep"):
                evidence = self.smoke.query_unity_subscription_endpoint(
                    Path("/usr/bin/ros2"),
                    {},
                    "/foxrun/phase179/string",
                    spec,
                    timeout_seconds=1.0,
                )

        self.assertEqual(2, probe.call_count)
        self.assertEqual(1, evidence.subscription_count)

    def test_positive_main_captures_log_offset_before_publication(self) -> None:
        """A positive reused token is accepted only from log content appended after its own publish starts."""

        token = "phase179-positive-offset"
        spec = self.smoke.MESSAGE_SPECS["string"]
        endpoint = self.smoke.EndpointEvidence(
            spec.message_type,
            1,
            spec.qos_reliability,
            spec.qos_history,
            spec.qos_depth,
            spec.qos_durability,
        )
        marker = self.smoke.UnityMarker(
            1,
            "/foxrun/phase179/string",
            token,
            1,
            1,
            0,
            spec.expected_value(token),
        )
        events: list[str] = []

        def command(command_argv, _env, _timeout, _label):
            """Record the command ordering while returning a successful owned process result."""
            events.append("publish" if command_argv[1:3] == ["topic", "pub"] else "interface")
            return self.smoke.CommandResult(tuple(command_argv), 0, "", False)

        def offset(_log: Path) -> int:
            """Record the fresh-log checkpoint used by the positive correlation test."""
            events.append("offset")
            return 41

        with tempfile.TemporaryDirectory() as temp:
            summary_path = Path(temp) / "positive-offset-summary.json"
            unity_log = Path(temp) / "Unity.log"
            unity_log.write_text("Unity started\n", encoding="utf-8")
            with mock.patch.object(self.smoke, "build_linux_environment", return_value={}):
                with mock.patch.object(self.smoke, "collect_optional_windows_peer_diagnostic", return_value="not-requested"):
                    with mock.patch.object(self.smoke, "configure_zenoh_topology", return_value="not-applicable"):
                        with mock.patch.object(self.smoke, "find_ros2_executable", return_value=Path("/usr/bin/ros2")):
                            with mock.patch.object(self.smoke, "wait_for_unity_subscription_topic"):
                                with mock.patch.object(self.smoke, "query_unity_subscription_endpoint", return_value=endpoint):
                                    with mock.patch.object(self.smoke, "run_bounded_command", side_effect=command):
                                        with mock.patch.object(self.smoke, "unity_log_offset", side_effect=offset):
                                            with mock.patch.object(self.smoke, "wait_for_unity_marker", return_value=marker) as wait_marker:
                                                exit_code = self.smoke.main(
                                                    [
                                                        "--message-set",
                                                        "string",
                                                        "--token",
                                                        token,
                                                        "--unity-log",
                                                        str(unity_log),
                                                        "--summary-json",
                                                        str(summary_path),
                                                    ]
                                                )

        self.assertEqual(0, exit_code)
        self.assertLess(events.index("offset"), events.index("publish"))
        self.assertEqual(41, wait_marker.call_args.kwargs["start_offset"])

    def test_burst_main_captures_a_fresh_log_offset_before_burst_publication(self) -> None:
        """The final burst marker cannot be satisfied by the baseline marker or an older same-token burst."""

        token = "phase179-burst-offset"
        spec = self.smoke.MESSAGE_SPECS["string"]
        endpoint = self.smoke.EndpointEvidence(
            spec.message_type,
            1,
            spec.qos_reliability,
            spec.qos_history,
            spec.qos_depth,
            spec.qos_durability,
        )
        baseline = self.smoke.UnityMarker(1, "/foxrun/phase179/string", token, 1, 1, 0, spec.expected_value(token))
        final = self.smoke.UnityMarker(
            1,
            "/foxrun/phase179/string",
            token,
            3,
            2,
            1,
            self.smoke.expected_string_burst_value(token, 1),
        )
        events: list[str] = []

        def command(command_argv, _env, _timeout, _label):
            """Record each baseline or burst process command without launching ROS2."""
            events.append("publish" if command_argv[1:3] == ["topic", "pub"] else "interface")
            return self.smoke.CommandResult(tuple(command_argv), 0, "", False)

        def offset(_log: Path) -> int:
            """Return distinct checkpoints to prove the burst path takes a fresh offset."""
            value = 101 if events.count("offset") == 0 else 202
            events.append("offset")
            return value

        with tempfile.TemporaryDirectory() as temp:
            summary_path = Path(temp) / "burst-offset-summary.json"
            unity_log = Path(temp) / "Unity.log"
            unity_log.write_text("Unity started\n", encoding="utf-8")
            with mock.patch.object(self.smoke, "build_linux_environment", return_value={}):
                with mock.patch.object(self.smoke, "collect_optional_windows_peer_diagnostic", return_value="not-requested"):
                    with mock.patch.object(self.smoke, "configure_zenoh_topology", return_value="not-applicable"):
                        with mock.patch.object(self.smoke, "find_ros2_executable", return_value=Path("/usr/bin/ros2")):
                            with mock.patch.object(self.smoke, "wait_for_unity_subscription_topic"):
                                with mock.patch.object(self.smoke, "query_unity_subscription_endpoint", return_value=endpoint):
                                    with mock.patch.object(self.smoke, "run_bounded_command", side_effect=command):
                                        with mock.patch.object(self.smoke, "unity_log_offset", side_effect=offset):
                                            with mock.patch.object(self.smoke, "run_string_burst", side_effect=lambda *_args: events.append("burst")):
                                                with mock.patch.object(self.smoke, "wait_for_unity_marker", side_effect=[baseline, final]) as wait_marker:
                                                    exit_code = self.smoke.main(
                                                        [
                                                            "--message-set",
                                                            "string",
                                                            "--token",
                                                            token,
                                                            "--unity-log",
                                                            str(unity_log),
                                                            "--string-burst-final-sequence",
                                                            "1",
                                                            "--summary-json",
                                                            str(summary_path),
                                                        ]
                                                    )

        self.assertEqual(0, exit_code)
        self.assertEqual(101, wait_marker.call_args_list[0].kwargs["start_offset"])
        self.assertEqual(202, wait_marker.call_args_list[1].kwargs["start_offset"])
        self.assertLess([index for index, event in enumerate(events) if event == "offset"][1], events.index("burst"))

    def test_negative_publish_commands_are_shell_free_and_deliberately_incompatible(self) -> None:
        """Type and QoS negative probes must not mutate the selected transport or contract topic."""

        string_spec = self.smoke.MESSAGE_SPECS["string"]
        type_mismatch = self.smoke.build_negative_publish_command(
            Path("/usr/bin/ros2"),
            string_spec,
            "phase179-negative",
            "type-mismatch",
        )
        self.assertIsInstance(type_mismatch, list)
        type_topic_index = type_mismatch.index("/foxrun/phase179/string")
        self.assertEqual("geometry_msgs/msg/Twist", type_mismatch[type_topic_index + 1])
        self.assertNotIn("shell", " ".join(type_mismatch).lower())

        qos_mismatch = self.smoke.build_negative_publish_command(
            Path("/usr/bin/ros2"),
            string_spec,
            "phase179-negative",
            "qos-incompatible",
        )
        qos_topic_index = qos_mismatch.index("/foxrun/phase179/string")
        self.assertEqual("std_msgs/msg/String", qos_mismatch[qos_topic_index + 1])
        self.assertEqual("best_effort", qos_mismatch[qos_mismatch.index("--qos-reliability") + 1])
        self.assertEqual("keep_last", qos_mismatch[qos_mismatch.index("--qos-history") + 1])
        self.assertEqual("volatile", qos_mismatch[qos_mismatch.index("--qos-durability") + 1])

    def test_negative_verdicts_never_claim_positive_interoperability(self) -> None:
        """Expected rejection stays distinct from a PASS and retains missing-Unity-proof status."""

        verified = self.smoke.classify_negative_verdict(
            negative_case="qos-incompatible",
            unity_log_available=True,
            expectation_observed=True,
            unity_ready=True,
            contract_identity=True,
            unity_no_apply=True,
            failure=None,
        )
        pending = self.smoke.classify_negative_verdict(
            negative_case="rmw-mismatch",
            unity_log_available=False,
            expectation_observed=True,
            unity_ready=False,
            contract_identity=False,
            unity_no_apply=False,
            failure=None,
        )
        missing_ready = self.smoke.classify_negative_verdict(
            negative_case="type-mismatch",
            unity_log_available=True,
            expectation_observed=True,
            unity_ready=False,
            contract_identity=False,
            unity_no_apply=True,
            failure=None,
        )
        missing_contract = self.smoke.classify_negative_verdict(
            negative_case="type-mismatch",
            unity_log_available=True,
            expectation_observed=True,
            unity_ready=True,
            contract_identity=False,
            unity_no_apply=True,
            failure=None,
        )

        self.assertEqual("EXPECTED_NEGATIVE_QOS_INCOMPATIBLE", verified)
        self.assertEqual("LOCAL_NEGATIVE_EVIDENCE_RMW_MISMATCH_UNITY_PROOF_PENDING", pending)
        self.assertEqual("FAIL_READY", missing_ready)
        self.assertEqual("FAIL_CONTRACT_IDENTITY", missing_contract)
        self.assertNotEqual("PASS", verified)
        self.assertNotEqual("PASS", pending)

    def test_qos_negative_establishes_current_string_contract_identity_before_no_apply_proof(self) -> None:
        """A QoS rejection is full evidence only after READY and a new positive String identity baseline."""

        token = "phase179-negative-qos-identity"
        spec = self.smoke.MESSAGE_SPECS["string"]
        args = self.smoke.parse_args(
            [
                "--negative-case",
                "qos-incompatible",
                "--message-set",
                "string",
                "--token",
                token,
                "--unity-ready-token",
                "manual",
                "--unity-log",
                "placeholder.log",
            ]
        )
        endpoint = self.smoke.EndpointEvidence(
            spec.message_type,
            1,
            spec.qos_reliability,
            spec.qos_history,
            spec.qos_depth,
            spec.qos_durability,
        )
        ready = self.smoke.UnityReadyMarker("jazzy", "rmw_fastrtps_cpp", "manual")
        baseline = self.smoke.UnityMarker(
            6,
            "/foxrun/phase179/string",
            token,
            1,
            1,
            0,
            spec.expected_value(token),
        )
        interface = self.smoke.CommandResult(("ros2", "interface", "show"), 0, "", False)
        positive = self.smoke.CommandResult(("ros2", "topic", "pub"), 0, "", False)
        negative = self.smoke.CommandResult(("ros2", "topic", "pub"), 0, "", False)
        with mock.patch.object(self.smoke, "wait_for_unity_ready_marker", return_value=ready) as wait_ready:
            with mock.patch.object(self.smoke, "wait_for_unity_subscription_topic"):
                with mock.patch.object(self.smoke, "query_unity_subscription_endpoint", return_value=endpoint):
                    with mock.patch.object(self.smoke, "unity_log_offset", side_effect=[101, 202]):
                        with mock.patch.object(self.smoke, "wait_for_unity_marker", return_value=baseline) as wait_marker:
                            with mock.patch.object(self.smoke, "wait_for_no_unity_apply_after_offset") as wait_no_apply:
                                with mock.patch.object(self.smoke, "run_bounded_command", side_effect=[interface, positive, negative]) as commands:
                                    result = self.smoke.run_negative_case(args, {}, Path("/usr/bin/ros2"), token)

        self.assertTrue(result["unityReady"])
        self.assertTrue(result["contractIdentity"])
        self.assertTrue(result["unityNoApply"])
        self.assertEqual("best_effort", result["attemptedQosReliability"])
        self.assertEqual("rmw_fastrtps_cpp", wait_ready.call_args.args[2])
        self.assertEqual(101, wait_marker.call_args.kwargs["start_offset"])
        self.assertEqual(202, wait_no_apply.call_args.args[2])
        baseline_publish = commands.call_args_list[1].args[0]
        baseline_topic_index = baseline_publish.index("/foxrun/phase179/string")
        self.assertEqual("std_msgs/msg/String", baseline_publish[baseline_topic_index + 1])
        self.assertEqual("best_effort", commands.call_args_list[2].args[0][commands.call_args_list[2].args[0].index("--qos-reliability") + 1])
        self.assertEqual(
            "EXPECTED_NEGATIVE_QOS_INCOMPATIBLE",
            self.smoke.classify_negative_verdict(
                negative_case="qos-incompatible",
                unity_log_available=True,
                expectation_observed=bool(result["expectationObserved"]),
                unity_ready=bool(result["unityReady"]),
                contract_identity=bool(result["contractIdentity"]),
                unity_no_apply=bool(result["unityNoApply"]),
                failure=None,
            ),
        )

    def test_rmw_negative_binds_ready_marker_to_the_expected_peer_rmw_and_current_token(self) -> None:
        """RMW non-discovery is never full evidence unless Unity reports the deliberately opposite active transport."""

        token = "phase179-negative-rmw-identity"
        args = self.smoke.parse_args(
            [
                "--negative-case",
                "rmw-mismatch",
                "--distro",
                "lyrical",
                "--rmw",
                "rmw_fastrtps_cpp",
                "--negative-peer-rmw",
                "rmw_zenoh_cpp",
                "--message-set",
                "string",
                "--token",
                token,
                "--unity-ready-token",
                token,
                "--unity-log",
                "placeholder.log",
            ]
        )
        ready = self.smoke.UnityReadyMarker("lyrical", "rmw_zenoh_cpp", token)
        interface = self.smoke.CommandResult(("ros2", "interface", "show"), 0, "", False)
        with mock.patch.object(self.smoke, "wait_for_unity_ready_marker", return_value=ready) as wait_ready:
            with mock.patch.object(self.smoke, "unity_log_offset", return_value=77):
                with mock.patch.object(self.smoke, "wait_for_unity_subscription_absence"):
                    with mock.patch.object(self.smoke, "wait_for_no_unity_apply_after_offset"):
                        with mock.patch.object(self.smoke, "run_bounded_command", return_value=interface):
                            result = self.smoke.run_negative_case(args, {}, Path("/usr/bin/ros2"), token)

        self.assertTrue(result["unityReady"])
        self.assertTrue(result["contractIdentity"])
        self.assertEqual("rmw_zenoh_cpp", result["expectedPeerRmw"])
        self.assertEqual("rmw_zenoh_cpp", wait_ready.call_args.args[2])

    def test_type_mismatch_main_records_only_a_local_expected_negative(self) -> None:
        """A rejected wrong-type publication cannot be upgraded into normal interop success."""

        endpoint = self.smoke.EndpointEvidence(
            "std_msgs/msg/String",
            1,
            "reliable",
            "keep_last",
            10,
            "volatile",
        )
        rejected = self.smoke.CommandResult(
            ("ros2", "topic", "pub"),
            1,
            "",
            False,
        )
        interface = self.smoke.CommandResult(
            ("ros2", "interface", "show"),
            0,
            "std_msgs/msg/String\n",
            False,
        )
        with tempfile.TemporaryDirectory() as temp:
            summary_path = Path(temp) / "negative-summary.json"
            with mock.patch.object(self.smoke, "build_linux_environment", return_value={}):
                with mock.patch.object(self.smoke, "collect_optional_windows_peer_diagnostic", return_value="not-requested"):
                    with mock.patch.object(self.smoke, "configure_zenoh_topology", return_value="not-applicable"):
                        with mock.patch.object(self.smoke, "find_ros2_executable", return_value=Path("/usr/bin/ros2")):
                            with mock.patch.object(self.smoke, "wait_for_unity_subscription_topic"):
                                with mock.patch.object(self.smoke, "query_unity_subscription_endpoint", return_value=endpoint):
                                    with mock.patch.object(self.smoke, "run_bounded_command", side_effect=[interface, rejected]) as command:
                                        exit_code = self.smoke.main(
                                            [
                                                "--negative-case",
                                                "type-mismatch",
                                                "--message-set",
                                                "string",
                                                "--token",
                                                "phase179-negative",
                                                "--summary-json",
                                                str(summary_path),
                                            ]
                                        )
            summary = json.loads(summary_path.read_text(encoding="utf-8"))

        self.assertEqual(2, exit_code)
        self.assertEqual(2, command.call_count)
        self.assertEqual("LOCAL_NEGATIVE_EVIDENCE_TYPE_MISMATCH_UNITY_PROOF_PENDING", summary["verdict"])
        self.assertNotEqual("PASS", summary["verdict"])
        self.assertTrue(summary["messageResults"][0]["expectationObserved"])
        self.assertEqual("geometry_msgs/msg/Twist", summary["messageResults"][0]["attemptedMessageType"])
        self.assertFalse(summary["messageResults"][0]["unityCounterUnchanged"])

    def test_type_mismatch_uses_graph_type_evidence_even_when_cli_finishes(self) -> None:
        """A CLI exit code is not transport proof; the observed endpoint type is the mismatch evidence."""

        endpoint = self.smoke.EndpointEvidence(
            "std_msgs/msg/String",
            1,
            "reliable",
            "keep_last",
            10,
            "volatile",
        )
        interface = self.smoke.CommandResult(("ros2", "interface", "show"), 0, "std_msgs/msg/String\n", False)
        completed = self.smoke.CommandResult(("ros2", "topic", "pub"), 0, "", False)
        with tempfile.TemporaryDirectory() as temp:
            summary_path = Path(temp) / "type-mismatch-completed.json"
            with mock.patch.object(self.smoke, "build_linux_environment", return_value={}):
                with mock.patch.object(self.smoke, "collect_optional_windows_peer_diagnostic", return_value="not-requested"):
                    with mock.patch.object(self.smoke, "configure_zenoh_topology", return_value="not-applicable"):
                        with mock.patch.object(self.smoke, "find_ros2_executable", return_value=Path("/usr/bin/ros2")):
                            with mock.patch.object(self.smoke, "wait_for_unity_subscription_topic"):
                                with mock.patch.object(self.smoke, "query_unity_subscription_endpoint", return_value=endpoint):
                                    with mock.patch.object(self.smoke, "run_bounded_command", side_effect=[interface, completed]):
                                        exit_code = self.smoke.main(
                                            [
                                                "--negative-case",
                                                "type-mismatch",
                                                "--message-set",
                                                "string",
                                                "--token",
                                                "phase179-negative-completed",
                                                "--summary-json",
                                                str(summary_path),
                                            ]
                                        )
            summary = json.loads(summary_path.read_text(encoding="utf-8"))

        self.assertEqual(2, exit_code)
        self.assertEqual("LOCAL_NEGATIVE_EVIDENCE_TYPE_MISMATCH_UNITY_PROOF_PENDING", summary["verdict"])
        self.assertEqual("completed", summary["messageResults"][0]["negativePublishOutcome"])

    def test_qos_negative_requires_a_current_ready_identity_before_full_expected_negative(self) -> None:
        """A no-apply window without a current Unity READY identity cannot certify a QoS rejection."""

        endpoint = self.smoke.EndpointEvidence(
            "std_msgs/msg/String",
            1,
            "reliable",
            "keep_last",
            10,
            "volatile",
        )
        interface = self.smoke.CommandResult(("ros2", "interface", "show"), 0, "std_msgs/msg/String\n", False)
        completed = self.smoke.CommandResult(("ros2", "topic", "pub"), 0, "", False)
        with tempfile.TemporaryDirectory() as temp:
            summary_path = Path(temp) / "qos-negative-summary.json"
            unity_log = Path(temp) / "Unity.log"
            unity_log.write_text("Unity started\n", encoding="utf-8")
            with mock.patch.object(self.smoke, "build_linux_environment", return_value={}):
                with mock.patch.object(self.smoke, "collect_optional_windows_peer_diagnostic", return_value="not-requested"):
                    with mock.patch.object(self.smoke, "configure_zenoh_topology", return_value="not-applicable"):
                        with mock.patch.object(self.smoke, "find_ros2_executable", return_value=Path("/usr/bin/ros2")):
                            with mock.patch.object(self.smoke, "wait_for_unity_subscription_topic"):
                                with mock.patch.object(self.smoke, "query_unity_subscription_endpoint", return_value=endpoint):
                                    with mock.patch.object(self.smoke, "run_bounded_command", side_effect=[interface, completed]):
                                        exit_code = self.smoke.main(
                                            [
                                                "--negative-case",
                                                "qos-incompatible",
                                                "--message-set",
                                                "string",
                                                "--token",
                                                "phase179-negative-qos",
                                                "--unity-log",
                                                str(unity_log),
                                                "--timeout-seconds",
                                                "0.01",
                                                "--summary-json",
                                                str(summary_path),
                                            ]
                                        )
            summary = json.loads(summary_path.read_text(encoding="utf-8"))

        self.assertEqual(2, exit_code)
        self.assertEqual("FAIL_READY", summary["verdict"])
        self.assertEqual("best_effort", summary["messageResults"][0]["attemptedQosReliability"])
        self.assertFalse(summary["messageResults"][0]["unityNoApply"])
        self.assertFalse(summary["messageResults"][0]["unityCounterUnchanged"])
        self.assertNotEqual("PASS", summary["verdict"])

    def test_rmw_negative_observes_absence_without_constructing_a_fallback_publication(self) -> None:
        """RMW mismatch keeps the caller-selected environment and only records bounded non-discovery."""

        interface = self.smoke.CommandResult(("ros2", "interface", "show"), 0, "std_msgs/msg/String\n", False)
        with tempfile.TemporaryDirectory() as temp:
            summary_path = Path(temp) / "rmw-negative-summary.json"
            with mock.patch.object(self.smoke, "build_linux_environment", return_value={}):
                with mock.patch.object(self.smoke, "collect_optional_windows_peer_diagnostic", return_value="not-requested"):
                    with mock.patch.object(self.smoke, "configure_zenoh_topology", return_value="not-applicable"):
                        with mock.patch.object(self.smoke, "find_ros2_executable", return_value=Path("/usr/bin/ros2")):
                            with mock.patch.object(self.smoke, "wait_for_unity_subscription_absence") as absent:
                                with mock.patch.object(self.smoke, "build_negative_publish_command") as build_negative:
                                    with mock.patch.object(self.smoke, "run_bounded_command", return_value=interface) as command:
                                        exit_code = self.smoke.main(
                                            [
                                                "--negative-case",
                                                "rmw-mismatch",
                                                "--rmw",
                                                "rmw_fastrtps_cpp",
                                                "--negative-peer-rmw",
                                                "rmw_zenoh_cpp",
                                                "--message-set",
                                                "string",
                                                "--token",
                                                "phase179-negative-rmw",
                                                "--summary-json",
                                                str(summary_path),
                                            ]
                                        )
            summary = json.loads(summary_path.read_text(encoding="utf-8"))

        self.assertEqual(2, exit_code)
        absent.assert_called_once()
        build_negative.assert_not_called()
        self.assertEqual(1, command.call_count)
        self.assertEqual("rmw_fastrtps_cpp", summary["rmwImplementation"])
        self.assertEqual("rmw_zenoh_cpp", summary["negativePeerRmw"])
        self.assertEqual("LOCAL_NEGATIVE_EVIDENCE_RMW_MISMATCH_UNITY_PROOF_PENDING", summary["verdict"])
        self.assertNotEqual("PASS", summary["verdict"])

    def test_no_unity_log_is_explicit_peer_publish_pending_not_pass(self) -> None:
        """Linux publication alone must never produce a green interop result."""

        verdict = self.smoke.classify_verdict(
            unity_log_available=False,
            message_results=[{"name": "string", "published": True}],
            failure=None,
        )

        self.assertEqual("PEER_PUBLISH_COMPLETE_UNITY_PROOF_PENDING", verdict)
        self.assertNotEqual("PASS", verdict)

    def test_string_burst_uses_bounded_rclpy_argv_and_requires_final_latest_wins_marker(self) -> None:
        """Burst acceptance proves the final sequence survived without requiring every intermediate apply."""

        token = "phase179-burst"
        command = self.smoke.build_string_burst_command(
            Path("/usr/bin/python3"),
            "/foxrun/phase179/string",
            token,
            final_sequence=8,
            rate_hz=500.0,
        )
        self.assertEqual(str(Path("/usr/bin/python3")), command[0])
        self.assertEqual("-c", command[1])
        self.assertIn("rclpy", command[2])
        self.assertIn("get_subscription_count", command[2])
        self.assertIn("DurabilityPolicy", command[2])
        self.assertIn("durability=DurabilityPolicy.VOLATILE", command[2])
        self.assertEqual("8", command[-2])
        self.assertEqual("500.0", command[-1])

        baseline = self.smoke.UnityMarker(
            session=3,
            topic="/foxrun/phase179/string",
            token=token,
            received=1,
            applied=1,
            replaced=0,
            value={"type": "String", "data": token},
        )
        final = self.smoke.UnityMarker(
            session=3,
            topic="/foxrun/phase179/string",
            token=token,
            received=9,
            applied=2,
            replaced=7,
            value=self.smoke.expected_string_burst_value(token, 8),
        )

        evidence = self.smoke.validate_string_burst_marker(baseline, final, token, 8)

        self.assertEqual(8, evidence["finalSequence"])
        self.assertEqual(9, evidence["total"])
        self.assertEqual(7, evidence["replaced"])

    def test_string_burst_rejects_zero_final_sequence_before_constructing_a_probe(self) -> None:
        """A one-message burst cannot prove latest-wins replacement and is not an acceptance case."""

        with self.assertRaises(ValueError):
            self.smoke.build_string_burst_command(
                Path("/usr/bin/python3"),
                "/foxrun/phase179/string",
                "phase179-burst-zero",
                final_sequence=0,
                rate_hz=500.0,
            )

    def test_string_burst_rejects_final_marker_without_latest_wins_replacement(self) -> None:
        """A final value alone is insufficient if the configured overload never exercised replacement."""

        token = "phase179-burst"
        baseline = self.smoke.UnityMarker(3, "/foxrun/phase179/string", token, 1, 1, 0, {"type": "String", "data": token})
        final = self.smoke.UnityMarker(
            3,
            "/foxrun/phase179/string",
            token,
            9,
            9,
            0,
            self.smoke.expected_string_burst_value(token, 8),
        )

        with self.assertRaises(self.smoke.AcceptanceFailure) as context:
            self.smoke.validate_string_burst_marker(baseline, final, token, 8)

        self.assertEqual("BURST", context.exception.category)

    def test_wait_for_unity_marker_times_out_with_stable_category(self) -> None:
        """A missing bounded Unity proof is a Unity timeout, not a publish pass."""

        with tempfile.TemporaryDirectory() as temp:
            log = Path(temp) / "Unity.log"
            log.write_text("Unity started\n", encoding="utf-8")
            with self.assertRaises(self.smoke.AcceptanceFailure) as context:
                self.smoke.wait_for_unity_marker(
                    log,
                    "/foxrun/phase179/string",
                    "phase179-timeout",
                    {"data": "phase179-timeout"},
                    timeout_seconds=0.0,
                )

        self.assertEqual("UNITY_TIMEOUT", context.exception.category)

    def test_bounded_command_terminates_only_its_owned_process_after_timeout(self) -> None:
        """Timeout cleanup targets the launched CLI process and never a global ROS process list."""

        process = mock.Mock()
        process.pid = 321
        process.communicate.side_effect = [
            subprocess.TimeoutExpired(["ros2"], 0.1),
            ("timed out", None),
        ]
        process.returncode = -9
        with mock.patch.object(self.smoke.subprocess, "Popen", return_value=process):
            with mock.patch.object(self.smoke, "terminate_owned_process") as terminate:
                result = self.smoke.run_bounded_command(
                    ["ros2", "topic", "list"],
                    {},
                    timeout_seconds=0.1,
                    label="topic list",
                )

        self.assertTrue(result.timed_out)
        terminate.assert_called_once_with(process)

    def test_summary_sanitization_does_not_persist_zenoh_paths_or_secrets(self) -> None:
        """Machine topology details and credentials stay out of portable evidence JSON."""

        payload = self.smoke.sanitize_summary(
            {
                "token": "correlation-token",
                "zenohRouterPath": "C:/private/router?password=super-secret",
                "error": "password=super-secret",
                "messageResults": [],
            }
        )
        serialized = json.dumps(payload)

        self.assertIn("correlation-token", serialized)
        self.assertNotIn("super-secret", serialized)
        self.assertNotIn("zenohRouterPath", payload)


if __name__ == "__main__":
    unittest.main()
