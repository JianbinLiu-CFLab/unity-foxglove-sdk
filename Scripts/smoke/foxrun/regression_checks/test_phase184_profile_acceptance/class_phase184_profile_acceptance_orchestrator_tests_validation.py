from __future__ import annotations
from .class_phase184_profile_acceptance_orchestrator_tests_fixtures import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "test_phase184_profile_acceptance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
class _Phase184ProfileAcceptanceOrchestratorTests_validation:
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


__all__ = [name for name in globals() if not name.startswith("__")]
