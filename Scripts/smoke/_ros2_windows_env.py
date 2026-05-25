#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Module: Scripts/smoke
# Purpose: Shared Windows ROS2 Jazzy helper utilities for smoke acceptance scripts.

"""Shared Windows ROS2 Jazzy helper utilities for smoke acceptance scripts."""

from __future__ import annotations

import os
import pathlib
import re
import subprocess
import time
from datetime import datetime


DEFAULT_ROS2_ROOT = pathlib.Path(r"C:\ros2_jazzy\ros2-windows")


def timestamp() -> str:
    """Return a local wall-clock timestamp for acceptance diagnostics."""

    return datetime.now().astimezone().isoformat(timespec="milliseconds")


def log_event(log_prefix: str, message: str) -> None:
    """Print a timestamped acceptance diagnostic line."""

    print(f"[{timestamp()}] [{log_prefix}] {message}", flush=True)


def find_workspace_root() -> pathlib.Path:
    """Find the repository root from either cwd or this module location."""

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
    domain_id: str | None = None,
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
    env["ROS_DOMAIN_ID"] = str(domain_id) if domain_id is not None else "0"
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


def topic_info_has_publisher(topic_info: str, node_name: str | None = None) -> bool:
    """Return whether verbose topic info contains a publisher, optionally from a node."""

    publisher_match = re.search(r"Publisher count:\s*([1-9][0-9]*)", topic_info)
    if not publisher_match:
        return False
    if node_name is None:
        return True
    return f"Node name: {node_name}" in topic_info


def wait_for_publisher(
    pixi_python: pathlib.Path,
    ros2_script: pathlib.Path,
    env: dict[str, str],
    topic: str,
    timeout_seconds: float,
    expected_type: str | None = None,
    node_name: str | None = None,
    poll_interval_seconds: float = 1.0,
) -> str:
    """Wait until a topic has a publisher endpoint, optionally from a node."""

    deadline = time.monotonic() + timeout_seconds
    last_output = ""
    while True:
        remaining = deadline - time.monotonic()
        if remaining <= 0.0:
            break

        probe_timeout_seconds = max(0.5, min(5.0, remaining))
        try:
            result = run_ros2(
                pixi_python,
                ros2_script,
                env,
                ["topic", "info", "-v", topic, "--no-daemon"],
                check=False,
                timeout_seconds=probe_timeout_seconds,
            )
            last_output = result.stdout
        except subprocess.TimeoutExpired:
            last_output = f"<topic info {topic} timed out after {probe_timeout_seconds:.1f}s>"

        type_ok = expected_type is None or expected_type in last_output
        if type_ok and topic_info_has_publisher(last_output, node_name):
            return last_output

        remaining = deadline - time.monotonic()
        if remaining > 0.0:
            time.sleep(min(poll_interval_seconds, remaining))

    try:
        topic_list = run_ros2(
            pixi_python,
            ros2_script,
            env,
            ["topic", "list", "-t", "--no-daemon"],
            check=False,
            timeout_seconds=5.0,
        ).stdout
    except subprocess.TimeoutExpired:
        topic_list = "<topic list timed out after 5.0s>"
    node_text = "" if node_name is None else f" from {node_name}"
    raise TimeoutError(
        f"Timed out waiting for publisher{node_text} on {topic}.\n"
        f"Current topic list:\n{topic_list}\nLast topic info:\n{last_output}"
    )


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


def visible_windows_for_pid(pid: int) -> list[str]:
    """Return visible top-level Windows titles owned by a process id."""

    if os.name != "nt":
        return []

    import ctypes
    from ctypes import wintypes

    user32 = ctypes.windll.user32
    titles: list[str] = []

    enum_windows_proc = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)

    def callback(hwnd: wintypes.HWND, _lparam: wintypes.LPARAM) -> bool:
        """Collect visible window titles owned by the target process."""
        if not user32.IsWindowVisible(hwnd):
            return True

        process_id = wintypes.DWORD()
        user32.GetWindowThreadProcessId(hwnd, ctypes.byref(process_id))
        if process_id.value != pid:
            return True

        length = user32.GetWindowTextLengthW(hwnd)
        buffer = ctypes.create_unicode_buffer(length + 1)
        user32.GetWindowTextW(hwnd, buffer, length + 1)
        titles.append(buffer.value or "<untitled>")
        return True

    user32.EnumWindows(enum_windows_proc(callback), 0)
    return titles


def launch_rviz(
    ros2_root: pathlib.Path,
    config: pathlib.Path,
    env: dict[str, str],
    log_prefix: str,
    startup_check_seconds: float = 1.5,
    window_wait_seconds: float = 0.0,
) -> subprocess.Popen:
    """Launch RViz2 with the supplied config and optional visible-window timing."""

    launch_started = time.perf_counter()

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

    log_event(log_prefix, f"RViz2 launch request exe={rviz_exe} config={config}")
    popen_started = time.perf_counter()
    process = subprocess.Popen(
        [str(rviz_exe), "-d", str(config)],
        cwd=str(ros2_root),
        env=rviz_env,
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
    )
    popen_elapsed = time.perf_counter() - popen_started
    total_elapsed = time.perf_counter() - launch_started
    log_event(
        log_prefix,
        f"RViz2 Popen returned pid={process.pid} popen_elapsed={popen_elapsed:.3f}s total_elapsed={total_elapsed:.3f}s",
    )

    if startup_check_seconds > 0.0:
        check_deadline = time.perf_counter() + startup_check_seconds
        exit_code = None
        while time.perf_counter() < check_deadline:
            exit_code = process.poll()
            if exit_code is not None:
                break
            time.sleep(0.1)
        if exit_code is not None:
            raise RuntimeError(
                f"RViz2 exited immediately with code {exit_code}; "
                f"check the RViz2 config and DLL search path. config={config}"
            )
        log_event(
            log_prefix,
            f"RViz2 process alive after startup_check={startup_check_seconds:.1f}s elapsed={time.perf_counter() - launch_started:.3f}s",
        )

    if window_wait_seconds > 0.0:
        log_event(log_prefix, f"Waiting up to {window_wait_seconds:.1f}s for visible RViz2 window.")
        window_deadline = time.perf_counter() + window_wait_seconds
        while time.perf_counter() < window_deadline:
            exit_code = process.poll()
            if exit_code is not None:
                raise RuntimeError(
                    f"RViz2 exited with code {exit_code} before a visible window appeared; "
                    f"elapsed={time.perf_counter() - launch_started:.3f}s config={config}"
                )

            titles = visible_windows_for_pid(process.pid)
            if titles:
                log_event(
                    log_prefix,
                    f"RViz2 visible window detected elapsed={time.perf_counter() - launch_started:.3f}s title={titles[0]}",
                )
                break

            time.sleep(0.25)
        else:
            log_event(
                log_prefix,
                f"RViz2 visible window not detected within {window_wait_seconds:.1f}s; "
                f"process_alive={process.poll() is None} elapsed={time.perf_counter() - launch_started:.3f}s",
            )

    log_event(log_prefix, f"Launched RViz2 pid={process.pid} config={config}")
    return process
