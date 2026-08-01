#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Regression tests for Phase186-H owned live orchestration helpers."""

from __future__ import annotations

import json
import pathlib
import struct
import tempfile
import unittest
from unittest import mock

from Scripts.smoke.foxrun import phase186_bridge_acceptance as acceptance
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
            ):
                environment = live._build_runtime_environment(
                    {"PATH": "ambient"},
                    root,
                    overlay,
                    distro="jazzy",
                    rmw="rmw_fastrtps_cpp",
                    domain_id=187,
                    topology_id="phase186h-test",
                    zenoh_session_config=None,
                )
            paths = environment["PATH"].split(";")
            self.assertIn(str(pixi_bin), paths)
            self.assertIn("ros-base", paths)
            self.assertNotIn("ambient", paths)


if __name__ == "__main__":
    unittest.main()
