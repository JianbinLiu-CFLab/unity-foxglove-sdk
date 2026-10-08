#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

from __future__ import annotations

from .foundation import *

from pathlib import Path as _Phase192Path
_PHASE192_FACADE_FILE = _Phase192Path(__file__).resolve().parents[1] / "launch_phase138t_camera_raw_rviz2.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _Phase192Path, _PHASE192_FACADE_FILE

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

__all__ = [name for name in globals() if not name.startswith("__")]
