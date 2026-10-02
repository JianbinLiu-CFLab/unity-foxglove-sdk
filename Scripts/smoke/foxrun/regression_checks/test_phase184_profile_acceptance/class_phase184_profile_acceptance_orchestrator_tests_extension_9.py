from __future__ import annotations
from .class_phase184_profile_acceptance_orchestrator_tests_extension_8 import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "test_phase184_profile_acceptance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
class _Phase184ProfileAcceptanceOrchestratorTests_extension_9:
    """Decomposed Phase192 implementation component."""
    def test_degraded_client_requests_delivery_only_after_subscribing(self):
        """Verify degraded client requests delivery only after subscribing."""

        module = load_module()
        token = "p184g_A1b2C3d4E5f6"
        topic = "/foxrun/phase184/degraded/state"
        config = {
            "case": "degraded-target",
            "token": token,
            "topics": [topic],
            "observationWindows": {
                "positiveSeconds": 3,
                "negativeSeconds": 3,
            },
            "foxgloveHost": "127.0.0.1",
            "foxglovePort": 18765,
        }
        events: list[str] = []
        websocket = mock.Mock()
        websocket.close = mock.AsyncMock()
        channels = {topic: mock.Mock(encoding="protobuf")}

        async def subscribe(_websocket, _channels):
            """Handle the subscribe step."""

            events.append("subscribe")
            return {184001: topic}

        async def send_json(*args, **kwargs):
            """Handle the send JSON step."""

            events.append(
                f"send:{args[4]}:advertise={kwargs['advertise']}"
            )

        async def receive(*_args, **_kwargs):
            """Receive one correlated acceptance sample."""

            events.append("receive")
            return {}, [], 1.0

        def wait_marker(_config, marker, _timeout):
            """Handle the wait marker step."""

            events.append(f"marker:{marker}")

        send = mock.AsyncMock(side_effect=send_json)
        websockets = mock.Mock(
            connect=mock.AsyncMock(return_value=websocket)
        )
        with mock.patch.dict(
            sys.modules,
            {"websockets": websockets},
        ), mock.patch.object(
            module,
            "write_actor_ready",
        ), mock.patch.object(
            module,
            "_wait_for_unity_context",
        ), mock.patch.object(
            module,
            "_wait_for_foxglove_channels",
            new=mock.AsyncMock(return_value=channels),
        ), mock.patch.object(
            module,
            "_foxglove_subscribe",
            side_effect=subscribe,
        ), mock.patch.object(
            module,
            "_foxglove_advertise_and_send_json",
            send,
        ), mock.patch.object(
            module,
            "_receive_foxglove_stages",
            side_effect=receive,
        ), mock.patch.object(
            module,
            "wait_for_log_marker",
            side_effect=wait_marker,
        ), mock.patch.object(
            module,
            "wait_for_terminal_marker",
        ):
            result = module.asyncio.run(
                module._run_foxglove_client_async(config)
            )

        self.assertTrue(result["deliveryObserved"])
        self.assertLess(
            events.index("subscribe"),
            events.index(
                "send:degraded-client-ready:advertise=True"
            ),
        )
        self.assertLess(
            events.index(
                "send:degraded-client-ready:advertise=True"
            ),
            events.index(
                "marker:PHASE184G_DEGRADED_CLIENT_READY"
            ),
        )
        self.assertLess(
            events.index(
                "marker:PHASE184G_DEGRADED_CLIENT_READY"
            ),
            events.index("receive"),
        )
        send.assert_awaited_once_with(
            websocket,
            module.DEGRADED_CLIENT_READY_TOPIC,
            "clientReady",
            token,
            "degraded-client-ready",
            18419,
            184902,
            advertise=True,
        )
        websocket.close.assert_awaited_once()
    def test_bridge_health_frame_is_correlated_and_strict(self):
        """Verify bridge health frame is correlated and strict."""

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
        """Verify marker parser requires exact case and token."""

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
        """Verify atomic config writer leaves no temporary file."""

        module = load_module()
        TEST_ROOT.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix="atomic-", dir=TEST_ROOT) as raw:
            target = pathlib.Path(raw) / "run-config.json"
            module.write_private_json_atomic(target, {"value": 184})
            self.assertEqual({"value": 184}, json.loads(target.read_text(encoding="utf-8")))
            self.assertEqual([], list(target.parent.glob("*.tmp")))
    def test_bridge_parser_requires_the_exact_qos_profile(self):
        """Verify bridge parser requires the exact QoS profile."""

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
        """Verify QoS summary requires and carries bridge parser evidence."""

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
                    "deliveryAttributionByTopic": {
                        topic: "publication-sequence-plus-graph-gid"
                        for topic in config["topics"]
                    },
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
                                    **{
                                        key: value
                                        for key, value in qos.items()
                                        if key != "profile"
                                    },
                                    "representedAxes": [
                                        "reliability",
                                        "durability",
                                        "history",
                                        "depth",
                                    ],
                                },
                                {
                                    **{
                                        key: value
                                        for key, value in qos.items()
                                        if key != "profile"
                                    },
                                    "representedAxes": [
                                        "reliability",
                                        "durability",
                                        "history",
                                        "depth",
                                    ],
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
        self._write_observed_runtime_markers(config)
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


__all__ = [name for name in globals() if not name.startswith("__")]
