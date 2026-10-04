#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Regression tests for the Phase186-H Bridge acceptance coordinator."""
from __future__ import annotations
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "test_phase186_bridge_acceptance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
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
from Scripts.phase192.compare_identity_surfaces import (
    _entrypoint_contract,
    _section_modules_for_root,
)
HEAD = "a" * 40
def _phase192_repository_root() -> pathlib.Path:
    """Locate the checkout even when the compatibility package rewrites __file__."""
    current = pathlib.Path(__file__).resolve()
    for candidate in (current.parent, *current.parents):
        if (candidate / "Scripts").is_dir() and (candidate / "Unity2Foxglove").is_dir():
            return candidate
    raise RuntimeError("Could not locate the repository root")


def _assert_entrypoint_delegates(package: pathlib.Path) -> None:
    """Require each generated module entrypoint to import its owning package."""
    entrypoint = package / "__main__.py"
    try:
        sections = _section_modules_for_root(package)(package)
        _entrypoint_contract(
            entrypoint,
            frozenset(section.stem for section in sections),
        )
    except (OSError, SyntaxError, ValueError) as exc:
        raise AssertionError(str(exc)) from exc


def _decomposed_cli_modules(repository: pathlib.Path, relative_roots: tuple[str, ...]) -> tuple[str, ...]:
    """Discover production split facades that expose module entrypoints."""
    modules: list[str] = []
    for relative_root in relative_roots:
        root = repository / relative_root
        for facade in sorted(root.rglob("*.py")):
            if "regression_checks" in facade.parts:
                continue
            package = facade.with_suffix("")
            if package.is_dir() and (package / "__init__.py").is_file():
                if not (package / "__main__.py").is_file():
                    raise AssertionError(f"Missing module entrypoint for {facade}")
                _assert_entrypoint_delegates(package)
                modules.append(".".join(facade.relative_to(repository).with_suffix("").parts))
    if not modules:
        raise AssertionError(f"No production split facades found under {relative_roots}")
    return tuple(modules)


class _Phase186BridgeAcceptanceTests_support:
    """Decomposed Phase192 implementation component."""
    def test_direct_script_bootstrap_can_import_deferred_live_runner(self) -> None:
        """Verify that direct script bootstrap can import deferred live runner."""
        repository = _phase192_repository_root()
        script_directory = repository / 'Scripts' / 'smoke' / 'foxrun'
        probe = '\nimport importlib\nimport pathlib\nimport sys\n\nscript_directory = pathlib.Path(sys.argv[1]).resolve()\nsys.path.insert(0, str(script_directory))\nimportlib.import_module("phase186_bridge_acceptance")\nimportlib.import_module("Scripts.smoke.foxrun.phase186_bridge_live")\n'
        with tempfile.TemporaryDirectory() as temp:
            completed = subprocess.run([sys.executable, '-I', '-c', probe, str(script_directory)], cwd=temp, capture_output=True, text=True, check=False)
        self.assertEqual(0, completed.returncode, completed.stderr)
    def test_decomposed_smoke_packages_support_module_execution(self) -> None:
        """Verify every decomposed smoke package has a working module entrypoint."""
        repository = _phase192_repository_root()
        modules = tuple(
            module.rsplit(".", 1)[-1]
            for module in _decomposed_cli_modules(repository, ("Scripts/smoke/foxrun",))
        )
        for module in modules:
            with self.subTest(module=module):
                completed = subprocess.run(
                    [sys.executable, '-m', f'Scripts.smoke.foxrun.{module}', '--help'],
                    cwd=repository,
                    capture_output=True,
                    text=True,
                    check=False,
                    timeout=60,
                )
                self.assertEqual(
                    0,
                    completed.returncode,
                    f'{module}\nstdout:\n{completed.stdout}\nstderr:\n{completed.stderr}',
                )
    def test_ros2_and_websocket_decomposed_packages_support_module_execution(self) -> None:
        """Verify every production ROS2 and WebSocket split package has a CLI entrypoint."""
        repository = _phase192_repository_root()
        modules = _decomposed_cli_modules(
            repository,
            ("Scripts/smoke/ros2", "Scripts/smoke/websocket"),
        )
        for module in modules:
            with self.subTest(module=module):
                completed = subprocess.run(
                    [sys.executable, "-m", module, "--help"],
                    cwd=repository,
                    capture_output=True,
                    text=True,
                    check=False,
                    timeout=60,
                )
                self.assertEqual(
                    0,
                    completed.returncode,
                    f"{module}\nstdout:\n{completed.stdout}\nstderr:\n{completed.stderr}",
                )

    def test_module_entrypoint_rejects_non_delegating_noop(self) -> None:
        """A non-star entrypoint must call an imported package callable."""
        with tempfile.TemporaryDirectory() as temp:
            package = pathlib.Path(temp) / "fixture"
            package.mkdir()
            (package / "__main__.py").write_text(
                "from . import main\n",
                encoding="utf-8",
            )
            with self.assertRaises(AssertionError):
                _assert_entrypoint_delegates(package)

    def test_module_entrypoint_rejects_unsafe_guard_forms(self) -> None:
        """The acceptance checker shares the identity gate's strict dispatch contract."""
        for entrypoint_source in (
            "from . import *\nif __name__ is '__main__': main()\n",
            "from . import *\nif __name__ == '__main__':\n    while True:\n        break\n",
            "from . import *\nif __name__ == '__main__':\n    def main():\n        import os\n    main()\n",
        ):
            with self.subTest(entrypoint_source=entrypoint_source), tempfile.TemporaryDirectory() as temp:
                package = pathlib.Path(temp) / "fixture"
                package.mkdir()
                (package / "__init__.py").write_text(
                    "from . import core\n", encoding="utf-8"
                )
                (package / "core.py").write_text(
                    "def main(): pass\n", encoding="utf-8"
                )
                (package / "__main__.py").write_text(
                    entrypoint_source, encoding="utf-8"
                )
                with self.assertRaises(AssertionError):
                    _assert_entrypoint_delegates(package)

    def test_reporter_handoff_precedes_pass_fail_not_run_machine_markers(self) -> None:
        """Verify that reporter handoff precedes pass fail not run machine markers."""
        for verdict, reason in (('PASS', 'cleanup complete'), ('FAIL', 'FAIL_RUNTIME: peer stopped'), ('NOT RUN', 'Unity license unavailable')):
            with self.subTest(verdict=verdict):
                ordered: list[str] = []
                reporter = mock.Mock()
                reporter.terminal.side_effect = lambda value, *_args: ordered.append('human:' + value)
                with mock.patch.object(acceptance, 'print', side_effect=lambda value, **_kwargs: ordered.append('machine:' + value), create=True):
                    acceptance._emit_terminal_handoff(reporter, verdict=verdict, reason=reason, evidence_root=pathlib.Path('D:\\evidence\\run'), machine_line='PHASE186_TERMINAL ' + verdict)
                self.assertEqual(['human:' + verdict, 'machine:PHASE186_TERMINAL ' + verdict], ordered)
                reporter.terminal.assert_called_once_with(verdict, reason, str(pathlib.Path('D:\\evidence\\run').resolve()))
    def test_direct_cli_still_requires_expected_head(self) -> None:
        """Verify that direct cli still requires expected head."""
        with self.assertRaises(SystemExit):
            acceptance.parse_args(['--case', 'manual-jazzy-fastrtps-duplex', '--manual', '--output-root', 'D:\\evidence'])
    def test_automatic_preflight_stdout_and_stderr_remain_exact_without_status(self) -> None:
        """Verify that automatic preflight stdout and stderr remain exact without status."""
        args = types.SimpleNamespace(case='full-duplex', manual=False, expected_head=HEAD, output_root=pathlib.Path('build/phase186/test-automatic'), unity_editor=None, run_id=None, bridge_port=None, foxglove_port=None, domain_id=None, runtime_row=None, unity_composition='bridge-only', preflight_only=True, manual_timeout_seconds=1800.0)
    
        class Reservation:
            """Represent reservation."""
    
            def __init__(self, port: int) -> None:
                """Initialize the helper state."""
                self.port = port
    
            def __enter__(self):
                """Enter the managed acceptance context."""
                return self
    
            def __exit__(self, *_unused) -> None:
                """Exit the managed acceptance context."""
                return None
        stdout = io.StringIO()
        stderr = io.StringIO()
        with tempfile.TemporaryDirectory() as temp:
            repository = pathlib.Path(temp).resolve()
            (repository / 'build' / 'phase186').mkdir(parents=True)
            reservations = iter((Reservation(18767), Reservation(18768)))
            with mock.patch.object(acceptance, 'parse_args', return_value=args), mock.patch.object(acceptance, 'validate_arguments', return_value=args), mock.patch.object(acceptance, 'repository_root', return_value=repository), mock.patch.object(acceptance, '_new_run_identity', return_value=('phase186h-full-duplex-0123456789ab', 'p186h_0123456789abcdef01234567')), mock.patch.object(acceptance, '_create_owned_unity_project', return_value=None), mock.patch.object(acceptance, 'reserve_loopback_port', side_effect=lambda *_: next(reservations)), mock.patch.object(acceptance, '_preflight', return_value={'unity': {'path': 'Unity.exe'}}), mock.patch.object(acceptance, '_remove_owned_unity_project'), contextlib.redirect_stdout(stdout), contextlib.redirect_stderr(stderr):
                result = acceptance.main([])
        self.assertEqual(acceptance.EXIT_PASS, result)
        self.assertEqual('PHASE186_PREFLIGHT_PASS run=phase186h-full-duplex-0123456789ab case=full-duplex tokenHash=' + protocol.token_sha256('p186h_0123456789abcdef01234567') + f' head={HEAD}\n', stdout.getvalue())
        self.assertEqual('', stderr.getvalue())
    def test_interrupted_coordinator_head_resolution_persists_incomplete_fail(self) -> None:
        """Verify that interrupted coordinator head resolution persists incomplete fail."""
        args = types.SimpleNamespace(case='manual-jazzy-fastrtps-duplex', manual=True, expected_head=None, output_root=pathlib.Path('build/phase186/test-manual'), unity_editor=None, run_id=None, bridge_port=None, foxglove_port=None, domain_id=None, runtime_row=None, unity_composition='repository-all-providers', preflight_only=False, manual_timeout_seconds=1800.0)
        reporter = mock.Mock()
        reporter.terminal.side_effect = lambda *_args: print('PHASE186 TEST HUMAN HANDOFF')
        stdout = io.StringIO()
        with tempfile.TemporaryDirectory() as temp:
            repository = pathlib.Path(temp).resolve()
            (repository / 'build' / 'phase186').mkdir(parents=True)
            with mock.patch.object(acceptance, 'parse_args', return_value=args), mock.patch.object(acceptance, 'validate_arguments', return_value=args), mock.patch.object(acceptance, 'repository_root', return_value=repository), mock.patch.object(acceptance, '_new_run_identity', return_value=('phase186h-jazzy-fastrtps-012345', 'p186h_0123456789abcdef01234567')), mock.patch.object(acceptance, 'git_head', side_effect=KeyboardInterrupt), mock.patch.object(protocol, 'make_failure_summary', side_effect=AssertionError('unknown HEAD entered terminal protocol')), contextlib.redirect_stdout(stdout):
                result = acceptance.main([], status=reporter, resolve_current_head=True)
            run_root = next((repository / 'build' / 'phase186' / 'test-manual').iterdir())
            interrupted_path = run_root / 'terminal-interrupted.json'
            terminal = json.loads(interrupted_path.read_text(encoding='utf-8'))
            terminal_summary_exists = (run_root / 'terminal-summary.json').exists()
            terminal_marker_exists = (run_root / 'terminal-marker.txt').exists()
            observed_root = repository / 'build' / 'phase186' / 'observed-head'
            observed_root.mkdir()
            with contextlib.redirect_stdout(io.StringIO()):
                observed = acceptance._persist_interrupted(observed_root, run_id='phase186h-observed-head-012345', token='p186h_0123456789abcdef01234567', case_id='manual-jazzy-fastrtps-duplex', head=HEAD, stage='manual wait')
            observed_persisted = json.loads((observed_root / 'terminal-summary.json').read_text(encoding='utf-8'))
            observed_interrupted_exists = (observed_root / 'terminal-interrupted.json').exists()
        self.assertEqual(acceptance.EXIT_FAIL, result)
        self.assertEqual({'schemaVersion', 'runId', 'tokenHash', 'caseId', 'head', 'headObserved', 'verdict', 'failureCode', 'stage', 'failureMessage', 'cleanup'}, set(terminal))
        self.assertEqual('FAIL_INTERRUPTED', terminal['failureCode'])
        self.assertIsNone(terminal['head'])
        self.assertFalse(terminal['headObserved'])
        self.assertIn('repository/HEAD/Unity/ports', terminal['failureMessage'])
        self.assertFalse(terminal['cleanup']['complete'])
        self.assertTrue(any(('not observed' in value for value in terminal['cleanup']['cleanupErrors'])))
        self.assertNotIn('or protocol.clean_cleanup_evidence()', inspect.getsource(acceptance.main))
        reporter.transition.assert_any_call('1/5', 'checking repository, HEAD, Unity, and ports')
        reporter.close.assert_not_called()
        self.assertIn('headObserved=false', stdout.getvalue())
        self.assertIn(str(interrupted_path), stdout.getvalue())
        self.assertLess(stdout.getvalue().index('PHASE186 TEST HUMAN HANDOFF'), stdout.getvalue().index('PHASE186_INTERRUPTED_FAIL'))
        self.assertFalse(terminal_summary_exists)
        self.assertFalse(terminal_marker_exists)
        self.assertEqual(HEAD, observed['head'])
        self.assertEqual(observed, observed_persisted)
        self.assertEqual(observed, protocol.validate_terminal_summary(observed))
        self.assertFalse(observed_interrupted_exists)
    def test_reporter_preflight_failure_with_head_uses_validated_terminal(self) -> None:
        """Verify that reporter preflight failure with head uses validated terminal."""
        args = types.SimpleNamespace(case='manual-jazzy-fastrtps-duplex', manual=True, expected_head=None, output_root=pathlib.Path('build/phase186/test-preflight-fail'), unity_editor=None, run_id=None, bridge_port=None, foxglove_port=None, domain_id=None, runtime_row=None, unity_composition='repository-all-providers', preflight_only=False, manual_timeout_seconds=1800.0)
    
        class Reservation:
            """Represent reservation."""
    
            def __init__(self, port: int) -> None:
                """Initialize the helper state."""
                self.port = port
    
            def __enter__(self):
                """Enter the managed acceptance context."""
                return self
    
            def __exit__(self, *_unused) -> None:
                """Exit the managed acceptance context."""
                return None
        reporter = mock.Mock()
        reporter.terminal.side_effect = lambda *_args: print('HUMAN PREFLIGHT FAIL')
        stdout = io.StringIO()
        stderr = io.StringIO()
        with tempfile.TemporaryDirectory() as temp:
            repository = pathlib.Path(temp).resolve()
            (repository / 'build' / 'phase186').mkdir(parents=True)
            owned = types.SimpleNamespace(path=repository / 'owned-project')
            reservations = iter((Reservation(18767), Reservation(18768)))
            with mock.patch.object(acceptance, 'parse_args', return_value=args), mock.patch.object(acceptance, 'validate_arguments', return_value=args), mock.patch.object(acceptance, 'repository_root', return_value=repository), mock.patch.object(acceptance, '_new_run_identity', return_value=('phase186h-preflight-fail-012345', 'p186h_0123456789abcdef01234567')), mock.patch.object(acceptance, 'git_head', return_value=HEAD), mock.patch.object(acceptance, '_create_owned_unity_project', return_value=owned), mock.patch.object(acceptance, 'reserve_loopback_port', side_effect=lambda *_: next(reservations)), mock.patch.object(acceptance, '_preflight', side_effect=acceptance.AcceptanceFailure('FAIL_PACKAGE_COMPOSITION', 'authority drift')), mock.patch.object(acceptance, '_remove_owned_unity_project') as remove, contextlib.redirect_stdout(stdout), contextlib.redirect_stderr(stderr):
                result = acceptance.main([], status=reporter, resolve_current_head=True)
            run_root = next((repository / 'build' / 'phase186' / 'test-preflight-fail').iterdir())
            terminal = json.loads((run_root / 'terminal-summary.json').read_text(encoding='utf-8'))
        self.assertEqual(acceptance.EXIT_FAIL, result)
        self.assertEqual(HEAD, terminal['head'])
        self.assertEqual('FAIL_PACKAGE_COMPOSITION', terminal['failureCode'])
        self.assertTrue(terminal['cleanup']['complete'])
        self.assertEqual(terminal, protocol.validate_terminal_summary(terminal))
        remove.assert_called_once_with(owned)
        self.assertEqual('', stderr.getvalue())
        self.assertLess(stdout.getvalue().index('HUMAN PREFLIGHT FAIL'), stdout.getvalue().index('PHASE186_ACCEPTANCE_FAIL'))
    def test_reporter_preauthority_failure_persists_null_head_before_machine(self) -> None:
        """Verify that reporter preauthority failure persists null head before machine."""
        args = types.SimpleNamespace(case='manual-jazzy-fastrtps-duplex', manual=True, expected_head=None, output_root=pathlib.Path('build/phase186/test-preauthority-fail'), unity_editor=None, run_id=None, bridge_port=None, foxglove_port=None, domain_id=None, runtime_row=None, unity_composition='repository-all-providers', preflight_only=False, manual_timeout_seconds=1800.0)
        reporter = mock.Mock()
        reporter.terminal.side_effect = lambda *_args: print('HUMAN PREAUTHORITY FAIL')
        stdout = io.StringIO()
        stderr = io.StringIO()
        with tempfile.TemporaryDirectory() as temp:
            repository = pathlib.Path(temp).resolve()
            (repository / 'build' / 'phase186').mkdir(parents=True)
            with mock.patch.object(acceptance, 'parse_args', return_value=args), mock.patch.object(acceptance, 'validate_arguments', return_value=args), mock.patch.object(acceptance, 'repository_root', return_value=repository), mock.patch.object(acceptance, '_new_run_identity', return_value=('phase186h-preauthority-fail-012345', 'p186h_0123456789abcdef01234567')), mock.patch.object(acceptance, 'git_head', side_effect=acceptance.AcceptanceFailure('FAIL_PREFLIGHT', 'Git HEAD could not be read')), mock.patch.object(acceptance, '_create_owned_unity_project') as create, contextlib.redirect_stdout(stdout), contextlib.redirect_stderr(stderr):
                result = acceptance.main([], status=reporter, resolve_current_head=True)
            run_root = next((repository / 'build' / 'phase186' / 'test-preauthority-fail').iterdir())
            evidence_path = run_root / 'terminal-preauthority-failure.json'
            terminal = json.loads(evidence_path.read_text(encoding='utf-8'))
        self.assertEqual(acceptance.EXIT_FAIL, result)
        self.assertIsNone(terminal['head'])
        self.assertFalse(terminal['headObserved'])
        self.assertEqual('FAIL_PREFLIGHT', terminal['failureCode'])
        self.assertEqual('Git HEAD could not be read', terminal['failureMessage'])
        self.assertTrue(terminal['cleanup']['complete'])
        create.assert_not_called()
        self.assertEqual('', stderr.getvalue())
        self.assertLess(stdout.getvalue().index('HUMAN PREAUTHORITY FAIL'), stdout.getvalue().index('PHASE186_PREAUTHORITY_FAIL'))
        self.assertIn(str(evidence_path), stdout.getvalue())
    def test_manual_flag_is_limited_to_the_two_manual_cases(self) -> None:
        """Verify that manual flag is limited to the two manual cases."""
        args = acceptance.parse_args(['--case', 'manual-jazzy-fastrtps-duplex', '--manual', '--expected-head', HEAD, '--output-root', 'D:\\evidence'])
        acceptance.validate_arguments(args)
        args = acceptance.parse_args(['--case', 'full-duplex', '--manual', '--expected-head', HEAD, '--output-root', 'D:\\evidence'])
        with self.assertRaises(protocol.ProtocolFailure):
            acceptance.validate_arguments(args)
    def test_automatic_cases_reject_manual_case_without_manual_flag(self) -> None:
        """Verify that automatic cases reject manual case without manual flag."""
        args = acceptance.parse_args(['--case', 'manual-lyrical-zenoh-duplex', '--expected-head', HEAD, '--output-root', 'D:\\evidence'])
        with self.assertRaises(protocol.ProtocolFailure):
            acceptance.validate_arguments(args)
    def test_live_cases_lock_their_unity_package_composition(self) -> None:
        """Verify that live cases lock their unity package composition."""
        full_duplex = acceptance.validate_arguments(acceptance.parse_args(['--case', 'full-duplex', '--expected-head', HEAD, '--output-root', 'D:\\evidence']))
        fanout = acceptance.validate_arguments(acceptance.parse_args(['--case', 'fanout-fairness-health', '--expected-head', HEAD, '--output-root', 'D:\\evidence']))
        self.assertEqual('bridge-only', full_duplex.unity_composition)
        self.assertEqual('repository-all-providers', fanout.unity_composition)
        with self.assertRaises(protocol.ProtocolFailure):
            acceptance.validate_arguments(acceptance.parse_args(['--case', 'full-duplex', '--expected-head', HEAD, '--output-root', 'D:\\evidence', '--unity-composition', 'repository-all-providers']))
    def test_expected_head_must_be_full_lowercase_sha(self) -> None:
        """Verify that expected head must be full lowercase sha."""
        args = acceptance.parse_args(['--case', 'full-duplex', '--expected-head', 'abc', '--output-root', 'D:\\evidence'])
        with self.assertRaises(protocol.ProtocolFailure):
            acceptance.validate_arguments(args)
    def test_cli_domain_id_uses_the_windows_safe_authority_limit(self) -> None:
        """Verify that CLI validation rejects IDs the run-config authority rejects."""
        args = acceptance.parse_args(['--case', 'full-duplex', '--expected-head', HEAD, '--output-root', 'D:\\evidence', '--domain-id', str(protocol.WINDOWS_SAFE_ROS_DOMAIN_ID_MAX + 1)])
        with self.assertRaisesRegex(protocol.ProtocolFailure, f'0\\.\\.{protocol.WINDOWS_SAFE_ROS_DOMAIN_ID_MAX}'):
            acceptance.validate_arguments(args)
    def test_resolve_unity_editor_locks_project_version(self) -> None:
        """Verify that resolve unity editor locks project version."""
        with tempfile.TemporaryDirectory() as temp:
            root = pathlib.Path(temp)
            project = root / 'Unity2Foxglove'
            settings = project / 'ProjectSettings'
            settings.mkdir(parents=True)
            (settings / 'ProjectVersion.txt').write_text('m_EditorVersion: 6000.3.14f1\n', encoding='utf-8')
            editor = root / 'Unity.exe'
            editor.write_bytes(b'unity')
            resolved = acceptance.resolve_unity_editor(project, editor)
            self.assertEqual(editor.resolve(), resolved.path)
            self.assertEqual('6000.3.14f1', resolved.version)
    def test_resolve_unity_editor_rejects_missing_or_malformed_version(self) -> None:
        """Verify that resolve unity editor rejects missing or malformed version."""
        with tempfile.TemporaryDirectory() as temp:
            project = pathlib.Path(temp) / 'Unity2Foxglove'
            (project / 'ProjectSettings').mkdir(parents=True)
            editor = pathlib.Path(temp) / 'Unity.exe'
            editor.write_bytes(b'unity')
            with self.assertRaises(protocol.ProtocolFailure):
                acceptance.resolve_unity_editor(project, editor)
            (project / 'ProjectSettings' / 'ProjectVersion.txt').write_text('m_EditorVersion: unsafe version\n', encoding='utf-8')
            with self.assertRaises(protocol.ProtocolFailure):
                acceptance.resolve_unity_editor(project, editor)
    def test_reserve_loopback_port_returns_owned_ipv4_socket(self) -> None:
        """Verify that reserve loopback port returns owned ipv4 socket."""
        reservation = acceptance.reserve_loopback_port()
        try:
            self.assertEqual('127.0.0.1', reservation.host)
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
        """Verify that package preflight rejects wrong ids or r2fu dependency."""
        with tempfile.TemporaryDirectory() as temp:
            repo = pathlib.Path(temp)
            sdk = repo / 'Packages' / 'dev.unity2foxglove.sdk'
            bridge = repo / 'Packages' / 'dev.unity2foxglove.ros2bridge'
            sdk.mkdir(parents=True)
            bridge.mkdir(parents=True)
            (sdk / 'package.json').write_text(json.dumps({'name': 'dev.unity2foxglove.sdk'}), encoding='utf-8')
            (bridge / 'package.json').write_text(json.dumps({'name': 'dev.unity2foxglove.ros2bridge', 'dependencies': {'dev.unity2foxglove.sdk': '1.9.6'}}), encoding='utf-8')
            self.assertEqual('dev.unity2foxglove.ros2bridge', acceptance.validate_package_manifests(repo)['bridgePackage'])
            (bridge / 'package.json').write_text(json.dumps({'name': 'dev.unity2foxglove.ros2bridge', 'dependencies': {'dev.unity2foxglove.sdk': '1.9.6', 'dev.unity2foxglove.ros2forunity': '0.9.0'}}), encoding='utf-8')
            with self.assertRaises(protocol.ProtocolFailure):
                acceptance.validate_package_manifests(repo)
    def test_owned_bridge_only_project_contains_no_r2fu_or_typesupport_package(self) -> None:
        """Verify that owned bridge only project contains no r2fu or typesupport package."""
        self.assertIn('Unity2Foxglove/Assets/Scripts/ManualAcceptance/Phase186ManualInteractionState.cs', bridge_project._ASSET_PATHS)
        self.assertIn('Unity2Foxglove/Assets/Scripts/ManualAcceptance/Phase186ManualInteractionState.cs.meta', bridge_project._ASSET_PATHS)
        with tempfile.TemporaryDirectory() as temp:
            repository = pathlib.Path(temp).resolve()
            base = repository / 'Unity2Foxglove'
            (base / 'ProjectSettings').mkdir(parents=True)
            (base / 'ProjectSettings' / 'ProjectVersion.txt').write_text('m_EditorVersion: 6000.3.14f1\n', encoding='utf-8')
            (base / 'Packages').mkdir()
            (base / 'Packages' / 'manifest.json').write_text(json.dumps({'dependencies': {'com.unity.modules.jsonserialize': '1.0.0', 'com.unity.inputsystem': '1.19.0', 'dev.unity2foxglove.sdk': 'file:../../Packages/dev.unity2foxglove.sdk', 'dev.unity2foxglove.ros2bridge': 'file:../../Packages/dev.unity2foxglove.ros2bridge', 'dev.unity2foxglove.ros2forunity': 'file:../../Packages/dev.unity2foxglove.ros2forunity', 'dev.unity2foxglove.foxrun.ros2.interfaces': 'file:../../Packages/dev.unity2foxglove.foxrun.ros2.interfaces'}}), encoding='utf-8')
            for package in ('dev.unity2foxglove.sdk', 'dev.unity2foxglove.ros2bridge'):
                (repository / 'Packages' / package).mkdir(parents=True)
            for relative_text in bridge_project._ASSET_PATHS:
                source = repository / relative_text
                source.parent.mkdir(parents=True, exist_ok=True)
                source.write_text(relative_text, encoding='utf-8')
            owned = bridge_project.create_bridge_only_project(repository, 'phase186h-test-0123456789ab')
            evidence = bridge_project.validate_bridge_only_manifest(owned.path)
            self.assertEqual(['dev.unity2foxglove.ros2bridge', 'dev.unity2foxglove.sdk'], evidence['productPackages'])
            manifest = json.loads((owned.path / 'Packages' / 'manifest.json').read_text(encoding='utf-8'))
            self.assertNotIn('com.unity.inputsystem', manifest['dependencies'])
            self.assertEqual('1.0.0', manifest['dependencies']['com.unity.modules.jsonserialize'])
            self.assertTrue((owned.path / 'Assets/Scripts/ManualAcceptance/Phase181AcceptanceDto.cs').is_file())
            bridge_project.cleanup_bridge_only_project(owned)
            self.assertFalse(owned.path.exists())
    def test_owned_bridge_only_project_rejects_unsafe_windows_lmdb_path(self) -> None:
        """Verify that owned bridge only project rejects unsafe windows lmdb path."""
        with tempfile.TemporaryDirectory() as temp:
            repository = pathlib.Path(temp).resolve() / ('x' * 120)
            with mock.patch.object(bridge_project.sys, 'platform', 'win32'):
                with self.assertRaisesRegex(bridge_project.BridgeOnlyProjectFailure, 'Windows path budget'):
                    bridge_project._owned_project_path(repository, 'phase186h-test-0123456789ab')
    def test_bridge_only_manifest_wraps_missing_product_package(self) -> None:
        """Verify that missing package roots surface a stable staging failure."""
        with tempfile.TemporaryDirectory() as temp:
            repository = pathlib.Path(temp).resolve()
            packages = repository / 'Unity2Foxglove' / 'Packages'
            packages.mkdir(parents=True)
            (packages / 'manifest.json').write_text(json.dumps({'dependencies': {'com.unity.modules.jsonserialize': '1.0.0'}}), encoding='utf-8')
            with self.assertRaisesRegex(bridge_project.BridgeOnlyProjectFailure, 'product package is unavailable'):
                bridge_project._bridge_only_manifest(repository)
    def test_bridge_only_project_wraps_asset_copy_failure_and_rolls_back(self) -> None:
        """Verify that required-asset copy failures are stable and leave no project."""
        with tempfile.TemporaryDirectory() as temp:
            repository = pathlib.Path(temp).resolve()
            project = repository / 'Unity2Foxglove'
            (project / 'ProjectSettings').mkdir(parents=True)
            (project / 'Packages').mkdir()
            (project / 'Packages' / 'manifest.json').write_text(json.dumps({'dependencies': {}}), encoding='utf-8')
            for package in ('dev.unity2foxglove.sdk', 'dev.unity2foxglove.ros2bridge'):
                (repository / 'Packages' / package).mkdir(parents=True)
            asset = project / 'Assets' / 'required.cs'
            asset.parent.mkdir(parents=True)
            asset.write_text('required', encoding='utf-8')
            token = 'phase186h-test-0123456789ab'
            target = bridge_project._owned_project_path(repository, token)
            with mock.patch.object(bridge_project, '_ASSET_PATHS', ('Unity2Foxglove/Assets/required.cs',)), mock.patch.object(bridge_project.shutil, 'copy2', side_effect=OSError('copy failed')), self.assertRaisesRegex(bridge_project.BridgeOnlyProjectFailure, 'acceptance asset is unavailable'):
                bridge_project.create_bridge_only_project(repository, token)
            self.assertFalse(target.exists())
    def test_bridge_only_project_reports_rollback_failure(self) -> None:
        """Verify that a failed staging rollback cannot be silently ignored."""
        with tempfile.TemporaryDirectory() as temp:
            repository = pathlib.Path(temp).resolve()
            (repository / 'Unity2Foxglove' / 'ProjectSettings').mkdir(parents=True)
            token = 'phase186h-test-0123456789ab'
            with mock.patch.object(bridge_project.shutil, 'copytree', side_effect=RuntimeError('staging failed')), mock.patch.object(bridge_project.shutil, 'rmtree', side_effect=OSError('rollback failed')), self.assertRaisesRegex(bridge_project.BridgeOnlyProjectFailure, 'rollback failed'):
                bridge_project.create_bridge_only_project(repository, token)
    def test_find_current_run_marker_rejects_stale_and_accepts_exact(self) -> None:
        """Verify that find current run marker rejects stale and accepts exact."""
        token = 'p186h_0123456789abcdef01234567'
        run_id = 'phase186h-run-0123456789ab'
        exact = protocol.format_manual_completion_marker(case_id='manual-jazzy-fastrtps-duplex', run_id=run_id, token=token, head=HEAD, verdict='PASS')
        lines = [exact.replace(run_id, 'phase186h-old-0123456789ab'), exact]
        self.assertEqual(exact, acceptance.find_current_manual_marker(lines, case_id='manual-jazzy-fastrtps-duplex', run_id=run_id, token=token, head=HEAD))
        with self.assertRaises(protocol.ProtocolFailure):
            acceptance.find_current_manual_marker(lines[:1], case_id='manual-jazzy-fastrtps-duplex', run_id=run_id, token=token, head=HEAD)
    def test_owned_cleanup_requires_no_process_port_or_temp_residue(self) -> None:
        """Verify that owned cleanup requires no process port or temp residue."""
        clean = {'complete': True, 'cleanupErrors': [], 'residualProcesses': [], 'residualPorts': [], 'residualOverlays': [], 'residualTemporaryProjects': []}
        acceptance.validate_cleanup_evidence(clean)
        for key in ('cleanupErrors', 'residualProcesses', 'residualPorts', 'residualOverlays', 'residualTemporaryProjects'):
            dirty = dict(clean)
            dirty[key] = ['leftover']
            with self.subTest(key=key):
                with self.assertRaises(protocol.ProtocolFailure):
                    acceptance.validate_cleanup_evidence(dirty)
    def test_live_pass_cannot_be_derived_from_build_summary(self) -> None:
        """Verify that live pass cannot be derived from build summary."""
        build = {'verdict': 'PASS', 'rowId': 'jazzy-fastrtps'}
        with self.assertRaises(protocol.ProtocolFailure):
            acceptance.promote_build_to_live_summary(build)
    def test_missing_prerequisite_persists_not_run_and_returns_blocking_code(self) -> None:
        """Verify that missing prerequisite persists not run and returns blocking code."""
        with tempfile.TemporaryDirectory() as temp:
            output = pathlib.Path(temp)
            result = acceptance.persist_not_run(output, run_id='phase186h-run-0123456789ab', token='p186h_0123456789abcdef01234567', case_id='manual-jazzy-fastrtps-duplex', head=HEAD, prerequisite='Unity 6000.3.14f1 license')
            self.assertEqual('NOT RUN', result['verdict'])
            self.assertEqual(acceptance.EXIT_NOT_RUN, protocol.verdict_exit_code(result))
            persisted = json.loads((output / 'terminal-summary.json').read_text(encoding='utf-8'))
            self.assertEqual(result, persisted)
    def test_preflight_checks_current_git_head_not_only_requested_text(self) -> None:
        """Verify that preflight checks current git head not only requested text."""
        with mock.patch.object(acceptance, 'git_head', return_value='b' * 40):
            with self.assertRaises(protocol.ProtocolFailure):
                acceptance.require_exact_head(pathlib.Path('D:/repo'), HEAD)
    def test_generated_unity_binding_is_token_scoped_and_uses_real_cdr_shapes(self) -> None:
        """Verify that generated unity binding is token scoped and uses real cdr shapes."""
        token = 'p186h_0123456789abcdef01234567'
        run_id = 'phase186h-manual-0123456789ab'
        with tempfile.TemporaryDirectory() as temp:
            repository = pathlib.Path(temp).resolve()
            project = repository / 'Unity2Foxglove'
            project.mkdir()
            output = repository / 'build' / 'phase186' / 'acceptance' / run_id
            output.mkdir(parents=True)
            config = protocol.make_run_config(repository=repository, project=project, output_root=output, run_id=run_id, token=token, case_id='manual-jazzy-fastrtps-duplex', head=HEAD, bridge_port=18767, domain_id=161)
            source = acceptance.render_unity_run_binding(config)
            for topic in config['topics']:
                self.assertIn(topic, source)
            self.assertIn('Foxglove.Log', source)
            self.assertIn('Phase181State', source)
            self.assertIn(protocol.INTERFACE_DIGEST, source)
            self.assertIn('Mode = FoxRunFlow.PublishAndSubscribe', source)
            self.assertIn('SubscribeTransportId = Ros2BridgeTransportProvider.ProviderId', source)
            self.assertNotIn('/foxrun/phase181/', source)
            self.assertNotIn('/foxrun/phase184/', source)
    def test_generated_unity_binding_install_and_cleanup_are_content_owned(self) -> None:
        """Verify that generated unity binding install and cleanup are content owned."""
        token = 'p186h_0123456789abcdef01234567'
        run_id = 'phase186h-source-0123456789ab'
        with tempfile.TemporaryDirectory() as temp:
            repository = pathlib.Path(temp).resolve()
            project = repository / 'Unity2Foxglove'
            project.mkdir()
            output = repository / 'build' / 'phase186' / 'acceptance' / run_id
            output.mkdir(parents=True)
            config = protocol.make_run_config(repository=repository, project=project, output_root=output, run_id=run_id, token=token, case_id='manual-jazzy-fastrtps-duplex', head=HEAD, bridge_port=18767, domain_id=161)
            installed = acceptance.install_unity_run_binding(project, config)
            self.assertEqual(project / 'Assets' / 'Scripts' / 'Generated' / 'Phase186AcceptanceRun.cs', installed.path)
            self.assertEqual(installed.sha256, acceptance.sha256_file(installed.path))
            installed.path.write_text('foreign', encoding='utf-8')
            with self.assertRaises(protocol.ProtocolFailure):
                acceptance.cleanup_unity_run_binding(installed)


__all__ = [name for name in globals() if not name.startswith("__")]
