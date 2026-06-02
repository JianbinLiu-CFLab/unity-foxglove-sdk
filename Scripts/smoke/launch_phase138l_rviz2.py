#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Module: Scripts/smoke
# Purpose: Launch RViz2 for Phase138L PointCloud2 Native product interaction.
#
# Launches RViz2 for the PointCloud2 Native product path while Unity is in Play mode.
# Expected setup:
# - FoxglovePointCloudPublisher output mode: PointCloud2 Native
# - Default topic: /unity/point_cloud2
# - Optional fixed frame override: --fixed-frame
# - Optional static TF fallback: --static-tf and --no-static-tf

_DESCRIPTION = (
    "Launch RViz2 for the PointCloud2 Native product path. "
    "The script writes a temporary RViz2 config with the selected PointCloud2 topic "
    "and then launches RViz2 using the pinned Windows ROS2 Jazzy environment."
)

from __future__ import annotations

import argparse
import pathlib
import re
import subprocess
import sys
import time

import _ros2_windows_env as ros2env


DEFAULT_RVIZ_CONFIG = pathlib.Path(
    r"Packages\dev.unity2foxglove.ros2forunity\Samples~"
    r"\Virtual LiDAR PointCloud2 Digital Twin\rviz2_phase138c_pointcloud2.rviz"
)
DEFAULT_POINTS_TOPIC = "/unity/point_cloud2"
DEFAULT_FIXED_FRAME = "os_lidar"
DEFAULT_TF_PARENT_FRAME = "map"


def parse_args(argv: list[str]) -> argparse.Namespace:
    """Parse launch arguments for RViz2 Phase 138L helper.

    Returns an argparse namespace used by the caller and main launch flow.
    """
    parser = argparse.ArgumentParser(description=_DESCRIPTION.strip())
    parser.add_argument(
        "--ros2-root",
        default=str(ros2env.DEFAULT_ROS2_ROOT),
        help="Windows ROS2 Jazzy root. Default: C:\\ros2_jazzy\\ros2-windows",
    )
    parser.add_argument(
        "--rviz-config",
        default=str(DEFAULT_RVIZ_CONFIG),
        help="Base RViz2 config. Relative paths resolve from the workspace root.",
    )
    parser.add_argument(
        "--points-topic",
        default=DEFAULT_POINTS_TOPIC,
        help=f"PointCloud2 topic to display. Default: {DEFAULT_POINTS_TOPIC}",
    )
    parser.add_argument(
        "--fixed-frame",
        default=DEFAULT_FIXED_FRAME,
        help=f"RViz fixed frame. Default: {DEFAULT_FIXED_FRAME}",
    )
    parser.add_argument(
        "--tf-parent-frame",
        default=DEFAULT_TF_PARENT_FRAME,
        help=f"Parent frame for the opt-in RViz static TF fallback. Default: {DEFAULT_TF_PARENT_FRAME}",
    )
    parser.add_argument(
        "--tf-child-frame",
        default=DEFAULT_FIXED_FRAME,
        help=f"Child frame for the opt-in RViz static TF fallback. Default: {DEFAULT_FIXED_FRAME}",
    )
    parser.add_argument(
        "--static-tf",
        action="store_true",
        help="Launch an opt-in map -> sensor static TF fallback. Product acceptance should use Unity's Publish TF Anchor instead.",
    )
    parser.add_argument(
        "--no-static-tf",
        action="store_true",
        help="Deprecated compatibility flag. Static TF fallback is disabled by default.",
    )
    parser.add_argument(
        "--rmw",
        default=None,
        help="RMW implementation. Omit to preserve RMW_IMPLEMENTATION or default to rmw_fastrtps_cpp.",
    )
    parser.add_argument(
        "--domain-id",
        default=None,
        help="ROS_DOMAIN_ID override. Omit for the helper default.",
    )
    parser.add_argument(
        "--discovery-range",
        choices=("LOCALHOST", "SUBNET", "OFF", "SYSTEM_DEFAULT"),
        default=None,
        help="Override ROS_AUTOMATIC_DISCOVERY_RANGE. Omit for same-machine acceptance.",
    )
    parser.add_argument(
        "--skip-topic-probe",
        action="store_true",
        help="Launch RViz2 without first running ros2 topic info.",
    )
    parser.add_argument(
        "--topic-probe-timeout",
        type=float,
        default=8.0,
        help="Seconds to wait for the optional ros2 topic info probe. Default: 8.0.",
    )
    parser.add_argument(
        "--strict-topic-probe",
        action="store_true",
        help="Fail before launching RViz2 if the optional ros2 topic info probe fails.",
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Print resolved paths and generated config path without launching RViz2.",
    )
    parser.add_argument(
        "--rviz-startup-check-seconds",
        type=float,
        default=1.5,
        help="Seconds to wait for an immediate RViz2 process exit after launch.",
    )
    parser.add_argument(
        "--rviz-window-wait-seconds",
        type=float,
        default=45.0,
        help="Seconds to wait for a visible RViz2 window after launch.",
    )
    return parser.parse_args(argv)


def normalize_topic(topic: str) -> str:
    """Normalize a PointCloud2 topic into a leading-slash absolute topic name."""
    value = (topic or "").strip()
    if not value:
        raise ValueError("PointCloud2 topic must not be empty.")
    return value if value.startswith("/") else "/" + value


def normalize_frame(frame: str, label: str) -> str:
    """Normalize and validate TF frame names used by launch arguments."""
    value = (frame or "").strip().strip("/")
    if not value:
        raise ValueError(f"{label} must not be empty.")
    return value


def sanitize_config_suffix(value: str) -> str:
    """Create a filesystem-safe suffix from a ROS topic or frame value."""
    suffix = re.sub(r"[^A-Za-z0-9_.-]+", "_", value.strip("/"))
    return suffix or "pointcloud2"


def write_runtime_rviz_config(
    base_config: pathlib.Path,
    workspace_root: pathlib.Path,
    points_topic: str,
    fixed_frame: str,
) -> pathlib.Path:
    """Write a temporary RViz config containing the selected PointCloud2 topic."""
    text = base_config.read_text(encoding="utf-8")
    text = text.replace("Value: /points", f"Value: {points_topic}")
    text = text.replace("Name: PointCloud2 /points", f"Name: PointCloud2 {points_topic}")
    text = re.sub(r"Fixed Frame: .+", f"Fixed Frame: {fixed_frame}", text)

    output_dir = workspace_root / "build" / "rviz2"
    output_dir.mkdir(parents=True, exist_ok=True)
    output_path = output_dir / f"phase138l_{sanitize_config_suffix(points_topic)}.rviz"
    output_path.write_text(text, encoding="utf-8", newline="\n")
    return output_path


def probe_topic(
    pixi_python: pathlib.Path,
    ros2_script: pathlib.Path,
    env: dict[str, str],
    topic: str,
    timeout_seconds: float,
    strict: bool,
) -> bool:
    """Optionally probe ROS2 topic info and return whether it is currently visible."""
    print(f"[phase138l-rviz] Probing ROS2 topic: {topic}")
    try:
        result = subprocess.run(
            [str(pixi_python), str(ros2_script), "topic", "info", topic, "--verbose", "--no-daemon"],
            env=env,
            text=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            timeout=max(0.1, timeout_seconds),
            check=False,
        )
    except subprocess.TimeoutExpired as exc:
        message = (
            f"Topic probe timed out after {timeout_seconds:.1f}s. "
            "RViz2 can still launch; confirm Unity is in Play Mode and the Publisher topic matches --points-topic."
        )
        print(f"[phase138l-rviz] {message}")
        if strict:
            raise RuntimeError(message) from exc

        return False

    print(result.stdout.strip() or "<no topic info output>")
    if result.returncode != 0:
        message = (
            "[phase138l-rviz] Topic probe did not pass. RViz2 can still launch, "
            "but confirm Unity is in Play Mode and the Publisher topic matches --points-topic."
        )
        print(message)
        if strict:
            raise RuntimeError(message)

        return False

    return True


def launch_static_tf(
    pixi_python: pathlib.Path,
    ros2_script: pathlib.Path,
    env: dict[str, str],
    parent_frame: str,
    child_frame: str,
) -> subprocess.Popen[str] | None:
    """Launch a one-shot helper static-transform publisher when requested."""
    if parent_frame == child_frame:
        print(
            "[phase138l-rviz] Static TF skipped because parent and child frame are both "
            f"{parent_frame}."
        )
        return None

    command = [
        str(pixi_python),
        str(ros2_script),
        "run",
        "tf2_ros",
        "static_transform_publisher",
        "--frame-id",
        parent_frame,
        "--child-frame-id",
        child_frame,
    ]
    print(f"[phase138l-rviz] Launching static TF: {parent_frame} -> {child_frame}")
    process = subprocess.Popen(
        command,
        env=env,
        text=True,
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
    )
    time.sleep(0.5)
    exit_code = process.poll()
    if exit_code is not None:
        print(
            f"[phase138l-rviz] Static TF helper exited immediately with code {exit_code}; "
            "RViz2 can still launch but may keep a Fixed Frame warning."
        )
        return None

    print(f"[phase138l-rviz] Static TF helper running pid={process.pid}")
    return process


def main(argv: list[str]) -> int:
    """Entry point for this smoke script. Returns process exit status."""
    args = parse_args(argv)
    workspace_root = ros2env.find_workspace_root()
    ros2_root = ros2env.resolve_existing_path(args.ros2_root, "ROS2 root", workspace_root)
    base_config = ros2env.resolve_existing_path(args.rviz_config, "RViz2 config", workspace_root)
    points_topic = normalize_topic(args.points_topic)
    fixed_frame = normalize_frame(args.fixed_frame or DEFAULT_FIXED_FRAME, "fixed frame")
    tf_parent_frame = normalize_frame(args.tf_parent_frame, "TF parent frame")
    tf_child_frame = normalize_frame(args.tf_child_frame, "TF child frame")
    pixi_python, ros2_script = ros2env.validate_ros2_root(ros2_root)
    env = ros2env.build_ros_env(ros2_root, args.rmw, args.discovery_range, args.domain_id)
    runtime_config = write_runtime_rviz_config(base_config, workspace_root, points_topic, fixed_frame)

    print(f"[phase138l-rviz] ROS2 root: {ros2_root}")
    print(f"[phase138l-rviz] pixi Python: {pixi_python}")
    print(f"[phase138l-rviz] ros2-script.py: {ros2_script}")
    print(f"[phase138l-rviz] RMW_IMPLEMENTATION: {env.get('RMW_IMPLEMENTATION')}")
    print(f"[phase138l-rviz] ROS_DOMAIN_ID: {env.get('ROS_DOMAIN_ID')}")
    print(f"[phase138l-rviz] ROS_AUTOMATIC_DISCOVERY_RANGE: {env.get('ROS_AUTOMATIC_DISCOVERY_RANGE', '<unset>')}")
    print(f"[phase138l-rviz] PointCloud2 topic: {points_topic}")
    print(f"[phase138l-rviz] Fixed frame: {fixed_frame}")
    launch_tf_fallback = args.static_tf and not args.no_static_tf
    print(
        "[phase138l-rviz] Static TF fallback: "
        + (tf_parent_frame + " -> " + tf_child_frame if launch_tf_fallback else "disabled")
    )
    print(f"[phase138l-rviz] Runtime RViz2 config: {runtime_config}")

    if args.dry_run:
        print("[phase138l-rviz] Dry run only; RViz2 was not launched.")
        return 0

    if not args.skip_topic_probe:
        probe_topic(
            pixi_python,
            ros2_script,
            env,
            points_topic,
            args.topic_probe_timeout,
            args.strict_topic_probe,
        )

    if launch_tf_fallback:
        launch_static_tf(pixi_python, ros2_script, env, tf_parent_frame, tf_child_frame)

    ros2env.launch_rviz(
        ros2_root,
        runtime_config,
        env,
        "phase138l-rviz",
        startup_check_seconds=args.rviz_startup_check_seconds,
        window_wait_seconds=args.rviz_window_wait_seconds,
    )
    print("[phase138l-rviz] RViz2 launched. Use MoveCamera/Interact to inspect the live PointCloud2 stream.")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main(sys.argv[1:]))
    except KeyboardInterrupt:
        raise SystemExit(130)
    except Exception as exc:
        print(f"[phase138l-rviz] FAIL: {exc}", file=sys.stderr)
        raise SystemExit(1)
