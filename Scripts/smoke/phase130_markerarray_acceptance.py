#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Module: Scripts/smoke
# Purpose: Manual ROS2 and RViz2 acceptance helper for Phase130 MarkerArray.

"""Validate Phase130 RViz2 MarkerArray standard visualization topics.

Start Unity manually first, import the RViz2 MarkerArray Acceptance sample, add
Phase130Rviz2MarkerArraySmoke to a scene object, and enter Play Mode. This
helper then uses the pinned Windows ROS2 Jazzy Python entry point to check the
external ROS2 graph and, optionally, launch RViz2.
"""

from __future__ import annotations

import argparse
import os
import pathlib
import re
import subprocess
import sys
import time


DEFAULT_ROS2_ROOT = pathlib.Path(r"C:\ros2_jazzy\ros2-windows")
DEFAULT_RVIZ_CONFIG = pathlib.Path(
    r"Packages\dev.unity2foxglove.ros2forunity\Samples~"
    r"\RViz2 MarkerArray Acceptance\rviz2_phase130_markerarray.rviz"
)
NODE_NAME = "unity2foxglove_phase130_markerarray"
MARKERS_TOPIC = "/markers"
MARKERS_MSG_TYPE = "visualization_msgs/msg/MarkerArray"


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
        help="How long to wait for Unity's Phase130 node and /markers publisher.",
    )
    parser.add_argument(
        "--echo-spin-seconds",
        type=float,
        default=20.0,
        help="ROS2 spin time for the bounded marker echo.",
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
            "or the ROS2 default."
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
    expected_type: str | None = None,
) -> str:
    """Wait until a topic has a Phase130 publisher endpoint."""

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
        type_ok = expected_type is None or expected_type in last_output
        if type_ok and has_phase130_publisher(last_output):
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
        f"Timed out waiting for Phase130 publisher on {topic}.\n"
        f"Current topic list:\n{topic_list}\nLast topic info:\n{last_output}"
    )


def has_phase130_publisher(topic_info: str) -> bool:
    """Return whether verbose topic info contains a Phase130 publisher endpoint."""

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


def validate_markerarray_echo(output: str) -> None:
    """Validate MarkerArray echo contains a bounded cube marker or cleanup action."""

    required = ["markers:", "frame_id: map", "ns: unity2foxglove", "id:", "lifetime:"]
    missing = [token for token in required if token not in output]
    if missing:
        raise RuntimeError(f"MarkerArray echo missing required token(s): {', '.join(missing)}\n{output}")

    id_match = re.search(r"\bid:\s*([1-9][0-9]*)", output)
    if not id_match:
        raise RuntimeError(f"MarkerArray echo did not contain a positive deterministic id.\n{output}")

    if "action: 3" in output:
        return

    if "type: 1" not in output:
        raise RuntimeError(f"MarkerArray echo did not contain CUBE marker type 1.\n{output}")
    if "action: 0" not in output and "action: 2" not in output:
        raise RuntimeError(f"MarkerArray echo did not contain ADD or DELETE action.\n{output}")
    if "sec: 0" not in output or "nanosec: 0" not in output:
        raise RuntimeError(f"MarkerArray echo did not contain zero marker lifetime.\n{output}")


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
    print(f"[phase130] Launched RViz2 pid={process.pid} config={config}")


def main(argv: list[str]) -> int:
    """Script entry point."""

    args = parse_args(argv)
    workspace_root = find_workspace_root()
    ros2_root = resolve_existing_path(args.ros2_root, "ROS2 root", workspace_root)
    rviz_config = resolve_existing_path(args.rviz_config, "RViz2 config", workspace_root)
    pixi_python, ros2_script = validate_ros2_root(ros2_root)
    env = build_ros_env(ros2_root, args.rmw, args.discovery_range)

    print(f"[phase130] ROS2 root: {ros2_root}")
    print(f"[phase130] pixi Python: {pixi_python}")
    print(f"[phase130] ros2-script.py: {ros2_script}")
    print(f"[phase130] ROS_DISTRO: {env.get('ROS_DISTRO')}")
    print(f"[phase130] RMW_IMPLEMENTATION: {env.get('RMW_IMPLEMENTATION')}")
    print(f"[phase130] ROS_DOMAIN_ID: {env.get('ROS_DOMAIN_ID')}")
    print(f"[phase130] ROS_AUTOMATIC_DISCOVERY_RANGE: {env.get('ROS_AUTOMATIC_DISCOVERY_RANGE', '<unset>')}")
    print(f"[phase130] RViz2 config: {rviz_config}")

    print("--- node list (diagnostic) ---")
    nodes = probe_node_list(pixi_python, ros2_script, env)
    print(nodes.rstrip() or "<empty>")
    if NODE_NAME not in nodes:
        print(f"[phase130] node list did not include {NODE_NAME}; continuing with publisher endpoint and echo checks.")

    print("--- topic info -v /markers ---")
    markers_info = wait_for_publisher(
        pixi_python,
        ros2_script,
        env,
        MARKERS_TOPIC,
        args.wait_seconds,
        MARKERS_MSG_TYPE,
    )
    print(markers_info.rstrip())

    print("--- echo /markers ---")
    markers_echo = echo_once(
        pixi_python,
        ros2_script,
        env,
        MARKERS_TOPIC,
        MARKERS_MSG_TYPE,
        args.echo_spin_seconds,
    )
    print(markers_echo.rstrip())
    validate_markerarray_echo(markers_echo)

    if args.launch_rviz:
        launch_rviz(ros2_root, rviz_config, env)

    print("[phase130] GREEN: /markers external ROS2 acceptance checks completed.")
    print("[phase130] Confirm RViz2 displays MarkerArray /markers before marking manual PASS.")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main(sys.argv[1:]))
    except KeyboardInterrupt:
        raise SystemExit(130)
    except Exception as exc:
        print(f"[phase130] FAIL: {exc}", file=sys.stderr)
        raise SystemExit(1)
