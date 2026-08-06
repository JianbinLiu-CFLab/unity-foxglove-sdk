#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Regression checks for the pure Phase181 custom-interface peer protocol."""

from __future__ import annotations

import importlib.util
import json
import pathlib
import sys
import unittest
from dataclasses import dataclass

from Scripts.test_support.phase181_scratch import temporary_directory


ROOT = pathlib.Path(__file__).resolve().parents[4]
PROTOCOL_PATH = ROOT / "Scripts" / "smoke" / "ros2" / "phase181_custom_ros2_peer_protocol.py"


def load_protocol_module():
    """Load the Phase181 module under test."""
    spec = importlib.util.spec_from_file_location("phase181_custom_ros2_peer_protocol", PROTOCOL_PATH)
    if spec is None or spec.loader is None:
        raise RuntimeError("Could not load the Phase181 custom ROS2 peer protocol module.")
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


@dataclass
class FakeProcess:
    """Small process fake that records only the expected cleanup call."""

    pid: int = 7181
    returncode: int | None = None
    terminate_calls: int = 0
    kill_calls: int = 0
    wait_calls: int = 0

    def poll(self):
        """Provide the required process-control test-double method."""
        return self.returncode

    def terminate(self):
        """Provide the required process-control test-double method."""
        self.terminate_calls += 1

    def kill(self):
        """Provide the required process-control test-double method."""
        self.kill_calls += 1

    def wait(self, timeout=None):
        """Provide the required process-control test-double method."""
        self.wait_calls += 1
        self.returncode = 0
        return self.returncode


class Phase181CustomRos2PeerProtocolTests(unittest.TestCase):
    """Keep the protocol from accepting one-sided or secret-bearing evidence."""

    def test_phase181_test_scratch_stays_inside_the_repository_build_root(self):
        """Verify Phase181 test fixtures do not use the host temporary directory."""
        with temporary_directory("peer-protocol-") as temporary:
            self.assertTrue(pathlib.Path(temporary).is_relative_to(ROOT / "build" / "Tests" / "Phase181"))

    def test_summary_redacts_token_environment_and_error_text_atomically(self):
        """Verify Phase181 behavior: summary redacts token environment and error text atomically."""
        protocol = load_protocol_module()
        with temporary_directory("peer-protocol-") as temporary:
            destination = pathlib.Path(temporary) / "summary.json"
            protocol.write_summary_atomic(
                destination,
                {
                    "phase": 181,
                    "token": "local-token-should-not-persist",
                    "environment": {"ZENOH_CONFIG": "secret-router"},
                    "error": "token=local-token-should-not-persist",
                    "safe": {"profile": "lyrical-zenoh"},
                },
            )
            text = destination.read_text(encoding="utf-8")
            summary = json.loads(text)

        self.assertNotIn("local-token-should-not-persist", text)
        self.assertNotIn("secret-router", text)
        self.assertEqual("redacted", summary["token"])
        self.assertEqual("redacted", summary["error"])
        self.assertNotIn("environment", summary)
        self.assertEqual("lyrical-zenoh", summary["safe"]["profile"])
        self.assertEqual(protocol.SUMMARY_SCHEMA_VERSION, summary["summarySchemaVersion"])

    def test_exact_interface_digest_is_required_for_positive_evidence(self):
        """Verify Phase181 behavior: exact interface digest is required for positive evidence."""
        protocol = load_protocol_module()
        expected = "a" * 64
        self.assertEqual(expected, protocol.require_interface_digest(expected, expected))
        with self.assertRaisesRegex(protocol.ProtocolFailure, "FAIL_INTERFACE_DIGEST"):
            protocol.require_interface_digest(expected, "a" * 63 + "b")
        with self.assertRaisesRegex(protocol.ProtocolFailure, "FAIL_INTERFACE_DIGEST"):
            protocol.require_interface_digest(expected, "not-a-digest")

    def test_state_machine_refuses_to_skip_correlation_or_clean_stop(self):
        """Verify Phase181 behavior: state machine refuses to skip correlation or clean stop."""
        protocol = load_protocol_module()
        run = protocol.EvidenceStateMachine(now=lambda: 42.0)
        with self.assertRaisesRegex(protocol.ProtocolFailure, "FAIL_STATE_TRANSITION"):
            run.transition(protocol.ProtocolState.PASS)

        for state in (
            protocol.ProtocolState.PEER_SOURCE_READY,
            protocol.ProtocolState.STRING_SUBSCRIBER_WAITING,
            protocol.ProtocolState.UNITY_READY,
            protocol.ProtocolState.STRING_CORRELATED,
            protocol.ProtocolState.PROBES_RUNNING,
            protocol.ProtocolState.UNITY_APPLIED,
            protocol.ProtocolState.ORIGIN_CHECKED,
            protocol.ProtocolState.CLEAN_STOP,
            protocol.ProtocolState.PASS,
        ):
            run.transition(state)

        self.assertEqual(protocol.ProtocolState.PASS, run.state)
        self.assertEqual(10, len(run.transitions))

    def test_state_machine_records_failure_without_fake_precheck_rewind(self):
        """Failure evidence must show a terminal FAILED state after progress."""
        protocol = load_protocol_module()
        run = protocol.EvidenceStateMachine(now=lambda: 42.0)
        run.transition(protocol.ProtocolState.PEER_SOURCE_READY)

        run.fail("FAIL_TEST")

        self.assertEqual(protocol.ProtocolState.FAILED, run.state)
        self.assertEqual(protocol.ProtocolState.FAILED, run.transitions[-1].state)
        with self.assertRaisesRegex(protocol.ProtocolFailure, "FAIL_STATE_TRANSITION"):
            run.transition(protocol.ProtocolState.STRING_SUBSCRIBER_WAITING)

    def test_marker_offset_scanning_is_append_only_and_deduplicates_repeated_lines(self):
        """Verify Phase181 behavior: marker offset scanning is append only and deduplicates repeated lines."""
        protocol = load_protocol_module()
        marker = "PHASE181_CUSTOM_ROS2_APPLIED runtime=lyrical token=opaque direction=input"
        with temporary_directory("peer-protocol-") as temporary:
            log = pathlib.Path(temporary) / "player.log"
            log.write_text("old line\n" + marker + "\n", encoding="utf-8")
            offset = protocol.log_offset(log)
            with log.open("a", encoding="utf-8") as stream:
                stream.write(marker + "\n")
                stream.write("PHASE181_CUSTOM_ROS2_READY runtime=lyrical token=opaque\n")

            markers, end_offset = protocol.read_new_markers(log, offset)

        self.assertGreater(end_offset, offset)
        self.assertEqual(["PHASE181_CUSTOM_ROS2_APPLIED", "PHASE181_CUSTOM_ROS2_READY"], [item.name for item in markers])

    def test_marker_offset_scanning_recovers_when_unity_truncates_the_batch_log(self):
        """Verify Phase181 behavior: a fresh Batch log is observed after Unity replaces an older log file."""
        protocol = load_protocol_module()
        with temporary_directory("peer-protocol-") as temporary:
            log = pathlib.Path(temporary) / "unity-editor-batch.log"
            log.write_text("previous batch output\n" * 64, encoding="utf-8")
            offset = protocol.log_offset(log)
            log.write_text(
                "PHASE181_CUSTOM_ROS2_READY runtime=lyrical rmw=rmw_fastrtps_cpp token=opaque\n"
                "PHASE181_CUSTOM_INTERFACE_READY interface=v1 digest=120864853239 token=opaque\n",
                encoding="utf-8",
            )

            markers, end_offset = protocol.read_new_markers(log, offset)
            final_size = log.stat().st_size

        self.assertEqual(
            ["PHASE181_CUSTOM_ROS2_READY", "PHASE181_CUSTOM_INTERFACE_READY"],
            [item.name for item in markers],
        )
        self.assertLess(end_offset, offset)
        self.assertEqual(final_size, end_offset)

    def test_interface_ready_marker_is_recognized_for_custom_correlation(self):
        """Verify Phase181 behavior: custom-interface readiness is a protocol marker."""
        protocol = load_protocol_module()

        marker = protocol.parse_marker_line(
            "PHASE181_CUSTOM_INTERFACE_READY interface=v1 digest=120864853239 token=opaque"
        )

        self.assertIsNotNone(marker)
        self.assertEqual("PHASE181_CUSTOM_INTERFACE_READY", marker.name)
        self.assertEqual("opaque", marker.fields["token"])

    def test_marker_parser_recovers_marker_concatenated_after_concurrent_logger_prefix(self):
        """Verify Phase181 behavior: a native warning cannot hide an adjacent Unity marker."""
        protocol = load_protocol_module()

        marker = protocol.parse_marker_line(
            "[WARNING] direct spin fallback timeout."
            "PHASE181_CUSTOM_ROS2_READY runtime=lyrical rmw=rmw_zenoh_cpp token=opaque"
        )

        self.assertIsNotNone(marker)
        self.assertEqual("PHASE181_CUSTOM_ROS2_READY", marker.name)
        self.assertEqual("opaque", marker.fields["token"])
        self.assertTrue(marker.raw.startswith("PHASE181_CUSTOM_ROS2_READY "))
        self.assertIsNone(
            protocol.parse_marker_line(
                "NOT_PHASE181_CUSTOM_ROS2_READY runtime=lyrical rmw=rmw_zenoh_cpp token=opaque"
            )
        )

    def test_nullable_empty_payload_and_envelope_metadata_are_verified(self):
        """Verify Phase181 behavior: nullable empty payload and envelope metadata are verified."""
        protocol = load_protocol_module()
        payload = {
            "count": 7,
            "kind": 1,
            "message": "",
            "has_message": True,
            "bytes": [],
            "has_bytes": True,
            "values": [],
            "has_values": True,
            "optional_count": 0,
            "has_optional_count": False,
            "nested": {"enabled": False, "label": ""},
            "has_nested": False,
            "optional_text": "",
            "has_optional_text": False,
        }
        protocol.require_nullable_empty_payload(payload)
        self.assertEqual(
            9,
            protocol.require_envelope_metadata({"foxrun_sequence": 9, "foxrun_stamp": {"sec": 12, "nanosec": 0}}, 8),
        )
        with self.assertRaisesRegex(protocol.ProtocolFailure, "FAIL_ENVELOPE_METADATA"):
            protocol.require_envelope_metadata({"foxrun_sequence": 8, "foxrun_stamp": {"sec": 12, "nanosec": 1_000_000_000}}, 8)

    def test_owned_process_cleanup_never_uses_posix_killpg_for_windows(self):
        """Verify Phase181 behavior: owned process cleanup never uses posix killpg for windows."""
        protocol = load_protocol_module()
        process = FakeProcess()
        posix_calls: list[tuple[int, int]] = []

        protocol.terminate_owned_process(
            process,
            platform_name="nt",
            killpg=lambda pid, signal: posix_calls.append((pid, signal)),
        )

        self.assertEqual([], posix_calls)
        self.assertEqual(1, process.terminate_calls)
        self.assertEqual(1, process.wait_calls)

    def test_owned_process_cleanup_uses_killpg_only_for_owned_posix_process(self):
        """Verify Phase181 behavior: owned process cleanup uses killpg only for owned posix process."""
        protocol = load_protocol_module()
        process = FakeProcess(pid=8123)
        posix_calls: list[tuple[int, int]] = []

        protocol.terminate_owned_process(
            process,
            platform_name="posix",
            killpg=lambda pid, signal: posix_calls.append((pid, signal)),
        )

        self.assertEqual([(8123, protocol.signal.SIGTERM)], posix_calls)
        self.assertEqual(0, process.terminate_calls)


if __name__ == "__main__":
    unittest.main()
