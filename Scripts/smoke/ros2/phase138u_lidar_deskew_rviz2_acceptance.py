#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Module: Scripts/smoke
# Purpose: ROS2-side acceptance helper for Phase138U raw + deskewed PointCloud2 DDS.

"""Validate raw and deskewed Phase138U PointCloud2 topics and launch RViz2.

Start Unity Play Mode first with ROS2 Native (R2FU), PointCloud2 Native mode,
and PointCloud Motion Compensation enabled in RawAndDeskewedTopic mode. Then run:

    python Scripts/smoke/ros2/phase138u_lidar_deskew_rviz2_acceptance.py

The script launches RViz2 by default for visual comparison and uses direct rclpy
subscribers as the hard DDS acceptance gate. Pass --no-rviz when only the DDS
subscription check is needed.
"""

from __future__ import annotations

import argparse
import json
import pathlib
import subprocess
import struct
import sys

import _ros2_windows_env as ros2env
import launch_phase138u_lidar_deskew_rviz2 as rviz2launch


POINTCLOUD2_MSG_TYPE = "sensor_msgs/msg/PointCloud2"


class InconclusiveError(RuntimeError):
    """Raised when DDS is healthy but the capture cannot prove motion deskew."""


POINTCLOUD2_METRIC_HELPERS = r'''
import math
import random
import struct

POINT_FIELD_INT8 = 1
POINT_FIELD_UINT8 = 2
POINT_FIELD_INT16 = 3
POINT_FIELD_UINT16 = 4
POINT_FIELD_INT32 = 5
POINT_FIELD_UINT32 = 6
POINT_FIELD_FLOAT32 = 7
POINT_FIELD_FLOAT64 = 8

POINT_FIELD_FORMATS = {
    POINT_FIELD_INT8: "b",
    POINT_FIELD_UINT8: "B",
    POINT_FIELD_INT16: "h",
    POINT_FIELD_UINT16: "H",
    POINT_FIELD_INT32: "i",
    POINT_FIELD_UINT32: "I",
    POINT_FIELD_FLOAT32: "f",
    POINT_FIELD_FLOAT64: "d",
}


def read_point_field(data, base, field, endian):
    """Read one scalar PointField value from a PointCloud2 data buffer."""

    fmt = POINT_FIELD_FORMATS.get(int(field.datatype))
    if fmt is None:
        return None
    return struct.unpack_from(endian + fmt, data, base + int(field.offset))[0]


def extract_pointcloud2_points(msg, max_points):
    """Extract XYZ, ring, and time fields from a PointCloud2 sample."""

    fields = {field.name: field for field in msg.fields}
    required = ("x", "y", "z")
    if not all(name in fields for name in required):
        return []

    endian = ">" if bool(msg.is_bigendian) else "<"
    point_step = int(msg.point_step)
    if point_step <= 0:
        return []

    count = int(msg.width) * int(msg.height)
    if count <= 0:
        return []

    stride = max(1, count // max(1, int(max_points)))
    points = []
    for point_index in range(0, count, stride):
        base = point_index * point_step
        if base + point_step > len(msg.data):
            break

        x = float(read_point_field(msg.data, base, fields["x"], endian))
        y = float(read_point_field(msg.data, base, fields["y"], endian))
        z = float(read_point_field(msg.data, base, fields["z"], endian))
        if not (math.isfinite(x) and math.isfinite(y) and math.isfinite(z)):
            continue

        ring = 0
        if "ring" in fields:
            ring_value = read_point_field(msg.data, base, fields["ring"], endian)
            if ring_value is not None:
                ring = int(ring_value)

        time_value = float(point_index)
        if "time_offset" in fields:
            time_value = float(read_point_field(msg.data, base, fields["time_offset"], endian))
        elif "t" in fields:
            time_value = float(read_point_field(msg.data, base, fields["t"], endian))

        points.append((x, y, z, ring, time_value, point_index))

    return points


def distance3(a, b):
    """Return Euclidean distance between two extracted PointCloud2 points."""

    dx = a[0] - b[0]
    dy = a[1] - b[1]
    dz = a[2] - b[2]
    return math.sqrt(dx * dx + dy * dy + dz * dz)


def ring_seam_metrics(points, min_ring_points):
    """Measure first/last seam distance for each LiDAR ring."""

    by_ring = {}
    for point in points:
        by_ring.setdefault(point[3], []).append(point)

    seams = []
    for ring, ring_points in by_ring.items():
        if len(ring_points) < int(min_ring_points):
            continue
        ordered = sorted(ring_points, key=lambda item: (item[4], item[5]))
        seams.append(
            {
                "ring": int(ring),
                "points": len(ordered),
                "distance_m": distance3(ordered[0], ordered[-1]),
                "first_time": float(ordered[0][4]),
                "last_time": float(ordered[-1][4]),
            }
        )

    if not seams:
        return {"ring_count": len(by_ring), "measured_rings": 0}

    distances = sorted(item["distance_m"] for item in seams)
    seams_sorted = sorted(seams, key=lambda item: item["distance_m"], reverse=True)
    return {
        "ring_count": len(by_ring),
        "measured_rings": len(seams),
        "median_distance_m": distances[len(distances) // 2],
        "max_distance_m": distances[-1],
        "largest": seams_sorted[:5],
    }


def coordinate_summary(points):
    """Return coordinate bounds and centroid for static/motion detection."""

    if not points:
        return {"available": False}

    min_x = min_y = min_z = float("inf")
    max_x = max_y = max_z = float("-inf")
    sum_x = sum_y = sum_z = 0.0
    min_time = float("inf")
    max_time = float("-inf")
    for point in points:
        x, y, z, _ring, time_value, _point_index = point
        min_x = min(min_x, x)
        min_y = min(min_y, y)
        min_z = min(min_z, z)
        max_x = max(max_x, x)
        max_y = max(max_y, y)
        max_z = max(max_z, z)
        sum_x += x
        sum_y += y
        sum_z += z
        min_time = min(min_time, time_value)
        max_time = max(max_time, time_value)

    count = float(len(points))
    return {
        "available": True,
        "bounds": {
            "x": [float(min_x), float(max_x)],
            "y": [float(min_y), float(max_y)],
            "z": [float(min_z), float(max_z)],
        },
        "centroid": [float(sum_x / count), float(sum_y / count), float(sum_z / count)],
        "time_span": [float(min_time), float(max_time)],
    }


def plane_from_points(a, b, c):
    """Return a normalized plane from three points, or None if degenerate."""

    ux = b[0] - a[0]
    uy = b[1] - a[1]
    uz = b[2] - a[2]
    vx = c[0] - a[0]
    vy = c[1] - a[1]
    vz = c[2] - a[2]
    nx = uy * vz - uz * vy
    ny = uz * vx - ux * vz
    nz = ux * vy - uy * vx
    norm = math.sqrt(nx * nx + ny * ny + nz * nz)
    if norm <= 1e-9:
        return None
    nx /= norm
    ny /= norm
    nz /= norm
    d = -(nx * a[0] + ny * a[1] + nz * a[2])
    return nx, ny, nz, d


def fit_plane_metrics(points, threshold_m, vertical_only):
    """Fit a dominant plane with deterministic RANSAC and return residuals."""

    if len(points) < 32:
        return {"available": False, "reason": "not enough points"}

    rng = random.Random(138)
    sample = points
    max_plane_points = 2500
    if len(sample) > max_plane_points:
        stride = max(1, len(sample) // max_plane_points)
        sample = sample[::stride][:max_plane_points]
    best = None
    iterations = 140
    threshold = float(threshold_m)
    for _ in range(iterations):
        a, b, c = rng.sample(sample, 3)
        plane = plane_from_points(a, b, c)
        if plane is None:
            continue
        nx, ny, nz, d = plane
        if vertical_only and abs(nz) > 0.45:
            continue

        inliers = 0
        sq_sum = 0.0
        for point in sample:
            residual = abs(nx * point[0] + ny * point[1] + nz * point[2] + d)
            if residual <= threshold:
                inliers += 1
                sq_sum += residual * residual
        if inliers == 0:
            continue
        rms = math.sqrt(sq_sum / inliers)
        candidate = (inliers, -rms, plane, rms)
        if best is None or candidate > best:
            best = candidate

    if best is None:
        return {"available": False, "reason": "no plane found"}

    inliers, neg_rms, plane, rms = best
    nx, ny, nz, d = plane
    return {
        "available": True,
        "inlier_count": int(inliers),
        "inlier_ratio": float(inliers / len(sample)),
        "rms_m": float(rms),
        "normal": [float(nx), float(ny), float(nz)],
        "d": float(d),
        "threshold_m": threshold,
    }


def compute_pointcloud2_metrics(msg, max_metric_points, plane_threshold_m, min_ring_points):
    """Compute motion-sensitive PointCloud2 geometry metrics."""

    points = extract_pointcloud2_points(msg, max_metric_points)
    metrics = {
        "parsed_point_count": len(points),
        "max_metric_points": int(max_metric_points),
        "coordinates": coordinate_summary(points),
        "ring_seam": ring_seam_metrics(points, min_ring_points),
    }
    metrics["best_vertical_plane"] = fit_plane_metrics(points, plane_threshold_m, vertical_only=True)
    metrics["best_any_plane"] = fit_plane_metrics(points, plane_threshold_m, vertical_only=False)
    return metrics
'''


def parse_args(argv: list[str]) -> argparse.Namespace:
    """Parse acceptance arguments."""

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--ros2-root", default=str(ros2env.DEFAULT_ROS2_ROOT))
    parser.add_argument("--raw-topic", default="/unity/point_cloud2")
    parser.add_argument("--deskewed-topic", default="/unity/point_cloud2_deskewed")
    parser.add_argument("--expected-frame-id", default="os_lidar")
    parser.add_argument("--fixed-frame", default="map")
    parser.add_argument("--spin-seconds", type=float, default=12.0)
    parser.add_argument("--rmw", default=None)
    parser.add_argument("--domain-id", default=None)
    parser.add_argument(
        "--discovery-range",
        choices=("LOCALHOST", "SUBNET", "OFF", "SYSTEM_DEFAULT"),
        default="SUBNET",
    )
    parser.add_argument("--print-json", action="store_true")
    parser.add_argument("--no-print-json", dest="print_json", action="store_false")
    parser.add_argument("--no-rviz", dest="launch_rviz", action="store_false")
    parser.add_argument("--skip-topic-probe", action="store_true")
    parser.add_argument("--max-metric-points", type=int, default=6000)
    parser.add_argument("--plane-threshold-m", type=float, default=0.05)
    parser.add_argument("--min-ring-points", type=int, default=16)
    parser.add_argument("--motion-delta-threshold-m", type=float, default=0.01)
    parser.add_argument("--wall-improvement-threshold-m", type=float, default=0.002)
    parser.add_argument("--allow-static", action="store_true")
    parser.add_argument("--rviz-display-mode", choices=("both", "raw"), default="both")
    parser.add_argument("--no-require-wall-improvement", dest="require_wall_improvement", action="store_false")
    parser.add_argument("--self-test", action="store_true")
    parser.set_defaults(print_json=True)
    parser.set_defaults(launch_rviz=True)
    parser.set_defaults(require_wall_improvement=True)
    return parser.parse_args(argv)


def normalize_topic(topic: str) -> str:
    """Normalize a topic to leading-slash form."""

    value = (topic or "").strip()
    if not value:
        raise ValueError("topic must not be empty")
    return value if value.startswith("/") else "/" + value


def subscribe_once_pointcloud2_pair(
    pixi_python: pathlib.Path,
    env: dict[str, str],
    raw_topic: str,
    deskewed_topic: str,
    spin_seconds: float,
    max_metric_points: int,
    plane_threshold_m: float,
    min_ring_points: int,
) -> dict[str, object]:
    """Receive raw and deskewed PointCloud2 messages in one rclpy process."""

    subscriber_code = POINTCLOUD2_METRIC_HELPERS + r'''
import json
import os
import sys
import time

import rclpy
from rclpy.qos import HistoryPolicy, QoSProfile, ReliabilityPolicy
from sensor_msgs.msg import PointCloud2

raw_topic = sys.argv[1]
deskewed_topic = sys.argv[2]
spin_seconds = float(sys.argv[3])
max_metric_points = int(sys.argv[4])
plane_threshold_m = float(sys.argv[5])
min_ring_points = int(sys.argv[6])
samples = {}

def capture(topic, msg):
    """Capture one PointCloud2 message with minimal work inside rclpy."""
    if topic in samples:
        return
    samples[topic] = msg

def summarize(topic, msg):
    """Summarize one captured PointCloud2 sample after subscriptions finish."""
    return {
        "topic": topic,
        "msg_type": "sensor_msgs/msg/PointCloud2",
        "frame_id": msg.header.frame_id,
        "stamp": {"sec": int(msg.header.stamp.sec), "nanosec": int(msg.header.stamp.nanosec)},
        "height": int(msg.height),
        "width": int(msg.width),
        "point_step": int(msg.point_step),
        "row_step": int(msg.row_step),
        "data_length": len(msg.data),
        "is_dense": bool(msg.is_dense),
        "fields": [field.name for field in msg.fields],
        "metrics": compute_pointcloud2_metrics(
            msg,
            max_metric_points,
            plane_threshold_m,
            min_ring_points,
        ),
    }

print("PHASE138U_SUBSCRIBER_STAGE=before_rclpy_init", flush=True)
rclpy.init(args=None)
print("PHASE138U_SUBSCRIBER_STAGE=after_rclpy_init", flush=True)
node = rclpy.create_node("phase138u_pointcloud2_direct_subscriber")
print("PHASE138U_SUBSCRIBER_STAGE=after_create_node", flush=True)
qos_reliable = QoSProfile(depth=10)
qos_best_effort = QoSProfile(
    history=HistoryPolicy.KEEP_LAST,
    depth=10,
    reliability=ReliabilityPolicy.BEST_EFFORT,
)
subs = []
for topic in (raw_topic, deskewed_topic):
    subs.append(node.create_subscription(PointCloud2, topic, lambda msg, t=topic: capture(t, msg), qos_reliable))
    subs.append(node.create_subscription(PointCloud2, topic, lambda msg, t=topic: capture(t, msg), qos_best_effort))
print("PHASE138U_SUBSCRIBER_STAGE=after_create_subscriptions", flush=True)

deadline = time.time() + spin_seconds
print("PHASE138U_SUBSCRIBER_STAGE=before_spin", flush=True)
while rclpy.ok() and len(samples) < 2 and time.time() < deadline:
    rclpy.spin_once(node, timeout_sec=0.2)
print("PHASE138U_SUBSCRIBER_STAGE=after_spin samples=" + str(len(samples)), flush=True)

missing = [topic for topic in (raw_topic, deskewed_topic) if topic not in samples]
if missing:
    print("Missing sensor_msgs/msg/PointCloud2 sample(s): " + ", ".join(missing), flush=True)
    partial = {}
    for topic, msg in samples.items():
        partial[topic] = {
            "topic": topic,
            "msg_type": "sensor_msgs/msg/PointCloud2",
            "frame_id": msg.header.frame_id,
            "width": int(msg.width),
            "data_length": len(msg.data),
            "fields": [field.name for field in msg.fields],
        }
    print("PHASE138U_PARTIAL_JSON=" + json.dumps(partial, sort_keys=True), flush=True)
    sys.stdout.flush()
    os._exit(2)
results = {
    raw_topic: summarize(raw_topic, samples[raw_topic]),
    deskewed_topic: summarize(deskewed_topic, samples[deskewed_topic]),
}
print("PHASE138U_POINTCLOUD2_JSON=" + json.dumps({"raw": results[raw_topic], "deskewed": results[deskewed_topic]}, sort_keys=True), flush=True)
sys.stdout.flush()
os._exit(0)
'''
    command = [
        str(pixi_python),
        "-c",
        subscriber_code,
        raw_topic,
        deskewed_topic,
        str(spin_seconds),
        str(max_metric_points),
        str(plane_threshold_m),
        str(min_ring_points),
    ]
    try:
        result = subprocess.run(
            command,
            env=env,
            text=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            check=False,
            timeout=spin_seconds + 8.0,
        )
    except subprocess.TimeoutExpired as exc:
        output = exc.stdout or exc.output or ""
        if isinstance(output, bytes):
            output = output.decode("utf-8", errors="replace")
        raise RuntimeError(
            "PointCloud2 direct rclpy subscriber timed out after "
            + f"{exc.timeout:.1f}s while waiting for raw/deskewed samples.\n"
            + output
        ) from exc
    if result.returncode != 0:
        raise RuntimeError(
            "PointCloud2 direct rclpy subscriber failed:\n"
            + "Unity must be in Play Mode, Enable Deskew must be on, and Output Policy should be RawAndDeskewedTopic.\n"
            + result.stdout)

    for line in reversed(result.stdout.splitlines()):
        if line.startswith("PHASE138U_POINTCLOUD2_JSON="):
            return json.loads(line[len("PHASE138U_POINTCLOUD2_JSON="):])
    raise RuntimeError("PointCloud2 direct subscriber did not print structured payload.\n" + result.stdout)


def validate_sample(sample: dict[str, object], expected_frame_id: str | None) -> None:
    """Validate one PointCloud2 sample."""

    if expected_frame_id and sample.get("frame_id") != expected_frame_id:
        raise RuntimeError(f"frame_id mismatch for {sample.get('topic')}: {sample.get('frame_id')} != {expected_frame_id}")
    if int(sample.get("width", 0)) <= 0:
        raise RuntimeError(f"PointCloud2 width is zero for {sample.get('topic')}")
    if int(sample.get("data_length", 0)) <= 0:
        raise RuntimeError(f"PointCloud2 data is empty for {sample.get('topic')}")


def coordinate_values(sample: dict[str, object]) -> list[float]:
    """Flatten stable coordinate summary values for raw/deskewed comparison."""

    metrics = sample.get("metrics") or {}
    coordinates = metrics.get("coordinates") or {}
    if not coordinates.get("available"):
        return []

    values: list[float] = []
    bounds = coordinates.get("bounds") or {}
    for axis in ("x", "y", "z"):
        axis_bounds = bounds.get(axis) or []
        if len(axis_bounds) == 2:
            values.extend(float(value) for value in axis_bounds)
    centroid = coordinates.get("centroid") or []
    if len(centroid) == 3:
        values.extend(float(value) for value in centroid)
    return values


def max_coordinate_delta(raw: dict[str, object], deskewed: dict[str, object]) -> float:
    """Return the largest raw-vs-deskewed coordinate-summary delta."""

    raw_values = coordinate_values(raw)
    deskewed_values = coordinate_values(deskewed)
    if len(raw_values) != len(deskewed_values) or not raw_values:
        return 0.0
    return max(abs(left - right) for left, right in zip(raw_values, deskewed_values))


def select_vertical_plane(sample: dict[str, object]):
    """Return the fitted vertical wall plane metric when available."""

    metrics = sample.get("metrics") or {}
    plane = metrics.get("best_vertical_plane") or {}
    return plane if plane.get("available") else None


def validate_motion_contract(evidence: dict[str, object], args: argparse.Namespace) -> dict[str, object]:
    """Validate that the live capture contains motion-sensitive deskew evidence."""

    raw = evidence["raw"]
    deskewed = evidence["deskewed"]
    geometry_delta = max_coordinate_delta(raw, deskewed)
    contract: dict[str, object] = {
        "geometry_delta_m": geometry_delta,
        "motion_delta_threshold_m": args.motion_delta_threshold_m,
    }

    if geometry_delta < args.motion_delta_threshold_m:
        contract["status"] = "static_allowed" if args.allow_static else "inconclusive_static_or_no_motion"
        if args.allow_static:
            return contract
        raise InconclusiveError(
            "raw and deskewed geometry are nearly identical "
            f"(max coordinate-summary delta {geometry_delta:.6f} m). "
            "Drive the vehicle during the capture, keep Enable Deskew on, and rerun; "
            "use --allow-static only for a DDS wiring check."
        )

    contract["status"] = "motion_detected"
    if not args.require_wall_improvement:
        return contract

    raw_plane = select_vertical_plane(raw)
    deskewed_plane = select_vertical_plane(deskewed)
    if raw_plane is None or deskewed_plane is None:
        raise RuntimeError(
            "motion was detected, but a vertical wall plane could not be fitted on both topics; "
            "point the LiDAR at the maze wall or pass --no-require-wall-improvement for DDS-only comparison."
        )

    raw_rms = float(raw_plane["rms_m"])
    deskewed_rms = float(deskewed_plane["rms_m"])
    improvement = raw_rms - deskewed_rms
    contract.update(
        {
            "raw_vertical_plane_rms_m": raw_rms,
            "deskewed_vertical_plane_rms_m": deskewed_rms,
            "wall_rms_improvement_m": improvement,
            "wall_improvement_threshold_m": args.wall_improvement_threshold_m,
        }
    )
    if improvement < args.wall_improvement_threshold_m:
        raise RuntimeError(
            "motion was detected, but deskewed vertical-wall RMS did not improve enough: "
            f"raw={raw_rms:.6f} m deskewed={deskewed_rms:.6f} m "
            f"improvement={improvement:.6f} m "
            f"threshold={args.wall_improvement_threshold_m:.6f} m."
        )

    contract["status"] = "pass"
    return contract


def run_self_test() -> int:
    """Exercise the PointCloud2 metric helpers without requiring ROS2."""

    namespace: dict[str, object] = {}
    exec(POINTCLOUD2_METRIC_HELPERS, namespace)

    class Field:
        """Minimal PointField stand-in for parser self-test data."""

        def __init__(self, name: str, offset: int, datatype: int) -> None:
            """Initialize a minimal field descriptor."""

            self.name = name
            self.offset = offset
            self.datatype = datatype

    class Message:
        """Minimal PointCloud2 stand-in for parser self-test data."""

        pass

    point_step = 20
    count = 40
    data = bytearray(point_step * count)
    for i in range(count):
        base = i * point_step
        y = i * 0.25
        z = 0.2 if i % 2 == 0 else 0.7
        struct.pack_into("<f", data, base + 0, 2.0)
        struct.pack_into("<f", data, base + 4, y)
        struct.pack_into("<f", data, base + 8, z)
        struct.pack_into("<H", data, base + 12, 2)
        struct.pack_into("<f", data, base + 16, i * 0.01)

    message = Message()
    message.fields = [
        Field("x", 0, 7),
        Field("y", 4, 7),
        Field("z", 8, 7),
        Field("ring", 12, 4),
        Field("time_offset", 16, 7),
    ]
    message.data = bytes(data)
    message.width = count
    message.height = 1
    message.point_step = point_step
    message.is_bigendian = False

    metrics = namespace["compute_pointcloud2_metrics"](message, 100, 0.01, 6)
    if metrics["parsed_point_count"] != count:
        raise RuntimeError(f"self-test parsed {metrics['parsed_point_count']} points, expected {count}")
    seam = metrics["ring_seam"]
    if seam.get("max_distance_m", 0.0) <= 2.0:
        raise RuntimeError(f"self-test seam metric too small: {seam}")
    plane = metrics["best_vertical_plane"]
    if not plane.get("available") or plane.get("rms_m", 1.0) > 1e-5:
        raise RuntimeError(f"self-test vertical plane metric failed: {plane}")

    print("[phase138u-lidar-deskew] self-test PASS")
    print(json.dumps(metrics, indent=2, sort_keys=True))
    return 0


def main(argv: list[str]) -> int:
    """Script entry point."""

    args = parse_args(argv)
    if args.self_test:
        return run_self_test()

    workspace_root = ros2env.find_workspace_root()
    ros2_root = ros2env.resolve_existing_path(args.ros2_root, "ROS2 root", workspace_root)
    pixi_python, ros2_script = ros2env.validate_ros2_root(ros2_root)
    env = ros2env.build_ros_env(
        ros2_root,
        rmw_implementation=args.rmw,
        discovery_range=args.discovery_range,
        domain_id=args.domain_id,
    )

    raw_topic = normalize_topic(args.raw_topic)
    deskewed_topic = normalize_topic(args.deskewed_topic)

    print(f"[phase138u-lidar-deskew] ROS2 root: {ros2_root}")
    print(f"[phase138u-lidar-deskew] ros2-script.py: {ros2_script}")
    print(f"[phase138u-lidar-deskew] raw={raw_topic} deskewed={deskewed_topic} spin={args.spin_seconds}s")
    print(
        "[phase138u-lidar-deskew] "
        f"RMW={env.get('RMW_IMPLEMENTATION')} "
        f"discovery={env.get('ROS_AUTOMATIC_DISCOVERY_RANGE', '<unset>')} "
        f"fastdds_transports={env.get('FASTDDS_BUILTIN_TRANSPORTS', '<unset>')}"
    )

    if args.launch_rviz:
        config_path = rviz2launch.write_config(
            workspace_root,
            raw_topic,
            deskewed_topic,
            rviz2launch.normalize_frame(args.fixed_frame),
            args.rviz_display_mode,
        )
        print(f"[phase138u-lidar-deskew] RViz2 config: {config_path}")
        rviz_process = ros2env.launch_rviz(
            ros2_root,
            config_path,
            env=env,
            log_prefix="phase138u-lidar-deskew",
            startup_check_seconds=1.5,
            window_wait_seconds=0.0,
        )
        print(f"[phase138u-lidar-deskew] RViz2 pid={rviz_process.pid}")

    if not args.skip_topic_probe:
        print("--- ros2 topic list -t --no-daemon ---")
        try:
            topic_list = ros2env.run_ros2(
                pixi_python,
                ros2_script,
                env,
                ["topic", "list", "-t", "--no-daemon"],
                check=False,
                timeout_seconds=5.0,
            ).stdout
            print(topic_list)
        except subprocess.TimeoutExpired:
            print("<topic list timed out after 5.0s>")

    evidence = subscribe_once_pointcloud2_pair(
        pixi_python,
        env,
        raw_topic,
        deskewed_topic,
        args.spin_seconds,
        args.max_metric_points,
        args.plane_threshold_m,
        args.min_ring_points,
    )

    validate_sample(evidence["raw"], args.expected_frame_id)
    validate_sample(evidence["deskewed"], args.expected_frame_id)

    if evidence["raw"]["width"] != evidence["deskewed"]["width"]:
        raise RuntimeError(
            f"raw/deskewed point count mismatch: {evidence['raw']['width']} != {evidence['deskewed']['width']}"
        )
    evidence["motion_contract"] = validate_motion_contract(evidence, args)

    print("--- structured evidence ---")
    print(json.dumps(evidence, indent=2, sort_keys=True))
    if args.print_json:
        print("PHASE138U_LIDAR_DESKEW_JSON=" + json.dumps(evidence, sort_keys=True))
    motion_status = evidence["motion_contract"].get("status")
    if motion_status == "static_allowed":
        print("[phase138u-lidar-deskew] PASS (DDS-WIRING-ONLY: static capture accepted)")
    else:
        print("[phase138u-lidar-deskew] PASS")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main(sys.argv[1:]))
    except KeyboardInterrupt:
        raise SystemExit(130)
    except InconclusiveError as exc:
        print(f"[phase138u-lidar-deskew] INCONCLUSIVE: {exc}", file=sys.stderr)
        raise SystemExit(2)
    except Exception as exc:
        print(f"[phase138u-lidar-deskew] FAIL: {exc}", file=sys.stderr)
        raise SystemExit(1)
