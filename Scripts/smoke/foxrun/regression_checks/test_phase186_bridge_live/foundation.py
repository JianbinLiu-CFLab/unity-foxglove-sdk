#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Regression tests for Phase186-H owned live orchestration helpers."""
from __future__ import annotations
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "test_phase186_bridge_live.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
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
class _Phase186BridgeLiveTests_support:
    """Decomposed Phase192 implementation component."""
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


__all__ = [name for name in globals() if not name.startswith("__")]
