#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Launch RViz2 to view Unity camera image and PointCloud2 together.

Start Unity Play Mode first with:

- FoxgloveManager: ROS2 Native (R2FU)
- Camera image topic: /unity/sensor/camera/image/compressed
- PointCloud2 topic: /unity/point_cloud2
- TF anchors enabled

Then run:

    python Scripts/smoke/ros2/launch_phase138m_rviz2.py

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

from pathlib import Path as _Phase192Path
_PHASE192_FACADE_FILE = _Phase192Path(__file__).resolve().parents[1] / "launch_phase138m_rviz2.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _Phase192Path, _PHASE192_FACADE_FILE

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
        help="Windows ROS2 Jazzy root. Default: ros2-windows\\ros2_jazzy",
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

__all__ = [name for name in globals() if not name.startswith("__")]
