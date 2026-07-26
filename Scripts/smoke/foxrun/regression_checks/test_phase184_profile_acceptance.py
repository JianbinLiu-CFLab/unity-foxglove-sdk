#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Regression checks for the owned Phase184-G acceptance orchestrator."""

from __future__ import annotations

import importlib.util
import inspect
import json
import os
import pathlib
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
        self.pid = pid
        self.returncode = None
        self.terminated = 0

    def poll(self):
        return self.returncode

    def send_signal(self, _signal):
        self.terminated += 1
        self.returncode = 0

    def wait(self, timeout=None):
        del timeout
        if self.returncode is None:
            self.returncode = 0
        return self.returncode

    def kill(self):
        self.terminated += 1
        self.returncode = -9


class FakeJobApi:
    """Records the exact hard-close ownership operations."""

    def __init__(self, *, assign_ok: bool = True):
        self.assign_ok = assign_ok
        self.created = 0
        self.configured = 0
        self.assigned: list[tuple[int, int]] = []
        self.closed: list[int] = []

    def create_kill_on_close_job(self):
        self.created += 1
        return 731

    def assign_pid(self, handle, pid):
        self.assigned.append((handle, pid))
        return self.assign_ok

    def close_handle(self, handle):
        self.closed.append(handle)


class Phase184ProfileAcceptanceOrchestratorTests(unittest.TestCase):
    """Lock command, ownership, configuration, and evidence boundaries."""

    @staticmethod
    def _write_reusable_runtime_selection(
        repository: pathlib.Path,
        *,
        distro: str = "jazzy",
        default_rmw: str = "rmw_fastrtps_cpp",
    ) -> tuple[str, str]:
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

    def test_current_unity_runtime_is_reused_only_for_an_exact_default_selection(self):
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

    def test_command_arrays_are_direct_and_carry_only_owned_state(self):
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
        module = load_module()
        events = []
        config = {
            "outputRoot": str(
                ROOT / "build" / "phase184" / "acceptance" / "serial-workers"
            )
        }
        runtime = mock.Mock(
            toolchain=mock.Mock(python_executable=pathlib.Path(r"C:\ros\python.exe")),
            actor_environment={"ROS_DOMAIN_ID": "184"},
            peer_runtime_workspace=ROOT / "build" / "phase181" / "peer",
        )

        def launch(role, *_args, **_kwargs):
            events.append(("launch", role))
            return FakeProcess()

        def wait(_config, roles, _owner):
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

    def test_peer_graph_auditor_validates_raw_rclpy_snapshot(self):
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

        with mock.patch.object(module, "write_actor_ready") as ready:
            with mock.patch.object(
                module,
                "_wait_for_peer_result_document",
                return_value=peer_result,
            ):
                with mock.patch.object(module, "wait_for_terminal_marker"):
                    evidence = module._run_peer_graph_auditor(config)

        ready.assert_called_once()
        self.assertTrue(evidence["endpointsObserved"])
        self.assertTrue(evidence["qosMatches"])
        self.assertEqual(
            ["/unity2foxglove_ros2_bridge", "/unity_native"],
            evidence["nodeIdentities"],
        )
        self.assertEqual(graphs, evidence["topics"])

    def test_run_config_is_immutable_case_specific_and_protocol_valid(self):
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
        module = load_module()
        source = {
            "PATH": r"C:\host",
            "AMENT_PREFIX_PATH": r"C:\ambient",
            "ROS_DOMAIN_ID": "1",
            "ROS_DISCOVERY_SERVER": "ambient",
            "ZENOH_SESSION_CONFIG_URI": "ambient",
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
            discovery_range="LOCALHOST",
            topology_id="",
            zenoh_session_config=None,
        )
        self.assertEqual(
            [str(bridge), str(peer), str(ros)],
            environment["AMENT_PREFIX_PATH"].split(os.pathsep),
        )
        self.assertEqual("84", environment["ROS_DOMAIN_ID"])
        self.assertEqual("LOCALHOST", environment["ROS_AUTOMATIC_DISCOVERY_RANGE"])
        self.assertNotIn("ROS_DISCOVERY_SERVER", environment)
        self.assertNotIn("ZENOH_SESSION_CONFIG_URI", environment)

    def test_zenoh_router_uses_the_exact_unity_project_endpoint(self):
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

    def test_stream_peer_waits_for_transport_graph_before_production(self):
        module = load_module()
        source = inspect.getsource(module._run_stream_peer)

        self.assertLess(
            source.index("_wait_for_stream_subscription"),
            source.index("offered = 1280"),
        )

    def test_stream_production_gate_requires_exact_external_subscription(self):
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

    def test_manual_editor_log_rescue_scan_is_rate_bounded(self):
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
        module = load_module()
        physical = ROOT / "build" / "phase184" / "long-workspace"
        alias = pathlib.Path(r"X:\phase184")
        self.assertTrue(module._paths_are_distinct(alias, physical))
        self.assertFalse(module._paths_are_distinct(physical, physical))

    def test_windows_bridge_runtime_encloses_ros_initialization_and_shutdown(self):
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

    def test_bridge_health_preflight_is_disposable_before_unity(self):
        module = load_module()
        process = FakeProcess(18404)
        owner = mock.Mock()
        owner.process.return_value = None
        owner.stop.return_value = 0
        runtime = mock.Mock()
        runtime.bridge_install = ROOT / "build" / "bridge-overlay"
        runtime.bridge_runtime_workspace = ROOT
        runtime.actor_environment = {}
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

        def launch(*args, **kwargs):
            del args, kwargs
            events.append("launch")
            return process

        def wait(*args, **kwargs):
            del args, kwargs
            events.append("health")
            return health

        def stop(role):
            events.append("stop")
            self.assertEqual("bridge-health", role)
            return 0

        def port(port, label):
            events.append("port")
            self.assertEqual(18767, port)
            self.assertEqual("Bridge", label)
            return port

        owner.stop.side_effect = stop
        with mock.patch.object(
            module,
            "_require_file",
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
            "require_available_loopback_port",
            side_effect=port,
        ):
            evidence = module._preflight_bridge_health(
                config=config,
                output=TEST_ROOT,
                runtime=runtime,
                owner=owner,
                streams=[],
            )

        self.assertEqual(["launch", "health", "stop", "port"], events)
        self.assertEqual(health, evidence["response"])
        self.assertEqual(0, evidence["processExitCode"])
        self.assertTrue(evidence["portReleased"])

    def test_bridge_cases_start_actual_sidecar_only_after_native_marker(self):
        source = (
            ROOT / "Scripts" / "smoke" / "foxrun" / "phase184_profile_acceptance.py"
        ).read_text(encoding="utf-8")
        module = load_module()
        deferred = {
            case
            for case in module.protocol.CASE_CONTRACTS
            if module._requires_deferred_bridge_start({"case": case})
        }
        self.assertEqual({"multi-target", "qos-contract"}, deferred)

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
        health_preflight = actors.index("_preflight_bridge_health(")
        worker_launch = actors.index("_start_case_workers_serially(")
        self.assertLess(health_preflight, worker_launch)
        self.assertIn("defer_bridge", actors)

        unity_launch = batch.index("unity = _launch_logged_process(")
        native_gate = batch.index("_wait_for_deferred_bridge_gate(config)")
        bridge_launch = batch.index("_start_bridge_actor(", native_gate)
        self.assertLess(unity_launch, native_gate)
        self.assertLess(native_gate, bridge_launch)

        actual_bridge = source[
            source.index("def _start_bridge_actor(") : source.index(
                "def _start_case_actors(",
                source.index("def _start_bridge_actor("),
            )
        ]
        self.assertIn("wait_for_bridge_listening(", actual_bridge)
        self.assertNotIn("wait_for_bridge_health(", actual_bridge)

    def test_manual_bridge_starts_actual_sidecar_only_after_native_marker(self):
        source = (
            ROOT / "Scripts" / "smoke" / "foxrun" / "phase184_profile_acceptance.py"
        ).read_text(encoding="utf-8")
        manual = source[
            source.index("def run_manual_parent(") : source.index(
                "def run_batch_parent(",
                source.index("def run_manual_parent("),
            )
        ]

        play_wait = manual.index("_wait_for_manual_session(")
        self.assertIn("deferred_bridge_start", manual)
        self.assertIn("_start_bridge_actor(", manual[:play_wait])

    def test_actual_bridge_readiness_reads_owned_log_without_connecting(self):
        module = load_module()
        TEST_ROOT.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix="bridge-ready-", dir=TEST_ROOT) as raw:
            log = pathlib.Path(raw) / "bridge.log"
            log.write_text(
                "[INFO] [unity2foxglove_ros2_bridge] listening on 127.0.0.1:18767\n",
                encoding="utf-8",
            )
            process = FakeProcess(18405)
            config = {"bridgeHost": "127.0.0.1", "bridgePort": 18767}
            with mock.patch.object(module.socket, "create_connection") as connect:
                ready = module.wait_for_bridge_listening(
                    config,
                    process,
                    log,
                    timeout_seconds=0.1,
                )

        connect.assert_not_called()
        self.assertEqual("127.0.0.1", ready["host"])
        self.assertEqual(18767, ready["port"])

    def test_deferred_bridge_gate_starts_native_deadline_after_unity_context(self):
        module = load_module()
        calls = []
        config = {
            "case": "multi-target",
            "token": "p184g_A1b2C3d4E5f6",
        }
        with mock.patch.object(
            module,
            "_wait_for_unity_context",
            side_effect=lambda value: calls.append(("context", value)),
        ), mock.patch.object(
            module,
            "wait_for_log_marker",
            side_effect=lambda value, marker, timeout: calls.append(
                ("marker", value, marker, timeout)
            ),
        ):
            module._wait_for_deferred_bridge_gate(config)

        self.assertEqual(
            [
                ("context", config),
                ("marker", config, module._DEFERRED_BRIDGE_START_MARKER, 120.0),
            ],
            calls,
        )

    def test_unity_routes_emit_native_gate_before_full_bridge_readiness(self):
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

    def test_bridge_health_frame_is_correlated_and_strict(self):
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
        module = load_module()
        TEST_ROOT.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix="atomic-", dir=TEST_ROOT) as raw:
            target = pathlib.Path(raw) / "run-config.json"
            module.write_private_json_atomic(target, {"value": 184})
            self.assertEqual({"value": 184}, json.loads(target.read_text(encoding="utf-8")))
            self.assertEqual([], list(target.parent.glob("*.tmp")))

    def test_bridge_parser_requires_the_exact_qos_profile(self):
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
                                    key: value
                                    for key, value in qos.items()
                                    if key != "profile"
                                },
                                {
                                    key: value
                                    for key, value in qos.items()
                                    if key != "profile"
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
        module = load_module()
        terminal = module.TerminalMarker(
            "PASS",
            "PHASE184G_CASE_PASS",
            {
                "received": "792",
                "accepted": "792",
                "drained": "760",
                "replaced": "32",
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
        module = load_module()
        config = {"case": "foxglove-profile"}

        async def fail_unexpectedly(_config):
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
