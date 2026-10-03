from __future__ import annotations
from .ros_peer_runtime import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase186_bridge_live_peer.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE


def _endpoint_document(info: object) -> dict[str, Any]:
    """Handle endpoint document for Phase186 acceptance."""
    qos = getattr(info, "qos_profile", None)
    return {
        "nodeName": str(getattr(info, "node_name", "")),
        "nodeNamespace": str(getattr(info, "node_namespace", "")),
        "topicType": str(getattr(info, "topic_type", "")),
        "reliability": str(getattr(getattr(qos, "reliability", None), "name", "")),
        "durability": str(getattr(getattr(qos, "durability", None), "name", "")),
        "history": str(getattr(getattr(qos, "history", None), "name", "")),
        "depth": int(getattr(qos, "depth", 0) or 0),
    }


def _observe_graph(
    rclpy_module: Any,
    node: Any,
    config: Mapping[str, Any],
) -> Mapping[str, Any]:
    """Capture exact Bridge endpoint and QoS evidence through rclpy graph APIs."""

    observed: dict[str, Any] = {}

    def graph_ready() -> bool:
        """Handle graph ready for Phase186 acceptance."""
        observed.clear()
        for topic, kind in _layout(config):
            publishers = node.get_publishers_info_by_topic(topic)
            subscriptions = node.get_subscriptions_info_by_topic(topic)
            expected_type = (
                protocol.INTERFACE_TYPE
                if kind.startswith("custom_")
                else "foxglove_msgs/msg/Log"
            )
            if _is_publish(kind):
                matching_publishers = [
                    info for info in publishers if info.topic_type == expected_type
                ]
                bridge_publishers = [
                    info
                    for info in matching_publishers
                    if str(getattr(info, "node_name", "")) == BRIDGE_NODE_NAME
                ]
                required_publishers = (
                    2 if config["caseId"] == "fanout-fairness-health" else 1
                )
                if (
                    not bridge_publishers
                    or len(matching_publishers) < required_publishers
                ):
                    return False
            if _is_subscribe(kind) and not any(
                info.topic_type == expected_type
                and str(getattr(info, "node_name", "")) == BRIDGE_NODE_NAME
                for info in subscriptions
            ):
                return False
            observed[topic] = {
                "expectedType": expected_type,
                "publishers": [_endpoint_document(info) for info in publishers],
                "subscriptions": [_endpoint_document(info) for info in subscriptions],
            }
        return True

    _spin_until(
        rclpy_module,
        node,
        graph_ready,
        LIVE_ACTOR_OPERATION_TIMEOUT_SECONDS,
        "independent graph observer did not find every exact Bridge endpoint",
    )
    return {"source": "rclpy-graph-api", "topics": observed}


def run_graph_observer(config: Mapping[str, Any]) -> Mapping[str, Any]:
    """Run graph observer."""
    try:
        import rclpy
    except ImportError as exc:
        raise LiveActorFailure("FAIL_RUNTIME_SELECTION", "rclpy is unavailable") from exc
    rclpy.init(args=None)
    node = None
    try:
        node = rclpy.create_node("phase186_graph_" + str(config["tokenHash"])[:12])
        _write_actor_document(
            config,
            "graph-observer",
            "ready",
            {"state": "independent-graph-api-ready"},
        )
        _wait_for_unity_ready(config)
        return _observe_graph(rclpy, node, config)
    finally:
        if node is not None:
            node.destroy_node()
        rclpy.shutdown()




__all__ = [name for name in globals() if not name.startswith("__")]
