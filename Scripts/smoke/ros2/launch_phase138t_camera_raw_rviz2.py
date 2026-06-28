#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Module: Scripts/smoke
# Purpose: Launch RViz2 for Phase138T camera raw Image DDS visual inspection.

"""Launch RViz2 to view the Phase138T raw camera Image DDS stream.

Start Unity Play Mode first with:

- FoxgloveManager: ROS2 Native (R2FU)
- FoxgloveCameraPublisher: Publish Raw Image DDS enabled
- Raw image topic: /unity/sensor/camera/image
- Optional PointCloud2 topic: /unity/point_cloud2
- TF anchors enabled, or use this helper's visual /tf map -> os_sensor fallback

Then run:

    python Scripts/smoke/ros2/launch_phase138t_camera_raw_rviz2.py

The helper writes a runtime RViz2 config under build/rviz2 and launches RViz2
using the pinned Windows ROS2 Jazzy environment. It shows raw Image, PointCloud2,
and TF together; no image republisher is needed.
"""

from __future__ import annotations

import argparse
import pathlib
import re
import subprocess
import sys
import time

import _ros2_windows_env as ros2env


DEFAULT_TOPIC = "/unity/sensor/camera/image"
DEFAULT_POINTS_TOPIC = "/unity/point_cloud2"
DEFAULT_FIXED_FRAME = "map"
DEFAULT_CAMERA_PARENT_FRAME = "os_sensor"


def parse_args(argv: list[str]) -> argparse.Namespace:
    """Parse launch arguments for the raw camera Image RViz2 helper."""

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--ros2-root",
        default=str(ros2env.DEFAULT_ROS2_ROOT),
        help="Windows ROS2 Jazzy root. Default: ros2-windows\\ros2_jazzy",
    )
    parser.add_argument(
        "--topic",
        default=DEFAULT_TOPIC,
        help=f"Raw sensor_msgs/msg/Image topic to display. Default: {DEFAULT_TOPIC}",
    )
    parser.add_argument(
        "--fixed-frame",
        default=DEFAULT_FIXED_FRAME,
        help=f"RViz2 fixed frame. Default: {DEFAULT_FIXED_FRAME}",
    )
    parser.add_argument(
        "--points-topic",
        default=DEFAULT_POINTS_TOPIC,
        help=f"PointCloud2 topic to display. Default: {DEFAULT_POINTS_TOPIC}",
    )
    parser.add_argument(
        "--camera-parent-frame",
        default=DEFAULT_CAMERA_PARENT_FRAME,
        help=(
            "Camera TF parent frame used by the visual static TF fallback. "
            f"Default: {DEFAULT_CAMERA_PARENT_FRAME}"
        ),
    )
    parser.add_argument(
        "--no-pointcloud",
        action="store_true",
        help="Do not add the PointCloud2 display.",
    )
    parser.add_argument(
        "--no-tf",
        action="store_true",
        help="Do not add the TF display.",
    )
    parser.add_argument(
        "--no-camera-static-tf",
        "--no-camera-tf-fallback",
        dest="no_camera_static_tf",
        action="store_true",
        help="Do not launch the visual /tf fallback from fixed frame to camera parent frame.",
    )
    parser.add_argument(
        "--rmw",
        default=None,
        help="RMW implementation override. Omit to preserve RMW_IMPLEMENTATION or default to rmw_fastrtps_cpp.",
    )
    parser.add_argument(
        "--domain-id",
        default=None,
        help="ROS_DOMAIN_ID override. Omit for the helper default.",
    )
    parser.add_argument(
        "--discovery-range",
        choices=("LOCALHOST", "SUBNET", "OFF", "SYSTEM_DEFAULT"),
        default="SUBNET",
        help="Override ROS_AUTOMATIC_DISCOVERY_RANGE. Default: SUBNET.",
    )
    parser.add_argument(
        "--skip-topic-probe",
        action="store_true",
        help="Launch RViz2 without first running diagnostic ros2 topic probes.",
    )
    parser.add_argument(
        "--strict-topic-probe",
        action="store_true",
        help="Fail before launching RViz2 if the optional topic probe does not see a publisher.",
    )
    parser.add_argument(
        "--topic-probe-timeout",
        type=float,
        default=5.0,
        help="Seconds to wait for each optional topic probe. Default: 5.0.",
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Write the RViz2 config and print diagnostics without launching RViz2.",
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
        default=20.0,
        help="Seconds to wait for a visible RViz2 window after launch.",
    )
    parser.add_argument(
        "--no-cleanup-stale",
        action="store_true",
        help="Do not stop stale Phase138T RViz/helper processes from previous script runs.",
    )
    parser.add_argument("--camera-tf-fallback-child", action="store_true", help=argparse.SUPPRESS)
    return parser.parse_args(argv)


def normalize_topic(topic: str) -> str:
    """Normalize a ROS topic into a leading-slash absolute topic name."""

    value = (topic or "").strip()
    if not value:
        raise ValueError("raw image topic must not be empty.")
    return value if value.startswith("/") else "/" + value


def normalize_frame(frame: str) -> str:
    """Normalize and validate the RViz2 fixed frame."""

    value = (frame or "").strip().strip("/")
    if not value:
        raise ValueError("fixed frame must not be empty.")
    return value


def sanitize_config_suffix(value: str) -> str:
    """Create a filesystem-safe suffix from a ROS topic or frame value."""

    suffix = re.sub(r"[^A-Za-z0-9_.-]+", "_", value.strip("/"))
    return suffix or "camera_raw_image"


def write_runtime_rviz_config(
    workspace_root: pathlib.Path,
    topic: str,
    points_topic: str,
    fixed_frame: str,
    show_tf: bool,
    show_pointcloud: bool,
) -> pathlib.Path:
    """Write a runtime RViz2 config for raw camera image viewing."""

    tf_display = ""
    if show_tf:
        tf_display = """    - Class: rviz_default_plugins/TF
      Enabled: true
      Frame Timeout: 15
      Name: TF
      Show Axes: true
      Show Names: true
      Topic:
        Depth: 5
        Durability Policy: Volatile
        History Policy: Keep Last
        Reliability Policy: Reliable
        Value: /tf
"""
    pointcloud_display = ""
    if show_pointcloud:
        pointcloud_display = f"""    - Class: rviz_default_plugins/PointCloud2
      Enabled: true
      Name: PointCloud2 {points_topic}
      Topic:
        Depth: 10
        Durability Policy: Volatile
        History Policy: Keep Last
        Reliability Policy: Reliable
        Value: {points_topic}
      Style: Points
      Size (Pixels): 3
      Color Transformer: Intensity
      Queue Size: 10
"""

    config = f"""Panels:
  - Class: rviz_common/Displays
    Name: Displays
Visualization Manager:
  Class: ""
  Displays:
    - Alpha: 0.5
      Cell Size: 1
      Class: rviz_default_plugins/Grid
      Color: 160; 160; 164
      Enabled: true
      Name: Grid
      Plane: XY
      Reference Frame: <Fixed Frame>
{tf_display}    - Class: rviz_default_plugins/Image
      Enabled: true
      Max Value: 1
      Median window: 5
      Min Value: 0
      Name: Raw Image {topic}
      Normalize Range: true
      Queue Size: 2
      Topic:
        Depth: 5
        Durability Policy: Volatile
        History Policy: Keep Last
        Reliability Policy: Reliable
        Value: {topic}
      Transport Hint: raw
{pointcloud_display}  Enabled: true
  Global Options:
    Background Color: 48; 48; 48
    Fixed Frame: {fixed_frame}
    Frame Rate: 30
  Name: root
  Tools:
    - Class: rviz_default_plugins/Interact
    - Class: rviz_default_plugins/MoveCamera
    - Class: rviz_default_plugins/Select
Window Geometry:
  Height: 960
  Width: 1600
"""
    output_dir = workspace_root / "build" / "rviz2"
    output_dir.mkdir(parents=True, exist_ok=True)
    output_path = output_dir / f"phase138t_{sanitize_config_suffix(topic)}.rviz"
    output_path.write_text(config, encoding="utf-8", newline="\n")
    return output_path


def cleanup_stale_processes(script_path: pathlib.Path, rviz_config: pathlib.Path, camera_parent_frame: str) -> None:
    """Stop stale Phase138T RViz/helper processes from previous script runs."""

    if sys.platform != "win32":
        return

    escaped_script = powershell_single_quote(str(script_path))
    escaped_config = powershell_single_quote(str(rviz_config))
    escaped_camera = powershell_single_quote(camera_parent_frame)
    command = f"""
$script = [System.Management.Automation.WildcardPattern]::Escape('{escaped_script}')
$config = [System.Management.Automation.WildcardPattern]::Escape('{escaped_config}')
$camera = [System.Management.Automation.WildcardPattern]::Escape('{escaped_camera}')
$matches = Get-CimInstance Win32_Process | Where-Object {{
    $cmd = $_.CommandLine
    if ([string]::IsNullOrWhiteSpace($cmd)) {{ return $false }}
    if ($cmd -like "*$script*" -and $cmd -like "*--camera-tf-fallback-child*") {{ return $true }}
    if ($_.Name -eq "rviz2.exe" -and $cmd -like "*$config*") {{ return $true }}
    if ($cmd -like "*static_transform_publisher*" -and $cmd -like "*--child-frame-id $camera*") {{ return $true }}
    if ($cmd -like "*topic pub /tf*" -and $cmd -like "*child_frame_id: '$camera'*") {{ return $true }}
    return $false
}}
foreach ($p in $matches) {{
    try {{
        Stop-Process -Id $p.ProcessId -Force -ErrorAction Stop
        Write-Output "$($p.ProcessId) $($p.Name)"
    }} catch {{}}
}}
"""
    result = subprocess.run(
        ["powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", command],
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        check=False,
        timeout=10.0,
    )
    output = result.stdout.strip()
    if output:
        print("[phase138t-rviz] Stopped stale helper processes:")
        print(output)


def powershell_single_quote(value: str) -> str:
    """Escape a string for a PowerShell single-quoted literal."""

    return value.replace("'", "''")


def probe_topic(
    pixi_python: pathlib.Path,
    ros2_script: pathlib.Path,
    env: dict[str, str],
    topic: str,
    timeout_seconds: float,
    strict: bool,
) -> bool:
    """Run diagnostic topic probes without making Windows discovery quirks a default hard gate."""

    print(f"[phase138t-rviz] Probing ROS2 topic: {topic}")
    try:
        info = ros2env.run_ros2(
            pixi_python,
            ros2_script,
            env,
            ["topic", "info", topic, "--verbose", "--no-daemon"],
            check=False,
            timeout_seconds=max(0.1, timeout_seconds),
        )
    except subprocess.TimeoutExpired as exc:
        message = (
            f"Topic info timed out after {timeout_seconds:.1f}s. "
            "RViz2 can still launch; direct subscribers may succeed on this Windows/FastDDS setup."
        )
        print(f"[phase138t-rviz] {message}")
        if strict:
            raise RuntimeError(message) from exc
        return False

    print(info.stdout.strip() or "<no topic info output>")
    if info.returncode == 0 and "Publisher count:" in info.stdout:
        return True

    message = (
        "[phase138t-rviz] Topic probe did not confirm a publisher. "
        "RViz2 will still launch unless --strict-topic-probe was used."
    )
    print(message)
    if strict:
        raise RuntimeError(message)
    return False


def camera_tf_fallback_child(args: argparse.Namespace) -> int:
    """Publish a visual map -> camera-parent TF fallback on /tf."""

    import rclpy
    from geometry_msgs.msg import TransformStamped
    from tf2_msgs.msg import TFMessage

    fixed_frame = normalize_frame(args.fixed_frame)
    camera_parent_frame = normalize_frame(args.camera_parent_frame)
    rclpy.init()
    node = rclpy.create_node("phase138t_camera_tf_fallback")
    publisher = node.create_publisher(TFMessage, "/tf", 10)
    message = TFMessage()
    transform = TransformStamped()
    transform.header.frame_id = fixed_frame
    transform.child_frame_id = camera_parent_frame
    transform.transform.rotation.w = 1.0
    message.transforms.append(transform)
    print(f"[phase138t-rviz-tf] publishing /tf {fixed_frame}->{camera_parent_frame}", flush=True)
    try:
        while rclpy.ok():
            stamp = node.get_clock().now().to_msg()
            message.transforms[0].header.stamp = stamp
            publisher.publish(message)
            rclpy.spin_once(node, timeout_sec=0.1)
            time.sleep(0.1)
    finally:
        node.destroy_node()
        rclpy.shutdown()
    return 0


def launch_camera_tf_fallback(
    pixi_python: pathlib.Path,
    env: dict[str, str],
    script_path: pathlib.Path,
    fixed_frame: str,
    camera_parent_frame: str,
    workspace_root: pathlib.Path,
) -> subprocess.Popen[str] | None:
    """Launch a visual TF fallback from fixed frame to camera parent frame."""

    if fixed_frame == camera_parent_frame:
        return None

    log_path = workspace_root / "build" / "rviz2" / "phase138t_camera_tf_fallback.log"
    log_path.parent.mkdir(parents=True, exist_ok=True)
    command = [
        str(pixi_python),
        str(script_path),
        "--camera-tf-fallback-child",
        "--fixed-frame",
        fixed_frame,
        "--camera-parent-frame",
        camera_parent_frame,
    ]
    log_file = log_path.open("w", encoding="utf-8")
    try:
        process = subprocess.Popen(command, env=env, text=True, stdout=log_file, stderr=subprocess.STDOUT)
    finally:
        log_file.close()
    time.sleep(0.5)
    exit_code = process.poll()
    if exit_code is not None:
        print(f"[phase138t-rviz] Camera TF fallback helper exited immediately with code {exit_code}. log={log_path}")
        return None

    print(f"[phase138t-rviz] Camera TF fallback running pid={process.pid}: {fixed_frame} -> {camera_parent_frame}")
    print(f"[phase138t-rviz] Camera TF fallback log: {log_path}")
    return process


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


if __name__ == "__main__":
    try:
        raise SystemExit(main(sys.argv[1:]))
    except KeyboardInterrupt:
        raise SystemExit(130)
    except Exception as exc:
        print(f"[phase138t-rviz] FAIL: {exc}", file=sys.stderr)
        raise SystemExit(1)
