#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Module: Scripts/smoke
# Purpose: Phase160 Humble PointCloud2 + TF RViz2 acceptance helper.

"""Launch the Phase160 Humble PointCloud2 + TF RViz2 acceptance view.

Start Unity Play Mode with the Humble Win64 R2FU runtime selected, ROS2 Native
enabled, and PointCloud2 Native publishing active. This helper uses the
repo-local ``ros2-windows/ros2_humble`` entrypoint for external probes only; do
not source that environment inside Unity.

The default operator path mirrors the Phase138U / Phase146B acceptance shape:
launch RViz2 immediately with the raw and deskewed PointCloud2 displays, then
manually confirm that the visible point cloud follows the moving vehicle. Pass
``--probe`` only when direct rclpy PointCloud2/TF evidence is also needed.
"""

from __future__ import annotations

import argparse
import json
import math
import pathlib
import re
import subprocess
import sys
import textwrap
from typing import Any

import _ros2_windows_env as ros2env


DEFAULT_RAW_TOPIC = "/unity/point_cloud2"
DEFAULT_DESKEWED_TOPIC = "/unity/point_cloud2_deskewed"
DEFAULT_TF_TOPIC = "/tf"
DEFAULT_FIXED_FRAME = "map"
DEFAULT_BASE_FRAME = "base_link"
DEFAULT_SENSOR_FRAME = "os_sensor"
DEFAULT_POINT_FRAME = "os_lidar"
RESULT_MARKER = "PHASE160_RESULT_JSON:"


class AcceptanceError(RuntimeError):
    """Raised when Phase160 Humble evidence does not satisfy the contract."""


def parse_args(argv: list[str]) -> argparse.Namespace:
    """Parse command-line arguments."""

    workspace_root = ros2env.find_workspace_root()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--ros2-root", default=str(ros2env.default_ros2_root("humble", workspace_root)))
    parser.add_argument("--raw-topic", default=DEFAULT_RAW_TOPIC)
    parser.add_argument("--deskewed-topic", default=DEFAULT_DESKEWED_TOPIC)
    parser.add_argument("--tf-topic", default=DEFAULT_TF_TOPIC)
    parser.add_argument("--fixed-frame", default=DEFAULT_FIXED_FRAME)
    parser.add_argument("--base-frame", default=DEFAULT_BASE_FRAME)
    parser.add_argument("--sensor-frame", default=DEFAULT_SENSOR_FRAME)
    parser.add_argument("--point-frame", default=DEFAULT_POINT_FRAME)
    parser.add_argument("--spin-seconds", type=float, default=20.0)
    parser.add_argument("--motion-threshold-m", type=float, default=0.02)
    parser.add_argument("--require-motion", action="store_true")
    parser.add_argument(
        "--probe",
        action="store_true",
        help="After launching RViz2, run direct rclpy PointCloud2/TF checks.",
    )
    parser.add_argument(
        "--required-clouds",
        choices=("raw", "deskewed", "both", "any"),
        default="raw",
        help=(
            "PointCloud2 streams required for pass. Phase160 defaults to raw runtime/DDS "
            "wiring; use 'both' only when motion-compensated deskew output is enabled."
        ),
    )
    parser.add_argument("--no-rviz", action="store_true")
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--self-test", action="store_true")
    parser.add_argument("--rmw", default="rmw_fastrtps_cpp")
    parser.add_argument("--domain-id", default=None)
    parser.add_argument(
        "--discovery-range",
        choices=("LOCALHOST", "SUBNET", "OFF", "SYSTEM_DEFAULT"),
        default="SUBNET",
    )
    parser.add_argument("--rviz-display-mode", choices=("both", "raw"), default="both")
    parser.add_argument("--rviz-startup-check-seconds", type=float, default=1.5)
    parser.add_argument("--rviz-window-wait-seconds", type=float, default=0.0)
    return parser.parse_args(argv)


def normalize_topic(topic: str) -> str:
    """Normalize a ROS topic."""

    value = (topic or "").strip()
    if not value:
        raise ValueError("topic must not be empty")
    return value if value.startswith("/") else "/" + value


def normalize_frame(frame: str) -> str:
    """Normalize a TF frame id."""

    value = (frame or "").strip().strip("/")
    if not value:
        raise ValueError("frame id must not be empty")
    return value


def suffix(value: str) -> str:
    """Return a filesystem-safe suffix."""

    return re.sub(r"[^A-Za-z0-9_.-]+", "_", value.strip("/")) or "phase160"


def pointcloud_display(name: str, topic: str, color: str, size: int) -> str:
    """Return one RViz2 PointCloud2 display YAML fragment."""

    return f"""    - Class: rviz_default_plugins/PointCloud2
      Enabled: true
      Name: {name} {topic}
      Topic:
        Depth: 10
        Durability Policy: Volatile
        History Policy: Keep Last
        Reliability Policy: Reliable
        Value: {topic}
      Style: Points
      Size (Pixels): {size}
      Color: {color}
      Color Transformer: FlatColor
      Queue Size: 10
"""


def write_rviz_config(
    workspace_root: pathlib.Path,
    raw_topic: str,
    deskewed_topic: str,
    tf_topic: str,
    fixed_frame: str,
    display_mode: str,
) -> pathlib.Path:
    """Write a Phase160-specific runtime RViz2 config."""

    pointcloud_displays = pointcloud_display("Phase160 Raw PointCloud2", raw_topic, "255; 0; 0", 2)
    if display_mode == "both":
        pointcloud_displays += pointcloud_display(
            "Phase160 Deskewed PointCloud2",
            deskewed_topic,
            "0; 255; 255",
            3,
        )

    config = f"""Panels:
  - Class: rviz_common/Displays
    Name: Displays
  - Class: rviz_common/Views
    Name: Views
  - Class: rviz_common/Time
    Experimental: false
    Name: Time
    SyncMode: 0
    SyncSource: ""
Visualization Manager:
  Class: ""
  Displays:
    - Alpha: 0.5
      Cell Size: 1
      Class: rviz_default_plugins/Grid
      Color: 160; 160; 164
      Enabled: true
      Name: Phase160 Grid
      Plane: XY
      Reference Frame: <Fixed Frame>
    - Class: rviz_default_plugins/TF
      Enabled: true
      Frame Timeout: 15
      Name: Phase160 TF {tf_topic}
      Show Axes: true
      Show Names: true
      Topic:
        Depth: 20
        Durability Policy: Volatile
        History Policy: Keep Last
        Reliability Policy: Reliable
        Value: {tf_topic}
{pointcloud_displays}  Enabled: true
  Global Options:
    Background Color: 48; 48; 48
    Fixed Frame: {fixed_frame}
    Frame Rate: 30
  Name: Phase160 Humble root
  Tools:
    - Class: rviz_default_plugins/Interact
      Hide Inactive Objects: true
    - Class: rviz_default_plugins/MoveCamera
    - Class: rviz_default_plugins/Select
    - Class: rviz_default_plugins/FocusCamera
    - Class: rviz_default_plugins/Measure
  Value: true
  Views:
    Current:
      Class: rviz_default_plugins/Orbit
      Distance: 12
      Focal Point:
        X: 0
        Y: 0
        Z: 0
      Name: Current View
      Near Clip Distance: 0.01
      Pitch: 0.785398
      Target Frame: <Fixed Frame>
      Value: Phase160 Orbit
      Yaw: 0.785398
    Saved: ~
Window Geometry:
  Height: 960
  Width: 1600
  X: 60
  Y: 60
"""
    output_dir = workspace_root / "build" / "rviz2"
    output_dir.mkdir(parents=True, exist_ok=True)
    path = output_dir / (
        f"phase160_humble_{suffix(raw_topic)}_{suffix(deskewed_topic)}_{display_mode}.rviz"
    )
    path.write_text(config, encoding="utf-8")
    return path


def probe_script_text() -> str:
    """Return the rclpy probe script executed by the Humble Python runtime."""

    return textwrap.dedent(
        r'''
        import argparse
        import json
        import math
        import time

        import rclpy
        from rclpy.qos import HistoryPolicy, QoSProfile, ReliabilityPolicy
        from sensor_msgs.msg import PointCloud2
        from tf2_msgs.msg import TFMessage

        RESULT_MARKER = "PHASE160_RESULT_JSON:"


        def stamp_to_float(stamp):
            """Convert a ROS timestamp into floating-point seconds."""

            return float(stamp.sec) + float(stamp.nanosec) / 1_000_000_000.0


        def vector_dict(value):
            """Convert a ROS vector object into a JSON-friendly dictionary."""

            return {"x": float(value.x), "y": float(value.y), "z": float(value.z)}


        def quat_dict(value):
            """Convert a ROS quaternion object into a JSON-friendly dictionary."""

            return {
                "x": float(value.x),
                "y": float(value.y),
                "z": float(value.z),
                "w": float(value.w),
            }


        def distance(a, b):
            """Return the Euclidean distance between two vector dictionaries."""

            return math.sqrt(
                (float(a["x"]) - float(b["x"])) ** 2
                + (float(a["y"]) - float(b["y"])) ** 2
                + (float(a["z"]) - float(b["z"])) ** 2
            )


        def point_summary(msg):
            """Return a compact summary of a PointCloud2 message."""

            return {
                "frame_id": msg.header.frame_id,
                "stamp": {"sec": int(msg.header.stamp.sec), "nanosec": int(msg.header.stamp.nanosec)},
                "stampSeconds": stamp_to_float(msg.header.stamp),
                "height": int(msg.height),
                "width": int(msg.width),
                "point_step": int(msg.point_step),
                "row_step": int(msg.row_step),
                "is_dense": bool(msg.is_dense),
                "data_len": len(msg.data),
                "fields": [
                    {"name": field.name, "offset": int(field.offset), "datatype": int(field.datatype), "count": int(field.count)}
                    for field in msg.fields
                ],
            }


        def main():
            """Run the direct Humble PointCloud2 and TF probe."""

            parser = argparse.ArgumentParser()
            parser.add_argument("--raw-topic", required=True)
            parser.add_argument("--deskewed-topic", required=True)
            parser.add_argument("--tf-topic", required=True)
            parser.add_argument("--spin-seconds", type=float, required=True)
            args = parser.parse_args()

            rclpy.init()
            node = rclpy.create_node("phase160_humble_pointcloud_tf_probe")
            qos_reliable = QoSProfile(depth=20)
            qos_best_effort = QoSProfile(
                history=HistoryPolicy.KEEP_LAST,
                depth=20,
                reliability=ReliabilityPolicy.BEST_EFFORT,
            )
            subs = []
            points = {args.raw_topic: [], args.deskewed_topic: []}
            transforms = {}

            def point_cb(topic):
                """Create a bounded PointCloud2 callback for one topic."""

                def callback(msg):
                    """Record a compact PointCloud2 summary."""

                    bucket = points[topic]
                    if len(bucket) < 6:
                        bucket.append(point_summary(msg))
                    elif bucket:
                        bucket[-1] = point_summary(msg)
                return callback

            def tf_cb(msg):
                """Record bounded TF samples by parent-child edge."""

                for transform in msg.transforms:
                    parent = transform.header.frame_id.strip("/")
                    child = transform.child_frame_id.strip("/")
                    key = parent + "->" + child
                    entry = {
                        "stamp": {
                            "sec": int(transform.header.stamp.sec),
                            "nanosec": int(transform.header.stamp.nanosec),
                        },
                        "stampSeconds": stamp_to_float(transform.header.stamp),
                        "translation": vector_dict(transform.transform.translation),
                        "rotation": quat_dict(transform.transform.rotation),
                    }
                    bucket = transforms.setdefault(key, [])
                    if len(bucket) < 12:
                        bucket.append(entry)
                    else:
                        bucket[-1] = entry

            subs.append(node.create_subscription(PointCloud2, args.raw_topic, point_cb(args.raw_topic), qos_reliable))
            subs.append(node.create_subscription(PointCloud2, args.raw_topic, point_cb(args.raw_topic), qos_best_effort))
            subs.append(node.create_subscription(PointCloud2, args.deskewed_topic, point_cb(args.deskewed_topic), qos_reliable))
            subs.append(node.create_subscription(PointCloud2, args.deskewed_topic, point_cb(args.deskewed_topic), qos_best_effort))
            subs.append(node.create_subscription(TFMessage, args.tf_topic, tf_cb, qos_reliable))

            deadline = time.monotonic() + args.spin_seconds
            while time.monotonic() < deadline:
                rclpy.spin_once(node, timeout_sec=0.1)

            tf_summary = {}
            for key, bucket in transforms.items():
                first = bucket[0]
                last = bucket[-1]
                tf_summary[key] = {
                    "count": len(bucket),
                    "first": first,
                    "last": last,
                    "translationDeltaM": distance(first["translation"], last["translation"]),
                    "stampDeltaSeconds": float(last["stampSeconds"]) - float(first["stampSeconds"]),
                }

            result = {
                "pointClouds": {
                    topic: {
                        "count": len(samples),
                        "first": samples[0] if samples else None,
                        "last": samples[-1] if samples else None,
                    }
                    for topic, samples in points.items()
                },
                "transforms": tf_summary,
            }
            print(RESULT_MARKER + json.dumps(result, sort_keys=True))
            node.destroy_node()
            rclpy.shutdown()


        if __name__ == "__main__":
            main()
        '''
    ).strip() + "\n"


def write_probe_script(workspace_root: pathlib.Path) -> pathlib.Path:
    """Write the temporary rclpy probe script under build/."""

    output_dir = workspace_root / "build" / "ros2_smoke"
    output_dir.mkdir(parents=True, exist_ok=True)
    path = output_dir / "phase160_humble_pointcloud_tf_probe.py"
    path.write_text(probe_script_text(), encoding="utf-8")
    return path


def run_probe(
    pixi_python: pathlib.Path,
    env: dict[str, str],
    probe_script: pathlib.Path,
    raw_topic: str,
    deskewed_topic: str,
    tf_topic: str,
    spin_seconds: float,
) -> dict[str, Any]:
    """Run the direct rclpy probe and return its JSON summary."""

    command = [
        str(pixi_python),
        str(probe_script),
        "--raw-topic",
        raw_topic,
        "--deskewed-topic",
        deskewed_topic,
        "--tf-topic",
        tf_topic,
        "--spin-seconds",
        str(spin_seconds),
    ]
    result = subprocess.run(
        command,
        env=env,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        check=False,
        timeout=spin_seconds + 15.0,
    )
    print(result.stdout, end="" if result.stdout.endswith("\n") else "\n")
    if result.returncode != 0:
        raise AcceptanceError(f"Phase160 rclpy probe failed with exit {result.returncode}.")

    for line in result.stdout.splitlines():
        if line.startswith(RESULT_MARKER):
            return json.loads(line[len(RESULT_MARKER):])
    raise AcceptanceError("Phase160 rclpy probe did not emit a JSON result marker.")


def required_tf_keys(fixed_frame: str, base_frame: str, sensor_frame: str, point_frame: str) -> list[str]:
    """Return the expected dynamic TF chain."""

    return [
        f"{fixed_frame}->{base_frame}",
        f"{base_frame}->{sensor_frame}",
        f"{sensor_frame}->{point_frame}",
    ]


def validate_summary(
    summary: dict[str, Any],
    raw_topic: str,
    deskewed_topic: str,
    required_clouds: str,
    fixed_frame: str,
    base_frame: str,
    sensor_frame: str,
    point_frame: str,
    allow_static: bool,
    motion_threshold_m: float,
) -> None:
    """Validate direct PointCloud2 + TF evidence."""

    point_clouds = summary.get("pointClouds", {})
    if required_clouds == "raw":
        required_topics = (raw_topic,)
    elif required_clouds == "deskewed":
        required_topics = (deskewed_topic,)
    elif required_clouds == "both":
        required_topics = (raw_topic, deskewed_topic)
    else:
        raw_count = int((point_clouds.get(raw_topic) or {}).get("count") or 0)
        deskewed_count = int((point_clouds.get(deskewed_topic) or {}).get("count") or 0)
        if raw_count <= 0 and deskewed_count <= 0:
            raise AcceptanceError(
                f"Neither PointCloud2 topic produced samples: {raw_topic}, {deskewed_topic}."
            )
        required_topics = tuple(
            topic for topic in (raw_topic, deskewed_topic)
            if int((point_clouds.get(topic) or {}).get("count") or 0) > 0
        )

    for topic in required_topics:
        record = point_clouds.get(topic) or {}
        if int(record.get("count") or 0) <= 0:
            raise AcceptanceError(f"PointCloud2 topic {topic} produced no samples.")
        last = record.get("last") or {}
        frame_id = (last.get("frame_id") or "").strip("/")
        if frame_id != point_frame:
            raise AcceptanceError(
                f"PointCloud2 topic {topic} frame_id is {frame_id!r}, expected {point_frame!r}."
            )
        data_len = int(last.get("data_len") or 0)
        width = int(last.get("width") or 0)
        if data_len <= 0 or width <= 0:
            raise AcceptanceError(
                f"PointCloud2 topic {topic} has empty payload width={width} data_len={data_len}."
            )

    transforms = summary.get("transforms", {})
    for key in required_tf_keys(fixed_frame, base_frame, sensor_frame, point_frame):
        if key not in transforms:
            available = ", ".join(sorted(transforms)) or "<none>"
            raise AcceptanceError(f"Missing TF edge {key}; available TF edges: {available}")

    base_key = f"{fixed_frame}->{base_frame}"
    base_record = transforms[base_key]
    base_delta = float(base_record.get("translationDeltaM") or 0.0)
    if not allow_static and base_delta < motion_threshold_m:
        raise AcceptanceError(
            f"TF edge {base_key} did not move enough: delta={base_delta:.4f} m "
            f"< threshold={motion_threshold_m:.4f} m. Move the vehicle for motion acceptance, "
            "or omit --require-motion for static runtime wiring smoke."
        )


def print_summary(summary: dict[str, Any]) -> None:
    """Print a compact human-readable evidence summary."""

    print("[phase160-humble] Direct subscriber evidence:")
    for topic, record in sorted((summary.get("pointClouds") or {}).items()):
        last = record.get("last") or {}
        print(
            f"  PointCloud2 {topic}: samples={record.get('count')} "
            f"frame={last.get('frame_id')} width={last.get('width')} data_len={last.get('data_len')} "
            f"stamp={last.get('stamp')}"
        )
    for key, record in sorted((summary.get("transforms") or {}).items()):
        print(
            f"  TF {key}: samples={record.get('count')} "
            f"translationDeltaM={float(record.get('translationDeltaM') or 0.0):.4f} "
            f"stampDeltaSeconds={float(record.get('stampDeltaSeconds') or 0.0):.3f}"
        )


def synthetic_summary() -> dict[str, Any]:
    """Return a representative moving vehicle summary for self-test."""

    fields = [
        {"name": "x", "offset": 0, "datatype": 7, "count": 1},
        {"name": "y", "offset": 4, "datatype": 7, "count": 1},
        {"name": "z", "offset": 8, "datatype": 7, "count": 1},
    ]
    point = {
        "frame_id": DEFAULT_POINT_FRAME,
        "stamp": {"sec": 10, "nanosec": 0},
        "height": 1,
        "width": 128,
        "point_step": 16,
        "row_step": 2048,
        "is_dense": True,
        "data_len": 2048,
        "fields": fields,
    }
    edge = {
        "count": 2,
        "first": {"translation": {"x": 0, "y": 0, "z": 0}},
        "last": {"translation": {"x": 0.2, "y": 0, "z": 0}},
        "translationDeltaM": 0.2,
        "stampDeltaSeconds": 1.0,
    }
    static_edge = {
        "count": 2,
        "first": {"translation": {"x": 0, "y": 0, "z": 0}},
        "last": {"translation": {"x": 0, "y": 0, "z": 0}},
        "translationDeltaM": 0.0,
        "stampDeltaSeconds": 1.0,
    }
    return {
        "pointClouds": {
            DEFAULT_RAW_TOPIC: {"count": 1, "first": point, "last": point},
            DEFAULT_DESKEWED_TOPIC: {"count": 1, "first": point, "last": point},
        },
        "transforms": {
            f"{DEFAULT_FIXED_FRAME}->{DEFAULT_BASE_FRAME}": edge,
            f"{DEFAULT_BASE_FRAME}->{DEFAULT_SENSOR_FRAME}": static_edge,
            f"{DEFAULT_SENSOR_FRAME}->{DEFAULT_POINT_FRAME}": static_edge,
        },
    }


def run_self_test() -> int:
    """Run validation logic without ROS2."""

    summary = synthetic_summary()
    validate_summary(
        summary,
        DEFAULT_RAW_TOPIC,
        DEFAULT_DESKEWED_TOPIC,
        "both",
        DEFAULT_FIXED_FRAME,
        DEFAULT_BASE_FRAME,
        DEFAULT_SENSOR_FRAME,
        DEFAULT_POINT_FRAME,
        allow_static=False,
        motion_threshold_m=0.02,
    )
    print_summary(summary)
    print("[phase160-humble] Self-test passed.")
    return 0


def main(argv: list[str]) -> int:
    """Launch Phase160 Humble RViz2 acceptance and optionally probe ROS2 data."""

    args = parse_args(argv)
    if args.self_test:
        return run_self_test()

    workspace_root = ros2env.find_workspace_root()
    raw_topic = normalize_topic(args.raw_topic)
    deskewed_topic = normalize_topic(args.deskewed_topic)
    tf_topic = normalize_topic(args.tf_topic)
    fixed_frame = normalize_frame(args.fixed_frame)
    base_frame = normalize_frame(args.base_frame)
    sensor_frame = normalize_frame(args.sensor_frame)
    point_frame = normalize_frame(args.point_frame)

    ros2_root = ros2env.resolve_existing_path(args.ros2_root, "ROS2 root", workspace_root)
    pixi_python, ros2_script = ros2env.validate_ros2_root(ros2_root)
    env = ros2env.build_ros_env(
        ros2_root,
        rmw_implementation=args.rmw,
        discovery_range=args.discovery_range,
        domain_id=args.domain_id,
        ros_distro="humble",
    )

    print("[phase160-humble] Unity must run the Humble Win64 runtime package in standalone mode.")
    print("[phase160-humble] External ROS2/RViz2 tools use repo-local ros2-windows/ros2_humble.")
    print("[phase160-humble] Humble is FastRTPS-only; no Zenoh router is expected.")
    print(
        "[phase160-humble] Default acceptance launches RViz2 first; visually confirm "
        "the point cloud follows the moving vehicle."
    )
    if args.probe and args.require_motion:
        print("[phase160-humble] Probe motion proof is required; move the vehicle while the probe runs.")
    elif args.probe:
        print("[phase160-humble] Probe static wiring smoke is allowed; pass --require-motion for TF motion proof.")
    print(f"[phase160-humble] ROS2 root: {ros2_root}")
    print(f"[phase160-humble] ros2-script.py: {ros2_script}")
    print(f"[phase160-humble] raw={raw_topic} deskewed={deskewed_topic} tf={tf_topic}")
    print(f"[phase160-humble] RViz2 display mode: {args.rviz_display_mode}")
    if args.probe:
        print(f"[phase160-humble] required PointCloud2 streams for probe: {args.required_clouds}")
    print(
        "[phase160-humble] expected TF chain: "
        + " -> ".join([fixed_frame, base_frame, sensor_frame, point_frame])
    )

    rviz_config = write_rviz_config(
        workspace_root,
        raw_topic,
        deskewed_topic,
        tf_topic,
        fixed_frame,
        args.rviz_display_mode,
    )
    print(f"[phase160-humble] RViz2 config: {rviz_config}")

    if args.dry_run:
        return 0

    if not args.no_rviz:
        process = ros2env.launch_rviz(
            ros2_root,
            rviz_config,
            env,
            "phase160-humble",
            startup_check_seconds=args.rviz_startup_check_seconds,
            window_wait_seconds=args.rviz_window_wait_seconds,
        )
        print(f"[phase160-humble] RViz2 pid={process.pid}")
    else:
        print("[phase160-humble] RViz2 launch skipped by --no-rviz.")

    if args.no_rviz and not args.probe:
        print("[phase160-humble] Nothing else to do because --no-rviz was supplied without --probe.")
        return 0

    if args.probe:
        probe_script = write_probe_script(workspace_root)
        summary = run_probe(
            pixi_python,
            env,
            probe_script,
            raw_topic,
            deskewed_topic,
            tf_topic,
            args.spin_seconds,
        )
        print_summary(summary)
        validate_summary(
            summary,
            raw_topic,
            deskewed_topic,
            args.required_clouds,
            fixed_frame,
            base_frame,
            sensor_frame,
            point_frame,
            allow_static=not args.require_motion,
            motion_threshold_m=args.motion_threshold_m,
        )
        print("[phase160-humble] PASS: direct PointCloud2 samples are connected to the expected TF chain.")
        return 0

    print("[phase160-humble] RViz2 launched. Manual pass criterion: the point cloud follows the moving vehicle.")
    print("[phase160-humble] Use --probe for optional direct PointCloud2/TF evidence.")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main(sys.argv[1:]))
    except KeyboardInterrupt:
        raise SystemExit(130)
    except Exception as exc:
        print(f"[phase160-humble] FAIL: {exc}", file=sys.stderr)
        print(
            "[phase160-humble] Check Unity Console, stale ROS2/RViz2 processes, and the Humble entrypoint. "
            "The default acceptance path should launch RViz2 before any optional probe work.",
            file=sys.stderr,
        )
        raise SystemExit(1)
