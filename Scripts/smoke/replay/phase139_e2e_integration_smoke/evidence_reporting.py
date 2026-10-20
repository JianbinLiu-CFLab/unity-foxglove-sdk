"""MCAP evidence, JSON output, and status rendering for Phase139."""

from __future__ import annotations

try:
    from Scripts.smoke.atomic_output import atomic_write_bytes, atomic_write_text
except ModuleNotFoundError:
    import sys as _sys
    from pathlib import Path as _Path
    _sys.path.insert(0, str(_Path(__file__).resolve().parents[4]))
    from Scripts.smoke.atomic_output import atomic_write_bytes, atomic_write_text

import json
import pathlib
from typing import Any

from .protocol_surface import *
from .summary_models import *


def inspect_mcap(path_text: str) -> dict[str, Any]:
    """Return a small MCAP evidence summary when the operator supplies a file."""

    if not path_text:
        return {"checked": False, "status": "skipped"}
    path = pathlib.Path(path_text)
    if not path.exists():
        return {"checked": True, "status": "fail", "path": str(path), "error": "file does not exist"}
    size = path.stat().st_size
    if size == 0:
        return {"checked": True, "status": "fail", "path": str(path), "size_bytes": 0, "error": "empty file"}
    with path.open("rb") as handle:
        prefix = handle.read(8)
    looks_like_mcap = prefix.startswith(b"\x89MCAP")
    return {
        "checked": True,
        "status": "pass" if looks_like_mcap else "limited",
        "path": str(path),
        "size_bytes": size,
        "magic_prefix": prefix.hex(),
        "note": "Use the existing MCAP reader inspections for deep schema and attachment checks.",
    }


def ros2_native_summary(args: argparse.Namespace) -> dict[str, Any]:
    """Explain delegated ROS2 Native acceptance without launching ROS2 in self-test."""

    helper_commands = [
        f"python Scripts/smoke/ros2/phase138s_imu_native_dds_acceptance.py --topic {args.imu_topic} --expected-frame-id os_imu",
        f"python Scripts/smoke/ros2/phase138t_camera_raw_image_dds_acceptance.py --topic {args.raw_image_topic} --expected-frame-id os_camera --expected-width 640 --expected-height 480 --expected-encoding rgb8",
        f"python Scripts/smoke/ros2/phase138u_lidar_deskew_rviz2_acceptance.py --raw-topic {args.pointcloud_topic} --deskewed-topic {args.deskewed_topic}",
    ]
    return {
        "checked": False,
        "status": "delegated",
        "runtime_helpers": helper_commands,
        "implementation_notes": [
            "Use _ros2_windows_env for pinned Windows Jazzy paths.",
            "Use direct rclpy subscribers as the hard gate when ros2 topic list times out.",
        ],
    }


def write_json(path_text: str, summary: dict[str, Any]) -> None:
    """Write evidence JSON if requested."""

    if not path_text:
        return
    path = pathlib.Path(path_text)
    path.parent.mkdir(parents=True, exist_ok=True)
    atomic_write_text(path, json.dumps(summary, indent=2, sort_keys=True) + "\n", encoding="utf-8")


def print_summary(summary: dict[str, Any]) -> None:
    """Print stable JSON to stdout."""

    print(json.dumps(summary, indent=2, sort_keys=True))


def determine_exit_code(summary: dict[str, Any], allow_ros2_skipped: bool) -> int:
    """Map the JSON status to a process exit code."""

    status = summary.get("status")
    if status in ("pass", "pass_with_limitations"):
        return 0
    if status == "limited" and allow_ros2_skipped:
        return 0
    return 1



__all__ = [name for name in globals() if not name.startswith("__")]
