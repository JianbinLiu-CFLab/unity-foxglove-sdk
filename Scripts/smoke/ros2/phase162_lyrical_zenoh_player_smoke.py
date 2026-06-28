#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Module: Scripts/smoke
# Purpose: Phase162 Lyrical Zenoh RViz2 acceptance helper.

"""Run Lyrical Zenoh RViz2 PointCloud2 acceptance against Unity/R2FU.

Start Unity Play Mode first with ROS2 Native (R2FU), Lyrical Win64, Zenoh
communication mode, PointCloud2 Native output, and deskew enabled. Bare runs
launch RViz2 and validate the raw + deskewed PointCloud2 topics. Pass
--echo-only for the older one-frame CLI smoke.
"""

from __future__ import annotations

import argparse
import json
import os
import pathlib
import subprocess
import sys
import time
from datetime import datetime

from _ros2_windows_env import build_ros_env, default_ros2_root, validate_ros2_root
import phase138u_lidar_deskew_rviz2_acceptance as phase138u


DEFAULT_RMW = "rmw_zenoh_cpp"
DEFAULT_TOPIC = "/unity/point_cloud2"
DEFAULT_DESKEWED_TOPIC = "/unity/point_cloud2_deskewed"
DEFAULT_MESSAGE_TYPE = "sensor_msgs/msg/PointCloud2"
DEFAULT_FIXED_FRAME = "map"
DEFAULT_EXPECTED_FRAME_ID = "os_lidar"
DEFAULT_ROUTER_READY_MARKER = "Started"


def workspace_root() -> pathlib.Path:
    """Return the repository root."""

    return phase138u.ros2env.find_workspace_root()


def parse_args(argv: list[str] | None = None) -> argparse.Namespace:
    """Parse command-line arguments."""

    root = workspace_root()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--ros2-root", type=pathlib.Path, default=default_ros2_root("lyrical", root))
    parser.add_argument("--rmw-implementation", default=DEFAULT_RMW)
    parser.add_argument("--domain-id", default="0")
    parser.add_argument("--topic", default=DEFAULT_TOPIC)
    parser.add_argument("--deskewed-topic", default=DEFAULT_DESKEWED_TOPIC)
    parser.add_argument("--message-type", default=DEFAULT_MESSAGE_TYPE)
    parser.add_argument("--expected-frame-id", default=DEFAULT_EXPECTED_FRAME_ID)
    parser.add_argument("--fixed-frame", default=DEFAULT_FIXED_FRAME)
    parser.add_argument("--expected-text", default="")
    parser.add_argument("--timeout-seconds", type=float, default=60.0)
    parser.add_argument("--spin-seconds", type=float, default=12.0)
    parser.add_argument(
        "--discovery-range",
        choices=("LOCALHOST", "SUBNET", "OFF", "SYSTEM_DEFAULT"),
        default="SUBNET",
    )
    parser.add_argument("--zenoh-router", type=pathlib.Path, default=None)
    parser.add_argument(
        "--no-zenoh-router",
        action="store_true",
        help="Do not auto-start rmw_zenohd when using rmw_zenoh_cpp.",
    )
    parser.add_argument("--router-ready-marker", default=DEFAULT_ROUTER_READY_MARKER)
    parser.add_argument("--unity-player", type=pathlib.Path, default=None)
    parser.add_argument("--player-log", type=pathlib.Path, default=None)
    parser.add_argument("--player-ready-marker", default="")
    parser.add_argument("--scripting-backend", choices=("mono", "il2cpp"), default="mono")
    parser.add_argument(
        "--echo-only",
        action="store_true",
        help="Run only the legacy one-frame ros2 topic echo smoke without launching RViz2.",
    )
    parser.add_argument("--no-rviz", dest="launch_rviz", action="store_false")
    parser.add_argument("--skip-topic-probe", action="store_true")
    parser.add_argument("--rviz-display-mode", choices=("both", "raw"), default="both")
    parser.add_argument(
        "--require-motion",
        action="store_true",
        help="Require raw-vs-deskewed motion evidence instead of accepting static DDS wiring.",
    )
    parser.add_argument(
        "--echo-output",
        type=pathlib.Path,
        default=root / "build" / "phase162-lyrical-zenoh-smoke" / "ros2-topic-echo.log",
    )
    parser.add_argument(
        "--summary-output",
        type=pathlib.Path,
        default=root / "build" / "phase162-lyrical-zenoh-smoke" / "summary.json",
    )
    parser.set_defaults(launch_rviz=True)
    return parser.parse_args(argv)


def default_zenoh_router(ros2_root: pathlib.Path) -> pathlib.Path | None:
    """Return the rmw_zenohd executable bundled with a Windows ROS2 root, if present."""

    candidate = ros2_root / "Lib" / "rmw_zenoh_cpp" / "rmw_zenohd.exe"
    if candidate.is_file():
        return candidate
    return None


def kill_process_tree(pid: int) -> None:
    """Kill a Windows process tree, falling back to process.kill elsewhere."""

    if os.name == "nt":
        subprocess.run(
            ["taskkill", "/PID", str(pid), "/T", "/F"],
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            check=False,
        )
        return

    try:
        os.kill(pid, 9)
    except OSError:
        pass


def wait_for_marker(path: pathlib.Path, marker: str, timeout_seconds: float) -> bool:
    """Wait for a marker in a growing log file."""

    if not marker:
        return True

    deadline = time.monotonic() + timeout_seconds
    while time.monotonic() < deadline:
        if path.exists() and marker in path.read_text(encoding="utf-8", errors="replace"):
            return True
        time.sleep(0.25)
    return False


def launch_to_log(command: list[str], cwd: pathlib.Path, env: dict[str, str], log_path: pathlib.Path) -> subprocess.Popen:
    """Launch a subprocess with stdout/stderr redirected to a UTF-8 log."""

    log_path.parent.mkdir(parents=True, exist_ok=True)
    log = log_path.open("w", encoding="utf-8", errors="replace")
    log.write("cmd: " + " ".join(command) + "\n")
    log.write(f"cwd: {cwd}\n\n")
    log.flush()
    return subprocess.Popen(
        command,
        cwd=str(cwd),
        env=env,
        text=True,
        stdout=log,
        stderr=subprocess.STDOUT,
    )


def run_bounded_to_file(
    command: list[str],
    cwd: pathlib.Path,
    env: dict[str, str],
    log_path: pathlib.Path,
    timeout_seconds: float,
) -> int:
    """Run a subprocess to a file and kill its process tree on timeout."""

    process = launch_to_log(command, cwd, env, log_path)
    try:
        process.wait(timeout=timeout_seconds)
    except subprocess.TimeoutExpired:
        kill_process_tree(process.pid)
        try:
            process.wait(timeout=10)
        except subprocess.TimeoutExpired:
            pass

    with log_path.open("a", encoding="utf-8", errors="replace") as log:
        log.write(f"\nexitCode: {process.returncode}\n")
    return int(process.returncode or 0)


def build_phase162_env(args: argparse.Namespace) -> tuple[dict[str, str], pathlib.Path, pathlib.Path]:
    """Build a Lyrical ROS2 CLI environment with explicit RMW selection."""

    ros2_root = args.ros2_root.resolve()
    pixi_python, ros2_script = validate_ros2_root(ros2_root)
    env = build_ros_env(
        ros2_root,
        rmw_implementation=args.rmw_implementation,
        domain_id=args.domain_id,
        ros_distro="lyrical",
    )
    if args.rmw_implementation == "rmw_zenoh_cpp":
        env.setdefault("ZENOH_ROUTER_CHECK_ATTEMPTS", "10")
    return env, pixi_python, ros2_script


def build_echo_command(pixi_python: pathlib.Path, ros2_script: pathlib.Path, args: argparse.Namespace) -> list[str]:
    """Build the bounded ros2 topic echo command used by the smoke helper."""

    command = [
        str(pixi_python),
        str(ros2_script),
        "topic",
        "echo",
        "--once",
        args.topic,
        args.message_type,
        "--no-daemon",
        "--spin-time",
        str(args.spin_seconds),
    ]
    if args.message_type == "sensor_msgs/msg/PointCloud2":
        command.extend(
            [
                "--qos-reliability",
                "best_effort",
                "--qos-history",
                "keep_last",
                "--qos-depth",
                "1",
            ]
        )
    return command


def build_rviz_acceptance_args(args: argparse.Namespace) -> list[str]:
    """Build the Phase138U RViz2 acceptance argv for Lyrical Zenoh."""

    rviz_args = [
        "--ros2-root",
        str(args.ros2_root),
        "--raw-topic",
        args.topic,
        "--deskewed-topic",
        args.deskewed_topic,
        "--expected-frame-id",
        args.expected_frame_id,
        "--fixed-frame",
        args.fixed_frame,
        "--spin-seconds",
        str(args.spin_seconds),
        "--rmw",
        args.rmw_implementation,
        "--domain-id",
        str(args.domain_id),
        "--discovery-range",
        args.discovery_range,
        "--rviz-display-mode",
        args.rviz_display_mode,
    ]
    if not args.launch_rviz:
        rviz_args.append("--no-rviz")
    if args.skip_topic_probe:
        rviz_args.append("--skip-topic-probe")
    if not args.require_motion:
        rviz_args.append("--allow-static")
    return rviz_args


def write_summary(args: argparse.Namespace, summary: dict[str, object]) -> None:
    """Write the JSON summary and print the operator-facing result."""

    args.summary_output.parent.mkdir(parents=True, exist_ok=True)
    args.summary_output.write_text(json.dumps(summary, indent=2), encoding="utf-8")
    print(f"Summary: {args.summary_output}")
    print(f"Verdict: {summary['verdict']}")
    if "error" in summary:
        print(f"Error: {summary['error']}", file=sys.stderr)


def main(argv: list[str] | None = None) -> int:
    """Run the Phase162 smoke helper."""

    args = parse_args(argv)
    root = workspace_root()
    env, pixi_python, ros2_script = build_phase162_env(args)
    args.echo_output.parent.mkdir(parents=True, exist_ok=True)
    args.summary_output.parent.mkdir(parents=True, exist_ok=True)

    run_id = datetime.now().astimezone().isoformat(timespec="seconds")
    router_log = args.echo_output.with_name("zenoh-router.log")
    player_log = args.player_log or args.echo_output.with_name("unity-player.log")
    router = None
    player = None
    summary: dict[str, object] = {
        "runId": run_id,
        "rosDistro": "lyrical",
        "rmwImplementation": args.rmw_implementation,
        "scriptingBackend": args.scripting_backend,
        "topic": args.topic,
        "messageType": args.message_type,
        "ros2Root": str(args.ros2_root),
        "echoOutput": str(args.echo_output),
        "summaryOutput": str(args.summary_output),
        "zenohRouterLog": str(router_log),
        "playerLog": str(player_log),
        "mode": "echo-only" if args.echo_only else "rviz2-pointcloud2",
        "exitCodes": {},
    }

    try:
        router_exe = args.zenoh_router
        if args.rmw_implementation == "rmw_zenoh_cpp" and router_exe is None and not args.no_zenoh_router:
            router_exe = default_zenoh_router(args.ros2_root.resolve())
            if router_exe is not None:
                summary["autoZenohRouter"] = str(router_exe)

        if args.rmw_implementation == "rmw_zenoh_cpp" and router_exe is not None:
            router_exe = router_exe.resolve()
            if not router_exe.is_file():
                raise FileNotFoundError(f"Zenoh router executable not found: {router_exe}")
            router = launch_to_log([str(router_exe)], root, env, router_log)
            if not wait_for_marker(router_log, args.router_ready_marker, args.timeout_seconds):
                raise TimeoutError(
                    f"Zenoh router did not emit marker {args.router_ready_marker!r}. See {router_log}"
                )

        if args.unity_player is not None:
            player_exe = args.unity_player.resolve()
            if not player_exe.is_file():
                raise FileNotFoundError(f"Unity player executable not found: {player_exe}")
            player = launch_to_log(
                [str(player_exe), "-batchmode", "-nographics", "-logFile", str(player_log)],
                player_exe.parent,
                env,
                player_log,
            )
            if not wait_for_marker(player_log, args.player_ready_marker, args.timeout_seconds):
                raise TimeoutError(
                    f"Unity player did not emit marker {args.player_ready_marker!r}. See {player_log}"
                )

        if args.echo_only:
            echo_command = build_echo_command(pixi_python, ros2_script, args)
            echo_code = run_bounded_to_file(echo_command, root, env, args.echo_output, args.timeout_seconds)
            summary["exitCodes"]["ros2TopicEcho"] = echo_code
            echo_text = args.echo_output.read_text(encoding="utf-8", errors="replace")
            if echo_code != 0:
                raise RuntimeError(f"ros2 topic echo failed with exit code {echo_code}. See {args.echo_output}")
            if args.expected_text and args.expected_text not in echo_text:
                raise RuntimeError(f"Expected text {args.expected_text!r} was not found in {args.echo_output}")

            summary["verdict"] = "PHASE162_LYRICAL_ZENOH_EXTERNAL_ECHO_PASS"
            return_code = 0
        else:
            rviz_args = build_rviz_acceptance_args(args)
            summary["rvizAcceptanceArgs"] = rviz_args
            try:
                rviz_code = phase138u.main(rviz_args)
            except phase138u.InconclusiveError as exc:
                summary["exitCodes"]["phase138uRviz2Acceptance"] = 2
                summary["verdict"] = "PHASE162_LYRICAL_ZENOH_RVIZ2_POINTCLOUD2_INCONCLUSIVE"
                summary["error"] = str(exc)
                return_code = 2
            else:
                summary["exitCodes"]["phase138uRviz2Acceptance"] = rviz_code
                if rviz_code != 0:
                    raise RuntimeError(f"Phase162 Lyrical Zenoh RViz2 acceptance failed with exit code {rviz_code}.")
                summary["verdict"] = "PHASE162_LYRICAL_ZENOH_RVIZ2_POINTCLOUD2_PASS"
                return_code = 0
    except Exception as exc:
        summary["verdict"] = (
            "PHASE162_LYRICAL_ZENOH_EXTERNAL_ECHO_FAIL"
            if args.echo_only
            else "PHASE162_LYRICAL_ZENOH_RVIZ2_POINTCLOUD2_FAIL"
        )
        summary["error"] = str(exc)
        return_code = 1
    finally:
        for label, process in (("unityPlayer", player), ("zenohRouter", router)):
            if process is None:
                continue
            if process.poll() is None:
                kill_process_tree(process.pid)
                try:
                    process.wait(timeout=10)
                except subprocess.TimeoutExpired:
                    pass
            summary["exitCodes"][label] = process.returncode
        write_summary(args, summary)
    return return_code


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
