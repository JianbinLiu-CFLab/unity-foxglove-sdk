#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Regression tests for Phase186-H owned live orchestration helpers."""

from __future__ import annotations

import json
import contextlib
import io
import os
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
    """Represent fake owner."""
    def residual_pids(self) -> list[int]:
        """Handle residual pids for Phase186 acceptance."""
        return []


class _ChunkSocket:
    """Represent chunk socket."""
    def __init__(self, chunks: list[bytes]):
        """Initialize the helper state."""
        self._chunks = list(chunks)

    def recv(self, count: int) -> bytes:
        """Handle recv for Phase186 acceptance."""
        if not self._chunks:
            return b""
        value = self._chunks.pop(0)
        if len(value) <= count:
            return value
        self._chunks.insert(0, value[count:])
        return value[:count]


class Phase186BridgeLiveTests(unittest.TestCase):
    """Group checks for phase186 bridge live tests."""

    def test_ros_peer_shutdown_runs_when_node_creation_fails(self) -> None:
        """Verify that initialized rclpy is shut down after node creation fails."""
        rclpy = types.ModuleType("rclpy")
        rclpy.init = mock.Mock()
        rclpy.shutdown = mock.Mock()
        rclpy.create_node = mock.Mock(side_effect=RuntimeError("create failed"))
        qos = types.ModuleType("rclpy.qos")
        qos.DurabilityPolicy = types.SimpleNamespace(VOLATILE=object())
        qos.HistoryPolicy = types.SimpleNamespace(KEEP_LAST=object())
        qos.ReliabilityPolicy = types.SimpleNamespace(RELIABLE=object())
        qos.QoSProfile = lambda **_kwargs: object()

        with mock.patch.dict(
            "sys.modules",
            {"rclpy": rclpy, "rclpy.qos": qos},
        ), mock.patch.object(
            live_peer,
            "_load_ros_types",
            return_value=(object(), object(), object(), object()),
        ), self.assertRaisesRegex(RuntimeError, "create failed"):
            live_peer.run_ros_peer({"tokenHash": "a" * 64})

        rclpy.init.assert_called_once_with(args=None)
        rclpy.shutdown.assert_called_once_with()

    def test_graph_observer_shutdown_runs_when_node_creation_fails(self) -> None:
        """Verify that initialized rclpy is shut down after observer creation fails."""
        rclpy = types.ModuleType("rclpy")
        rclpy.init = mock.Mock()
        rclpy.shutdown = mock.Mock()
        rclpy.create_node = mock.Mock(side_effect=RuntimeError("create failed"))

        with mock.patch.dict("sys.modules", {"rclpy": rclpy}), \
                self.assertRaisesRegex(RuntimeError, "create failed"):
            live_peer.run_graph_observer({"tokenHash": "b" * 64})

        rclpy.init.assert_called_once_with(args=None)
        rclpy.shutdown.assert_called_once_with()

    def test_current_manual_progress_requires_exact_identity_and_emits_once(self) -> None:
        """Verify that current manual progress requires exact identity and emits once."""
        reporter = mock.Mock()
        emitted: set[str] = set()
        with tempfile.TemporaryDirectory() as temp:
            output = pathlib.Path(temp)
            log = output / "unity.log"
            config = {
                "unityLog": str(log),
                "runId": "phase186h-current-0123456789ab",
                "caseId": "manual-jazzy-fastrtps-duplex",
                "manual": True,
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

    def test_automatic_progress_accepts_real_marker_without_manual_identity_fields(self) -> None:
        """Verify that automatic progress accepts real marker without manual identity fields."""
        with tempfile.TemporaryDirectory() as temp:
            log = pathlib.Path(temp) / "unity.log"
            config = {
                "unityLog": str(log),
                "runId": "phase186h-auto-reconnect-012345",
                "caseId": "reconnect-degraded-recovery",
                "manual": False,
                "tokenHash": "a" * 64,
                "head": "b" * 40,
            }
            marker = (
                "PHASE186_ACCEPTANCE_PROGRESS "
                "run=phase186h-auto-reconnect-012345 "
                "case=reconnect-degraded-recovery "
                "ready=true external=false generated=false caseSpecific=false "
                "connected=true publish=Ready subscribe=Ready "
                "received=0 applied=0 diagnostic="
            )
            log.write_text(
                marker.replace("phase186h-auto-reconnect", "phase186h-foreign")
                + "\n"
                + marker
                + "\n",
                encoding="utf-8",
            )

            documents = live._unity_progress_documents(config)
            self.assertEqual(1, len(documents))
            owner = mock.Mock()
            with mock.patch.object(live.time, "monotonic", side_effect=(10.0, 10.0)), \
                    mock.patch.object(live.time, "sleep") as sleep:
                live._wait_unity_progress_after(
                    config,
                    owner,
                    0,
                    {"ready": "true", "connected": "true"},
                )

        owner.poll.assert_not_called()
        sleep.assert_not_called()

    def test_live_startup_interrupt_closes_owner_and_writes_real_cleanup(self) -> None:
        """Verify that live startup interrupt closes owner and writes real cleanup."""
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
        """Verify that runtime prepare interrupt reports cleanup stage before rethrow."""
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
        """Verify that manual wait interrupt removes only owned pointer and gates."""
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
            """Handle prepare for Phase186 acceptance."""
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
                mock.call.transition("4/5", "prepare the Unity scene"),
                mock.call.unity_prepare(
                    "the Phase186 ROS2 Bridge acceptance scene"
                ),
                mock.call.transition(
                    "5/5", "validating completion and cleaning owned resources"
                ),
            ],
            reporter.mock_calls,
        )

    def test_manual_wait_fails_immediately_on_exact_unity_context_failure(self) -> None:
        """Verify that manual wait fails immediately on exact unity context failure."""
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

        with mock.patch.object(live, "prepare_runtime", return_value=runtime), \
                mock.patch.object(live, "OwnedLiveProcesses", return_value=owner), \
                mock.patch.object(live, "_launch_sidecar", return_value=(mock.Mock(), {})), \
                mock.patch.object(live, "_write_manual_pointer", return_value=pointer), \
                mock.patch.object(live, "_mirror_manual_log"), \
                mock.patch.object(live, "_manual_scene_prepare_failure", return_value=None), \
                mock.patch.object(
                    live,
                    "_manual_context_failure",
                    return_value="serialized-run-identity-differs",
                ), \
                mock.patch.object(live, "_remove_manual_pointer"), \
                mock.patch.object(live, "_cleanup_document", return_value=cleanup), \
                self.assertRaises(live.LiveFailure) as failure:
            live.run_live(
                pathlib.Path("D:/repo"),
                config,
                unity_editor=pathlib.Path("Unity.exe"),
                manual_timeout_seconds=30.0,
                reporter=mock.Mock(),
            )

        self.assertEqual("FAIL_UNITY_CONTEXT", failure.exception.code)
        owner.close.assert_called_once_with()

    def test_manual_wait_fails_immediately_when_live_actor_exits(self) -> None:
        """Verify that manual wait fails immediately when live actor exits."""
        with tempfile.TemporaryDirectory() as temp:
            output = pathlib.Path(temp)
            actors = output / "actors"
            actors.mkdir()
            (actors / "ros-peer-failure.json").write_text(
                json.dumps(
                    {
                        "failureCode": "FAIL_PEER",
                        "failureMessage": (
                            "FAIL_PEER: Unity Bridge outbound sample did not reach "
                            "the exact ROS peer"
                        ),
                    }
                ),
                encoding="utf-8",
            )
            config = {
                "outputRoot": str(output),
                "runtimeRowId": "jazzy-fastrtps",
                "bridgeHost": "127.0.0.1",
                "bridgePort": 18767,
                "foxgloveHost": "127.0.0.1",
                "foxglovePort": 18768,
                "externalGate": str(output / "external-gate.json"),
                "exerciseGate": str(output / "exercise-gate.json"),
                "caseId": "manual-jazzy-fastrtps-duplex",
                "manual": True,
                "requiredActors": ["ros-peer"],
                "runId": "phase186h-current-0123456789ab",
                "tokenHash": "a" * 64,
                "head": "b" * 40,
                "unityLog": str(output / "unity.log"),
            }
            runtime = types.SimpleNamespace(
                zenoh_router=None,
                zenoh_endpoint=None,
                row_id="jazzy-fastrtps",
                distro="jazzy",
                rmw="rmw_fastrtps_cpp",
                python_executable=pathlib.Path("python.exe"),
                environment={},
            )
            owner = mock.Mock()
            owner.poll.return_value = 1
            owner.residual_pids.return_value = []
            cleanup = {
                "complete": True,
                "cleanupErrors": [],
                "residualProcesses": [],
                "residualPorts": [],
                "residualOverlays": [],
                "residualTemporaryProjects": [],
            }

            with mock.patch.object(live, "prepare_runtime", return_value=runtime), \
                    mock.patch.object(live, "OwnedLiveProcesses", return_value=owner), \
                    mock.patch.object(live, "_launch_sidecar", return_value=(mock.Mock(), {})), \
                    mock.patch.object(live, "_wait_actor_document", return_value={}), \
                    mock.patch.object(live, "_write_manual_pointer", return_value=output / "pointer.json"), \
                    mock.patch.object(live, "_mirror_manual_log"), \
                    mock.patch.object(live, "_manual_scene_prepare_failure", return_value=None), \
                    mock.patch.object(live, "_manual_context_failure", return_value=None), \
                    mock.patch.object(live, "_manual_scene_preparing_in_log", return_value=False), \
                    mock.patch.object(live, "_manual_scene_ready_in_log", return_value=False), \
                    mock.patch.object(live, "_report_manual_progress"), \
                    mock.patch.object(live, "_remove_manual_pointer"), \
                    mock.patch.object(live, "_cleanup_document", return_value=cleanup), \
                    mock.patch.object(live.time, "monotonic", side_effect=(0.0, 0.0, 31.0)), \
                    mock.patch.object(live.time, "sleep"), \
                    self.assertRaises(live.LiveFailure) as failure:
                live.run_live(
                    pathlib.Path("D:/repo"),
                    config,
                    unity_editor=pathlib.Path("Unity.exe"),
                    manual_timeout_seconds=30.0,
                    reporter=mock.Mock(),
                )

        self.assertEqual("FAIL_PROCESS_EXIT", failure.exception.code)
        self.assertIn("FAIL_PEER", str(failure.exception))
        self.assertIn("Unity Bridge outbound sample", str(failure.exception))
        owner.close.assert_called_once_with()

    def test_manual_completion_waits_for_editor_release_before_pointer_cleanup(self) -> None:
        """Verify that manual completion waits for editor release before pointer cleanup."""
        with tempfile.TemporaryDirectory() as temp:
            output = pathlib.Path(temp)
            config = {
                "outputRoot": str(output),
                "runtimeRowId": "jazzy-fastrtps",
                "bridgeHost": "127.0.0.1",
                "bridgePort": 18767,
                "foxgloveHost": "127.0.0.1",
                "foxglovePort": 18768,
                "externalGate": str(output / "external-gate.json"),
                "exerciseGate": str(output / "exercise-gate.json"),
                "caseId": "manual-jazzy-fastrtps-duplex",
                "manual": True,
                "requiredActors": [],
                "runId": "phase186h-current-0123456789ab",
                "tokenHash": "a" * 64,
                "head": "b" * 40,
                "unityLog": str(output / "unity.log"),
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
            pointer = output / "current-run.json"
            cleanup = {
                "complete": True,
                "cleanupErrors": [],
                "residualProcesses": [],
                "residualPorts": [],
                "residualOverlays": [],
                "residualTemporaryProjects": [],
            }
            events: list[str] = []

            with mock.patch.object(live, "prepare_runtime", return_value=runtime), \
                    mock.patch.object(live, "OwnedLiveProcesses", return_value=owner), \
                    mock.patch.object(live, "_launch_sidecar", return_value=(mock.Mock(), {})), \
                    mock.patch.object(live, "_write_manual_pointer", return_value=pointer), \
                    mock.patch.object(live, "_mirror_manual_log"), \
                    mock.patch.object(live, "_manual_scene_prepare_failure", return_value=None), \
                    mock.patch.object(live, "_manual_context_failure", return_value=None), \
                    mock.patch.object(live, "_manual_scene_preparing_in_log", return_value=False), \
                    mock.patch.object(live, "_manual_scene_ready_in_log", return_value=True), \
                    mock.patch.object(live, "_report_manual_progress"), \
                    mock.patch.object(live, "_manual_marker_in_log", return_value=True), \
                    mock.patch.object(
                        live,
                        "_wait_manual_editor_release",
                        side_effect=lambda *_args, **_kwargs: events.append("editor-release"),
                        create=True,
                    ), \
                    mock.patch.object(
                        live,
                        "_parse_unity_evidence",
                        side_effect=lambda *_args, **_kwargs: events.append("unity-evidence") or {},
                    ), \
                    mock.patch.object(
                        live,
                        "_remove_manual_pointer",
                        side_effect=lambda *_args, **_kwargs: events.append("pointer-cleanup"),
                    ), \
                    mock.patch.object(live, "_cleanup_document", return_value=cleanup), \
                    mock.patch.object(
                        live,
                        "_build_observations",
                        return_value={},
                    ):
                live.run_live(
                    pathlib.Path("D:/repo"),
                    config,
                    unity_editor=pathlib.Path("Unity.exe"),
                    manual_timeout_seconds=30.0,
                    reporter=mock.Mock(),
                )

        self.assertEqual(
            ["editor-release", "unity-evidence", "pointer-cleanup"],
            events,
        )

    def test_observations_require_existing_evidence_files(self) -> None:
        """Do not promote resolved path strings into observed live evidence."""
        with tempfile.TemporaryDirectory() as temp:
            output = pathlib.Path(temp)
            config = {
                "outputRoot": str(output),
                "caseId": "product-inspector",
            }
            (output / "preflight.json").write_text("{}", encoding="utf-8")
            (output / "cleanup.json").write_text("{}", encoding="utf-8")

            with self.assertRaises(live.LiveFailure):
                live._build_observations(config, {})

            (output / "unity-evidence.json").write_text("{}", encoding="utf-8")
            observations = live._build_observations(config, {})
            self.assertTrue(all(item["observed"] for item in observations.values()))

    def test_hostile_peer_data_uses_rejection_evidence_label(self) -> None:
        """Do not label a hostile-frame rejection document as delivered payload."""
        self.assertEqual(
            "live-hostile-frame-rejection",
            live._observation_source("bounds-hostile-peer", "data"),
        )

    def test_manual_editor_release_marker_requires_exact_run_identity(self) -> None:
        """Verify that manual editor release marker requires exact run identity."""
        self.assertTrue(
            hasattr(live, "_manual_editor_released_in_log"),
            "manual completion needs an identity-bound EnteredEditMode marker",
        )
        with tempfile.TemporaryDirectory() as temp:
            log = pathlib.Path(temp) / "unity.log"
            config = {
                "unityLog": str(log),
                "runId": "phase186h-current-0123456789ab",
                "caseId": "manual-jazzy-fastrtps-duplex",
                "tokenHash": "a" * 64,
                "head": "b" * 40,
            }
            exact = (
                "PHASE186_MANUAL_PLAY_EXITED "
                "run=phase186h-current-0123456789ab "
                "case=manual-jazzy-fastrtps-duplex "
                f"tokenHash={'a' * 64} head={'b' * 40}"
            )
            log.write_text(
                exact.replace("phase186h-current", "phase186h-foreign") + "\n",
                encoding="utf-8",
            )
            self.assertFalse(live._manual_editor_released_in_log(config))
            log.write_text(
                exact.replace("phase186h-current", "phase186h-foreign")
                + "\n"
                + exact
                + "\n",
                encoding="utf-8",
            )
            self.assertTrue(live._manual_editor_released_in_log(config))

    def test_manual_unity_evidence_requires_real_publish_and_apply_counts(self) -> None:
        """Manual completion must parse generated runtime counters."""
        with tempfile.TemporaryDirectory() as temp:
            output = pathlib.Path(temp)
            log = output / "unity.log"
            config = {
                "outputRoot": str(output),
                "unityLog": str(log),
                "runId": "phase186h-current-0123456789ab",
                "caseId": "manual-jazzy-fastrtps-duplex",
                "tokenHash": "a" * 64,
            }
            prefix = (
                "PHASE186_ACCEPTANCE_EVIDENCE "
                f"run={config['runId']} "
                f"case={config['caseId']} "
                f"tokenHash={config['tokenHash']} "
            )
            log.write_text(
                prefix
                + "generation=1 received=2 applied=2 replaced=0 "
                + "localMutations=1 accepted=1 sent=1 failed=0 "
                + "connectTransitions=1 disconnectTransitions=0 "
                + "generationChanges=0 dropped=0 providerReplaced=0\n",
                encoding="utf-8",
            )

            evidence = live._parse_unity_evidence(
                config,
                require_pass_marker=False,
            )
            self.assertEqual(1, evidence["fields"]["sent"])
            self.assertEqual(2, evidence["fields"]["applied"])

            log.write_text(
                prefix
                + "generation=1 received=0 applied=0 replaced=0 "
                + "localMutations=0 accepted=0 sent=0 failed=0 "
                + "connectTransitions=1 disconnectTransitions=0 "
                + "generationChanges=0 dropped=0 providerReplaced=0\n",
                encoding="utf-8",
            )
            with self.assertRaises(live.LiveFailure):
                live._parse_unity_evidence(
                    config,
                    require_pass_marker=False,
                )

    def test_manual_wait_accepts_actor_exit_after_all_owned_results_exist(self) -> None:
        """Verify that manual wait accepts actor exit after all owned results exist."""
        config = {
            "outputRoot": "unused",
            "requiredActors": ["graph-observer", "ros-peer"],
        }
        owner = mock.Mock()
        owner.poll.return_value = 0

        with mock.patch.object(live, "_read_actor_document", return_value={}) as read:
            try:
                live._raise_if_worker_process_exited(config, owner)
            except live.LiveFailure as exc:
                self.fail(f"completed actor was misreported as failed: {exc}")

        self.assertEqual(
            [
                mock.call(config, "graph-observer", "result"),
                mock.call(config, "ros-peer", "result"),
            ],
            read.call_args_list,
        )

    def test_manual_scene_ready_requires_exact_identity_and_stable_schema(self) -> None:
        """Verify that manual scene ready requires exact identity and stable schema."""
        config = {
            "unityLog": "unity.log",
            "runId": "phase186h-current-0123456789ab",
            "caseId": "manual-jazzy-fastrtps-duplex",
            "tokenHash": "a" * 64,
            "head": "b" * 40,
        }
        prefix = "PHASE186_MANUAL_SCENE_READY "
        stale = (
            prefix
            + "run=phase186h-stale-0123456789ab "
            + f"case={config['caseId']} tokenHash={config['tokenHash']} "
            + f"head={config['head']} schemaInfoChanged=false"
        )
        changing = (
            prefix
            + f"run={config['runId']} case={config['caseId']} "
            + f"tokenHash={config['tokenHash']} head={config['head']} "
            + "schemaInfoChanged=true"
        )
        exact = (
            prefix
            + f"run={config['runId']} case={config['caseId']} "
            + f"tokenHash={config['tokenHash']} head={config['head']} "
            + "manifest=abc schemaInfoChanged=false"
        )
        preparing = (
            "PHASE186_MANUAL_SCENE_PREPARING "
            + f"run={config['runId']} case={config['caseId']} "
            + f"tokenHash={config['tokenHash']} head={config['head']} "
            + "schemaRefresh=1"
        )

        with mock.patch.object(live_peer, "_read_log", return_value=stale + "\n" + changing):
            self.assertFalse(live._manual_scene_ready_in_log(config))
        with mock.patch.object(live_peer, "_read_log", return_value=stale + "\n" + exact):
            self.assertTrue(live._manual_scene_ready_in_log(config))
        with mock.patch.object(live_peer, "_read_log", return_value=stale):
            self.assertFalse(live._manual_scene_preparing_in_log(config))
        with mock.patch.object(
            live_peer,
            "_read_log",
            return_value=stale + "\n" + preparing,
        ):
            self.assertTrue(live._manual_scene_preparing_in_log(config))

        failed = (
            "PHASE186_MANUAL_SCENE_PREPARE_FAIL "
            + f"run={config['runId']} case={config['caseId']} "
            + f"tokenHash={config['tokenHash']} head={config['head']} "
            + "reason=InvalidOperationException"
        )
        with mock.patch.object(live_peer, "_read_log", return_value=stale + "\n" + failed):
            self.assertEqual(
                "InvalidOperationException",
                live._manual_scene_prepare_failure(config),
            )

    def test_manual_context_failure_requires_exact_generated_identity(self) -> None:
        """Verify that manual context failure requires exact generated identity."""
        config = {
            "unityLog": "unity.log",
            "runId": "phase186h-current-0123456789ab",
            "caseId": "manual-jazzy-fastrtps-duplex",
            "tokenHash": "a" * 64,
            "head": "b" * 40,
        }
        prefix = "PHASE186_ACCEPTANCE_CONTEXT_FAIL "
        exact = (
            prefix
            + f"run={config['runId']} case={config['caseId']} "
            + f"tokenHash={config['tokenHash']} head={config['head']} "
            + "reason=serialized-run-identity-differs"
        )
        stale = exact.replace(
            str(config["runId"]),
            "phase186h-stale-0123456789ab",
        )

        with mock.patch.object(live_peer, "_read_log", return_value=stale):
            self.assertIsNone(live._manual_context_failure(config))
        with mock.patch.object(
            live_peer,
            "_read_log",
            return_value=stale + "\n" + exact,
        ):
            self.assertEqual(
                "serialized-run-identity-differs",
                live._manual_context_failure(config),
            )

    def test_ros_graph_gate_requires_exact_bridge_publisher(self) -> None:
        """Verify that ros graph gate requires exact bridge publisher."""
        config = {
            "caseId": "slow-main-thread-640hz",
            "topics": ("/phase186/slow_ingress", "/phase186/slow_control"),
        }

        def endpoint(node_name: str, topic_type: str) -> object:
            """Handle endpoint for Phase186 acceptance."""
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
        """Verify that custom peer sets nested string presence."""
        class Envelope:
            """Represent envelope."""
            pass

        class Payload:
            """Represent payload."""
            pass

        class Nested:
            """Represent nested."""
            def __init__(self) -> None:
                """Initialize the helper state."""
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
        """Verify that source publish keeps ros peer alive for bounded delivery."""
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

    def test_spin_timeout_builds_failure_detail_from_final_observations(self) -> None:
        """Verify that spin timeout builds failure detail from final observations."""
        observed: list[str] = []

        def predicate() -> bool:
            """Handle predicate for Phase186 acceptance."""
            observed.append("duplex:none")
            return False

        with mock.patch.object(
            live_peer.time,
            "monotonic",
            side_effect=(10.0, 10.0, 11.0),
        ), self.assertRaises(live_peer.LiveActorFailure) as failure:
            live_peer._spin_until(
                mock.Mock(),
                object(),
                predicate,
                0.5,
                lambda: "missing outbound; observed=" + ",".join(observed),
            )

        self.assertIn("missing outbound; observed=duplex:none", str(failure.exception))

    def test_duplex_origin_check_consumes_one_direct_sample_per_sequence(self) -> None:
        """Verify that duplex origin check consumes one direct sample per sequence."""
        token_hash = "a" * 64

        def sample(sequence: int, label: str = "external-a") -> object:
            """Handle sample for Phase186 acceptance."""
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
        """Verify that direct sample filter keeps malformed or out of run samples."""
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
        """Verify that direct sample filter uses standard message sequence."""
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

    def test_outbound_timeout_detail_names_missing_topic_and_observed_samples(self) -> None:
        """Verify that outbound timeout detail names missing topic and observed samples."""
        token_hash = "e" * 64
        custom_topic = "/phase186/custom"
        standard_topic = "/phase186/standard"
        direct_custom = types.SimpleNamespace(
            foxrun_sequence=1,
            payload=types.SimpleNamespace(
                message=f"phase186:{token_hash[:12]}:1:external-a"
            ),
        )
        direct_standard = types.SimpleNamespace(
            message=f"phase186:{token_hash[:12]}:1:external-a"
        )
        standard_local = types.SimpleNamespace(
            message=f"phase186:{token_hash[:12]}:9:unity-local-b-1"
        )
        describe = getattr(live_peer, "_outbound_wait_detail", None)

        self.assertIsNotNone(describe)
        detail = describe(
            "manual-jazzy-fastrtps-duplex",
            {custom_topic, standard_topic},
            {
                custom_topic: [direct_custom],
                standard_topic: [direct_standard, standard_local],
            },
            {
                custom_topic: "custom_duplex",
                standard_topic: "standard_duplex",
            },
            token_hash,
            8,
            {custom_topic: object(), standard_topic: object()},
        )

        self.assertIn(f'missing=["{custom_topic}"]', detail)
        self.assertIn(f'"{custom_topic}":[]', detail)
        self.assertIn("unity-local-b-1", detail)

    def test_actor_readiness_budget_outlives_coordinator_budget(self) -> None:
        """Verify that actor readiness budget outlives coordinator budget."""
        self.assertEqual(
            900.0,
            live_protocol.COORDINATOR_UNITY_READY_TIMEOUT_SECONDS,
        )
        self.assertGreater(
            live_protocol.ACTOR_UNITY_READY_TIMEOUT_SECONDS,
            live_protocol.COORDINATOR_UNITY_READY_TIMEOUT_SECONDS,
        )

    def test_live_actor_operation_budget_allows_slow_unity_and_ros_startup(self) -> None:
        """Verify that live actor operation budget allows slow unity and ros startup."""
        self.assertEqual(300.0, live_peer.LIVE_ACTOR_OPERATION_TIMEOUT_SECONDS)
        self.assertGreater(
            live.LIVE_ACTOR_RESULT_TIMEOUT_SECONDS,
            live_peer.LIVE_ACTOR_OPERATION_TIMEOUT_SECONDS,
        )

    def test_slow_sequence_flood_waits_for_current_unity_baseline(self) -> None:
        """Verify that slow sequence flood waits for current unity baseline."""
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
        """Verify that reconnect peer waits for identity bound exercise gate."""
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
        """Verify that reconnect restart waits for observed disconnect and recovery."""
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
        """Verify that hostile frames encode the exact declared lengths."""
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
        """Verify that busy response uses the stable protocol error code field."""
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
        """Verify that optional rejection response reassembles fragmented error frame."""
        header = json.dumps(
            {"status": "error", "code": "invalid_frame"},
            separators=(",", ":"),
        ).encode("ascii")
        frame = b"U2R2" + struct.pack("<HHII", 1, 0, len(header), 0) + header
        connection = _ChunkSocket([frame[:3], frame[3:9], frame[9:17], frame[17:]])
        self.assertEqual(frame, live_peer._read_optional_frame(connection))
        self.assertIsNone(live_peer._read_optional_frame(_ChunkSocket([b""])))

    def test_cleanup_detects_owned_gate_pointer_and_extra_listener(self) -> None:
        """Verify that cleanup detects owned gate pointer and extra listener."""
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
        """Verify that coordinator reloads actual cleanup after live failure."""
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
        """Verify that process registry has public presence query."""
        owner = object.__new__(live.OwnedLiveProcesses)
        owner._records = {"sidecar-2": object()}
        self.assertTrue(owner.has_record("sidecar-2"))
        self.assertFalse(owner.has_record("sidecar-1"))

    def test_ros_peer_cohosts_graph_observer_to_bound_fastdds_participants(self) -> None:
        """Verify that ros peer cohosts graph observer to bound fastdds participants."""
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
        """Verify that cohosted graph process evidence names the ros peer owner."""
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
        """Verify that cohosted graph documents are identity bound and explicit."""
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
        """Verify that graph observation requires the exact bridge node."""
        expected_type = live_protocol.INTERFACE_TYPE
        config = {"caseId": "full-duplex", "topics": ["/phase186/duplex"]}

        def endpoint(node_name: str) -> types.SimpleNamespace:
            """Handle endpoint for Phase186 acceptance."""
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
            """Handle observe for Phase186 acceptance."""
            info = endpoint(node_name)
            node = mock.Mock()
            node.get_publishers_info_by_topic.return_value = [info]
            node.get_subscriptions_info_by_topic.return_value = [info]
            ready: list[bool] = []

            def spin_once(_rclpy, _node, predicate, _timeout, _message) -> None:
                """Handle spin once for Phase186 acceptance."""
                ready.append(bool(predicate()))

            with mock.patch.object(live_peer, "_spin_until", side_effect=spin_once):
                live_peer._observe_graph(mock.Mock(), node, config)
            self.assertEqual(1, len(ready))
            return ready[0]

        self.assertFalse(observe("phase186_peer_deadbeef"))
        self.assertTrue(observe("unity2foxglove_ros2_bridge"))

    def test_runtime_environment_includes_ros_pixi_dll_directory(self) -> None:
        """Verify that runtime environment includes ros pixi dll directory."""
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
            paths = environment["PATH"].split(os.pathsep)
            self.assertIn(str(pixi_bin), paths)
            self.assertIn("ros-base", paths)
            self.assertNotIn("ambient", paths)

    def test_unity_environment_binds_only_all_provider_fanout_to_run_domain(
        self,
    ) -> None:
        """Verify that unity environment binds only all provider fanout to run domain."""
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
