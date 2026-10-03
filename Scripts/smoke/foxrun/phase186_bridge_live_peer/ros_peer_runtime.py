from __future__ import annotations
from .message_contracts import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase186_bridge_live_peer.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
def run_ros_peer(config: Mapping[str, Any]) -> Mapping[str, Any]:
    """Run ros peer."""
    try:
        import rclpy
        from rclpy.qos import DurabilityPolicy, HistoryPolicy, QoSProfile, ReliabilityPolicy
    except ImportError as exc:
        raise LiveActorFailure("FAIL_RUNTIME_SELECTION", "rclpy is unavailable") from exc
    standard, nested, payload, envelope = _load_ros_types()
    qos = QoSProfile(
        history=HistoryPolicy.KEEP_LAST,
        depth=32,
        reliability=ReliabilityPolicy.RELIABLE,
        durability=DurabilityPolicy.VOLATILE,
    )
    rclpy.init(args=None)
    node = None
    publishers: dict[str, Any] = {}
    subscriptions: dict[str, Any] = {}
    received: dict[str, list[object]] = {}
    kinds: dict[str, str] = {}
    try:
        node = rclpy.create_node("phase186_peer_" + str(config["tokenHash"])[:12])
        for topic, kind in _layout(config):
            kinds[topic] = kind
            message_type = _message_type(kind, standard, envelope)
            if _is_subscribe(kind):
                publishers[topic] = node.create_publisher(message_type, topic, qos)
            if _is_publish(kind):
                received[topic] = []

                def capture(value, *, selected=topic):
                    """Handle capture for Phase186 acceptance."""
                    received[selected].append(value)
                    if len(received[selected]) > 4096:
                        del received[selected][:-4096]

                subscriptions[topic] = node.create_subscription(
                    message_type, topic, capture, qos
                )
        _write_actor_document(
            config,
            "ros-peer",
            "ready",
            {
                "publishers": sorted(publishers),
                "subscriptions": sorted(subscriptions),
                "interfaceDigest": protocol.INTERFACE_DIGEST,
            },
        )
        if "graph-observer" in set(config["requiredActors"]):
            _write_cohosted_graph_ready(config)
        _wait_for_ros_exercise_window(config)
        _spin_until(
            rclpy,
            node,
            lambda: _bridge_endpoints_ready(node, config),
            LIVE_ACTOR_OPERATION_TIMEOUT_SECONDS,
            "Bridge ROS graph endpoints did not match the live peer",
        )
        if "graph-observer" in set(config["requiredActors"]):
            _write_cohosted_graph_result(
                config,
                _observe_graph(rclpy, node, config),
            )

        slow_case = config["caseId"] == "slow-main-thread-640hz"
        offered = 1280 if slow_case else 8
        token_hash = str(config["tokenHash"])
        sequence_windows = _sequence_windows(str(config["caseId"]), offered)
        for window_index, sequences in enumerate(sequence_windows):
            started = time.perf_counter()
            for window_offset, sequence in enumerate(sequences, start=1):
                for topic, publisher in publishers.items():
                    kind = kinds[topic]
                    value = (
                        _custom_message(
                            envelope,
                            payload,
                            nested,
                            node,
                            config,
                            sequence,
                        )
                        if kind.startswith("custom_")
                        else _standard_message(standard, node, config, sequence)
                    )
                    publisher.publish(value)
                if slow_case and window_index == 1:
                    deadline = started + window_offset / 640.0
                    remaining = deadline - time.perf_counter()
                    if remaining > 0:
                        time.sleep(remaining)
                if sequence % 16 == 0:
                    rclpy.spin_once(node, timeout_sec=0.0)

            if slow_case and window_index == 0:
                rclpy.spin_once(node, timeout_sec=0.0)
                _wait_until(
                    lambda: _slow_unity_baseline_ready(config),
                    LIVE_ACTOR_OPERATION_TIMEOUT_SECONDS,
                    "FAIL_TERMINAL",
                    "Unity did not apply the identity-bound slow-case baseline",
                )

        if publishers:
            _settle_source_delivery(
                rclpy,
                node,
                SOURCE_DELIVERY_SETTLE_SECONDS,
            )

        expected_outbound = set(subscriptions)

        def outbound_ready() -> bool:
            """Handle outbound ready for Phase186 acceptance."""
            for topic in expected_outbound:
                kind = kinds[topic]
                texts = _outbound_texts(
                    topic,
                    received,
                    kinds,
                    token_hash,
                    offered,
                    publishers,
                )
                if not _outbound_topic_ready(
                    str(config["caseId"]), kind, texts, token_hash
                ):
                    return False
            return True

        if expected_outbound:
            _spin_until(
                rclpy,
                node,
                outbound_ready,
                LIVE_ACTOR_OPERATION_TIMEOUT_SECONDS,
                lambda: _outbound_wait_detail(
                    str(config["caseId"]),
                    expected_outbound,
                    received,
                    kinds,
                    token_hash,
                    offered,
                    publishers,
                ),
            )
            duplicate_deadline = time.monotonic() + 0.75
            while time.monotonic() < duplicate_deadline:
                rclpy.spin_once(node, timeout_sec=0.05)

        outbound: dict[str, list[str]] = {}
        same_origin_republished = 0
        for topic, samples in received.items():
            kind = kinds[topic]
            values = _without_direct_peer_samples(
                samples,
                kind,
                token_hash,
                offered,
                consume_direct=topic in publishers,
            )
            texts = [_message_text(value, kind) for value in values]
            outbound[topic] = texts[-32:]
            if kind.endswith("duplex"):
                local = [value for value in values if "unity-local-b" in _message_text(value, kind)]
                if len(local) != 1:
                    raise LiveActorFailure(
                        "FAIL_ORIGIN", "later local mutation was not observed exactly once"
                    )
                if any("external-a" in text for text in texts):
                    raise LiveActorFailure(
                        "FAIL_ORIGIN", "external input was causally mirrored back to ROS"
                    )
                publisher = publishers.get(topic)
                if publisher is not None:
                    publisher.publish(local[0])
                    same_origin_republished += 1
        return {
            "offered": offered,
            "nominalHz": 640 if offered == 1280 else None,
            "productionSeconds": round(time.perf_counter() - started, 6),
            "outbound": outbound,
            "sameOriginRepublished": same_origin_republished,
            "interfaceType": protocol.INTERFACE_TYPE,
            "interfaceDigest": protocol.INTERFACE_DIGEST,
        }
    finally:
        if node is not None:
            node.destroy_node()
        rclpy.shutdown()


__all__ = [name for name in globals() if not name.startswith("__")]
