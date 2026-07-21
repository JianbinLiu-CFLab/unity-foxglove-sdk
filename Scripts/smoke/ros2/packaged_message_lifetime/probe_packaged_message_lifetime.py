#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Run bounded packaged ros2cs message-lifetime smoke probes per ROS2 distro.
# Usage: python Scripts/smoke/ros2/packaged_message_lifetime/probe_packaged_message_lifetime.py --distro all
# Outputs: Builds under repo build/ and prints one PASS/FAIL result per fresh distro process.

"""Run bounded ros2cs message lifetime probes in fresh per-distro processes."""

from __future__ import annotations

import argparse
import os
from pathlib import Path
import subprocess
import sys


DISTROS = ("humble", "jazzy", "lyrical")
BUILD_TIMEOUT_SECONDS = 60
PROBE_TIMEOUT_SECONDS = 30


def repo_root() -> Path:
    """Return the repository root containing this packaged-runtime smoke helper."""

    return Path(__file__).resolve().parents[4]


def clean_path(path_value: str) -> list[str]:
    """Remove previously selected ROS2/runtime entries from a PATH value."""

    markers = ("ros2-windows", "ros2_humble", "ros2_jazzy", "ros2_lyrical", "ros2forunity.runtime.")
    return [
        entry
        for entry in path_value.split(os.pathsep)
        if entry and not any(marker in entry.lower() for marker in markers)
    ]


def distro_environment(root: Path, distro: str) -> dict[str, str]:
    """Build an isolated environment for one packaged ROS2 distribution."""

    runtime = root / "Packages" / f"dev.unity2foxglove.ros2forunity.runtime.{distro}.win64" / "Runtime" / "Ros2ForUnity"
    plugins = runtime / "Plugins"
    native = plugins / "Windows" / "x86_64"
    ros_bin = root / "ros2-windows" / f"ros2_{distro}" / "bin"
    required = (plugins, native, ros_bin)
    missing = [str(path) for path in required if not path.is_dir()]
    if missing:
        raise RuntimeError(f"{distro}: required runtime directories are missing: {missing}")

    env = os.environ.copy()
    env["PATH"] = os.pathsep.join([str(path) for path in required] + clean_path(env.get("PATH", "")))
    env["RMW_IMPLEMENTATION"] = "rmw_fastrtps_cpp"
    env["ROS_DOMAIN_ID"] = "179"
    env["ROS_DISTRO"] = distro
    env["ROS_VERSION"] = "2"
    env["ROS_PYTHON_VERSION"] = "3"
    env["AMENT_PREFIX_PATH"] = str(root / "ros2-windows" / f"ros2_{distro}")
    env["DOTNET_ROLL_FORWARD"] = "Major"
    return env


def run_checked(command: list[str], *, cwd: Path, env: dict[str, str], timeout: int) -> subprocess.CompletedProcess[str]:
    """Run a bounded child process and raise with captured output on failure."""

    result = subprocess.run(
        command,
        cwd=cwd,
        env=env,
        timeout=timeout,
        check=False,
        shell=False,
        text=True,
        capture_output=True,
    )
    if result.stdout:
        print(result.stdout, end="")
    if result.stderr:
        print(result.stderr, end="", file=sys.stderr)
    if result.returncode != 0:
        raise RuntimeError(f"command failed with exit code {result.returncode}: {command}")
    return result


def run_distro(root: Path, distro: str, iterations: int) -> None:
    """Build and execute the lifetime probe for one ROS2 distribution."""

    project = root / "Scripts" / "smoke" / "ros2" / "packaged_message_lifetime" / "PackagedMessageLifetimeProbe" / "PackagedMessageLifetimeProbe.csproj"
    intermediate = root / "build" / "smoke" / "ros2" / "packaged-message-lifetime" / distro / "obj"
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
    probe_dll = root / "build" / "smoke" / "ros2" / "packaged-message-lifetime" / distro / "bin" / "Debug" / "net8.0" / "PackagedMessageLifetimeProbe.dll"
    if not probe_dll.is_file():
        raise RuntimeError(f"{distro}: build did not create expected probe DLL: {probe_dll}")
    run_checked(
        ["dotnet", str(probe_dll), distro, str(iterations)],
        cwd=root,
        env=env,
        timeout=PROBE_TIMEOUT_SECONDS,
    )


def main() -> int:
    """Parse CLI arguments and run each requested distribution probe."""

    parser = argparse.ArgumentParser()
    parser.add_argument("--distro", choices=("all",) + DISTROS, default="all")
    parser.add_argument("--iterations", type=int, default=128)
    args = parser.parse_args()
    if not 1 <= args.iterations <= 1000:
        parser.error("--iterations must be in the bounded range 1..1000")

    root = repo_root()
    distros = DISTROS if args.distro == "all" else (args.distro,)
    try:
        for distro in distros:
            run_distro(root, distro, args.iterations)
    except (RuntimeError, subprocess.TimeoutExpired) as error:
        print(f"PROBE_FAILURE {error}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
