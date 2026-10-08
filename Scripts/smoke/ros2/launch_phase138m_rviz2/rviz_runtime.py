#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

from __future__ import annotations

from .foundation import *

from pathlib import Path as _Phase192Path
_PHASE192_FACADE_FILE = _Phase192Path(__file__).resolve().parents[1] / "launch_phase138m_rviz2.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _Phase192Path, _PHASE192_FACADE_FILE

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

__all__ = [name for name in globals() if not name.startswith("__")]
