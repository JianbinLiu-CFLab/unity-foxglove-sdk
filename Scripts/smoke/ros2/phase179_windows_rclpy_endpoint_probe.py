#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Module: Scripts/smoke
# Purpose: Bounded repo-local Windows ROS2 rclpy endpoint evidence for Phase179.

"""Read one ROS2 subscription graph snapshot without routing through the ros2 CLI.

This helper is deliberately launched with the selected repo-local ROS2 Python.
It keeps the Windows endpoint proof in the same distribution/RMW environment as
the publishers while avoiding a ros2 CLI graph-query hang observed on Windows.
"""

from __future__ import annotations

import argparse
import json
import sys
import time
import uuid
from typing import Sequence


def positive_seconds(text: str) -> float:
    """Parse a strictly positive, bounded graph-observation timeout."""

    try:
        value = float(text)
    except ValueError as exc:
        raise argparse.ArgumentTypeError("--timeout-seconds must be a number") from exc
    if value <= 0.0:
        raise argparse.ArgumentTypeError("--timeout-seconds must be greater than zero")
    return value


def parse_args(argv: Sequence[str] | None = None) -> argparse.Namespace:
    """Parse the narrow single-topic probe contract."""

    parser = argparse.ArgumentParser(description="Phase179 Windows rclpy subscription endpoint probe")
    parser.add_argument("--topic", required=True)
    parser.add_argument("--timeout-seconds", type=positive_seconds, required=True)
    return parser.parse_args(argv)


def _policy_name(value: object, names: dict[int, str]) -> str:
    """Map rclpy QoS enum values to the portable lower-case summary vocabulary."""

    try:
        numeric = int(value)
    except (TypeError, ValueError):
        numeric = -1
    return names.get(numeric, "unknown")


def _endpoint_record(endpoint: object) -> dict[str, object]:
    """Return only the topic type and QoS fields that Phase179 certifies."""

    qos = endpoint.qos_profile
    return {
        "messageType": str(endpoint.topic_type),
        "qosReliability": _policy_name(
            qos.reliability,
            {0: "system_default", 1: "reliable", 2: "best_effort"},
        ),
        "qosHistory": _policy_name(
            qos.history,
            {0: "system_default", 1: "keep_last", 2: "keep_all"},
        ),
        "qosDepth": int(qos.depth),
        "qosDurability": _policy_name(
            qos.durability,
            {0: "system_default", 1: "transient_local", 2: "volatile"},
        ),
    }


def probe_subscription_endpoints(topic: str, timeout_seconds: float) -> dict[str, object]:
    """Wait up to the supplied bound for a non-empty subscription endpoint snapshot."""

    import rclpy

    node = None
    initialized = False
    try:
        rclpy.init(args=None)
        initialized = True
        node = rclpy.create_node("phase179_endpoint_probe_" + uuid.uuid4().hex[:8])
        deadline = time.monotonic() + timeout_seconds
        while True:
            endpoints = node.get_subscriptions_info_by_topic(topic)
            if endpoints:
                return {
                    "topic": topic,
                    "subscriptionCount": len(endpoints),
                    "endpoints": [_endpoint_record(endpoint) for endpoint in endpoints],
                }
            remaining = deadline - time.monotonic()
            if remaining <= 0.0:
                return {"topic": topic, "subscriptionCount": 0, "endpoints": []}
            rclpy.spin_once(node, timeout_sec=min(0.1, remaining))
    finally:
        if node is not None:
            node.destroy_node()
        if initialized and rclpy.ok():
            rclpy.shutdown()


def main(argv: Sequence[str] | None = None) -> int:
    """Emit one machine-readable endpoint snapshot without leaking environment details."""

    args = parse_args(argv)
    try:
        result = probe_subscription_endpoints(args.topic, args.timeout_seconds)
    except Exception:
        print("Phase179 Windows rclpy endpoint probe failed.", file=sys.stderr, flush=True)
        return 1
    print(json.dumps(result, sort_keys=True), flush=True)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
