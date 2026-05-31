#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Module: Scripts/smoke
# Purpose: ROS2-side acceptance helper for PointCloud2 t field verification.

"""Validate that a ROS2 PointCloud2 stream exposes the per-point `t` field.

Start Unity (and any ROS2 bridge path) first, keep Unity running, then run this
script while the point cloud publisher is live. The script waits for publisher
availability, receives a bounded /points message, and checks PointField metadata
for `t` and the expected dtype.
"""

from __future__ import annotations

import argparse
import json
import pathlib
import re
import sys

import _ros2_windows_env as ros2env


DEFAULT_ROS2_ROOT = ros2env.DEFAULT_ROS2_ROOT
POINTS_TOPIC = "/points"
POINTS_MSG_TYPE = "sensor_msgs/msg/PointCloud2"
EXPECTED_FIELD = "t"
EXPECTED_DATATYPE = "uint32"


def parse_args(argv: list[str]) -> argparse.Namespace:
    """Parse script arguments."""

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--ros2-root",
        default=str(DEFAULT_ROS2_ROOT),
        help="Windows ROS2 Jazzy root. Default: C:\\ros2_jazzy\\ros2-windows",
    )
    parser.add_argument(
        "--topic",
        default=POINTS_TOPIC,
        help="PointCloud2 topic to check (default: /points).",
    )
    parser.add_argument(
        "--wait-seconds",
        type=float,
        default=10.0,
        help="How long to wait for the PointCloud2 publisher. (default: 10)",
    )
    parser.add_argument(
        "--echo-spin-seconds",
        type=float,
        default=8.0,
        help="ROS2 spin time for bounded echo. (default: 8)",
    )
    parser.add_argument(
        "--expected-type",
        default=EXPECTED_DATATYPE,
        help="Expected t field datatype. Default: uint32",
    )
    parser.add_argument(
        "--rmw",
        default=None,
        help="RMW implementation to use. Omit to preserve existing value or default to rmw_fastrtps_cpp.",
    )
    parser.add_argument(
        "--discovery-range",
        choices=("LOCALHOST", "SUBNET", "OFF", "SYSTEM_DEFAULT"),
        default=None,
        help="Override ROS_AUTOMATIC_DISCOVERY_RANGE. Omit to preserve shell value.",
    )
    parser.add_argument(
        "--node-name",
        default=None,
        help="Optional ROS2 publisher node-name hint used during topic endpoint validation.",
    )
    parser.add_argument(
        "--print-json",
        action="store_true",
        help="Print structured JSON evidence in addition to text logs.",
    )
    parser.add_argument(
        "--no-print-json",
        dest="print_json",
        action="store_false",
        help="Disable structured JSON evidence output.",
    )
    parser.set_defaults(print_json=True)
    return parser.parse_args(argv)


def normalize_datatype(raw: str) -> str:
    """Normalize ROS PointField datatype tokens into lower-case canonical names."""

    value = raw.strip().lower()
    if value.isdigit():
        return {
            "1": "int8",
            "2": "uint8",
            "3": "int16",
            "4": "uint16",
            "5": "int32",
            "6": "uint32",
            "7": "float32",
            "8": "float64",
        }.get(value, value)
    return value.replace("pointfield.", "").replace("sensor_msgs.msg.pointfield.", "")


def parse_pointcloud2_fields(echo_output: str) -> list[dict[str, str]]:
    """Parse `fields:` block from `ros2 topic echo` output."""

    fields: list[dict[str, str]] = []
    in_fields = False
    current: dict[str, str] = {}

    for line in echo_output.splitlines():
        if re.match(r"^\s*fields:\s*$", line):
            in_fields = True
            continue
        if not in_fields:
            continue

        if re.match(r"^\s*height:\s*", line):
            break
        if re.match(r"^\s*point_step:\s*", line):
            break

        name_m = re.match(r"^\s*-\s*name:\s*(\S+)", line)
        if not name_m:
            name_m = re.match(r"^\s*name:\s*(\S+)", line)
        if name_m:
            if current:
                fields.append(current)
            current = {"name": name_m.group(1)}
            continue

        if not current:
            continue

        datatype_m = re.match(r"^\s*datatype:\s*(.+)", line)
        if datatype_m:
            current["datatype"] = datatype_m.group(1).strip()
            continue

        offset_m = re.match(r"^\s*offset:\s*([0-9]+)", line)
        if offset_m:
            current["offset"] = offset_m.group(1)
            continue

        count_m = re.match(r"^\s*count:\s*([0-9]+)", line)
        if count_m:
            current["count"] = count_m.group(1)

    if current:
        fields.append(current)

    return fields


def validate_fields(fields: list[dict[str, str]], expected: str) -> dict[str, object]:
    """Validate `t` field existence and expected datatype."""

    normalized_expected = normalize_datatype(expected)
    lookup = {f.get("name"): normalize_datatype(f.get("datatype", "")) for f in fields if "name" in f}
    found = {k: v for k, v in lookup.items()}
    if EXPECTED_FIELD not in found:
        raise RuntimeError(f"PointCloud2 fields missing expected '{EXPECTED_FIELD}': {sorted(found.keys())}")

    actual = found[EXPECTED_FIELD]
    if actual != normalized_expected and normalized_expected not in actual and actual not in normalized_expected:
        raise RuntimeError(
            f"Field '{EXPECTED_FIELD}' has datatype '{actual}', expected '{normalized_expected}'."
        )

    return {
        "has_t": True,
        "t_type": actual,
        "field_count": len(fields),
        "field_names": sorted(found.keys()),
    }


def main(argv: list[str]) -> int:
    """Script entry point."""

    args = parse_args(argv)
    workspace_root = ros2env.find_workspace_root()
    ros2_root = ros2env.resolve_existing_path(args.ros2_root, "ROS2 root", workspace_root)
    pixi_python, ros2_script = ros2env.validate_ros2_root(ros2_root)
    env = ros2env.build_ros_env(ros2_root, args.rmw, args.discovery_range)

    print(f"[phase138h-pointcloud2-t] ROS2 root: {ros2_root}")
    print(f"[phase138h-pointcloud2-t] ros2-script.py: {ros2_script}")
    print(f"[phase138h-pointcloud2-t] topic={args.topic} wait={args.wait_seconds}s spin={args.echo_spin_seconds}s")

    print(f"--- topic info -v {args.topic} ---")
    try:
        topic_info = ros2env.wait_for_publisher(
            pixi_python,
            ros2_script,
            env,
            args.topic,
            args.wait_seconds,
            expected_type=POINTS_MSG_TYPE,
            node_name=args.node_name,
        )
    except TimeoutError as exc:
        ros2env.log_event(
            "phase138h-pointcloud2-t",
            f"wait_for_publisher timed out, falling back to direct topic-info probe: {exc}",
        )
        topic_info = ros2env.probe_topic_info(
            pixi_python,
            ros2_script,
            env,
            args.topic,
            timeout_seconds=min(15.0, max(1.0, args.echo_spin_seconds)),
        )
    print(topic_info.rstrip())

    if not ros2env.topic_info_has_publisher(topic_info, args.node_name):
        raise RuntimeError(f"No active publisher detected for {args.topic} in topic info output.")

    print(f"--- echo {args.topic} ---")
    echo_output = ros2env.echo_once(pixi_python, ros2_script, env, args.topic, POINTS_MSG_TYPE, args.echo_spin_seconds)
    print(echo_output.rstrip())

    if "Could not determine the type" in echo_output or not echo_output.strip():
        raise RuntimeError("Did not receive a valid PointCloud2 message from ros2 topic echo.")

    fields = parse_pointcloud2_fields(echo_output)
    if not fields:
        raise RuntimeError("Could not parse PointField metadata from pointcloud echo output.")

    result = validate_fields(fields, args.expected_type)
    print(f"[phase138h-pointcloud2-t] PASS: t field exists with dtype={result['t_type']}")
    print(f"[phase138h-pointcloud2-t] field_count={result['field_count']} fields={', '.join(result['field_names'])}")

    if args.print_json:
        print("PHASE138H_POINTCLOUD2_T_FIELD_JSON=" + json.dumps(result, sort_keys=True))

    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main(sys.argv[1:]))
    except KeyboardInterrupt:
        raise SystemExit(130)
    except Exception as exc:
        print(f"[phase138h-pointcloud2-t] FAIL: {exc}", file=sys.stderr)
        raise SystemExit(1)
