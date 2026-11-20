#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Run the live PointCloud2 inbound topic-switch probe per ROS2 distro.
# Usage: python Scripts/smoke/ros2/pointcloud2_inbound_topic_switch/probe_pointcloud2_inbound_topic_switch.py --distro all
# Outputs: Builds under repo build/ and prints one PASS/FAIL result per fresh distro process.

"""Run the real PointCloud2 inbound bridge over live ros2cs DDS through a topic switch."""

from __future__ import annotations

import argparse
import os
from pathlib import Path
import subprocess
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "packaged_message_lifetime"))

from probe_packaged_message_lifetime import DISTROS, distro_environment, repo_root, run_checked  # noqa: E402


BUILD_TIMEOUT_SECONDS = 300
PROBE_TIMEOUT_SECONDS = 90


def run_distro(root: Path, distro: str) -> None:
    """Build and execute the topic-switch probe for one ROS2 distribution."""

    project = (
        root / "Scripts" / "smoke" / "ros2" / "pointcloud2_inbound_topic_switch"
        / "PointCloud2InboundTopicSwitchProbe" / "PointCloud2InboundTopicSwitchProbe.csproj"
    )
    output = root / "build" / "smoke" / "ros2" / "pointcloud2-inbound-topic-switch" / distro
    intermediate = output / "obj"
    env = distro_environment(root, distro)
    run_checked(
        [
            "dotnet",
            "build",
            str(project),
            "--nologo",
            "-p:Distro=" + distro,
            "-p:BaseIntermediateOutputPath=" + str(intermediate) + os.sep,
            "-p:MSBuildProjectExtensionsPath=" + str(intermediate) + os.sep,
        ],
        cwd=root,
        env=env,
        timeout=BUILD_TIMEOUT_SECONDS,
    )
    probe_dll = output / "bin" / "Debug" / "net10.0" / "PointCloud2InboundTopicSwitchProbe.dll"
    if not probe_dll.is_file():
        raise RuntimeError(f"{distro}: build did not create expected probe DLL: {probe_dll}")
    run_checked(["dotnet", str(probe_dll), distro], cwd=root, env=env, timeout=PROBE_TIMEOUT_SECONDS)


def main() -> int:
    """Parse CLI arguments and run each requested distribution probe."""

    parser = argparse.ArgumentParser()
    parser.add_argument("--distro", choices=("all",) + DISTROS, default="all")
    args = parser.parse_args()

    root = repo_root()
    distros = DISTROS if args.distro == "all" else (args.distro,)
    try:
        for distro in distros:
            run_distro(root, distro)
    except (RuntimeError, subprocess.TimeoutExpired) as error:
        print(f"PROBE_FAILURE {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
