from __future__ import annotations
from .process_support import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase162_lyrical_zenoh_player_smoke.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE

def build_echo_command(pixi_python: pathlib.Path, ros2_script: pathlib.Path, args: argparse.Namespace) -> list[str]:
    """Build the bounded ros2 topic echo command used by the smoke helper."""

    command = [
        str(pixi_python),
        str(ros2_script),
        "topic",
        "echo",
        "--once",
        args.topic,
        args.message_type,
        "--no-daemon",
        "--spin-time",
        str(args.spin_seconds),
    ]
    if args.message_type == "sensor_msgs/msg/PointCloud2":
        command.extend(
            [
                "--qos-reliability",
                "best_effort",
                "--qos-history",
                "keep_last",
                "--qos-depth",
                "1",
            ]
        )
    return command


def build_rviz_acceptance_args(args: argparse.Namespace) -> list[str]:
    """Build the Phase138U RViz2 acceptance argv for Lyrical Zenoh."""

    rviz_args = [
        "--ros2-root",
        str(args.ros2_root),
        "--raw-topic",
        args.topic,
        "--deskewed-topic",
        args.deskewed_topic,
        "--expected-frame-id",
        args.expected_frame_id,
        "--fixed-frame",
        args.fixed_frame,
        "--spin-seconds",
        str(args.spin_seconds),
        "--rmw",
        args.rmw_implementation,
        "--domain-id",
        str(args.domain_id),
        "--discovery-range",
        args.discovery_range,
        "--rviz-display-mode",
        args.rviz_display_mode,
    ]
    if not args.launch_rviz:
        rviz_args.append("--no-rviz")
    if args.skip_topic_probe:
        rviz_args.append("--skip-topic-probe")
    if not args.require_motion:
        rviz_args.append("--allow-static")
    return rviz_args


def run_phase138u_with_rviz_capture(
    rviz_args: list[str],
    captured_rviz_processes: list[subprocess.Popen],
) -> int:
    """Run Phase138U while capturing RViz2 processes it launches."""

    original_launch_rviz = phase138u.ros2env.launch_rviz

    def launch_rviz_and_capture(*args, **kwargs):
        """Launch RViz2 through the original helper and remember the process."""

        process = original_launch_rviz(*args, **kwargs)
        captured_rviz_processes.append(process)
        return process

    phase138u.ros2env.launch_rviz = launch_rviz_and_capture
    try:
        return phase138u.main(rviz_args)
    finally:
        phase138u.ros2env.launch_rviz = original_launch_rviz


__all__ = [name for name in globals() if not name.startswith("__")]
