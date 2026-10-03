from __future__ import annotations
from .class_phase184_profile_acceptance_orchestrator_tests_support import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "test_phase184_profile_acceptance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
class _Phase184ProfileAcceptanceOrchestratorTests_fixtures:
    """Decomposed Phase192 implementation component."""
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


__all__ = [name for name in globals() if not name.startswith("__")]
