from __future__ import annotations
from .ros_peer_contracts import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase184_profile_acceptance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
def _run_qos_peer(
    config: Mapping[str, object],
    rclpy_module,
    node,
    envelope_type,
) -> Mapping[str, object]:
    """Run QoS peer."""

    token = str(config["token"])
    topic_kinds = (
        (str(config["topics"][0]), "system-default", token + "-qos-system-default"),
        (str(config["topics"][1]), "keep-all", token + "-qos-keep-all"),
        (str(config["topics"][2]), "keep-last-depth", token + "-qos-keep-last-depth"),
    )
    received: dict[str, list[tuple[object, str, int | None]]] = {
        topic: [] for topic, _, _ in topic_kinds
    }
    subscriptions = []
    for topic, kind, _stage in topic_kinds:
        def receive(message, info, *, selected=topic):
            """Receive one correlated acceptance sample."""

            received[selected].append(
                (
                    message,
                    _publisher_gid(info),
                    _publication_sequence_number(info),
                )
            )

        subscriptions.append(
            node.create_subscription(envelope_type, topic, receive, _qos_profile(kind))
        )
    write_actor_ready(
        config,
        "ros2-peer",
        {"state": "qos-subscriptions-ready", "topicCount": len(topic_kinds)},
    )
    _wait_for_unity_context(config)
    graph_topics = _wait_for_graph_snapshot(config, rclpy_module, node)
    expected_type = str(config["interfaceType"])
    graph_publishers = {
        topic: _external_endpoints(
            graph_topics[topic],
            "publishers",
            expected_type,
        )
        for topic, _kind, _stage in topic_kinds
    }

    def topic_attribution(
        topic: str,
        stage: str,
    ) -> tuple[list[str], str]:
        """Handle the topic attribution step."""

        matching = [
            (gid, sequence)
            for message, gid, sequence in received[topic]
            if _ros_stage(message) == stage
        ]
        return _attribute_sample_publishers(
            direct_gids=(gid for gid, _sequence in matching),
            publication_sequences=(
                sequence for _gid, sequence in matching
            ),
            graph_publishers=graph_publishers[topic],
            minimum_publishers=2,
        )

    def all_delivered():
        """Handle the all delivered step."""

        for topic, _kind, stage in topic_kinds:
            gids, _source = topic_attribution(topic, stage)
            if len(gids) < 2:
                return False
        return True

    _spin_until(
        rclpy_module,
        node,
        all_delivered,
        60.0,
        "FAIL_QOS",
        "QoS case did not deliver every topic from Native and Bridge.",
    )
    wait_for_terminal_marker(config, 30.0)
    del subscriptions
    return {
        "deliveryByTopic": {
            topic: topic_attribution(topic, stage)[0]
            for topic, _kind, stage in topic_kinds
        },
        "deliveryAttributionByTopic": {
            topic: topic_attribution(topic, stage)[1]
            for topic, _kind, stage in topic_kinds
        },
        "graphEvidence": {
            "source": "ros2-peer-rclpy-graph-api",
            "topics": graph_topics,
        },
    }


def _validated_stream_production_elapsed(value: float) -> float:
    """Require the locked nominal stream window and retain the measured value."""

    elapsed = float(value)
    if not math.isfinite(elapsed) or elapsed < 1.8 or elapsed > 3.5:
        raise AcceptanceFailure(
            "FAIL_STREAM",
            (
                "Nominal 640 Hz production interval drifted outside tolerance: "
                f"observed={elapsed:.6f}s expected=1.800000..3.500000s."
            ),
        )
    return elapsed


def _publish_prepared_stream_samples(
    samples: Sequence[object],
    *,
    publisher,
    node,
    nominal_hz: float,
    perf_counter,
    sleep,
) -> float:
    """Timestamp and publish prepared samples on one deadline-based schedule."""

    period = 1.0 / float(nominal_hz)
    started = perf_counter()
    for index, sample in enumerate(samples):
        sample.foxrun_stamp = node.get_clock().now().to_msg()
        publisher.publish(sample)
        deadline = started + (index + 1) * period
        remaining = deadline - perf_counter()
        if remaining > 0:
            sleep(remaining)
    return perf_counter() - started


def _run_stream_peer(
    config: Mapping[str, object],
    rclpy_module,
    node,
    peer,
    envelope_type,
    payload_type,
    nested_type,
) -> Mapping[str, object]:
    """Run stream peer."""

    stream_topic, origin_topic = (str(item) for item in config["topics"])
    token = str(config["token"])
    sensor_qos = _qos_profile("sensor-data")
    origin_messages: list[tuple[object, str, int | None]] = []

    def receive_origin(message, info):
        """Handle the receive origin step."""

        origin_messages.append(
            (
                message,
                _publisher_gid(info),
                _publication_sequence_number(info),
            )
        )
        if len(origin_messages) > 256:
            del origin_messages[: len(origin_messages) - 256]

    origin_subscription = node.create_subscription(
        envelope_type,
        origin_topic,
        receive_origin,
        sensor_qos,
    )
    stream_publisher = node.create_publisher(envelope_type, stream_topic, sensor_qos)
    origin_publisher = node.create_publisher(envelope_type, origin_topic, sensor_qos)
    write_actor_ready(
        config,
        "ros2-peer",
        {"state": "stream-publishers-ready", "topicCount": 2, "nominalHz": 640},
    )
    _wait_for_unity_context(config)

    warmup_stage = token + "-origin-warmup"
    _spin_until(
        rclpy_module,
        node,
        lambda: any(
            _ros_stage(message) == warmup_stage
            for message, _gid, _sequence in origin_messages
        ),
        60.0,
        "FAIL_ORIGIN",
        "Unity origin warmup was not observed.",
    )
    warmup = next(
        message
        for message, _gid, _sequence in origin_messages
        if _ros_stage(message) == warmup_stage
    )
    unity_origin = str(getattr(warmup, "foxrun_origin_id", ""))
    if not unity_origin:
        raise AcceptanceFailure("FAIL_ORIGIN", "Unity origin warmup had no origin id.")
    peer_origin = "phase184-peer-" + protocol.token_sha256(token)[:16]

    _worker_progress("ros2-peer", "stream-wait-transport-graph")
    _wait_for_stream_subscription(config, rclpy_module, node)
    offered = 1280
    stream_samples = [
        _make_ros_envelope(
            peer,
            node,
            envelope_type,
            payload_type,
            nested_type,
            token=token,
            stage=f"stream-{index}",
            count=index,
            origin=peer_origin,
            sequence=index + 1,
        )
        for index in range(offered)
    ]
    elapsed = _validated_stream_production_elapsed(
        _publish_prepared_stream_samples(
            stream_samples,
            publisher=stream_publisher,
            node=node,
            nominal_hz=640.0,
            perf_counter=time.perf_counter,
            sleep=time.sleep,
        )
    )

    remote = _make_ros_envelope(
        peer,
        node,
        envelope_type,
        payload_type,
        nested_type,
        token=token,
        stage="origin-remote",
        count=18441,
        origin=peer_origin,
        sequence=18441,
    )
    for _ in range(3):
        origin_publisher.publish(remote)
        rclpy_module.spin_once(node, timeout_sec=0.05)
    wait_for_log_marker(config, "PHASE184G_STREAM_REMOTE_ORIGIN_APPLIED", 30.0)

    same_origin = _make_ros_envelope(
        peer,
        node,
        envelope_type,
        payload_type,
        nested_type,
        token=token,
        stage="origin-self",
        count=18498,
        origin=unity_origin,
        sequence=18498,
    )
    for _ in range(3):
        origin_publisher.publish(same_origin)
        rclpy_module.spin_once(node, timeout_sec=0.05)
    wait_for_log_marker(config, "PHASE184G_STREAM_LOCAL_ORIGIN_MUTATED", 30.0)

    local_stage = token + "-origin-local"
    _spin_until(
        rclpy_module,
        node,
        lambda: any(
            _ros_stage(message) == local_stage
            for message, _gid, _sequence in origin_messages
        ),
        30.0,
        "FAIL_ORIGIN",
        "Later local Zenoh origin mutation was not observed.",
    )
    graph_topics = _wait_for_graph_snapshot(config, rclpy_module, node)
    origin_publishers = _external_endpoints(
        graph_topics[origin_topic],
        "publishers",
        str(config["interfaceType"]),
    )
    local_observations = [
        (gid, sequence)
        for message, gid, sequence in origin_messages
        if _ros_stage(message) == local_stage
    ]
    local_origin_gids, local_origin_attribution = _attribute_sample_publishers(
        direct_gids=(gid for gid, _sequence in local_observations),
        publication_sequences=(
            sequence for _gid, sequence in local_observations
        ),
        graph_publishers=origin_publishers,
        minimum_publishers=1,
    )
    if not local_origin_gids:
        raise AcceptanceFailure(
            "FAIL_GRAPH",
            "Later local Zenoh origin sample had no publisher GID.",
        )
    terminal = wait_for_terminal_marker(config, 60.0)
    del origin_subscription
    return {
        "offered": offered,
        "nominalHz": 640,
        "productionElapsedSeconds": round(elapsed, 6),
        "remoteApplied": True,
        "sameOriginDropped": True,
        "laterLocalPublished": True,
        "unityOriginDigest": hashlib.sha256(unity_origin.encode("utf-8")).hexdigest(),
        "localOriginPublisherGids": local_origin_gids,
        "localOriginAttribution": local_origin_attribution,
        "terminalFields": dict(terminal.fields),
        "graphEvidence": {
            "source": "ros2-peer-rclpy-graph-api",
            "topics": graph_topics,
        },
    }


def run_ros2_peer_worker(config: Mapping[str, object]) -> int:
    """Run the selected typed peer using only the configured ROS Python."""

    role = "ros2-peer"
    try:
        _worker_progress(role, "import-rclpy")
        import rclpy

        _worker_progress(role, "load-message-types")
        peer, _lock, envelope, payload, nested = _load_ros_message_types(config)
        _worker_progress(role, "rclpy-init")
        rclpy.init(args=None)
        _worker_progress(role, "create-node")
        node = rclpy.create_node(_helper_node_name("peer", config))
        try:
            case = str(config["case"])
            _worker_progress(role, "run-" + case)
            if case == "multi-target":
                evidence = _run_multi_target_peer(
                    config, rclpy, node, peer, envelope, payload, nested
                )
            elif case == "qos-contract":
                evidence = _run_qos_peer(config, rclpy, node, envelope)
            elif case == "stream-640hz":
                evidence = _run_stream_peer(
                    config, rclpy, node, peer, envelope, payload, nested
                )
            else:
                raise AcceptanceFailure("FAIL_PEER", "Selected case has no ROS peer.")
        finally:
            node.destroy_node()
            rclpy.shutdown()
        write_actor_result(config, role, verdict="PASS", evidence=evidence)
        return 0
    except AcceptanceFailure as exc:
        write_actor_result(
            config,
            role,
            verdict=exc.code,
            evidence={"diagnostic": str(exc)[: protocol.MAX_DIAGNOSTIC_CHARACTERS]},
        )
        return 1
    except Exception as exc:
        write_actor_result(
            config,
            role,
            verdict="FAIL_PEER",
            evidence={"diagnostic": type(exc).__name__},
        )
        return 1




__all__ = [name for name in globals() if not name.startswith("__")]
