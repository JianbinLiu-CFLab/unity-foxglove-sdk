#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Module: Scripts/smoke
# Purpose: Launch Phase128 RViz2 with the Windows Jazzy runtime environment.

"""Launch RViz2 for the Phase128 TF/LaserScan manual acceptance.

This launcher avoids project-owned PowerShell scripts while preserving the
Windows Jazzy DLL search path needed by RViz2. Keep Unity in Play Mode with
Phase128Rviz2TfLaserScanSmoke publishing before launching RViz2.
"""

from __future__ import annotations

import argparse
import os
import pathlib
import subprocess
import sys


DEFAULT_ROS2_ROOT = pathlib.Path(r"C:\ros2_jazzy\ros2-windows")
DEFAULT_RVIZ_CONFIG = pathlib.Path(
    r"Packages\dev.unity2foxglove.ros2forunity\Samples~"
    r"\RViz2 Standard Visualization Acceptance\rviz2_phase128_tf_laserscan.rviz"
)


def parse_args(argv: list[str]) -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--ros2-root",
        default=str(DEFAULT_ROS2_ROOT),
        help="Windows ROS2 Jazzy root. Default: C:\\ros2_jazzy\\ros2-windows",
    )
    parser.add_argument(
        "--rviz-config",
        default=str(DEFAULT_RVIZ_CONFIG),
        help="RViz2 config path. Relative paths resolve from the current workspace root.",
    )
    parser.add_argument(
        "--discovery-range",
        choices=("LOCALHOST", "SUBNET", "OFF", "SYSTEM_DEFAULT"),
        default=None,
        help="Optional ROS_AUTOMATIC_DISCOVERY_RANGE override. Omit for the known-good unset Phase128 path.",
    )
    parser.add_argument(
        "--detached",
        action="store_true",
        help="Launch RViz2 and return immediately instead of waiting for RViz2 to close.",
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Print the resolved command and environment summary without launching RViz2.",
    )
    parser.add_argument(
        "--no-software-rendering",
        action="store_true",
        help="Do not set QT_OPENGL/QT_QUICK_BACKEND/LIBGL_ALWAYS_SOFTWARE.",
    )
    return parser.parse_args(argv)


def find_workspace_root() -> pathlib.Path:
    """Find the repository root from either cwd or this script location."""

    starts = [pathlib.Path.cwd(), pathlib.Path(__file__).resolve().parent]
    for start in starts:
        for candidate in (start, *start.parents):
            if (candidate / "Packages").is_dir() and (candidate / "Scripts").is_dir():
                return candidate
    return pathlib.Path.cwd()


def resolve_existing_path(path_text: str, description: str, workspace_root: pathlib.Path) -> pathlib.Path:
    path = pathlib.Path(path_text)
    candidates = [path] if path.is_absolute() else [workspace_root / path, pathlib.Path.cwd() / path]
    for candidate in candidates:
        try:
            return candidate.resolve(strict=True)
        except FileNotFoundError:
            continue

    path = candidates[0]
    try:
        return path.resolve(strict=True)
    except FileNotFoundError as exc:
        raise FileNotFoundError(f"{description} does not exist: {path}") from exc


def build_rviz_env(
    ros2_root: pathlib.Path,
    discovery_range: str | None,
    software_rendering: bool,
) -> dict[str, str]:
    pixi = ros2_root / ".pixi" / "envs" / "default"
    path_entries = [
        ros2_root / "bin",
        ros2_root / "Scripts",
        pixi,
        pixi / "Library" / "bin",
        pixi / "Scripts",
        ros2_root / "opt" / "rviz_ogre_vendor" / "bin",
        ros2_root / "opt" / "gz_math_vendor" / "bin",
        pathlib.Path(r"C:\Windows\system32"),
        pathlib.Path(r"C:\Windows"),
        pathlib.Path(r"C:\Windows\System32\Wbem"),
        pathlib.Path(r"C:\Windows\System32\WindowsPowerShell\v1.0"),
    ]

    missing = [path for path in path_entries if not path.exists()]
    if missing:
        details = "\n".join(f"  missing: {path}" for path in missing)
        raise FileNotFoundError(f"Required RViz2 PATH entries are missing:\n{details}")

    env = os.environ.copy()
    env["PATH"] = os.pathsep.join(str(path) for path in path_entries)
    env["PYTHONPATH"] = str(ros2_root / "Lib" / "site-packages")
    env["AMENT_PREFIX_PATH"] = str(ros2_root)
    env["CMAKE_PREFIX_PATH"] = str(ros2_root)
    env["COLCON_PREFIX_PATH"] = str(ros2_root)
    env["COLCON_PYTHON_EXECUTABLE"] = str(pixi / "python.exe")
    env["ROS_VERSION"] = "2"
    env["ROS_PYTHON_VERSION"] = "3"
    env["ROS_DISTRO"] = "jazzy"
    env["ROS_DOMAIN_ID"] = "0"
    env["RMW_IMPLEMENTATION"] = env.get("RMW_IMPLEMENTATION") or "rmw_fastrtps_cpp"

    if discovery_range:
        env["ROS_AUTOMATIC_DISCOVERY_RANGE"] = discovery_range
    else:
        env.pop("ROS_AUTOMATIC_DISCOVERY_RANGE", None)
    env.pop("ROS_LOCALHOST_ONLY", None)
    env.pop("ROS_DISCOVERY_SERVER", None)

    if software_rendering:
        env["QT_OPENGL"] = "software"
        env["QT_QUICK_BACKEND"] = "software"
        env["LIBGL_ALWAYS_SOFTWARE"] = "1"

    return env


def print_summary(
    ros2_root: pathlib.Path,
    rviz_exe: pathlib.Path,
    rviz_config: pathlib.Path,
    env: dict[str, str],
) -> None:
    print(f"[phase128] ROS2 root: {ros2_root}")
    print(f"[phase128] RViz2 exe: {rviz_exe}")
    print(f"[phase128] RViz2 config: {rviz_config}")
    print(f"[phase128] ROS_DISTRO: {env.get('ROS_DISTRO')}")
    print(f"[phase128] RMW_IMPLEMENTATION: {env.get('RMW_IMPLEMENTATION')}")
    print(f"[phase128] ROS_DOMAIN_ID: {env.get('ROS_DOMAIN_ID')}")
    print(f"[phase128] ROS_AUTOMATIC_DISCOVERY_RANGE: {env.get('ROS_AUTOMATIC_DISCOVERY_RANGE', '<unset>')}")
    print(f'[phase128] Command: "{rviz_exe}" -d "{rviz_config}"')


def main(argv: list[str]) -> int:
    args = parse_args(argv)
    workspace_root = find_workspace_root()
    ros2_root = resolve_existing_path(args.ros2_root, "ROS2 root", workspace_root)
    rviz_config = resolve_existing_path(args.rviz_config, "RViz2 config", workspace_root)
    rviz_exe = ros2_root / "bin" / "rviz2.exe"
    if not rviz_exe.exists():
        raise FileNotFoundError(f"rviz2.exe not found: {rviz_exe}")

    env = build_rviz_env(
        ros2_root,
        discovery_range=args.discovery_range,
        software_rendering=not args.no_software_rendering,
    )
    print_summary(ros2_root, rviz_exe, rviz_config, env)

    if args.dry_run:
        print("[phase128] Dry run only; RViz2 was not launched.")
        return 0

    command = [str(rviz_exe), "-d", str(rviz_config)]
    process = subprocess.Popen(command, cwd=str(ros2_root), env=env)
    print(f"[phase128] Launched RViz2 pid={process.pid}")
    if args.detached:
        return 0

    print("[phase128] Waiting for RViz2 to exit. Close RViz2 to return to the terminal.")
    return process.wait()


if __name__ == "__main__":
    try:
        raise SystemExit(main(sys.argv[1:]))
    except Exception as exc:
        print(f"[phase128] FAIL: {exc}", file=sys.stderr)
        raise SystemExit(1)
