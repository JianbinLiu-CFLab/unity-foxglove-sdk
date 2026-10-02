from __future__ import annotations
from .class_phase186_bridge_live_tests_fixtures import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "test_phase186_bridge_live.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
class _Phase186BridgeLiveTests_validation:
    """Decomposed Phase192 implementation component."""
    def test_ros_graph_gate_requires_exact_bridge_publisher(self) -> None:
        """Verify that ros graph gate requires exact bridge publisher."""
        config = {
            "caseId": "slow-main-thread-640hz",
            "topics": ("/phase186/slow_ingress", "/phase186/slow_control"),
        }

        def endpoint(node_name: str, topic_type: str) -> object:
            """Handle endpoint for Phase186 acceptance."""
            return types.SimpleNamespace(
                node_name=node_name,
                topic_type=topic_type,
            )

        bridge_custom = endpoint(
            live_peer.BRIDGE_NODE_NAME,
            live_protocol.INTERFACE_TYPE,
        )
        bridge_standard = endpoint(
            live_peer.BRIDGE_NODE_NAME,
            "foxglove_msgs/msg/Log",
        )
        peer_standard = endpoint(
            "phase186_peer_self",
            "foxglove_msgs/msg/Log",
        )
        node = mock.Mock()
        node.get_subscriptions_info_by_topic.side_effect = lambda topic: (
            [bridge_custom]
            if topic.endswith("slow_ingress")
            else [bridge_standard]
        )
        node.get_publishers_info_by_topic.side_effect = lambda topic: (
            []
            if topic.endswith("slow_ingress")
            else [peer_standard]
        )

        self.assertFalse(live_peer._bridge_endpoints_ready(node, config))

        node.get_publishers_info_by_topic.side_effect = lambda topic: (
            []
            if topic.endswith("slow_ingress")
            else [peer_standard, bridge_standard]
        )
        self.assertTrue(live_peer._bridge_endpoints_ready(node, config))
    def test_custom_peer_sets_nested_string_presence(self) -> None:
        """Verify that custom peer sets nested string presence."""
        class Envelope:
            """Represent envelope."""
            pass

        class Payload:
            """Represent payload."""
            pass

        class Nested:
            """Represent nested."""
            def __init__(self) -> None:
                """Initialize the helper state."""
                self.foxrun_has_label = False

        node = mock.Mock()
        stamp = object()
        node.get_clock.return_value.now.return_value.to_msg.return_value = stamp

        value = live_peer._custom_message(
            Envelope,
            Payload,
            Nested,
            node,
            {"tokenHash": "a" * 64},
            sequence=1,
        )

        self.assertEqual("external-a", value.payload.nested.label)
        self.assertTrue(value.payload.nested.foxrun_has_label)
    def test_source_publish_keeps_ros_peer_alive_for_bounded_delivery(self) -> None:
        """Verify that source publish keeps ros peer alive for bounded delivery."""
        ros = mock.Mock()
        node = object()
        with mock.patch.object(
            live_peer.time,
            "monotonic",
            side_effect=(10.0, 10.0, 10.03, 10.06),
        ):
            live_peer._settle_source_delivery(ros, node, 0.05)

        self.assertEqual(2, ros.spin_once.call_count)
        timeouts = [call.kwargs["timeout_sec"] for call in ros.spin_once.call_args_list]
        self.assertAlmostEqual(0.05, timeouts[0])
        self.assertAlmostEqual(0.02, timeouts[1])
    def test_spin_timeout_builds_failure_detail_from_final_observations(self) -> None:
        """Verify that spin timeout builds failure detail from final observations."""
        observed: list[str] = []

        def predicate() -> bool:
            """Handle predicate for Phase186 acceptance."""
            observed.append("duplex:none")
            return False

        with mock.patch.object(
            live_peer.time,
            "monotonic",
            side_effect=(10.0, 10.0, 11.0),
        ), self.assertRaises(live_peer.LiveActorFailure) as failure:
            live_peer._spin_until(
                mock.Mock(),
                object(),
                predicate,
                0.5,
                lambda: "missing outbound; observed=" + ",".join(observed),
            )

        self.assertIn("missing outbound; observed=duplex:none", str(failure.exception))
    def test_duplex_origin_check_consumes_one_direct_sample_per_sequence(self) -> None:
        """Verify that duplex origin check consumes one direct sample per sequence."""
        token_hash = "a" * 64

        def sample(sequence: int, label: str = "external-a") -> object:
            """Handle sample for Phase186 acceptance."""
            payload = types.SimpleNamespace(
                message=f"phase186:{token_hash[:12]}:{sequence}:{label}"
            )
            return types.SimpleNamespace(
                foxrun_sequence=sequence,
                payload=payload,
            )

        direct_one = sample(1)
        direct_two = sample(2)
        bridge_echo_one = sample(1)
        bridge_local = sample(9, "unity-local-b")

        actual = live_peer._without_direct_peer_samples(
            [direct_two, direct_one, bridge_local, bridge_echo_one],
            "custom_duplex",
            token_hash,
            offered=8,
            consume_direct=True,
        )

        self.assertEqual([bridge_local, bridge_echo_one], actual)
    def test_direct_sample_filter_keeps_malformed_or_out_of_run_samples(self) -> None:
        """Verify that direct sample filter keeps malformed or out of run samples."""
        token_hash = "b" * 64
        wrong_envelope_sequence = types.SimpleNamespace(
            foxrun_sequence=2,
            payload=types.SimpleNamespace(
                message=f"phase186:{token_hash[:12]}:1:external-a"
            ),
        )
        other_run = types.SimpleNamespace(
            foxrun_sequence=1,
            payload=types.SimpleNamespace(
                message="phase186:cccccccccccc:1:external-a"
            ),
        )
        oversized_sequence = types.SimpleNamespace(
            foxrun_sequence=1,
            payload=types.SimpleNamespace(
                message=(
                    f"phase186:{token_hash[:12]}:"
                    + "9" * 5000
                    + ":external-a"
                )
            ),
        )

        actual = live_peer._without_direct_peer_samples(
            [wrong_envelope_sequence, other_run, oversized_sequence],
            "custom_duplex",
            token_hash,
            offered=8,
            consume_direct=True,
        )

        self.assertEqual(
            [wrong_envelope_sequence, other_run, oversized_sequence],
            actual,
        )
    def test_direct_sample_filter_uses_standard_message_sequence(self) -> None:
        """Verify that direct sample filter uses standard message sequence."""
        token_hash = "d" * 64
        direct = types.SimpleNamespace(
            message=f"phase186:{token_hash[:12]}:7:external-a"
        )
        duplicate = types.SimpleNamespace(
            message=f"phase186:{token_hash[:12]}:7:external-a"
        )

        actual = live_peer._without_direct_peer_samples(
            [direct, duplicate],
            "standard_duplex",
            token_hash,
            offered=8,
            consume_direct=True,
        )

        self.assertEqual([duplicate], actual)
    def test_outbound_timeout_detail_names_missing_topic_and_observed_samples(self) -> None:
        """Verify that outbound timeout detail names missing topic and observed samples."""
        token_hash = "e" * 64
        custom_topic = "/phase186/custom"
        standard_topic = "/phase186/standard"
        direct_custom = types.SimpleNamespace(
            foxrun_sequence=1,
            payload=types.SimpleNamespace(
                message=f"phase186:{token_hash[:12]}:1:external-a"
            ),
        )
        direct_standard = types.SimpleNamespace(
            message=f"phase186:{token_hash[:12]}:1:external-a"
        )
        standard_local = types.SimpleNamespace(
            message=f"phase186:{token_hash[:12]}:9:unity-local-b-1"
        )
        describe = getattr(live_peer, "_outbound_wait_detail", None)

        self.assertIsNotNone(describe)
        detail = describe(
            "manual-jazzy-fastrtps-duplex",
            {custom_topic, standard_topic},
            {
                custom_topic: [direct_custom],
                standard_topic: [direct_standard, standard_local],
            },
            {
                custom_topic: "custom_duplex",
                standard_topic: "standard_duplex",
            },
            token_hash,
            8,
            {custom_topic: object(), standard_topic: object()},
        )

        self.assertIn(f'missing=["{custom_topic}"]', detail)
        self.assertIn(f'"{custom_topic}":[]', detail)
        self.assertIn("unity-local-b-1", detail)
    def test_actor_readiness_budget_outlives_coordinator_budget(self) -> None:
        """Verify that actor readiness budget outlives coordinator budget."""
        self.assertEqual(
            900.0,
            live_protocol.COORDINATOR_UNITY_READY_TIMEOUT_SECONDS,
        )
        self.assertGreater(
            live_protocol.ACTOR_UNITY_READY_TIMEOUT_SECONDS,
            live_protocol.COORDINATOR_UNITY_READY_TIMEOUT_SECONDS,
        )
    def test_live_actor_operation_budget_allows_slow_unity_and_ros_startup(self) -> None:
        """Verify that live actor operation budget allows slow unity and ros startup."""
        self.assertEqual(300.0, live_peer.LIVE_ACTOR_OPERATION_TIMEOUT_SECONDS)
        self.assertGreater(
            live.LIVE_ACTOR_RESULT_TIMEOUT_SECONDS,
            live_peer.LIVE_ACTOR_OPERATION_TIMEOUT_SECONDS,
        )
    def test_slow_sequence_flood_waits_for_current_unity_baseline(self) -> None:
        """Verify that slow sequence flood waits for current unity baseline."""
        windows = live_peer._sequence_windows(
            "slow-main-thread-640hz",
            offered=6,
        )
        self.assertEqual(
            ((1,), (2, 3, 4, 5, 6)),
            tuple(tuple(window) for window in windows),
        )

        with tempfile.TemporaryDirectory() as temp:
            unity_log = pathlib.Path(temp) / "unity.log"
            config = {
                "caseId": "slow-main-thread-640hz",
                "runId": "phase186h-slow-baseline-test",
                "unityLog": str(unity_log),
            }
            unity_log.write_text(
                "PHASE186_ACCEPTANCE_PROGRESS "
                "run=phase186h-stale-baseline case=slow-main-thread-640hz "
                "generated=true received=1 applied=1\n"
                "PHASE186_ACCEPTANCE_PROGRESS "
                "run=phase186h-slow-baseline-test case=slow-main-thread-640hz "
                "generated=true received=0 applied=0\n",
                encoding="utf-8",
            )
            self.assertFalse(live_peer._slow_unity_baseline_ready(config))
            with unity_log.open("a", encoding="utf-8") as stream:
                stream.write(
                    "PHASE186_ACCEPTANCE_PROGRESS "
                    "run=phase186h-slow-baseline-test "
                    "case=slow-main-thread-640hz "
                    "generated=true received=1 applied=1\n"
                )
            self.assertTrue(live_peer._slow_unity_baseline_ready(config))
    def test_reconnect_peer_waits_for_identity_bound_exercise_gate(self) -> None:
        """Verify that reconnect peer waits for identity bound exercise gate."""
        with tempfile.TemporaryDirectory() as temp:
            gate = pathlib.Path(temp) / "exercise.json"
            config = {
                "caseId": "reconnect-degraded-recovery",
                "runId": "phase186h-reconnect-test",
                "tokenHash": "a" * 64,
                "head": "b" * 40,
                "exerciseGate": str(gate),
            }
            self.assertFalse(live_peer._identity_gate_ready(config, "exerciseGate"))
            gate.write_text(
                json.dumps(
                    {
                        "schemaVersion": 1,
                        "runId": config["runId"],
                        "caseId": config["caseId"],
                        "tokenHash": config["tokenHash"],
                        "head": "c" * 40,
                        "ready": True,
                    }
                ),
                encoding="utf-8",
            )
            self.assertFalse(live_peer._identity_gate_ready(config, "exerciseGate"))
            gate.write_text(
                json.dumps(
                    {
                        "schemaVersion": 1,
                        "runId": config["runId"],
                        "caseId": config["caseId"],
                        "tokenHash": config["tokenHash"],
                        "head": config["head"],
                        "ready": True,
                    }
                ),
                encoding="utf-8",
            )
            self.assertTrue(live_peer._identity_gate_ready(config, "exerciseGate"))
            with mock.patch.object(live_peer, "_wait_for_unity_ready") as unity_ready, \
                 mock.patch.object(live_peer, "_wait_for_exercise_gate") as exercise_ready:
                live_peer._wait_for_ros_exercise_window(config)
            unity_ready.assert_called_once_with(config)
            exercise_ready.assert_called_once_with(config)
    def test_reconnect_restart_waits_for_observed_disconnect_and_recovery(self) -> None:
        """Verify that reconnect restart waits for observed disconnect and recovery."""
        owner = mock.Mock()
        runtime = object()
        config = {"bridgeHost": "127.0.0.1", "bridgePort": 18605}
        health_generations: list[object] = []
        health = {"status": "ok", "generation": 2}
        with mock.patch.object(
            live,
            "_unity_progress_count",
            side_effect=(3, 4),
        ), mock.patch.object(
            live,
            "_wait_until_port_released",
        ) as port_released, mock.patch.object(
            live,
            "_wait_unity_progress_after",
        ) as progress, mock.patch.object(
            live,
            "_launch_sidecar",
            return_value=(object(), health),
        ) as launch, mock.patch.object(
            live,
            "_write_exercise_gate",
        ) as write_gate:
            live._restart_sidecar_after_observed_disconnect(
                owner,
                runtime,
                config,
                health_generations,
            )

        owner.stop.assert_called_once_with("sidecar-1")
        port_released.assert_called_once_with("127.0.0.1", 18605)
        self.assertEqual(
            [
                mock.call(config, owner, 3, {"ready": "false", "connected": "false"}),
                mock.call(
                    config,
                    owner,
                    4,
                    {
                        "ready": "true",
                        "connected": "true",
                        "publish": "Ready",
                        "subscribe": "Ready",
                    },
                ),
            ],
            progress.call_args_list,
        )
        launch.assert_called_once_with(owner, runtime, config, "sidecar-2")
        write_gate.assert_called_once_with(config)
        self.assertEqual([health], health_generations)


__all__ = [name for name in globals() if not name.startswith("__")]
