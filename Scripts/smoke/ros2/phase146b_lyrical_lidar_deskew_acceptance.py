#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Module: Scripts/smoke
# Purpose: Phase146B Lyrical wrapper for the Phase138U LiDAR deskew RViz2 acceptance.

from __future__ import annotations

import sys

import _ros2_windows_env as ros2env
import phase138u_lidar_deskew_rviz2_acceptance as phase138u


DEFAULT_LYRICAL_ROOT = str(ros2env.default_ros2_root("lyrical"))
REQUIRE_MOTION_FLAG = "--require-motion"
ALLOW_STATIC_FLAG = "--allow-static"


def main(argv: list[str]) -> int:
    """Run the Phase138U LiDAR deskew acceptance against Lyrical by default."""

    args = list(argv)
    if REQUIRE_MOTION_FLAG in args and ALLOW_STATIC_FLAG in args:
        raise ValueError("--require-motion and --allow-static are mutually exclusive")
    if "--ros2-root" not in args:
        args = ["--ros2-root", DEFAULT_LYRICAL_ROOT, *args]
    if REQUIRE_MOTION_FLAG in args:
        args.remove(REQUIRE_MOTION_FLAG)
        if "--rviz-display-mode" not in args:
            args.extend(["--rviz-display-mode", "both"])
    elif ALLOW_STATIC_FLAG not in args:
        args.append(ALLOW_STATIC_FLAG)
        if "--rviz-display-mode" not in args:
            args.extend(["--rviz-display-mode", "both"])

    print("[phase146b-lyrical-lidar-deskew] Unity must run the Lyrical Win64 runtime package in standalone mode.")
    print("[phase146b-lyrical-lidar-deskew] External ROS2/RViz2 probes use the repo-local ros2-windows/ros2_lyrical entrypoint; Unity must not source that environment.")
    print("[phase146b-lyrical-lidar-deskew] Static captures are accepted by default for runtime/DDS wiring; pass --require-motion for strict deskew motion proof.")
    return phase138u.main(args)


if __name__ == "__main__":
    try:
        raise SystemExit(main(sys.argv[1:]))
    except KeyboardInterrupt:
        raise SystemExit(130)
    except phase138u.InconclusiveError as exc:
        print(f"[phase146b-lyrical-lidar-deskew] INCONCLUSIVE: {exc}", file=sys.stderr)
        raise SystemExit(2)
    except Exception as exc:
        print(f"[phase146b-lyrical-lidar-deskew] FAIL: {exc}", file=sys.stderr)
        print(
            "[phase146b-lyrical-lidar-deskew] If Unity Console reports \"You should not source ROS2\", "
            "restart Unity after this package fix compiles so the standalone runtime can sanitize ROS2 env vars.",
            file=sys.stderr,
        )
        raise SystemExit(1)
