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

    def test_generated_unity_binding_is_token_scoped_and_uses_real_cdr_shapes(self) -> None:
        token = "p186h_0123456789abcdef01234567"
        run_id = "phase186h-manual-0123456789ab"
        with tempfile.TemporaryDirectory() as temp:
            repository = pathlib.Path(temp).resolve()
            project = repository / "Unity2Foxglove"
            project.mkdir()
            output = repository / "build" / "phase186" / "acceptance" / run_id
            output.mkdir(parents=True)
            config = protocol.make_run_config(
                repository=repository,
                project=project,
                output_root=output,
                run_id=run_id,
                token=token,
                case_id="manual-jazzy-fastrtps-duplex",
                head=HEAD,
                bridge_port=18767,
                domain_id=187,
            )

            source = acceptance.render_unity_run_binding(config)
            for topic in config["topics"]:
                self.assertIn(topic, source)
            self.assertIn("Foxglove.Log", source)
            self.assertIn("Phase181State", source)
            self.assertIn(protocol.INTERFACE_DIGEST, source)
            self.assertIn("Mode = FoxRunFlow.PublishAndSubscribe", source)
            self.assertIn(
                "SubscribeTransportId = Ros2BridgeTransportProvider.ProviderId",
                source,
            )
            self.assertNotIn("/foxrun/phase181/", source)
            self.assertNotIn("/foxrun/phase184/", source)

    def test_generated_unity_binding_install_and_cleanup_are_content_owned(self) -> None:
        token = "p186h_0123456789abcdef01234567"
        run_id = "phase186h-source-0123456789ab"
        with tempfile.TemporaryDirectory() as temp:
            repository = pathlib.Path(temp).resolve()
            project = repository / "Unity2Foxglove"
            project.mkdir()
            output = repository / "build" / "phase186" / "acceptance" / run_id
            output.mkdir(parents=True)
            config = protocol.make_run_config(
                repository=repository,
                project=project,
                output_root=output,
                run_id=run_id,
                token=token,
                case_id="manual-jazzy-fastrtps-duplex",
                head=HEAD,
                bridge_port=18767,
                domain_id=187,
            )

            installed = acceptance.install_unity_run_binding(project, config)
            self.assertEqual(
                project
                / "Assets"
                / "Scripts"
                / "Generated"
                / "Phase186AcceptanceRun.cs",
                installed.path,
            )
            self.assertEqual(installed.sha256, acceptance.sha256_file(installed.path))
            installed.path.write_text("foreign", encoding="utf-8")
            with self.assertRaises(protocol.ProtocolFailure):
                acceptance.cleanup_unity_run_binding(installed)

    def test_every_case_has_an_exact_generated_unity_contract_layout(self) -> None:
        token = "p186h_0123456789abcdef01234567"
        with tempfile.TemporaryDirectory() as temp:
            repository = pathlib.Path(temp).resolve()
            project = repository / "Unity2Foxglove"
            project.mkdir()
            for case_id in protocol.CASES:
                run_id = "phase186h-" + case_id + "-012345"
                output = repository / "build" / "phase186" / run_id
                output.mkdir(parents=True)
                config = protocol.make_run_config(
                    repository=repository,
                    project=project,
                    output_root=output,
                    run_id=run_id,
                    token=token,
                    case_id=case_id,
                    head=HEAD,
                    bridge_port=18767,
                    domain_id=187,
                )
                source = acceptance.render_unity_run_binding(config)
                with self.subTest(case_id=case_id):
                    self.assertEqual(
                        len(config["topics"]),
                        source.count("public const string Phase186GeneratedTopic"),
                    )
                    for topic in config["topics"]:
                        self.assertEqual(1, source.count(f'= "{topic}";'))
                    self.assertIn("partial void Phase186Generated_Tick", source)
                    self.assertIn("Phase186GeneratedInterfaceDigest", source)

            bridge_output = repository / "build" / "phase186" / "phase186h-bridge-source-check"
            bridge_output.mkdir(parents=True)
            bridge_config = protocol.make_run_config(
                repository=repository,
                project=project,
                output_root=bridge_output,
                run_id="phase186h-bridge-source-check",
                token=token,
                case_id="bridge-source",
                head=HEAD,
                bridge_port=18767,
                domain_id=187,
            )
            bridge_source = acceptance.render_unity_run_binding(bridge_config)
            mutation_method = bridge_source.split(
                "partial void Phase186Generated_PublishLocalMutation", 1
            )[1].split("private static Foxglove.Log", 1)[0]
            self.assertIn("published = false;", mutation_method)
            self.assertNotIn("evidence.LocalMutations++", mutation_method)


if __name__ == "__main__":
    unittest.main()
