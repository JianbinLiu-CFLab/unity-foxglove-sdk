#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Regression tests for the Phase186-H Bridge acceptance coordinator."""

from __future__ import annotations

import json
import pathlib
import socket
import tempfile
import unittest
from unittest import mock

from Scripts.smoke.foxrun import phase186_bridge_acceptance as acceptance
from Scripts.smoke.foxrun import phase186_bridge_acceptance_protocol as protocol


HEAD = "a" * 40


class Phase186BridgeAcceptanceTests(unittest.TestCase):
    """Prove orchestration boundaries without launching live prerequisites."""

    def test_manual_flag_is_limited_to_the_two_manual_cases(self) -> None:
        args = acceptance.parse_args(
            [
                "--case",
                "manual-jazzy-fastrtps-duplex",
                "--manual",
                "--expected-head",
                HEAD,
                "--output-root",
                r"D:\evidence",
            ]
        )
        acceptance.validate_arguments(args)
        args = acceptance.parse_args(
            [
                "--case",
                "full-duplex",
                "--manual",
                "--expected-head",
                HEAD,
                "--output-root",
                r"D:\evidence",
            ]
        )
        with self.assertRaises(protocol.ProtocolFailure):
            acceptance.validate_arguments(args)

    def test_automatic_cases_reject_manual_case_without_manual_flag(self) -> None:
        args = acceptance.parse_args(
            [
                "--case",
                "manual-lyrical-zenoh-duplex",
                "--expected-head",
                HEAD,
                "--output-root",
                r"D:\evidence",
            ]
        )
        with self.assertRaises(protocol.ProtocolFailure):
            acceptance.validate_arguments(args)

    def test_expected_head_must_be_full_lowercase_sha(self) -> None:
        args = acceptance.parse_args(
            [
                "--case",
                "full-duplex",
                "--expected-head",
                "abc",
                "--output-root",
                r"D:\evidence",
            ]
        )
        with self.assertRaises(protocol.ProtocolFailure):
            acceptance.validate_arguments(args)

    def test_resolve_unity_editor_locks_project_version(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = pathlib.Path(temp)
            project = root / "Unity2Foxglove"
            settings = project / "ProjectSettings"
            settings.mkdir(parents=True)
            (settings / "ProjectVersion.txt").write_text(
                "m_EditorVersion: 6000.3.14f1\n",
                encoding="utf-8",
            )
            editor = root / "Unity.exe"
            editor.write_bytes(b"unity")
            resolved = acceptance.resolve_unity_editor(project, editor)
            self.assertEqual(editor.resolve(), resolved.path)
            self.assertEqual("6000.3.14f1", resolved.version)

    def test_resolve_unity_editor_rejects_missing_or_malformed_version(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            project = pathlib.Path(temp) / "Unity2Foxglove"
            (project / "ProjectSettings").mkdir(parents=True)
            editor = pathlib.Path(temp) / "Unity.exe"
            editor.write_bytes(b"unity")
            with self.assertRaises(protocol.ProtocolFailure):
                acceptance.resolve_unity_editor(project, editor)
            (project / "ProjectSettings" / "ProjectVersion.txt").write_text(
                "m_EditorVersion: unsafe version\n",
                encoding="utf-8",
            )
            with self.assertRaises(protocol.ProtocolFailure):
                acceptance.resolve_unity_editor(project, editor)

    def test_reserve_loopback_port_returns_owned_ipv4_socket(self) -> None:
        reservation = acceptance.reserve_loopback_port()
        try:
            self.assertEqual("127.0.0.1", reservation.host)
            self.assertGreater(reservation.port, 0)
            with self.assertRaises(OSError):
                contender = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
                try:
                    contender.bind((reservation.host, reservation.port))
                finally:
                    contender.close()
        finally:
            reservation.close()

    def test_package_preflight_rejects_wrong_ids_or_r2fu_dependency(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            repo = pathlib.Path(temp)
            sdk = repo / "Packages" / "dev.unity2foxglove.sdk"
            bridge = repo / "Packages" / "dev.unity2foxglove.ros2bridge"
            sdk.mkdir(parents=True)
            bridge.mkdir(parents=True)
            (sdk / "package.json").write_text(
                json.dumps({"name": "dev.unity2foxglove.sdk"}), encoding="utf-8"
            )
            (bridge / "package.json").write_text(
                json.dumps(
                    {
                        "name": "dev.unity2foxglove.ros2bridge",
                        "dependencies": {"dev.unity2foxglove.sdk": "1.9.6"},
                    }
                ),
                encoding="utf-8",
            )
            self.assertEqual(
                "dev.unity2foxglove.ros2bridge",
                acceptance.validate_package_manifests(repo)["bridgePackage"],
            )
            (bridge / "package.json").write_text(
                json.dumps(
                    {
                        "name": "dev.unity2foxglove.ros2bridge",
                        "dependencies": {
                            "dev.unity2foxglove.sdk": "1.9.6",
                            "dev.unity2foxglove.ros2forunity": "0.9.0",
                        },
                    }
                ),
                encoding="utf-8",
            )
            with self.assertRaises(protocol.ProtocolFailure):
                acceptance.validate_package_manifests(repo)

    def test_find_current_run_marker_rejects_stale_and_accepts_exact(self) -> None:
        token = "p186h_0123456789abcdef01234567"
        run_id = "phase186h-run-0123456789ab"
        exact = protocol.format_manual_completion_marker(
            case_id="manual-jazzy-fastrtps-duplex",
            run_id=run_id,
            token=token,
            head=HEAD,
            verdict="PASS",
        )
        lines = [
            exact.replace(run_id, "phase186h-old-0123456789ab"),
            exact,
        ]
        self.assertEqual(
            exact,
            acceptance.find_current_manual_marker(
                lines,
                case_id="manual-jazzy-fastrtps-duplex",
                run_id=run_id,
                token=token,
                head=HEAD,
            ),
        )
        with self.assertRaises(protocol.ProtocolFailure):
            acceptance.find_current_manual_marker(
                lines[:1],
                case_id="manual-jazzy-fastrtps-duplex",
                run_id=run_id,
                token=token,
                head=HEAD,
            )

    def test_owned_cleanup_requires_no_process_port_or_temp_residue(self) -> None:
        clean = {
            "complete": True,
            "residualProcesses": [],
            "residualPorts": [],
            "residualOverlays": [],
            "residualTemporaryProjects": [],
        }
        acceptance.validate_cleanup_evidence(clean)
        for key in (
            "residualProcesses",
            "residualPorts",
            "residualOverlays",
            "residualTemporaryProjects",
        ):
            dirty = dict(clean)
            dirty[key] = ["leftover"]
            with self.subTest(key=key):
                with self.assertRaises(protocol.ProtocolFailure):
                    acceptance.validate_cleanup_evidence(dirty)

    def test_live_pass_cannot_be_derived_from_build_summary(self) -> None:
        build = {"verdict": "PASS", "rowId": "jazzy-fastrtps"}
        with self.assertRaises(protocol.ProtocolFailure):
            acceptance.promote_build_to_live_summary(build)

    def test_missing_prerequisite_persists_not_run_and_returns_blocking_code(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            output = pathlib.Path(temp)
            result = acceptance.persist_not_run(
                output,
                run_id="phase186h-run-0123456789ab",
                token="p186h_0123456789abcdef01234567",
                case_id="manual-jazzy-fastrtps-duplex",
                head=HEAD,
                prerequisite="Unity 6000.3.14f1 license",
            )
            self.assertEqual("NOT RUN", result["verdict"])
            self.assertEqual(acceptance.EXIT_NOT_RUN, protocol.verdict_exit_code(result))
            persisted = json.loads((output / "terminal-summary.json").read_text(encoding="utf-8"))
            self.assertEqual(result, persisted)

    def test_preflight_checks_current_git_head_not_only_requested_text(self) -> None:
        with mock.patch.object(acceptance, "git_head", return_value="b" * 40):
            with self.assertRaises(protocol.ProtocolFailure):
                acceptance.require_exact_head(pathlib.Path("D:/repo"), HEAD)


if __name__ == "__main__":
    unittest.main()
