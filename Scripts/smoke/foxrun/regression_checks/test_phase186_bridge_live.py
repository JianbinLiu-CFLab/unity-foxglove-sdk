#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Regression tests for Phase186-H owned live orchestration helpers."""

from __future__ import annotations

import json
import contextlib
import io
import pathlib
import struct
import tempfile
import types
import unittest
from unittest import mock

from Scripts.smoke.foxrun import phase186_bridge_acceptance as acceptance
from Scripts.smoke.foxrun import phase186_bridge_acceptance_protocol as live_protocol
from Scripts.smoke.foxrun import phase186_bridge_live as live
from Scripts.smoke.foxrun import phase186_bridge_live_peer as live_peer


class _FakeOwner:
    def residual_pids(self) -> list[int]:
        return []


class _ChunkSocket:
    def __init__(self, chunks: list[bytes]):
        self._chunks = list(chunks)

    def recv(self, count: int) -> bytes:
        if not self._chunks:
            return b""
        value = self._chunks.pop(0)
        if len(value) <= count:
            return value
        self._chunks.insert(0, value[count:])
        return value[:count]


class Phase186BridgeLiveTests(unittest.TestCase):
    def test_current_manual_progress_requires_exact_identity_and_emits_once(self) -> None:
        reporter = mock.Mock()
        emitted: set[str] = set()
        with tempfile.TemporaryDirectory() as temp:
            output = pathlib.Path(temp)
            log = output / "unity.log"
            config = {
                "unityLog": str(log),
                "runId": "phase186h-current-0123456789ab",
                "caseId": "manual-jazzy-fastrtps-duplex",
                "tokenHash": "a" * 64,
                "head": "b" * 40,
            }
            exact = (
                "PHASE186_ACCEPTANCE_PROGRESS "
                "run=phase186h-current-0123456789ab "
                "case=manual-jazzy-fastrtps-duplex "
                f"tokenHash={'a' * 64} head={'b' * 40} "
                "ready=true external=true generated=true caseSpecific=true "
                "connected=true publish=Ready subscribe=Ready externalA=true"
            )
            log.write_text(
                exact.replace("phase186h-current", "phase186h-foreign")
                + "\n"
                + exact.replace("tokenHash=" + "a" * 64, "tokenHash=" + "c" * 64)
                + "\n"
                + exact.replace("head=" + "b" * 40, "head=" + "d" * 40)
                + "\n"
                + exact
                + "\n"
                + exact
                + "\n",
                encoding="utf-8",
            )

            documents = live._unity_progress_documents(config)
            live._report_manual_progress(config, reporter, emitted)
            live._report_manual_progress(config, reporter, emitted)

        self.assertEqual(2, len(documents))
        self.assertEqual(
            [
                mock.call("provider directions ready"),
                mock.call("external A observed"),
                mock.call("local B published; waiting for peer verification"),
                mock.call("peer verification complete; Complete enabled"),
            ],
            reporter.detail.call_args_list,
        )

    def test_live_startup_interrupt_closes_owner_and_writes_real_cleanup(self) -> None:
        config = {
            "outputRoot": "unused",
            "runtimeRowId": "jazzy-fastrtps",
            "bridgeHost": "127.0.0.1",
            "bridgePort": 18767,
            "foxgloveHost": "127.0.0.1",
            "foxglovePort": 18768,
            "externalGate": "external-gate.json",
            "exerciseGate": "exercise-gate.json",
        }
        runtime = types.SimpleNamespace(zenoh_router=None, zenoh_endpoint=None)
        owner = mock.Mock()
        owner.residual_pids.return_value = []
        cleanup = {
            "complete": True,
            "cleanupErrors": [],
            "residualProcesses": [],
            "residualPorts": [],
            "residualOverlays": [],
            "residualTemporaryProjects": [],
        }
        reporter = mock.Mock()
        with mock.patch.object(live, "prepare_runtime", return_value=runtime), \
                mock.patch.object(live, "OwnedLiveProcesses", return_value=owner), \
                mock.patch.object(live, "_launch_sidecar", side_effect=KeyboardInterrupt), \
                mock.patch.object(live, "_cleanup_document", return_value=cleanup) as write_cleanup, \
                self.assertRaises(KeyboardInterrupt):
            live.run_live(
                pathlib.Path("D:/repo"),
                config,
                unity_editor=pathlib.Path("Unity.exe"),
                manual_timeout_seconds=30.0,
                reporter=reporter,
            )

        owner.close.assert_called_once_with()
        write_cleanup.assert_called_once()
        reporter.transition.assert_any_call(
            "5/5", "validating completion and cleaning owned resources"
        )

    def test_runtime_prepare_interrupt_reports_cleanup_stage_before_rethrow(self) -> None:
        reporter = mock.Mock()
        with mock.patch.object(live, "prepare_runtime", side_effect=KeyboardInterrupt), \
                mock.patch.object(live, "OwnedLiveProcesses") as owner, \
                self.assertRaises(KeyboardInterrupt):
            live.run_live(
                pathlib.Path("D:/repo"),
                {"outputRoot": "unused"},
                unity_editor=pathlib.Path("Unity.exe"),
                manual_timeout_seconds=30.0,
                reporter=reporter,
            )

        reporter.transition.assert_called_once_with(
            "5/5", "validating completion and cleaning owned resources"
        )
        owner.assert_not_called()

    def test_manual_wait_interrupt_removes_only_owned_pointer_and_gates(self) -> None:
        config = {
            "outputRoot": "unused",
            "runtimeRowId": "jazzy-fastrtps",
            "bridgeHost": "127.0.0.1",
            "bridgePort": 18767,
            "foxgloveHost": "127.0.0.1",
            "foxglovePort": 18768,
            "externalGate": "external-gate.json",
            "exerciseGate": "exercise-gate.json",
            "caseId": "manual-jazzy-fastrtps-duplex",
            "manual": True,
            "requiredActors": [],
            "runId": "phase186h-current-0123456789ab",
            "tokenHash": "a" * 64,
            "head": "b" * 40,
            "unityLog": "unity.log",
        }
        runtime = types.SimpleNamespace(
            zenoh_router=None,
            zenoh_endpoint=None,
            row_id="jazzy-fastrtps",
            distro="jazzy",
            rmw="rmw_fastrtps_cpp",
        )
        owner = mock.Mock()
        owner.residual_pids.return_value = []
        pointer = pathlib.Path("owned-pointer.json")
        cleanup = {
            "complete": True,
            "cleanupErrors": [],
            "residualProcesses": [],
            "residualPorts": [],
            "residualOverlays": [],
            "residualTemporaryProjects": [],
        }
        reporter = mock.Mock()
        def prepare(*_args, **_kwargs):
            reporter.transition("2/5", "preparing exact runtime and Bridge build")
            return runtime

        stdout = io.StringIO()
        with mock.patch.object(live, "prepare_runtime", side_effect=prepare), \
                mock.patch.object(live, "OwnedLiveProcesses", return_value=owner), \
                mock.patch.object(live, "_launch_sidecar", return_value=(mock.Mock(), {})), \
                mock.patch.object(live, "_write_manual_pointer", return_value=pointer), \
                mock.patch.object(live, "_mirror_manual_log", side_effect=KeyboardInterrupt), \
                mock.patch.object(live, "_remove_manual_pointer") as remove_pointer, \
                mock.patch.object(live, "_cleanup_document", return_value=cleanup), \
                contextlib.redirect_stdout(stdout), \
                self.assertRaises(KeyboardInterrupt):
            live.run_live(
                pathlib.Path("D:/repo"),
                config,
                unity_editor=pathlib.Path("Unity.exe"),
                manual_timeout_seconds=30.0,
                reporter=reporter,
            )

        remove_pointer.assert_called_once_with(pointer, config)
        owner.close.assert_called_once_with()
        self.assertNotIn("PHASE186_MANUAL_READY", stdout.getvalue())
        self.assertEqual(
            [
                mock.call.transition(
                    "2/5", "preparing exact runtime and Bridge build"
                ),
                mock.call.transition(
                    "3/5", "starting and proving sidecar, peer, observer, and router"
                ),
                mock.call.transition("4/5", "manual Unity run is ready"),
                mock.call.unity_ready(
                    "the Phase186 ROS2 Bridge acceptance scene"
                ),
                mock.call.transition(
                    "5/5", "validating completion and cleaning owned resources"
                ),
            ],
            reporter.mock_calls,
        )
    def test_ros_graph_gate_requires_exact_bridge_publisher(self) -> None:
        config = {
            "caseId": "slow-main-thread-640hz",
            "topics": ("/phase186/slow_ingress", "/phase186/slow_control"),
        }

        def endpoint(node_name: str, topic_type: str) -> object:
            return types.SimpleNamespace(
                node_name=node_name,
                topic_type=topic_type,
            )

        bridge_custom = endpoint(
            live_peer.BRIDGE_NODE_NAME,
            live_protocol.INTERFACE_TYPE,
        )
        bridge_standard = endpoint(
            live_peer.BRIDGE_NODE_NAME,
            "foxglove_msgs/msg/Log",
        )
        peer_standard = endpoint(
            "phase186_peer_self",
            "foxglove_msgs/msg/Log",
        )
        node = mock.Mock()
        node.get_subscriptions_info_by_topic.side_effect = lambda topic: (
            [bridge_custom]
            if topic.endswith("slow_ingress")
            else [bridge_standard]
        )
        node.get_publishers_info_by_topic.side_effect = lambda topic: (
            []
            if topic.endswith("slow_ingress")
            else [peer_standard]
        )

        self.assertFalse(live_peer._bridge_endpoints_ready(node, config))

        node.get_publishers_info_by_topic.side_effect = lambda topic: (
            []
            if topic.endswith("slow_ingress")
            else [peer_standard, bridge_standard]
        )
        self.assertTrue(live_peer._bridge_endpoints_ready(node, config))

    def test_custom_peer_sets_nested_string_presence(self) -> None:
        class Envelope:
            pass

        class Payload:
            pass

        class Nested:
            def __init__(self) -> None:
                self.foxrun_has_label = False

        node = mock.Mock()
        stamp = object()
        node.get_clock.return_value.now.return_value.to_msg.return_value = stamp

        value = live_peer._custom_message(
            Envelope,
            Payload,
            Nested,
            node,
            {"tokenHash": "a" * 64},
            sequence=1,
        )

        self.assertEqual("external-a", value.payload.nested.label)
        self.assertTrue(value.payload.nested.foxrun_has_label)

    def test_source_publish_keeps_ros_peer_alive_for_bounded_delivery(self) -> None:
        ros = mock.Mock()
        node = object()
        with mock.patch.object(
            live_peer.time,
            "monotonic",
            side_effect=(10.0, 10.0, 10.03, 10.06),
        ):
            live_peer._settle_source_delivery(ros, node, 0.05)

        self.assertEqual(2, ros.spin_once.call_count)
        timeouts = [call.kwargs["timeout_sec"] for call in ros.spin_once.call_args_list]
        self.assertAlmostEqual(0.05, timeouts[0])
        self.assertAlmostEqual(0.02, timeouts[1])

    def test_duplex_origin_check_consumes_one_direct_sample_per_sequence(self) -> None:
        token_hash = "a" * 64

        def sample(sequence: int, label: str = "external-a") -> object:
            payload = types.SimpleNamespace(
                message=f"phase186:{token_hash[:12]}:{sequence}:{label}"
            )
            return types.SimpleNamespace(
                foxrun_sequence=sequence,
                payload=payload,
            )

        direct_one = sample(1)
        direct_two = sample(2)
        bridge_echo_one = sample(1)
        bridge_local = sample(9, "unity-local-b")

        actual = live_peer._without_direct_peer_samples(
            [direct_two, direct_one, bridge_local, bridge_echo_one],
            "custom_duplex",
            token_hash,
            offered=8,
            consume_direct=True,
        )

        self.assertEqual([bridge_local, bridge_echo_one], actual)

    def test_direct_sample_filter_keeps_malformed_or_out_of_run_samples(self) -> None:
        token_hash = "b" * 64
        wrong_envelope_sequence = types.SimpleNamespace(
            foxrun_sequence=2,
            payload=types.SimpleNamespace(
                message=f"phase186:{token_hash[:12]}:1:external-a"
            ),
        )
        other_run = types.SimpleNamespace(
            foxrun_sequence=1,
            payload=types.SimpleNamespace(
                message="phase186:cccccccccccc:1:external-a"
            ),
        )
        oversized_sequence = types.SimpleNamespace(
            foxrun_sequence=1,
            payload=types.SimpleNamespace(
                message=(
                    f"phase186:{token_hash[:12]}:"
                    + "9" * 5000
                    + ":external-a"
                )
            ),
        )

        actual = live_peer._without_direct_peer_samples(
            [wrong_envelope_sequence, other_run, oversized_sequence],
            "custom_duplex",
            token_hash,
            offered=8,
            consume_direct=True,
        )

        self.assertEqual(
            [wrong_envelope_sequence, other_run, oversized_sequence],
            actual,
        )

    def test_direct_sample_filter_uses_standard_message_sequence(self) -> None:
        token_hash = "d" * 64
        direct = types.SimpleNamespace(
            message=f"phase186:{token_hash[:12]}:7:external-a"
        )
        duplicate = types.SimpleNamespace(
            message=f"phase186:{token_hash[:12]}:7:external-a"
        )

        actual = live_peer._without_direct_peer_samples(
            [direct, duplicate],
            "standard_duplex",
            token_hash,
            offered=8,
            consume_direct=True,
        )

        self.assertEqual([duplicate], actual)

    def test_actor_readiness_budget_outlives_coordinator_budget(self) -> None:
        self.assertGreaterEqual(
            live_protocol.COORDINATOR_UNITY_READY_TIMEOUT_SECONDS,
            480.0,
        )
        self.assertGreater(
            live_protocol.ACTOR_UNITY_READY_TIMEOUT_SECONDS,
            live_protocol.COORDINATOR_UNITY_READY_TIMEOUT_SECONDS,
        )

    def test_live_actor_operation_budget_allows_slow_unity_and_ros_startup(self) -> None:
        self.assertEqual(300.0, live_peer.LIVE_ACTOR_OPERATION_TIMEOUT_SECONDS)
        self.assertGreater(
            live.LIVE_ACTOR_RESULT_TIMEOUT_SECONDS,
            live_peer.LIVE_ACTOR_OPERATION_TIMEOUT_SECONDS,
        )

    def test_slow_sequence_flood_waits_for_current_unity_baseline(self) -> None:
        windows = live_peer._sequence_windows(
            "slow-main-thread-640hz",
            offered=6,
        )
        self.assertEqual(
            ((1,), (2, 3, 4, 5, 6)),
            tuple(tuple(window) for window in windows),
        )

        with tempfile.TemporaryDirectory() as temp:
            unity_log = pathlib.Path(temp) / "unity.log"
            config = {
                "caseId": "slow-main-thread-640hz",
                "runId": "phase186h-slow-baseline-test",
                "unityLog": str(unity_log),
            }
            unity_log.write_text(
                "PHASE186_ACCEPTANCE_PROGRESS "
                "run=phase186h-stale-baseline case=slow-main-thread-640hz "
                "generated=true received=1 applied=1\n"
                "PHASE186_ACCEPTANCE_PROGRESS "
                "run=phase186h-slow-baseline-test case=slow-main-thread-640hz "
                "generated=true received=0 applied=0\n",
                encoding="utf-8",
            )
            self.assertFalse(live_peer._slow_unity_baseline_ready(config))
            with unity_log.open("a", encoding="utf-8") as stream:
                stream.write(
                    "PHASE186_ACCEPTANCE_PROGRESS "
                    "run=phase186h-slow-baseline-test "
                    "case=slow-main-thread-640hz "
                    "generated=true received=1 applied=1\n"
                )
            self.assertTrue(live_peer._slow_unity_baseline_ready(config))

    def test_reconnect_peer_waits_for_identity_bound_exercise_gate(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            gate = pathlib.Path(temp) / "exercise.json"
            config = {
                "caseId": "reconnect-degraded-recovery",
                "runId": "phase186h-reconnect-test",
                "tokenHash": "a" * 64,
                "head": "b" * 40,
                "exerciseGate": str(gate),
            }
            self.assertFalse(live_peer._identity_gate_ready(config, "exerciseGate"))
            gate.write_text(
                json.dumps(
                    {
                        "schemaVersion": 1,
                        "runId": config["runId"],
                        "caseId": config["caseId"],
                        "tokenHash": config["tokenHash"],
                        "head": "c" * 40,
                        "ready": True,
                    }
                ),
                encoding="utf-8",
            )
            self.assertFalse(live_peer._identity_gate_ready(config, "exerciseGate"))
            gate.write_text(
                json.dumps(
                    {
                        "schemaVersion": 1,
                        "runId": config["runId"],
                        "caseId": config["caseId"],
                        "tokenHash": config["tokenHash"],
                        "head": config["head"],
                        "ready": True,
                    }
                ),
                encoding="utf-8",
            )
            self.assertTrue(live_peer._identity_gate_ready(config, "exerciseGate"))
            with mock.patch.object(live_peer, "_wait_for_unity_ready") as unity_ready, \
                 mock.patch.object(live_peer, "_wait_for_exercise_gate") as exercise_ready:
                live_peer._wait_for_ros_exercise_window(config)
            unity_ready.assert_called_once_with(config)
            exercise_ready.assert_called_once_with(config)

    def test_reconnect_restart_waits_for_observed_disconnect_and_recovery(self) -> None:
        owner = mock.Mock()
        runtime = object()
        config = {"bridgeHost": "127.0.0.1", "bridgePort": 18605}
        health_generations: list[object] = []
        health = {"status": "ok", "generation": 2}
        with mock.patch.object(
            live,
            "_unity_progress_count",
            side_effect=(3, 4),
        ), mock.patch.object(
            live,
            "_wait_until_port_released",
        ) as port_released, mock.patch.object(
            live,
            "_wait_unity_progress_after",
        ) as progress, mock.patch.object(
            live,
            "_launch_sidecar",
            return_value=(object(), health),
        ) as launch, mock.patch.object(
            live,
            "_write_exercise_gate",
        ) as write_gate:
            live._restart_sidecar_after_observed_disconnect(
                owner,
                runtime,
                config,
                health_generations,
            )

        owner.stop.assert_called_once_with("sidecar-1")
        port_released.assert_called_once_with("127.0.0.1", 18605)
        self.assertEqual(
            [
                mock.call(config, owner, 3, {"ready": "false", "connected": "false"}),
                mock.call(
                    config,
                    owner,
                    4,
                    {
                        "ready": "true",
                        "connected": "true",
                        "publish": "Ready",
                        "subscribe": "Ready",
                    },
                ),
            ],
            progress.call_args_list,
        )
        launch.assert_called_once_with(owner, runtime, config, "sidecar-2")
        write_gate.assert_called_once_with(config)
        self.assertEqual([health], health_generations)

    def test_hostile_frames_encode_the_exact_declared_lengths(self) -> None:
        mutations = live_peer._hostile_mutations()
        unknown = mutations["unknown-op"]
        _version, _flags, header_size, payload_size = struct.unpack(
            "<HHII", unknown[4:16]
        )
        self.assertEqual(len(unknown) - 16, header_size + payload_size)
        self.assertEqual(
            {"op": "phase186_unknown_op"},
            json.loads(unknown[16 : 16 + header_size].decode("utf-8")),
        )

    def test_busy_response_uses_the_stable_protocol_error_code_field(self) -> None:
        self.assertTrue(
            live_peer._is_busy_response(
                {
                    "op": "busy",
                    "status": "error",
                    "errorCode": "busy",
                    "terminal": True,
                }
            )
        )
        self.assertFalse(
            live_peer._is_busy_response(
                {
                    "op": "busy",
                    "status": "error",
                    "code": "busy",
                    "terminal": True,
                }
            )
        )
        self.assertFalse(
            live_peer._is_busy_response(
                {
                    "op": "busy",
                    "status": "error",
                    "errorCode": "busy",
                    "terminal": False,
                }
            )
        )

    def test_optional_rejection_response_reassembles_fragmented_error_frame(self) -> None:
        header = json.dumps(
            {"status": "error", "code": "invalid_frame"},
            separators=(",", ":"),
        ).encode("ascii")
        frame = b"U2R2" + struct.pack("<HHII", 1, 0, len(header), 0) + header
        connection = _ChunkSocket([frame[:3], frame[3:9], frame[9:17], frame[17:]])
        self.assertEqual(frame, live_peer._read_optional_frame(connection))
        self.assertIsNone(live_peer._read_optional_frame(_ChunkSocket([b""])))

    def test_cleanup_detects_owned_gate_pointer_and_extra_listener(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            output = pathlib.Path(temp)
            gate = output / "unity-external-gate.json"
            exercise_gate = output / "unity-exercise-gate.json"
            pointer = output / "current-run.json"
            gate.write_text("{}", encoding="utf-8")
            exercise_gate.write_text("{}", encoding="utf-8")
            pointer.write_text("{}", encoding="utf-8")
            config = {
                "outputRoot": str(output),
                "bridgeHost": "127.0.0.1",
                "bridgePort": 18767,
                "foxgloveHost": "127.0.0.1",
                "foxglovePort": 18768,
                "externalGate": str(gate),
                "exerciseGate": str(exercise_gate),
            }
            cleanup = live._cleanup_document(
                config,
                _FakeOwner(),
                pointer,
                extra_endpoints=(("127.0.0.1", 18769),),
                cleanup_errors=("owner close failed",),
            )
            self.assertFalse(cleanup["complete"])
            self.assertIn(str(gate.resolve()), cleanup["residualTemporaryProjects"])
            self.assertIn(
                str(exercise_gate.resolve()),
                cleanup["residualTemporaryProjects"],
            )
            self.assertIn(str(pointer.resolve()), cleanup["residualTemporaryProjects"])
            self.assertIn("owner close failed", cleanup["cleanupErrors"])

    def test_coordinator_reloads_actual_cleanup_after_live_failure(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            output = pathlib.Path(temp)
            expected = {
                "complete": False,
                "residualProcesses": [186],
                "residualPorts": [18767],
                "residualOverlays": [],
                "residualTemporaryProjects": [],
                "cleanupErrors": ["owner close failed"],
            }
            (output / "cleanup.json").write_text(
                json.dumps(expected), encoding="utf-8"
            )
            self.assertEqual(
                expected,
                acceptance.load_cleanup_evidence_if_present(output),
            )

    def test_process_registry_has_public_presence_query(self) -> None:
        owner = object.__new__(live.OwnedLiveProcesses)
        owner._records = {"sidecar-2": object()}
        self.assertTrue(owner.has_record("sidecar-2"))
        self.assertFalse(owner.has_record("sidecar-1"))

    def test_ros_peer_cohosts_graph_observer_to_bound_fastdds_participants(self) -> None:
        config = {
            "requiredActors": [
                "graph-observer",
                "ros-peer",
                "sidecar",
                "unity",
            ]
        }

        self.assertEqual(("ros-peer",), live._worker_process_roles(config))
        self.assertEqual(
            "ros-peer",
            live._owner_role_for_document(config, "graph-observer"),
        )
        self.assertEqual(
            "ros-peer",
            live._owner_role_for_document(config, "ros-peer"),
        )

    def test_cohosted_graph_process_evidence_names_the_ros_peer_owner(self) -> None:
        process = mock.Mock()
        process.pid = 18602
        process.poll.return_value = 0
        owner = object.__new__(live.OwnedLiveProcesses)
        owner._records = {
            "ros-peer": types.SimpleNamespace(
                key="ros-peer",
                logical_role="ros-peer",
                executable=pathlib.Path("python.exe"),
                process=process,
                identity_verified=True,
                owner_requested=False,
            )
        }

        evidence = owner.actor_evidence(
            "graph-observer",
            preferred_key="ros-peer",
            allow_role_alias=True,
        )

        self.assertEqual("ros-peer", evidence["processRole"])
        self.assertTrue(evidence["cohosted"])
        self.assertEqual(18602, evidence["pid"])

    def test_cohosted_graph_documents_are_identity_bound_and_explicit(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            output = pathlib.Path(temp)
            config = {
                "outputRoot": str(output),
                "schemaVersion": 3,
                "runId": "phase186h-test-cohosted-graph",
                "caseId": "full-duplex",
                "tokenHash": "a" * 64,
                "head": "b" * 40,
                "runtimeRowId": "jazzy-fastrtps",
            }
            live_peer._write_cohosted_graph_ready(config)
            live_peer._write_cohosted_graph_result(
                config,
                {"source": "rclpy-graph-api", "topics": {"/topic": {}}},
            )

            ready = json.loads(
                (output / "actors" / "graph-observer-ready.json").read_text(
                    encoding="utf-8"
                )
            )
            result = json.loads(
                (output / "actors" / "graph-observer-result.json").read_text(
                    encoding="utf-8"
                )
            )
            self.assertEqual("ros-peer", ready["evidence"]["processRole"])
            self.assertTrue(ready["evidence"]["cohosted"])
            self.assertEqual("ros-peer", result["evidence"]["processRole"])
            self.assertTrue(result["evidence"]["cohosted"])

    def test_graph_observation_requires_the_exact_bridge_node(self) -> None:
        expected_type = live_protocol.INTERFACE_TYPE
        config = {"caseId": "full-duplex", "topics": ["/phase186/duplex"]}

        def endpoint(node_name: str) -> types.SimpleNamespace:
            return types.SimpleNamespace(
                node_name=node_name,
                node_namespace="/",
                topic_type=expected_type,
                qos_profile=types.SimpleNamespace(
                    reliability=types.SimpleNamespace(name="RELIABLE"),
                    durability=types.SimpleNamespace(name="VOLATILE"),
                    history=types.SimpleNamespace(name="KEEP_LAST"),
                    depth=10,
                ),
            )

        def observe(node_name: str) -> bool:
            info = endpoint(node_name)
            node = mock.Mock()
            node.get_publishers_info_by_topic.return_value = [info]
            node.get_subscriptions_info_by_topic.return_value = [info]
            ready: list[bool] = []

            def spin_once(_rclpy, _node, predicate, _timeout, _message) -> None:
                ready.append(bool(predicate()))

            with mock.patch.object(live_peer, "_spin_until", side_effect=spin_once):
                live_peer._observe_graph(mock.Mock(), node, config)
            self.assertEqual(1, len(ready))
            return ready[0]

        self.assertFalse(observe("phase186_peer_deadbeef"))
        self.assertTrue(observe("unity2foxglove_ros2_bridge"))

    def test_runtime_environment_includes_ros_pixi_dll_directory(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = pathlib.Path(temp) / "ros2"
            overlay = pathlib.Path(temp) / "overlay"
            pixi_bin = root / ".pixi" / "envs" / "default" / "Library" / "bin"
            pixi_bin.mkdir(parents=True)
            (overlay / "bin").mkdir(parents=True)
            with mock.patch.object(
                live.phase181_peer.ros2env,
                "build_ros_env",
                return_value={
                    "PATH": "ros-base",
                    "PYTHONPATH": "ros-python",
                    "AMENT_PREFIX_PATH": str(root),
                    "CMAKE_PREFIX_PATH": str(root),
                    "COLCON_PREFIX_PATH": str(root),
                },
            ) as build_ros_env:
                environment = live._build_runtime_environment(
                    {"PATH": "ambient"},
                    root,
                    overlay,
                    distro="jazzy",
                    rmw="rmw_fastrtps_cpp",
                    domain_id=161,
                    discovery_range="SUBNET",
                    topology_id="phase186h-test",
                    zenoh_session_config=None,
                )
            build_ros_env.assert_called_once_with(
                root,
                "rmw_fastrtps_cpp",
                "SUBNET",
                "161",
                "jazzy",
            )
            paths = environment["PATH"].split(";")
            self.assertIn(str(pixi_bin), paths)
            self.assertIn("ros-base", paths)
            self.assertNotIn("ambient", paths)

    def test_unity_environment_binds_only_all_provider_fanout_to_run_domain(
        self,
    ) -> None:
        source = {
            "PATH": "ambient",
            "ROS_DOMAIN_ID": "7",
            "ROS_DISTRO": "ambient-distro",
            "RMW_IMPLEMENTATION": "ambient-rmw",
            "AMENT_PREFIX_PATH": "ambient-prefix",
        }

        bridge_only = live._build_unity_environment(
            source,
            {"caseId": "full-duplex", "domainId": 161},
        )
        fanout = live._build_unity_environment(
            source,
            {
                "caseId": "fanout-fairness-health",
                "domainId": 162,
                "rmw": "rmw_fastrtps_cpp",
            },
        )

        self.assertNotIn("ROS_DOMAIN_ID", bridge_only)
        self.assertNotIn("FASTDDS_BUILTIN_TRANSPORTS", bridge_only)
        self.assertEqual("162", fanout["ROS_DOMAIN_ID"])
        self.assertEqual(
            "SUBNET",
            fanout["ROS_AUTOMATIC_DISCOVERY_RANGE"],
        )
        self.assertEqual("UDPv4", fanout["FASTDDS_BUILTIN_TRANSPORTS"])
        self.assertNotIn("ROS_AUTOMATIC_DISCOVERY_RANGE", bridge_only)
        for environment in (bridge_only, fanout):
            self.assertNotIn("ROS_DISTRO", environment)
            self.assertNotIn("RMW_IMPLEMENTATION", environment)
            self.assertNotIn("AMENT_PREFIX_PATH", environment)
            self.assertEqual("ambient", environment["PATH"])
        self.assertEqual("7", source["ROS_DOMAIN_ID"])


if __name__ == "__main__":
    unittest.main()
