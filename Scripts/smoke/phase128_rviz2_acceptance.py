#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Module: Scripts/smoke
# Purpose: Manual ROS2 and RViz2 acceptance helper for Phase128 TF/LaserScan.

"""Validate Phase128 RViz2 standard visualization topics.

Start Unity manually first, import the RViz2 Standard Visualization Acceptance
sample, add Phase128Rviz2TfLaserScanSmoke to a scene object, and enter Play
Mode. This helper then uses the pinned Windows ROS2 Jazzy Python entry point to
check the external ROS2 graph and, optionally, launch RViz2.
"""

from __future__ import annotations

import argparse
import math
import os
import pathlib
import re
import subprocess
import sys
import time


DEFAULT_ROS2_ROOT = pathlib.Path(r"C:\ros2_jazzy\ros2-windows")
DEFAULT_RVIZ_CONFIG = pathlib.Path(
    r"Packages\dev.unity2foxglove.ros2forunity\Samples~"
    r"\RViz2 Standard Visualization Acceptance\rviz2_phase128_tf_laserscan.rviz"
)
NODE_NAME = "unity2foxglove_phase128_rviz2"
TF_TOPIC = "/tf"
SCAN_TOPIC = "/scan"
TF_MSG_TYPE = "tf2_msgs/msg/TFMessage"
SCAN_MSG_TYPE = "sensor_msgs/msg/LaserScan"


def parse_args(argv: list[str]) -> argparse.Namespace:
    """Parse command-line arguments."""

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--ros2-root",
        default=str(DEFAULT_ROS2_ROOT),
        help="Windows ROS2 Jazzy root. Default: C:\\ros2_jazzy\\ros2-windows",
    )
    parser.add_argument(
        "--rviz-config",
        default=str(DEFAULT_RVIZ_CONFIG),
        help="RViz2 config path to launch or print in evidence.",
    )
    parser.add_argument(
        "--wait-seconds",
        type=float,
        default=90.0,
        help="How long to wait for Unity's Phase128 node and publishers.",
    )
    parser.add_argument(
        "--echo-spin-seconds",
        type=float,
        default=20.0,
        help="ROS2 spin time for each bounded echo.",
    )
    parser.add_argument(
        "--rmw",
        default=None,
        help="RMW implementation to use. Omit to preserve RMW_IMPLEMENTATION or default to rmw_fastrtps_cpp.",
    )
    parser.add_argument(
        "--discovery-range",
        choices=("LOCALHOST", "SUBNET", "OFF", "SYSTEM_DEFAULT"),
        default=None,
        help=(
            "Override ROS_AUTOMATIC_DISCOVERY_RANGE. Omit to preserve the shell value "
            "or the ROS2 default. Use LOCALHOST for same-machine Unity acceptance if SUBNET sees no Unity node."
        ),
    )
    parser.add_argument(
        "--launch-rviz",
        action="store_true",
        help="Launch RViz2 with --rviz-config after CLI checks pass.",
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
    """Resolve an absolute path or a path relative to the workspace root."""

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


def build_ros_env(
    ros2_root: pathlib.Path,
    rmw_implementation: str | None = None,
    discovery_range: str | None = None,
) -> dict[str, str]:
    """Build a deterministic Windows ROS2 Jazzy environment."""

    pixi = ros2_root / ".pixi" / "envs" / "default"
    env = os.environ.copy()
    env["PATH"] = os.pathsep.join(
        [
            str(ros2_root / "bin"),
            str(ros2_root / "Scripts"),
            str(pixi),
            str(pixi / "Library" / "bin"),
            str(pixi / "Scripts"),
            r"C:\Windows\system32",
            r"C:\Windows",
            r"C:\Windows\System32\Wbem",
            r"C:\Windows\System32\WindowsPowerShell\v1.0",
        ]
    )
    env["PYTHONPATH"] = str(ros2_root / "Lib" / "site-packages")
    env["AMENT_PREFIX_PATH"] = str(ros2_root)
    env["CMAKE_PREFIX_PATH"] = str(ros2_root)
    env["COLCON_PREFIX_PATH"] = str(ros2_root)
    env["COLCON_PYTHON_EXECUTABLE"] = str(pixi / "python.exe")
    env["ROS_VERSION"] = "2"
    env["ROS_PYTHON_VERSION"] = "3"
    env["ROS_DISTRO"] = "jazzy"
    env["ROS_DOMAIN_ID"] = "0"
    env["RMW_IMPLEMENTATION"] = rmw_implementation or env.get("RMW_IMPLEMENTATION") or "rmw_fastrtps_cpp"
    if discovery_range:
        env["ROS_AUTOMATIC_DISCOVERY_RANGE"] = discovery_range
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
        raise FileNotFoundError(f"Invalid ROS2 Jazzy root: {ros2_root}\n{details}")
    return pixi_python, ros2_script


def run_ros2(
    pixi_python: pathlib.Path,
    ros2_script: pathlib.Path,
    env: dict[str, str],
    args: list[str],
    check: bool = True,
    timeout_seconds: float = 30.0,
) -> subprocess.CompletedProcess[str]:
    """Run ros2-script.py with captured output."""

    result = subprocess.run(
        [str(pixi_python), str(ros2_script), *args],
        env=env,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        check=False,
        timeout=timeout_seconds,
    )
    if check and result.returncode != 0:
        raise RuntimeError(
            "ROS2 command failed:\n"
            + " ".join(["ros2", *args])
            + f"\nexit={result.returncode}\n{result.stdout}"
        )
    return result


def probe_node_list(
    pixi_python: pathlib.Path,
    ros2_script: pathlib.Path,
    env: dict[str, str],
    timeout_seconds: float = 10.0,
) -> str:
    """Return one node-list snapshot without making it a hard acceptance gate."""

    try:
        result = run_ros2(
            pixi_python,
            ros2_script,
            env,
            ["node", "list", "--no-daemon"],
            check=False,
            timeout_seconds=timeout_seconds,
        )
    except subprocess.TimeoutExpired:
        return f"<node list timed out after {timeout_seconds:.1f}s>"

    return result.stdout


def probe_topic_info(
    pixi_python: pathlib.Path,
    ros2_script: pathlib.Path,
    env: dict[str, str],
    topic: str,
    timeout_seconds: float = 10.0,
) -> str:
    """Return one verbose topic-info snapshot without making it a hard gate."""

    try:
        result = run_ros2(
            pixi_python,
            ros2_script,
            env,
            ["topic", "info", "-v", topic, "--no-daemon"],
            check=False,
            timeout_seconds=timeout_seconds,
        )
    except subprocess.TimeoutExpired:
        return f"<topic info {topic} timed out after {timeout_seconds:.1f}s>"

    return result.stdout


def wait_for_publisher(
    pixi_python: pathlib.Path,
    ros2_script: pathlib.Path,
    env: dict[str, str],
    topic: str,
    timeout_seconds: float,
) -> str:
    """Wait until a topic has a Phase128 publisher endpoint."""

    deadline = time.monotonic() + timeout_seconds
    last_output = ""
    while time.monotonic() < deadline:
        result = run_ros2(
            pixi_python,
            ros2_script,
            env,
            ["topic", "info", "-v", topic, "--no-daemon"],
            check=False,
            timeout_seconds=10.0,
        )
        last_output = result.stdout
        if has_phase128_publisher(last_output):
            return last_output
        time.sleep(2.0)

    topic_list = run_ros2(
        pixi_python,
        ros2_script,
        env,
        ["topic", "list", "-t", "--no-daemon"],
        check=False,
        timeout_seconds=10.0,
    ).stdout
    raise TimeoutError(
        f"Timed out waiting for Phase128 publisher on {topic}.\n"
        f"Current topic list:\n{topic_list}\nLast topic info:\n{last_output}"
    )


def has_phase128_publisher(topic_info: str) -> bool:
    """Return whether verbose topic info contains a Phase128 publisher endpoint."""

    publisher_match = re.search(r"Publisher count:\s*([1-9][0-9]*)", topic_info)
    return bool(publisher_match) and f"Node name: {NODE_NAME}" in topic_info


def echo_once(
    pixi_python: pathlib.Path,
    ros2_script: pathlib.Path,
    env: dict[str, str],
    topic: str,
    msg_type: str,
    spin_seconds: float,
) -> str:
    """Echo one ROS2 message with bounded spin time."""

    return run_ros2(
        pixi_python,
        ros2_script,
        env,
        [
            "topic",
            "echo",
            "--once",
            topic,
            msg_type,
            "--spin-time",
            str(spin_seconds),
            "--no-daemon",
        ],
        timeout_seconds=spin_seconds + 10.0,
    ).stdout


def validate_tf_echo(output: str) -> None:
    """Validate TF echo contains the required frame tree."""

    missing = [token for token in ("map", "base_link", "laser") if token not in output]
    if missing:
        raise RuntimeError(f"TF echo missing required frame token(s): {', '.join(missing)}\n{output}")


def validate_scan_echo(output: str) -> None:
    """Validate LaserScan echo contains laser frame and finite ranges."""

    if "frame_id: laser" not in output:
        raise RuntimeError(f"LaserScan echo did not contain frame_id: laser.\n{output}")

    ranges_block = extract_ranges_block(output)
    values = [float(value) for value in re.findall(r"[-+]?(?:\d+\.\d+|\d+)(?:[eE][-+]?\d+)?", ranges_block)]
    finite_values = [value for value in values if math.isfinite(value)]
    if not finite_values:
        raise RuntimeError(f"LaserScan echo did not contain non-empty finite ranges.\n{output}")


def extract_ranges_block(output: str) -> str:
    """Extract the ranges section from a ROS2 LaserScan echo."""

    ranges_index = output.find("ranges:")
    if ranges_index < 0:
        raise RuntimeError(f"LaserScan echo did not contain ranges.\n{output}")

    intensities_index = output.find("intensities:", ranges_index)
    if intensities_index < 0:
        return output[ranges_index:]
    return output[ranges_index:intensities_index]


def launch_rviz(
    ros2_root: pathlib.Path,
    config: pathlib.Path,
    env: dict[str, str],
) -> None:
    """Launch RViz2 with the supplied config."""

    if not config.exists():
        raise FileNotFoundError(f"RViz2 config does not exist: {config}")

    rviz_exe = ros2_root / "bin" / "rviz2.exe"
    if not rviz_exe.exists():
        raise FileNotFoundError(f"rviz2.exe does not exist: {rviz_exe}")

    pixi = ros2_root / ".pixi" / "envs" / "default"
    rviz_path = [
        str(ros2_root / "bin"),
        str(ros2_root / "Scripts"),
        str(pixi),
        str(pixi / "Library" / "bin"),
        str(pixi / "Scripts"),
        str(ros2_root / "opt" / "rviz_ogre_vendor" / "bin"),
        str(ros2_root / "opt" / "gz_math_vendor" / "bin"),
        r"C:\Windows\system32",
        r"C:\Windows",
        r"C:\Windows\System32\Wbem",
        r"C:\Windows\System32\WindowsPowerShell\v1.0",
    ]
    rviz_env = env.copy()
    rviz_env["PATH"] = os.pathsep.join(rviz_path)
    rviz_env["QT_OPENGL"] = "software"
    rviz_env["QT_QUICK_BACKEND"] = "software"
    rviz_env["LIBGL_ALWAYS_SOFTWARE"] = "1"

    process = subprocess.Popen(
        [str(rviz_exe), "-d", str(config)],
        cwd=str(ros2_root),
        env=rviz_env,
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
    )
    print(f"[phase128] Launched RViz2 pid={process.pid} config={config}")


def main(argv: list[str]) -> int:
    """Script entry point."""

    args = parse_args(argv)
    workspace_root = find_workspace_root()
    ros2_root = resolve_existing_path(args.ros2_root, "ROS2 root", workspace_root)
    rviz_config = resolve_existing_path(args.rviz_config, "RViz2 config", workspace_root)
    pixi_python, ros2_script = validate_ros2_root(ros2_root)
    env = build_ros_env(ros2_root, args.rmw, args.discovery_range)

    print(f"[phase128] ROS2 root: {ros2_root}")
    print(f"[phase128] pixi Python: {pixi_python}")
    print(f"[phase128] ros2-script.py: {ros2_script}")
    print(f"[phase128] ROS_DISTRO: {env.get('ROS_DISTRO')}")
    print(f"[phase128] RMW_IMPLEMENTATION: {env.get('RMW_IMPLEMENTATION')}")
    print(f"[phase128] ROS_DOMAIN_ID: {env.get('ROS_DOMAIN_ID')}")
    print(f"[phase128] ROS_AUTOMATIC_DISCOVERY_RANGE: {env.get('ROS_AUTOMATIC_DISCOVERY_RANGE', '<unset>')}")
    print(f"[phase128] RViz2 config: {rviz_config}")

    print("--- node list (diagnostic) ---")
    nodes = probe_node_list(pixi_python, ros2_script, env)
    print(nodes.rstrip() or "<empty>")
    if NODE_NAME not in nodes:
        print(f"[phase128] node list did not include {NODE_NAME}; continuing with publisher endpoint and echo checks.")

    print("--- topic info -v /scan ---")
    scan_info = wait_for_publisher(pixi_python, ros2_script, env, SCAN_TOPIC, args.wait_seconds)
    print(scan_info.rstrip())

    print("--- topic info -v /tf (diagnostic) ---")
    tf_info = probe_topic_info(pixi_python, ros2_script, env, TF_TOPIC)
    print(tf_info.rstrip() or "<empty>")
    if not has_phase128_publisher(tf_info):
        print("[phase128] /tf topic info did not prove the publisher; continuing with /tf echo content check.")

    print("--- echo /tf ---")
    tf_echo = echo_once(pixi_python, ros2_script, env, TF_TOPIC, TF_MSG_TYPE, args.echo_spin_seconds)
    print(tf_echo.rstrip())
    validate_tf_echo(tf_echo)

    print("--- echo /scan ---")
    scan_echo = echo_once(pixi_python, ros2_script, env, SCAN_TOPIC, SCAN_MSG_TYPE, args.echo_spin_seconds)
    print(scan_echo.rstrip())
    validate_scan_echo(scan_echo)

    if args.launch_rviz:
        launch_rviz(ros2_root, rviz_config, env)

    print("[phase128] GREEN: /tf and /scan external ROS2 acceptance checks completed.")
    print("[phase128] Confirm RViz2 displays TF map/base_link/laser and LaserScan /scan before marking manual PASS.")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main(sys.argv[1:]))
    except KeyboardInterrupt:
        raise SystemExit(130)
    except Exception as exc:
        print(f"[phase128] FAIL: {exc}", file=sys.stderr)
        raise SystemExit(1)
