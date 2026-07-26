#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Regression checks for the owned Phase184-G acceptance orchestrator."""

from __future__ import annotations

import importlib.util
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
            pathlib.Path(r"C:\ros\ros2.exe"),
            "127.0.0.1",
            18767,
        )
        self.assertEqual(
            [
                r"C:\ros\ros2.exe",
                "run",
                "unity2foxglove_ros2_bridge",
                "unity2foxglove_ros2_bridge",
                "--host",
                "127.0.0.1",
                "--port",
                "18767",
                "--payload-format",
                "cdr-with-encapsulation",
            ],
            bridge,
        )

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
        results = {
            "ros2-peer": {
                "verdict": "PASS",
                "evidence": {
                    "deliveryByTopic": {
                        topic: ["native-gid-" + str(index), "bridge-gid-" + str(index)]
                        for index, topic in enumerate(config["topics"])
                    }
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
                    "publisherGids": ["native-gid", "bridge-gid"],
                    "transportObservedQos": {
                        topic: {"publishers": [dict(qos), dict(qos)]}
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

    def test_stream_peer_evidence_must_match_unity_and_nominal_rate(self):
        module = load_module()
        terminal = module.TerminalMarker(
            "PASS",
            "PHASE184G_CASE_PASS",
            {
                "offered": "1280",
                "accepted": "1000",
                "drained": "968",
                "replaced": "32",
                "rateDropped": "280",
                "highWater": "32",
                "disposalFailures": "0",
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

        with self.assertRaisesRegex(module.AcceptanceFailure, "FAIL_STREAM"):
            module._validated_stream_evidence(
                terminal,
                {
                    "offered": 1279,
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
