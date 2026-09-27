from __future__ import annotations
from .class_phase184_profile_acceptance_orchestrator_tests_cleanup import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "test_phase184_profile_acceptance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
class _Phase184ProfileAcceptanceOrchestratorTests_edge_cases:
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
            source.index("bool read_exact_into(") : source.index(
                "bool read_exact(", source.index("bool read_exact_into(")
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
    def test_bridge_health_readiness_does_not_create_a_data_generation(self):
        """Verify a health probe uses process ownership without creating data entities."""

        source = (
            ROOT
            / "Tools"
            / "ros2_bridge"
            / "unity2foxglove_ros2_bridge"
            / "src"
            / "unity2foxglove_ros2_bridge.cpp"
        ).read_text(encoding="utf-8")
        owned_client = source[
            source.index("void process_owned_client(") : source.index(
                "void process_client(",
                source.index("void process_owned_client("),
            )
        ]
        main = source[source.index("int main(int argc, char ** argv)") :]

        probe = owned_client.index(
            "if (first.role == bridge_runtime::FirstFrameRole::probe)"
        )
        data_lease = owned_client.index("auto data_lease =")
        generation = owned_client.index("generation.adopt_entities(generation_factory())")
        self.assertLess(probe, data_lease)
        self.assertLess(data_lease, generation)
        self.assertIn("schedule_legacy_health_response(first, protocol);", owned_client)
        self.assertIn("probe_lease->release();\n    return;", owned_client[probe:data_lease])
        self.assertIn("bridge_runtime::ProcessRosOwner ros_owner", main)
        self.assertLess(
            main.index("bridge_runtime::ProcessRosOwner ros_owner"),
            main.index("create_listen_socket("),
        )
        self.assertIn("ros_owner.require_node()", main)
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

        source = read_orchestrator_source()

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


__all__ = [name for name in globals() if not name.startswith("__")]
