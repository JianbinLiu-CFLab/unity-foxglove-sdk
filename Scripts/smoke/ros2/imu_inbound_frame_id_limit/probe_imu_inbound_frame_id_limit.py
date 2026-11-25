#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Run the live IMU inbound frame_id limit probe per ROS2 distro, or receive external publishers.
# Usage: python Scripts/smoke/ros2/imu_inbound_frame_id_limit/probe_imu_inbound_frame_id_limit.py --distro all
#        ... --distro jazzy --receive-topic /imu/data   (then type `negative` and `end` on stdin)
# Outputs: Builds under repo build/ and prints one PASS/FAIL result per fresh distro process.

"""Run the real IMU inbound bridge over live ros2cs DDS at and above the 256-byte frame_id limit."""

from __future__ import annotations

import argparse
import os
from pathlib import Path
import subprocess
import sys

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "packaged_message_lifetime"))

from probe_packaged_message_lifetime import DISTROS, distro_environment, repo_root, run_checked  # noqa: E402


BUILD_TIMEOUT_SECONDS = 300
PROBE_TIMEOUT_SECONDS = 120


def build_probe(root: Path, distro: str, env: dict[str, str]) -> Path:
    """Build the probe for one ROS2 distribution and return its DLL path."""

    project = (
        root / "Scripts" / "smoke" / "ros2" / "imu_inbound_frame_id_limit"
        / "ImuInboundFrameIdLimitProbe" / "ImuInboundFrameIdLimitProbe.csproj"
    )
    output = root / "build" / "smoke" / "ros2" / "imu-inbound-frame-id-limit" / distro
    intermediate = output / "obj"
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
    probe_dll = output / "bin" / "Debug" / "net10.0" / "ImuInboundFrameIdLimitProbe.dll"
    if not probe_dll.is_file():
        raise RuntimeError(f"{distro}: build did not create expected probe DLL: {probe_dll}")
    return probe_dll


def main() -> int:
    """Parse CLI arguments and run the limit probe or one receive window."""

    parser = argparse.ArgumentParser()
    parser.add_argument("--distro", choices=("all",) + DISTROS, default="all")
    parser.add_argument(
        "--receive-topic",
        help="Receive external publishers on this topic. Keeps the caller's ROS_DOMAIN_ID (default 0) and reads "
             "`negative` and then `end` from stdin.",
    )
    parser.add_argument("--seconds", type=int, default=300, help="Receive timeout. Default: 300")
    parser.add_argument("--expect", default="trusted", help="frame_id of the authorized liveness publisher.")
    parser.add_argument("--forbid", default="unauthorized", help="frame_id of the publisher that must not be delivered.")
    args = parser.parse_args()

    root = repo_root()
    distros = DISTROS if args.distro == "all" else (args.distro,)
    if args.receive_topic and len(distros) != 1:
        parser.error("--receive-topic needs one --distro")
    try:
        for distro in distros:
            env = distro_environment(root, distro)
            probe_dll = build_probe(root, distro, env)
            if not args.receive_topic:
                run_checked(["dotnet", str(probe_dll), distro, "limit"], cwd=root, env=env, timeout=PROBE_TIMEOUT_SECONDS)
                continue
            env["ROS_DOMAIN_ID"] = os.environ.get("ROS_DOMAIN_ID", "0")
            command = ["dotnet", str(probe_dll), distro, "receive", args.receive_topic, str(args.seconds), args.expect, args.forbid]
            result = subprocess.run(command, cwd=root, env=env, timeout=PROBE_TIMEOUT_SECONDS + args.seconds, check=False)
            if result.returncode != 0:
                raise RuntimeError(f"{distro}: receive classification failed with exit code {result.returncode}")
    except (RuntimeError, subprocess.TimeoutExpired) as error:
        print(f"PROBE_FAILURE {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
