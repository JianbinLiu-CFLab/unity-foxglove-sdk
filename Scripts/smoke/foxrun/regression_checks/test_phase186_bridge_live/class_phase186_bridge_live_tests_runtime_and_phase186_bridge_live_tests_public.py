from __future__ import annotations
from .class_phase186_bridge_live_tests_validation import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "test_phase186_bridge_live.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
class _Phase186BridgeLiveTests_runtime:
    def test_hostile_frames_encode_the_exact_declared_lengths(self) -> None:
        """Verify that hostile frames encode the exact declared lengths."""
        mutations = live_peer._hostile_mutations()
        unknown = mutations["unknown-op"]
        _version, _flags, header_size, payload_size = struct.unpack(
            "<HHII", unknown[4:16]
        )
        self.assertEqual(len(unknown) - 16, header_size + payload_size)
        self.assertEqual(
            {"op": "phase186_unknown_op"},
            json.loads(unknown[16 : 16 + header_size].decode("utf-8")),
        )
    def test_busy_response_uses_the_stable_protocol_error_code_field(self) -> None:
        """Verify that busy response uses the stable protocol error code field."""
        self.assertTrue(
            live_peer._is_busy_response(
                {
                    "op": "busy",
                    "status": "error",
                    "errorCode": "busy",
                    "terminal": True,
                }
            )
        )
        self.assertFalse(
            live_peer._is_busy_response(
                {
                    "op": "busy",
                    "status": "error",
                    "code": "busy",
                    "terminal": True,
                }
            )
        )
        self.assertFalse(
            live_peer._is_busy_response(
                {
                    "op": "busy",
                    "status": "error",
                    "errorCode": "busy",
                    "terminal": False,
                }
            )
        )
    def test_optional_rejection_response_reassembles_fragmented_error_frame(self) -> None:
        """Verify that optional rejection response reassembles fragmented error frame."""
        header = json.dumps(
            {"status": "error", "code": "invalid_frame"},
            separators=(",", ":"),
        ).encode("ascii")
        frame = b"U2R2" + struct.pack("<HHII", 1, 0, len(header), 0) + header
        connection = _ChunkSocket([frame[:3], frame[3:9], frame[9:17], frame[17:]])
        self.assertEqual(frame, live_peer._read_optional_frame(connection))
        self.assertIsNone(live_peer._read_optional_frame(_ChunkSocket([b""])))
    def test_cleanup_detects_owned_gate_pointer_and_extra_listener(self) -> None:
        """Verify that cleanup detects owned gate pointer and extra listener."""
        with tempfile.TemporaryDirectory() as temp:
            output = pathlib.Path(temp)
            gate = output / "unity-external-gate.json"
            exercise_gate = output / "unity-exercise-gate.json"
            pointer = output / "current-run.json"
            gate.write_text("{}", encoding="utf-8")
            exercise_gate.write_text("{}", encoding="utf-8")
            pointer.write_text("{}", encoding="utf-8")
            config = {
                "outputRoot": str(output),
                "bridgeHost": "127.0.0.1",
                "bridgePort": 18767,
                "foxgloveHost": "127.0.0.1",
                "foxglovePort": 18768,
                "externalGate": str(gate),
                "exerciseGate": str(exercise_gate),
            }
            cleanup = live._cleanup_document(
                config,
                _FakeOwner(),
                pointer,
                extra_endpoints=(("127.0.0.1", 18769),),
                cleanup_errors=("owner close failed",),
            )
            self.assertFalse(cleanup["complete"])
            self.assertIn(str(gate.resolve()), cleanup["residualTemporaryProjects"])
            self.assertIn(
                str(exercise_gate.resolve()),
                cleanup["residualTemporaryProjects"],
            )
            self.assertIn(str(pointer.resolve()), cleanup["residualTemporaryProjects"])
            self.assertIn("owner close failed", cleanup["cleanupErrors"])
    def test_coordinator_reloads_actual_cleanup_after_live_failure(self) -> None:
        """Verify that coordinator reloads actual cleanup after live failure."""
        with tempfile.TemporaryDirectory() as temp:
            output = pathlib.Path(temp)
            expected = {
                "complete": False,
                "residualProcesses": [186],
                "residualPorts": [18767],
                "residualOverlays": [],
                "residualTemporaryProjects": [],
                "cleanupErrors": ["owner close failed"],
            }
            (output / "cleanup.json").write_text(
                json.dumps(expected), encoding="utf-8"
            )
            self.assertEqual(
                expected,
                acceptance.load_cleanup_evidence_if_present(output),
            )
    def test_process_registry_has_public_presence_query(self) -> None:
        """Verify that process registry has public presence query."""
        owner = object.__new__(live.OwnedLiveProcesses)
        owner._records = {"sidecar-2": object()}
        self.assertTrue(owner.has_record("sidecar-2"))
        self.assertFalse(owner.has_record("sidecar-1"))
    def test_ros_peer_cohosts_graph_observer_to_bound_fastdds_participants(self) -> None:
        """Verify that ros peer cohosts graph observer to bound fastdds participants."""
        config = {
            "requiredActors": [
                "graph-observer",
                "ros-peer",
                "sidecar",
                "unity",
            ]
        }

        self.assertEqual(("ros-peer",), live._worker_process_roles(config))
        self.assertEqual(
            "ros-peer",
            live._owner_role_for_document(config, "graph-observer"),
        )
        self.assertEqual(
            "ros-peer",
            live._owner_role_for_document(config, "ros-peer"),
        )
    def test_cohosted_graph_process_evidence_names_the_ros_peer_owner(self) -> None:
        """Verify that cohosted graph process evidence names the ros peer owner."""
        process = mock.Mock()
        process.pid = 18602
        process.poll.return_value = 0
        owner = object.__new__(live.OwnedLiveProcesses)
        owner._records = {
            "ros-peer": types.SimpleNamespace(
                key="ros-peer",
                logical_role="ros-peer",
                executable=pathlib.Path("python.exe"),
                process=process,
                identity_verified=True,
                owner_requested=False,
            )
        }

        evidence = owner.actor_evidence(
            "graph-observer",
            preferred_key="ros-peer",
            allow_role_alias=True,
        )

        self.assertEqual("ros-peer", evidence["processRole"])
        self.assertTrue(evidence["cohosted"])
        self.assertEqual(18602, evidence["pid"])
    def test_cohosted_graph_documents_are_identity_bound_and_explicit(self) -> None:
        """Verify that cohosted graph documents are identity bound and explicit."""
        with tempfile.TemporaryDirectory() as temp:
            output = pathlib.Path(temp)
            config = {
                "outputRoot": str(output),
                "schemaVersion": 3,
                "runId": "phase186h-test-cohosted-graph",
                "caseId": "full-duplex",
                "tokenHash": "a" * 64,
                "head": "b" * 40,
                "runtimeRowId": "jazzy-fastrtps",
            }
            live_peer._write_cohosted_graph_ready(config)
            live_peer._write_cohosted_graph_result(
                config,
                {"source": "rclpy-graph-api", "topics": {"/topic": {}}},
            )

            ready = json.loads(
                (output / "actors" / "graph-observer-ready.json").read_text(
                    encoding="utf-8"
                )
            )
            result = json.loads(
                (output / "actors" / "graph-observer-result.json").read_text(
                    encoding="utf-8"
                )
            )
            self.assertEqual("ros-peer", ready["evidence"]["processRole"])
            self.assertTrue(ready["evidence"]["cohosted"])
            self.assertEqual("ros-peer", result["evidence"]["processRole"])
            self.assertTrue(result["evidence"]["cohosted"])
    def test_graph_observation_requires_the_exact_bridge_node(self) -> None:
        """Verify that graph observation requires the exact bridge node."""
        expected_type = live_protocol.INTERFACE_TYPE
        config = {"caseId": "full-duplex", "topics": ["/phase186/duplex"]}

        def endpoint(node_name: str) -> types.SimpleNamespace:
            """Handle endpoint for Phase186 acceptance."""
            return types.SimpleNamespace(
                node_name=node_name,
                node_namespace="/",
                topic_type=expected_type,
                qos_profile=types.SimpleNamespace(
                    reliability=types.SimpleNamespace(name="RELIABLE"),
                    durability=types.SimpleNamespace(name="VOLATILE"),
                    history=types.SimpleNamespace(name="KEEP_LAST"),
                    depth=10,
                ),
            )

        def observe(node_name: str) -> bool:
            """Handle observe for Phase186 acceptance."""
            info = endpoint(node_name)
            node = mock.Mock()
            node.get_publishers_info_by_topic.return_value = [info]
            node.get_subscriptions_info_by_topic.return_value = [info]
            ready: list[bool] = []

            def spin_once(_rclpy, _node, predicate, _timeout, _message) -> None:
                """Handle spin once for Phase186 acceptance."""
                ready.append(bool(predicate()))

            with mock.patch.object(live_peer, "_spin_until", side_effect=spin_once):
                live_peer._observe_graph(mock.Mock(), node, config)
            self.assertEqual(1, len(ready))
            return ready[0]

        self.assertFalse(observe("phase186_peer_deadbeef"))
        self.assertTrue(observe("unity2foxglove_ros2_bridge"))
    def test_runtime_environment_includes_ros_pixi_dll_directory(self) -> None:
        """Verify that runtime environment includes ros pixi dll directory."""
        with tempfile.TemporaryDirectory() as temp:
            root = pathlib.Path(temp) / "ros2"
            overlay = pathlib.Path(temp) / "overlay"
            pixi_bin = root / ".pixi" / "envs" / "default" / "Library" / "bin"
            pixi_bin.mkdir(parents=True)
            (overlay / "bin").mkdir(parents=True)
            with mock.patch.object(
                live.phase181_peer.ros2env,
                "build_ros_env",
                return_value={
                    "PATH": "ros-base",
                    "PYTHONPATH": "ros-python",
                    "AMENT_PREFIX_PATH": str(root),
                    "CMAKE_PREFIX_PATH": str(root),
                    "COLCON_PREFIX_PATH": str(root),
                },
            ) as build_ros_env:
                environment = live._build_runtime_environment(
                    {"PATH": "ambient"},
                    root,
                    overlay,
                    distro="jazzy",
                    rmw="rmw_fastrtps_cpp",
                    domain_id=161,
                    discovery_range="SUBNET",
                    topology_id="phase186h-test",
                    zenoh_session_config=None,
                )
            build_ros_env.assert_called_once_with(
                root,
                "rmw_fastrtps_cpp",
                "SUBNET",
                "161",
                "jazzy",
            )
            paths = environment["PATH"].split(os.pathsep)
            self.assertIn(str(pixi_bin), paths)
            self.assertIn("ros-base", paths)
            self.assertNotIn("ambient", paths)
    def test_unity_environment_binds_only_all_provider_fanout_to_run_domain(
        self,
    ) -> None:
        """Verify that unity environment binds only all provider fanout to run domain."""
        source = {
            "PATH": "ambient",
            "ROS_DOMAIN_ID": "7",
            "ROS_DISTRO": "ambient-distro",
            "RMW_IMPLEMENTATION": "ambient-rmw",
            "AMENT_PREFIX_PATH": "ambient-prefix",
        }

        bridge_only = live._build_unity_environment(
            source,
            {"caseId": "full-duplex", "domainId": 161},
        )
        fanout = live._build_unity_environment(
            source,
            {
                "caseId": "fanout-fairness-health",
                "domainId": 162,
                "rmw": "rmw_fastrtps_cpp",
            },
        )

        self.assertNotIn("ROS_DOMAIN_ID", bridge_only)
        self.assertNotIn("FASTDDS_BUILTIN_TRANSPORTS", bridge_only)
        self.assertEqual("162", fanout["ROS_DOMAIN_ID"])
        self.assertEqual(
            "SUBNET",
            fanout["ROS_AUTOMATIC_DISCOVERY_RANGE"],
        )
        self.assertEqual("UDPv4", fanout["FASTDDS_BUILTIN_TRANSPORTS"])
        self.assertNotIn("ROS_AUTOMATIC_DISCOVERY_RANGE", bridge_only)
        for environment in (bridge_only, fanout):
            self.assertNotIn("ROS_DISTRO", environment)
            self.assertNotIn("RMW_IMPLEMENTATION", environment)
            self.assertNotIn("AMENT_PREFIX_PATH", environment)
            self.assertEqual("ambient", environment["PATH"])
        self.assertEqual("7", source["ROS_DOMAIN_ID"])
class Phase186BridgeLiveTests(_Phase186BridgeLiveTests_support, _Phase186BridgeLiveTests_fixtures, _Phase186BridgeLiveTests_validation, _Phase186BridgeLiveTests_runtime, unittest.TestCase):
    """Group checks for phase186 bridge live tests."""
    pass


__all__ = [name for name in globals() if not name.startswith("__")]
