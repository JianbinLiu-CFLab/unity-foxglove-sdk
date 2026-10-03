from __future__ import annotations
from .classify_evidence_and_can_complete_live_evidence import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase181_custom_ros2_peer.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
def evaluate_graph_evidence(
    publish_publishers,
    subscribe_subscriptions,
    bidirectional_publishers,
    bidirectional_subscriptions,
    node_name: str,
    expected_type: str,
    expected_reliability: int,
    requires_outbound: bool,
    requires_bidirectional: bool,
) -> dict[str, bool]:
    """Return bounded, per-direction graph evidence without exposing endpoint identities."""

    checks = {
        "subscribeReliable": external_endpoint_has_reliability(
            subscribe_subscriptions,
            node_name,
            expected_type,
            expected_reliability,
        ),
    }
    if requires_outbound:
        checks["publishPublisher"] = _external_endpoint_exists(
            publish_publishers,
            node_name,
            expected_type,
        )
    if requires_bidirectional:
        checks["bidirectionalPublisher"] = _external_endpoint_exists(
            bidirectional_publishers,
            node_name,
            expected_type,
        )
        checks["bidirectionalReliable"] = external_endpoint_has_reliability(
            bidirectional_subscriptions,
            node_name,
            expected_type,
            expected_reliability,
        )
    return checks
def summarize_graph_endpoints(
    infos,
    node_name: str,
    expected_type: str,
    expected_reliability: int,
) -> dict[str, int]:
    """Return bounded endpoint counts that distinguish discovery delay from mismatch."""

    summary = {
        "total": 0,
        "matchingType": 0,
        "externalMatchingType": 0,
        "externalMatchingReliable": 0,
    }
    for info in infos:
        summary["total"] += 1
        if getattr(info, "topic_type", "") != expected_type:
            continue
        summary["matchingType"] += 1
        if getattr(info, "node_name", "") == node_name:
            continue
        summary["externalMatchingType"] += 1
        qos = getattr(info, "qos_profile", None)
        reliability = getattr(qos, "reliability", None)
        try:
            actual = int(getattr(reliability, "value", reliability))
        except (TypeError, ValueError):
            continue
        if actual == int(getattr(expected_reliability, "value", expected_reliability)):
            summary["externalMatchingReliable"] += 1
    return summary
def merge_graph_observations(
    observed: dict[str, bool],
    current: dict[str, bool],
) -> dict[str, bool]:
    """Accumulate positive graph observations for one bounded peer run.

    Unity intentionally exits after its proof dwell.  Endpoint discovery evidence
    gathered while it was alive remains evidence for that same correlated run;
    a later post-exit query must not erase it.
    """

    return {
        name: bool(observed.get(name, False)) or bool(current.get(name, False))
        for name in current
    }
def _worker_result_base(lock: StaticInterfaceLock, verdict: str, **values: object) -> dict[str, object]:
    """Return summary-safe worker evidence without exposing raw process commands."""

    result: dict[str, object] = {
        "phase": 181,
        "interfacePackage": STATIC_INTERFACE_PACKAGE_ID,
        "rosPackageName": lock.ros_package_name,
        "interfaceRevision": lock.interface_revision,
        "interfaceDigest": lock.interface_digest,
        "interfaceDigestPrefix": protocol.digest_prefix(lock.interface_digest),
        "verdict": verdict,
    }
    result.update(values)
    return result


__all__ = [name for name in globals() if not name.startswith("__")]
