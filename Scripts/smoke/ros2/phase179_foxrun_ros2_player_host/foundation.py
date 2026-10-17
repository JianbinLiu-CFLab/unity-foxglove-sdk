#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Launch one Phase179 Player with an explicit ROS2 environment and collect proof.

Run this helper on Windows before starting the Linux peer helper.  It owns only
the Player it launches and an explicitly supplied Zenoh router executable.  A
Player launch is not a pass: the final verdict also requires its READY marker,
all required copied-value APPLIED markers, a successful COMPLETE marker, and a
zero operating-system exit code.
"""

from __future__ import annotations

import argparse
import json
import os
import pathlib
import re
import signal
import subprocess
import sys
import time
from dataclasses import dataclass
from typing import Mapping, Sequence

import phase179_foxrun_ros2_inbound_acceptance as inbound
import phase179_zenoh_topology as zenoh_topology

from pathlib import Path as _Phase192Path
_PHASE192_FACADE_FILE = _Phase192Path(__file__).resolve().parents[1] / "phase179_foxrun_ros2_player_host.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _Phase192Path, _PHASE192_FACADE_FILE

SUPPORTED_DISTROS = inbound.SUPPORTED_DISTROS
SUPPORTED_RMWS = inbound.SUPPORTED_RMWS
REQUIRED_MESSAGE_NAMES = ("string", "twist", "joy")
READY_MARKER = "PHASE179_ROS2_INBOUND_READY"
COMPLETE_MARKER = "PHASE179_ROS2_INBOUND_COMPLETE"


class PlayerHostFailure(RuntimeError):
    """A stable Player host failure category without raw process diagnostics."""

    def __init__(self, category: str, message: str) -> None:
        """Initialize the stable category without embedding it in the message."""
        super().__init__(message)
        self.category = category


@dataclass(frozen=True)
class OwnedProcessResult:
    """Result of a helper-owned Player/router process wait."""

    return_code: int | None
    output: str
    timed_out: bool


def positive_seconds(text: str) -> float:
    """Reuse the same strict timeout parser as the Linux peer helper."""

    return inbound.positive_seconds(text)


def parse_domain_id(text: str) -> int:
    """Reuse the Phase179 ROS domain-id bounds."""

    return inbound.parse_domain_id(text)


def parse_args(argv: Sequence[str] | None = None) -> argparse.Namespace:
    """Parse Windows Player host arguments without opening any ROS runtime files."""

    root = inbound.workspace_root()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--player", type=pathlib.Path, required=True, help="Absolute WindowsStandalone64 executable path.")
    parser.add_argument("--distro", choices=SUPPORTED_DISTROS, required=True)
    parser.add_argument("--rmw", choices=SUPPORTED_RMWS, required=True)
    parser.add_argument("--domain-id", type=parse_domain_id, default=0)
    parser.add_argument("--discovery-range", default="SUBNET")
    parser.add_argument("--token", type=inbound.parse_token, required=True)
    parser.add_argument("--profile-id", type=inbound.parse_token, default=None)
    parser.add_argument("--surface", choices=("editor", "player"), default=None)
    parser.add_argument("--message-set", type=inbound.parse_message_set, default=REQUIRED_MESSAGE_NAMES)
    parser.add_argument("--topic-prefix", type=inbound.parse_topic_prefix, default="/foxrun/phase179")
    parser.add_argument("--zenoh-topology-id", type=inbound.parse_token, default=None)
    parser.add_argument(
        "--string-burst-final-sequence",
        type=inbound.nonnegative_sequence,
        default=None,
        help="Optional inclusive final String burst sequence shared with the Linux peer helper.",
    )
    parser.add_argument("--player-log", type=pathlib.Path, required=True)
    parser.add_argument("--ready-timeout-seconds", type=positive_seconds, default=45.0)
    parser.add_argument("--exit-timeout-seconds", type=positive_seconds, default=120.0)
    parser.add_argument(
        "--zenoh-router",
        type=pathlib.Path,
        default=None,
        help="Owned Zenoh router executable, or a session JSON/JSON5 configuration for an external certified router.",
    )
    parser.add_argument("--no-zenoh-router", action="store_true", help="Use an externally managed certified Zenoh topology.")
    parser.add_argument(
        "--zenoh-router-ready-marker",
        type=inbound.parse_ready_marker,
        default="Started",
        help="Router log marker required before an owned Zenoh router may be used. Default: Started.",
    )
    parser.add_argument(
        "--summary-json",
        type=pathlib.Path,
        default=root / "build" / "phase179" / "windows-player-host-summary.json",
    )
    args = parser.parse_args(argv)
    if args.zenoh_router is not None and args.no_zenoh_router:
        parser.error("--zenoh-router and --no-zenoh-router are mutually exclusive")
    if (args.profile_id is None) != (args.surface is None):
        parser.error("--profile-id and --surface must be provided together")
    if args.profile_id is not None and args.surface != "player":
        parser.error("Phase179 Player host profile evidence requires --surface player")
    if tuple(args.message_set) != REQUIRED_MESSAGE_NAMES:
        parser.error("Phase179 Player host requires --message-set string,twist,joy")
    if args.topic_prefix != "/foxrun/phase179":
        parser.error("Phase179 Player host requires --topic-prefix /foxrun/phase179")
    if args.rmw != "rmw_zenoh_cpp" and args.zenoh_topology_id is not None:
        parser.error("--zenoh-topology-id is valid only with --rmw rmw_zenoh_cpp")
    if args.rmw == "rmw_zenoh_cpp" and args.distro != "lyrical":
        parser.error("rmw_zenoh_cpp is certified only with --distro lyrical in Phase179")
    return args


def build_player_environment(args: argparse.Namespace, source: Mapping[str, str] | None = None) -> dict[str, str]:
    """Build the exact ROS2 selection passed to the Player before first R2FU Ok()."""

    env = dict(os.environ if source is None else source)
    env["ROS_VERSION"] = "2"
    env["ROS_PYTHON_VERSION"] = "3"
    env["ROS_DISTRO"] = args.distro
    env["RMW_IMPLEMENTATION"] = args.rmw
    env["ROS_DOMAIN_ID"] = str(args.domain_id)
    env["ROS_AUTOMATIC_DISCOVERY_RANGE"] = args.discovery_range
    env.pop("ROS_LOCALHOST_ONLY", None)
    env.pop("ROS_DISCOVERY_SERVER", None)
    zenoh_router = getattr(args, "zenoh_router", None)
    if args.rmw == "rmw_zenoh_cpp" and zenoh_router is not None:
        config = pathlib.Path(zenoh_router)
        if config.suffix.lower() in {".json", ".json5", ".yaml", ".yml"}:
            env["ZENOH_SESSION_CONFIG_URI"] = str(config)
    return env


def build_player_command(
    player: pathlib.Path,
    player_log: pathlib.Path,
    token: str,
    string_burst_final_sequence: int | None = None,
) -> list[str]:
    """Build a direct Unity Player argv; no command shell is involved."""

    command = [
        str(player),
        "-batchmode",
        "-nographics",
        "-logFile",
        str(player_log),
        "--phase179-player-auto-quit",
        "--phase179-token",
        token,
    ]
    if string_burst_final_sequence is not None:
        command.extend(
            [
                "--phase179-player-burst-final-sequence",
                str(string_burst_final_sequence),
            ]
        )
    return command


def terminate_owned_process(process: subprocess.Popen[str]) -> None:
    """Terminate exactly a process launched by this helper and its child tree."""

    if process.poll() is not None:
        return
    if os.name == "nt":
        subprocess.run(
            ["taskkill", "/PID", str(process.pid), "/T", "/F"],
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            check=False,
        )
        return
    try:
        os.killpg(process.pid, signal.SIGTERM)
    except (OSError, ProcessLookupError):
        try:
            process.terminate()
        except OSError:
            return
    try:
        process.wait(timeout=3.0)
    except subprocess.TimeoutExpired:
        try:
            os.killpg(process.pid, signal.SIGKILL)
        except (OSError, ProcessLookupError):
            try:
                process.kill()
            except OSError:
                return


def launch_owned_process(command: Sequence[str], cwd: pathlib.Path, env: Mapping[str, str]) -> subprocess.Popen[str]:
    """Launch a process with its own environment and process group."""

    if not command:
        raise ValueError("owned process command must not be empty")
    popen_kwargs: dict[str, object] = {
        "cwd": str(cwd),
        "env": dict(env),
        "text": True,
        "stdout": subprocess.PIPE,
        "stderr": subprocess.STDOUT,
    }
    if os.name != "nt":
        popen_kwargs["start_new_session"] = True
    return subprocess.Popen(list(command), **popen_kwargs)


def wait_for_owned_process(process: subprocess.Popen[str], timeout_seconds: float) -> OwnedProcessResult:
    """Wait for one owned process and terminate it if its bounded deadline expires."""

    try:
        output, _ = process.communicate(timeout=timeout_seconds)
        return OwnedProcessResult(process.returncode, output or "", False)
    except subprocess.TimeoutExpired:
        terminate_owned_process(process)
        try:
            output, _ = process.communicate(timeout=5.0)
        except subprocess.TimeoutExpired:
            output = ""
        return OwnedProcessResult(process.returncode, output or "", True)
    except KeyboardInterrupt:
        terminate_owned_process(process)
        raise


def run_owned_process(
    command: Sequence[str],
    cwd: pathlib.Path,
    env: Mapping[str, str],
    timeout_seconds: float,
) -> OwnedProcessResult:
    """Launch and synchronously wait for a helper-owned process (testable primitive)."""

    return wait_for_owned_process(launch_owned_process(command, cwd, env), timeout_seconds)

__all__ = ['argparse', 'json', 'os', 'pathlib', 're', 'signal', 'subprocess', 'sys', 'time', 'dataclass', 'Mapping', 'Sequence', 'inbound', 'zenoh_topology', 'SUPPORTED_DISTROS', 'SUPPORTED_RMWS', 'REQUIRED_MESSAGE_NAMES', 'READY_MARKER', 'COMPLETE_MARKER', 'PlayerHostFailure', 'OwnedProcessResult', 'positive_seconds', 'parse_domain_id', 'parse_args', 'build_player_environment', 'build_player_command', 'terminate_owned_process', 'launch_owned_process', 'wait_for_owned_process', 'run_owned_process']
