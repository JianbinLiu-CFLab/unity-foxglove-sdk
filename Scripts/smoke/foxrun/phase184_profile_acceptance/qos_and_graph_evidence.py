from __future__ import annotations
from .ros_peer_workers import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase184_profile_acceptance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
def _policy_name(value: object) -> str:
    """Handle the policy name step."""

    name = getattr(value, "name", None)
    if isinstance(name, str):
        return name.lower()
    text = str(value)
    return text.rsplit(".", 1)[-1].lower()


def _endpoint_identity(info) -> str:
    """Handle the endpoint identity step."""

    namespace = str(getattr(info, "node_namespace", "") or "/")
    name = str(getattr(info, "node_name", ""))
    return namespace.rstrip("/") + "/" + name


def _endpoint_snapshot(info) -> dict[str, object]:
    """Handle the endpoint snapshot step."""

    qos = getattr(info, "qos_profile", None)
    raw_gid = getattr(info, "endpoint_gid", b"")
    try:
        gid = bytes(raw_gid).hex()
    except (TypeError, ValueError):
        gid = ""
    reliability = _policy_name(getattr(qos, "reliability", ""))
    durability = _policy_name(getattr(qos, "durability", ""))
    history = _policy_name(getattr(qos, "history", ""))
    depth = int(getattr(qos, "depth", 0))
    represented_axes = [
        axis
        for axis, represented in (
            ("reliability", reliability != "unknown"),
            ("durability", durability != "unknown"),
            ("history", history != "unknown"),
            ("depth", history != "unknown"),
        )
        if represented
    ]
    return {
        "node": _endpoint_identity(info),
        "gid": gid,
        "topicType": str(getattr(info, "topic_type", "")),
        "qos": {
            "reliability": reliability,
            "durability": durability,
            "history": history,
            "depth": depth,
            "representedAxes": represented_axes,
        },
    }


def _is_helper_endpoint(snapshot: Mapping[str, object]) -> bool:
    """Return whether helper endpoint."""

    return str(snapshot.get("node", "")).rsplit("/", 1)[-1].startswith("phase184g_")


def _graph_for_topic(node, topic: str) -> dict[str, list[dict[str, object]]]:
    """Handle the graph for topic step."""

    publishers = [
        _endpoint_snapshot(info)
        for info in node.get_publishers_info_by_topic(topic)
    ]
    subscriptions = [
        _endpoint_snapshot(info)
        for info in node.get_subscriptions_info_by_topic(topic)
    ]
    return {"publishers": publishers, "subscriptions": subscriptions}


def _expected_qos_by_topic(config: Mapping[str, object]) -> dict[str, dict[str, object]]:
    """Handle the expected QoS by topic step."""

    return protocol.expected_qos_by_topic(str(config["case"]))


def _normalized_policy(value: object) -> str:
    """Handle the normalized policy step."""

    text = str(value).lower()
    aliases = {
        "qosreliabilitypolicy.reliable": "reliable",
        "qosreliabilitypolicy.best_effort": "best_effort",
        "qosreliabilitypolicy.system_default": "system_default",
        "qosdurabilitypolicy.volatile": "volatile",
        "qosdurabilitypolicy.transient_local": "transient_local",
        "qosdurabilitypolicy.system_default": "system_default",
        "qoshistorypolicy.keep_last": "keep_last",
        "qoshistorypolicy.keep_all": "keep_all",
        "qoshistorypolicy.system_default": "system_default",
    }
    return aliases.get(text, text)


_RESOLVED_SYSTEM_DEFAULT_POLICIES = {
    "reliability": frozenset({"system_default", "reliable", "best_effort"}),
    "durability": frozenset({"system_default", "volatile", "transient_local"}),
    "history": frozenset({"system_default", "keep_last", "keep_all"}),
}


def _observable_policy_matches(
    actual: object,
    expected: object,
    axis: str,
) -> bool:
    """Handle the observable policy matches step."""

    normalized_actual = _normalized_policy(actual)
    normalized_expected = _normalized_policy(expected)
    if normalized_actual == "unknown":
        return True
    if normalized_expected == "system_default":
        return normalized_actual in _RESOLVED_SYSTEM_DEFAULT_POLICIES[axis]
    return normalized_actual == normalized_expected


def _qos_equals(actual: Mapping[str, object], expected: Mapping[str, object]) -> bool:
    """Handle the QoS equals step."""

    return (
        _normalized_policy(actual.get("reliability")) == expected["reliability"]
        and _normalized_policy(actual.get("durability")) == expected["durability"]
        and _normalized_policy(actual.get("history")) == expected["history"]
        and int(actual.get("depth", -1)) == int(expected["depth"])
    )


def _qos_observable_axes_match(
    actual: Mapping[str, object],
    expected: Mapping[str, object],
) -> bool:
    """Match FastDDS graph QoS without inventing unreported History/Depth."""

    reliability = _normalized_policy(actual.get("reliability"))
    durability = _normalized_policy(actual.get("durability"))
    history = _normalized_policy(actual.get("history"))
    depth = int(actual.get("depth", -1))
    return (
        _observable_policy_matches(reliability, expected["reliability"], "reliability")
        and _observable_policy_matches(durability, expected["durability"], "durability")
        and _observable_policy_matches(history, expected["history"], "history")
        and (
            depth == int(expected["depth"])
            or (history == "unknown" and depth == 0)
            or (
                expected["history"] == "system_default"
                and history != "unknown"
                and depth >= 0
            )
        )
    )


def _resolved_system_default_publishers_agree(
    publishers: Sequence[Mapping[str, object]],
    expected: Mapping[str, object],
) -> bool:
    """Handle the resolved system default publishers agree step."""

    for axis in ("reliability", "durability", "history"):
        if expected[axis] != "system_default":
            continue
        actual_values = {
            _normalized_policy(item["qos"].get(axis))
            for item in publishers
        }
        if len(actual_values) != 1:
            return False
    if expected["history"] == "system_default":
        depths = {int(item["qos"].get("depth", -1)) for item in publishers}
        if len(depths) != 1:
            return False
    return True


def _external_endpoints(
    graph: Mapping[str, Sequence[Mapping[str, object]]],
    direction: str,
    expected_type: str,
) -> list[dict[str, object]]:
    """Handle the external endpoints step."""

    return [
        dict(item)
        for item in graph[direction]
        if not _is_helper_endpoint(item)
        and item.get("topicType") == expected_type
        and item.get("gid")
    ]


def _has_distinct_native_and_bridge_publishers(
    publishers: Sequence[Mapping[str, object]],
) -> bool:
    """Return whether distinct native and bridge publishers."""

    bridge = [
        item
        for item in publishers
        if str(item.get("node", "")).rstrip("/").endswith(
            "/unity2foxglove_ros2_bridge"
        )
    ]
    native = [
        item
        for item in publishers
        if item not in bridge
    ]
    return bool(
        bridge
        and native
        and {str(item.get("gid", "")) for item in bridge}
        .isdisjoint({str(item.get("gid", "")) for item in native})
    )


def _graph_ready(
    config: Mapping[str, object],
    graphs: Mapping[str, Mapping[str, Sequence[Mapping[str, object]]]],
) -> bool:
    """Handle the graph ready step."""

    case = str(config["case"])
    expected_type = str(config["interfaceType"])
    topics = [str(item) for item in config["topics"]]
    expected_qos = _expected_qos_by_topic(config)
    if case in {"multi-target", "qos-contract"}:
        for topic in topics:
            publishers = _external_endpoints(graphs[topic], "publishers", expected_type)
            if len(publishers) != 2 or len({item["gid"] for item in publishers}) != 2:
                return False
            if not _has_distinct_native_and_bridge_publishers(publishers):
                return False
            if any(
                not _qos_observable_axes_match(
                    item["qos"],
                    expected_qos[topic],
                )
                for item in publishers
            ):
                return False
            if not _resolved_system_default_publishers_agree(
                publishers,
                expected_qos[topic],
            ):
                return False
        return True
    if case == "stream-640hz":
        stream_subscriptions = _external_endpoints(
            graphs[topics[0]], "subscriptions", expected_type
        )
        origin_publishers = _external_endpoints(
            graphs[topics[1]], "publishers", expected_type
        )
        origin_subscriptions = _external_endpoints(
            graphs[topics[1]], "subscriptions", expected_type
        )
        required = (stream_subscriptions, origin_publishers, origin_subscriptions)
        return all(
            items
            and all(_qos_equals(item["qos"], expected_qos[topic]) for item in items)
            for items, topic in (
                (stream_subscriptions, topics[0]),
                (origin_publishers, topics[1]),
                (origin_subscriptions, topics[1]),
            )
        )
    if case == "degraded-target":
        return all(
            not _external_endpoints(graphs[topic], "publishers", expected_type)
            for topic in topics
        )
    return False


def _stream_subscription_ready(
    config: Mapping[str, object],
    graphs: Mapping[str, Mapping[str, Sequence[Mapping[str, object]]]],
) -> bool:
    """Require the exact external stream subscription before timed production."""

    stream_topic = str(config["topics"][0])
    graph = graphs.get(stream_topic)
    if not isinstance(graph, Mapping):
        return False
    subscriptions = graph.get("subscriptions")
    if not isinstance(subscriptions, Sequence):
        return False
    expected_type = str(config["interfaceType"])
    return any(
        isinstance(item, Mapping)
        and not _is_helper_endpoint(item)
        and str(item.get("node", "")).strip("/") != ""
        and item.get("topicType") == expected_type
        for item in subscriptions
    )


def _graph_evidence_from_topics(
    config: Mapping[str, object],
    graphs: Mapping[str, Mapping[str, Sequence[Mapping[str, object]]]],
) -> dict[str, object]:
    """Build strict graph evidence from one current rclpy topic snapshot."""

    if not _graph_ready(config, graphs):
        raise AcceptanceFailure(
            "FAIL_GRAPH",
            "Required transport endpoints and exact QoS were not observed.",
        )
    topics = [str(item) for item in config["topics"]]
    expected_type = str(config["interfaceType"])
    all_external: list[dict[str, object]] = []
    for topic in topics:
        for direction in ("publishers", "subscriptions"):
            all_external.extend(
                _external_endpoints(graphs[topic], direction, expected_type)
            )
    publishers_by_topic = {
        topic: [
            {"node": str(item["node"]), "gid": str(item["gid"])}
            for item in _external_endpoints(
                graphs[topic],
                "publishers",
                expected_type,
            )
        ]
        for topic in topics
    }
    gids = sorted(
        {
            str(item["gid"])
            for publishers in publishers_by_topic.values()
            for item in publishers
        }
    )
    nodes = sorted({str(item["node"]) for item in all_external})
    observed_qos = {
        topic: {
            direction: [
                item["qos"]
                for item in _external_endpoints(
                    graphs[topic],
                    direction,
                    expected_type,
                )
            ]
            for direction in ("publishers", "subscriptions")
        }
        for topic in topics
    }
    return {
        "endpointsObserved": True,
        "nodeIdentities": nodes,
        "publisherGids": gids,
        "publishersByTopic": publishers_by_topic,
        "negativeObservationSeconds": 0,
        "topics": dict(graphs),
        "requestedQos": _expected_qos_by_topic(config),
        "transportObservedQos": observed_qos,
        "qosMatches": True,
    }


def _write_graph_timeout_snapshot(
    config: Mapping[str, object],
    graphs: Mapping[str, Mapping[str, Sequence[Mapping[str, object]]]],
    *,
    stage: str = "graph",
) -> pathlib.Path:
    """Persist bounded raw graph facts only when an owned graph wait times out."""

    destination = (
        pathlib.Path(str(config["outputRoot"]))
        / "diagnostics"
        / f"ros2-peer-{stage}-timeout.json"
    )
    protocol.write_json_atomic(
        destination,
        {
            "case": str(config["case"]),
            "stage": stage,
            "topics": dict(graphs),
        },
        repo_root=repository_root(),
    )
    return destination


def _wait_for_stream_subscription(
    config: Mapping[str, object],
    rclpy_module,
    node,
    timeout_seconds: float = 30.0,
) -> dict[str, dict[str, list[dict[str, object]]]]:
    """Wait only for the typed Unity stream consumer needed before production."""

    stream_topic = str(config["topics"][0])
    deadline = time.monotonic() + timeout_seconds
    while True:
        rclpy_module.spin_once(node, timeout_sec=0.05)
        graphs = {stream_topic: _graph_for_topic(node, stream_topic)}
        if _stream_subscription_ready(config, graphs):
            return graphs
        if time.monotonic() >= deadline:
            _write_graph_timeout_snapshot(
                config,
                graphs,
                stage="stream-subscription",
            )
            raise AcceptanceFailure(
                "FAIL_GRAPH",
                "The ROS peer did not observe the typed Unity stream subscription.",
            )


def _wait_for_graph_snapshot(
    config: Mapping[str, object],
    rclpy_module,
    node,
    timeout_seconds: float = 30.0,
) -> dict[str, dict[str, list[dict[str, object]]]]:
    """Capture the exact transport graph from an already-owned ROS peer node."""

    topics = [str(item) for item in config["topics"]]
    deadline = time.monotonic() + timeout_seconds
    while True:
        rclpy_module.spin_once(node, timeout_sec=0.05)
        graphs = {topic: _graph_for_topic(node, topic) for topic in topics}
        if _graph_ready(config, graphs):
            return graphs
        if time.monotonic() >= deadline:
            _write_graph_timeout_snapshot(config, graphs)
            raise AcceptanceFailure(
                "FAIL_GRAPH",
                "The ROS peer did not capture the required transport graph.",
            )


def _wait_for_peer_result_document(
    config: Mapping[str, object],
    timeout_seconds: float = 180.0,
) -> dict[str, object]:
    """Wait for the peer's atomic PASS document without creating another ROS node."""

    path = _actor_path(config, "resultFiles", "ros2-peer")
    deadline = time.monotonic() + timeout_seconds
    while True:
        if path.is_file():
            try:
                return read_actor_document(
                    config,
                    "ros2-peer",
                    "resultFiles",
                )
            except AcceptanceFailure as exc:
                raise AcceptanceFailure(
                    "FAIL_GRAPH",
                    "The ROS peer did not produce auditable graph evidence.",
                ) from exc
        if time.monotonic() >= deadline:
            raise AcceptanceFailure(
                "FAIL_GRAPH",
                "The ROS peer graph snapshot did not arrive.",
            )
        time.sleep(0.1)




__all__ = [name for name in globals() if not name.startswith("__")]
