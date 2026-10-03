#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Regression checks for the executable Phase181 custom ROS2 peer harness."""
from __future__ import annotations
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "test_phase181_custom_ros2_peer.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
import contextlib
import importlib.util
import io
import json
import pathlib
import subprocess
import sys
import tempfile
import unittest
from types import SimpleNamespace
from unittest import mock
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
class _Phase181CustomRos2PeerTests_support:
    """Decomposed Phase192 implementation component."""
    def test_peer_build_has_a_separate_bounded_window_from_unity_readiness(self):
        """Verify Phase181 behavior: a cold rosidl build cannot consume the Unity readiness window."""
        peer = load_peer_module()

        self.assertEqual(1800.0, peer.peer_build_timeout_seconds())
        self.assertGreater(peer.peer_build_timeout_seconds(), 300.0)
    def test_streamed_build_watchdog_measures_silence_not_total_elapsed_time(self):
        """Verify Phase181 behavior: a long cold build remains healthy whenever it continues to emit progress."""
        peer = load_peer_module()
        self.assertTrue(hasattr(peer, "stream_output_is_stalled"))

        self.assertFalse(peer.stream_output_is_stalled(900.0, 900.0, 1800.0))
        self.assertFalse(peer.stream_output_is_stalled(0.0, 1799.0, 1800.0))
        self.assertTrue(peer.stream_output_is_stalled(0.0, 1801.0, 1800.0))
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
    def test_static_interface_digest_rejects_unstaged_artifact_drift(self):
        """Extra files must invalidate the complete generated-package lock."""
        peer = load_peer_module()
        with temporary_directory("peer-digest-") as temporary:
            package = pathlib.Path(temporary) / "static"
            source = package / "Ros2Package~"
            source.mkdir(parents=True)
            (package / "package.json").write_text("{}\n", encoding="utf-8")
            (source / "package.xml").write_text("<package/>\n", encoding="utf-8")
            clean_digest = peer.compute_static_source_digest(package)
            artifact = source / "build" / "generated.txt"
            artifact.parent.mkdir()
            artifact.write_text("non-source artifact\n", encoding="utf-8")

            dirty_digest = peer.compute_static_source_digest(package)

        self.assertNotEqual(clean_digest, dirty_digest)
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
    def test_peer_and_player_environments_reject_invalid_domain_ids(self):
        """Both halves of the interop pair share one exact ROS domain boundary."""
        peer = load_peer_module()
        for invalid in (-1, 233, True, "17"):
            with self.subTest(invalid=invalid):
                with self.assertRaisesRegex(peer.PeerFailure, "FAIL_ARGUMENTS"):
                    peer.build_peer_environment(
                        {"PATH": "base"},
                        pathlib.Path("C:/ros2"),
                        pathlib.Path("C:/owned/install"),
                        distro="lyrical",
                        rmw="rmw_zenoh_cpp",
                        domain_id=invalid,
                    )
                with self.assertRaisesRegex(peer.PeerFailure, "FAIL_ARGUMENTS"):
                    peer.build_player_environment(
                        {"PATH": "base"},
                        distro="lyrical",
                        rmw="rmw_zenoh_cpp",
                        domain_id=invalid,
                        interface_revision=1,
                        interface_digest="a" * 64,
                    )
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
    def test_explicit_zenoh_session_config_replaces_ambient_router_settings_for_peer_and_unity(self):
        """Verify Phase181 behavior: an owned Zenoh config is forwarded explicitly, never inherited ambiently."""

        peer = load_peer_module()
        with tempfile.TemporaryDirectory() as temporary:
            session_config = pathlib.Path(temporary) / "owned-zenoh-session-config.json5"
            session_config.write_text('{ connect: { endpoints: ["tcp/127.0.0.1:45678"] } }\n', encoding="utf-8")
            source = {
                "PATH": "base",
                "ZENOH_ROUTER_CONFIG_URI": "C:/ambient/router.json5",
                "ZENOH_SESSION_CONFIG_URI": "C:/ambient/session.json5",
                "ZENOH_CONFIG_OVERRIDE": "connect/endpoints=[\"tcp/ambient:7447\"]",
            }
            peer_environment = peer.build_peer_environment(
                source,
                pathlib.Path("C:/ros2"),
                pathlib.Path("C:/owned/install"),
                distro="lyrical",
                rmw="rmw_zenoh_cpp",
                domain_id=17,
                topology_id="phase181-test-router",
                zenoh_session_config=session_config,
            )
            unity_environment = peer.build_player_environment(
                source,
                distro="lyrical",
                rmw="rmw_zenoh_cpp",
                domain_id=17,
                interface_revision=1,
                interface_digest="a" * 64,
                topology_id="phase181-test-router",
                zenoh_session_config=session_config,
            )

        for environment in (peer_environment, unity_environment):
            self.assertEqual(str(session_config.resolve()), environment["ZENOH_SESSION_CONFIG_URI"])
            self.assertNotIn("ZENOH_ROUTER_CONFIG_URI", environment)
            self.assertNotIn("ZENOH_CONFIG_OVERRIDE", environment)
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
    def test_runtime_selection_batch_command_uses_the_official_unity_selector(self):
        """Verify Phase181 behavior: an isolated row selects its runtime/add-on before launching the peer."""
        peer = load_peer_module()

        command = peer.build_runtime_selection_batch_command(
            pathlib.Path("C:/Program Files/Unity/Hub/Editor/6000.3.14f1/Editor/Unity.exe"),
            pathlib.Path("C:/repo/Unity2Foxglove"),
            pathlib.Path("C:/repo/build/phase181/humble-fastrtps/runtime-selection.log"),
            "humble",
            "rmw_fastrtps_cpp",
        )

        self.assertEqual(
            "Unity2Foxglove.Ros2ForUnity.Editor.Phase181Ros2RuntimeBatchSelection.SelectFromCommandLine",
            command[command.index("-executeMethod") + 1],
        )
        self.assertEqual("humble", command[command.index("-phase181Ros2Distro") + 1])
        self.assertEqual("fastdds", command[command.index("-phase181Ros2CommunicationMode") + 1])
        self.assertEqual(
            str(pathlib.Path("C:/repo/build/phase181/humble-fastrtps/runtime-selection.log")),
            command[command.index("-logFile") + 1],
        )
        self.assertNotIn("-quit", command)
    def test_runtime_selection_wait_accepts_continued_unity_log_progress_past_a_legacy_total_timeout(self):
        """Verify Phase181 behavior: active Unity compilation is not killed merely for exceeding a total selector duration."""
        peer = load_peer_module()
        self.assertTrue(hasattr(peer, "wait_for_runtime_selection_process"))

        class Process:
            """Stalled process double for streamed timeout cleanup."""
            """Provide one deterministic owned Unity process for the selection wait policy."""

            def __init__(self):
                """Schedule two active polls followed by a successful completion."""

                self._poll_results = [None, None, 0]

            def poll(self):
                """Return the next deterministic process state."""

                return self._poll_results.pop(0)

        with temporary_directory("peer-") as temporary:
            selection_log = pathlib.Path(temporary) / "runtime-selection.log"
            selection_log.write_text("initial Unity work\n", encoding="utf-8")
            clock = {"seconds": 0.0}
            terminated: list[Process] = []

            def now() -> float:
                """Expose the deterministic test clock to the wait helper."""

                return clock["seconds"]

            def advance_with_unity_log_progress(_seconds: float) -> None:
                """Advance the clock while proving that Unity continues to make log progress."""

                clock["seconds"] += 301.0
                selection_log.write_text(
                    "Unity compilation is still progressing at " + str(clock["seconds"]) + "\n",
                    encoding="utf-8",
                )

            exit_code = peer.wait_for_runtime_selection_process(
                Process(),
                selection_log,
                profile_id="humble-fastrtps",
                stall_seconds=300.0,
                clock=now,
                sleep=advance_with_unity_log_progress,
                terminate_process=lambda process: terminated.append(process),
            )

        self.assertEqual(0, exit_code)
        self.assertEqual([], terminated)
    def test_runtime_selection_enforces_absolute_deadline(self):
        """A selector that never settles must terminate at the absolute deadline."""
        peer = load_peer_module()

        class Process:
            """Resistant process double for deadline cleanup."""
            pid = 123
            def poll(self):
                """Remain live throughout the probe."""
                return None

        with temporary_directory("peer-deadline-") as temporary:
            log = pathlib.Path(temporary) / "selection.log"
            log.write_text("progress\n", encoding="utf-8")
            clock = {"seconds": 0.0}
            terminated = []
            def now():
                """Expose the deterministic clock value."""
                return clock["seconds"]
            def sleep(_seconds):
                """Advance beyond the configured deadline."""
                clock["seconds"] = 10.0
            with self.assertRaisesRegex(peer.PeerFailure, "bounded total duration"):
                peer.wait_for_runtime_selection_process(
                    Process(), log, profile_id="test", stall_seconds=300.0,
                    clock=now, sleep=sleep,
                    terminate_process=lambda process: terminated.append(process),
                    max_seconds=5.0,
                )
            self.assertEqual(1, len(terminated))
    def test_capture_windows_msvc_environment_rejects_timeout_and_output_flood(self):
        """Toolchain discovery rejects timeout and oversized captured output."""
        peer = load_peer_module()
        with tempfile.TemporaryDirectory() as temporary:
            root = pathlib.Path(temporary)
            vswhere = root / "Microsoft Visual Studio" / "Installer" / "vswhere.exe"
            vswhere.parent.mkdir(parents=True)
            vswhere.write_bytes(b"")
            with mock.patch.dict(peer.os.environ, {"ProgramFiles(x86)": temporary}, clear=False):
                with mock.patch.object(peer.subprocess, "run", side_effect=subprocess.TimeoutExpired("vswhere", 60)):
                    with self.assertRaisesRegex(peer.PeerFailure, "bounded timeout"):
                        peer.capture_windows_msvc_environment({})
                result = SimpleNamespace(stdout="x" * (peer._TOOLCHAIN_CAPTURE_MAX_BYTES + 1), stderr="", returncode=0)
                with mock.patch.object(peer.subprocess, "run", return_value=result):
                    with self.assertRaisesRegex(peer.PeerFailure, "output capacity"):
                        peer.capture_windows_msvc_environment({})
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
    def test_manual_editor_uses_a_three_times_readiness_budget(self):
        """Verify Phase181 behavior: a human-driven Editor run has enough time to enter Play Mode."""
        peer = load_peer_module()
        args = peer.parse_args(["--role", "windows-local-editor"])

        self.assertFalse(args.unity_batch)
        self.assertEqual(900.0, args.ready_timeout_seconds)
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
    def test_editor_batch_exit_diagnostic_retains_the_actual_operating_system_code(self):
        """Verify Phase181 behavior: a Batch exit failure exposes its real OS code without weakening the zero gate."""
        peer = load_peer_module()

        with self.assertRaisesRegex(peer.PeerFailure, r"exit code 17"):
            peer.require_editor_batch_exit_code(17)


__all__ = [name for name in globals() if not name.startswith("__")]
