from __future__ import annotations

from .foundation import *


class Phase179WindowsLocalAcceptanceTests:
    """Exercise Windows local editor and Zenoh orchestration evidence"""

    def test_windows_local_editor_starts_waiting_publishers_before_waiting_for_unity_ready(self) -> None:
        """The one-command local workflow keeps repo-local publishers alive before the operator enters Play Mode."""

        profile = self.profiles.PROFILES["lyrical-fastrtps"]
        token = "phase179-lyrical-local-token"
        preexisting_ready_token = "phase179-lyrical-ready-before-local-run"
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            unity_log = root / "Editor.log"
            unity_log.write_text(
                "PHASE179_ROS2_INBOUND_READY runtime=lyrical rmw=rmw_fastrtps_cpp token="
                + preexisting_ready_token
                + "\n",
                encoding="utf-8",
            )
            summary_path = root / "windows-local-editor.json"
            markers = {
                name: self.profiles.inbound.UnityMarker(
                    session=8,
                    topic=f"/foxrun/phase179/{name}",
                    token=token,
                    received=1,
                    applied=1,
                    replaced=0,
                    value=self.profiles.inbound.MESSAGE_SPECS[name].expected_value(token),
                )
                for name in profile.message_set
            }
            ready = self.profiles.inbound.UnityReadyMarker("lyrical", "rmw_fastrtps_cpp", "manual")
            events: list[str] = []

            @contextlib.contextmanager
            def managed_publishers(*_args, **kwargs):
                """Record the staged local publisher lifecycle without launching ROS2."""

                message_names = kwargs.get("message_names", profile.message_set)
                label = ",".join(message_names)
                events.append(f"publishers-started:{label}")
                try:
                    yield
                finally:
                    events.append(f"publishers-stopped:{label}")

            def wait_ready(*_args, **_kwargs):
                """Record the fresh Unity READY boundary for the staged test flow."""

                events.append("ready")
                return ready

            def wait_marker(*marker_args, **_kwargs):
                """Return the copied-value marker for the currently requested topic."""

                name = str(marker_args[1]).rsplit("/", 1)[-1]
                events.append(f"marker:{name}")
                return markers[name]

            with mock.patch.object(
                self.profiles,
                "resolve_windows_ros2_root",
                return_value=(Path("C:/ros2_lyrical"), Path("python.exe"), Path("ros2.py")),
            ):
                with mock.patch.object(self.profiles.ros2env, "build_ros_env", return_value={}) as build_env:
                    with mock.patch.object(self.profiles.inbound, "unity_log_offset", side_effect=[11, 22, 33, 44]):
                        with mock.patch.object(
                            self.profiles,
                            "managed_windows_local_publishers",
                            managed_publishers,
                            create=True,
                        ):
                            with mock.patch.object(
                                self.profiles.inbound,
                                "wait_for_unity_ready_marker",
                                side_effect=wait_ready,
                            ) as ready_waiter:
                                with mock.patch.object(
                                    self.profiles,
                                    "validate_windows_subscription_endpoints",
                                    side_effect=AssertionError(
                                        "Windows-local acceptance must not replace its successful data-path proof "
                                        "with a post-publication rclpy graph observer."
                                    ),
                                ):
                                    with mock.patch.object(
                                        self.profiles.ros2env,
                                        "run_ros2",
                                        return_value=SimpleNamespace(returncode=0, stdout="published"),
                                    ) as publish:
                                        with mock.patch.object(
                                            self.profiles.inbound,
                                            "wait_for_unity_marker",
                                            side_effect=wait_marker,
                                        ) as wait_marker:
                                            exit_code = self.profiles.run_profile(
                                                "lyrical-fastrtps",
                                                [
                                                    "--role",
                                                    "windows-local-editor",
                                                    "--token",
                                                    token,
                                                    "--unity-log",
                                                    str(unity_log),
                                                    "--summary-json",
                                                    str(summary_path),
                                                ],
                                            )
            summary = json.loads(summary_path.read_text(encoding="utf-8"))

        self.assertEqual(0, exit_code)
        self.assertEqual("PHASE179_LYRICAL_FASTRTPS_WINDOWS_LOCAL_EDITOR_PASS", summary["verdict"])
        self.assertEqual("windows-local-editor", summary["role"])
        self.assertEqual("windows-local-loopback", summary["transportScope"])
        self.assertEqual("publisher-complete-and-unity-applied", summary["windowsLocalDataPathEvidence"])
        self.assertNotIn("endpointEvidence", summary)
        self.assertEqual(["string", "twist", "joy"], [entry["name"] for entry in summary["messageResults"]])
        self.assertEqual(
            [
                "publishers-started:string",
                "ready",
                "marker:string",
                "publishers-stopped:string",
                "publishers-started:twist,joy",
                "marker:twist",
                "marker:joy",
                "publishers-stopped:twist,joy",
            ],
            events,
        )
        self.assertEqual(0, publish.call_count)
        self.assertIsNone(ready_waiter.call_args.args[3])
        self.assertIsNone(ready_waiter.call_args.kwargs["start_offset"])
        self.assertEqual(
            frozenset({preexisting_ready_token}),
            ready_waiter.call_args.kwargs["excluded_tokens"],
        )
        self.assertEqual([None, None, None], [call.kwargs["start_offset"] for call in wait_marker.call_args_list])
        build_env.assert_called_once()

    def test_windows_local_zenoh_owns_router_before_publishers_and_releases_it_afterwards(self) -> None:
        """Zenoh local acceptance owns one router across the publisher, Unity, and endpoint-proof window."""

        profile = self.profiles.PROFILES["lyrical-zenoh"]
        token = "phase179-lyrical-zenoh-local-token"
        topology_id = "phase179-lyrical-zenoh-local-router"
        router = Path("C:/ros2_lyrical/Lib/rmw_zenoh_cpp/rmw_zenohd.exe")
        handle = SimpleNamespace(mode="owned-router", topology_id=topology_id)
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            unity_log = root / "Editor.log"
            unity_log.write_text("before-local-run\n", encoding="utf-8")
            summary_path = root / "windows-local-editor.json"
            markers = {
                name: self.profiles.inbound.UnityMarker(
                    session=9,
                    topic=f"/foxrun/phase179/{name}",
                    token=token,
                    received=1,
                    applied=1,
                    replaced=0,
                    value=self.profiles.inbound.MESSAGE_SPECS[name].expected_value(token),
                )
                for name in profile.message_set
            }
            events: list[str] = []

            @contextlib.contextmanager
            def managed_publishers(*_args, **kwargs):
                """Record each staged publisher set while the fake Zenoh router remains owned."""

                message_names = kwargs.get("message_names", profile.message_set)
                label = ",".join(message_names)
                events.append(f"publishers-started:{label}")
                try:
                    yield
                finally:
                    events.append(f"publishers-stopped:{label}")

            def start_topology(*_args, **_kwargs):
                """Record owned-router startup and return the fixed test handle."""

                events.append("topology-started")
                return handle

            def close_topology(*_args, **_kwargs):
                """Record owned-router cleanup after staged local publishers finish."""

                events.append("topology-closed")

            def wait_ready(*_args, **_kwargs):
                """Record the fresh Unity READY boundary for the Zenoh staged flow."""

                events.append("ready")

            def wait_marker(*marker_args, **_kwargs):
                """Return the fixed copied-value marker for the requested Zenoh topic."""

                name = str(marker_args[1]).rsplit("/", 1)[-1]
                events.append(f"marker:{name}")
                return markers[name]

            with mock.patch.object(
                self.profiles,
                "resolve_windows_ros2_root",
                return_value=(Path("C:/ros2_lyrical"), Path("python.exe"), Path("ros2.py")),
            ):
                with mock.patch.object(self.profiles.ros2env, "build_ros_env", return_value={}):
                    with mock.patch.object(self.profiles.inbound, "unity_log_offset", return_value=11):
                        with mock.patch.object(self.profiles, "managed_windows_local_publishers", managed_publishers, create=True):
                            with mock.patch.object(self.profiles.zenoh_topology, "start_topology", side_effect=start_topology):
                                with mock.patch.object(self.profiles.zenoh_topology, "close_topology", side_effect=close_topology):
                                    with mock.patch.object(
                                        self.profiles.inbound,
                                        "topology_summary",
                                        return_value={"mode": "owned-router", "readiness": "owned-router-ready"},
                                    ):
                                        with mock.patch.object(
                                            self.profiles.inbound,
                                            "wait_for_unity_ready_marker",
                                            side_effect=wait_ready,
                                        ):
                                            with mock.patch.object(
                                                self.profiles.inbound,
                                                "wait_for_unity_marker",
                                                side_effect=wait_marker,
                                            ):
                                                with mock.patch.object(
                                                    self.profiles,
                                                    "validate_windows_subscription_endpoints",
                                                    side_effect=AssertionError(
                                                        "Windows-local Zenoh acceptance must use its owned router, "
                                                        "publisher completion, and Unity copied-value evidence."
                                                    ),
                                                ):
                                                    exit_code = self.profiles.run_profile(
                                                        "lyrical-zenoh",
                                                        [
                                                            "--role",
                                                            "windows-local-editor",
                                                            "--token",
                                                            token,
                                                            "--unity-log",
                                                            str(unity_log),
                                                            "--summary-json",
                                                            str(summary_path),
                                                            "--zenoh-router",
                                                            str(router),
                                                            "--zenoh-topology-id",
                                                            topology_id,
                                                        ],
                                                    )
            summary = json.loads(summary_path.read_text(encoding="utf-8"))

        self.assertEqual(0, exit_code)
        self.assertEqual("PHASE179_LYRICAL_ZENOH_WINDOWS_LOCAL_EDITOR_PASS", summary["verdict"])
        self.assertEqual("publisher-complete-and-unity-applied", summary["windowsLocalDataPathEvidence"])
        self.assertNotIn("endpointEvidence", summary)
        self.assertEqual(topology_id, summary["zenohTopologyId"])
        self.assertEqual(
            [
                "topology-started",
                "publishers-started:string",
                "ready",
                "marker:string",
                "publishers-stopped:string",
                "publishers-started:twist,joy",
                "marker:twist",
                "marker:joy",
                "publishers-stopped:twist,joy",
                "topology-closed",
            ],
            events,
        )


__all__ = ["Phase179WindowsLocalAcceptanceTests"]
