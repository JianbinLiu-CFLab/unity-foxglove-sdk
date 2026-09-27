from __future__ import annotations
from .actor_configuration import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase186_bridge_live_peer.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE


def _message_text(value: object, kind: str) -> str:
    """Handle message text for Phase186 acceptance."""
    if kind.startswith("custom_"):
        return str(getattr(getattr(value, "payload", None), "message", ""))
    return str(getattr(value, "message", ""))


def _direct_peer_sequence(
    value: object,
    kind: str,
    token_hash: str,
    offered: int,
) -> int | None:
    """Return the exact current-run sequence for a peer-authored sample."""

    text = _message_text(value, kind)
    prefix = "phase186:" + token_hash[:12] + ":"
    suffix = ":external-a"
    if not text.startswith(prefix) or not text.endswith(suffix):
        return None
    encoded = text[len(prefix) : -len(suffix)]
    if not encoded or any(character < "0" or character > "9" for character in encoded):
        return None
    if len(encoded) > len(str(offered)):
        return None
    sequence = int(encoded)
    if str(sequence) != encoded or sequence < 1 or sequence > offered:
        return None
    if kind.startswith("custom_"):
        envelope_sequence = getattr(value, "foxrun_sequence", None)
        if type(envelope_sequence) is not int or envelope_sequence != sequence:
            return None
    return sequence


def _without_direct_peer_samples(
    samples: Sequence[object],
    kind: str,
    token_hash: str,
    offered: int,
    *,
    consume_direct: bool,
) -> list[object]:
    """Consume one peer self-delivery per exact current-run sequence.

    Jazzy rclpy does not expose the publisher GID in subscription callback
    message metadata.  A second copy of the same peer-authored sequence is
    therefore retained as evidence that the Bridge mirrored external input.
    """

    if not consume_direct:
        return list(samples)
    consumed: set[int] = set()
    remaining: list[object] = []
    for value in samples:
        sequence = _direct_peer_sequence(value, kind, token_hash, offered)
        if sequence is not None and sequence not in consumed:
            consumed.add(sequence)
            continue
        remaining.append(value)
    return remaining


def _outbound_texts(
    topic: str,
    received: Mapping[str, Sequence[object]],
    kinds: Mapping[str, str],
    token_hash: str,
    offered: int,
    publishers: Mapping[str, object],
) -> list[str]:
    """Handle outbound texts for Phase186 acceptance."""
    kind = kinds[topic]
    values = _without_direct_peer_samples(
        received[topic],
        kind,
        token_hash,
        offered,
        consume_direct=topic in publishers,
    )
    return [_message_text(value, kind) for value in values]


def _outbound_topic_ready(
    case_id: str,
    kind: str,
    texts: Sequence[str],
    token_hash: str,
) -> bool:
    """Handle outbound topic ready for Phase186 acceptance."""
    if kind.endswith("duplex") or case_id == "fanout-fairness-health":
        return any("unity-local-b" in text for text in texts)
    prefix = "phase186:" + token_hash[:12] + ":"
    return any(text.startswith(prefix) for text in texts)


def _outbound_wait_detail(
    case_id: str,
    expected_outbound: set[str],
    received: Mapping[str, Sequence[object]],
    kinds: Mapping[str, str],
    token_hash: str,
    offered: int,
    publishers: Mapping[str, object],
) -> str:
    """Handle outbound wait detail for Phase186 acceptance."""
    missing: list[str] = []
    observed: dict[str, list[str]] = {}
    for topic in sorted(expected_outbound):
        texts = _outbound_texts(
            topic,
            received,
            kinds,
            token_hash,
            offered,
            publishers,
        )
        observed[topic] = [text[:160] for text in texts[-8:]]
        if not _outbound_topic_ready(case_id, kinds[topic], texts, token_hash):
            missing.append(topic)
    return (
        "Unity Bridge outbound sample did not reach the exact ROS peer; missing="
        + json.dumps(missing, separators=(",", ":"))
        + " observed="
        + json.dumps(observed, separators=(",", ":"), sort_keys=True)
    )


def _spin_until(
    rclpy_module,
    node,
    predicate,
    timeout_seconds: float,
    message: str | Callable[[], str],
) -> None:
    """Handle spin until for Phase186 acceptance."""
    deadline = time.monotonic() + timeout_seconds
    while time.monotonic() < deadline:
        if predicate():
            return
        rclpy_module.spin_once(node, timeout_sec=0.05)
    detail = message() if callable(message) else message
    raise LiveActorFailure("FAIL_PEER", detail)


def _settle_source_delivery(rclpy_module, node, timeout_seconds: float) -> None:
    """Keep source publishers alive while reliable samples reach the Bridge."""

    if timeout_seconds <= 0:
        raise ValueError("source delivery timeout must be positive")
    deadline = time.monotonic() + timeout_seconds
    while True:
        remaining = deadline - time.monotonic()
        if remaining <= 0:
            return
        rclpy_module.spin_once(node, timeout_sec=min(0.05, remaining))




__all__ = [name for name in globals() if not name.startswith("__")]
