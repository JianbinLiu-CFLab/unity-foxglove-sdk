#!/usr/bin/env python3
"""Command-line entry points for Phase138U lidar deskew acceptance."""

from __future__ import annotations

import json
import subprocess
import struct

from . import pointcloud_metrics as _phase192_metrics
from .pointcloud_metrics import *
from .subscription import *

def run_self_test() -> int:
    """Exercise the PointCloud2 metric helpers without requiring ROS2."""

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

    metrics = _phase192_metrics.compute_pointcloud2_metrics(message, 100, 0.01, 6)
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
    validate_correlated_pair(evidence["raw"], evidence["deskewed"])

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


__all__ = ["run_self_test", "main"]
