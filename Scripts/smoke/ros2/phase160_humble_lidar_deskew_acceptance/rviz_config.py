#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

from __future__ import annotations

from .foundation import *

from pathlib import Path as _Phase192Path
_PHASE192_FACADE_FILE = _Phase192Path(__file__).resolve().parents[1] / "phase160_humble_lidar_deskew_acceptance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _Phase192Path, _PHASE192_FACADE_FILE

def write_rviz_config(
    workspace_root: pathlib.Path,
    raw_topic: str,
    deskewed_topic: str,
    tf_topic: str,
    fixed_frame: str,
    display_mode: str,
) -> pathlib.Path:
    """Write a Phase160-specific runtime RViz2 config."""

    pointcloud_displays = pointcloud_display("Phase160 Raw PointCloud2", raw_topic, "255; 0; 0", 2)
    if display_mode == "both":
        pointcloud_displays += pointcloud_display(
            "Phase160 Deskewed PointCloud2",
            deskewed_topic,
            "0; 255; 255",
            3,
        )

    config = f"""Panels:
  - Class: rviz_common/Displays
    Name: Displays
  - Class: rviz_common/Views
    Name: Views
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
      Name: Phase160 Grid
      Plane: XY
      Reference Frame: <Fixed Frame>
    - Class: rviz_default_plugins/TF
      Enabled: true
      Frame Timeout: 15
      Name: Phase160 TF {tf_topic}
      Show Axes: true
      Show Names: true
      Topic:
        Depth: 20
        Durability Policy: Volatile
        History Policy: Keep Last
        Reliability Policy: Reliable
        Value: {tf_topic}
{pointcloud_displays}  Enabled: true
  Global Options:
    Background Color: 48; 48; 48
    Fixed Frame: {fixed_frame}
    Frame Rate: 30
  Name: Phase160 Humble root
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
      Value: Phase160 Orbit
      Yaw: 0.785398
    Saved: ~
Window Geometry:
  Height: 960
  Width: 1600
  X: 60
  Y: 60
"""
    output_dir = workspace_root / "build" / "rviz2"
    output_dir.mkdir(parents=True, exist_ok=True)
    path = output_dir / (
        f"phase160_humble_{suffix(raw_topic)}_{suffix(deskewed_topic)}_{display_mode}.rviz"
    )
    path.write_text(config, encoding="utf-8")
    return path

__all__ = [name for name in globals() if not name.startswith("__")]
