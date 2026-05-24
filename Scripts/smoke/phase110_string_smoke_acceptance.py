#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Module: Scripts/smoke
# Purpose: Manual ROS2-side acceptance helper for Phase110 String Smoke.

"""Run the external ROS2 side of the Phase110 String Smoke acceptance.

Start Unity manually first, open Assets/Scenes/Phase106Acceptance.unity, set
String Smoke to the mode you want to test, and press Play. This script then
sets a Windows ROS2 Jazzy environment and validates the external graph:

  1. /unity2foxglove/ros2forunity/string/in has a Unity subscription.
  2. /unity2foxglove/ros2forunity/string/out echoes a Unity tick.
  3. Publishing three messages to /in succeeds.

The optional positional path is the ROS2 Jazzy Windows root, not the Unity
project path. It defaults to C:\\ros2_jazzy\\ros2-windows.
"""

from __future__ import annotations

import argparse
import os
import pathlib
import subprocess
import sys
import time


DEFAULT_ROS2_ROOT = pathlib.Path(r"C:\ros2_jazzy\ros2-windows")
NODE_NAME = "unity2foxglove_phase110"
IN_TOPIC = "/unity2foxglove/ros2forunity/string/in"
OUT_TOPIC = "/unity2foxglove/ros2forunity/string/out"
MSG_TYPE = "std_msgs/msg/String"


def parse_args(argv: list[str]) -> argparse.Namespace:
    """Parse command-line arguments."""

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "ros2_root",
        nargs="?",
        default=str(DEFAULT_ROS2_ROOT),
        help="Windows ROS2 Jazzy root. Default: C:\\ros2_jazzy\\ros2-windows",
    )
    parser.add_argument(
        "--mode",
        choices=("direct", "facade"),
        default="direct",
        help="Label used in the published manual acceptance payload.",
    )
    parser.add_argument(
        "--message",
        default="",
        help="Override the String payload published to Unity.",
    )
    parser.add_argument(
        "--wait-seconds",
        type=float,
        default=60.0,
        help="How long to wait for Unity's /in subscription.",
    )
    parser.add_argument(
        "--echo-spin-seconds",
        type=float,
        default=20.0,
        help="ROS2 spin time for echoing one Unity /out message.",
    )
    parser.add_argument(
        "--publish-count",
        type=int,
        default=3,
        help="Number of messages to publish to Unity's /in topic.",
    )
    parser.add_argument(
        "--rate",
        type=float,
        default=1.0,
        help="Publish rate in Hz.",
    )
    return parser.parse_args(argv)


def build_ros_env(ros2_root: pathlib.Path) -> dict[str, str]:
    """Build a deterministic Windows ROS2 Jazzy environment."""

    pixi = ros2_root / ".pixi" / "envs" / "default"
    env = os.environ.copy()
    env["PATH"] = os.pathsep.join(
        [
            str(ros2_root / "bin"),
            str(ros2_root / "Scripts"),
            str(pixi),
            str(pixi / "Library" / "bin"),
            r"C:\Windows\system32",
            r"C:\Windows",
        ]
    )
    env["PYTHONPATH"] = str(ros2_root / "Lib" / "site-packages")
    env["AMENT_PREFIX_PATH"] = str(ros2_root)
    env["CMAKE_PREFIX_PATH"] = str(ros2_root)
    env["COLCON_PREFIX_PATH"] = str(ros2_root)
    env["ROS_VERSION"] = "2"
    env["ROS_PYTHON_VERSION"] = "3"
    env["ROS_DISTRO"] = "jazzy"
    env["ROS_DOMAIN_ID"] = "0"
    env["RMW_IMPLEMENTATION"] = "rmw_fastrtps_cpp"
    env["ROS_AUTOMATIC_DISCOVERY_RANGE"] = "SUBNET"
    env.pop("ROS_LOCALHOST_ONLY", None)
    env.pop("ROS_DISCOVERY_SERVER", None)
    return env


def validate_ros2_root(ros2_root: pathlib.Path) -> tuple[pathlib.Path, pathlib.Path]:
    """Validate ROS2 root and return python.exe plus ros2-script.py paths."""

    pixi_python = ros2_root / ".pixi" / "envs" / "default" / "python.exe"
    ros2_script = ros2_root / "Scripts" / "ros2-script.py"
    missing = [path for path in (pixi_python, ros2_script) if not path.exists()]
    if missing:
        details = "\n".join(f"  missing: {path}" for path in missing)
        raise FileNotFoundError(
            f"Invalid ROS2 Jazzy root: {ros2_root}\n{details}\n"
            "Pass the ROS2 root, for example: "
            r"python Scripts\smoke\phase110_string_smoke_acceptance.py C:\ros2_jazzy\ros2-windows"
        )
    return pixi_python, ros2_script


def run_ros2(
    pixi_python: pathlib.Path,
    ros2_script: pathlib.Path,
    env: dict[str, str],
    args: list[str],
    check: bool = True,
) -> subprocess.CompletedProcess[str]:
    """Run ros2-script.py with captured output."""

    result = subprocess.run(
        [str(pixi_python), str(ros2_script), *args],
        env=env,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        check=False,
    )
    if check and result.returncode != 0:
        raise RuntimeError(
            "ROS2 command failed:\n"
            + " ".join(["ros2", *args])
            + f"\nexit={result.returncode}\n{result.stdout}"
        )
    return result


def wait_for_subscription(
    pixi_python: pathlib.Path,
    ros2_script: pathlib.Path,
    env: dict[str, str],
    timeout_seconds: float,
) -> str:
    """Wait until Unity exposes a subscription on the /in topic."""

    deadline = time.monotonic() + timeout_seconds
    last_output = ""
    while time.monotonic() < deadline:
        result = run_ros2(
            pixi_python,
            ros2_script,
            env,
            ["topic", "info", IN_TOPIC, "-v", "--no-daemon"],
            check=False,
        )
        last_output = result.stdout
        if "Subscription count: 1" in last_output and f"Node name: {NODE_NAME}" in last_output:
            return last_output
        time.sleep(2)

    topic_list = run_ros2(
        pixi_python,
        ros2_script,
        env,
        ["topic", "list", "-t", "--no-daemon"],
        check=False,
    ).stdout
    raise TimeoutError(
        f"Timed out waiting for Unity subscription on {IN_TOPIC}.\n"
        "Make sure Unity is in Play mode, String Smoke has Enable Subscription checked, "
        "and the ROS_DOMAIN_ID/RMW settings match.\n"
        f"Current topic list:\n{topic_list}\n"
        f"Last output:\n{last_output}"
    )


def main(argv: list[str]) -> int:
    """Script entry point."""

    args = parse_args(argv)
    ros2_root = pathlib.Path(args.ros2_root).resolve()
    pixi_python, ros2_script = validate_ros2_root(ros2_root)
    env = build_ros_env(ros2_root)
    payload = args.message or f"phase110 {args.mode} manual acceptance"

    print(f"[phase110] ROS2 root: {ros2_root}")
    print(f"[phase110] Mode label: {args.mode}")
    print(f"[phase110] Waiting for Unity subscription: {IN_TOPIC}")
    info = wait_for_subscription(
        pixi_python,
        ros2_script,
        env,
        args.wait_seconds,
    )
    print("--- topic info /in ---")
    print(info.rstrip())

    print("--- echo /out ---")
    echo = run_ros2(
        pixi_python,
        ros2_script,
        env,
        [
            "topic",
            "echo",
            OUT_TOPIC,
            MSG_TYPE,
            "--once",
            "--spin-time",
            str(args.echo_spin_seconds),
            "--no-daemon",
        ],
    ).stdout
    print(echo.rstrip())
    if "unity2foxglove string tick" not in echo:
        raise RuntimeError(f"Did not receive Unity String Smoke tick on {OUT_TOPIC}.\n{echo}")

    print("--- pub /in ---")
    pub = run_ros2(
        pixi_python,
        ros2_script,
        env,
        [
            "topic",
            "pub",
            IN_TOPIC,
            MSG_TYPE,
            "{data: '" + payload.replace("'", " ") + "'}",
            "--times",
            str(args.publish_count),
            "--rate",
            str(args.rate),
            "--wait-matching-subscriptions",
            "1",
            "--max-wait-time-secs",
            str(max(5, int(args.wait_seconds))),
            "--keep-alive",
            "3",
        ],
    ).stdout
    print(pub.rstrip())

    print("[phase110] GREEN: external ROS2 echo/pub completed.")
    print("[phase110] Check Unity Inspector: Received Count should increase and Last Received should match:")
    print(f"[phase110]   {payload}")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main(sys.argv[1:]))
    except KeyboardInterrupt:
        raise SystemExit(130)
    except Exception as exc:
        print(f"[phase110] FAIL: {exc}", file=sys.stderr)
        raise SystemExit(1)
