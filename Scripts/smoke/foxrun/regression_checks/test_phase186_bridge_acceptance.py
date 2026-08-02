#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Regression tests for the Phase186-H Bridge acceptance coordinator."""

from __future__ import annotations

import json
import contextlib
import inspect
import io
import pathlib
import socket
import subprocess
import sys
import tempfile
import types
import unittest
from unittest import mock

from Scripts.smoke.foxrun import phase186_bridge_acceptance as acceptance
from Scripts.smoke.foxrun import phase186_bridge_acceptance_protocol as protocol
from Scripts.smoke.foxrun import phase186_bridge_project as bridge_project


HEAD = "a" * 40


class Phase186BridgeAcceptanceTests(unittest.TestCase):
    """Prove orchestration boundaries without launching live prerequisites."""

    def test_direct_script_bootstrap_can_import_deferred_live_runner(self) -> None:
        repository = pathlib.Path(__file__).resolve().parents[4]
        script_directory = repository / "Scripts" / "smoke" / "foxrun"
        probe = """
import importlib
import pathlib
import sys

script_directory = pathlib.Path(sys.argv[1]).resolve()
sys.path.insert(0, str(script_directory))
importlib.import_module("phase186_bridge_acceptance")
importlib.import_module("Scripts.smoke.foxrun.phase186_bridge_live")
"""
        with tempfile.TemporaryDirectory() as temp:
            completed = subprocess.run(
                [
                    sys.executable,
                    "-I",
                    "-c",
                    probe,
                    str(script_directory),
                ],
                cwd=temp,
                capture_output=True,
                text=True,
                check=False,
            )

        self.assertEqual(0, completed.returncode, completed.stderr)

    def test_reporter_handoff_precedes_pass_fail_not_run_machine_markers(self) -> None:
        for verdict, reason in (
            ("PASS", "cleanup complete"),
            ("FAIL", "FAIL_RUNTIME: peer stopped"),
            ("NOT RUN", "Unity license unavailable"),
        ):
            with self.subTest(verdict=verdict):
                ordered: list[str] = []
                reporter = mock.Mock()
                reporter.terminal.side_effect = (
                    lambda value, *_args: ordered.append("human:" + value)
                )
                with mock.patch.object(
                    acceptance,
                    "print",
                    side_effect=lambda value, **_kwargs: ordered.append(
                        "machine:" + value
                    ),
                    create=True,
                ):
                    acceptance._emit_terminal_handoff(
                        reporter,
                        verdict=verdict,
                        reason=reason,
                        evidence_root=pathlib.Path(r"D:\evidence\run"),
                        machine_line="PHASE186_TERMINAL " + verdict,
                    )

                self.assertEqual(
                    ["human:" + verdict, "machine:PHASE186_TERMINAL " + verdict],
                    ordered,
                )
                reporter.terminal.assert_called_once_with(
                    verdict,
                    reason,
                    str(pathlib.Path(r"D:\evidence\run").resolve()),
                )

    def test_direct_cli_still_requires_expected_head(self) -> None:
        with self.assertRaises(SystemExit):
            acceptance.parse_args(
                [
                    "--case",
                    "manual-jazzy-fastrtps-duplex",
                    "--manual",
                    "--output-root",
                    r"D:\evidence",
                ]
            )

    def test_automatic_preflight_stdout_and_stderr_remain_exact_without_status(self) -> None:
        args = types.SimpleNamespace(
            case="full-duplex",
            manual=False,
            expected_head=HEAD,
            output_root=pathlib.Path("build/phase186/test-automatic"),
            unity_editor=None,
            run_id=None,
            bridge_port=None,
            foxglove_port=None,
            domain_id=None,
            runtime_row=None,
            unity_composition="bridge-only",
            preflight_only=True,
            manual_timeout_seconds=1800.0,
        )

        class Reservation:
            def __init__(self, port: int) -> None:
                self.port = port

            def __enter__(self):
                return self

            def __exit__(self, *_unused) -> None:
                return None

        stdout = io.StringIO()
        stderr = io.StringIO()
        with tempfile.TemporaryDirectory() as temp:
            repository = pathlib.Path(temp).resolve()
            (repository / "build" / "phase186").mkdir(parents=True)
            reservations = iter((Reservation(18767), Reservation(18768)))
            with mock.patch.object(acceptance, "parse_args", return_value=args), \
                    mock.patch.object(acceptance, "validate_arguments", return_value=args), \
                    mock.patch.object(acceptance, "repository_root", return_value=repository), \
                    mock.patch.object(
                        acceptance,
                        "_new_run_identity",
                        return_value=(
                            "phase186h-full-duplex-0123456789ab",
                            "p186h_0123456789abcdef01234567",
                        ),
                    ), \
                    mock.patch.object(acceptance, "_create_owned_unity_project", return_value=None), \
                    mock.patch.object(acceptance, "reserve_loopback_port", side_effect=lambda *_: next(reservations)), \
                    mock.patch.object(
                        acceptance,
                        "_preflight",
                        return_value={"unity": {"path": "Unity.exe"}},
                    ), \
                    mock.patch.object(acceptance, "_remove_owned_unity_project"), \
                    contextlib.redirect_stdout(stdout), contextlib.redirect_stderr(stderr):
                result = acceptance.main([])

        self.assertEqual(acceptance.EXIT_PASS, result)
        self.assertEqual(
            "PHASE186_PREFLIGHT_PASS run=phase186h-full-duplex-0123456789ab "
            "case=full-duplex tokenHash="
            + protocol.token_sha256("p186h_0123456789abcdef01234567")
            + f" head={HEAD}\n",
            stdout.getvalue(),
        )
        self.assertEqual("", stderr.getvalue())

    def test_interrupted_coordinator_head_resolution_persists_incomplete_fail(self) -> None:
        args = types.SimpleNamespace(
            case="manual-jazzy-fastrtps-duplex",
            manual=True,
            expected_head=None,
            output_root=pathlib.Path("build/phase186/test-manual"),
            unity_editor=None,
            run_id=None,
            bridge_port=None,
            foxglove_port=None,
            domain_id=None,
            runtime_row=None,
            unity_composition="repository-all-providers",
            preflight_only=False,
            manual_timeout_seconds=1800.0,
        )
        reporter = mock.Mock()
        reporter.terminal.side_effect = lambda *_args: print(
            "PHASE186 TEST HUMAN HANDOFF"
        )
        stdout = io.StringIO()
        with tempfile.TemporaryDirectory() as temp:
            repository = pathlib.Path(temp).resolve()
            (repository / "build" / "phase186").mkdir(parents=True)
            with mock.patch.object(acceptance, "parse_args", return_value=args), \
                    mock.patch.object(acceptance, "validate_arguments", return_value=args), \
                    mock.patch.object(acceptance, "repository_root", return_value=repository), \
                    mock.patch.object(
                        acceptance,
                        "_new_run_identity",
                        return_value=(
                            "phase186h-jazzy-fastrtps-012345",
                            "p186h_0123456789abcdef01234567",
                        ),
                    ), \
                    mock.patch.object(acceptance, "git_head", side_effect=KeyboardInterrupt), \
                    mock.patch.object(
                        protocol,
                        "make_failure_summary",
                        side_effect=AssertionError("unknown HEAD entered terminal protocol"),
                    ), \
                    contextlib.redirect_stdout(stdout):
                result = acceptance.main(
                    [],
                    status=reporter,
                    resolve_current_head=True,
                )

            run_root = next((repository / "build" / "phase186" / "test-manual").iterdir())
            interrupted_path = run_root / "terminal-interrupted.json"
            terminal = json.loads(
                interrupted_path.read_text(encoding="utf-8")
            )
            terminal_summary_exists = (run_root / "terminal-summary.json").exists()
            terminal_marker_exists = (run_root / "terminal-marker.txt").exists()

            observed_root = repository / "build" / "phase186" / "observed-head"
            observed_root.mkdir()
            with contextlib.redirect_stdout(io.StringIO()):
                observed = acceptance._persist_interrupted(
                    observed_root,
                    run_id="phase186h-observed-head-012345",
                    token="p186h_0123456789abcdef01234567",
                    case_id="manual-jazzy-fastrtps-duplex",
                    head=HEAD,
                    stage="manual wait",
                )
            observed_persisted = json.loads(
                (observed_root / "terminal-summary.json").read_text(encoding="utf-8")
            )
            observed_interrupted_exists = (
                observed_root / "terminal-interrupted.json"
            ).exists()

        self.assertEqual(acceptance.EXIT_FAIL, result)
        self.assertEqual(
            {
                "schemaVersion",
                "runId",
                "tokenHash",
                "caseId",
                "head",
                "headObserved",
                "verdict",
                "failureCode",
                "stage",
                "failureMessage",
                "cleanup",
            },
            set(terminal),
        )
        self.assertEqual("FAIL_INTERRUPTED", terminal["failureCode"])
        self.assertIsNone(terminal["head"])
        self.assertFalse(terminal["headObserved"])
        self.assertIn("repository/HEAD/Unity/ports", terminal["failureMessage"])
        self.assertFalse(terminal["cleanup"]["complete"])
        self.assertTrue(
            any("not observed" in value for value in terminal["cleanup"]["cleanupErrors"])
        )
        self.assertNotIn(
            "or protocol.clean_cleanup_evidence()",
            inspect.getsource(acceptance.main),
        )
        reporter.transition.assert_any_call(
            "1/5", "checking repository, HEAD, Unity, and ports"
        )
        reporter.close.assert_not_called()
        self.assertIn("headObserved=false", stdout.getvalue())
        self.assertIn(str(interrupted_path), stdout.getvalue())
        self.assertLess(
            stdout.getvalue().index("PHASE186 TEST HUMAN HANDOFF"),
            stdout.getvalue().index("PHASE186_INTERRUPTED_FAIL"),
        )
        self.assertFalse(terminal_summary_exists)
        self.assertFalse(terminal_marker_exists)
        self.assertEqual(HEAD, observed["head"])
        self.assertEqual(observed, observed_persisted)
        self.assertEqual(observed, protocol.validate_terminal_summary(observed))
        self.assertFalse(observed_interrupted_exists)

    def test_reporter_preflight_failure_with_head_uses_validated_terminal(self) -> None:
        args = types.SimpleNamespace(
            case="manual-jazzy-fastrtps-duplex",
            manual=True,
            expected_head=None,
            output_root=pathlib.Path("build/phase186/test-preflight-fail"),
            unity_editor=None,
            run_id=None,
            bridge_port=None,
            foxglove_port=None,
            domain_id=None,
            runtime_row=None,
            unity_composition="repository-all-providers",
            preflight_only=False,
            manual_timeout_seconds=1800.0,
        )

        class Reservation:
            def __init__(self, port: int) -> None:
                self.port = port

            def __enter__(self):
                return self

            def __exit__(self, *_unused) -> None:
                return None

        reporter = mock.Mock()
        reporter.terminal.side_effect = lambda *_args: print("HUMAN PREFLIGHT FAIL")
        stdout = io.StringIO()
        stderr = io.StringIO()
        with tempfile.TemporaryDirectory() as temp:
            repository = pathlib.Path(temp).resolve()
            (repository / "build" / "phase186").mkdir(parents=True)
            owned = types.SimpleNamespace(path=repository / "owned-project")
            reservations = iter((Reservation(18767), Reservation(18768)))
            with mock.patch.object(acceptance, "parse_args", return_value=args), \
                    mock.patch.object(acceptance, "validate_arguments", return_value=args), \
                    mock.patch.object(acceptance, "repository_root", return_value=repository), \
                    mock.patch.object(
                        acceptance,
                        "_new_run_identity",
                        return_value=(
                            "phase186h-preflight-fail-012345",
                            "p186h_0123456789abcdef01234567",
                        ),
                    ), \
                    mock.patch.object(acceptance, "git_head", return_value=HEAD), \
                    mock.patch.object(acceptance, "_create_owned_unity_project", return_value=owned), \
                    mock.patch.object(
                        acceptance,
                        "reserve_loopback_port",
                        side_effect=lambda *_: next(reservations),
                    ), \
                    mock.patch.object(
                        acceptance,
                        "_preflight",
                        side_effect=acceptance.AcceptanceFailure(
                            "FAIL_PACKAGE_COMPOSITION", "authority drift"
                        ),
                    ), \
                    mock.patch.object(acceptance, "_remove_owned_unity_project") as remove, \
                    contextlib.redirect_stdout(stdout), contextlib.redirect_stderr(stderr):
                result = acceptance.main(
                    [], status=reporter, resolve_current_head=True
                )

            run_root = next(
                (repository / "build" / "phase186" / "test-preflight-fail").iterdir()
            )
            terminal = json.loads(
                (run_root / "terminal-summary.json").read_text(encoding="utf-8")
            )

        self.assertEqual(acceptance.EXIT_FAIL, result)
        self.assertEqual(HEAD, terminal["head"])
        self.assertEqual("FAIL_PACKAGE_COMPOSITION", terminal["failureCode"])
        self.assertTrue(terminal["cleanup"]["complete"])
        self.assertEqual(terminal, protocol.validate_terminal_summary(terminal))
        remove.assert_called_once_with(owned)
        self.assertEqual("", stderr.getvalue())
        self.assertLess(
            stdout.getvalue().index("HUMAN PREFLIGHT FAIL"),
            stdout.getvalue().index("PHASE186_ACCEPTANCE_FAIL"),
        )

    def test_reporter_preauthority_failure_persists_null_head_before_machine(self) -> None:
        args = types.SimpleNamespace(
            case="manual-jazzy-fastrtps-duplex",
            manual=True,
            expected_head=None,
            output_root=pathlib.Path("build/phase186/test-preauthority-fail"),
            unity_editor=None,
            run_id=None,
            bridge_port=None,
            foxglove_port=None,
            domain_id=None,
            runtime_row=None,
            unity_composition="repository-all-providers",
            preflight_only=False,
            manual_timeout_seconds=1800.0,
        )
        reporter = mock.Mock()
        reporter.terminal.side_effect = lambda *_args: print("HUMAN PREAUTHORITY FAIL")
        stdout = io.StringIO()
        stderr = io.StringIO()
        with tempfile.TemporaryDirectory() as temp:
            repository = pathlib.Path(temp).resolve()
            (repository / "build" / "phase186").mkdir(parents=True)
            with mock.patch.object(acceptance, "parse_args", return_value=args), \
                    mock.patch.object(acceptance, "validate_arguments", return_value=args), \
                    mock.patch.object(acceptance, "repository_root", return_value=repository), \
                    mock.patch.object(
                        acceptance,
                        "_new_run_identity",
                        return_value=(
                            "phase186h-preauthority-fail-012345",
                            "p186h_0123456789abcdef01234567",
                        ),
                    ), \
                    mock.patch.object(
                        acceptance,
                        "git_head",
                        side_effect=acceptance.AcceptanceFailure(
                            "FAIL_PREFLIGHT", "Git HEAD could not be read"
                        ),
                    ), \
                    mock.patch.object(acceptance, "_create_owned_unity_project") as create, \
                    contextlib.redirect_stdout(stdout), contextlib.redirect_stderr(stderr):
                result = acceptance.main(
                    [], status=reporter, resolve_current_head=True
                )

            run_root = next(
                (repository / "build" / "phase186" / "test-preauthority-fail").iterdir()
            )
            evidence_path = run_root / "terminal-preauthority-failure.json"
            terminal = json.loads(evidence_path.read_text(encoding="utf-8"))

        self.assertEqual(acceptance.EXIT_FAIL, result)
        self.assertIsNone(terminal["head"])
        self.assertFalse(terminal["headObserved"])
        self.assertEqual("FAIL_PREFLIGHT", terminal["failureCode"])
        self.assertEqual("Git HEAD could not be read", terminal["failureMessage"])
        self.assertTrue(terminal["cleanup"]["complete"])
        create.assert_not_called()
        self.assertEqual("", stderr.getvalue())
        self.assertLess(
            stdout.getvalue().index("HUMAN PREAUTHORITY FAIL"),
            stdout.getvalue().index("PHASE186_PREAUTHORITY_FAIL"),
        )
        self.assertIn(str(evidence_path), stdout.getvalue())

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

    def test_live_cases_lock_their_unity_package_composition(self) -> None:
        full_duplex = acceptance.validate_arguments(
            acceptance.parse_args(
                [
                    "--case",
                    "full-duplex",
                    "--expected-head",
                    HEAD,
                    "--output-root",
                    r"D:\evidence",
                ]
            )
        )
        fanout = acceptance.validate_arguments(
            acceptance.parse_args(
                [
                    "--case",
                    "fanout-fairness-health",
                    "--expected-head",
                    HEAD,
                    "--output-root",
                    r"D:\evidence",
                ]
            )
        )
        self.assertEqual("bridge-only", full_duplex.unity_composition)
        self.assertEqual("repository-all-providers", fanout.unity_composition)
        with self.assertRaises(protocol.ProtocolFailure):
            acceptance.validate_arguments(
                acceptance.parse_args(
                    [
                        "--case",
                        "full-duplex",
                        "--expected-head",
                        HEAD,
                        "--output-root",
                        r"D:\evidence",
                        "--unity-composition",
                        "repository-all-providers",
                    ]
                )
            )

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

    def test_owned_bridge_only_project_contains_no_r2fu_or_typesupport_package(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            repository = pathlib.Path(temp).resolve()
            base = repository / "Unity2Foxglove"
            (base / "ProjectSettings").mkdir(parents=True)
            (base / "ProjectSettings" / "ProjectVersion.txt").write_text(
                "m_EditorVersion: 6000.3.14f1\n",
                encoding="utf-8",
            )
            (base / "Packages").mkdir()
            (base / "Packages" / "manifest.json").write_text(
                json.dumps(
                    {
                        "dependencies": {
                            "com.unity.modules.jsonserialize": "1.0.0",
                            "com.unity.inputsystem": "1.19.0",
                            "dev.unity2foxglove.sdk": "file:../../Packages/dev.unity2foxglove.sdk",
                            "dev.unity2foxglove.ros2bridge": "file:../../Packages/dev.unity2foxglove.ros2bridge",
                            "dev.unity2foxglove.ros2forunity": "file:../../Packages/dev.unity2foxglove.ros2forunity",
                            "dev.unity2foxglove.foxrun.ros2.interfaces": "file:../../Packages/dev.unity2foxglove.foxrun.ros2.interfaces",
                        }
                    }
                ),
                encoding="utf-8",
            )
            for package in (
                "dev.unity2foxglove.sdk",
                "dev.unity2foxglove.ros2bridge",
            ):
                (repository / "Packages" / package).mkdir(parents=True)
            for relative_text in bridge_project._ASSET_PATHS:
                source = repository / relative_text
                source.parent.mkdir(parents=True, exist_ok=True)
                source.write_text(relative_text, encoding="utf-8")
            owned = bridge_project.create_bridge_only_project(
                repository,
                "phase186h-test-0123456789ab",
            )
            evidence = bridge_project.validate_bridge_only_manifest(owned.path)
            self.assertEqual(
                [
                    "dev.unity2foxglove.ros2bridge",
                    "dev.unity2foxglove.sdk",
                ],
                evidence["productPackages"],
            )
            manifest = json.loads(
                (owned.path / "Packages" / "manifest.json").read_text(
                    encoding="utf-8"
                )
            )
            self.assertNotIn(
                "com.unity.inputsystem",
                manifest["dependencies"],
            )
            self.assertEqual(
                "1.0.0",
                manifest["dependencies"]["com.unity.modules.jsonserialize"],
            )
            self.assertTrue(
                (
                    owned.path
                    / "Assets/Scripts/ManualAcceptance/Phase181AcceptanceDto.cs"
                ).is_file()
            )
            bridge_project.cleanup_bridge_only_project(owned)
            self.assertFalse(owned.path.exists())

    def test_owned_bridge_only_project_rejects_unsafe_windows_lmdb_path(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            repository = pathlib.Path(temp).resolve() / ("x" * 120)
            with mock.patch.object(bridge_project.sys, "platform", "win32"):
                with self.assertRaisesRegex(
                    bridge_project.BridgeOnlyProjectFailure,
                    "Windows path budget",
                ):
                    bridge_project._owned_project_path(
                        repository,
                        "phase186h-test-0123456789ab",
                    )

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
            "cleanupErrors": [],
            "residualProcesses": [],
            "residualPorts": [],
            "residualOverlays": [],
            "residualTemporaryProjects": [],
        }
        acceptance.validate_cleanup_evidence(clean)
        for key in (
            "cleanupErrors",
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
                domain_id=161,
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
                domain_id=161,
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
                    domain_id=161,
                )
                source = acceptance.render_unity_run_binding(config)
                with self.subTest(case_id=case_id):
                    self.assertEqual(
                        len(config["topics"]),
                        source.count("public const string Phase186GeneratedTopic"),
                    )
                    for topic in config["topics"]:
                        self.assertEqual(1, source.count(f'= "{topic}";'))
                    for index, kind in enumerate(
                        protocol.CASE_CONTRACT_KINDS[case_id]
                    ):
                        if kind.endswith("subscribe"):
                            self.assertIn(
                                f"_incomingPhase186Generated{index}", source
                            )
                            self.assertNotIn(
                                f"_phase186GeneratedValue{index}", source
                            )
                            self.assertNotIn(
                                f"_phase186GeneratedIncoming{index}", source
                            )
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
                domain_id=161,
            )
            bridge_source = acceptance.render_unity_run_binding(bridge_config)
            mutation_method = bridge_source.split(
                "partial void Phase186Generated_PublishLocalMutation", 1
            )[1].split("private static Foxglove.Log", 1)[0]
            self.assertIn("published = false;", mutation_method)
            self.assertNotIn("evidence.LocalMutations++", mutation_method)

            fanout_output = repository / "build" / "phase186" / "phase186h-fanout-check-012345"
            fanout_output.mkdir(parents=True)
            fanout_config = protocol.make_run_config(
                repository=repository,
                project=project,
                output_root=fanout_output,
                run_id="phase186h-fanout-check-012345",
                token=token,
                case_id="fanout-fairness-health",
                head=HEAD,
                bridge_port=18767,
                domain_id=161,
            )
            fanout_source = acceptance.render_unity_run_binding(fanout_config)
            self.assertEqual(3, fanout_source.count('"unity2foxglove.r2fu"'))
            self.assertEqual(3, fanout_source.count("unity-local-b-"))
            self.assertEqual(
                ("custom_publish", "custom_publish", "custom_publish"),
                protocol.CASE_CONTRACT_KINDS["fanout-fairness-health"],
            )
            self.assertEqual(
                3,
                fanout_source.count("[SerializeField] private Phase181State"),
            )
            self.assertNotIn(
                "[SerializeField] private Foxglove.Log",
                fanout_source,
            )

    def test_duplex_binding_warms_publisher_without_claiming_local_mutation(self) -> None:
        token = "p186h_0123456789abcdef01234567"
        with tempfile.TemporaryDirectory() as temp:
            repository = pathlib.Path(temp).resolve()
            project = repository / "Unity2Foxglove"
            project.mkdir()
            output = repository / "build" / "phase186" / "phase186h-warmup-check"
            output.mkdir(parents=True)
            config = protocol.make_run_config(
                repository=repository,
                project=project,
                output_root=output,
                run_id="phase186h-warmup-check",
                token=token,
                case_id="slow-main-thread-640hz",
                head=HEAD,
                bridge_port=18767,
                domain_id=161,
            )

            source = acceptance.render_unity_run_binding(config)
            warmup_method = source.split(
                "partial void Phase186Generated_WarmPublishers", 1
            )[1].split(
                "partial void Phase186Generated_PublishLocalMutation", 1
            )[0]

            self.assertIn("unity-publisher-warmup", warmup_method)
            self.assertIn("published = true;", warmup_method)
            self.assertNotIn("evidence.LocalMutations++", warmup_method)


if __name__ == "__main__":
    unittest.main()
