from __future__ import annotations
from .class_phase181_custom_ros2_peer_tests_fixtures import *
from Scripts.phase192.source_layout import read_split_source
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "test_phase181_custom_ros2_peer.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
class _Phase181CustomRos2PeerTests_validation:
    """Decomposed Phase192 implementation component."""
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
        source = read_split_source(PEER_PATH)

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
    def test_failed_peer_alias_probe_releases_subst_reservation(self):
        """A successful subst call with an invisible drive must be undone before retry."""
        peer = load_peer_module()
        workspace = pathlib.Path("D:/" + ("x" * 260))
        commands = []

        class Result:
            """Minimal subprocess result for alias reservation probing."""
            def __init__(self, returncode):
                """Store the mocked command status and empty streams."""
                self.returncode = returncode
                self.stdout = ""
                self.stderr = ""

        def run(command, **kwargs):
            """Record map/unmap calls and make the visibility probe fail."""
            commands.append(tuple(command))
            return Result(0 if len(commands) == 1 else 1)

        windows_os = mock.Mock(wraps=peer.os)
        windows_os.name = "nt"
        with mock.patch.object(peer, "os", windows_os), mock.patch.object(
            peer.subprocess, "run", side_effect=run
        ), mock.patch.object(pathlib.Path, "exists", return_value=False), mock.patch.object(
            pathlib.Path, "is_file", return_value=True
        ):
            with self.assertRaisesRegex(peer.PeerFailure, "FAIL_PEER_WORKSPACE"):
                with peer.temporary_short_windows_peer_workspace(workspace):
                    self.fail("alias reservation should not succeed when the mapped drive is invisible")

        self.assertTrue(any(command[-2:] == ("Z:", "/D") for command in commands))
    def test_failed_plugin_alias_probe_releases_subst_reservation(self):
        """A failed native plugin alias probe must not leak its drive mapping."""
        peer = load_peer_module()
        commands = []

        class Result:
            """Minimal subprocess result for plugin alias probing."""
            def __init__(self, returncode):
                """Store the mocked command status and empty streams."""
                self.returncode = returncode
                self.stdout = ""
                self.stderr = ""

        def run(command, **kwargs):
            """Record map/unmap calls and make the visibility probe fail."""
            commands.append(tuple(command))
            return Result(0 if len(commands) == 1 else 1)

        windows_os = mock.Mock(wraps=peer.os)
        windows_os.name = "nt"
        with temporary_directory("plugin-alias-") as temporary:
            plugin_directory = pathlib.Path(temporary)
            with mock.patch.object(peer, "os", windows_os), mock.patch.object(
                peer.subprocess, "run", side_effect=run
            ), mock.patch.object(pathlib.Path, "exists", return_value=False), mock.patch.object(
                pathlib.Path, "is_file", return_value=True
            ):
                with self.assertRaisesRegex(peer.PeerFailure, "FAIL_EDITOR_BATCH"):
                    with peer.temporary_short_windows_plugin_alias(plugin_directory):
                        self.fail("alias reservation should not succeed when the mapped drive is invisible")

        self.assertTrue(any(command[-2:] == ("Z:", "/D") for command in commands))
    def test_windows_peer_keeps_the_short_workspace_alias_through_worker_startup(self):
        """Verify Phase181 behavior: generated Python typesupport loads from the same short alias used to build it."""
        source = read_split_source(PEER_PATH)

        self.assertIn("peer_workspace_alias_stack = contextlib.ExitStack()", source)
        self.assertIn("peer_runtime_workspace = peer_workspace_alias_stack.enter_context(", source)
        self.assertIn("peer_runtime_workspace / \"install\"", source)
        self.assertIn("peer_workspace_alias_stack.close()", source)
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
                json.dumps({"verdict": "FAIL_STATE_TRANSITION"}),
                encoding="utf-8",
            )
            with self.assertRaisesRegex(peer.PeerFailure, "FAIL_STATE_TRANSITION"):
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
    def test_addon_license_repair_command_is_profile_pinned_and_uses_current_python(self):
        """The preflight may repair only the selected add-on's canonical legal text."""
        peer = load_peer_module()
        command = peer.build_addon_license_repair_command(ROOT, "lyrical")

        self.assertEqual(sys.executable, command[0])
        self.assertIn("build_foxrun_custom_typesupport_addon.py", command[1])
        self.assertEqual(["--distro", "lyrical", "--repair-tracked-license-eol"], command[2:])
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
    def test_worker_command_carries_one_owned_endpoint_ready_path(self):
        """Verify Phase181 behavior: outer helper can wait for the worker's endpoint-ready proof."""
        peer = load_peer_module()
        command = peer.build_worker_command(
            pathlib.Path("C:/ros2/.pixi/envs/default/python.exe"),
            role="windows-local-editor",
            workspace=pathlib.Path("C:/build/peer-workspace"),
            interface_digest="a" * 64,
            token="phase181-peer-token",
            worker_ready_json=pathlib.Path("C:/build/worker-ready.json"),
        )

        self.assertIn("--worker-ready-json", command)
        self.assertEqual(
            str(pathlib.Path("C:/build/worker-ready.json")),
            command[command.index("--worker-ready-json") + 1],
        )
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


__all__ = [name for name in globals() if not name.startswith("__")]
