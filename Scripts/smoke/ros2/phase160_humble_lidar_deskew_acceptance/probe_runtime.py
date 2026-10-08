#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

from __future__ import annotations

from .foundation import *

from pathlib import Path as _Phase192Path
_PHASE192_FACADE_FILE = _Phase192Path(__file__).resolve().parents[1] / "phase160_humble_lidar_deskew_acceptance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _Phase192Path, _PHASE192_FACADE_FILE

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
    text = probe_script_text()
    if not path.exists() or path.read_text(encoding="utf-8", errors="replace") != text:
        path.write_text(text, encoding="utf-8")
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

__all__ = [name for name in globals() if not name.startswith("__")]
