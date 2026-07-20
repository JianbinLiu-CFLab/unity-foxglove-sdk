#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Regression checks for the executable Phase181 custom ROS2 peer harness."""

from __future__ import annotations

import contextlib
import importlib.util
import io
import json
import pathlib
import sys
import unittest
from types import SimpleNamespace

from Scripts.test_support.phase181_scratch import temporary_directory


ROOT = pathlib.Path(__file__).resolve().parents[4]
PEER_PATH = ROOT / "Scripts" / "smoke" / "ros2" / "phase181_custom_ros2_peer.py"


def load_peer_module():
    """Load the Phase181 module under test."""
    script_directory = str(PEER_PATH.parent)
    if script_directory not in sys.path:
        sys.path.insert(0, script_directory)
    spec = importlib.util.spec_from_file_location("phase181_custom_ros2_peer", PEER_PATH)
    if spec is None or spec.loader is None:
        raise RuntimeError("Could not load the Phase181 custom ROS2 peer module.")
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


class Phase181CustomRos2PeerTests(unittest.TestCase):
    """Verify peer setup cannot turn a partial observation into a PASS."""

    def test_peer_build_has_a_separate_bounded_window_from_unity_readiness(self):
        """Verify Phase181 behavior: a cold rosidl build cannot consume the Unity readiness window."""
        peer = load_peer_module()

        self.assertEqual(600.0, peer.peer_build_timeout_seconds())
        self.assertGreater(peer.peer_build_timeout_seconds(), 300.0)

    def test_static_interface_lock_requires_identity_and_full_digest(self):
        """Verify Phase181 behavior: static interface lock requires identity and full digest."""
        peer = load_peer_module()
        static_package = ROOT / "Packages" / "dev.unity2foxglove.foxrun.ros2.interfaces"
        lock = peer.load_static_interface_lock(static_package)

        self.assertEqual("unity2foxglove_foxrun_interfaces_v1", lock.ros_package_name)
        self.assertEqual(1, lock.interface_revision)
        self.assertRegex(lock.interface_digest, r"^[0-9a-f]{64}$")
        self.assertEqual("Phase181State48D288ED82F1Envelope", lock.envelope_message_name)
        self.assertEqual(lock.interface_digest, peer.compute_static_source_digest(static_package))

    def test_stage_source_copies_only_the_locked_ros_package_into_owned_workspace(self):
        """Verify Phase181 behavior: stage source copies only the locked ros package into owned workspace."""
        peer = load_peer_module()
        with temporary_directory("peer-") as temporary:
            root = pathlib.Path(temporary)
            static_package = root / "static"
            source = static_package / "Ros2Package~"
            (source / "msg").mkdir(parents=True)
            (source / "msg" / "State.msg").write_text("int32 count\n", encoding="utf-8")
            (source / "package.xml").write_text("<package/>\n", encoding="utf-8")
            (source / "CMakeLists.txt").write_text("cmake_minimum_required(VERSION 3.8)\n", encoding="utf-8")
            workspace = root / "peer-workspace"

            destination = peer.stage_locked_ros_source(static_package, workspace, "example_interfaces")

            self.assertEqual(workspace / "src" / "example_interfaces", destination)
            self.assertEqual("int32 count\n", (destination / "msg" / "State.msg").read_text(encoding="utf-8"))
            self.assertFalse((workspace / "Ros2Package~").exists())
            with self.assertRaisesRegex(peer.PeerFailure, "FAIL_PEER_SOURCE"):
                peer.stage_locked_ros_source(static_package, workspace, "example_interfaces")

    def test_worker_command_uses_pinned_python_and_never_a_bare_ros2_executable(self):
        """Verify Phase181 behavior: worker command uses pinned python and never a bare ros2 executable."""
        peer = load_peer_module()
        command = peer.build_worker_command(
            pathlib.Path("C:/ros2/.pixi/envs/default/python.exe"),
            role="windows-local-editor",
            surface="player",
            workspace=pathlib.Path("C:/temp/peer-workspace"),
            interface_digest="a" * 64,
            token="opaque-local-token",
        )

        self.assertEqual(str(pathlib.Path("C:/ros2/.pixi/envs/default/python.exe")), command[0])
        self.assertIn("--worker", command)
        self.assertNotIn("ros2", command)
        self.assertIn("--interface-digest", command)

    def test_peer_environment_adds_only_owned_workspace_and_explicit_ros_values(self):
        """Verify Phase181 behavior: peer environment adds only owned workspace and explicit ros values."""
        peer = load_peer_module()
        environment = peer.build_peer_environment(
            {"PATH": "base", "TOKEN": "do-not-inherit"},
            pathlib.Path("C:/ros2"),
            pathlib.Path("C:/owned/install"),
            distro="lyrical",
            rmw="rmw_zenoh_cpp",
            domain_id=17,
            topology_id="phase181-test-router",
        )

        self.assertNotIn("TOKEN", environment)
        self.assertEqual("lyrical", environment["ROS_DISTRO"])
        self.assertEqual("rmw_zenoh_cpp", environment["RMW_IMPLEMENTATION"])
        self.assertEqual("17", environment["ROS_DOMAIN_ID"])
        self.assertEqual("phase181-test-router", environment["UNITY2FOXGLOVE_ZENOH_TOPOLOGY_ID"])
        self.assertIn(str(pathlib.Path("C:/owned/install")), environment["AMENT_PREFIX_PATH"])

    def test_player_environment_contains_only_explicit_safe_profile_identity(self):
        """Verify Phase181 behavior: player environment contains only explicit safe profile identity."""
        peer = load_peer_module()
        environment = peer.build_player_environment(
            {"PATH": "base", "TOKEN": "do-not-inherit", "ROS_DISTRO": "wrong"},
            distro="lyrical",
            rmw="rmw_zenoh_cpp",
            domain_id=17,
            interface_revision=1,
            interface_digest="a" * 64,
            topology_id="phase181-test-router",
        )

        self.assertNotIn("TOKEN", environment)
        self.assertEqual("lyrical", environment["ROS_DISTRO"])
        self.assertEqual("rmw_zenoh_cpp", environment["RMW_IMPLEMENTATION"])
        self.assertEqual("17", environment["ROS_DOMAIN_ID"])
        self.assertEqual("SUBNET", environment["ROS_AUTOMATIC_DISCOVERY_RANGE"])
        self.assertEqual("1", environment["UNITY2FOXGLOVE_FOXRUN_INTERFACE_REVISION"])
        self.assertEqual("a" * 64, environment["UNITY2FOXGLOVE_FOXRUN_INTERFACE_DIGEST"])
        self.assertEqual("phase181-test-router", environment["UNITY2FOXGLOVE_ZENOH_TOPOLOGY_ID"])

    def test_player_command_uses_a_generated_token_and_bounded_auto_quit(self):
        """Verify Phase181 behavior: player command uses a generated token and bounded auto quit."""
        peer = load_peer_module()
        command = peer.build_player_command(
            pathlib.Path("C:/build/Phase181FoxRunCustomRos2Interface.exe"),
            pathlib.Path("C:/build/player.log"),
            "phase181-player-token",
            450.0,
        )

        self.assertEqual(str(pathlib.Path("C:/build/Phase181FoxRunCustomRos2Interface.exe")), command[0])
        self.assertIn("--phase181-custom-ros2-player-auto-quit", command)
        self.assertIn("--phase181-custom-ros2-token", command)
        self.assertIn("phase181-player-token", command)
        self.assertIn("450", command)
        self.assertNotIn("ros2", command)

    def test_editor_batch_command_runs_the_probe_without_automatic_quit(self):
        """Verify Phase181 behavior: Editor Batch owns the probe lifetime rather than Unity's generic quit switch."""
        peer = load_peer_module()
        command = peer.build_editor_batch_command(
            pathlib.Path("C:/Program Files/Unity/Hub/Editor/6000.3.14f1/Editor/Unity.exe"),
            pathlib.Path("C:/repo/Unity2Foxglove"),
            pathlib.Path("C:/repo/build/phase181/lyrical-fastrtps/unity-editor-batch.log"),
        )

        self.assertEqual(
            str(pathlib.Path("C:/Program Files/Unity/Hub/Editor/6000.3.14f1/Editor/Unity.exe")),
            command[0],
        )
        self.assertEqual(str(pathlib.Path("C:/repo/Unity2Foxglove")), command[command.index("-projectPath") + 1])
        self.assertIn("-batchmode", command)
        self.assertIn("-nographics", command)
        self.assertEqual(
            "Phase181BatchModeCustomRos2InteropProbe.Run",
            command[command.index("-executeMethod") + 1],
        )
        self.assertEqual(
            str(pathlib.Path("C:/repo/build/phase181/lyrical-fastrtps/unity-editor-batch.log")),
            command[command.index("-logFile") + 1],
        )
        self.assertNotIn("-quit", command)

    def test_editor_batch_is_an_explicit_opt_in_with_an_editor_path(self):
        """Verify Phase181 behavior: a named profile can opt into an owned Editor Batch launch."""
        peer = load_peer_module()
        args = peer.parse_args(
            [
                "--role",
                "windows-local-editor",
                "--unity-batch",
                "--unity-editor",
                "C:/Program Files/Unity/Hub/Editor/6000.3.14f1/Editor/Unity.exe",
            ]
        )

        self.assertTrue(args.unity_batch)
        self.assertEqual(
            pathlib.Path("C:/Program Files/Unity/Hub/Editor/6000.3.14f1/Editor/Unity.exe"),
            args.unity_editor,
        )

    def test_editor_batch_environment_prioritizes_the_short_custom_plugin_alias(self):
        """Verify Phase181 behavior: the Windows loader sees custom and runtime native plugin directories before ROS paths."""
        peer = load_peer_module()
        custom_alias = pathlib.Path("Y:/")
        runtime_plugins = pathlib.Path("C:/repo/Packages/runtime/Runtime/Ros2ForUnity/Plugins/Windows/x86_64")

        environment = peer.build_editor_batch_environment(
            {"PATH": "C:/ros/bin", "RMW_IMPLEMENTATION": "rmw_fastrtps_cpp", "TOKEN": "not-forwarded"},
            runtime_plugins,
            custom_alias,
        )

        self.assertEqual(
            peer.os.pathsep.join((str(custom_alias), str(runtime_plugins), "C:/ros/bin")),
            environment["PATH"],
        )
        self.assertEqual("rmw_fastrtps_cpp", environment["RMW_IMPLEMENTATION"])
        self.assertNotIn("TOKEN", environment)

    def test_player_exit_code_is_never_inferred_from_peer_receipt(self):
        """Verify Phase181 behavior: player exit code is never inferred from peer receipt."""
        peer = load_peer_module()

        peer.require_player_exit_code(0)
        with self.assertRaisesRegex(peer.PeerFailure, "FAIL_PLAYER_EXIT"):
            peer.require_player_exit_code(2)

    def test_one_sided_graph_or_inspector_evidence_cannot_pass(self):
        """Verify Phase181 behavior: one sided graph or inspector evidence cannot pass."""
        peer = load_peer_module()
        graph_only = peer.classify_evidence(
            {
                "interfaceDigestMatches": True,
                "graphEvidence": True,
                "outboundObserved": True,
                "inboundApplied": False,
                "sameOriginDropped": False,
                "remoteOriginApplied": False,
                "unityTerminalPass": False,
                "cleanStop": False,
            }
        )
        inspector_only = peer.classify_evidence(
            {
                "interfaceDigestMatches": True,
                "graphEvidence": False,
                "outboundObserved": False,
                "inboundApplied": True,
                "sameOriginDropped": True,
                "remoteOriginApplied": True,
                "nullableEmptyObserved": True,
                "unityTerminalPass": True,
                "cleanStop": True,
            }
        )

        self.assertEqual("FAIL_REMOTE_APPLY", graph_only)
        self.assertEqual("FAIL_GRAPH_EVIDENCE", inspector_only)

    def test_full_evidence_requires_native_both_direction_and_origin_proof(self):
        """Verify Phase181 behavior: full evidence requires native both direction and origin proof."""
        peer = load_peer_module()
        verdict = peer.classify_evidence(
            {
                "interfaceDigestMatches": True,
                "graphEvidence": True,
                "outboundObserved": True,
                "inboundApplied": True,
                "sameOriginDropped": True,
                "remoteOriginApplied": True,
                "nullableEmptyObserved": True,
                "unityTerminalPass": True,
                "cleanStop": True,
            }
        )

        self.assertEqual("PASS", verdict)

    def test_live_completion_does_not_wait_for_the_finally_only_clean_stop_flag(self):
        """Verify Phase181 behavior: live completion does not wait for the finally only clean stop flag."""
        peer = load_peer_module()
        live_evidence = {
            "interfaceDigestMatches": True,
            "graphEvidence": True,
            "outboundObserved": True,
            "inboundApplied": True,
            "sameOriginDropped": True,
            "remoteOriginApplied": True,
            "nullableEmptyObserved": True,
            "unityTerminalPass": True,
            "cleanStop": False,
        }

        self.assertEqual("FAIL_CLEAN_STOP", peer.classify_evidence(live_evidence))
        self.assertTrue(peer.can_complete_live_evidence(live_evidence))

    def test_linux_peer_roles_require_only_the_directional_proof_they_own(self):
        """Verify Phase181 behavior: linux peer roles require only the directional proof they own."""
        peer = load_peer_module()
        base = {
            "interfaceDigestMatches": True,
            "graphEvidence": True,
            "outboundObserved": False,
            "inboundApplied": True,
            "sameOriginDropped": False,
            "remoteOriginApplied": False,
            "nullableEmptyObserved": False,
            "unityTerminalPass": True,
            "cleanStop": True,
        }

        self.assertEqual("PASS", peer.classify_evidence(base, "publisher"))
        self.assertEqual("FAIL_OUTBOUND_EVIDENCE", peer.classify_evidence(base, "subscriber"))
        self.assertEqual("FAIL_SAME_ORIGIN", peer.classify_evidence(base, "bidirectional"))
        self.assertEqual("FAIL_OUTBOUND_EVIDENCE", peer.classify_evidence(base, "orchestrate"))
        self.assertEqual("PASS", peer.classify_evidence(base, "correlate"))

    def test_worker_result_json_never_persists_the_correlation_token(self):
        """Verify Phase181 behavior: worker result json never persists the correlation token."""
        peer = load_peer_module()
        with temporary_directory("peer-") as temporary:
            destination = pathlib.Path(temporary) / "worker-result.json"
            peer.write_worker_result(
                destination,
                {"token": "private-correlation", "verdict": "PASS", "error": "token=private-correlation"},
            )
            text = destination.read_text(encoding="utf-8")
            result = json.loads(text)

        self.assertNotIn("private-correlation", text)
        self.assertEqual("redacted", result["token"])
        self.assertEqual("redacted", result["error"])

    def test_probe_payloads_preserve_correlated_and_nullable_empty_cases(self):
        """Verify Phase181 behavior: probe payloads preserve correlated and nullable empty cases."""
        peer = load_peer_module()

        correlated = peer.custom_payload_fields("opaque-local-token", null_empty=False)
        nullable_empty = peer.custom_payload_fields("opaque-local-token", null_empty=True)

        self.assertEqual("opaque-local-token", correlated["message"])
        self.assertTrue(correlated["has_nested"])
        self.assertEqual([181, 182, 183], correlated["values"])
        self.assertEqual("", nullable_empty["message"])
        self.assertTrue(nullable_empty["has_message"])
        self.assertEqual([], nullable_empty["bytes"])
        self.assertEqual([], nullable_empty["values"])
        self.assertFalse(nullable_empty["has_nested"])
        self.assertFalse(nullable_empty["has_optional_count"])
        self.assertFalse(nullable_empty["has_optional_text"])

    def test_final_bidirectional_probe_retries_until_unity_reports_the_remote_apply(self):
        """Verify Phase181 behavior: the final nullable probe is retried after the same-origin replay barrier."""
        peer = load_peer_module()

        self.assertFalse(
            peer.should_publish_final_bidirectional_probe(
                requires_bidirectional=True,
                same_origin_dropped=False,
                remote_origin_applied=False,
                now=10.0,
                next_publish_time=None,
            )
        )
        self.assertTrue(
            peer.should_publish_final_bidirectional_probe(
                requires_bidirectional=True,
                same_origin_dropped=True,
                remote_origin_applied=False,
                now=10.0,
                next_publish_time=None,
            )
        )
        self.assertFalse(
            peer.should_publish_final_bidirectional_probe(
                requires_bidirectional=True,
                same_origin_dropped=True,
                remote_origin_applied=False,
                now=10.5,
                next_publish_time=10.75,
            )
        )
        self.assertTrue(
            peer.should_publish_final_bidirectional_probe(
                requires_bidirectional=True,
                same_origin_dropped=True,
                remote_origin_applied=False,
                now=10.75,
                next_publish_time=10.75,
            )
        )
        self.assertFalse(
            peer.should_publish_final_bidirectional_probe(
                requires_bidirectional=True,
                same_origin_dropped=True,
                remote_origin_applied=True,
                now=11.0,
                next_publish_time=10.75,
            )
        )

    def test_owned_workspace_refuses_unmarked_or_outside_build_paths(self):
        """Verify Phase181 behavior: owned workspace refuses unmarked or outside build paths."""
        peer = load_peer_module()
        with temporary_directory("peer-") as temporary:
            root = pathlib.Path(temporary)
            build_root = root / "build" / "phase181"
            workspace = peer.prepare_owned_workspace(build_root, "jazzy-fastrtps")
            self.assertEqual(build_root / "jazzy-fastrtps" / "peer-workspace", workspace)
            self.assertTrue((workspace / peer.OWNERSHIP_MARKER_NAME).is_file())

            unsafe = build_root / "outside"
            unsafe.mkdir(parents=True)
            with self.assertRaisesRegex(peer.PeerFailure, "FAIL_PEER_WORKSPACE"):
                peer.cleanup_owned_workspace(unsafe, build_root)

    def test_colcon_command_pins_ninja_release_and_cmake_safe_pixi_python(self):
        """Verify Phase181 behavior: colcon receives the portable native-interface build contract."""
        peer = load_peer_module()
        command = peer.build_windows_colcon_command(
            pathlib.Path("C:/ros2/.pixi/envs/default/Scripts/colcon.exe"),
            "unity2foxglove_foxrun_interfaces_v1",
            pathlib.Path("C:/ros2/.pixi/envs/default/python.exe"),
        )

        self.assertEqual(str(pathlib.Path("C:/ros2/.pixi/envs/default/Scripts/colcon.exe")), command[0])
        self.assertEqual(
            [
                "build",
                "--merge-install",
                "--packages-select",
                "unity2foxglove_foxrun_interfaces_v1",
                "--cmake-args",
                "-G",
                "Ninja",
                "-DCMAKE_BUILD_TYPE=Release",
                "-DPython3_EXECUTABLE=C:/ros2/.pixi/envs/default/python.exe",
                "-DPYTHON_EXECUTABLE=C:/ros2/.pixi/envs/default/python.exe",
            ],
            command[1:],
        )

    def test_windows_peer_build_environment_preserves_ros_tools_and_enables_utf8_templates(self):
        """Verify Phase181 behavior: native build keeps ROS paths while adding only captured MSVC state."""
        peer = load_peer_module()

        environment = peer.merge_windows_peer_build_environment(
            {"PATH": "C:/ros/bin;C:/ros/pixi", "ROS_DISTRO": "lyrical", "TOKEN": "not-forwarded"},
            {"PATH": "C:/VS/bin;C:/Windows Kits/bin", "VisualStudioVersion": "18.0", "INCLUDE": "C:/VS/include"},
        )

        self.assertEqual(
            "C:/VS/bin;C:/Windows Kits/bin" + peer.os.pathsep + "C:/ros/bin;C:/ros/pixi",
            environment["PATH"],
        )
        self.assertEqual("18.0", environment["VisualStudioVersion"])
        self.assertEqual("C:/VS/include", environment["INCLUDE"])
        self.assertEqual("lyrical", environment["ROS_DISTRO"])
        self.assertEqual("1", environment["PYTHONUTF8"])
        self.assertNotIn("TOKEN", environment)

    def test_msvc_activator_command_keeps_humble_python310_fstrings_parseable(self):
        """Verify Phase181 behavior: the pinned Humble worker does not parse a backslash inside an f-string expression."""
        source = PEER_PATH.read_text(encoding="utf-8")

        self.assertIn('comspec = os.environ.get("ComSpec", r"C:\\Windows\\System32\\cmd.exe")', source)
        self.assertIn("f'\"{comspec}\" '", source)
        self.assertNotIn('f\'\"{os.environ.get("ComSpec", r"C:', source)

    def test_windows_peer_build_alias_is_reserved_for_projected_rosidl_path_overflow(self):
        """Verify Phase181 behavior: temporary drive aliases are limited to Windows paths that need them."""
        peer = load_peer_module()

        self.assertFalse(peer.requires_short_windows_peer_workspace_alias(pathlib.Path("C:/"), "nt"))
        self.assertTrue(
            peer.requires_short_windows_peer_workspace_alias(
                pathlib.Path("D:/BaiduSyncdisk/Obsidian Vault/Websocket/00 Inbox/build/phase181/lyrical-fastrtps/peer-workspace"),
                "nt",
            )
        )
        self.assertFalse(
            peer.requires_short_windows_peer_workspace_alias(
                pathlib.Path("D:/BaiduSyncdisk/Obsidian Vault/Websocket/00 Inbox/build/phase181/lyrical-fastrtps/peer-workspace"),
                "posix",
            )
        )

    def test_windows_toolchain_requires_pinned_python_ros2_and_colcon(self):
        """Verify Phase181 behavior: windows toolchain requires pinned python ros2 and colcon."""
        peer = load_peer_module()
        with temporary_directory("peer-") as temporary:
            ros2_root = pathlib.Path(temporary) / "ros2_humble"
            pixi = ros2_root / ".pixi" / "envs" / "default"
            scripts = ros2_root / "Scripts"
            pixi.mkdir(parents=True)
            scripts.mkdir(parents=True)
            (pixi / "python.exe").touch()
            (pixi / "Scripts").mkdir()
            (pixi / "Scripts" / "colcon.exe").touch()
            (scripts / "ros2-script.py").touch()

            toolchain = peer.resolve_windows_peer_toolchain(ros2_root)

        self.assertEqual(ros2_root, toolchain.ros2_root)
        self.assertEqual(pixi / "python.exe", toolchain.python_executable)
        self.assertEqual(pixi / "Scripts" / "colcon.exe", toolchain.colcon_executable)

    def test_worker_launch_uses_only_a_new_owned_process_group(self):
        """Verify Phase181 behavior: worker launch uses only a new owned process group."""
        peer = load_peer_module()

        windows = peer.worker_launch_options("nt")
        posix = peer.worker_launch_options("posix")

        self.assertIn("creationflags", windows)
        self.assertNotIn("start_new_session", windows)
        self.assertEqual({"start_new_session": True}, posix)

    def test_worker_result_requires_the_locked_full_digest_and_pass_verdict(self):
        """Verify Phase181 behavior: worker result requires the locked full digest and pass verdict."""
        peer = load_peer_module()
        with temporary_directory("peer-") as temporary:
            root = pathlib.Path(temporary)
            result_path = root / "worker-result.json"
            lock = peer.StaticInterfaceLock(
                ros_package_name="unity2foxglove_foxrun_interfaces_v1",
                interface_revision=1,
                interface_digest="a" * 64,
                payload_message_name="Phase181State48D288ED82F1",
                envelope_message_name="Phase181State48D288ED82F1Envelope",
            )
            result_path.write_text(
                json.dumps({"interfaceDigest": "b" * 64, "verdict": "PASS"}),
                encoding="utf-8",
            )

            with self.assertRaisesRegex(peer.PeerFailure, "FAIL_INTERFACE_DIGEST"):
                peer.read_successful_worker_result(result_path, lock)

            result_path.write_text(
                json.dumps({"interfaceDigest": "a" * 64, "verdict": "FAIL_REMOTE_APPLY"}),
                encoding="utf-8",
            )
            with self.assertRaisesRegex(peer.PeerFailure, "FAIL_REMOTE_APPLY"):
                peer.read_successful_worker_result(result_path, lock)

            result_path.write_text(
                json.dumps({"interfaceDigest": "a" * 64, "verdict": "PASS"}),
                encoding="utf-8",
            )
            self.assertEqual("PASS", peer.read_successful_worker_result(result_path, lock)["verdict"])

    def test_addon_validator_command_is_profile_pinned_and_uses_current_python(self):
        """Verify Phase181 behavior: addon validator command is profile pinned and uses current python."""
        peer = load_peer_module()
        command = peer.build_addon_validator_command(ROOT, "lyrical", "rmw_zenoh_cpp")

        self.assertEqual(sys.executable, command[0])
        self.assertIn("validate_foxrun_custom_typesupport_addon.py", command[1])
        self.assertEqual(["--distro", "lyrical", "--require-rmw", "rmw_zenoh_cpp"], command[2:])

    def test_unity_readiness_requires_matching_profile_and_locked_digest_prefix(self):
        """Verify Phase181 behavior: unity readiness requires matching profile and locked digest prefix."""
        peer = load_peer_module()
        lock = peer.StaticInterfaceLock(
            ros_package_name="unity2foxglove_foxrun_interfaces_v1",
            interface_revision=1,
            interface_digest="a" * 64,
            payload_message_name="Phase181State48D288ED82F1",
            envelope_message_name="Phase181State48D288ED82F1Envelope",
        )
        marker = peer.protocol.UnityMarker(
            "PHASE181_CUSTOM_ROS2_READY",
            {"token": "phase181-test", "runtime": "lyrical", "rmw": "rmw_zenoh_cpp"},
            "ready",
        )
        interface = peer.protocol.UnityMarker(
            "PHASE181_CUSTOM_INTERFACE_READY",
            {"token": "phase181-test", "digest": "a" * 12},
            "interface",
        )

        self.assertEqual(
            "phase181-test",
            peer.require_matching_unity_readiness(marker, interface, lock, "lyrical", "rmw_zenoh_cpp"),
        )

        wrong_digest = peer.protocol.UnityMarker(
            "PHASE181_CUSTOM_INTERFACE_READY",
            {"token": "phase181-test", "digest": "b" * 12},
            "interface",
        )
        with self.assertRaisesRegex(peer.PeerFailure, "FAIL_INTERFACE_DIGEST"):
            peer.require_matching_unity_readiness(marker, wrong_digest, lock, "lyrical", "rmw_zenoh_cpp")

        with self.assertRaisesRegex(peer.PeerFailure, "FAIL_RUNTIME_IDENTITY"):
            peer.require_matching_unity_readiness(marker, interface, lock, "jazzy", "rmw_zenoh_cpp")

    def test_peer_gives_apply_probes_a_fresh_timeout_after_unity_correlation(self):
        """Verify Phase181 behavior: peer gives apply probes a fresh timeout after unity correlation."""
        peer = load_peer_module()

        self.assertEqual(300.0, peer.worker_phase_deadline(None, 300.0, None))
        self.assertEqual(420.0, peer.worker_phase_deadline("phase181-test", 300.0, 420.0))
        with self.assertRaisesRegex(peer.PeerFailure, "FAIL_STATE_TRANSITION"):
            peer.worker_phase_deadline("phase181-test", 300.0, None)

    def test_outer_worker_command_carries_only_explicit_profile_and_log_inputs(self):
        """Verify Phase181 behavior: outer worker command carries only explicit profile and log inputs."""
        peer = load_peer_module()
        command = peer.build_worker_command(
            pathlib.Path("C:/ros2/.pixi/envs/default/python.exe"),
            role="windows-local-editor",
            surface="player",
            workspace=pathlib.Path("C:/build/peer-workspace"),
            interface_digest="a" * 64,
            token="phase181-peer-token",
            unity_log=pathlib.Path("C:/Unity/Editor.log"),
            result_json=pathlib.Path("C:/build/worker-result.json"),
            distro="jazzy",
            rmw="rmw_fastrtps_cpp",
            domain_id=27,
            unity_log_offset=91,
            static_interface_package=pathlib.Path("C:/repo/Packages/static"),
            ready_timeout_seconds=300.0,
            apply_timeout_seconds=120.0,
        )

        self.assertIn("--distro", command)
        self.assertIn("jazzy", command)
        self.assertIn("--rmw", command)
        self.assertIn("rmw_fastrtps_cpp", command)
        self.assertEqual("player", command[command.index("--surface") + 1])
        self.assertIn("--unity-log-offset", command)
        self.assertIn("91", command)
        self.assertIn("--static-interface-package", command)
        self.assertIn("--ready-timeout-seconds", command)
        self.assertNotIn("ros2", command)

    def test_logged_owned_command_uses_explicit_environment_and_never_a_shell(self):
        """Verify Phase181 behavior: logged owned command uses explicit environment and never a shell."""
        peer = load_peer_module()
        calls: list[tuple[list[str], dict[str, object]]] = []

        def runner(command, **kwargs):
            """Implement the Phase181 runner step."""
            calls.append((list(command), kwargs))
            return SimpleNamespace(returncode=0)

        with temporary_directory("peer-") as temporary:
            root = pathlib.Path(temporary)
            peer.run_logged_owned_command(
                ["colcon.exe", "build"],
                cwd=root,
                env={"PATH": "safe"},
                log_path=root / "colcon.log",
                timeout_seconds=5.0,
                failure_code="FAIL_PEER_BUILD",
                runner=runner,
            )

        self.assertEqual(["colcon.exe", "build"], calls[0][0])
        self.assertFalse(calls[0][1]["shell"])
        self.assertEqual({"PATH": "safe"}, calls[0][1]["env"])

    def test_logged_owned_command_maps_a_nonzero_exit_to_its_bounded_failure_code(self):
        """Verify Phase181 behavior: logged owned command maps a nonzero exit to its bounded failure code."""
        peer = load_peer_module()

        with temporary_directory("peer-") as temporary:
            root = pathlib.Path(temporary)
            with self.assertRaisesRegex(peer.PeerFailure, "FAIL_TYPESUPPORT_PREFLIGHT"):
                peer.run_logged_owned_command(
                    ["validator.py"],
                    cwd=root,
                    env={},
                    log_path=root / "validator.log",
                    timeout_seconds=5.0,
                    failure_code="FAIL_TYPESUPPORT_PREFLIGHT",
                    runner=lambda *args, **kwargs: SimpleNamespace(returncode=9),
                )

    def test_failure_log_archive_retains_only_named_owned_diagnostic(self):
        """Verify Phase181 behavior: cleanup may retain a bounded profile diagnostic for failures."""
        peer = load_peer_module()
        with temporary_directory("peer-") as temporary:
            root = pathlib.Path(temporary)
            workspace = root / "peer-workspace"
            output = root / "profile-output"
            workspace.mkdir()
            (workspace / "colcon-build.log").write_text("bounded build failure\n", encoding="utf-8")

            peer.preserve_failure_log(workspace, output, "colcon-build.log", "peer-build-failure.log")

            self.assertEqual(
                "bounded build failure\n",
                (output / "peer-build-failure.log").read_text(encoding="utf-8"),
            )
            self.assertFalse((output / "unexpected.log").exists())

    def test_failure_log_archive_retains_redacted_worker_result(self):
        """Verify Phase181 behavior: failed peers preserve their bounded result evidence."""
        peer = load_peer_module()
        with temporary_directory("peer-") as temporary:
            root = pathlib.Path(temporary)
            workspace = root / "peer-workspace"
            output = root / "profile-output"
            workspace.mkdir()
            expected = '{"verdict":"FAIL_GRAPH_EVIDENCE","token":"redacted"}\n'
            (workspace / "worker-result.json").write_text(expected, encoding="utf-8")

            peer.preserve_failure_log(
                workspace,
                output,
                "worker-result.json",
                "peer-worker-result-failure.json",
            )

            self.assertEqual(
                expected,
                (output / "peer-worker-result-failure.json").read_text(encoding="utf-8"),
            )

    def test_selected_typesupport_requires_exactly_one_matching_runtime_and_addon(self):
        """Verify Phase181 behavior: selected typesupport requires exactly one matching runtime and addon."""
        peer = load_peer_module()
        with temporary_directory("peer-") as temporary:
            root = pathlib.Path(temporary)
            manifest = root / "Unity2Foxglove" / "Packages" / "manifest.json"
            manifest.parent.mkdir(parents=True)
            manifest.write_text(
                json.dumps(
                    {
                        "dependencies": {
                            "dev.unity2foxglove.ros2forunity.runtime.lyrical.win64": "file:../../Packages/runtime",
                            "dev.unity2foxglove.foxrun.ros2.interfaces.typesupport.lyrical.win64": "file:../../Packages/addon",
                        }
                    }
                ),
                encoding="utf-8",
            )

            self.assertEqual(
                "dev.unity2foxglove.foxrun.ros2.interfaces.typesupport.lyrical.win64",
                peer.require_selected_typesupport_addon(root, "lyrical"),
            )

            manifest.write_text(
                json.dumps(
                    {
                        "dependencies": {
                            "dev.unity2foxglove.ros2forunity.runtime.lyrical.win64": "file:../../Packages/runtime",
                            "dev.unity2foxglove.foxrun.ros2.interfaces.typesupport.jazzy.win64": "file:../../Packages/wrong",
                        }
                    }
                ),
                encoding="utf-8",
            )
            with self.assertRaisesRegex(peer.PeerFailure, "FAIL_TYPESUPPORT_SELECTION"):
                peer.require_selected_typesupport_addon(root, "lyrical")

    def test_graph_endpoint_evidence_requires_matching_type_external_owner_and_reliability(self):
        """Verify Phase181 behavior: graph endpoint evidence requires matching type external owner and reliability."""
        peer = load_peer_module()

        class Qos:
            """Provide a lightweight Phase181 test double for Qos."""
            reliability = 1

        class Endpoint:
            """Provide a lightweight Phase181 test double for Endpoint."""
            topic_type = "example/msg/Envelope"
            node_name = "unity"
            qos_profile = Qos()

        self.assertTrue(
            peer.external_endpoint_has_reliability(
                [Endpoint()],
                "phase181_peer",
                "example/msg/Envelope",
                1,
            )
        )
        self.assertFalse(
            peer.external_endpoint_has_reliability(
                [Endpoint()],
                "unity",
                "example/msg/Envelope",
                1,
            )
        )
        self.assertFalse(
            peer.external_endpoint_has_reliability(
                [Endpoint()],
                "phase181_peer",
                "example/msg/Envelope",
                2,
            )
        )

    def test_graph_evidence_reports_each_required_direction_without_endpoint_identity(self):
        """Verify Phase181 behavior: graph failures retain bounded per-direction evidence."""
        peer = load_peer_module()

        class Qos:
            """Provide a lightweight Phase181 test double for Qos."""
            reliability = 1

        class Endpoint:
            """Provide a lightweight Phase181 test double for Endpoint."""
            topic_type = "example/msg/Envelope"
            node_name = "unity"
            qos_profile = Qos()

        checks = peer.evaluate_graph_evidence(
            [Endpoint()],
            [Endpoint()],
            [Endpoint()],
            [Endpoint()],
            "phase181_peer",
            "example/msg/Envelope",
            1,
            True,
            True,
        )

        self.assertEqual(
            {
                "subscribeReliable": True,
                "publishPublisher": True,
                "bidirectionalPublisher": True,
                "bidirectionalReliable": True,
            },
            checks,
        )

    def test_graph_endpoint_summary_distinguishes_type_mismatch_from_reliability_mismatch(self):
        """Verify Phase181 behavior: diagnostic endpoint counts expose no identities or raw endpoint data."""
        peer = load_peer_module()

        class Qos:
            """Provide a lightweight Phase181 test double for Qos."""
            def __init__(self, reliability):
                self.reliability = reliability

        class Endpoint:
            """Provide a lightweight Phase181 test double for Endpoint."""
            def __init__(self, topic_type, node_name, reliability):
                self.topic_type = topic_type
                self.node_name = node_name
                self.qos_profile = Qos(reliability)

        summary = peer.summarize_graph_endpoints(
            [
                Endpoint("wrong/msg/Envelope", "unity", 1),
                Endpoint("example/msg/Envelope", "phase181_peer", 1),
                Endpoint("example/msg/Envelope", "unity", 2),
            ],
            "phase181_peer",
            "example/msg/Envelope",
            1,
        )

        self.assertEqual(
            {
                "total": 3,
                "matchingType": 2,
                "externalMatchingType": 1,
                "externalMatchingReliable": 0,
            },
            summary,
        )

    def test_graph_observation_survives_unity_endpoint_teardown_in_the_same_run(self):
        """Verify Phase181 behavior: a post-stop graph query cannot erase live endpoint evidence."""
        peer = load_peer_module()

        observed = peer.merge_graph_observations(
            {},
            {
                "subscribeReliable": True,
                "publishPublisher": True,
                "bidirectionalPublisher": True,
                "bidirectionalReliable": True,
            },
        )
        after_unity_exit = peer.merge_graph_observations(
            observed,
            {
                "subscribeReliable": False,
                "publishPublisher": False,
                "bidirectionalPublisher": False,
                "bidirectionalReliable": False,
            },
        )

        self.assertEqual(observed, after_unity_exit)
    def test_peer_never_mistakes_its_remote_final_origin_for_a_unity_echo(self):
        """Verify Phase181 behavior: peer never mistakes its remote final origin for a unity echo."""
        peer = load_peer_module()
        token = "phase181-test"

        self.assertTrue(peer.is_peer_remote_origin("remote-" + token, token))
        self.assertTrue(peer.is_peer_remote_origin("remote-final-" + token, token))
        self.assertFalse(peer.is_peer_remote_origin("unity-origin-123", token))

    def test_typed_worker_endpoint_setup_disposes_a_partially_created_node_on_failure(self):
        """Verify Phase181 behavior: typed worker endpoint setup disposes a partially created node on failure."""
        peer = load_peer_module()

        class FakeNode:
            """Provide a lightweight Phase181 test double for FakeNode."""
            def __init__(self):
                """Initialize the lightweight Phase181 test double."""
                self.destroy_calls = 0

            def create_subscription(self, *args):
                """Implement the Phase181 create subscription step."""
                raise RuntimeError("subscription setup failed")

            def destroy_node(self):
                """Implement the Phase181 destroy node step."""
                self.destroy_calls += 1

        class FakeRclpy:
            """Provide a lightweight Phase181 test double for FakeRclpy."""
            def __init__(self, node):
                """Initialize the lightweight Phase181 test double."""
                self.node = node

            def create_node(self, name):
                """Implement the Phase181 create node step."""
                self.name = name
                return self.node

        node = FakeNode()
        with self.assertRaisesRegex(peer.PeerFailure, "FAIL_PEER_RUNTIME"):
            peer.create_typed_worker_endpoints(
                FakeRclpy(node),
                object(),
                "phase181-worker",
                object(),
                "/phase181/publish",
                "/phase181/subscribe",
                "/phase181/bidirectional",
            )

        self.assertEqual(1, node.destroy_calls)

    def test_post_stop_observer_rejects_a_late_correlated_unity_apply_marker(self):
        """Verify Phase181 behavior: post stop observer rejects a late correlated unity apply marker."""
        peer = load_peer_module()
        with temporary_directory("peer-") as temporary:
            log = pathlib.Path(temporary) / "unity.log"
            log.write_text("before stop\n", encoding="utf-8")
            offset = peer.protocol.log_offset(log)
            with log.open("a", encoding="utf-8") as stream:
                stream.write(
                    "PHASE181_CUSTOM_ROS2_APPLIED token=phase181-stop-check "
                    "topic=/unity2foxglove/phase181/custom/subscribe\n"
                )

            clean_stop, end_offset = peer.observe_no_late_unity_apply(
                log,
                offset,
                "phase181-stop-check",
                observation_seconds=0.0,
            )

        self.assertFalse(clean_stop)
        self.assertGreater(end_offset, offset)

    def test_worker_main_writes_a_bounded_result_for_an_unhandled_runtime_setup_error(self):
        """Verify Phase181 behavior: worker main writes a bounded result for an unhandled runtime setup error."""
        peer = load_peer_module()
        with temporary_directory("peer-") as temporary:
            result_path = pathlib.Path(temporary) / "worker-result.json"
            args = SimpleNamespace(worker_result_json=result_path)
            original = peer.run_typed_worker
            try:
                def fail_runtime(_):
                    """Implement the Phase181 fail runtime step."""
                    raise RuntimeError("unbounded native setup detail")

                peer.run_typed_worker = fail_runtime
                with contextlib.redirect_stderr(io.StringIO()):
                    self.assertEqual(1, peer.worker_main(args))
            finally:
                peer.run_typed_worker = original

            result = json.loads(result_path.read_text(encoding="utf-8"))
            self.assertEqual("FAIL_PEER_RUNTIME", result["verdict"])
            self.assertEqual("redacted", result["error"])


if __name__ == "__main__":
    unittest.main()
