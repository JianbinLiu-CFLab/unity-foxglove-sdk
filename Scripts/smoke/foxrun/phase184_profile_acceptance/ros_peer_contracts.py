from __future__ import annotations
from .foxglove_client_stages import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase184_profile_acceptance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
def _ros_payload_fields(token: str, stage: str, count: int) -> dict[str, object]:
    """Handle the ROS payload fields step."""

    label = token + "-" + stage
    return {
        "count": count,
        "kind": 1,
        "message": label,
        "has_message": True,
        "bytes": [0x18, 0x04, count & 0xFF],
        "has_bytes": True,
        "values": [count, count + 1, count + 2],
        "has_values": True,
        "nested": {"enabled": True, "label": label},
        "has_nested": True,
        "optional_count": count,
        "has_optional_count": True,
        "optional_text": label,
        "has_optional_text": True,
    }


def _ros_stage(envelope) -> str:
    """Handle the ROS stage step."""

    payload = getattr(envelope, "payload", None)
    return str(getattr(payload, "message", ""))


def _ros_count(envelope) -> int:
    """Handle the ROS count step."""

    payload = getattr(envelope, "payload", None)
    return int(getattr(payload, "count", -1))


def _publisher_gid(message_info) -> str:
    """Handle the publisher GID step."""

    raw = (
        message_info.get("publisher_gid")
        if isinstance(message_info, Mapping)
        else getattr(message_info, "publisher_gid", None)
    )
    if isinstance(raw, Mapping):
        raw = raw.get("data")
    elif raw is not None and not isinstance(raw, (bytes, bytearray, memoryview)):
        raw = getattr(raw, "data", raw)
    try:
        value = bytes(raw) if raw is not None else b""
    except (TypeError, ValueError):
        return ""
    return value.hex() if value and any(value) else ""


def _publication_sequence_number(message_info) -> int | None:
    """Read the per-writer DDS sequence exposed by Windows Jazzy rclpy."""

    raw = (
        message_info.get("publication_sequence_number")
        if isinstance(message_info, Mapping)
        else getattr(message_info, "publication_sequence_number", None)
    )
    if isinstance(raw, bool) or not isinstance(raw, int) or raw < 0:
        return None
    return raw


def _attribute_sample_publishers(
    *,
    direct_gids: Iterable[str],
    publication_sequences: Iterable[int | None],
    graph_publishers: Sequence[Mapping[str, object]],
    minimum_publishers: int,
) -> tuple[list[str], str]:
    """Attribute one logical sample without fabricating unavailable rclpy GIDs."""

    graph_gids = sorted(
        {
            str(item.get("gid", ""))
            for item in graph_publishers
            if str(item.get("gid", ""))
        }
    )
    if len(graph_gids) != minimum_publishers:
        return [], ""

    observed_gids = sorted(
        {
            str(gid)
            for gid in direct_gids
            if str(gid) and str(gid) in graph_gids
        }
    )
    if len(observed_gids) >= minimum_publishers:
        return observed_gids, "message-info-publisher-gid"

    sequences = [
        value
        for value in publication_sequences
        if isinstance(value, int) and not isinstance(value, bool) and value >= 0
    ]
    if minimum_publishers == 1 and sequences:
        return graph_gids, "sole-external-graph-gid"
    if minimum_publishers == 2 and len(sequences) != len(set(sequences)):
        return graph_gids, "publication-sequence-plus-graph-gid"
    return [], ""


def _helper_node_name(role: str, config: Mapping[str, object]) -> str:
    """Handle the helper node name step."""

    digest = protocol.token_sha256(str(config["token"]))[:12]
    return f"phase184g_{role.replace('-', '_')}_{digest}"


def _worker_progress(role: str, stage: str) -> None:
    """Emit one bounded stage marker to the owned worker log."""

    print(
        f"PHASE184G_WORKER_PROGRESS role={role} stage={stage}",
        flush=True,
    )


def _qos_profile(kind: str):
    """Return an explicit rclpy QoS contract for acceptance endpoints."""

    try:
        from rclpy.qos import (
            DurabilityPolicy,
            HistoryPolicy,
            QoSProfile,
            ReliabilityPolicy,
        )
    except ImportError as exc:
        raise AcceptanceFailure("FAIL_PEER", "rclpy QoS APIs are unavailable.") from exc
    if kind == "default":
        return QoSProfile(
            history=HistoryPolicy.KEEP_LAST,
            depth=10,
            reliability=ReliabilityPolicy.RELIABLE,
            durability=DurabilityPolicy.VOLATILE,
        )
    if kind == "sensor-data":
        return QoSProfile(
            history=HistoryPolicy.KEEP_LAST,
            depth=5,
            reliability=ReliabilityPolicy.BEST_EFFORT,
            durability=DurabilityPolicy.VOLATILE,
        )
    if kind == "system-default":
        return QoSProfile(
            history=HistoryPolicy.SYSTEM_DEFAULT,
            depth=0,
            reliability=ReliabilityPolicy.SYSTEM_DEFAULT,
            durability=DurabilityPolicy.SYSTEM_DEFAULT,
        )
    if kind == "keep-all":
        return QoSProfile(
            history=HistoryPolicy.KEEP_ALL,
            depth=0,
            reliability=ReliabilityPolicy.RELIABLE,
            durability=DurabilityPolicy.VOLATILE,
        )
    if kind == "keep-last-depth":
        return QoSProfile(
            history=HistoryPolicy.KEEP_LAST,
            depth=7,
            reliability=ReliabilityPolicy.BEST_EFFORT,
            durability=DurabilityPolicy.TRANSIENT_LOCAL,
        )
    raise AcceptanceFailure("FAIL_QOS", "Unknown acceptance QoS contract.")


def _load_ros_message_types(config: Mapping[str, object]):
    """Load ROS message types."""

    peer = _phase181_peer_module()
    static_package = repository_root() / "Packages" / INTERFACE_PACKAGE_ID
    lock = peer.load_static_interface_lock(static_package)
    if lock.interface_digest != config["interfaceDigest"]:
        raise AcceptanceFailure("FAIL_PEER", "Worker interface digest drifted.")
    try:
        envelope, payload, nested = peer._load_generated_message_types(lock)
    except peer.PeerFailure as exc:
        raise AcceptanceFailure("FAIL_PEER", str(exc)) from exc
    return peer, lock, envelope, payload, nested


def _make_ros_envelope(
    peer,
    node,
    envelope_type,
    payload_type,
    nested_type,
    *,
    token: str,
    stage: str,
    count: int,
    origin: str,
    sequence: int,
):
    """Build ROS envelope."""

    return peer._make_envelope(
        node,
        envelope_type,
        payload_type,
        nested_type,
        _ros_payload_fields(token, stage, count),
        origin,
        sequence,
    )


def _spin_until(
    rclpy_module,
    node,
    predicate,
    timeout_seconds: float,
    failure_code: str,
    message: str,
) -> None:
    """Handle the spin until step."""

    deadline = time.monotonic() + timeout_seconds
    while not predicate():
        remaining = deadline - time.monotonic()
        if remaining <= 0:
            raise AcceptanceFailure(failure_code, message)
        rclpy_module.spin_once(node, timeout_sec=min(0.05, remaining))


def _run_multi_target_peer(
    config: Mapping[str, object],
    rclpy_module,
    node,
    peer,
    envelope_type,
    payload_type,
    nested_type,
) -> Mapping[str, object]:
    """Run multi target peer."""

    topic = str(config["topics"][0])
    token = str(config["token"])
    _worker_progress("ros2-peer", "multi-qos")
    qos = _qos_profile("default")
    messages: list[tuple[object, str, int | None]] = []
    observed_sample_gids: set[tuple[str, str]] = set()

    def receive(message, info):
        """Receive one correlated acceptance sample."""

        gid = _publisher_gid(info)
        publication_sequence = _publication_sequence_number(info)
        stage = _ros_stage(message)
        messages.append((message, gid, publication_sequence))
        if stage in {
            token + "-multi-local-1",
            token + "-multi-local-3",
        }:
            evidence_key = gid or (
                "publication-sequence"
                if publication_sequence is not None
                else "unattributed"
            )
            if (stage, evidence_key) not in observed_sample_gids:
                observed_sample_gids.add((stage, evidence_key))
                _worker_progress(
                    "ros2-peer",
                    "multi-sample-"
                    + stage.rsplit("-", 1)[-1]
                    + "-gid-"
                    + (
                        gid
                        or (
                            "publication-sequence-"
                            + str(publication_sequence)
                            if publication_sequence is not None
                            else "unattributed"
                        )
                    ),
                )
        if len(messages) > 4096:
            del messages[: len(messages) - 4096]

    _worker_progress("ros2-peer", "multi-create-subscription")
    subscription = node.create_subscription(envelope_type, topic, receive, qos)
    _worker_progress("ros2-peer", "multi-create-publisher")
    publisher = node.create_publisher(envelope_type, topic, qos)
    _worker_progress("ros2-peer", "multi-endpoints-ready")
    write_actor_ready(
        config,
        "ros2-peer",
        {"state": "typed-endpoints-ready", "topicCount": 1},
    )
    _wait_for_unity_context(config)

    local1 = token + "-multi-local-1"
    wait_for_log_marker(config, "PHASE184G_MULTI_LOCAL_ARMED", 120.0)
    graph_topics = _wait_for_graph_snapshot(config, rclpy_module, node)
    graph_publishers = _external_endpoints(
        graph_topics[topic],
        "publishers",
        str(config["interfaceType"]),
    )

    def sample_attribution(stage: str) -> tuple[list[str], str]:
        """Handle the sample attribution step."""

        matching = [
            (gid, sequence)
            for message, gid, sequence in messages
            if _ros_stage(message) == stage
        ]
        return _attribute_sample_publishers(
            direct_gids=(gid for gid, _sequence in matching),
            publication_sequences=(
                sequence for _gid, sequence in matching
            ),
            graph_publishers=graph_publishers,
            minimum_publishers=2,
        )

    def local_one_ready():
        """Handle the local one ready step."""

        gids, _source = sample_attribution(local1)
        return len(gids) >= 2

    _spin_until(
        rclpy_module,
        node,
        local_one_ready,
        60.0,
        "FAIL_FANOUT",
        "Native and Bridge did not both deliver local token 1.",
    )
    local_one = [
        (message, gid, sequence)
        for message, gid, sequence in messages
        if _ros_stage(message) == local1
    ]
    unity_origins = {
        str(getattr(message, "foxrun_origin_id", ""))
        for message, _gid, _sequence in local_one
        if getattr(message, "foxrun_origin_id", "")
    }
    if len(unity_origins) != 1:
        raise AcceptanceFailure("FAIL_ORIGIN", "Native and Bridge local token 1 origins differ.")
    unity_origin = next(iter(unity_origins))
    peer_origin = "phase184-peer-" + protocol.token_sha256(token)[:16]
    remote = _make_ros_envelope(
        peer,
        node,
        envelope_type,
        payload_type,
        nested_type,
        token=token,
        stage="multi-remote-2",
        count=18412,
        origin=peer_origin,
        sequence=18412,
    )
    for _ in range(3):
        publisher.publish(remote)
        rclpy_module.spin_once(node, timeout_sec=0.05)
    wait_for_log_marker(config, "PHASE184G_MULTI_REMOTE_APPLIED", 30.0)

    same_origin = _make_ros_envelope(
        peer,
        node,
        envelope_type,
        payload_type,
        nested_type,
        token=token,
        stage="multi-self-origin",
        count=18499,
        origin=unity_origin,
        sequence=18499,
    )
    for _ in range(3):
        publisher.publish(same_origin)
        rclpy_module.spin_once(node, timeout_sec=0.05)
    wait_for_log_marker(
        config,
        "PHASE184G_MULTI_LOCAL_MUTATED",
        float(config["observationWindows"]["negativeSeconds"]) + 20.0,
    )

    local3 = token + "-multi-local-3"

    def local_three_ready():
        """Handle the local three ready step."""

        gids, _source = sample_attribution(local3)
        return len(gids) >= 2

    _spin_until(
        rclpy_module,
        node,
        local_three_ready,
        30.0,
        "FAIL_FANOUT",
        "Native and Bridge did not both deliver later local token 3.",
    )
    graph_topics = _wait_for_graph_snapshot(config, rclpy_module, node)
    graph_publishers = _external_endpoints(
        graph_topics[topic],
        "publishers",
        str(config["interfaceType"]),
    )
    wait_for_terminal_marker(config, 30.0)
    local1_gids, local1_attribution = sample_attribution(local1)
    local3_gids, local3_attribution = sample_attribution(local3)
    if len(local1_gids) < 2 or len(local3_gids) < 2:
        raise AcceptanceFailure(
            "FAIL_FANOUT",
            "Native and Bridge sample attribution drifted before terminal evidence.",
        )
    del subscription
    return {
        "remoteApplied": True,
        "sameOriginDropped": True,
        "laterLocalPublished": True,
        "unityOriginDigest": hashlib.sha256(unity_origin.encode("utf-8")).hexdigest(),
        "local1PublisherGids": local1_gids,
        "local3PublisherGids": local3_gids,
        "local1Attribution": local1_attribution,
        "local3Attribution": local3_attribution,
        "distinctFanoutPublishers": len(local1_gids),
        "graphEvidence": {
            "source": "ros2-peer-rclpy-graph-api",
            "topics": graph_topics,
        },
    }




__all__ = [name for name in globals() if not name.startswith("__")]
