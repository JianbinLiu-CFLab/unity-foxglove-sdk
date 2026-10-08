#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

from __future__ import annotations

from .foundation import *
from .rviz_config import *
from .probe_runtime import *
from .validation import *

from pathlib import Path as _Phase192Path
_PHASE192_FACADE_FILE = _Phase192Path(__file__).resolve().parents[1] / "phase160_humble_lidar_deskew_acceptance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _Phase192Path, _PHASE192_FACADE_FILE

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

__all__ = [name for name in globals() if not name.startswith("__")]
