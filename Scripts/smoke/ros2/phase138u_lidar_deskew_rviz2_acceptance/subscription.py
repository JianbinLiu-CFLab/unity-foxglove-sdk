"""Validate raw and deskewed Phase138U PointCloud2 topics and launch RViz2.

Start Unity Play Mode first with ROS2 Native (R2FU), PointCloud2 Native mode,
and PointCloud Motion Compensation enabled in RawAndDeskewedTopic mode. Then run:

    python Scripts/smoke/ros2/phase138u_lidar_deskew_rviz2_acceptance.py

The script launches RViz2 by default for visual comparison and uses direct rclpy
subscribers as the hard DDS acceptance gate. Pass --no-rviz when only the DDS
subscription check is needed.
"""

from __future__ import annotations

from .pointcloud_metrics import *

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
pending = {raw_topic: {}, deskewed_topic: {}}
last_seen = {}
max_pending_per_topic = 64

def capture(topic, msg):
    """Capture only a raw/deskewed pair with the same source stamp."""
    if len(samples) == 2:
        return
    stamp = (int(msg.header.stamp.sec), int(msg.header.stamp.nanosec))
    last_seen[topic] = msg
    topic_pending = pending[topic]
    topic_pending[stamp] = msg
    while len(topic_pending) > max_pending_per_topic:
        del topic_pending[next(iter(topic_pending))]
    other_topic = deskewed_topic if topic == raw_topic else raw_topic
    other = pending[other_topic].get(stamp)
    if other is None:
        return
    samples[topic] = msg
    samples[other_topic] = other

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

deadline = time.monotonic() + spin_seconds
print("PHASE138U_SUBSCRIBER_STAGE=before_spin", flush=True)
exit_code = 0
try:
    while rclpy.ok() and len(samples) < 2 and time.monotonic() < deadline:
        rclpy.spin_once(node, timeout_sec=0.2)
    print("PHASE138U_SUBSCRIBER_STAGE=after_spin samples=" + str(len(samples)), flush=True)

    if len(samples) < 2:
        print("No stamp-correlated raw/deskewed PointCloud2 pair was received.", flush=True)
        partial = {}
        for topic, msg in last_seen.items():
            partial[topic] = {
                "topic": topic,
                "msg_type": "sensor_msgs/msg/PointCloud2",
                "frame_id": msg.header.frame_id,
                "stamp": {"sec": int(msg.header.stamp.sec), "nanosec": int(msg.header.stamp.nanosec)},
                "width": int(msg.width),
                "data_length": len(msg.data),
                "fields": [field.name for field in msg.fields],
            }
        print("PHASE138U_PARTIAL_JSON=" + json.dumps(partial, sort_keys=True), flush=True)
        exit_code = 2
    else:
        results = {
            raw_topic: summarize(raw_topic, samples[raw_topic]),
            deskewed_topic: summarize(deskewed_topic, samples[deskewed_topic]),
        }
        print("PHASE138U_POINTCLOUD2_JSON=" + json.dumps({"raw": results[raw_topic], "deskewed": results[deskewed_topic]}, sort_keys=True), flush=True)
finally:
    node.destroy_node()
    if rclpy.ok():
        rclpy.shutdown()
sys.exit(exit_code)
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


def pointcloud_stamp(sample: dict[str, object]) -> tuple[int, int]:
    """Return one validated ROS2 header stamp."""

    stamp = sample.get("stamp")
    if not isinstance(stamp, dict):
        raise RuntimeError(f"PointCloud2 stamp is missing for {sample.get('topic')}")
    try:
        sec = int(stamp["sec"])
        nanosec = int(stamp["nanosec"])
    except (KeyError, TypeError, ValueError) as exc:
        raise RuntimeError(f"PointCloud2 stamp is invalid for {sample.get('topic')}: {stamp}") from exc
    if nanosec < 0 or nanosec >= 1_000_000_000:
        raise RuntimeError(f"PointCloud2 stamp nanosec is out of range for {sample.get('topic')}: {nanosec}")
    return sec, nanosec


def validate_correlated_pair(raw: dict[str, object], deskewed: dict[str, object]) -> None:
    """Require raw and deskewed evidence to describe the same LiDAR scan."""

    raw_stamp = pointcloud_stamp(raw)
    deskewed_stamp = pointcloud_stamp(deskewed)
    if raw_stamp != deskewed_stamp:
        raise RuntimeError(
            "raw/deskewed PointCloud2 stamp mismatch: "
            f"raw={raw_stamp[0]}.{raw_stamp[1]:09d} "
            f"deskewed={deskewed_stamp[0]}.{deskewed_stamp[1]:09d}"
        )


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




__all__ = ['parse_args', 'normalize_topic', 'subscribe_once_pointcloud2_pair', 'validate_sample', 'pointcloud_stamp', 'validate_correlated_pair', 'coordinate_values', 'max_coordinate_delta', 'select_vertical_plane', 'validate_motion_contract']
