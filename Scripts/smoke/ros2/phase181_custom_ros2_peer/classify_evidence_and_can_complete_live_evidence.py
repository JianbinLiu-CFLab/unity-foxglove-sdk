from __future__ import annotations
from .apply_explicit_zenoh_session_config_and_require_valid_domain_id import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase181_custom_ros2_peer.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
def classify_evidence(evidence: Mapping[str, object], probe_role: str = "orchestrate") -> str:
    """Return PASS only for the exact directional proof owned by a peer role."""

    requires_outbound, requires_bidirectional, requires_nullable_empty = _role_requires(probe_role)
    if evidence.get("interfaceDigestMatches") is not True:
        return "FAIL_INTERFACE_DIGEST"
    if evidence.get("graphEvidence") is not True:
        return "FAIL_GRAPH_EVIDENCE"
    if evidence.get("inboundApplied") is not True:
        return "FAIL_REMOTE_APPLY"
    if requires_outbound and evidence.get("outboundObserved") is not True:
        return "FAIL_OUTBOUND_EVIDENCE"
    if requires_bidirectional and evidence.get("sameOriginDropped") is not True:
        return "FAIL_SAME_ORIGIN"
    if requires_bidirectional and evidence.get("remoteOriginApplied") is not True:
        return "FAIL_REMOTE_APPLY"
    if requires_nullable_empty and evidence.get("nullableEmptyObserved") is not True:
        return "FAIL_PAYLOAD_SHAPE"
    if evidence.get("unityTerminalPass") is not True:
        return "FAIL_UNITY_EVIDENCE"
    if evidence.get("cleanStop") is not True:
        return "FAIL_CLEAN_STOP"
    return "PASS"
def can_complete_live_evidence(evidence: Mapping[str, object], probe_role: str = "orchestrate") -> bool:
    """Check business proof before the worker's finally block proves clean stop.

    The final endpoint teardown is intentionally performed after the probe loop.
    Treating that future teardown fact as a prerequisite for leaving the loop
    creates a self-deadlocking timeout even when all typed-direction evidence
    is already present.
    """

    completion_view = dict(evidence)
    completion_view["cleanStop"] = True
    return classify_evidence(completion_view, probe_role) == "PASS"
def should_publish_initial_bidirectional_probe(
    *,
    requires_bidirectional: bool,
    inbound_applied: bool,
    graph_evidence: bool,
    origin_probe_ready: bool,
    initial_remote_applied: bool,
    now: float,
    next_publish_time: float | None,
) -> bool:
    """Retry the first volatile P&S sample only after both directions are observable."""

    if (
        not requires_bidirectional
        or not inbound_applied
        or not graph_evidence
        or not origin_probe_ready
        or initial_remote_applied
    ):
        return False
    return next_publish_time is None or now >= next_publish_time
def should_publish_final_bidirectional_probe(
    *,
    requires_bidirectional: bool,
    same_origin_dropped: bool,
    remote_origin_applied: bool,
    now: float,
    next_publish_time: float | None,
) -> bool:
    """Keep the final nullable probe live until Unity proves the post-replay apply."""

    if not requires_bidirectional or not same_origin_dropped or remote_origin_applied:
        return False
    return next_publish_time is None or now >= next_publish_time
def has_role_transport_evidence(evidence: Mapping[str, object], probe_role: str) -> bool:
    """Check the directional transport facts before terminal/teardown proof exists."""

    completion_view = dict(evidence)
    completion_view["unityTerminalPass"] = True
    completion_view["cleanStop"] = True
    return classify_evidence(completion_view, probe_role) == "PASS"
def run_token_probe_count(token: str) -> int:
    """Bind the nullable/empty probe to one run without filling nullable fields."""

    if not token or len(token) > 96:
        raise PeerFailure("FAIL_PAYLOAD_SHAPE", "The correlation token is not safe for the bounded custom DTO probe.")
    digest = hashlib.sha256(token.encode("utf-8")).digest()
    value = int.from_bytes(digest[:4], byteorder="big", signed=False) & 0x7FFFFFFF
    return value or 1
def custom_payload_fields(token: str, *, null_empty: bool) -> dict[str, object]:
    """Return the exact ordinary DTO cases that the generated envelope must preserve."""

    probe_count = run_token_probe_count(token)
    if null_empty:
        return {
            "count": probe_count,
            "kind": 1,
            "message": "",
            "has_message": True,
            "bytes": [],
            "has_bytes": True,
            "values": [],
            "has_values": True,
            "nested": {"enabled": False, "label": ""},
            "has_nested": False,
            "optional_count": 0,
            "has_optional_count": False,
            "optional_text": "",
            "has_optional_text": False,
        }
    return {
        "count": 181,
        "kind": 1,
        "message": token,
        "has_message": True,
        "bytes": [0x18, 0x01, 0x81],
        "has_bytes": True,
        "values": [181, 182, 183],
        "has_values": True,
        "nested": {"enabled": True, "label": token},
        "has_nested": True,
        "optional_count": 181,
        "has_optional_count": True,
        "optional_text": token,
        "has_optional_text": True,
    }
def is_peer_remote_origin(origin: str, token: str) -> bool:
    """Recognize only origins emitted by this peer, including its null/empty probe."""

    return origin in {"remote-" + token, "remote-final-" + token}
def is_unity_origin_probe(
    origin: str,
    payload: Mapping[str, object],
    token: str,
) -> bool:
    """Recognize the bounded local envelope retained before remote apply suppression."""

    return (
        bool(origin)
        and not is_peer_remote_origin(origin, token)
        and dict(payload) == custom_payload_fields(token, null_empty=True)
    )
def write_worker_result(path: pathlib.Path, result: Mapping[str, object]) -> None:
    """Write one atomic, redacted worker result usable by its owning outer helper."""

    protocol.write_summary_atomic(path, result)
def write_worker_ready(path: pathlib.Path, lock: StaticInterfaceLock) -> None:
    """Atomically prove that the typed peer created its generated ROS2 endpoints."""

    protocol.write_summary_atomic(
        path,
        {
            "phase": 181,
            "ready": True,
            "interfaceDigest": lock.interface_digest,
        },
    )
def require_matching_worker_ready(path: pathlib.Path, lock: StaticInterfaceLock) -> None:
    """Reject a missing, partial, or foreign endpoint-ready file before prompting Unity."""

    try:
        ready = json.loads(pathlib.Path(path).read_text(encoding="utf-8"))
    except (OSError, UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise PeerFailure("FAIL_WORKER_READY", "The custom ROS2 peer did not produce a valid endpoint-ready proof.") from exc
    if (
        not isinstance(ready, Mapping)
        or ready.get("phase") != 181
        or ready.get("ready") is not True
        or ready.get("interfaceDigest") != lock.interface_digest
    ):
        raise PeerFailure("FAIL_WORKER_READY", "The custom ROS2 peer endpoint-ready proof did not match the locked interface.")
def wait_for_matching_worker_ready(
    process: subprocess.Popen[str],
    ready_path: pathlib.Path,
    lock: StaticInterfaceLock,
    timeout_seconds: float,
) -> None:
    """Wait a separate bounded startup window before allowing Unity to enter Play Mode."""

    deadline = time.monotonic() + timeout_seconds
    while True:
        if pathlib.Path(ready_path).is_file():
            require_matching_worker_ready(ready_path, lock)
            return
        if process.poll() is not None:
            raise PeerFailure("FAIL_WORKER_READY", "The custom ROS2 peer stopped before its typed endpoints became ready.")
        if time.monotonic() >= deadline:
            raise PeerFailure("FAIL_WORKER_READY", "The custom ROS2 peer did not create typed endpoints before its bounded startup timeout.")
        time.sleep(0.05)
def _safe_marker_token(value: str | None) -> bool:
    """Accept only the local opaque token grammar shared with the Unity component."""

    if not isinstance(value, str) or not value or len(value) > 96:
        return False
    return all(character.isalnum() or character in "-_." for character in value)
def _matching_marker(
    markers: Sequence[protocol.UnityMarker],
    name: str,
    token: str | None = None,
    topic: str | None = None,
) -> protocol.UnityMarker | None:
    """Find a marker with the same generated run token and, if needed, topic."""

    for marker in markers:
        if marker.name != name:
            continue
        if token is not None and marker.fields.get("token") != token:
            continue
        if topic is not None and marker.fields.get("topic") != topic:
            continue
        return marker
    return None
def count_bidirectional_apply_markers(
    markers: Sequence[protocol.UnityMarker],
    token: str,
) -> int:
    """Count only the selected run's exact bidirectional apply markers."""

    return sum(
        1
        for marker in markers
        if marker.name == "PHASE181_CUSTOM_ROS2_APPLIED"
        and marker.fields.get("token") == token
        and marker.fields.get("topic") == DEFAULT_TOPICS["bidirectional"]
    )
def _append_unique_markers(
    target: list[protocol.UnityMarker],
    seen: set[str],
    appended: Sequence[protocol.UnityMarker],
) -> None:
    """Keep one ordered record for each exact marker line across log polling."""

    for marker in appended:
        if marker.raw in seen:
            continue
        seen.add(marker.raw)
        target.append(marker)
def observe_no_late_unity_apply(
    unity_log: pathlib.Path,
    offset: int,
    token: str,
    *,
    observation_seconds: float = 0.25,
) -> tuple[bool, int]:
    """Observe one bounded post-stop window for a correlated late Unity apply."""

    if not _safe_marker_token(token):
        raise PeerFailure("FAIL_READY_TOKEN", "The post-stop Unity marker token is invalid.")
    if observation_seconds < 0.0 or observation_seconds > 5.0:
        raise PeerFailure("FAIL_ARGUMENTS", "The post-stop observation window is outside the bounded acceptance range.")
    marker_offset = offset
    deadline = time.monotonic() + observation_seconds
    while True:
        markers, marker_offset = protocol.read_new_markers(unity_log, marker_offset)
        if any(
            marker.name == "PHASE181_CUSTOM_ROS2_APPLIED" and marker.fields.get("token") == token
            for marker in markers
        ):
            return False, marker_offset
        remaining = deadline - time.monotonic()
        if remaining <= 0.0:
            return True, marker_offset
        time.sleep(min(0.05, remaining))
def _load_generated_message_types(lock: StaticInterfaceLock):
    """Load generated Python classes only inside the selected ROS2 worker process."""

    module = importlib.import_module(lock.ros_package_name + ".msg")
    try:
        return (
            getattr(module, lock.envelope_message_name),
            getattr(module, lock.payload_message_name),
            getattr(module, "Phase181NestedState3281D0E21244"),
        )
    except AttributeError as exc:
        raise PeerFailure("FAIL_INTERFACE_DIGEST", "The peer workspace does not expose the locked generated message classes.") from exc
def create_typed_worker_endpoints(
    rclpy_module,
    envelope_type,
    node_name: str,
    qos,
    publish_topic: str,
    subscribe_topic: str,
    bidirectional_topic: str,
):
    """Create one peer node and dispose it if endpoint setup only partially succeeds."""

    node = rclpy_module.create_node(node_name)
    received_publish: list[object] = []
    received_bidirectional: list[object] = []
    try:
        node.create_subscription(envelope_type, publish_topic, received_publish.append, qos)
        node.create_subscription(envelope_type, bidirectional_topic, received_bidirectional.append, qos)
        subscribe_publisher = node.create_publisher(envelope_type, subscribe_topic, qos)
        bidirectional_publisher = node.create_publisher(envelope_type, bidirectional_topic, qos)
    except BaseException as exc:
        try:
            node.destroy_node()
        except Exception:  # noqa: BLE001 - retain the setup failure as the bounded worker cause.
            pass
        if isinstance(exc, PeerFailure) or not isinstance(exc, Exception):
            raise
        raise PeerFailure("FAIL_PEER_RUNTIME", "The typed peer could not create every generated endpoint.") from exc
    return node, received_publish, received_bidirectional, subscribe_publisher, bidirectional_publisher
def _assign_payload(payload, nested_type, fields: Mapping[str, object]) -> None:
    """Assign the locked ordinary DTO graph to one generated payload message."""

    payload.count = int(fields["count"])
    payload.kind = int(fields["kind"])
    payload.message = str(fields["message"])
    payload.foxrun_has_message = bool(fields["has_message"])
    payload.bytes = list(fields["bytes"])
    payload.foxrun_has_bytes = bool(fields["has_bytes"])
    payload.values = list(fields["values"])
    payload.foxrun_has_values = bool(fields["has_values"])
    nested_fields = fields["nested"]
    nested = nested_type()
    nested.enabled = bool(nested_fields["enabled"])
    nested.label = str(nested_fields["label"])
    nested.foxrun_has_label = bool(fields["has_nested"])
    payload.nested = nested
    payload.foxrun_has_nested = bool(fields["has_nested"])
    payload.optional_count = int(fields["optional_count"])
    payload.foxrun_has_optional_count = bool(fields["has_optional_count"])
    payload.optional_text = str(fields["optional_text"])
    payload.foxrun_has_optional_text = bool(fields["has_optional_text"])
def _make_envelope(node, envelope_type, payload_type, nested_type, fields: Mapping[str, object], origin: str, sequence: int):
    """Create one typed envelope with a real ROS clock stamp and stable origin sequence."""

    envelope = envelope_type()
    envelope.foxrun_origin_id = origin
    envelope.foxrun_sequence = sequence
    envelope.foxrun_stamp = node.get_clock().now().to_msg()
    payload = payload_type()
    _assign_payload(payload, nested_type, fields)
    envelope.payload = payload
    return envelope
def _payload_evidence(envelope) -> dict[str, object]:
    """Copy only small payload facts from a received generated envelope."""

    payload = envelope.payload
    nested = payload.nested
    return {
        "count": int(payload.count),
        "kind": int(payload.kind),
        "message": str(payload.message),
        "has_message": bool(payload.foxrun_has_message),
        "bytes": list(payload.bytes),
        "has_bytes": bool(payload.foxrun_has_bytes),
        "values": list(payload.values),
        "has_values": bool(payload.foxrun_has_values),
        "nested": {"enabled": bool(nested.enabled), "label": str(nested.label)},
        "has_nested": bool(payload.foxrun_has_nested),
        "optional_count": int(payload.optional_count),
        "has_optional_count": bool(payload.foxrun_has_optional_count),
        "optional_text": str(payload.optional_text),
        "has_optional_text": bool(payload.foxrun_has_optional_text),
    }
def _envelope_metadata(envelope) -> dict[str, object]:
    """Read only the portable envelope timestamp and sequence needed for peer proof."""

    stamp = getattr(envelope, "foxrun_stamp", None)
    return {
        "foxrun_sequence": getattr(envelope, "foxrun_sequence", None),
        "foxrun_stamp": {
            "sec": getattr(stamp, "sec", None),
            "nanosec": getattr(stamp, "nanosec", None),
        },
    }
def _external_endpoint_exists(infos, node_name: str, expected_type: str) -> bool:
    """Require a matching endpoint that does not belong to this helper node."""

    for info in infos:
        if getattr(info, "topic_type", "") != expected_type:
            continue
        if getattr(info, "node_name", "") != node_name:
            return True
    return False
def external_endpoint_has_reliability(
    infos,
    node_name: str,
    expected_type: str,
    expected_reliability: int,
) -> bool:
    """Require observable external endpoint type, owner, and effective reliability."""

    for info in infos:
        if getattr(info, "topic_type", "") != expected_type or getattr(info, "node_name", "") == node_name:
            continue
        qos = getattr(info, "qos_profile", None)
        reliability = getattr(qos, "reliability", None)
        try:
            actual = int(getattr(reliability, "value", reliability))
        except (TypeError, ValueError):
            continue
        if actual == int(getattr(expected_reliability, "value", expected_reliability)):
            return True
    return False


__all__ = [name for name in globals() if not name.startswith("__")]
