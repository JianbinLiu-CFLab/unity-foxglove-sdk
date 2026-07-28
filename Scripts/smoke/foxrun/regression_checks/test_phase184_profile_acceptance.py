#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Regression checks for the owned Phase184-G acceptance orchestrator."""

from __future__ import annotations

import copy
import importlib.util
import inspect
import json
import os
import pathlib
import re
import struct
import sys
import tempfile
import unittest
from unittest import mock


ROOT = pathlib.Path(__file__).resolve().parents[4]
MODULE_PATH = ROOT / "Scripts" / "smoke" / "foxrun" / "phase184_profile_acceptance.py"
TEST_ROOT = ROOT / "build" / "phase184" / "test-orchestrator"


def load_module():
    """Load the script as a fresh module without running its CLI."""

    module_name = "phase184_profile_acceptance_under_test"
    sys.modules.pop(module_name, None)
    spec = importlib.util.spec_from_file_location(module_name, MODULE_PATH)
    if spec is None or spec.loader is None:
        raise RuntimeError("Could not load Phase184 acceptance orchestrator.")
    module = importlib.util.module_from_spec(spec)
    sys.modules[module_name] = module
    spec.loader.exec_module(module)
    return module


class FakeProcess:
    """Minimal helper-owned child used by cleanup and Job tests."""

    def __init__(self, pid: int = 184):
        """Initialize the fake process."""

        self.pid = pid
        self.returncode = None
        self.terminated = 0

    def poll(self):
        """Handle the poll step."""

        return self.returncode

    def send_signal(self, _signal):
        """Handle the send signal step."""

        self.terminated += 1
        self.returncode = 0

    def wait(self, timeout=None):
        """Wait for the configured owned process."""

        del timeout
        if self.returncode is None:
            self.returncode = 0
        return self.returncode

    def kill(self):
        """Handle the kill step."""

        self.terminated += 1
        self.returncode = -9


class FakeJobApi:
    """Records the exact hard-close ownership operations."""

    def __init__(self, *, assign_ok: bool = True):
        """Initialize the fake job API."""

        self.assign_ok = assign_ok
        self.created = 0
        self.configured = 0
        self.assigned: list[tuple[int, int]] = []
        self.closed: list[int] = []

    def create_kill_on_close_job(self):
        """Handle the create kill on close job step."""

        self.created += 1
        return 731

    def assign_pid(self, handle, pid):
        """Handle the assign PID step."""

        self.assigned.append((handle, pid))
        return self.assign_ok

    def close_handle(self, handle):
        """Handle the close handle step."""

        self.closed.append(handle)


class Phase184ProfileAcceptanceOrchestratorTests(unittest.TestCase):
    """Lock command, ownership, configuration, and evidence boundaries."""

    @staticmethod
    def _write_observed_runtime_markers(config):
        """Write current-token Unity evidence used by summary unit tests."""

        case = str(config["case"])
        token = str(config["token"])
        profile_fields = {
            "foxglove-profile": (
                "source=Foxglove targets=Foxglove "
                "publishEncoding=protobuf,json subscribeEncoding=protobuf,json"
            ),
            "multi-target": (
                "source=Ros2Native "
                "targets=Foxglove,Ros2Native,Ros2Bridge "
                "publishEncoding=protobuf subscribeEncoding=protobuf"
            ),
            "degraded-target": (
                "source=None targets=Foxglove,Ros2Bridge "
                "publishEncoding=protobuf subscribeEncoding=not_applicable"
            ),
            "qos-contract": (
                "source=None targets=Ros2Native,Ros2Bridge "
                "publishEncoding=protobuf subscribeEncoding=not_applicable"
            ),
            "stream-640hz": (
                "source=Ros2Native targets=Ros2Native "
                "publishEncoding=protobuf subscribeEncoding=protobuf"
            ),
        }
        lines = [
            "PHASE184G_PROFILE_EVIDENCE "
            f"case={case} token={token} {profile_fields[case]}"
        ]
        if case == "foxglove-profile":
            lines.append(
                "PHASE184G_FOXGLOVE_TARGET_STATUS "
                f"case={case} token={token} status=Ready "
                "succeeded=Foxglove failed=None topics=2"
            )
        elif case == "multi-target":
            lines.append(
                "PHASE184G_MULTI_TARGET_STATUS "
                f"case={case} token={token} status=Ready "
                "succeeded=Foxglove,Ros2Native,Ros2Bridge failed=None "
                "bridgeRuntimeFailures=0"
            )
        elif case == "qos-contract":
            lines.extend(
                "PHASE184G_QOS_TARGET_STATUS "
                f"case={case} token={token} topic={topic} status=Ready "
                "succeeded=Ros2Native,Ros2Bridge failed=None"
                for topic in config["topics"]
            )
        elif case == "stream-640hz":
            lines.append(
                "PHASE184G_STREAM_SUBSCRIPTION_STATUS "
                f"case={case} token={token} state=Receiving received=792 "
                "copyFailed=0 staleCallbacks=0 rejectedAfterStop=0"
            )
        log_path = pathlib.Path(str(config["unityLog"]))
        log_path.parent.mkdir(parents=True, exist_ok=True)
        log_path.write_text("\n".join(lines) + "\n", encoding="utf-8")

    @staticmethod
    def _write_reusable_runtime_selection(
        repository: pathlib.Path,
        *,
        distro: str = "jazzy",
        default_rmw: str = "rmw_fastrtps_cpp",
    ) -> tuple[str, str]:
        """Write reusable runtime selection."""

        runtime_package = (
            f"dev.unity2foxglove.ros2forunity.runtime.{distro}.win64"
        )
        addon_package = (
            "dev.unity2foxglove.foxrun.ros2.interfaces.typesupport."
            f"{distro}.win64"
        )
        runtime_reference = f"file:../../Packages/{runtime_package}"
        addon_reference = f"file:../../Packages/{addon_package}"
        packages = repository / "Unity2Foxglove" / "Packages"
        project_settings = repository / "Unity2Foxglove" / "ProjectSettings"
        runtime_root = repository / "Packages" / runtime_package
        addon_root = repository / "Packages" / addon_package
        for directory in (
            packages,
            project_settings,
            runtime_root / "RuntimeSupport",
            addon_root,
        ):
            directory.mkdir(parents=True, exist_ok=True)
        (packages / "manifest.json").write_text(
            json.dumps(
                {
                    "dependencies": {
                        runtime_package: runtime_reference,
                        addon_package: addon_reference,
                    }
                }
            ),
            encoding="utf-8",
        )
        (packages / "packages-lock.json").write_text(
            json.dumps(
                {
                    "dependencies": {
                        runtime_package: {
                            "version": runtime_reference,
                            "depth": 0,
                            "source": "local",
                            "dependencies": {},
                        },
                        addon_package: {
                            "version": addon_reference,
                            "depth": 0,
                            "source": "local",
                            "dependencies": {
                                runtime_package: "0.1.0-preview.1",
                            },
                        },
                    }
                }
            ),
            encoding="utf-8",
        )
        (runtime_root / "package.json").write_text(
            json.dumps({"name": runtime_package, "version": "0.1.0-preview.1"}),
            encoding="utf-8",
        )
        (runtime_root / "RuntimeSupport" / "runtime-manifest.json").write_text(
            json.dumps(
                {
                    "packageName": runtime_package,
                    "rosDistro": distro,
                    "platform": "win64",
                    "architecture": "x86_64",
                    "rmwImplementation": default_rmw,
                }
            ),
            encoding="utf-8",
        )
        (addon_root / "package.json").write_text(
            json.dumps(
                {
                    "name": addon_package,
                    "version": "0.1.0-preview.1",
                    "dependencies": {
                        runtime_package: "0.1.0-preview.1",
                    },
                    "unity2foxgloveFoxRunCustomTypesupportAddOn": True,
                }
            ),
            encoding="utf-8",
        )
        (project_settings / "ProjectSettings.asset").write_text(
            "  applicationIdentifier:\n"
            "    Standalone: dev.unity2foxglove.demo\n"
            "  scriptingDefineSymbols:\n"
            "    Standalone: UNITY2FOXGLOVE_ROS2_FOR_UNITY;"
            "UNITY2FOXGLOVE_FOXRUN_CUSTOM_ROS2_INTERFACES\n",
            encoding="utf-8",
        )
        return runtime_package, addon_package

    def test_windows_domain_id_stays_below_the_dynamic_port_collision_range(self):
        """Verify windows domain id stays below the dynamic port collision range."""

        module = load_module()

        self.assertEqual(0, module.choose_domain_id(0))
        self.assertEqual(166, module.choose_domain_id(166))
        for unsafe in (-1, 167, 202, 233):
            with self.subTest(unsafe=unsafe):
                with self.assertRaisesRegex(
                    module.AcceptanceFailure,
                    r"FAIL_PREFLIGHT.*0\.\.166",
                ):
                    module.choose_domain_id(unsafe)

        with mock.patch.object(module.secrets, "randbelow", return_value=0) as random:
            self.assertEqual(64, module.choose_domain_id(None))
            random.assert_called_once_with(96)
        with mock.patch.object(module.secrets, "randbelow", return_value=95):
            self.assertEqual(159, module.choose_domain_id(None))

    def test_manual_domain_defaults_to_hub_domain_zero_and_rejects_nonzero_override(self):
        """A user-owned Hub Editor cannot inherit the helper's isolated domain."""

        module = load_module()

        self.assertEqual(0, module.choose_parent_domain_id(None, "manual"))
        self.assertEqual(0, module.choose_parent_domain_id(0, "manual"))
        with self.assertRaisesRegex(
            module.AcceptanceFailure,
            r"FAIL_PREFLIGHT.*manual.*domain 0",
        ):
            module.choose_parent_domain_id(68, "manual")

        with mock.patch.object(module.secrets, "randbelow", return_value=7) as random:
            self.assertEqual(71, module.choose_parent_domain_id(None, "batch"))
            random.assert_called_once_with(96)

    def test_current_unity_runtime_is_reused_only_for_an_exact_default_selection(self):
        """Verify current unity runtime is reused only for an exact default selection."""

        module = load_module()
        TEST_ROOT.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix="runtime-reuse-", dir=TEST_ROOT) as raw:
            repository = pathlib.Path(raw) / "repository"
            runtime_package, addon_package = self._write_reusable_runtime_selection(
                repository
            )

            evidence = module._current_unity_runtime_selection_evidence(
                repository,
                "jazzy",
                "rmw_fastrtps_cpp",
            )
            self.assertEqual(
                {
                    "mode": "reused",
                    "runtimePackage": runtime_package,
                    "typesupportPackage": addon_package,
                    "rosDistro": "jazzy",
                    "rmwImplementation": "rmw_fastrtps_cpp",
                },
                evidence,
            )
            self.assertIsNone(
                module._current_unity_runtime_selection_evidence(
                    repository,
                    "jazzy",
                    "rmw_zenoh_cpp",
                )
            )

            lock_path = repository / "Unity2Foxglove" / "Packages" / "packages-lock.json"
            lock_document = json.loads(lock_path.read_text(encoding="utf-8"))
            lock_document["dependencies"][runtime_package]["depth"] = 1
            lock_path.write_text(json.dumps(lock_document), encoding="utf-8")
            self.assertIsNone(
                module._current_unity_runtime_selection_evidence(
                    repository,
                    "jazzy",
                    "rmw_fastrtps_cpp",
                )
            )

    def test_runtime_selection_reuse_skips_unity_resolve_and_persists_evidence(self):
        """Verify runtime selection reuse skips unity resolve and persists evidence."""

        module = load_module()
        TEST_ROOT.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix="runtime-skip-", dir=TEST_ROOT) as raw:
            repository = pathlib.Path(raw) / "repository"
            self._write_reusable_runtime_selection(repository)
            output = repository / "build" / "phase184" / "acceptance" / "run"
            output.mkdir(parents=True, exist_ok=True)
            peer = mock.Mock()

            with mock.patch.object(module, "_run_logged_preflight") as run:
                module._select_unity_runtime(
                    peer=peer,
                    editor=pathlib.Path(r"C:\Unity.exe"),
                    repository=repository,
                    output=output,
                    distro="jazzy",
                    rmw="rmw_fastrtps_cpp",
                    job=None,
                )

            run.assert_not_called()
            peer.build_runtime_selection_batch_command.assert_not_called()
            selection_log = (output / "runtime-selection.log").read_text(
                encoding="utf-8"
            )
            self.assertIn("PHASE184G_RUNTIME_SELECTION_REUSED", selection_log)
            self.assertIn("rmw=rmw_fastrtps_cpp", selection_log)

    def test_nondefault_rmw_falls_back_to_the_validated_unity_selector(self):
        """Verify nondefault rmw falls back to the validated unity selector."""

        module = load_module()
        TEST_ROOT.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix="runtime-select-", dir=TEST_ROOT) as raw:
            repository = pathlib.Path(raw) / "repository"
            self._write_reusable_runtime_selection(repository)
            output = repository / "build" / "phase184" / "acceptance" / "run"
            output.mkdir(parents=True, exist_ok=True)
            peer = mock.Mock()
            peer._RUNTIME_SELECTION_READY_MARKER = "PHASE181_RUNTIME_SELECTION_READY"
            peer.build_runtime_selection_batch_command.return_value = [
                r"C:\Unity.exe",
                "-batchmode",
            ]
            peer.ros2env.sanitized_subprocess_env.return_value = {}

            with mock.patch.object(module, "_run_logged_preflight") as run:
                with mock.patch.object(
                    module,
                    "read_log_lines",
                    return_value=["PHASE181_RUNTIME_SELECTION_READY"],
                ):
                    module._select_unity_runtime(
                        peer=peer,
                        editor=pathlib.Path(r"C:\Unity.exe"),
                        repository=repository,
                        output=output,
                        distro="jazzy",
                        rmw="rmw_zenoh_cpp",
                        job=None,
                    )

            run.assert_called_once()
            peer.build_runtime_selection_batch_command.assert_called_once()

    def test_cli_has_exact_parent_and_worker_modes(self):
        """Verify CLI has exact parent and worker modes."""

        module = load_module()

        parent = module.parse_args(
            [
                "--case",
                "multi-target",
                "--profile",
                "jazzy-fastrtps",
                "--unity-editor",
                r"C:\Program Files\Unity\Hub\Editor\6000.3.14f1\Editor\Unity.exe",
            ]
        )
        module.validate_arguments(parent)
        self.assertEqual("batch", parent.execution_mode)

        worker = module.parse_args(
            [
                "--worker",
                "ros2-peer",
                "--run-config",
                str(ROOT / "build" / "phase184" / "acceptance" / "run-config.json"),
            ]
        )
        module.validate_arguments(worker)
        self.assertEqual("worker", worker.execution_mode)

        contradictory = module.parse_args(
            [
                "--worker",
                "foxglove-client",
                "--run-config",
                str(ROOT / "build" / "phase184" / "acceptance" / "run-config.json"),
                "--case",
                "foxglove-profile",
            ]
        )
        with self.assertRaisesRegex(module.AcceptanceFailure, "FAIL_PREFLIGHT"):
            module.validate_arguments(contradictory)

        missing_profile = module.parse_args(
            [
                "--case",
                "multi-target",
                "--unity-editor",
                r"C:\Unity.exe",
            ]
        )
        with self.assertRaisesRegex(module.AcceptanceFailure, "FAIL_RUNTIME_SELECTION"):
            module.validate_arguments(missing_profile)

    def test_desktop_client_wait_is_parent_only_and_foxglove_batch_only(self):
        """Verify desktop client wait is parent only and foxglove batch only."""

        module = load_module()
        editor = r"C:\Unity.exe"

        accepted = module.parse_args(
            [
                "--case",
                "foxglove-profile",
                "--unity-editor",
                editor,
                "--wait-for-desktop-client",
            ]
        )
        module.validate_arguments(accepted)
        self.assertEqual("batch", accepted.execution_mode)
        self.assertTrue(accepted.wait_for_desktop_client)

        worker = module.parse_args(
            [
                "--worker",
                "foxglove-client",
                "--run-config",
                str(ROOT / "build" / "phase184" / "acceptance" / "run-config.json"),
                "--wait-for-desktop-client",
            ]
        )
        with self.assertRaisesRegex(module.AcceptanceFailure, "FAIL_PREFLIGHT"):
            module.validate_arguments(worker)

        manual = module.parse_args(
            [
                "--case",
                "foxglove-profile",
                "--unity-editor",
                editor,
                "--manual-editor",
                "--wait-for-desktop-client",
            ]
        )
        with self.assertRaisesRegex(module.AcceptanceFailure, "FAIL_PREFLIGHT"):
            module.validate_arguments(manual)

        for case, contract in module.protocol.CASE_CONTRACTS.items():
            if case == "foxglove-profile":
                continue
            with self.subTest(case=case):
                other_case = module.parse_args(
                    [
                        "--case",
                        case,
                        "--profile",
                        contract.profile,
                        "--unity-editor",
                        editor,
                        "--wait-for-desktop-client",
                    ]
                )
                with self.assertRaisesRegex(module.AcceptanceFailure, "FAIL_PREFLIGHT"):
                    module.validate_arguments(other_case)

        with self.assertRaises(SystemExit):
            module.parse_args(
                [
                    "--case",
                    "foxglove-profile",
                    "--unity-editor",
                    editor,
                    "--desktop-client-barrier",
                    r"D:\untrusted\barrier.json",
                ]
            )

    def test_command_arrays_are_direct_and_carry_only_owned_state(self):
        """Verify command arrays are direct and carry only owned state."""

        module = load_module()
        editor = pathlib.Path(r"C:\Program Files\Unity\Hub\Editor\6000.3.14f1\Editor\Unity.exe")
        project = ROOT / "Unity2Foxglove"
        config = ROOT / "build" / "phase184" / "acceptance" / "run" / "run-config.json"
        log = config.parent / "unity-editor.log"

        unity = module.build_unity_batch_command(editor, project, config, log)
        self.assertEqual(str(editor), unity[0])
        self.assertIn("-batchmode", unity)
        self.assertIn("-nographics", unity)
        self.assertEqual(
            "Unity2Foxglove.Phase184BatchModeProfileProbe.Run",
            unity[unity.index("-executeMethod") + 1],
        )
        self.assertEqual(str(config), unity[unity.index("-phase184RunConfig") + 1])
        self.assertNotIn("cmd.exe", " ".join(unity).lower())

        worker = module.build_worker_command(
            pathlib.Path(sys.executable),
            "graph-observer",
            config,
        )
        self.assertEqual(str(pathlib.Path(sys.executable)), worker[0])
        self.assertEqual("--worker", worker[2])
        self.assertEqual("graph-observer", worker[3])
        self.assertEqual(str(config), worker[-1])

        bridge = module.build_bridge_command(
            pathlib.Path(
                r"C:\phase184\install\lib\unity2foxglove_ros2_bridge"
                r"\unity2foxglove_ros2_bridge.exe"
            ),
            "127.0.0.1",
            18767,
        )
        self.assertEqual(
            [
                (
                    r"C:\phase184\install\lib\unity2foxglove_ros2_bridge"
                    r"\unity2foxglove_ros2_bridge.exe"
                ),
                "--host",
                "127.0.0.1",
                "--port",
                "18767",
                "--payload-format",
                "cdr-with-encapsulation",
            ],
            bridge,
        )

    def test_ros_workers_are_launched_and_made_ready_serially(self):
        """Verify ROS workers are launched and made ready serially."""

        module = load_module()
        events = []
        config = {
            "outputRoot": str(
                ROOT / "build" / "phase184" / "acceptance" / "serial-workers"
            )
        }
        runtime = mock.Mock(
            toolchain=mock.Mock(python_executable=pathlib.Path(r"C:\ros\python.exe")),
            actor_environment={
                "ROS_DOMAIN_ID": "184",
                "PHASE184H_DESKTOP_CLIENT_BARRIER": r"D:\ambient\barrier.json",
            },
            peer_runtime_workspace=ROOT / "build" / "phase181" / "peer",
        )

        def launch(role, *_args, **_kwargs):
            """Launch the configured owned process."""

            events.append(("launch", role))
            return FakeProcess()

        def wait(_config, roles, _owner):
            """Wait for the configured owned process."""

            events.append(("ready", tuple(roles)[0]))
            return {}

        with mock.patch.object(module, "_launch_logged_process", side_effect=launch):
            with mock.patch.object(module, "_wait_for_actor_readiness", side_effect=wait):
                module._start_case_workers_serially(
                    config=config,
                    repository=ROOT,
                    output=ROOT / "build" / "phase184" / "acceptance" / "serial-workers",
                    runtime=runtime,
                    worker_roles={"ros2-peer", "graph-observer"},
                    owner=mock.Mock(),
                    streams=[],
                )

        self.assertEqual(
            [
                ("launch", "graph-observer"),
                ("ready", "graph-observer"),
                ("launch", "ros2-peer"),
                ("ready", "ros2-peer"),
            ],
            events,
        )

    def test_desktop_barrier_environment_is_injected_only_into_foxglove_worker(self):
        """Verify desktop barrier environment is injected only into foxglove worker."""

        module = load_module()
        output = ROOT / "build" / "phase184" / "acceptance" / "barrier-workers"
        barrier = output / "desktop-client-barrier.json"
        config = {"outputRoot": str(output)}
        runtime = mock.Mock(
            toolchain=mock.Mock(python_executable=pathlib.Path(r"C:\ros\python.exe")),
            actor_environment={"ROS_DOMAIN_ID": "184"},
            peer_runtime_workspace=ROOT / "build" / "phase181" / "peer",
        )

        def capture_environments(desktop_barrier):
            """Capture environments."""

            environments = {}

            def launch(role, *_args, **kwargs):
                """Launch the configured owned process."""

                environments[role] = dict(kwargs["environment"])
                return FakeProcess()

            with mock.patch.dict(
                module.os.environ,
                {"PHASE184H_DESKTOP_CLIENT_BARRIER": r"D:\ambient\barrier.json"},
                clear=True,
            ), mock.patch.object(
                module,
                "_launch_logged_process",
                side_effect=launch,
            ), mock.patch.object(
                module,
                "_wait_for_actor_readiness",
                return_value={},
            ):
                module._start_case_workers_serially(
                    config=config,
                    repository=ROOT,
                    output=output,
                    runtime=runtime,
                    worker_roles={"foxglove-client", "ros2-peer", "graph-observer"},
                    owner=mock.Mock(),
                    streams=[],
                    desktop_barrier=desktop_barrier,
                )
            return environments

        gated = capture_environments(barrier)
        self.assertEqual(
            str(barrier),
            gated["foxglove-client"]["PHASE184H_DESKTOP_CLIENT_BARRIER"],
        )
        self.assertNotIn("PHASE184H_DESKTOP_CLIENT_BARRIER", gated["ros2-peer"])
        self.assertNotIn("PHASE184H_DESKTOP_CLIENT_BARRIER", gated["graph-observer"])

        normal = capture_environments(None)
        for role, environment in normal.items():
            with self.subTest(role=role):
                self.assertNotIn("PHASE184H_DESKTOP_CLIENT_BARRIER", environment)

    def test_case_actor_threads_the_optional_barrier_only_to_worker_startup(self):
        """Verify case actor threads the optional barrier only to worker startup."""

        module = load_module()
        output = ROOT / "build" / "phase184" / "acceptance" / "barrier-actors"
        barrier = output / "desktop-client-barrier.json"
        config = {"case": "foxglove-profile"}

        with mock.patch.object(module, "_start_case_workers_serially") as workers:
            roles, evidence = module._start_case_actors(
                config=config,
                repository=ROOT,
                output=output,
                runtime=None,
                owner=mock.Mock(),
                streams=[],
                desktop_barrier=barrier,
            )

        self.assertEqual({"foxglove-client"}, roles)
        self.assertEqual({}, evidence)
        self.assertEqual(barrier, workers.call_args.kwargs["desktop_barrier"])

    def test_desktop_barrier_is_excluded_from_owned_router_environment(self):
        """Verify desktop barrier is excluded from owned router environment."""

        module = load_module()
        output = ROOT / "build" / "phase184" / "acceptance" / "barrier-router"
        barrier = output / "desktop-client-barrier.json"
        runtime = mock.Mock(
            zenoh_router=pathlib.Path(r"D:\owned\zenohd.exe"),
            zenoh_router_environment={
                "ROS_DOMAIN_ID": "184",
                "PHASE184H_DESKTOP_CLIENT_BARRIER": r"D:\ambient\barrier.json",
            },
            zenoh_router_endpoint=mock.Mock(),
        )
        launched_environment = {}

        def launch(_role, *_args, **kwargs):
            """Launch the configured owned process."""

            launched_environment.update(kwargs["environment"])
            return FakeProcess()

        with mock.patch.object(
            module,
            "_launch_logged_process",
            side_effect=launch,
        ), mock.patch.object(
            module,
            "wait_for_owned_zenoh_router",
            return_value={"state": "ready"},
        ), mock.patch.object(
            module,
            "write_actor_ready",
        ), mock.patch.object(
            module,
            "_start_case_workers_serially",
        ):
            module._start_case_actors(
                config={
                    "case": "stream-640hz",
                    "zenohTopologyId": "phase184g-router",
                },
                repository=ROOT,
                output=output,
                runtime=runtime,
                owner=mock.Mock(),
                streams=[],
                desktop_barrier=barrier,
            )

        self.assertNotIn(
            "PHASE184H_DESKTOP_CLIENT_BARRIER",
            launched_environment,
        )

    def test_peer_graph_auditor_validates_raw_rclpy_snapshot(self):
        """Verify peer graph auditor validates raw rclpy snapshot."""

        module = load_module()
        topic = "/foxrun/phase184/multi/state"
        topic_type = "demo/msg/State"
        qos = {
            "reliability": "reliable",
            "durability": "volatile",
            "history": "keep_last",
            "depth": 10,
        }
        graphs = {
            topic: {
                "publishers": [
                    {
                        "node": "/unity_native",
                        "gid": "01",
                        "topicType": topic_type,
                        "qos": qos,
                    },
                    {
                        "node": "/unity2foxglove_ros2_bridge",
                        "gid": "02",
                        "topicType": topic_type,
                        "qos": qos,
                    },
                ],
                "subscriptions": [],
            }
        }
        config = {
            "case": "multi-target",
            "topics": [topic],
            "interfaceType": topic_type,
        }
        peer_result = {
            "evidence": {
                "graphEvidence": {
                    "source": "ros2-peer-rclpy-graph-api",
                    "topics": graphs,
                }
            }
        }
        events = []

        with mock.patch.object(
            module,
            "write_actor_ready",
            side_effect=lambda *_args, **_kwargs: events.append("ready"),
        ) as ready:
            with mock.patch.object(
                module,
                "_wait_for_unity_context",
                side_effect=lambda *_args, **_kwargs: events.append("context"),
            ) as context:
                with mock.patch.object(
                    module,
                    "_wait_for_peer_result_document",
                    side_effect=lambda *_args, **_kwargs: (
                        events.append("peer-result"),
                        peer_result,
                    )[1],
                ):
                    with mock.patch.object(module, "wait_for_terminal_marker"):
                        evidence = module._run_peer_graph_auditor(config)

        ready.assert_called_once()
        context.assert_called_once_with(config)
        self.assertEqual(["ready", "context", "peer-result"], events)
        self.assertTrue(evidence["endpointsObserved"])
        self.assertTrue(evidence["qosMatches"])
        self.assertEqual(
            ["/unity2foxglove_ros2_bridge", "/unity_native"],
            evidence["nodeIdentities"],
        )
        self.assertEqual(graphs, evidence["topics"])

    def test_peer_graph_auditor_rejects_missing_unity_context_before_peer_budget(self):
        """The finite peer-result budget cannot start before the Play barrier."""

        module = load_module()
        config = {
            "case": "multi-target",
            "topics": ["/foxrun/phase184/multi/state"],
            "interfaceType": "demo/msg/State",
        }

        with mock.patch.object(module, "write_actor_ready"):
            with mock.patch.object(
                module,
                "_wait_for_unity_context",
                side_effect=module.AcceptanceFailure(
                    "FAIL_UNITY_STARTUP",
                    "Unity context missing.",
                ),
            ):
                with mock.patch.object(
                    module,
                    "_wait_for_peer_result_document",
                ) as peer_result:
                    with self.assertRaisesRegex(
                        module.AcceptanceFailure,
                        r"FAIL_UNITY_STARTUP.*context missing",
                    ):
                        module._run_peer_graph_auditor(config)

        peer_result.assert_not_called()

    def test_run_config_is_immutable_case_specific_and_protocol_valid(self):
        """Verify run config is immutable case specific and protocol valid."""

        module = load_module()
        run_id = "phase184g-20260726-config01"
        output = ROOT / "build" / "phase184" / "acceptance" / run_id
        config = module.make_run_config(
            repository=ROOT,
            run_id=run_id,
            token="p184g_A1b2C3d4E5f6",
            case="degraded-target",
            profile="jazzy-fastrtps",
            output_root=output,
            domain_id=84,
            foxglove_port=18765,
            bridge_port=18767,
            phase181_workspace=ROOT / "build" / "phase181" / "jazzy-fastrtps" / "peer-workspace",
            interface_package="unity2foxglove_foxrun_interfaces_v1",
            interface_type="unity2foxglove_foxrun_interfaces_v1/msg/Phase181State48D288ED82F1Envelope",
            interface_digest="a" * 64,
        )
        module.protocol.validate_run_config(config, ROOT)
        self.assertEqual(1, module.protocol.RUN_CONFIG_SCHEMA_VERSION)
        self.assertEqual(
            {
                "schemaVersion",
                "executionMode",
                "runId",
                "token",
                "case",
                "profile",
                "projectPath",
                "outputRoot",
                "rosDistro",
                "rmw",
                "domainId",
                "discoveryRange",
                "zenohTopologyId",
                "phase181Workspace",
                "phase181Install",
                "bridgeOverlayInstall",
                "foxgloveHost",
                "foxglovePort",
                "bridgeHost",
                "bridgePort",
                "interfacePackage",
                "interfaceType",
                "interfaceDigest",
                "topics",
                "observationWindows",
                "readyFiles",
                "resultFiles",
                "unityLog",
            },
            set(config),
        )
        self.assertEqual("SUBNET", config["discoveryRange"])
        self.assertEqual(
            {"foxglove-client", "graph-observer", "bridge"},
            set(config["readyFiles"]),
        )
        self.assertEqual(
            str(output / "ready" / "bridge.json"),
            config["readyFiles"]["bridge"],
        )
        self.assertEqual(
            "Bridge deliberately not started",
            module.protocol.CASE_CONTRACTS["degraded-target"].deliberately_absent_actors[
                "bridge"
            ],
        )

        manual = module.make_run_config(
            repository=ROOT,
            run_id="phase184g-20260726-manual01",
            token="p184g_F6e5D4c3B2a1",
            case="multi-target",
            profile="jazzy-fastrtps",
            output_root=ROOT
            / "build"
            / "phase184"
            / "acceptance"
            / "phase184g-20260726-manual01",
            domain_id=85,
            foxglove_port=18768,
            bridge_port=18769,
            phase181_workspace=ROOT
            / "build"
            / "phase181"
            / "jazzy-fastrtps"
            / "peer-workspace",
            interface_package="unity2foxglove_foxrun_interfaces_v1",
            interface_type="unity2foxglove_foxrun_interfaces_v1/msg/Phase181State48D288ED82F1Envelope",
            interface_digest="b" * 64,
            execution_mode="manual",
        )
        module.protocol.validate_run_config(manual, ROOT)
        self.assertEqual("manual", manual["executionMode"])
        self.assertEqual(
            str(
                ROOT
                / "build"
                / "phase184"
                / "bridge-cache"
                / "jazzy-fastrtps"
                / "bridge-overlay"
                / "install"
            ),
            manual["bridgeOverlayInstall"],
        )

        another = module.make_run_config(
            repository=ROOT,
            run_id="phase184g-20260726-cache02",
            token="p184g_C3d4E5f6A1b2",
            case="qos-contract",
            profile="jazzy-fastrtps",
            output_root=ROOT
            / "build"
            / "phase184"
            / "acceptance"
            / "phase184g-20260726-cache02",
            domain_id=86,
            foxglove_port=18770,
            bridge_port=18771,
            phase181_workspace=ROOT
            / "build"
            / "phase181"
            / "jazzy-fastrtps"
            / "peer-workspace",
            interface_package="unity2foxglove_foxrun_interfaces_v1",
            interface_type="unity2foxglove_foxrun_interfaces_v1/msg/Phase181State48D288ED82F1Envelope",
            interface_digest="c" * 64,
        )
        self.assertEqual(
            manual["bridgeOverlayInstall"],
            another["bridgeOverlayInstall"],
        )

    def test_environment_prefix_order_is_bridge_peer_ros_and_ambient_is_removed(self):
        """Verify environment prefix order is bridge peer ROS and ambient is removed."""

        module = load_module()
        source = {
            "PATH": r"C:\host",
            "AMENT_PREFIX_PATH": r"C:\ambient",
            "ROS_DOMAIN_ID": "1",
            "ROS_DISCOVERY_SERVER": "ambient",
            "ZENOH_SESSION_CONFIG_URI": "ambient",
            "PHASE184H_DESKTOP_CLIENT_BARRIER": r"D:\ambient\barrier.json",
        }
        bridge = pathlib.Path(r"D:\owned\bridge-overlay\install")
        peer = pathlib.Path(r"D:\owned\peer-workspace\install")
        ros = pathlib.Path(r"D:\owned\ros2-windows\jazzy")
        environment = module.build_ros_actor_environment(
            source,
            bridge_install=bridge,
            peer_install=peer,
            ros2_root=ros,
            distro="jazzy",
            rmw="rmw_fastrtps_cpp",
            domain_id=84,
            discovery_range="SUBNET",
            topology_id="",
            zenoh_session_config=None,
        )
        self.assertEqual(
            [str(bridge), str(peer), str(ros)],
            environment["AMENT_PREFIX_PATH"].split(os.pathsep),
        )
        self.assertEqual("84", environment["ROS_DOMAIN_ID"])
        self.assertEqual("SUBNET", environment["ROS_AUTOMATIC_DISCOVERY_RANGE"])
        self.assertNotIn("ROS_DISCOVERY_SERVER", environment)
        self.assertNotIn("ZENOH_SESSION_CONFIG_URI", environment)
        self.assertNotIn("PHASE184H_DESKTOP_CLIENT_BARRIER", environment)

        zenoh_environment = module.build_ros_actor_environment(
            source,
            bridge_install=bridge,
            peer_install=peer,
            ros2_root=ros,
            distro="lyrical",
            rmw="rmw_zenoh_cpp",
            domain_id=85,
            discovery_range="LOCALHOST",
            topology_id="phase184-local",
            zenoh_session_config=ROOT / "build" / "phase184" / "zenoh.json5",
        )
        self.assertEqual(
            "LOCALHOST",
            zenoh_environment["ROS_AUTOMATIC_DISCOVERY_RANGE"],
        )

    def test_zenoh_router_uses_the_exact_unity_project_endpoint(self):
        """Verify zenoh router uses the exact unity project endpoint."""

        module = load_module()
        TEST_ROOT.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix="zenoh-endpoint-", dir=TEST_ROOT) as raw:
            repository = pathlib.Path(raw)
            settings = (
                repository
                / "Unity2Foxglove"
                / "Library"
                / "Unity2Foxglove"
                / "R2fuZenohRouterSettings.json"
            )
            settings.parent.mkdir(parents=True)
            settings.write_text(
                json.dumps(
                    {
                        "schemaVersion": 1,
                        "routerAddress": "127.0.0.1",
                        "routerPort": 8778,
                        "endpoint": "tcp/127.0.0.1:8778",
                    }
                ),
                encoding="utf-8",
            )

            endpoint = module.load_unity_zenoh_router_endpoint(repository)

            self.assertEqual("tcp/127.0.0.1:8778", endpoint.endpoint)
            self.assertEqual("127.0.0.1", endpoint.host)
            self.assertEqual(8778, endpoint.port)

            invalid = json.loads(settings.read_text(encoding="utf-8"))
            invalid["endpoint"] = "tcp/127.0.0.1:7447"
            settings.write_text(json.dumps(invalid), encoding="utf-8")
            with self.assertRaisesRegex(
                module.AcceptanceFailure,
                "FAIL_RUNTIME_SELECTION",
            ):
                module.load_unity_zenoh_router_endpoint(repository)

            invalid["endpoint"] = "tcp/192.0.2.1:8778"
            invalid["routerAddress"] = "192.0.2.1"
            settings.write_text(json.dumps(invalid), encoding="utf-8")
            with self.assertRaisesRegex(
                module.AcceptanceFailure,
                "FAIL_RUNTIME_SELECTION",
            ):
                module.load_unity_zenoh_router_endpoint(repository)

    def test_zenoh_router_readiness_requires_marker_and_listening_socket(self):
        """Verify zenoh router readiness requires marker and listening socket."""

        module = load_module()
        process = FakeProcess(pid=18403)
        endpoint = module.UnityZenohRouterEndpoint(
            endpoint="tcp/127.0.0.1:8778",
            host="127.0.0.1",
            port=8778,
        )
        connection = mock.MagicMock()

        with mock.patch.object(
            module,
            "read_log_lines",
            return_value=["Started Zenoh router with id abc"],
        ), mock.patch.object(
            module.socket,
            "create_connection",
            return_value=connection,
        ) as connect:
            evidence = module.wait_for_owned_zenoh_router(
                process,
                pathlib.Path("router.log"),
                endpoint,
                timeout_seconds=0.1,
            )

        connect.assert_called_once_with(("127.0.0.1", 8778), timeout=0.25)
        connection.close.assert_called_once_with()
        self.assertEqual(
            {
                "state": "owned-router-ready",
                "endpoint": "tcp/127.0.0.1:8778",
            },
            evidence,
        )

    def test_publisher_gid_supports_current_and_legacy_rclpy_shapes(self):
        """Verify publisher GID supports current and legacy rclpy shapes."""

        module = load_module()
        raw = bytes(range(24))
        current = {
            "publisher_gid": {
                "implementation_identifier": "rmw_zenoh_cpp",
                "data": raw,
            }
        }
        legacy_mapping = {"publisher_gid": raw}

        class LegacyObject:
            """Represent the legacy object contract."""

            publisher_gid = raw

        self.assertEqual(raw.hex(), module._publisher_gid(current))
        self.assertEqual(raw.hex(), module._publisher_gid(legacy_mapping))
        self.assertEqual(raw.hex(), module._publisher_gid(LegacyObject()))
        self.assertEqual("", module._publisher_gid({"publisher_gid": None}))
        self.assertEqual(
            "",
            module._publisher_gid(
                {
                    "publisher_gid": {
                        "implementation_identifier": "rmw_zenoh_cpp",
                        "data": b"",
                    }
                }
            ),
        )

    def test_publication_sequence_supports_the_jazzy_message_info_shape(self):
        """Verify publication sequence supports the jazzy message info shape."""

        module = load_module()

        self.assertEqual(
            17,
            module._publication_sequence_number(
                {"publication_sequence_number": 17}
            ),
        )
        self.assertEqual(
            23,
            module._publication_sequence_number(
                type(
                    "MessageInfo",
                    (),
                    {"publication_sequence_number": 23},
                )()
            ),
        )
        self.assertIsNone(
            module._publication_sequence_number(
                {"publication_sequence_number": None}
            )
        )
        self.assertIsNone(
            module._publication_sequence_number(
                {"publication_sequence_number": True}
            )
        )

    def test_sample_attribution_uses_duplicate_sequences_and_exact_graph_gids(self):
        """Verify sample attribution uses duplicate sequences and exact graph gids."""

        module = load_module()
        publishers = [
            {"node": "/unity_native", "gid": "native-gid"},
            {
                "node": "/unity2foxglove_ros2_bridge",
                "gid": "bridge-gid",
            },
        ]

        gids, source = module._attribute_sample_publishers(
            direct_gids=[],
            publication_sequences=[41, 41, 42, 42],
            graph_publishers=publishers,
            minimum_publishers=2,
        )

        self.assertEqual(["bridge-gid", "native-gid"], gids)
        self.assertEqual(
            "publication-sequence-plus-graph-gid",
            source,
        )
        self.assertEqual(
            ([], ""),
            module._attribute_sample_publishers(
                direct_gids=[],
                publication_sequences=[41, 42, 43],
                graph_publishers=publishers,
                minimum_publishers=2,
            ),
        )
        self.assertEqual(
            ([], ""),
            module._attribute_sample_publishers(
                direct_gids=[],
                publication_sequences=[41, 41],
                graph_publishers=publishers + [
                    {"node": "/unexpected", "gid": "third-gid"}
                ],
                minimum_publishers=2,
            ),
        )

    def test_multi_graph_allows_only_unrepresented_history_and_depth(self):
        """Verify multi graph allows only unrepresented history and depth."""

        module = load_module()
        topic = "/foxrun/phase184/multi/state"
        topic_type = "demo/msg/State"
        publishers = [
            {
                "node": "/unity_native",
                "gid": "native-gid",
                "topicType": topic_type,
                "qos": {
                    "reliability": "reliable",
                    "durability": "volatile",
                    "history": "unknown",
                    "depth": 0,
                },
            },
            {
                "node": "/unity2foxglove_ros2_bridge",
                "gid": "bridge-gid",
                "topicType": topic_type,
                "qos": {
                    "reliability": "reliable",
                    "durability": "volatile",
                    "history": "unknown",
                    "depth": 0,
                },
            },
        ]
        config = {
            "case": "multi-target",
            "interfaceType": topic_type,
            "topics": [topic],
        }
        graphs = {
            topic: {
                "publishers": publishers,
                "subscriptions": [],
            }
        }

        self.assertTrue(module._graph_ready(config, graphs))
        contradicted = copy.deepcopy(graphs)
        contradicted[topic]["publishers"][0]["qos"][
            "reliability"
        ] = "best_effort"
        self.assertFalse(module._graph_ready(config, contradicted))

    def test_qos_graph_accepts_matching_system_default_resolution_only(self):
        """Verify QoS graph accepts matching system default resolution only."""

        module = load_module()
        topic_type = "demo/msg/State"
        topics = list(module.protocol.CASE_CONTRACTS["qos-contract"].topics)
        config = {
            "case": "qos-contract",
            "interfaceType": topic_type,
            "topics": topics,
        }
        actual_qos = (
            {
                "reliability": "reliable",
                "durability": "transient_local",
                "history": "unknown",
                "depth": 0,
                "representedAxes": ["reliability", "durability"],
            },
            {
                "reliability": "reliable",
                "durability": "volatile",
                "history": "unknown",
                "depth": 0,
                "representedAxes": ["reliability", "durability"],
            },
            {
                "reliability": "best_effort",
                "durability": "transient_local",
                "history": "unknown",
                "depth": 0,
                "representedAxes": ["reliability", "durability"],
            },
        )
        graphs = {}
        for topic, qos in zip(topics, actual_qos):
            graphs[topic] = {
                "publishers": [
                    {
                        "node": "/unity_native",
                        "gid": f"native-{topic}",
                        "topicType": topic_type,
                        "qos": copy.deepcopy(qos),
                    },
                    {
                        "node": "/unity2foxglove_ros2_bridge",
                        "gid": f"bridge-{topic}",
                        "topicType": topic_type,
                        "qos": copy.deepcopy(qos),
                    },
                ],
                "subscriptions": [],
            }

        self.assertTrue(module._graph_ready(config, graphs))

        divergent = copy.deepcopy(graphs)
        divergent[topics[0]]["publishers"][1]["qos"]["durability"] = "volatile"
        self.assertFalse(module._graph_ready(config, divergent))

    def test_stream_peer_waits_for_transport_graph_before_production(self):
        """Verify stream peer waits for transport graph before production."""

        module = load_module()
        source = inspect.getsource(module._run_stream_peer)

        self.assertLess(
            source.index("_wait_for_stream_subscription"),
            source.index("offered = 1280"),
        )

    def test_stream_production_window_reports_observed_elapsed(self):
        """A timing failure must retain its measured value for diagnosis."""

        module = load_module()

        self.assertEqual(
            2.0,
            module._validated_stream_production_elapsed(2.0),
        )
        with self.assertRaisesRegex(
            module.AcceptanceFailure,
            r"observed=4\.250000s",
        ):
            module._validated_stream_production_elapsed(4.25)

    def test_prepared_stream_publisher_stamps_and_paces_without_executor_spin(self):
        """The timed producer owns only stamping, publishing, and pacing."""

        module = load_module()

        class FakeTimeline:
            """Deterministic monotonic clock used by the paced publisher test."""

            def __init__(self):
                """Start the synthetic clock at zero seconds."""
                self.now = 0.0

            def perf_counter(self):
                """Return the current synthetic monotonic time."""
                return self.now

            def sleep(self, seconds):
                """Advance synthetic time without blocking the test process."""
                self.now += seconds

        class FakeStamp:
            """Minimal ROS clock stamp wrapper."""

            def __init__(self, value):
                """Capture one deterministic clock value."""
                self.value = value

            def to_msg(self):
                """Return the captured value as the fake wire stamp."""
                return self.value

        class FakeClock:
            """Deterministic ROS clock facade."""

            def __init__(self):
                """Start before the first generated stamp."""
                self.value = 0

            def now(self):
                """Return the next deterministic stamp."""
                self.value += 1
                return FakeStamp(self.value)

        class FakeNode:
            """Minimal node facade exposing the deterministic clock."""

            def __init__(self):
                """Own one fake ROS clock."""
                self.clock = FakeClock()

            def get_clock(self):
                """Return the node-owned fake ROS clock."""
                return self.clock

        class FakeMessage:
            """Mutable message surface populated by the prepared publisher."""

            foxrun_stamp = None

        timeline = FakeTimeline()
        messages = [FakeMessage() for _ in range(4)]
        published = []
        elapsed = module._publish_prepared_stream_samples(
            messages,
            publisher=mock.Mock(publish=published.append),
            node=FakeNode(),
            nominal_hz=2.0,
            perf_counter=timeline.perf_counter,
            sleep=timeline.sleep,
        )

        self.assertEqual(2.0, elapsed)
        self.assertEqual([1, 2, 3, 4], [message.foxrun_stamp for message in messages])
        self.assertEqual(messages, published)
        source = inspect.getsource(module._run_stream_peer)
        self.assertLess(
            source.index("stream_samples = ["),
            source.index("_publish_prepared_stream_samples("),
        )

    def test_stream_production_gate_requires_exact_external_subscription(self):
        """Verify stream production gate requires exact external subscription."""

        module = load_module()
        expected_type = "example_interfaces/msg/Envelope"
        config = {
            "case": "stream-640hz",
            "interfaceType": expected_type,
            "topics": ["/stream", "/origin"],
        }

        helper = {
            "node": "/phase184g_peer_deadbeef",
            "gid": "01",
            "topicType": expected_type,
            "qos": {},
        }
        external = {
            "node": "/unity2foxglove_foxrun_input",
            "gid": "",
            "topicType": expected_type,
            "qos": {},
        }
        wrong_type = dict(external, topicType="std_msgs/msg/String")

        self.assertFalse(
            module._stream_subscription_ready(
                config,
                {"/stream": {"publishers": [], "subscriptions": [helper]}},
            )
        )
        self.assertFalse(
            module._stream_subscription_ready(
                config,
                {"/stream": {"publishers": [], "subscriptions": [wrong_type]}},
            )
        )
        self.assertTrue(
            module._stream_subscription_ready(
                config,
                {"/stream": {"publishers": [], "subscriptions": [external]}},
            )
        )

    def test_graph_timeout_snapshot_is_persisted_below_owned_run_root(self):
        """Verify graph timeout snapshot is persisted below owned run root."""

        module = load_module()
        with tempfile.TemporaryDirectory(dir=TEST_ROOT) as temp:
            output = pathlib.Path(temp)
            config = {
                "case": "stream-640hz",
                "outputRoot": str(output),
            }
            graphs = {
                "/stream": {
                    "publishers": [],
                    "subscriptions": [
                        {
                            "node": "/unity2foxglove_foxrun_input",
                            "gid": "01",
                            "topicType": "example_interfaces/msg/Envelope",
                            "qos": {
                                "reliability": "best_effort",
                                "durability": "volatile",
                                "history": "keep_last",
                                "depth": 5,
                            },
                        }
                    ],
                }
            }

            path = module._write_graph_timeout_snapshot(config, graphs)

            self.assertEqual(
                output / "diagnostics" / "ros2-peer-graph-timeout.json",
                path,
            )
            payload = json.loads(path.read_text(encoding="utf-8"))
            self.assertEqual("stream-640hz", payload["case"])
            self.assertEqual(graphs, payload["topics"])

    def test_windows_job_assignment_is_required_and_close_is_idempotent(self):
        """Verify windows job assignment is required and close is idempotent."""

        module = load_module()
        api = FakeJobApi()
        job = module.WindowsKillOnCloseJob(api=api, platform_name="nt")
        process = FakeProcess(pid=18401)
        job.assign(process)
        job.close()
        job.close()

        self.assertEqual(1, api.created)
        self.assertEqual([(731, 18401)], api.assigned)
        self.assertEqual([731], api.closed)

        failed = module.WindowsKillOnCloseJob(
            api=FakeJobApi(assign_ok=False),
            platform_name="nt",
        )
        with self.assertRaisesRegex(module.AcceptanceFailure, "FAIL_PREFLIGHT"):
            failed.assign(process)
        failed.close()

    def test_process_owner_terminates_only_registered_children(self):
        """Verify process owner terminates only registered children."""

        module = load_module()
        first = FakeProcess(1)
        second = FakeProcess(2)
        owner = module.OwnedProcessSet(job=None)
        owner.register("foxglove-client", first)
        owner.register("ros2-peer", second)
        owner.close()

        self.assertEqual(1, first.terminated)
        self.assertEqual(1, second.terminated)
        self.assertEqual(
            {"foxglove-client": 0, "ros2-peer": 0},
            owner.exit_codes(),
        )
        self.assertTrue(owner.all_stopped())

    def test_process_owner_can_stop_one_preflight_child_without_closing_others(self):
        """Verify process owner can stop one preflight child without closing others."""

        module = load_module()
        preflight = FakeProcess(1)
        actual = FakeProcess(2)
        owner = module.OwnedProcessSet(job=None)
        owner.register("bridge-health", preflight)
        owner.register("bridge", actual)

        self.assertEqual(0, owner.stop("bridge-health"))
        self.assertEqual(1, preflight.terminated)
        self.assertIsNone(actual.poll())
        self.assertEqual({"bridge-health": 0}, owner.exit_codes())
        self.assertEqual({"bridge-health"}, owner.owner_stopped_roles())

        owner.close()
        self.assertEqual(1, actual.terminated)
        self.assertTrue(owner.all_stopped())

    def test_failed_job_registration_terminates_the_untracked_child(self):
        """Job assignment failure cannot leave the just-spawned process orphaned."""

        module = load_module()
        process = FakeProcess(18402)
        job = mock.Mock()
        job.assign.side_effect = module.AcceptanceFailure(
            "FAIL_PREFLIGHT",
            "job assignment failed",
        )
        owner = module.OwnedProcessSet(job=job)

        with self.assertRaisesRegex(module.AcceptanceFailure, "FAIL_PREFLIGHT"):
            owner.register("bridge", process)

        self.assertEqual(1, process.terminated)
        self.assertTrue(owner.all_stopped())

    def test_preflight_assignment_failure_terminates_the_spawned_process(self):
        """Preparatory children are reclaimed before owner registration exists."""

        module = load_module()
        TEST_ROOT.mkdir(parents=True, exist_ok=True)
        process = FakeProcess(18403)
        job = mock.Mock()
        job.assign.side_effect = module.AcceptanceFailure(
            "FAIL_PREFLIGHT",
            "job assignment failed",
        )
        with tempfile.TemporaryDirectory(prefix="assign-fail-", dir=TEST_ROOT) as raw:
            log = pathlib.Path(raw) / "preflight.log"
            with mock.patch.object(module.subprocess, "Popen", return_value=process):
                with self.assertRaisesRegex(module.AcceptanceFailure, "FAIL_PREFLIGHT"):
                    module._run_logged_preflight(
                        ["owned-tool"],
                        cwd=ROOT,
                        environment={},
                        log_path=log,
                        job=job,
                        failure_code="FAIL_PREFLIGHT",
                        operation="preflight",
                    )

        self.assertEqual(1, process.terminated)

    def test_owner_requested_daemon_exit_preserves_raw_windows_semantics(self):
        """Only owned Bridge/router control-break exits are accepted as clean stops."""

        module = load_module()
        for code in (-1073741510, 3221225786):
            with self.subTest(code=code):
                self.assertTrue(
                    module.process_exit_is_acceptable(
                        "bridge",
                        code,
                        owner_requested=True,
                    )
                )
                self.assertTrue(
                    module.process_exit_is_acceptable(
                        "zenoh-router",
                        code,
                        owner_requested=True,
                    )
                )
                self.assertFalse(
                    module.process_exit_is_acceptable(
                        "ros2-peer",
                        code,
                        owner_requested=True,
                    )
                )
                self.assertFalse(
                    module.process_exit_is_acceptable(
                        "bridge",
                        code,
                        owner_requested=False,
                    )
                )

    def test_explicit_loopback_port_must_be_bindable(self):
        """Verify explicit loopback port must be bindable."""

        module = load_module()
        with module.socket.socket(module.socket.AF_INET, module.socket.SOCK_STREAM) as held:
            exclusive = getattr(module.socket, "SO_EXCLUSIVEADDRUSE", None)
            if exclusive is not None:
                held.setsockopt(module.socket.SOL_SOCKET, exclusive, 1)
            held.bind(("127.0.0.1", 0))
            port = int(held.getsockname()[1])
            with self.assertRaisesRegex(module.AcceptanceFailure, "FAIL_PREFLIGHT"):
                module.require_available_loopback_port(port, "Foxglove")

        module.require_available_loopback_port(port, "Foxglove")

    def test_progress_snapshot_observes_secondary_unity_log(self):
        """Verify progress snapshot observes secondary unity log."""

        module = load_module()
        TEST_ROOT.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix="progress-", dir=TEST_ROOT) as raw:
            root = pathlib.Path(raw)
            process_log = root / "process.log"
            unity_log = root / "unity.log"
            process_log.write_text("", encoding="utf-8")
            unity_log.write_text("first\n", encoding="utf-8")
            before = module._progress_snapshot((process_log, unity_log))
            unity_log.write_text("first\nsecond\n", encoding="utf-8")
            after = module._progress_snapshot((process_log, unity_log))
            self.assertNotEqual(before, after)

    def test_unity_wait_uses_progress_watchdog_instead_of_total_duration(self):
        """A progressing cold import is bounded by silence, not total duration."""

        module = load_module()
        unity = mock.Mock()
        unity.poll.side_effect = (None, 0)
        unity.returncode = 0
        terminal = module.TerminalMarker("PASS", "pass", {})
        watchdog = mock.Mock()
        config = {
            "case": "multi-target",
            "token": "p184g_A1b2C3d4E5f6",
            "unityLog": str(TEST_ROOT / "unity-progress.log"),
        }
        snapshots = (
            (("unity-progress.log", 10, 1),),
            (("unity-progress.log", 20, 2),),
        )
        with mock.patch.object(
            module.protocol,
            "ProgressWatchdog",
            return_value=watchdog,
        ) as create_watchdog:
            with mock.patch.object(module, "_progress_snapshot", side_effect=snapshots):
                with mock.patch.object(module, "read_log_lines", return_value=[]):
                    with mock.patch.object(
                        module,
                        "wait_for_terminal_marker",
                        return_value=terminal,
                    ):
                        with mock.patch.object(module.time, "sleep"):
                            observed = module._wait_for_unity_exit(
                                config,
                                unity,
                                owner=mock.Mock(),
                                worker_roles=(),
                            )

        self.assertIs(terminal, observed)
        create_watchdog.assert_called_once_with("unity-startup")
        self.assertGreaterEqual(watchdog.progress.call_count, 2)
        watchdog.check.assert_called()

    def test_manual_editor_log_mirror_survives_truncation_and_correlates_token(self):
        """Verify manual editor log mirror survives truncation and correlates token."""

        module = load_module()
        TEST_ROOT.mkdir(parents=True, exist_ok=True)
        token = "p184g_A1b2C3d4E5f6"
        with tempfile.TemporaryDirectory(prefix="mirror-", dir=TEST_ROOT) as raw:
            root = pathlib.Path(raw)
            editor_log = root / "Editor.log"
            owned_log = root / "unity-editor.log"
            editor_log.write_text("stale history\n", encoding="utf-8")
            mirror = module.EditorLogMirror(editor_log, owned_log, token)
            mirror.capture()

            with editor_log.open("a", encoding="utf-8") as stream:
                stream.write(
                    f"PHASE184G_CONTEXT_READY case=multi-target token={token}\n"
                )
            mirror.poll()
            self.assertIn(token, owned_log.read_text(encoding="utf-8"))

            editor_log.write_text(
                f"PHASE184G_MANUAL_PLAY_EXITED case=multi-target token={token}\n",
                encoding="utf-8",
            )
            mirror.poll()
            copied = owned_log.read_text(encoding="utf-8")
            self.assertIn("PHASE184G_CONTEXT_READY", copied)
            self.assertIn("PHASE184G_MANUAL_PLAY_EXITED", copied)

    def test_manual_editor_log_mirror_ignores_oversized_stale_history(self):
        """Historical log size must not poison fresh token-correlated appends."""

        module = load_module()
        TEST_ROOT.mkdir(parents=True, exist_ok=True)
        token = "p184g_A1b2C3d4E5f6"
        with tempfile.TemporaryDirectory(prefix="mirror-large-", dir=TEST_ROOT) as raw:
            root = pathlib.Path(raw)
            editor_log = root / "Editor.log"
            owned_log = root / "unity-editor.log"
            editor_log.write_text("stale history exceeds cap\n", encoding="utf-8")
            mirror = module.EditorLogMirror(editor_log, owned_log, token)
            mirror._MAX_SOURCE_BYTES = 8
            mirror.capture()

            with editor_log.open("a", encoding="utf-8") as stream:
                stream.write(
                    f"PHASE184G_CONTEXT_READY case=multi-target token={token}\n"
                )

            mirror.poll()

            copied = owned_log.read_text(encoding="utf-8")
            self.assertIn("PHASE184G_CONTEXT_READY", copied)
            self.assertNotIn("stale history", copied)

    def test_manual_editor_log_mirror_retries_transient_windows_open_contention(self):
        """Unity log rotation may briefly deny the stat-to-open transition."""

        module = load_module()
        TEST_ROOT.mkdir(parents=True, exist_ok=True)
        token = "p184g_A1b2C3d4E5f6"
        with tempfile.TemporaryDirectory(prefix="mirror-contention-", dir=TEST_ROOT) as raw:
            root = pathlib.Path(raw)
            editor_log = root / "Editor.log"
            owned_log = root / "unity-editor.log"
            editor_log.write_text("existing\n", encoding="utf-8")
            mirror = module.EditorLogMirror(editor_log, owned_log, token)
            mirror.capture()
            with editor_log.open("a", encoding="utf-8") as stream:
                stream.write(
                    f"PHASE184G_CONTEXT_READY case=multi-target token={token}\n"
                )

            original_open = pathlib.Path.open
            source_attempts = 0

            def open_with_one_contention(path, *args, **kwargs):
                """Fail only the first binary source read."""

                nonlocal source_attempts
                if path == editor_log and args and args[0] == "rb":
                    source_attempts += 1
                    if source_attempts == 1:
                        raise PermissionError("simulated Windows sharing violation")
                return original_open(path, *args, **kwargs)

            with mock.patch.object(
                pathlib.Path,
                "open",
                autospec=True,
                side_effect=open_with_one_contention,
            ):
                mirror.poll()
                mirror.poll()

            self.assertEqual(2, source_attempts)
            self.assertIn(token, owned_log.read_text(encoding="utf-8"))

    def test_manual_editor_log_mirror_fails_after_bounded_open_contention(self):
        """Persistent inability to read Editor.log must remain fail closed."""

        module = load_module()
        TEST_ROOT.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix="mirror-contention-bound-", dir=TEST_ROOT) as raw:
            root = pathlib.Path(raw)
            editor_log = root / "Editor.log"
            owned_log = root / "unity-editor.log"
            editor_log.write_text("existing\n", encoding="utf-8")
            mirror = module.EditorLogMirror(
                editor_log,
                owned_log,
                "p184g_A1b2C3d4E5f6",
            )
            mirror._ACCESS_FAILURE_GRACE_SECONDS = 0.0
            mirror.capture()

            original_open = pathlib.Path.open

            def deny_source_open(path, *args, **kwargs):
                """Deny only binary reads of the interactive Editor log."""

                if path == editor_log and args and args[0] == "rb":
                    raise PermissionError("persistent Windows sharing violation")
                return original_open(path, *args, **kwargs)

            with mock.patch.object(
                pathlib.Path,
                "open",
                autospec=True,
                side_effect=deny_source_open,
            ):
                mirror.poll()
                with self.assertRaisesRegex(
                    module.AcceptanceFailure,
                    r"FAIL_TERMINAL.*bounded retry window",
                ):
                    mirror.poll()

    def test_manual_session_does_not_latch_pass_over_a_later_failure(self):
        """The latest correlated terminal marker remains authoritative until exit."""

        module = load_module()
        token = "p184g_A1b2C3d4E5f6"
        config = {
            "case": "multi-target",
            "token": token,
            "unityLog": str(TEST_ROOT / "manual-latch.log"),
        }
        context = f"PHASE184G_CONTEXT_READY case=multi-target token={token}"
        passed = f"PHASE184G_CASE_PASS case=multi-target token={token}"
        failed = f"PHASE184G_CASE_FAIL case=multi-target token={token}"

        with mock.patch.object(
            module,
            "read_log_lines",
            side_effect=([context, passed], [context, passed, failed]),
        ):
            with mock.patch.object(
                module,
                "_manual_exit_seen",
                side_effect=(False, True),
            ):
                with mock.patch.object(module.time, "sleep"):
                    with self.assertRaisesRegex(
                        module.AcceptanceFailure,
                        "FAIL_TERMINAL",
                    ):
                        module._wait_for_manual_session(
                            config,
                            mirror=mock.Mock(),
                            owner=mock.Mock(),
                            worker_roles=(),
                        )

    def test_manual_session_fails_immediately_from_finished_worker_result(self):
        """A persisted worker failure must not consume the manual review timeout."""

        module = load_module()
        TEST_ROOT.mkdir(parents=True, exist_ok=True)
        token = "p184g_A1b2C3d4E5f6"
        with tempfile.TemporaryDirectory(prefix="manual-worker-", dir=TEST_ROOT) as raw:
            root = pathlib.Path(raw)
            result = root / "foxglove-client.json"
            config = {
                "runId": "phase184g-20260728-worker-fail01",
                "case": "multi-target",
                "token": token,
                "unityLog": str(root / "unity-editor.log"),
                "resultFiles": {"foxglove-client": str(result)},
            }
            module.write_actor_result(
                config,
                "foxglove-client",
                verdict="FAIL_CLIENT",
                evidence={"diagnostic": "incomplete delivery"},
            )
            process = FakeProcess()
            process.returncode = 1
            owner = mock.Mock()
            owner.process.return_value = process
            context = f"PHASE184G_CONTEXT_READY case=multi-target token={token}"

            with mock.patch.object(module, "read_log_lines", return_value=[context]):
                with mock.patch.object(module, "_manual_exit_seen", return_value=False):
                    with mock.patch.object(
                        module,
                        "MANUAL_REVIEW_TIMEOUT_SECONDS",
                        0.0,
                    ):
                        with mock.patch.object(module.time, "monotonic", return_value=0.0):
                            with self.assertRaisesRegex(
                                module.AcceptanceFailure,
                                r"FAIL_CLIENT.*incomplete delivery",
                            ):
                                module._wait_for_manual_session(
                                    config,
                                    mirror=mock.Mock(),
                                    owner=owner,
                                    worker_roles=("foxglove-client",),
                                )

    def test_manual_editor_log_rescue_scan_is_rate_bounded(self):
        """Verify manual editor log rescue scan is rate bounded."""

        module = load_module()
        TEST_ROOT.mkdir(parents=True, exist_ok=True)
        token = "p184g_A1b2C3d4E5f6"
        with tempfile.TemporaryDirectory(prefix="mirror-rate-", dir=TEST_ROOT) as raw:
            root = pathlib.Path(raw)
            editor_log = root / "Editor.log"
            owned_log = root / "unity-editor.log"
            editor_log.write_text("existing\n", encoding="utf-8")
            mirror = module.EditorLogMirror(editor_log, owned_log, token)
            mirror.capture()

            original_read_text = pathlib.Path.read_text
            editor_reads = 0

            def read_text_spy(path, *args, **kwargs):
                """Read text spy."""

                nonlocal editor_reads
                if path == editor_log:
                    editor_reads += 1
                return original_read_text(path, *args, **kwargs)

            with mock.patch.object(
                pathlib.Path,
                "read_text",
                autospec=True,
                side_effect=read_text_spy,
            ):
                with mock.patch.object(
                    module.time,
                    "monotonic",
                    side_effect=(10.0, 10.1, 11.1),
                ):
                    mirror.poll()
                    mirror.poll()
                    mirror.poll()

            self.assertEqual(2, editor_reads)

    def test_manual_pointer_is_removed_only_by_its_owner(self):
        """Verify manual pointer is removed only by its owner."""

        module = load_module()
        TEST_ROOT.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix="pointer-", dir=TEST_ROOT) as raw:
            pointer = pathlib.Path(raw) / "manual-active.json"
            module._write_manual_pointer(
                pointer,
                pathlib.Path(raw) / "run-config.json",
                "p184g_A1b2C3d4E5f6",
                helper_pid=18401,
                helper_created=184.0,
                expires_utc=module.dt.datetime(
                    2026,
                    7,
                    26,
                    12,
                    0,
                    tzinfo=module.dt.timezone.utc,
                ),
            )
            value = json.loads(pointer.read_text(encoding="utf-8"))
            self.assertEqual(18401, value["helperPid"])
            self.assertFalse(
                module._remove_manual_pointer_if_owned(
                    pointer,
                    "p184g_other000000",
                    18401,
                )
            )
            self.assertTrue(pointer.is_file())
            self.assertTrue(
                module._remove_manual_pointer_if_owned(
                    pointer,
                    "p184g_A1b2C3d4E5f6",
                    18401,
                )
            )
            self.assertFalse(pointer.exists())

    def test_short_workspace_alias_uses_path_identity_not_resolved_target(self):
        """Verify short workspace alias uses path identity not resolved target."""

        module = load_module()
        physical = ROOT / "build" / "phase184" / "long-workspace"
        alias = pathlib.Path(r"X:\phase184")
        self.assertTrue(module._paths_are_distinct(alias, physical))
        self.assertFalse(module._paths_are_distinct(physical, physical))

    def test_windows_bridge_runtime_encloses_ros_initialization_and_shutdown(self):
        """Verify windows bridge runtime encloses ROS initialization and shutdown."""

        source = (
            ROOT
            / "Tools"
            / "ros2_bridge"
            / "unity2foxglove_ros2_bridge"
            / "src"
            / "unity2foxglove_ros2_bridge.cpp"
        ).read_text(encoding="utf-8")
        main_source = source[source.index("int main(int argc, char ** argv)") :]
        winsock = main_source.index("std::unique_ptr<WinsockRuntime> winsock;")
        winsock_start = main_source.index(
            "winsock = std::make_unique<WinsockRuntime>();"
        )
        ros_init = main_source.index("rclcpp::init_and_remove_ros_arguments")
        final_shutdown = main_source.rindex("rclcpp::shutdown();")

        self.assertLess(winsock, winsock_start)
        self.assertLess(winsock_start, ros_init)
        self.assertLess(ros_init, final_shutdown)
        self.assertNotIn("winsock.reset(", main_source)
        self.assertEqual(
            1,
            main_source.count("std::unique_ptr<WinsockRuntime> winsock;"),
        )

    def test_windows_bridge_only_times_out_partial_frame_reads(self):
        """Verify windows bridge only times out partial frame reads."""

        source = (
            ROOT
            / "Tools"
            / "ros2_bridge"
            / "unity2foxglove_ros2_bridge"
            / "src"
            / "unity2foxglove_ros2_bridge.cpp"
        ).read_text(encoding="utf-8")
        read_exact = source[
            source.index("bool read_exact(") : source.index(
                "void write_all(", source.index("bool read_exact(")
            )
        ]
        retryable_timeout = read_exact[
            read_exact.index("if (socket_error_is_retryable_timeout(error))") :
        ]

        idle_guard = retryable_timeout.index("if (offset == 0)")
        stall_clock = retryable_timeout.index(
            "if (stalled_since == std::chrono::steady_clock::time_point {})"
        )
        self.assertLess(idle_guard, stall_clock)
        self.assertIn("rclcpp::spin_some(node);", retryable_timeout[:stall_clock])
        self.assertIn("continue;", retryable_timeout[:stall_clock])

    def test_bridge_health_readiness_does_not_create_a_ros_participant(self):
        """Verify bridge health readiness does not create a ROS participant."""

        source = (
            ROOT
            / "Tools"
            / "ros2_bridge"
            / "unity2foxglove_ros2_bridge"
            / "src"
            / "unity2foxglove_ros2_bridge.cpp"
        ).read_text(encoding="utf-8")
        dispatch = source[
            source.index("void dispatch_deferred_frame(") : source.index(
                "void process_deferred_client(",
                source.index("void dispatch_deferred_frame("),
            )
        ]
        main = source[source.index("int main(int argc, char ** argv)") :]

        health = dispatch.index('if (op == "health_ping")')
        ros_bridge = dispatch.index("session.require_bridge()")
        self.assertLess(health, ros_bridge)
        self.assertIn("DeferredBridgeSession session(", main)
        self.assertLess(
            main.index("create_listen_socket("),
            main.index("DeferredBridgeSession session("),
        )
        self.assertNotIn(
            'std::make_shared<rclcpp::Node>("unity2foxglove_ros2_bridge");',
            main[: main.index("create_listen_socket(")],
        )

    def test_bridge_actor_health_checks_the_same_owned_process_before_unity(self):
        """Verify bridge actor health checks the same owned process before unity."""

        module = load_module()
        process = FakeProcess(18404)
        owner = mock.Mock()
        owner.process.return_value = None
        runtime = mock.Mock()
        runtime.bridge_install = ROOT / "build" / "bridge-overlay"
        runtime.bridge_runtime_workspace = ROOT
        runtime.actor_environment = {
            "PHASE184H_DESKTOP_CLIENT_BARRIER": r"D:\ambient\barrier.json"
        }
        config = {
            "token": "p184g_A1b2C3d4E5f6",
            "bridgeHost": "127.0.0.1",
            "bridgePort": 18767,
        }
        health = {
            "op": "health_pong",
            "requestId": config["token"],
            "protocolVersion": 1,
            "status": "ok",
            "sidecarName": "unity2foxglove_ros2_bridge",
            "sidecarVersion": "0.1.0",
        }
        events = []
        launched_environment = {}

        def launch(*args, **kwargs):
            """Launch the configured owned process."""

            del args
            launched_environment.update(kwargs["environment"])
            events.append("launch")
            return process

        def wait(*args, **kwargs):
            """Wait for the configured owned process."""

            del args, kwargs
            events.append("health")
            return health

        with mock.patch.object(
            module,
            "_installed_bridge_executable",
            return_value=ROOT / "bridge.exe",
        ), mock.patch.object(
            module,
            "_launch_logged_process",
            side_effect=launch,
        ), mock.patch.object(
            module,
            "wait_for_bridge_health",
            side_effect=wait,
        ), mock.patch.object(
            module,
            "write_actor_ready",
        ):
            evidence = module._start_bridge_actor(
                config=config,
                output=TEST_ROOT,
                runtime=runtime,
                owner=owner,
                streams=[],
            )

        self.assertEqual(["launch", "health"], events)
        self.assertEqual(health, evidence["bridge-health"])
        self.assertNotIn(
            "PHASE184H_DESKTOP_CLIENT_BARRIER",
            launched_environment,
        )
        owner.stop.assert_not_called()

    def test_bridge_cases_health_check_actual_sidecar_before_workers_and_unity(self):
        """Verify bridge cases health check actual sidecar before workers and unity."""

        source = (
            ROOT / "Scripts" / "smoke" / "foxrun" / "phase184_profile_acceptance.py"
        ).read_text(encoding="utf-8")

        actors = source[
            source.index("def _start_case_actors(") : source.index(
                "def _write_parent_actor_results(",
                source.index("def _start_case_actors("),
            )
        ]
        batch = source[
            source.index("def run_batch_parent(") : source.index(
                "def main(",
                source.index("def run_batch_parent("),
            )
        ]
        bridge_launch = actors.index("_start_bridge_actor(")
        worker_launch = actors.index("_start_case_workers_serially(")
        self.assertLess(bridge_launch, worker_launch)
        self.assertNotIn("_preflight_bridge_health(", actors)
        self.assertNotIn("defer_bridge", actors)

        unity_launch = batch.index("unity = _launch_logged_process(")
        self.assertNotIn("_wait_for_deferred_bridge_gate(", batch)
        self.assertNotIn("_start_bridge_actor(", batch[unity_launch:])

        actual_bridge = source[
            source.index("def _start_bridge_actor(") : source.index(
                "def _start_case_actors(",
                source.index("def _start_bridge_actor("),
            )
        ]
        self.assertIn("wait_for_bridge_health(", actual_bridge)
        self.assertNotIn("wait_for_bridge_listening(", actual_bridge)

        manual = source[
            source.index("def run_manual_parent(") : source.index(
                "def run_batch_parent(",
                source.index("def run_manual_parent("),
            )
        ]
        self.assertNotIn("deferred_bridge_start", manual)

    def test_batch_parent_computes_only_the_fixed_barrier_and_excludes_unity(self):
        """Verify batch parent computes only the fixed barrier and excludes unity."""

        module = load_module()
        output = (
            ROOT
            / "build"
            / "phase184"
            / "acceptance"
            / "phase184g-20260727-desktop01"
        ).resolve()
        fixed_barrier = output / "desktop-client-barrier.json"
        config = {
            "case": "foxglove-profile",
            "profile": "core-foxglove",
            "rosDistro": "jazzy",
            "outputRoot": str(output),
            "unityLog": str(output / "unity-editor.log"),
        }
        prepared = module.PreparedParentRun(
            repository=ROOT,
            editor=pathlib.Path(r"C:\Unity.exe"),
            run_id="phase184g-20260727-desktop01",
            token="p184g_A1b2C3d4E5f6",
            output=output,
            config=config,
            config_path=output / "run-config.json",
        )
        args = module.argparse.Namespace(
            case="foxglove-profile",
            wait_for_desktop_client=True,
        )
        owner = mock.Mock()
        owner.exit_codes.return_value = {}
        owner.owner_stopped_roles.return_value = frozenset()
        actor_start = mock.Mock(return_value=({"foxglove-client"}, {}))
        unity_environment = {}
        runtime = mock.Mock(
            unity_environment={
                "PHASE184H_DESKTOP_CLIENT_BARRIER": r"D:\ambient\barrier.json"
            },
            subst_roots=(),
        )

        def stop_at_unity(role, *_args, **kwargs):
            """Handle the stop at unity step."""

            self.assertEqual("unity", role)
            unity_environment.update(kwargs["environment"])
            raise module.AcceptanceFailure("FAIL_PREFLIGHT", "test stop")

        with mock.patch.object(
            module,
            "_prepare_parent_run",
            return_value=prepared,
        ), mock.patch.object(
            module.desktop_live_protocol,
            "resolve_desktop_client_barrier_path",
            return_value=fixed_barrier,
        ) as resolve_barrier, mock.patch.object(
            module,
            "WindowsKillOnCloseJob",
            return_value=mock.Mock(),
        ), mock.patch.object(
            module,
            "OwnedProcessSet",
            return_value=owner,
        ), mock.patch.object(
            module,
            "_ensure_acceptance_scene",
        ), mock.patch.object(
            module,
            "_prepare_ros_runtime",
            return_value=runtime,
        ), mock.patch.object(
            module,
            "_start_case_actors",
            actor_start,
        ), mock.patch.object(
            module,
            "_launch_logged_process",
            side_effect=stop_at_unity,
        ), mock.patch.object(
            module,
            "_cleanup_evidence",
            return_value={
                "processes": True,
                "files": True,
                "junctions": True,
                "subst": True,
            },
        ), mock.patch.object(
            module,
            "_write_failure_record",
        ), mock.patch.dict(
            module.os.environ,
            {"PHASE184H_DESKTOP_CLIENT_BARRIER": r"D:\ambient\barrier.json"},
            clear=True,
        ):
            with self.assertRaisesRegex(module.AcceptanceFailure, "test stop"):
                module.run_batch_parent(args)

        resolve_barrier.assert_called_once_with(output)
        self.assertEqual(fixed_barrier, actor_start.call_args.kwargs["desktop_barrier"])
        self.assertNotIn("PHASE184H_DESKTOP_CLIENT_BARRIER", unity_environment)

    def test_unity_routes_emit_native_gate_before_full_bridge_readiness(self):
        """Verify unity routes emit native gate before full bridge readiness."""

        source = (
            ROOT
            / "Unity2Foxglove"
            / "Assets"
            / "Scripts"
            / "ManualAcceptance"
            / "Phase184FoxRunProfileAcceptance.cs"
        ).read_text(encoding="utf-8")
        multi = source[
            source.index("public sealed partial class Phase184MultiTargetRoute") :
            source.index("public sealed partial class Phase184DegradedTargetRoute")
        ]
        qos = source[
            source.index("public sealed partial class Phase184QosContractRoute") :
            source.index("public sealed partial class Phase184StreamRoute")
        ]

        marker = '"PHASE184G_NATIVE_READY_FOR_BRIDGE"'
        self.assertIn(marker, multi)
        self.assertIn(marker, qos)
        self.assertLess(
            multi.index(marker),
            multi.index(
                "status.SucceededTargets\n"
                "                       == (FoxRunEndpoint.Foxglove"
            ),
        )
        self.assertLess(qos.index(marker), qos.index("if (_readyContracts == 3)"))

    def test_multi_target_peer_starts_delivery_window_after_unity_arms_local_token(self):
        """Verify multi target peer starts delivery window after unity arms local token."""

        source = (
            ROOT / "Scripts" / "smoke" / "foxrun" / "phase184_profile_acceptance.py"
        ).read_text(encoding="utf-8")
        multi_peer = source[
            source.index("def _run_multi_target_peer(") : source.index(
                "def _run_qos_peer(", source.index("def _run_multi_target_peer(")
            )
        ]

        armed_marker = multi_peer.index(
            'wait_for_log_marker(config, "PHASE184G_MULTI_LOCAL_ARMED"'
        )
        delivery_window = multi_peer.index(
            "_spin_until(",
            multi_peer.index("def local_one_ready()"),
        )
        self.assertLess(armed_marker, delivery_window)

    def test_multi_target_foxglove_starts_delivery_window_after_unity_arms_local_token(
        self,
    ):
        """Verify multi target foxglove starts delivery window after unity arms local token."""

        source = (
            ROOT / "Scripts" / "smoke" / "foxrun" / "phase184_profile_acceptance.py"
        ).read_text(encoding="utf-8")
        client_start = source.index("async def _run_foxglove_client_async(")
        multi_start = source.index('if case == "multi-target":', client_start)
        multi_client = source[
            multi_start : source.index('if case == "degraded-target":', multi_start)
        ]

        armed_marker = multi_client.index(
            '"PHASE184G_MULTI_LOCAL_ARMED"'
        )
        delivery_window = multi_client.index("_receive_foxglove_stages(")
        self.assertLess(armed_marker, delivery_window)

    def test_windows_bridge_build_dependencies_are_selected_from_ros_prefix(self):
        """Verify windows bridge build dependencies are selected from ROS prefix."""

        module = load_module()
        TEST_ROOT.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix="bridge-deps-", dir=TEST_ROOT) as raw:
            ros_root = pathlib.Path(raw) / "ros2_jazzy"
            library = ros_root / ".pixi" / "envs" / "default" / "Library"
            required = (
                library / "include" / "openssl" / "opensslv.h",
                library / "lib" / "libcrypto.lib",
                library / "lib" / "libssl.lib",
                library / "lib" / "cmake" / "tinyxml2" / "tinyxml2-config.cmake",
                library
                / "share"
                / "cmake"
                / "nlohmann_json"
                / "nlohmann_jsonConfig.cmake",
            )
            for path in required:
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text("fixture\n", encoding="utf-8")

            environment = module.prepare_windows_bridge_build_environment(
                {"CMAKE_PREFIX_PATH": str(ros_root)},
                ros_root,
            )
            self.assertEqual(str(library), environment["OPENSSL_ROOT_DIR"])
            self.assertEqual(
                str(library / "share" / "cmake" / "nlohmann_json"),
                environment["nlohmann_json_DIR"],
            )
            self.assertEqual(
                str(library / "lib" / "cmake" / "tinyxml2"),
                environment["tinyxml2_DIR"],
            )
            self.assertEqual(
                [str(library), str(ros_root)],
                environment["CMAKE_PREFIX_PATH"].split(os.pathsep),
            )

            required[-1].unlink()
            with self.assertRaisesRegex(module.AcceptanceFailure, "FAIL_BUILD"):
                module.prepare_windows_bridge_build_environment(
                    {"CMAKE_PREFIX_PATH": str(ros_root)},
                    ros_root,
                )

    def test_bridge_build_cache_is_stable_exact_and_owned(self):
        """Verify bridge build cache is stable exact and owned."""

        module = load_module()
        TEST_ROOT.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix="bridge-cache-", dir=TEST_ROOT) as raw:
            root = pathlib.Path(raw)
            cache_root = root / "cache"
            toolchain = mock.Mock()
            toolchain.ros2_root = root / "ros"
            toolchain.python_executable = root / "python.exe"
            toolchain.colcon_executable = root / "colcon.exe"
            command = ["colcon.exe", "build", "--merge-install"]
            environment = {
                "VCToolsVersion": "14.51",
                "WindowsSDKVersion": "10.0.26100.0",
                "CMAKE_PREFIX_PATH": str(root / "ros" / "Library"),
            }
            key = module.bridge_build_cache_key(
                ROOT,
                "jazzy-fastrtps",
                "jazzy",
                "rmw_fastrtps_cpp",
                toolchain,
                command,
                environment,
            )
            self.assertRegex(key, r"\A[0-9a-f]{64}\Z")
            self.assertNotEqual(
                key,
                module.bridge_build_cache_key(
                    ROOT,
                    "jazzy-fastrtps",
                    "jazzy",
                    "rmw_fastrtps_cpp",
                    toolchain,
                    [*command, "--changed"],
                    environment,
                ),
            )

            overlay, reused = module.prepare_bridge_build_workspace(
                cache_root,
                "jazzy-fastrtps",
                key,
            )
            self.assertFalse(reused)
            install = overlay / "install"
            (install / "lib" / "unity2foxglove_ros2_bridge").mkdir(
                parents=True,
                exist_ok=True,
            )
            (install / "share" / "unity2foxglove_ros2_bridge").mkdir(
                parents=True,
                exist_ok=True,
            )
            (install / "local_setup.bat").write_text("@echo off\n", encoding="utf-8")
            (
                install
                / "share"
                / "unity2foxglove_ros2_bridge"
                / "package.xml"
            ).write_text("<package/>\n", encoding="utf-8")
            (
                install
                / "lib"
                / "unity2foxglove_ros2_bridge"
                / "unity2foxglove_ros2_bridge.exe"
            ).write_bytes(b"bridge")
            module.seal_bridge_build_workspace(
                overlay,
                "jazzy-fastrtps",
                key,
            )

            cached, reused = module.prepare_bridge_build_workspace(
                cache_root,
                "jazzy-fastrtps",
                key,
            )
            self.assertTrue(reused)
            self.assertEqual(overlay, cached)

            unowned = cache_root / "lyrical-zenoh" / "bridge-overlay"
            unowned.mkdir(parents=True)
            with self.assertRaisesRegex(module.AcceptanceFailure, "FAIL_BUILD"):
                module.prepare_bridge_build_workspace(
                    cache_root,
                    "lyrical-zenoh",
                    "b" * 64,
                )

    def test_existing_acceptance_scene_still_runs_the_cold_start_preflight(self):
        """Verify existing acceptance scene still runs the cold start preflight."""

        module = load_module()
        TEST_ROOT.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix="scene-preflight-", dir=TEST_ROOT) as raw:
            repository = pathlib.Path(raw) / "repository"
            output = repository / "build" / "phase184" / "acceptance" / "run"
            scene = (
                repository
                / "Unity2Foxglove"
                / "Assets"
                / "Scenes"
                / "ManualAcceptance"
                / "Phase184FoxRunProfileAcceptance.unity"
            )
            scene.parent.mkdir(parents=True, exist_ok=True)
            scene.write_text("tracked scene\n", encoding="utf-8")
            output.mkdir(parents=True, exist_ok=True)

            with mock.patch.object(module, "_run_logged_preflight") as run:
                with mock.patch.object(
                    module,
                    "read_log_lines",
                    return_value=["PHASE184G_SCENE_BUILDER_PASS"],
                ):
                    actual = module._ensure_acceptance_scene(
                        pathlib.Path(r"C:\Unity.exe"),
                        repository,
                        output,
                        job=None,
                    )

            self.assertEqual(scene, actual)
            run.assert_called_once()
            command = run.call_args.args[0]
            self.assertIn(
                "Unity2Foxglove.Phase184FoxRunProfileAcceptanceBuilder.CreateOrRefreshAcceptanceScene",
                command,
            )

    def test_manual_play_prompt_is_helper_selected_and_single_play(self):
        """Manual helpers must own the selected case and exactly one Play session."""

        prompt = getattr(load_module(), "manual_play_prompt", lambda _case: "")(
            "multi-target"
        )

        self.assertIn("helper-selected case multi-target", prompt)
        self.assertIn("exactly one Play session", prompt)
        self.assertNotIn("select a route", prompt.lower())
        self.assertNotIn("wait for endpoint readiness", prompt.lower())

    def test_generated_scene_routes_are_read_only_inactive_and_helper_owned(self):
        """The generated route assets must stay read-only until the controller arms one."""

        scene = (
            ROOT
            / "Unity2Foxglove"
            / "Assets"
            / "Scenes"
            / "ManualAcceptance"
            / "Phase184FoxRunProfileAcceptance.unity"
        )
        contents = scene.read_text(encoding="utf-8")
        object_blocks = re.findall(
            r"(?ms)^--- !u!1 &(\d+)\r?\n(.*?)(?=^--- !u!|\Z)",
            contents,
        )
        component_blocks = dict(
            re.findall(
                r"(?ms)^--- !u!114 &(\d+)\r?\n(.*?)(?=^--- !u!|\Z)",
                contents,
            )
        )
        transform_blocks = dict(
            re.findall(
                r"(?ms)^--- !u!4 &(\d+)\r?\n(.*?)(?=^--- !u!|\Z)",
                contents,
            )
        )
        for name, route_guid in {
            "Helper-owned Route - Foxglove Profile": "983acb559504477ebd0c4d69a7d1edbe",
            "Helper-owned Route - Multi Target": "7b052fef51264defb3b5934d0271da7a",
            "Helper-owned Route - Degraded Target": "7f1320889ffd4aae8580cf5507278c6a",
            "Helper-owned Route - QoS Contract": "ae2bf84a4ef244ccb4185841a415279b",
            "Helper-owned Route - Stream 640 Hz": "f08839578006415a9d94a9ce4ef663a9",
        }.items():
            with self.subTest(name=name):
                matching = [
                    (file_id, block)
                    for file_id, block in object_blocks
                    if f"m_Name: {name}" in block
                ]
                self.assertEqual(1, len(matching))
                file_id, block = matching[0]
                self.assertRegex(block, r"(?m)^  m_ObjectHideFlags: 8$")
                self.assertRegex(block, rf"(?m)^  m_Name: {re.escape(name)}$")
                self.assertRegex(block, r"(?m)^  m_IsActive: 0$")
                component_ids = re.findall(
                    r"(?m)^  - component: \{fileID: (\d+)\}$",
                    block,
                )
                self.assertEqual(2, len(component_ids))
                route_components = [
                    component_blocks[component_id]
                    for component_id in component_ids
                    if component_id in component_blocks
                    and f"m_GameObject: {{fileID: {file_id}}}" in component_blocks[component_id]
                ]
                self.assertEqual(1, len(route_components))
                self.assertRegex(route_components[0], r"(?m)^  m_ObjectHideFlags: 8$")
                self.assertRegex(
                    route_components[0],
                    rf"(?m)^  m_Script: \{{fileID: 11500000, guid: {route_guid}, type: 3\}}$",
                )
                transforms = [
                    transform_blocks[component_id]
                    for component_id in component_ids
                    if component_id in transform_blocks
                    and f"m_GameObject: {{fileID: {file_id}}}" in transform_blocks[component_id]
                ]
                self.assertEqual(1, len(transforms))
                self.assertRegex(transforms[0], r"(?m)^  m_ObjectHideFlags: 8$")

    def test_workers_wait_for_correlated_unity_context_in_batch_and_manual_modes(self):
        """Cold Batch imports cannot consume finite actor deadlines before Play."""

        module = load_module()
        for execution_mode in ("batch", "manual"):
            with self.subTest(execution_mode=execution_mode):
                config = {
                    "executionMode": execution_mode,
                    "case": "foxglove-profile",
                    "token": "p184g_A1b2C3d4E5f6",
                }
                with mock.patch.object(module, "wait_for_log_marker") as wait:
                    module._wait_for_unity_context(config)

                wait.assert_called_once_with(
                    config,
                    "PHASE184G_CONTEXT_READY",
                    900.0,
                )

    def test_foxglove_connect_waits_for_optional_desktop_barrier_after_context(self):
        """Verify foxglove connect waits for optional desktop barrier after context."""

        module = load_module()
        config = {
            "case": "foxglove-profile",
            "token": "p184g_A1b2C3d4E5f6",
            "topics": ["/profile/default", "/profile/json"],
            "observationWindows": {"positiveSeconds": 3},
            "foxgloveHost": "127.0.0.1",
            "foxglovePort": 18765,
        }
        barrier = pathlib.Path(
            r"D:\owned\phase184g-20260727-desktop01\desktop-client-barrier.json"
        )

        class StopAfterConnect(RuntimeError):
            """Represent the stop after connect contract."""

            pass

        def run_client(environment, *, barrier_failure=None):
            """Run client."""

            events = []

            def context_ready(_config):
                """Handle the context ready step."""

                events.append("context")

            def wait_for_barrier(actual_config, actual_path):
                """Wait for barrier."""

                self.assertIs(config, actual_config)
                self.assertEqual(str(barrier), actual_path)
                events.append("barrier")
                if barrier_failure is not None:
                    raise barrier_failure

            async def connect(*_args, **_kwargs):
                """Handle the connect step."""

                events.append("connect")
                raise StopAfterConnect()

            websockets = mock.Mock(connect=mock.AsyncMock(side_effect=connect))
            with mock.patch.dict(sys.modules, {"websockets": websockets}), mock.patch.dict(
                module.os.environ,
                environment,
                clear=True,
            ), mock.patch.object(
                module,
                "write_actor_ready",
            ), mock.patch.object(
                module,
                "_wait_for_unity_context",
                side_effect=context_ready,
            ), mock.patch.object(
                module.desktop_live_protocol,
                "wait_for_desktop_barrier",
                side_effect=wait_for_barrier,
            ) as wait:
                expected_type = (
                    type(barrier_failure)
                    if barrier_failure is not None
                    else StopAfterConnect
                )
                with self.assertRaises(expected_type):
                    module.asyncio.run(module._run_foxglove_client_async(config))
            return events, wait, websockets.connect

        events, wait, connect = run_client({})
        self.assertEqual(["context", "connect"], events)
        wait.assert_not_called()
        connect.assert_awaited_once()

        events, wait, connect = run_client(
            {"PHASE184H_DESKTOP_CLIENT_BARRIER": str(barrier)}
        )
        self.assertEqual(["context", "barrier", "connect"], events)
        wait.assert_called_once_with(config, str(barrier))
        connect.assert_awaited_once()

        invalid = ValueError("invalid desktop barrier")
        events, wait, connect = run_client(
            {"PHASE184H_DESKTOP_CLIENT_BARRIER": str(barrier)},
            barrier_failure=invalid,
        )
        self.assertEqual(["context", "barrier"], events)
        wait.assert_called_once_with(config, str(barrier))
        connect.assert_not_awaited()

    def test_foxglove_profile_requests_bootstrap_only_after_subscribing(self):
        """Verify foxglove profile requests bootstrap only after subscribing."""

        module = load_module()
        topics = ("/profile/default", "/profile/json")
        config = {
            "case": "foxglove-profile",
            "token": "p184g_A1b2C3d4E5f6",
            "topics": list(topics),
            "observationWindows": {
                "positiveSeconds": 3,
                "negativeSeconds": 3,
            },
            "foxgloveHost": "127.0.0.1",
            "foxglovePort": 18765,
        }
        events: list[str] = []
        websocket = mock.Mock()
        websocket.close = mock.AsyncMock()
        channels = {
            topics[0]: mock.Mock(encoding="protobuf"),
            topics[1]: mock.Mock(encoding="json"),
        }

        async def subscribe(_websocket, _channels):
            """Handle the subscribe step."""

            events.append("subscribe")
            return {184001: topics[0], 184002: topics[1]}

        async def send_json(
            _websocket,
            _topic,
            _field_name,
            _token,
            stage,
            _count,
            _channel_id,
            *,
            advertise,
        ):
            """Handle the send JSON step."""

            events.append(f"send:{stage}:advertise={advertise}")

        receive_count = 0

        async def receive(*_args, **_kwargs):
            """Receive one correlated acceptance sample."""

            nonlocal receive_count
            receive_count += 1
            events.append(f"receive:{receive_count}")
            return {}, [], float(receive_count)

        def wait_marker(_config, marker, _timeout):
            """Handle the wait marker step."""

            events.append(f"marker:{marker}")

        websockets = mock.Mock(
            connect=mock.AsyncMock(return_value=websocket)
        )
        with mock.patch.dict(
            sys.modules,
            {"websockets": websockets},
        ), mock.patch.object(
            module,
            "write_actor_ready",
        ), mock.patch.object(
            module,
            "_wait_for_unity_context",
        ), mock.patch.object(
            module,
            "_wait_for_foxglove_channels",
            new=mock.AsyncMock(return_value=channels),
        ), mock.patch.object(
            module,
            "_foxglove_subscribe",
            side_effect=subscribe,
        ), mock.patch.object(
            module,
            "_foxglove_advertise_and_send_json",
            side_effect=send_json,
        ) as send, mock.patch.object(
            module,
            "_receive_foxglove_stages",
            side_effect=receive,
        ), mock.patch.object(
            module,
            "wait_for_log_marker",
            side_effect=wait_marker,
        ), mock.patch.object(
            module,
            "wait_for_terminal_marker",
        ):
            result = module.asyncio.run(
                module._run_foxglove_client_async(config)
            )

        self.assertTrue(result["deliveryObserved"])
        self.assertLess(
            events.index("subscribe"),
            events.index(
                "send:profile-client-ready:advertise=True"
            ),
        )
        self.assertLess(
            events.index(
                "send:profile-client-ready:advertise=True"
            ),
            events.index(
                "marker:PHASE184G_PROFILE_CLIENT_READY"
            ),
        )
        self.assertLess(
            events.index(
                "marker:PHASE184G_PROFILE_CLIENT_READY"
            ),
            events.index("receive:1"),
        )
        self.assertEqual(
            [
                call.args[4]
                for call in send.await_args_list
            ],
            [
                "profile-client-ready",
                "profile-a",
                "profile-b",
                "profile-b",
            ],
        )
        self.assertTrue(send.await_args_list[0].kwargs["advertise"])
        self.assertEqual(
            (
                websocket,
                topics[1],
                "explicitJson",
                config["token"],
                "profile-client-ready",
                18400,
                184901,
            ),
            send.await_args_list[0].args,
        )
        self.assertTrue(
            all(
                call.args[1] == topics[1]
                and call.args[2] == "explicitJson"
                and call.args[3] == config["token"]
                and call.args[6] == 184901
                for call in send.await_args_list
            )
        )
        self.assertTrue(
            all(
                not call.kwargs["advertise"]
                for call in send.await_args_list[1:]
            )
        )
        websocket.close.assert_awaited_once()

    def test_foxglove_profile_rejects_client_ready_echo_before_profile_input(self):
        """Verify foxglove profile rejects client ready echo before profile input."""

        module = load_module()
        token = "p184g_A1b2C3d4E5f6"
        topics = ("/profile/default", "/profile/json")
        config = {
            "case": "foxglove-profile",
            "token": token,
            "topics": list(topics),
            "observationWindows": {
                "positiveSeconds": 3,
                "negativeSeconds": 3,
            },
            "foxgloveHost": "127.0.0.1",
            "foxglovePort": 18765,
        }
        websocket = mock.Mock()
        websocket.close = mock.AsyncMock()
        channels = {
            topics[0]: mock.Mock(encoding="protobuf"),
            topics[1]: mock.Mock(encoding="json"),
        }
        send = mock.AsyncMock()
        websockets = mock.Mock(
            connect=mock.AsyncMock(return_value=websocket)
        )

        with mock.patch.dict(
            sys.modules,
            {"websockets": websockets},
        ), mock.patch.object(
            module,
            "write_actor_ready",
        ), mock.patch.object(
            module,
            "_wait_for_unity_context",
        ), mock.patch.object(
            module,
            "_wait_for_foxglove_channels",
            new=mock.AsyncMock(return_value=channels),
        ), mock.patch.object(
            module,
            "_foxglove_subscribe",
            new=mock.AsyncMock(
                return_value={
                    184001: topics[0],
                    184002: topics[1],
                }
            ),
        ), mock.patch.object(
            module,
            "_foxglove_advertise_and_send_json",
            send,
        ), mock.patch.object(
            module,
            "_receive_foxglove_stages",
            new=mock.AsyncMock(
                return_value=(
                    {},
                    [token + "-profile-client-ready"],
                    1.0,
                )
            ),
        ), mock.patch.object(
            module,
            "wait_for_log_marker",
        ):
            with self.assertRaisesRegex(
                module.AcceptanceFailure,
                "FAIL_ORIGIN",
            ):
                module.asyncio.run(
                    module._run_foxglove_client_async(config)
                )

        self.assertEqual(1, send.await_count)
        self.assertEqual(
            "profile-client-ready",
            send.await_args.args[4],
        )
        websocket.close.assert_awaited_once()

    def test_degraded_client_requests_delivery_only_after_subscribing(self):
        """Verify degraded client requests delivery only after subscribing."""

        module = load_module()
        token = "p184g_A1b2C3d4E5f6"
        topic = "/foxrun/phase184/degraded/state"
        config = {
            "case": "degraded-target",
            "token": token,
            "topics": [topic],
            "observationWindows": {
                "positiveSeconds": 3,
                "negativeSeconds": 3,
            },
            "foxgloveHost": "127.0.0.1",
            "foxglovePort": 18765,
        }
        events: list[str] = []
        websocket = mock.Mock()
        websocket.close = mock.AsyncMock()
        channels = {topic: mock.Mock(encoding="protobuf")}

        async def subscribe(_websocket, _channels):
            """Handle the subscribe step."""

            events.append("subscribe")
            return {184001: topic}

        async def send_json(*args, **kwargs):
            """Handle the send JSON step."""

            events.append(
                f"send:{args[4]}:advertise={kwargs['advertise']}"
            )

        async def receive(*_args, **_kwargs):
            """Receive one correlated acceptance sample."""

            events.append("receive")
            return {}, [], 1.0

        def wait_marker(_config, marker, _timeout):
            """Handle the wait marker step."""

            events.append(f"marker:{marker}")

        send = mock.AsyncMock(side_effect=send_json)
        websockets = mock.Mock(
            connect=mock.AsyncMock(return_value=websocket)
        )
        with mock.patch.dict(
            sys.modules,
            {"websockets": websockets},
        ), mock.patch.object(
            module,
            "write_actor_ready",
        ), mock.patch.object(
            module,
            "_wait_for_unity_context",
        ), mock.patch.object(
            module,
            "_wait_for_foxglove_channels",
            new=mock.AsyncMock(return_value=channels),
        ), mock.patch.object(
            module,
            "_foxglove_subscribe",
            side_effect=subscribe,
        ), mock.patch.object(
            module,
            "_foxglove_advertise_and_send_json",
            send,
        ), mock.patch.object(
            module,
            "_receive_foxglove_stages",
            side_effect=receive,
        ), mock.patch.object(
            module,
            "wait_for_log_marker",
            side_effect=wait_marker,
        ), mock.patch.object(
            module,
            "wait_for_terminal_marker",
        ):
            result = module.asyncio.run(
                module._run_foxglove_client_async(config)
            )

        self.assertTrue(result["deliveryObserved"])
        self.assertLess(
            events.index("subscribe"),
            events.index(
                "send:degraded-client-ready:advertise=True"
            ),
        )
        self.assertLess(
            events.index(
                "send:degraded-client-ready:advertise=True"
            ),
            events.index(
                "marker:PHASE184G_DEGRADED_CLIENT_READY"
            ),
        )
        self.assertLess(
            events.index(
                "marker:PHASE184G_DEGRADED_CLIENT_READY"
            ),
            events.index("receive"),
        )
        send.assert_awaited_once_with(
            websocket,
            module.DEGRADED_CLIENT_READY_TOPIC,
            "clientReady",
            token,
            "degraded-client-ready",
            18419,
            184902,
            advertise=True,
        )
        websocket.close.assert_awaited_once()

    def test_bridge_health_frame_is_correlated_and_strict(self):
        """Verify bridge health frame is correlated and strict."""

        module = load_module()
        request_id = "p184g_A1b2C3d4E5f6"
        frame = module.build_u2r2_health_frame(request_id)
        self.assertEqual(b"U2R2", frame[:4])
        version, flags, header_size, payload_size = struct.unpack("<HHII", frame[4:16])
        self.assertEqual((1, 0, 0), (version, flags, payload_size))
        header = json.loads(frame[16 : 16 + header_size])
        self.assertEqual(
            {"op": "health_ping", "requestId": request_id, "protocolVersion": 1},
            header,
        )

        response = module.encode_u2r2_frame(
            {
                "op": "health_pong",
                "requestId": request_id,
                "protocolVersion": 1,
                "status": "ok",
                "sidecarName": "unity2foxglove_ros2_bridge",
                "sidecarVersion": "0.1.0",
            },
            b"",
        )
        parsed, payload = module.decode_u2r2_frame(response)
        module.validate_bridge_health_response(parsed, payload, request_id)
        parsed["requestId"] = "stale"
        with self.assertRaisesRegex(module.AcceptanceFailure, "FAIL_BRIDGE"):
            module.validate_bridge_health_response(parsed, payload, request_id)

    def test_marker_parser_requires_exact_case_and_token(self):
        """Verify marker parser requires exact case and token."""

        module = load_module()
        token = "p184g_A1b2C3d4E5f6"
        lines = [
            "PHASE184G_CASE_PASS case=multi-target token=p184g_stale000000",
            f"PHASE184G_CASE_PASS case=other token={token}",
            f"PHASE184G_CASE_PASS case=multi-target token={token} remoteApplied=True",
        ]
        marker = module.find_terminal_marker(lines, "multi-target", token)
        self.assertIsNotNone(marker)
        self.assertEqual("PASS", marker.verdict)

    def test_atomic_config_writer_leaves_no_temporary_file(self):
        """Verify atomic config writer leaves no temporary file."""

        module = load_module()
        TEST_ROOT.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix="atomic-", dir=TEST_ROOT) as raw:
            target = pathlib.Path(raw) / "run-config.json"
            module.write_private_json_atomic(target, {"value": 184})
            self.assertEqual({"value": 184}, json.loads(target.read_text(encoding="utf-8")))
            self.assertEqual([], list(target.parent.glob("*.tmp")))

    def test_bridge_parser_requires_the_exact_qos_profile(self):
        """Verify bridge parser requires the exact QoS profile."""

        module = load_module()
        run_id = "phase184g-20260726-bridge01"
        output = ROOT / "build" / "phase184" / "acceptance" / run_id
        config = module.make_run_config(
            repository=ROOT,
            run_id=run_id,
            token="p184g_A1b2C3d4E5f6",
            case="qos-contract",
            profile="jazzy-fastrtps",
            output_root=output,
            domain_id=84,
            foxglove_port=18765,
            bridge_port=18767,
            phase181_workspace=ROOT / "build" / "phase181" / "jazzy-fastrtps" / "peer-workspace",
            interface_package="unity2foxglove_foxrun_interfaces_v1",
            interface_type="unity2foxglove_foxrun_interfaces_v1/msg/Phase181State48D288ED82F1Envelope",
            interface_digest="a" * 64,
        )
        expected = module._expected_qos_by_topic(config)
        TEST_ROOT.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix="bridge-", dir=TEST_ROOT) as raw:
            log = pathlib.Path(raw) / "bridge.log"
            lines = []
            for topic, qos in expected.items():
                lines.append(
                    "publisher "
                    + topic
                    + " example/msg/Type profile="
                    + str(qos["profile"])
                    + " reliability="
                    + str(qos["reliability"])
                    + " durability="
                    + str(qos["durability"])
                    + " history="
                    + str(qos["history"])
                    + " depth="
                    + str(qos["depth"])
                )
            log.write_text("\n".join(lines) + "\n", encoding="utf-8")
            evidence = module.parse_bridge_publisher_evidence(config, log)
            self.assertEqual(set(expected), set(evidence["publishers"]))
            self.assertNotIn("healthReady", evidence)

            log.write_text(
                ("\n".join(lines)).replace(
                    "profile=system_default",
                    "profile=default",
                    1,
                )
                + "\n",
                encoding="utf-8",
            )
            with self.assertRaisesRegex(module.AcceptanceFailure, "FAIL_QOS"):
                module.parse_bridge_publisher_evidence(config, log)

    def test_qos_summary_requires_and_carries_bridge_parser_evidence(self):
        """Verify QoS summary requires and carries bridge parser evidence."""

        module = load_module()
        run_id = "phase184g-20260726-qossum01"
        output = ROOT / "build" / "phase184" / "acceptance" / run_id
        config = module.make_run_config(
            repository=ROOT,
            run_id=run_id,
            token="p184g_A1b2C3d4E5f6",
            case="qos-contract",
            profile="jazzy-fastrtps",
            output_root=output,
            domain_id=84,
            foxglove_port=18765,
            bridge_port=18767,
            phase181_workspace=ROOT / "build" / "phase181" / "jazzy-fastrtps" / "peer-workspace",
            interface_package="unity2foxglove_foxrun_interfaces_v1",
            interface_type="unity2foxglove_foxrun_interfaces_v1/msg/Phase181State48D288ED82F1Envelope",
            interface_digest="a" * 64,
        )
        expected = module._expected_qos_by_topic(config)
        bridge_publishers = {
            topic: dict(qos)
            for topic, qos in expected.items()
        }
        delivery_by_topic = {
            topic: [
                "native-gid-" + str(index),
                "bridge-gid-" + str(index),
            ]
            for index, topic in enumerate(config["topics"])
        }
        publishers_by_topic = {
            topic: [
                {"node": "/unity_native", "gid": gids[0]},
                {
                    "node": "/unity2foxglove_ros2_bridge",
                    "gid": gids[1],
                },
            ]
            for topic, gids in delivery_by_topic.items()
        }
        results = {
            "ros2-peer": {
                "verdict": "PASS",
                "evidence": {
                    "deliveryByTopic": delivery_by_topic,
                    "deliveryAttributionByTopic": {
                        topic: "publication-sequence-plus-graph-gid"
                        for topic in config["topics"]
                    },
                },
            },
            "graph-observer": {
                "verdict": "PASS",
                "evidence": {
                    "endpointsObserved": True,
                    "nodeIdentities": [
                        "/unity_native",
                        "/unity2foxglove_ros2_bridge",
                    ],
                    "publisherGids": sorted(
                        gid
                        for gids in delivery_by_topic.values()
                        for gid in gids
                    ),
                    "publishersByTopic": publishers_by_topic,
                    "negativeObservationSeconds": 0,
                    "transportObservedQos": {
                        topic: {
                            "publishers": [
                                {
                                    **{
                                        key: value
                                        for key, value in qos.items()
                                        if key != "profile"
                                    },
                                    "representedAxes": [
                                        "reliability",
                                        "durability",
                                        "history",
                                        "depth",
                                    ],
                                },
                                {
                                    **{
                                        key: value
                                        for key, value in qos.items()
                                        if key != "profile"
                                    },
                                    "representedAxes": [
                                        "reliability",
                                        "durability",
                                        "history",
                                        "depth",
                                    ],
                                },
                            ],
                            "subscriptions": [],
                        }
                        for topic, qos in expected.items()
                    },
                    "qosMatches": True,
                },
            },
            "bridge": {
                "verdict": "PASS",
                "evidence": {
                    "healthReady": True,
                    "nodeIdentity": "unity2foxglove_ros2_bridge",
                    "publishers": bridge_publishers,
                },
            },
        }
        process_codes = {
            "unity": 0,
            "ros2-peer": 0,
            "graph-observer": 0,
            "bridge": 0,
        }
        self._write_observed_runtime_markers(config)
        summary = module.build_pass_summary(
            config=config,
            terminal=module.TerminalMarker(
                "PASS",
                "PHASE184G_CASE_PASS",
                {},
            ),
            results=results,
            process_exit_codes=process_codes,
            unity_version="6000.3.14f1",
            cleanup={
                "processes": True,
                "files": True,
                "junctions": True,
                "subst": True,
            },
        )
        self.assertEqual(
            bridge_publishers,
            summary["qos"]["transportObserved"]["bridge"],
        )

        incomplete = dict(results)
        incomplete.pop("bridge")
        with self.assertRaisesRegex(module.AcceptanceFailure, "FAIL_TERMINAL"):
            module.build_pass_summary(
                config=config,
                terminal=module.TerminalMarker(
                    "PASS",
                    "PHASE184G_CASE_PASS",
                    {},
                ),
                results=incomplete,
                process_exit_codes=process_codes,
                unity_version="6000.3.14f1",
                cleanup={
                    "processes": True,
                    "files": True,
                    "junctions": True,
                    "subst": True,
                },
            )

    def test_summary_evidence_comes_from_correlated_unity_runtime_markers(self):
        """Profile and healthy fanout claims must be parsed from current Unity markers."""

        module = load_module()
        token = "p184g_A1b2C3d4E5f6"
        case = "multi-target"
        TEST_ROOT.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix="observed-", dir=TEST_ROOT) as raw:
            log_path = pathlib.Path(raw) / "unity.log"
            log_path.write_text(
                "\n".join(
                    (
                        "PHASE184G_PROFILE_EVIDENCE "
                        f"case={case} token=p184g_Stale00000000 "
                        "source=Foxglove targets=Foxglove "
                        "publishEncoding=json subscribeEncoding=json",
                        "PHASE184G_PROFILE_EVIDENCE "
                        f"case={case} token={token} "
                        "source=Ros2Native "
                        "targets=Foxglove,Ros2Native,Ros2Bridge "
                        "publishEncoding=protobuf subscribeEncoding=protobuf",
                        "PHASE184G_MULTI_TARGET_STATUS "
                        f"case={case} token={token} status=Ready "
                        "succeeded=Foxglove,Ros2Native,Ros2Bridge failed=None "
                        "bridgeRuntimeFailures=0",
                    )
                )
                + "\n",
                encoding="utf-8",
            )
            config = {
                "case": case,
                "token": token,
                "unityLog": str(log_path),
                "topics": ["/foxrun/phase184/multi/state"],
            }

            self.assertEqual(
                {
                    "source": "Ros2Native",
                    "targets": ["Foxglove", "Ros2Native", "Ros2Bridge"],
                    "publishEncoding": "protobuf",
                    "subscribeEncoding": "protobuf",
                },
                module._observed_profile_evidence(config),
            )
            self.assertEqual(
                {
                    "states": {
                        "foxglove": "Ready",
                        "ros2Native": "Ready",
                        "ros2Bridge": "Ready",
                    },
                    "diagnosticCounts": {
                        "failedTargets": 0,
                        "bridgeRuntimeFailures": 0,
                    },
                    "statusEvidence": {
                        "aggregate": "Ready",
                        "succeeded": "Foxglove,Ros2Native,Ros2Bridge",
                        "failed": "None",
                        "bridgeRuntimeFailures": 0,
                    },
                },
                module._observed_target_evidence(
                    config,
                    module.TerminalMarker("PASS", "PHASE184G_CASE_PASS", {}),
                ),
            )

            log_path.write_text(
                "PHASE184G_PROFILE_EVIDENCE "
                f"case={case} token=p184g_Stale00000000 "
                "source=Foxglove targets=Foxglove "
                "publishEncoding=json subscribeEncoding=json\n",
                encoding="utf-8",
            )
            with self.assertRaisesRegex(module.AcceptanceFailure, "FAIL_TERMINAL"):
                module._observed_profile_evidence(config)

    def test_degraded_summary_consumes_exact_unity_target_status_fields(self):
        """The parent must carry the runtime status marker instead of recreating it."""

        module = load_module()
        token = "p184g_A1b2C3d4E5f6"
        run_id = "phase184g-20260726-degraded01"
        output = ROOT / "build" / "phase184" / "acceptance" / run_id
        config = module.make_run_config(
            repository=ROOT,
            run_id=run_id,
            token=token,
            case="degraded-target",
            profile="jazzy-fastrtps",
            output_root=output,
            domain_id=84,
            foxglove_port=18765,
            bridge_port=18767,
            phase181_workspace=(
                ROOT
                / "build"
                / "phase181"
                / "jazzy-fastrtps"
                / "peer-workspace"
            ),
            interface_package="unity2foxglove_foxrun_interfaces_v1",
            interface_type=(
                "unity2foxglove_foxrun_interfaces_v1"
                "/msg/Phase181State48D288ED82F1Envelope"
            ),
            interface_digest="a" * 64,
        )
        topic = str(config["topics"][0])
        results = {
            "foxglove-client": {
                "verdict": "PASS",
                "evidence": {
                    "deliveryObserved": True,
                    "channelEncodings": ["protobuf"],
                    "sampleToken": module.protocol.token_sha256(token),
                    "sampleStages": ["degraded-local"],
                    "timestamp": 1.0,
                },
            },
            "graph-observer": {
                "verdict": "PASS",
                "evidence": {
                    "endpointsObserved": True,
                    "nodeIdentities": [],
                    "publisherGids": [],
                    "publishersByTopic": {topic: []},
                    "negativeObservationSeconds": 3.0,
                    "noFallbackPublisher": True,
                    "transportObservedQos": {},
                    "qosMatches": False,
                },
            },
        }
        process_codes = {
            "unity": 0,
            "foxglove-client": 0,
            "graph-observer": 0,
        }
        cleanup = {
            "processes": True,
            "files": True,
            "junctions": True,
            "subst": True,
        }
        runtime_fields = {
            "status": "Degraded",
            "succeeded": "Foxglove",
            "failed": "Ros2Bridge",
            "foxgloveState": "Ready",
            "ros2BridgeState": "Unavailable",
            "bridgeDiagnostics": "1",
        }
        self._write_observed_runtime_markers(config)

        summary = module.build_pass_summary(
            config=config,
            terminal=module.TerminalMarker(
                "PASS",
                "PHASE184G_CASE_PASS",
                runtime_fields,
            ),
            results=results,
            process_exit_codes=process_codes,
            unity_version="6000.3.14f1",
            cleanup=cleanup,
        )

        self.assertEqual(
            {
                "foxglove": runtime_fields["foxgloveState"],
                "ros2Bridge": runtime_fields["ros2BridgeState"],
            },
            summary["targets"]["states"],
        )
        self.assertEqual(
            {
                "aggregate": runtime_fields["status"],
                "succeeded": runtime_fields["succeeded"],
                "failed": runtime_fields["failed"],
                "bridgeDiagnostics": 1,
            },
            summary["targets"]["statusEvidence"],
        )

        for field, value in (
            ("status", "Ready"),
            ("succeeded", "Ros2Bridge"),
            ("failed", "Foxglove"),
            ("foxgloveState", "Degraded"),
            ("ros2BridgeState", "Ready"),
            ("bridgeDiagnostics", "0"),
        ):
            with self.subTest(field=field):
                invalid_fields = dict(runtime_fields)
                invalid_fields[field] = value
                with self.assertRaisesRegex(
                    module.AcceptanceFailure,
                    "FAIL_FANOUT",
                ):
                    module.build_pass_summary(
                        config=config,
                        terminal=module.TerminalMarker(
                            "PASS",
                            "PHASE184G_CASE_PASS",
                            invalid_fields,
                        ),
                        results=results,
                        process_exit_codes=process_codes,
                        unity_version="6000.3.14f1",
                        cleanup=cleanup,
                    )

    def test_stream_peer_evidence_must_match_unity_and_nominal_rate(self):
        """Verify stream peer evidence must match unity and nominal rate."""

        module = load_module()
        terminal = module.TerminalMarker(
            "PASS",
            "PHASE184G_CASE_PASS",
            {
                "received": "792",
                "accepted": "792",
                "drained": "625",
                "replaced": "167",
                "rateDropped": "0",
                "highWater": "32",
                "disposalFailures": "0",
                "lastSequence": "1279",
                "ordered": "True",
                "ownershipBalanced": "True",
            },
        )
        evidence = module._validated_stream_evidence(
            terminal,
            {
                "offered": 1280,
                "nominalHz": 640,
                "productionElapsedSeconds": 2.0,
            },
        )
        self.assertEqual(1280, evidence["offered"])
        self.assertEqual(792, evidence["received"])
        self.assertEqual(488, evidence["transportDropped"])
        self.assertEqual(488, evidence["dropped"])
        self.assertEqual(1279, evidence["lastSequence"])

        with self.assertRaisesRegex(module.AcceptanceFailure, "FAIL_STREAM"):
            module._validated_stream_evidence(
                terminal,
                {
                    "offered": 1279,
                    "nominalHz": 640,
                    "productionElapsedSeconds": 2.0,
                },
            )
        too_many_received = module.TerminalMarker(
            terminal.verdict,
            terminal.line,
            dict(terminal.fields, received="1281"),
        )
        with self.assertRaisesRegex(module.AcceptanceFailure, "FAIL_STREAM"):
            module._validated_stream_evidence(
                too_many_received,
                {
                    "offered": 1280,
                    "nominalHz": 640,
                    "productionElapsedSeconds": 2.0,
                },
            )
        with self.assertRaisesRegex(module.AcceptanceFailure, "FAIL_STREAM"):
            module._validated_stream_evidence(
                terminal,
                {
                    "offered": 1280,
                    "nominalHz": 640,
                    "productionElapsedSeconds": 4.0,
                },
            )

    def test_foxglove_worker_persists_unexpected_failure(self):
        """Verify foxglove worker persists unexpected failure."""

        module = load_module()
        config = {"case": "foxglove-profile"}

        async def fail_unexpectedly(_config):
            """Handle the fail unexpectedly step."""

            raise ValueError("boom")

        with mock.patch.object(
            module,
            "_run_foxglove_client_async",
            side_effect=fail_unexpectedly,
        ):
            with mock.patch.object(module, "write_actor_result") as write_result:
                self.assertEqual(1, module.run_foxglove_client_worker(config))
        self.assertEqual("FAIL_CLIENT", write_result.call_args.kwargs["verdict"])
        self.assertEqual(
            {"diagnostic": "ValueError"},
            write_result.call_args.kwargs["evidence"],
        )

    def test_main_dispatches_manual_mode_without_launching_batch_parent(self):
        """Verify main dispatches manual mode without launching batch parent."""

        module = load_module()
        with mock.patch.object(module, "run_manual_parent", return_value=0) as manual:
            with mock.patch.object(module, "run_batch_parent") as batch:
                result = module.main(
                    [
                        "--case",
                        "multi-target",
                        "--profile",
                        "jazzy-fastrtps",
                        "--manual-editor",
                        "--unity-editor",
                        r"C:\Unity.exe",
                    ]
                )
        self.assertEqual(0, result)
        manual.assert_called_once()
        batch.assert_not_called()


if __name__ == "__main__":
    unittest.main()
