#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Module: Scripts/smoke
# Purpose: Manual ROS2-side acceptance helper for Phase127 R2FU real-project smoke.

"""Run the external ROS2 side of the Phase127 real-project smoke.

Start Unity manually first, add/enable Phase127R2FURealProjectSmoke in the
Phase106Acceptance scene, and press Play. This script then sets a Windows ROS2
Jazzy environment and validates the external graph:

  1. /unity2foxglove/phase127/in has a Unity subscription.
  2. /unity2foxglove/phase127/out echoes a Unity tick.
  3. Publishing inbound messages to /in succeeds.

The optional positional path is the ROS2 Jazzy Windows root. It defaults to
C:\\ros2_jazzy\\ros2-windows.
"""

from __future__ import annotations

import argparse
import pathlib
import re
import subprocess
import sys
import time

import _ros2_windows_env as ros2env

DEFAULT_ROS2_ROOT = ros2env.DEFAULT_ROS2_ROOT
NODE_NAME = "unity2foxglove_phase127"
IN_TOPIC = "/unity2foxglove/phase127/in"
OUT_TOPIC = "/unity2foxglove/phase127/out"
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
        "--message",
        default="phase127 manual real-project acceptance",
        help="String payload published to Unity.",
    )
    parser.add_argument(
        "--wait-seconds",
        type=float,
        default=90.0,
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
    parser.add_argument(
        "--rmw",
        default=None,
        help="Override RMW_IMPLEMENTATION. Omit to preserve the shell value or shared default.",
    )
    parser.add_argument(
        "--discovery-range",
        choices=("LOCALHOST", "SUBNET", "OFF", "SYSTEM_DEFAULT"),
        default=None,
        help="Override ROS_AUTOMATIC_DISCOVERY_RANGE. Omit to preserve the shell value or ROS2 default.",
    )
    parser.add_argument(
        "--domain-id",
        default=None,
        help="Override ROS_DOMAIN_ID. Omit to preserve the shared default.",
    )
    return parser.parse_args(argv)


def has_positive_subscription_count(output: str) -> bool:
    """Return whether topic info reports at least one subscription from Unity."""

    match = re.search(r"(?m)^Subscription count:\s*([1-9][0-9]*)\s*$", output)
    return bool(match) and f"Node name: {NODE_NAME}" in output


def wait_for_subscription(
    pixi_python: pathlib.Path,
    ros2_script: pathlib.Path,
    env: dict[str, str],
    timeout_seconds: float,
) -> str:
    """Wait until Unity exposes a subscription on the Phase127 /in topic."""

    deadline = time.monotonic() + timeout_seconds
    last_output = ""
    while time.monotonic() < deadline:
        try:
            result = ros2env.run_ros2(
                pixi_python,
                ros2_script,
                env,
                ["topic", "info", IN_TOPIC, "-v", "--no-daemon"],
                check=False,
                timeout_seconds=min(10.0, max(1.0, deadline - time.monotonic())),
            )
            last_output = result.stdout
        except subprocess.TimeoutExpired as exc:
            last_output = f"<topic info timed out after {exc.timeout:.1f}s>"
        if has_positive_subscription_count(last_output):
            return last_output
        time.sleep(2)

    topic_list = ros2env.run_ros2(
        pixi_python,
        ros2_script,
        env,
        ["topic", "list", "-t", "--no-daemon"],
        check=False,
        timeout_seconds=10.0,
    ).stdout
    raise TimeoutError(
        f"Timed out waiting for Unity subscription on {IN_TOPIC}.\n"
        "Make sure Unity is in Play mode and Phase127R2FURealProjectSmoke is enabled.\n"
        f"Current topic list:\n{topic_list}\n"
        f"Last output:\n{last_output}"
    )


def main(argv: list[str]) -> int:
    """Script entry point."""

    args = parse_args(argv)
    workspace_root = ros2env.find_workspace_root()
    ros2_root = ros2env.resolve_existing_path(args.ros2_root, "ROS2 root", workspace_root)
    pixi_python, ros2_script = ros2env.validate_ros2_root(ros2_root)
    env = ros2env.build_ros_env(ros2_root, args.rmw, args.discovery_range, args.domain_id)
    payload = args.message.replace("'", " ")

    print(f"[phase127] ROS2 root: {ros2_root}")
    print(f"[phase127] ROS_DISTRO: {env.get('ROS_DISTRO')}")
    print(f"[phase127] RMW_IMPLEMENTATION: {env.get('RMW_IMPLEMENTATION')}")
    print(f"[phase127] ROS_DOMAIN_ID: {env.get('ROS_DOMAIN_ID')}")
    print(f"[phase127] ROS_AUTOMATIC_DISCOVERY_RANGE: {env.get('ROS_AUTOMATIC_DISCOVERY_RANGE', '<unset>')}")
    print(f"[phase127] Waiting for Unity subscription: {IN_TOPIC}")
    info = wait_for_subscription(pixi_python, ros2_script, env, args.wait_seconds)
    print("--- topic info /in ---")
    print(info.rstrip())

    print("--- echo /out ---")
    echo = ros2env.run_ros2(
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
        timeout_seconds=args.echo_spin_seconds + 10.0,
    ).stdout
    print(echo.rstrip())
    if "phase127 unity smoke" not in echo:
        raise RuntimeError(f"Did not receive Phase127 Unity tick on {OUT_TOPIC}.\n{echo}")

    print("--- pub /in ---")
    pub = ros2env.run_ros2(
        pixi_python,
        ros2_script,
        env,
        [
            "topic",
            "pub",
            IN_TOPIC,
            MSG_TYPE,
            "{data: '" + payload + "'}",
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
        timeout_seconds=max(20.0, args.wait_seconds + args.publish_count / max(args.rate, 0.1) + 5.0),
    ).stdout
    print(pub.rstrip())

    print("[phase127] GREEN: external ROS2 echo/pub completed.")
    print("[phase127] Check Unity Inspector: Received Count should increase and Last Received should match:")
    print(f"[phase127]   {payload}")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main(sys.argv[1:]))
    except KeyboardInterrupt:
        raise SystemExit(130)
    except Exception as exc:
        print(f"[phase127] FAIL: {exc}", file=sys.stderr)
        raise SystemExit(1)
