#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

from __future__ import annotations

from .foundation import *
from .rviz_runtime import *
from .image_republisher import *

from pathlib import Path as _Phase192Path
_PHASE192_FACADE_FILE = _Phase192Path(__file__).resolve().parents[1] / "launch_phase138m_rviz2.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _Phase192Path, _PHASE192_FACADE_FILE

def main(argv: list[str]) -> int:
    """Script entry point."""

    args = parse_args(argv)
    args.image_topic = normalize_topic(args.image_topic, "image topic")
    args.raw_image_topic = normalize_topic(args.raw_image_topic, "raw image topic")
    args.points_topic = normalize_topic(args.points_topic, "points topic")
    args.fixed_frame = normalize_frame(args.fixed_frame)
    args.camera_parent_frame = normalize_frame(args.camera_parent_frame)

    if args.image_republisher_child:
        return image_republisher_main(args)

    workspace_root = ros2env.find_workspace_root()
    ros2_root = ros2env.resolve_existing_path(args.ros2_root, "ROS2 root", workspace_root)
    pixi_python, ros2_script = ros2env.validate_ros2_root(ros2_root)
    env = ros2env.build_ros_env(ros2_root, args.rmw, args.discovery_range, args.domain_id)
    ros2_overlay = resolve_ros2_overlay(args, workspace_root)
    if ros2_overlay is not None:
        prepend_ros2_overlay(env, ros2_overlay)
    rviz_image_topic = (
        args.raw_image_topic if not args.no_image_republisher else compressed_transport_base_topic(args.image_topic)
    )
    rviz_image_transport = "raw" if not args.no_image_republisher else "compressed"
    rviz_config = write_rviz_config(
        workspace_root,
        rviz_image_topic,
        rviz_image_transport,
        args.points_topic,
        args.fixed_frame,
    )
    log_path = workspace_root / "build" / "rviz2" / f"phase138m_image_republisher_{sanitize_name(args.raw_image_topic)}.log"
    rviz_log_path = workspace_root / "build" / "rviz2" / "phase138m_rviz2.log"
    script_path = pathlib.Path(__file__).resolve()

    if not args.no_cleanup_stale:
        cleanup_stale_processes(script_path, rviz_config, args.fixed_frame, args.camera_parent_frame)

    print(f"[phase138m-rviz] ROS2 root: {ros2_root}")
    if ros2_overlay is not None:
        print(f"[phase138m-rviz] ROS2 overlay: {ros2_overlay}")
    else:
        print("[phase138m-rviz] ROS2 overlay: <none>")
    print(f"[phase138m-rviz] RMW_IMPLEMENTATION: {env.get('RMW_IMPLEMENTATION')}")
    print(f"[phase138m-rviz] ROS_DOMAIN_ID: {env.get('ROS_DOMAIN_ID')}")
    print(f"[phase138m-rviz] ROS_AUTOMATIC_DISCOVERY_RANGE: {env.get('ROS_AUTOMATIC_DISCOVERY_RANGE', '<unset>')}")
    print(f"[phase138m-rviz] compressed image: {args.image_topic}")
    print(f"[phase138m-rviz] RViz image: {rviz_image_topic} transport={rviz_image_transport}")
    print(f"[phase138m-rviz] PointCloud2: {args.points_topic}")
    print(f"[phase138m-rviz] fixed frame: {args.fixed_frame}")
    print(f"[phase138m-rviz] camera parent frame: {args.camera_parent_frame}")
    print(f"[phase138m-rviz] RViz2 config: {rviz_config}")
    print(f"[phase138m-rviz] RViz2 log: {rviz_log_path}")

    if not args.skip_topic_probe:
        probe_topic(pixi_python, ros2_script, env, args.image_topic, args.topic_probe_timeout)
        probe_topic(pixi_python, ros2_script, env, args.points_topic, args.topic_probe_timeout)
        probe_topic(pixi_python, ros2_script, env, "/tf", args.topic_probe_timeout)

    if args.dry_run:
        print("[phase138m-rviz] Dry run only; no processes launched.")
        return 0

    started_helpers: list[subprocess.Popen] = []
    with ros2env.cleanup_owned_processes_on_error(started_helpers):
        if not args.no_image_republisher:
            republisher = launch_image_republisher(
                pixi_python,
                env,
                args.image_topic,
                args.raw_image_topic,
                log_path,
            )
            started_helpers.append(republisher)
            wait_for_raw_image(pixi_python, env, args.raw_image_topic, args.raw_image_wait_seconds)

        if not args.no_camera_static_tf:
            static_tf = launch_static_camera_tf(
                pixi_python,
                ros2_script,
                env,
                args.fixed_frame,
                args.camera_parent_frame,
            )
            if static_tf is not None:
                started_helpers.append(static_tf)

        ros2env.launch_rviz(
            ros2_root,
            rviz_config,
            env,
            "phase138m-rviz",
            startup_check_seconds=1.5,
            window_wait_seconds=45.0,
            stdout_log_path=rviz_log_path,
        )
    print(
        textwrap.dedent(
            f"""
            [phase138m-rviz] RViz2 launched.
            Displays:
              Image: {rviz_image_topic}
              Image transport: {rviz_image_transport}
              PointCloud2: {args.points_topic}
              TF: /tf
            """
        ).strip()
    )
    return 0

__all__ = [name for name in globals() if not name.startswith("__")]
