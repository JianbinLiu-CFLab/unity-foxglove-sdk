#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Module: Scripts/smoke
# Purpose: Linux/WSL ROS2 peer acceptance for Phase179 FoxRun native subscriptions.

"""Publish bounded Phase179 ROS2 probes and require independently observable Unity proof.

The caller must source the requested ROS2 distribution before starting this
helper.  This script deliberately never sources a ROS installation or replaces
the selected RMW.  A successful Linux publish without a matching Unity marker
is reported as ``PEER_PUBLISH_COMPLETE_UNITY_PROOF_PENDING``, never PASS.
"""
from __future__ import annotations
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase179_foxrun_ros2_inbound_acceptance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
import argparse
import json
import math
import os
import pathlib
import re
import shutil
import signal
import subprocess
import sys
import time
import uuid
from dataclasses import dataclass
from typing import Callable, Mapping, Sequence
import phase179_zenoh_topology as zenoh_topology
SUPPORTED_DISTROS = ("humble", "jazzy", "lyrical")
SUPPORTED_RMWS = ("rmw_fastrtps_cpp", "rmw_zenoh_cpp")
SUPPORTED_MESSAGE_NAMES = ("string", "twist", "joy", "imu")
SUPPORTED_NEGATIVE_CASES = ("type-mismatch", "rmw-mismatch", "qos-incompatible")
UNITY_APPLIED_MARKER = "PHASE179_ROS2_INBOUND_APPLIED"
UNITY_READY_MARKER = "PHASE179_ROS2_INBOUND_READY"
_SENSITIVE_KEY_PARTS = ("password", "secret", "credential", "zenohrouterpath", "zenohconfig")
_SENSITIVE_VALUE_RE = re.compile(r"(?i)(?:password|secret|credential|token)=([^\s;&]+)")
_TOKEN_RE = re.compile(r"^[A-Za-z0-9][A-Za-z0-9._:-]{0,95}$")
class AcceptanceFailure(RuntimeError):
    """A stable, non-secret acceptance failure category."""

    def __init__(self, category: str, message: str) -> None:
        """Initialize the stable category without embedding it in the message."""
        super().__init__(message)
        self.category = category
@dataclass(frozen=True)
class MessageSpec:
    """One native ROS2 input contract exercised by the helper."""

    name: str
    topic_suffix: str
    message_type: str
    qos_reliability: str
    qos_history: str
    qos_depth: int
    qos_durability: str
    publish_payload: Callable[[str], dict[str, object]]
    expected_value: Callable[[str], dict[str, object]]
@dataclass(frozen=True)
class CommandResult:
    """Bounded subprocess outcome without exposing command output in summaries."""

    command: tuple[str, ...]
    return_code: int | None
    output: str
    timed_out: bool
@dataclass(frozen=True)
class UnityMarker:
    """A bounded, copied-value Unity acceptance marker."""

    session: int
    topic: str
    token: str
    received: int
    applied: int
    replaced: int
    value: dict[str, object] | None
@dataclass(frozen=True)
class UnityReadyMarker:
    """One bounded Unity native-runtime identity marker."""

    runtime: str
    rmw: str
    token: str
@dataclass(frozen=True)
class EndpointEvidence:
    """Portable graph facts validated from verbose ROS2 topic info."""

    message_type: str
    subscription_count: int
    qos_reliability: str
    qos_history: str
    qos_depth: int
    qos_durability: str
def _twist_publish_payload(_token: str) -> dict[str, object]:
    """Return the deterministic Twist payload used by the Linux publisher."""
    return {
        "linear": {"x": 1.25, "y": -0.25, "z": 0.0},
        "angular": {"x": 0.0, "y": 0.0, "z": -0.5},
    }
def _twist_expected_value(_token: str) -> dict[str, object]:
    """Return the bounded Unity value proof expected after a Twist apply."""
    return {
        "type": "Twist",
        "linear": {"x": 1.25, "y": -0.25},
        "angular": {"z": -0.5},
    }
def _joy_publish_payload(token: str) -> dict[str, object]:
    """Return the deterministic Joy payload with its correlation token in frame_id."""
    return {
        "header": {"frame_id": token},
        "axes": [0.125, -0.5, 1.0],
        "buttons": [1, 0, 1],
    }
def _joy_expected_value(token: str) -> dict[str, object]:
    """Return the managed Joy fields expected from the Unity sample."""
    return {
        "type": "Joy",
        "frameId": token,
        "axes": [0.125, -0.5, 1.0],
        "buttons": [1, 0, 1],
    }
def _imu_publish_payload(token: str) -> dict[str, object]:
    """Return the deterministic Imu payload with its correlation token in frame_id."""
    return {
        "header": {"frame_id": token},
        "orientation": {"x": 0.1, "y": -0.2, "z": 0.3, "w": 0.9},
        "angular_velocity": {"x": 0.4, "y": -0.5, "z": 0.6},
        "linear_acceleration": {"x": 1.1, "y": 1.2, "z": 1.3},
    }
def _imu_expected_value(token: str) -> dict[str, object]:
    """Return the bounded Imu fields expected from the Unity sample."""
    return {
        "type": "Imu",
        "frameId": token,
        "orientation": {"x": 0.1, "y": -0.2, "z": 0.3, "w": 0.9},
        "angularVelocity": {"x": 0.4, "y": -0.5, "z": 0.6},
        "linearAcceleration": {"x": 1.1, "y": 1.2, "z": 1.3},
    }
MESSAGE_SPECS: dict[str, MessageSpec] = {
    "string": MessageSpec(
        "string",
        "string",
        "std_msgs/msg/String",
        "reliable",
        "keep_last",
        10,
        "volatile",
        lambda token: {"data": token},
        lambda token: {"type": "String", "data": token},
    ),
    "twist": MessageSpec(
        "twist",
        "twist",
        "geometry_msgs/msg/Twist",
        "reliable",
        "keep_last",
        10,
        "volatile",
        _twist_publish_payload,
        _twist_expected_value,
    ),
    "joy": MessageSpec(
        "joy",
        "joy",
        "sensor_msgs/msg/Joy",
        "best_effort",
        "keep_last",
        5,
        "volatile",
        _joy_publish_payload,
        _joy_expected_value,
    ),
    "imu": MessageSpec(
        "imu",
        "imu",
        "sensor_msgs/msg/Imu",
        "best_effort",
        "keep_last",
        5,
        "volatile",
        _imu_publish_payload,
        _imu_expected_value,
    ),
}
def workspace_root() -> pathlib.Path:
    """Return the repository root without traversing local runtime junctions."""

    for candidate in (pathlib.Path(__file__).resolve().parent, *pathlib.Path(__file__).resolve().parents):
        if (candidate / "Packages").is_dir() and (candidate / "Scripts").is_dir():
            return candidate
    return pathlib.Path.cwd()
def parse_message_set(text: str) -> tuple[str, ...]:
    """Validate an ordered, unique set of native input message names."""

    names = tuple(part.strip().lower() for part in text.split(",") if part.strip())
    if not names:
        raise ValueError("--message-set must name at least one supported message type")
    unknown = [name for name in names if name not in MESSAGE_SPECS]
    if unknown:
        raise ValueError("unsupported message type(s): " + ", ".join(unknown))
    if len(set(names)) != len(names):
        raise ValueError("--message-set must not repeat a message type")
    if "twist" in names and "string" not in names:
        raise ValueError("--message-set containing twist requires string to establish the shared correlation token first")
    return tuple(name for name in SUPPORTED_MESSAGE_NAMES if name in names)
def parse_domain_id(text: str) -> int:
    """Parse a ROS domain id without silently wrapping invalid values."""

    try:
        value = int(text)
    except ValueError as exc:
        raise argparse.ArgumentTypeError("--domain-id must be an integer") from exc
    if not 0 <= value <= 232:
        raise argparse.ArgumentTypeError("--domain-id must be in the ROS2 range 0..232")
    return value
def positive_seconds(text: str) -> float:
    """Parse a bounded operation timeout."""

    try:
        value = float(text)
    except ValueError as exc:
        raise argparse.ArgumentTypeError("timeout must be a number of seconds") from exc
    if not math.isfinite(value) or value <= 0.0:
        raise argparse.ArgumentTypeError("timeout must be a finite positive number")
    return value
def nonnegative_sequence(text: str) -> int:
    """Parse the inclusive final sequence for a latest-wins String burst."""

    try:
        value = int(text)
    except ValueError as exc:
        raise argparse.ArgumentTypeError("burst final sequence must be an integer") from exc
    if value < 1:
        raise argparse.ArgumentTypeError("burst final sequence must be at least 1 to prove replacement")
    if value > 10_000:
        raise argparse.ArgumentTypeError("burst final sequence must not exceed 10000")
    return value
def parse_topic_prefix(text: str) -> str:
    """Normalize a ROS topic prefix while rejecting ambiguous input."""

    value = text.strip().rstrip("/")
    if not value.startswith("/") or value == "/" or any(char.isspace() for char in value):
        raise argparse.ArgumentTypeError("--topic-prefix must be a non-root absolute ROS topic prefix")
    return value
def parse_token(text: str) -> str:
    """Accept only a bounded token that is unambiguous in a one-line marker."""

    value = text.strip()
    if not _TOKEN_RE.fullmatch(value):
        raise argparse.ArgumentTypeError(
            "--token must start with an alphanumeric character, then use only alphanumeric characters plus . _ : - (max 96 characters)"
        )
    if any(fragment in value.lower() for fragment in _SENSITIVE_KEY_PARTS):
        raise argparse.ArgumentTypeError("--token must not contain a credential-like word")
    return value
def parse_ready_marker(text: str) -> str:
    """Accept a bounded one-line router readiness marker without allowing an always-match value."""

    value = text.strip()
    if not value or len(value) > 128 or "\n" in value or "\r" in value:
        raise argparse.ArgumentTypeError("--zenoh-router-ready-marker must be a non-empty single-line marker (max 128 characters)")
    return value
def parse_args(argv: Sequence[str] | None = None) -> argparse.Namespace:
    """Parse Linux/WSL2 peer arguments without loading ROS Python modules."""

    root = workspace_root()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--distro", choices=SUPPORTED_DISTROS, default="jazzy")
    parser.add_argument("--rmw", choices=SUPPORTED_RMWS, default="rmw_fastrtps_cpp")
    parser.add_argument("--domain-id", type=parse_domain_id, default=0)
    parser.add_argument("--discovery-range", default="SUBNET")
    parser.add_argument("--topic-prefix", type=parse_topic_prefix, default="/foxrun/phase179")
    parser.add_argument("--message-set", type=parse_message_set, default=parse_message_set("string,twist,joy"))
    parser.add_argument("--timeout-seconds", type=positive_seconds, default=45.0)
    parser.add_argument(
        "--string-burst-final-sequence",
        type=nonnegative_sequence,
        default=None,
        help="Optional inclusive final String sequence; publishes token|seq=0..N|total=N+1 to exercise latest-wins replacement.",
    )
    parser.add_argument(
        "--string-burst-rate-hz",
        type=positive_seconds,
        default=500.0,
        help="Bounded rclpy String burst rate when --string-burst-final-sequence is supplied. Default: 500 Hz.",
    )
    parser.add_argument(
        "--negative-case",
        choices=SUPPORTED_NEGATIVE_CASES,
        default=None,
        help="Run one bounded expected-rejection probe instead of positive interoperability acceptance.",
    )
    parser.add_argument(
        "--negative-peer-rmw",
        choices=SUPPORTED_RMWS,
        default=None,
        help="The intentionally incompatible Unity peer RMW for --negative-case rmw-mismatch; never selected locally.",
    )
    parser.add_argument("--unity-log", type=pathlib.Path, default=None)
    parser.add_argument(
        "--unity-ready-token",
        type=parse_token,
        default=None,
        help="Current Unity READY marker token. Required with --unity-log for a full expected-negative verdict.",
    )
    parser.add_argument("--token", type=parse_token, default=None)
    parser.add_argument("--profile-id", type=parse_token, default=None)
    parser.add_argument("--surface", choices=("editor", "player"), default=None)
    parser.add_argument("--zenoh-topology-id", type=parse_token, default=None)
    parser.add_argument(
        "--zenoh-router",
        type=pathlib.Path,
        default=None,
        help="Owned Zenoh router executable, or a session JSON/JSON5 configuration path for a certified topology.",
    )
    parser.add_argument("--no-zenoh-router", action="store_true", help="Use an externally managed certified Zenoh topology.")
    parser.add_argument(
        "--zenoh-router-ready-marker",
        type=parse_ready_marker,
        default="Started",
        help="Router log marker required before an owned Zenoh router may be used. Default: Started.",
    )
    parser.add_argument(
        "--summary-json",
        type=pathlib.Path,
        default=root / "build" / "phase179" / "linux-inbound-summary.json",
    )
    parser.add_argument(
        "--ros2-root",
        type=pathlib.Path,
        default=None,
        help="Optional Windows ROS2 root used only for supplemental peer diagnostics; not a Linux environment source.",
    )
    args = parser.parse_args(argv)
    if args.zenoh_router is not None and args.no_zenoh_router:
        parser.error("--zenoh-router and --no-zenoh-router are mutually exclusive")
    if (args.profile_id is None) != (args.surface is None):
        parser.error("--profile-id and --surface must be provided together")
    if args.rmw != "rmw_zenoh_cpp" and args.zenoh_topology_id is not None:
        parser.error("--zenoh-topology-id is valid only with --rmw rmw_zenoh_cpp")
    if args.unity_ready_token is not None and args.unity_log is None:
        parser.error("--unity-ready-token requires --unity-log")
    if args.rmw == "rmw_zenoh_cpp" and args.distro != "lyrical":
        parser.error("rmw_zenoh_cpp is certified only with --distro lyrical in Phase179")
    if args.string_burst_final_sequence is not None and "string" not in args.message_set:
        parser.error("--string-burst-final-sequence requires string in --message-set")
    if args.negative_case is not None:
        if args.string_burst_final_sequence is not None:
            parser.error("--negative-case cannot be combined with --string-burst-final-sequence")
        if len(args.message_set) != 1:
            parser.error("--negative-case requires exactly one --message-set contract")
        if args.negative_case == "qos-incompatible" and MESSAGE_SPECS[args.message_set[0]].qos_reliability != "reliable":
            parser.error("--negative-case qos-incompatible requires a Reliable String or Twist contract")
        if args.negative_case == "rmw-mismatch":
            if args.negative_peer_rmw is None:
                parser.error("--negative-case rmw-mismatch requires --negative-peer-rmw")
            if args.negative_peer_rmw == args.rmw:
                parser.error("--negative-peer-rmw must differ from --rmw for rmw-mismatch")
        elif args.negative_peer_rmw is not None:
            parser.error("--negative-peer-rmw is valid only with --negative-case rmw-mismatch")
    elif args.negative_peer_rmw is not None:
        parser.error("--negative-peer-rmw requires --negative-case rmw-mismatch")
    return args
def validate_selected_linux_environment(args: argparse.Namespace, env: Mapping[str, str]) -> None:
    """Require the caller's already-sourced ROS environment to match exactly."""

    actual_distro = (env.get("ROS_DISTRO") or "").strip().lower()
    actual_rmw = (env.get("RMW_IMPLEMENTATION") or "").strip()
    if actual_distro != args.distro:
        raise AcceptanceFailure(
            "ENVIRONMENT",
            "ROS_DISTRO does not match --distro; source the selected ROS2 setup before running this helper.",
        )
    if actual_rmw != args.rmw:
        raise AcceptanceFailure(
            "ENVIRONMENT",
            "RMW_IMPLEMENTATION does not match --rmw; this helper never selects a fallback transport.",
        )
    if (env.get("ROS_VERSION") or "2").strip() != "2":
        raise AcceptanceFailure("ENVIRONMENT", "ROS_VERSION must be 2 for Phase179 acceptance.")
def build_linux_environment(args: argparse.Namespace, source: Mapping[str, str] | None = None) -> dict[str, str]:
    """Copy the sourced Linux environment and apply only requested peer settings."""

    selected = dict(os.environ if source is None else source)
    validate_selected_linux_environment(args, selected)
    selected["ROS_DOMAIN_ID"] = str(args.domain_id)
    selected["ROS_AUTOMATIC_DISCOVERY_RANGE"] = args.discovery_range
    selected.pop("ROS_LOCALHOST_ONLY", None)
    selected.pop("ROS_DISCOVERY_SERVER", None)
    return selected
def topic_for_spec(prefix: str, spec: MessageSpec) -> str:
    """Return the public topic for one selected contract."""

    return f"{prefix}/{spec.topic_suffix}"
def _build_publish_command(
    ros2_executable: pathlib.Path,
    topic: str,
    message_type: str,
    qos_reliability: str,
    qos_history: str,
    qos_depth: int,
    qos_durability: str,
    payload: Mapping[str, object],
) -> list[str]:
    """Build one shell-free, bounded ROS2 publication argv from explicit contract facts."""

    return [
        str(ros2_executable),
        "topic",
        "pub",
        "--once",
        "--qos-reliability",
        qos_reliability,
        "--qos-history",
        qos_history,
        "--qos-depth",
        str(qos_depth),
        "--qos-durability",
        qos_durability,
        topic,
        message_type,
        json.dumps(payload, separators=(",", ":"), sort_keys=True),
    ]


__all__ = [name for name in globals() if not name.startswith("__")]
