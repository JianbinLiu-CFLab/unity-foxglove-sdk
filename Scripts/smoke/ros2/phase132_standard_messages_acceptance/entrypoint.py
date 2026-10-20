from __future__ import annotations
from .message_validators import *

def main(argv: list[str]) -> int:
    """Script entry point."""

    args = parse_args(argv)
    script_started = time.perf_counter()
    ros2env.log_event(
        "phase132",
        "script start "
        + f"launch_rviz={args.launch_rviz} wait_seconds={args.wait_seconds:.1f} "
        + f"topic_info_timeout_seconds={args.topic_info_timeout_seconds:.1f} "
        + f"echo_spin_seconds={args.echo_spin_seconds:.1f}",
    )
    workspace_root = ros2env.find_workspace_root()
    ros2_root = ros2env.resolve_existing_path(args.ros2_root, "ROS2 root", workspace_root)
    rviz_config = ros2env.resolve_existing_path(args.rviz_config, "RViz2 config", workspace_root)
    pixi_python, ros2_script = ros2env.validate_ros2_root(ros2_root)
    env = ros2env.build_ros_env(ros2_root, args.rmw, args.discovery_range, args.domain_id)

    print(f"[phase132] ROS2 root: {ros2_root}")
    print(f"[phase132] pixi Python: {pixi_python}")
    print(f"[phase132] ros2-script.py: {ros2_script}")
    print(f"[phase132] ROS_DISTRO: {env.get('ROS_DISTRO')}")
    print(f"[phase132] RMW_IMPLEMENTATION: {env.get('RMW_IMPLEMENTATION')}")
    print(f"[phase132] ROS_DOMAIN_ID: {env.get('ROS_DOMAIN_ID')}")
    print(f"[phase132] ROS_AUTOMATIC_DISCOVERY_RANGE: {env.get('ROS_AUTOMATIC_DISCOVERY_RANGE', '<unset>')}")
    print(f"[phase132] RViz2 config: {rviz_config}")

    if args.graph_diagnostics:
        print("--- node list (diagnostic) ---")
        stage_started = time.perf_counter()
        ros2env.log_event("phase132", "node list probe start")
        nodes = ros2env.probe_node_list(pixi_python, ros2_script, env)
        print(nodes.rstrip() or "<empty>")
        ros2env.log_event("phase132", f"node list probe done elapsed={time.perf_counter() - stage_started:.3f}s")

        for topic, _msg_type in TOPICS:
            print(f"--- topic info -v {topic} ---")
            stage_started = time.perf_counter()
            ros2env.log_event("phase132", f"topic info diagnostic start topic={topic}")
            info = ros2env.probe_topic_info(
                pixi_python,
                ros2_script,
                env,
                topic,
                timeout_seconds=args.topic_info_timeout_seconds,
            )
            print(info.rstrip())
            ros2env.log_event(
                "phase132",
                f"topic info diagnostic done topic={topic} elapsed={time.perf_counter() - stage_started:.3f}s",
            )
    else:
        ros2env.log_event("phase132", "graph diagnostics skipped; rclpy collection is the acceptance gate.")

    print("--- rclpy collect Phase132 topics ---")
    stage_started = time.perf_counter()
    ros2env.log_event("phase132", "rclpy collect start")
    summaries = collect_phase132_messages(pixi_python, env, args.echo_spin_seconds)
    ros2env.log_event("phase132", f"rclpy collect done elapsed={time.perf_counter() - stage_started:.3f}s")

    for topic, _msg_type in TOPICS:
        print(f"--- collected {topic} ---")
        summary = summaries.get(topic)
        if not isinstance(summary, dict):
            raise RuntimeError(f"Phase132 rclpy probe did not return a summary for {topic}: {summaries}")
        print(json.dumps(summary, indent=2, sort_keys=True))
        SUMMARY_VALIDATORS[topic](summary)

    if args.launch_rviz:
        ros2env.launch_rviz(
            ros2_root,
            rviz_config,
            env,
            "phase132",
            startup_check_seconds=args.rviz_startup_check_seconds,
            window_wait_seconds=args.rviz_window_wait_seconds,
        )
    else:
        ros2env.log_event("phase132", "RViz2 launch skipped because --no-launch-rviz was supplied.")

    print("[phase132] GREEN: standard ROS2 message expansion CLI checks completed.")
    print("[phase132] RViz2 is a visual helper for /pose and /camera/image_raw; rclpy collection remains the acceptance gate.")
    ros2env.log_event("phase132", f"script completed elapsed={time.perf_counter() - script_started:.3f}s")
    return 0


__all__ = [name for name in globals() if not name.startswith("__")]
