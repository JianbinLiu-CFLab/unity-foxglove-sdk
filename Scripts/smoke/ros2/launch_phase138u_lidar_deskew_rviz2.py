#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Module: Scripts/smoke
# Purpose: Launch RViz2 for Phase138U raw + deskewed PointCloud2 visual inspection.

"""Launch RViz2 with raw and deskewed Phase138U PointCloud2 displays.

Start Unity Play Mode first with ROS2 Native (R2FU), PointCloud2 Native mode,
and PointCloud Motion Compensation enabled in RawAndDeskewedTopic mode. Then run:

    python Scripts/smoke/ros2/launch_phase138u_lidar_deskew_rviz2.py

The helper writes a runtime RViz2 config under build/rviz2 and uses the pinned
Windows ROS2 Jazzy environment. It keeps TF visible because TF correctness is
part of the acceptance signal.
"""

from __future__ import annotations

import argparse
import pathlib
import re
import subprocess
import sys

import _ros2_windows_env as ros2env


DEFAULT_RAW_TOPIC = "/unity/point_cloud2"
DEFAULT_DESKEWED_TOPIC = "/unity/point_cloud2_deskewed"
DEFAULT_FIXED_FRAME = "map"


def parse_args(argv: list[str]) -> argparse.Namespace:
    """Parse launch arguments."""

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--ros2-root", default=str(ros2env.DEFAULT_ROS2_ROOT))
    parser.add_argument("--raw-topic", default=DEFAULT_RAW_TOPIC)
    parser.add_argument("--deskewed-topic", default=DEFAULT_DESKEWED_TOPIC)
    parser.add_argument("--fixed-frame", default=DEFAULT_FIXED_FRAME)
    parser.add_argument("--rmw", default=None)
    parser.add_argument("--domain-id", default=None)
    parser.add_argument(
        "--discovery-range",
        choices=("LOCALHOST", "SUBNET", "OFF", "SYSTEM_DEFAULT"),
        default="SUBNET",
    )
    parser.add_argument("--skip-topic-probe", action="store_true")
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--rviz-display-mode", choices=("both", "raw"), default="both")
    return parser.parse_args(argv)


def normalize_topic(topic: str) -> str:
    """Normalize a ROS topic."""

    value = (topic or "").strip()
    if not value:
        raise ValueError("topic must not be empty")
    return value if value.startswith("/") else "/" + value


def normalize_frame(frame: str) -> str:
    """Normalize an RViz fixed frame."""

    value = (frame or "").strip().strip("/")
    if not value:
        raise ValueError("fixed frame must not be empty")
    return value


def suffix(value: str) -> str:
    """Create a safe config suffix."""

    return re.sub(r"[^A-Za-z0-9_.-]+", "_", value.strip("/")) or "phase138u"


def pointcloud_display(name: str, topic: str, color: str, size: int) -> str:
    """Return one RViz2 PointCloud2 display YAML fragment."""

    return f"""    - Class: rviz_default_plugins/PointCloud2
      Enabled: true
      Name: {name} {topic}
      Topic:
        Depth: 1
        Durability Policy: Volatile
        History Policy: Keep Last
        Reliability Policy: Best Effort
        Value: {topic}
      Style: Points
      Size (Pixels): {size}
      Color: {color}
      Color Transformer: FlatColor
      Queue Size: 10
"""


def write_config(
    workspace_root: pathlib.Path,
    raw_topic: str,
    deskewed_topic: str,
    fixed_frame: str,
    display_mode: str = "both") -> pathlib.Path:
    """Write a runtime RViz2 config."""

    pointcloud_displays = pointcloud_display("Raw PointCloud2", raw_topic, "255; 0; 0", 2)
    if display_mode == "both":
        pointcloud_displays += pointcloud_display("Deskewed PointCloud2", deskewed_topic, "0; 255; 255", 3)

    config = f"""Panels:
  - Class: rviz_common/Displays
    Help Height: 78
    Name: Displays
    Property Tree Widget:
      Expanded:
        - /Global Options1
        - /Status1
        - /Grid1
        - /TF1
        - /Raw PointCloud21
      Splitter Ratio: 0.5
    Tree Height: 640
  - Class: rviz_common/Views
    Expanded:
      - /Current View1
    Name: Views
    Splitter Ratio: 0.5
  - Class: rviz_common/Time
    Experimental: false
    Name: Time
    SyncMode: 0
    SyncSource: ""
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
{pointcloud_displays}  Enabled: true
  Global Options:
    Background Color: 48; 48; 48
    Fixed Frame: {fixed_frame}
    Frame Rate: 30
  Name: root
  Tools:
    - Class: rviz_default_plugins/Interact
      Hide Inactive Objects: true
    - Class: rviz_default_plugins/MoveCamera
    - Class: rviz_default_plugins/Select
    - Class: rviz_default_plugins/FocusCamera
    - Class: rviz_default_plugins/Measure
  Value: true
  Views:
    Current:
      Class: rviz_default_plugins/Orbit
      Distance: 12
      Focal Point:
        X: 0
        Y: 0
        Z: 0
      Name: Current View
      Near Clip Distance: 0.01
      Pitch: 0.785398
      Target Frame: <Fixed Frame>
      Value: Orbit (rviz)
      Yaw: 0.785398
    Saved: ~
Window Geometry:
  Displays:
    collapsed: false
  Height: 960
  Hide Left Dock: false
  Hide Right Dock: false
  QMainWindow State: 000000ff00000000fd00000004000000000000013c0000025ffc0200000008fb0000001200530065006c0065006300740069006f006e00000001e10000009b0000006400fffffffb0000001e0054006f006f006c002000500072006f007000650072007400690065007302000001ed000001df00000185000000a3fb000000120056006900650077007300200054006f006f02000001df000002110000018500000122fb000000200054006f006f006c002000500072006f0070006500720074006900650073003203000002880000011d000002210000017afb000000100044006900730070006c00610079007301000000410000025f000000dd00fffffffb0000002000730065006c0065006300740069006f006e00200062007500660066006500720200000138000000aa0000023a00000294fb00000014005700690064006500530074006500720065006f02000000e6000000d2000003ee0000030bfb0000000c004b0069006e0065006300740200000186000001060000030c00000261000000010000010f0000025ffc0200000003fb0000001e0054006f006f006c002000500072006f00700065007200740069006500730100000041000000780000000000000000fb0000000a0056006900650077007301000000410000025f000000b000fffffffb0000001200530065006c0065006300740069006f006e010000025a000000b200000000000000000000000200000490000000a9fc0100000001fb0000000a00560069006500770073030000004e00000080000002e10000019700000003000004420000003efc0100000002fb0000000800540069006d00650100000000000004420000028900fffffffb0000000800540069006d00650100000000000004500000000000000000000001eb0000025f00000004000000040000000800000008fc0000000100000002000000010000000a0054006f006f006c00730100000000ffffffff0000000000000000
  Time:
    collapsed: false
  Views:
    collapsed: false
  Width: 1600
  X: 60
  Y: 60
"""
    output_dir = workspace_root / "build" / "rviz2"
    output_dir.mkdir(parents=True, exist_ok=True)
    path = output_dir / f"phase138u_{suffix(raw_topic)}_{suffix(deskewed_topic)}_{display_mode}.rviz"
    path.write_text(config, encoding="utf-8")
    return path


def probe_topics(pixi_python: pathlib.Path, ros2_script: pathlib.Path, env: dict[str, str]) -> None:
    """Print bounded ROS2 graph diagnostics."""

    probes = (
        (["topic", "list", "-t", "--no-daemon"], 5.0),
        (["topic", "echo", "/tf", "tf2_msgs/msg/TFMessage", "--once", "--no-daemon"], 12.0),
    )
    for args, timeout_seconds in probes:
        print("--- ros2 " + " ".join(args) + " ---")
        try:
            result = ros2env.run_ros2(
                pixi_python,
                ros2_script,
                env,
                args,
                check=False,
                timeout_seconds=timeout_seconds,
            )
            print(result.stdout)
        except subprocess.TimeoutExpired:
            print(f"<probe timed out after {timeout_seconds:.1f}s>")


def main(argv: list[str]) -> int:
    """Launch RViz2."""

    args = parse_args(argv)
    workspace_root = pathlib.Path(__file__).resolve().parents[3]
    raw_topic = normalize_topic(args.raw_topic)
    deskewed_topic = normalize_topic(args.deskewed_topic)
    fixed_frame = normalize_frame(args.fixed_frame)

    ros2_root = ros2env.resolve_existing_path(
        args.ros2_root,
        "ROS2 root",
        workspace_root,
    )
    pixi_python, ros2_script = ros2env.validate_ros2_root(ros2_root)
    env = ros2env.build_ros_env(
        ros2_root,
        rmw_implementation=args.rmw,
        discovery_range=args.discovery_range,
        domain_id=args.domain_id,
    )
    config_path = write_config(
        workspace_root,
        raw_topic,
        deskewed_topic,
        fixed_frame,
        args.rviz_display_mode)
    print(f"[phase138u-rviz2] config: {config_path}")
    print(f"[phase138u-rviz2] raw={raw_topic} deskewed={deskewed_topic} fixed={fixed_frame}")
    print(
        "[phase138u-rviz2] "
        f"RMW={env.get('RMW_IMPLEMENTATION')} "
        f"discovery={env.get('ROS_AUTOMATIC_DISCOVERY_RANGE', '<unset>')} "
        f"fastdds_transports={env.get('FASTDDS_BUILTIN_TRANSPORTS', '<unset>')}"
    )

    if not args.skip_topic_probe:
        probe_topics(pixi_python, ros2_script, env)

    if args.dry_run:
        return 0

    process = ros2env.launch_rviz(
        ros2_root,
        config_path,
        env=env,
        log_prefix="phase138u-rviz2",
        startup_check_seconds=1.5,
        window_wait_seconds=0.0,
    )
    print(f"[phase138u-rviz2] launched rviz2 pid={process.pid}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
