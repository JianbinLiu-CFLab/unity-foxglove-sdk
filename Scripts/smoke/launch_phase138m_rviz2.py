#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Module: Scripts/smoke
# Purpose: Launch RViz2 for Phase138M camera + PointCloud2 product interaction.

"""Launch RViz2 to view Unity camera image and PointCloud2 together.

Start Unity Play Mode first with:

- FoxgloveManager: ROS2 Native (R2FU)
- Camera image topic: /unity/sensor/camera/image/compressed
- PointCloud2 topic: /unity/point_cloud2
- TF anchors enabled

Then run:

    python Scripts/smoke/launch_phase138m_rviz2.py

The helper opens RViz2 with Image, PointCloud2, TF, and Grid displays. By
default the Image display uses RViz2's `compressed` transport against the
image_transport base topic. Install compressed_image_transport for RViz2 image
decoding. The Python raw-image republisher is retained only as an explicit
diagnostic fallback.
"""

from __future__ import annotations

import argparse
import os
import pathlib
import subprocess
import sys
import textwrap
import time

import _ros2_windows_env as ros2env


DEFAULT_COMPRESSED_IMAGE_TOPIC = "/unity/sensor/camera/image/compressed"
DEFAULT_RAW_IMAGE_TOPIC = "/unity/sensor/camera/image_raw_view"
DEFAULT_POINTS_TOPIC = "/unity/point_cloud2"
DEFAULT_FIXED_FRAME = "map"
DEFAULT_CAMERA_PARENT_FRAME = "os_sensor"
DEFAULT_COMPRESSED_TRANSPORT_OVERLAY = "build/ros2_compressed_transport_overlay/install"


def parse_args(argv: list[str]) -> argparse.Namespace:
    """Parse command line arguments."""

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--ros2-root",
        default=str(ros2env.DEFAULT_ROS2_ROOT),
        help="Windows ROS2 Jazzy root. Default: C:\\ros2_jazzy\\ros2-windows",
    )
    parser.add_argument("--image-topic", default=DEFAULT_COMPRESSED_IMAGE_TOPIC, help="CompressedImage source topic.")
    parser.add_argument("--raw-image-topic", default=DEFAULT_RAW_IMAGE_TOPIC, help="Temporary raw Image topic for RViz2.")
    parser.add_argument("--points-topic", default=DEFAULT_POINTS_TOPIC, help="PointCloud2 topic for RViz2.")
    parser.add_argument("--fixed-frame", default=DEFAULT_FIXED_FRAME, help="RViz2 fixed frame.")
    parser.add_argument(
        "--camera-parent-frame",
        default=DEFAULT_CAMERA_PARENT_FRAME,
        help="Camera TF parent frame connected to the RViz fixed frame for visual inspection.",
    )
    parser.add_argument("--rmw", default=None, help="RMW implementation override.")
    parser.add_argument("--domain-id", default=None, help="ROS_DOMAIN_ID override.")
    parser.add_argument(
        "--discovery-range",
        choices=("LOCALHOST", "SUBNET", "OFF", "SYSTEM_DEFAULT"),
        default=None,
        help="Override ROS_AUTOMATIC_DISCOVERY_RANGE. Leave unset to match the current Unity/R2FU runtime.",
    )
    parser.add_argument(
        "--ros2-overlay",
        default=None,
        help=(
            "Optional ROS2 overlay install prefix to prepend to PATH/AMENT_PREFIX_PATH/CMAKE_PREFIX_PATH/"
            "COLCON_PREFIX_PATH. Defaults to the local compressed_image_transport overlay when present."
        ),
    )
    parser.add_argument(
        "--no-auto-compressed-transport-overlay",
        action="store_true",
        help="Do not auto-detect build/ros2_compressed_transport_overlay/install.",
    )
    parser.add_argument(
        "--image-republisher",
        dest="no_image_republisher",
        action="store_false",
        help="Start the experimental Python CompressedImage -> raw Image republisher.",
    )
    parser.add_argument(
        "--no-image-republisher",
        dest="no_image_republisher",
        action="store_true",
        help="Do not start the experimental raw image republisher. This is the default.",
    )
    parser.add_argument(
        "--no-camera-static-tf",
        action="store_true",
        help="Do not start the visual static TF fallback from fixed frame to camera parent frame.",
    )
    parser.add_argument(
        "--no-cleanup-stale",
        action="store_true",
        help="Do not stop stale Phase138M RViz/helper processes from previous script runs.",
    )
    parser.add_argument("--skip-topic-probe", action="store_true", help="Skip pre-launch topic info probes.")
    parser.add_argument("--topic-probe-timeout", type=float, default=8.0, help="Topic probe timeout.")
    parser.add_argument(
        "--raw-image-wait-seconds",
        type=float,
        default=10.0,
        help="Wait this long for the republisher to emit one raw Image before launching RViz2.",
    )
    parser.add_argument("--dry-run", action="store_true", help="Write config and print commands without launching RViz2.")
    parser.add_argument("--image-republisher-child", action="store_true", help=argparse.SUPPRESS)
    parser.set_defaults(no_image_republisher=True)
    return parser.parse_args(argv)


def normalize_topic(value: str, label: str) -> str:
    """Normalize a ROS topic into a leading-slash absolute name."""

    topic = (value or "").strip()
    if not topic:
        raise ValueError(f"{label} must not be empty.")
    return topic if topic.startswith("/") else "/" + topic


def normalize_frame(value: str) -> str:
    """Normalize an RViz fixed frame."""

    frame = (value or "").strip().strip("/")
    if not frame:
        raise ValueError("fixed frame must not be empty.")
    return frame


def sanitize_name(value: str) -> str:
    """Return a compact filesystem-safe label."""

    return "".join(ch if ch.isalnum() or ch in ("_", "-") else "_" for ch in value.strip("/")) or "topic"


def write_rviz_config(
    workspace_root: pathlib.Path,
    image_topic: str,
    image_transport: str,
    points_topic: str,
    fixed_frame: str,
) -> pathlib.Path:
    """Write a runtime RViz2 config for image + point cloud viewing."""

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
    - Class: rviz_default_plugins/TF
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
    - Class: rviz_default_plugins/PointCloud2
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
    - Class: rviz_default_plugins/Image
      Enabled: true
      Max Value: 1
      Median window: 5
      Min Value: 0
      Name: Image {image_topic}
      Normalize Range: true
      Queue Size: 2
      Topic:
        Depth: 5
        Durability Policy: Volatile
        History Policy: Keep Last
        Reliability Policy: Reliable
        Value: {image_topic}
      Transport Hint: {image_transport}
  Enabled: true
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
    output_path = output_dir / "phase138m_camera_pointcloud.rviz"
    output_path.write_text(config, encoding="utf-8", newline="\n")
    return output_path


def cleanup_stale_processes(script_path: pathlib.Path, rviz_config: pathlib.Path, fixed_frame: str, camera_frame: str) -> None:
    """Stop stale Phase138M helper processes from previous runs."""

    if os.name != "nt":
        return

    escaped_script = powershell_single_quote(str(script_path))
    escaped_config = powershell_single_quote(str(rviz_config))
    escaped_fixed = powershell_single_quote(fixed_frame)
    escaped_camera = powershell_single_quote(camera_frame)
    command = f"""
$script = [System.Management.Automation.WildcardPattern]::Escape('{escaped_script}')
$config = [System.Management.Automation.WildcardPattern]::Escape('{escaped_config}')
$fixed = [System.Management.Automation.WildcardPattern]::Escape('{escaped_fixed}')
$camera = [System.Management.Automation.WildcardPattern]::Escape('{escaped_camera}')
$matches = Get-CimInstance Win32_Process | Where-Object {{
    $cmd = $_.CommandLine
    if ([string]::IsNullOrWhiteSpace($cmd)) {{ return $false }}
    if ($cmd -like "*$script*" -and $cmd -like "*--image-republisher-child*") {{ return $true }}
    if ($_.Name -eq "rviz2.exe" -and $cmd -like "*$config*") {{ return $true }}
    if ($cmd -like "*static_transform_publisher*" -and $cmd -like "*--frame-id $fixed*" -and $cmd -like "*--child-frame-id $camera*") {{ return $true }}
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
        ["powershell.exe", "-NoProfile", "-Command", command],
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        check=False,
        timeout=10.0,
    )
    cleaned = [line.strip() for line in result.stdout.splitlines() if line.strip()]
    if cleaned:
        print("[phase138m-rviz] Cleaned stale helper process(es): " + ", ".join(cleaned))


def powershell_single_quote(value: str) -> str:
    """Escape a string for a PowerShell single-quoted literal."""
    return value.replace("'", "''")


def compressed_transport_base_topic(compressed_topic: str) -> str:
    """Return the image_transport base topic for a sensor_msgs/CompressedImage topic."""

    suffix = "/compressed"
    return compressed_topic[: -len(suffix)] if compressed_topic.endswith(suffix) else compressed_topic


def prepend_ros2_overlay(env: dict[str, str], overlay: pathlib.Path) -> None:
    """Prepend a ROS2 overlay install prefix to runtime discovery paths."""

    env["PATH"] = os.pathsep.join([str(overlay / "bin"), env["PATH"]])
    for key in ("AMENT_PREFIX_PATH", "CMAKE_PREFIX_PATH", "COLCON_PREFIX_PATH"):
        current = env.get(key, "")
        env[key] = os.pathsep.join([str(overlay), current]) if current else str(overlay)


def resolve_ros2_overlay(args: argparse.Namespace, workspace_root: pathlib.Path) -> pathlib.Path | None:
    """Resolve an explicit or auto-detected ROS2 overlay install prefix."""

    if args.ros2_overlay:
        return ros2env.resolve_existing_path(args.ros2_overlay, "ROS2 overlay", workspace_root)
    if args.no_auto_compressed_transport_overlay:
        return None

    candidate = workspace_root / DEFAULT_COMPRESSED_TRANSPORT_OVERLAY
    return candidate.resolve() if candidate.is_dir() else None


def probe_topic(
    pixi_python: pathlib.Path,
    ros2_script: pathlib.Path,
    env: dict[str, str],
    topic: str,
    timeout_seconds: float,
) -> None:
    """Print a non-fatal topic-info probe."""

    print(f"[phase138m-rviz] Probing {topic}")
    try:
        result = ros2env.run_ros2(
            pixi_python,
            ros2_script,
            env,
            ["topic", "info", topic, "--verbose", "--no-daemon"],
            check=False,
            timeout_seconds=timeout_seconds,
        )
        print(result.stdout.strip() or "<no topic info output>")
    except subprocess.TimeoutExpired:
        print(f"[phase138m-rviz] Topic probe timed out for {topic}; RViz2 will still launch.")


def launch_image_republisher(
    pixi_python: pathlib.Path,
    env: dict[str, str],
    source_topic: str,
    raw_topic: str,
    log_path: pathlib.Path,
) -> subprocess.Popen[str]:
    """Start the child raw-image republisher."""

    command = [
        str(pixi_python),
        str(pathlib.Path(__file__).resolve()),
        "--image-republisher-child",
        "--image-topic",
        source_topic,
        "--raw-image-topic",
        raw_topic,
    ]
    log_path.parent.mkdir(parents=True, exist_ok=True)
    log_file = log_path.open("w", encoding="utf-8")
    try:
        process = subprocess.Popen(
            command,
            env=env,
            text=True,
            stdout=log_file,
            stderr=subprocess.STDOUT,
            cwd=str(pathlib.Path.cwd()),
        )
    finally:
        log_file.close()
    time.sleep(0.5)
    exit_code = process.poll()
    if exit_code is not None:
        log_file.close()
        details = log_path.read_text(encoding="utf-8", errors="replace") if log_path.exists() else ""
        raise RuntimeError(f"Image republisher exited immediately with code {exit_code}.\n{details}")
    print(f"[phase138m-rviz] Image republisher running pid={process.pid}: {source_topic} -> {raw_topic}")
    print(f"[phase138m-rviz] Image republisher log: {log_path}")
    return process


def launch_static_camera_tf(
    pixi_python: pathlib.Path,
    ros2_script: pathlib.Path,
    env: dict[str, str],
    fixed_frame: str,
    camera_parent_frame: str,
) -> subprocess.Popen[str] | None:
    """Launch a visual-only static TF fallback from fixed frame to camera parent."""

    if fixed_frame == camera_parent_frame:
        return None

    command = [
        str(pixi_python),
        str(ros2_script),
        "run",
        "tf2_ros",
        "static_transform_publisher",
        "--frame-id",
        fixed_frame,
        "--child-frame-id",
        camera_parent_frame,
    ]
    process = subprocess.Popen(command, env=env, text=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    time.sleep(0.5)
    exit_code = process.poll()
    if exit_code is not None:
        print(f"[phase138m-rviz] Static camera TF helper exited immediately with code {exit_code}.")
        return None

    print(f"[phase138m-rviz] Static camera TF running pid={process.pid}: {fixed_frame} -> {camera_parent_frame}")
    return process


def wait_for_raw_image(
    pixi_python: pathlib.Path,
    env: dict[str, str],
    raw_topic: str,
    timeout_seconds: float,
) -> None:
    """Wait for one raw Image frame from the republisher as a non-fatal diagnostic."""

    if timeout_seconds <= 0.0:
        return

    code = f"""
import json
import rclpy
from sensor_msgs.msg import Image

topic = {raw_topic!r}
result = {{"received": False}}

rclpy.init()
node = rclpy.create_node("phase138m_raw_image_wait_probe")
sub = node.create_subscription(
    Image,
    topic,
    lambda msg: (
        result.__setitem__("received", True),
        result.__setitem__("width", int(msg.width)),
        result.__setitem__("height", int(msg.height)),
        result.__setitem__("encoding", str(msg.encoding)),
        result.__setitem__("stamp", int(msg.header.stamp.sec) * 1000000000 + int(msg.header.stamp.nanosec)),
    ),
    10,
)
deadline = node.get_clock().now().nanoseconds + int({timeout_seconds!r} * 1000000000)
while rclpy.ok() and not result["received"] and node.get_clock().now().nanoseconds < deadline:
    rclpy.spin_once(node, timeout_sec=0.1)
node.destroy_node()
rclpy.shutdown()
print(json.dumps(result, separators=(",", ":")))
"""
    try:
        result = subprocess.run(
            [str(pixi_python), "-c", code],
            env=env,
            text=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            check=False,
            timeout=timeout_seconds + 3.0,
        )
    except subprocess.TimeoutExpired:
        print(f"[phase138m-rviz] Raw image wait timed out after {timeout_seconds:.1f}s: {raw_topic}")
        return

    output = result.stdout.strip()
    if result.returncode == 0 and '"received":true' in output.lower():
        print(f"[phase138m-rviz] Raw image ready: {output}")
        return

    print(f"[phase138m-rviz] Raw image not received within {timeout_seconds:.1f}s: {raw_topic}")
    if output:
        print(f"[phase138m-rviz] Raw image wait output: {output}")


def image_republisher_main(args: argparse.Namespace) -> int:
    """Decode CompressedImage and publish raw Image for RViz2."""

    print(f"[phase138m-rviz-image] starting source={args.image_topic} raw={args.raw_image_topic}", flush=True)
    import cv2
    print("[phase138m-rviz-image] imported cv2", flush=True)
    import numpy as np
    print("[phase138m-rviz-image] imported numpy", flush=True)
    import rclpy
    print("[phase138m-rviz-image] imported rclpy", flush=True)
    from rclpy.node import Node
    from sensor_msgs.msg import CompressedImage, Image
    print("[phase138m-rviz-image] imported sensor_msgs", flush=True)

    class ImageRepublisher(Node):
        """CompressedImage to raw Image bridge for RViz2 display."""

        def __init__(self) -> None:
            """Create publisher/subscriber state for one RViz helper node."""
            super().__init__("phase138m_rviz_image_republisher")
            self.publisher = self.create_publisher(Image, args.raw_image_topic, 10)
            self.subscription = self.create_subscription(CompressedImage, args.image_topic, self.on_image, 10)
            self.count = 0

        def on_image(self, msg: CompressedImage) -> None:
            """Decode one CompressedImage and republish it as raw Image."""
            encoded = np.frombuffer(bytes(msg.data), dtype=np.uint8)
            bgr = cv2.imdecode(encoded, cv2.IMREAD_COLOR)
            if bgr is None:
                return
            rgb = cv2.cvtColor(bgr, cv2.COLOR_BGR2RGB)

            out = Image()
            out.header = msg.header
            out.height = int(rgb.shape[0])
            out.width = int(rgb.shape[1])
            out.encoding = "rgb8"
            out.is_bigendian = False
            out.step = int(rgb.shape[1] * 3)
            out.data = rgb.tobytes()
            self.publisher.publish(out)
            self.count += 1
            if self.count == 1 or self.count % 60 == 0:
                self.get_logger().info(
                    f"republished {self.count} frame(s) {args.image_topic} -> {args.raw_image_topic} "
                    f"({out.width}x{out.height})"
                )

    rclpy.init()
    print("[phase138m-rviz-image] rclpy initialized", flush=True)
    node = ImageRepublisher()
    print(f"[phase138m-rviz-image] ready source={args.image_topic} raw={args.raw_image_topic}", flush=True)
    try:
        rclpy.spin(node)
    finally:
        node.destroy_node()
        rclpy.shutdown()
    return 0


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

    if not args.no_image_republisher:
        launch_image_republisher(pixi_python, env, args.image_topic, args.raw_image_topic, log_path)
        wait_for_raw_image(pixi_python, env, args.raw_image_topic, args.raw_image_wait_seconds)

    if not args.no_camera_static_tf:
        launch_static_camera_tf(pixi_python, ros2_script, env, args.fixed_frame, args.camera_parent_frame)

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


if __name__ == "__main__":
    try:
        raise SystemExit(main(sys.argv[1:]))
    except KeyboardInterrupt:
        raise SystemExit(130)
    except Exception as exc:
        print(f"[phase138m-rviz] FAIL: {exc}", file=sys.stderr)
        raise SystemExit(1)
