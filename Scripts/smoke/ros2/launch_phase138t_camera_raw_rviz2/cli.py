#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

from __future__ import annotations

from .foundation import *
from .rviz_config import *
from .process_runtime import *

from pathlib import Path as _Phase192Path
_PHASE192_FACADE_FILE = _Phase192Path(__file__).resolve().parents[1] / "launch_phase138t_camera_raw_rviz2.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _Phase192Path, _PHASE192_FACADE_FILE

def main(argv: list[str]) -> int:
    """Entry point for this smoke script. Returns process exit status."""

    args = parse_args(argv)
    if args.camera_tf_fallback_child:
        return camera_tf_fallback_child(args)

    workspace_root = ros2env.find_workspace_root()
    ros2_root = ros2env.resolve_existing_path(args.ros2_root, "ROS2 root", workspace_root)
    topic = normalize_topic(args.topic)
    points_topic = normalize_topic(args.points_topic)
    fixed_frame = normalize_frame(args.fixed_frame)
    camera_parent_frame = normalize_frame(args.camera_parent_frame)
    pixi_python, ros2_script = ros2env.validate_ros2_root(ros2_root)
    env = ros2env.build_ros_env(ros2_root, args.rmw, args.discovery_range, args.domain_id)
    runtime_config = write_runtime_rviz_config(
        workspace_root,
        topic,
        points_topic,
        fixed_frame,
        not args.no_tf,
        not args.no_pointcloud,
    )
    script_path = pathlib.Path(__file__).resolve()

    print(f"[phase138t-rviz] ROS2 root: {ros2_root}")
    print(f"[phase138t-rviz] pixi Python: {pixi_python}")
    print(f"[phase138t-rviz] ros2-script.py: {ros2_script}")
    print(f"[phase138t-rviz] RMW_IMPLEMENTATION: {env.get('RMW_IMPLEMENTATION')}")
    print(f"[phase138t-rviz] ROS_DOMAIN_ID: {env.get('ROS_DOMAIN_ID')}")
    print(f"[phase138t-rviz] ROS_AUTOMATIC_DISCOVERY_RANGE: {env.get('ROS_AUTOMATIC_DISCOVERY_RANGE', '<unset>')}")
    print(f"[phase138t-rviz] Raw Image topic: {topic}")
    print(f"[phase138t-rviz] PointCloud2 topic: {points_topic if not args.no_pointcloud else '<disabled>'}")
    print(f"[phase138t-rviz] Fixed frame: {fixed_frame}")
    print(f"[phase138t-rviz] TF display: {'disabled' if args.no_tf else 'enabled'}")
    print(
        "[phase138t-rviz] Camera TF fallback: "
        + ("disabled" if args.no_camera_static_tf else f"{fixed_frame} -> {camera_parent_frame}")
    )
    print(f"[phase138t-rviz] Runtime RViz2 config: {runtime_config}")

    if not args.skip_topic_probe:
        probe_topic(
            pixi_python,
            ros2_script,
            env,
            topic,
            args.topic_probe_timeout,
            args.strict_topic_probe,
        )
        if not args.no_pointcloud:
            probe_topic(
                pixi_python,
                ros2_script,
                env,
                points_topic,
                args.topic_probe_timeout,
                args.strict_topic_probe,
            )
        if not args.no_tf:
            probe_topic(
                pixi_python,
                ros2_script,
                env,
                "/tf",
                args.topic_probe_timeout,
                args.strict_topic_probe,
            )

    if args.dry_run:
        print("[phase138t-rviz] Dry run only; RViz2 was not launched.")
        return 0

    if not args.no_cleanup_stale:
        cleanup_stale_processes(script_path, runtime_config, camera_parent_frame)

    if not args.no_camera_static_tf:
        launch_camera_tf_fallback(pixi_python, env, script_path, fixed_frame, camera_parent_frame, workspace_root)

    ros2env.launch_rviz(
        ros2_root,
        runtime_config,
        env,
        "phase138t-rviz",
        startup_check_seconds=args.rviz_startup_check_seconds,
        window_wait_seconds=args.rviz_window_wait_seconds,
    )
    print("[phase138t-rviz] RViz2 launched. Displays: raw Image, PointCloud2, TF.")
    return 0

__all__ = [name for name in globals() if not name.startswith("__")]
