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


def build_publish_command(ros2_executable: pathlib.Path, spec: MessageSpec, token: str, topic_prefix: str = "/foxrun/phase179") -> list[str]:
    """Build a bounded ``ros2 topic pub --once`` argv with no shell interpolation."""

    return _build_publish_command(
        ros2_executable,
        topic_for_spec(topic_prefix, spec),
        spec.message_type,
        spec.qos_reliability,
        spec.qos_history,
        spec.qos_depth,
        spec.qos_durability,
        spec.publish_payload(token),
    )


def build_negative_publish_command(
    ros2_executable: pathlib.Path,
    spec: MessageSpec,
    token: str,
    negative_case: str,
    topic_prefix: str = "/foxrun/phase179",
) -> list[str]:
    """Build a deliberate wrong-type or wrong-QoS publication without changing the selected RMW."""

    topic = topic_for_spec(topic_prefix, spec)
    if negative_case == "type-mismatch":
        mismatch = next(candidate for candidate in MESSAGE_SPECS.values() if candidate.message_type != spec.message_type)
        return _build_publish_command(
            ros2_executable,
            topic,
            mismatch.message_type,
            mismatch.qos_reliability,
            mismatch.qos_history,
            mismatch.qos_depth,
            mismatch.qos_durability,
            mismatch.publish_payload(token),
        )
    if negative_case == "qos-incompatible":
        if spec.qos_reliability != "reliable":
            raise ValueError("qos-incompatible negative probes require a Reliable contract")
        return _build_publish_command(
            ros2_executable,
            topic,
            spec.message_type,
            "best_effort",
            spec.qos_history,
            spec.qos_depth,
            spec.qos_durability,
            spec.publish_payload(token),
        )
    raise ValueError(f"{negative_case} does not use a ROS2 publication command")


def expected_string_burst_value(token: str, final_sequence: int) -> dict[str, object]:
    """Return the exact bounded Unity value required after a latest-wins burst."""

    total = final_sequence + 1
    return {"type": "String", "data": f"{token}|seq={final_sequence}|total={total}"}


_STRING_BURST_PUBLISHER_CODE = r'''
import sys
import time

import rclpy
from rclpy.qos import DurabilityPolicy, HistoryPolicy, QoSProfile, ReliabilityPolicy
from std_msgs.msg import String

topic = sys.argv[1]
token = sys.argv[2]
final_sequence = int(sys.argv[3])
rate_hz = float(sys.argv[4])
total = final_sequence + 1
interval = 1.0 / rate_hz

rclpy.init(args=None)
node = rclpy.create_node("u2f_phase179_string_burst")
qos = QoSProfile(
    history=HistoryPolicy.KEEP_LAST,
    depth=10,
    reliability=ReliabilityPolicy.RELIABLE,
    durability=DurabilityPolicy.VOLATILE,
)
publisher = node.create_publisher(String, topic, qos)
try:
    discovery_deadline = time.monotonic() + 5.0
    while publisher.get_subscription_count() <= 0 and time.monotonic() < discovery_deadline:
        time.sleep(0.02)
    if publisher.get_subscription_count() <= 0:
        raise RuntimeError("No matching Unity subscription was discovered by the burst publisher")
    for sequence in range(total):
        message = String()
        message.data = f"{token}|seq={sequence}|total={total}"
        publisher.publish(message)
        if sequence != final_sequence:
            time.sleep(interval)
    time.sleep(0.1)
finally:
    node.destroy_publisher(publisher)
    node.destroy_node()
    rclpy.shutdown()
'''


def build_string_burst_command(
    python_executable: pathlib.Path,
    topic: str,
    token: str,
    final_sequence: int,
    rate_hz: float,
) -> list[str]:
    """Build one shell-free rclpy publisher process for a deterministic String burst."""

    if final_sequence < 1:
        raise ValueError("final_sequence must be at least 1 to prove latest-wins replacement")
    if not math.isfinite(rate_hz) or rate_hz <= 0.0:
        raise ValueError("rate_hz must be a finite positive number")
    return [
        str(python_executable),
        "-c",
        _STRING_BURST_PUBLISHER_CODE,
        topic,
        token,
        str(final_sequence),
        str(rate_hz),
    ]


def run_string_burst(
    env: Mapping[str, str],
    topic: str,
    token: str,
    final_sequence: int,
    rate_hz: float,
    timeout_seconds: float,
) -> None:
    """Publish one bounded latest-wins String burst from the caller's sourced ROS Python."""

    expected_duration = (final_sequence + 1) / rate_hz
    burst_timeout = min(timeout_seconds, max(2.0, expected_duration + 5.0))
    result = run_bounded_command(
        build_string_burst_command(pathlib.Path(sys.executable), topic, token, final_sequence, rate_hz),
        env,
        burst_timeout,
        "rclpy String burst",
    )
    require_command_success(result, "PUBLISH", "rclpy String burst")


def validate_string_burst_marker(
    baseline: UnityMarker,
    final: UnityMarker,
    token: str,
    final_sequence: int,
) -> dict[str, int]:
    """Prove latest-wins behavior without requiring every intermediate sample to apply."""

    if final.session != baseline.session or final.topic != baseline.topic or final.token != token:
        raise AcceptanceFailure("BURST", "Burst final marker did not belong to the active String subscription session.")
    if final.value != expected_string_burst_value(token, final_sequence):
        raise AcceptanceFailure("BURST", "Burst final marker did not contain the final deterministic String sequence.")
    if final.received <= baseline.received:
        raise AcceptanceFailure("BURST", "Burst did not increase the Unity received counter.")
    if final.replaced <= baseline.replaced:
        raise AcceptanceFailure("BURST", "Burst did not exercise latest-wins replacement.")
    if final.applied > final.received:
        raise AcceptanceFailure("BURST", "Burst marker violated applied <= received.")
    return {
        "finalSequence": final_sequence,
        "total": final_sequence + 1,
        "received": final.received,
        "applied": final.applied,
        "replaced": final.replaced,
    }


def terminate_owned_process(process: subprocess.Popen[str]) -> None:
    """Terminate precisely one helper-launched process tree, never global ROS state."""

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


def run_bounded_command(
    command: Sequence[str],
    env: Mapping[str, str],
    timeout_seconds: float,
    label: str,
) -> CommandResult:
    """Run one helper-owned argv and clean it up on timeout or interruption."""

    if not command:
        raise ValueError(f"{label} command must not be empty")
    popen_kwargs: dict[str, object] = {
        "env": dict(env),
        "text": True,
        "stdout": subprocess.PIPE,
        "stderr": subprocess.STDOUT,
    }
    if os.name != "nt":
        popen_kwargs["start_new_session"] = True
    process = subprocess.Popen(list(command), **popen_kwargs)
    try:
        output, _ = process.communicate(timeout=timeout_seconds)
        return CommandResult(tuple(command), process.returncode, output or "", False)
    except subprocess.TimeoutExpired:
        terminate_owned_process(process)
        try:
            output, _ = process.communicate(timeout=5.0)
        except subprocess.TimeoutExpired:
            output = ""
        return CommandResult(tuple(command), process.returncode, output or "", True)
    except KeyboardInterrupt:
        terminate_owned_process(process)
        raise


def find_ros2_executable(env: Mapping[str, str]) -> pathlib.Path:
    """Resolve the already-sourced ROS2 command without choosing an install root."""

    ros2 = shutil.which("ros2", path=env.get("PATH"))
    if not ros2:
        raise AcceptanceFailure("ENVIRONMENT", "ros2 was not found on PATH after sourcing the selected environment.")
    return pathlib.Path(ros2)


def require_command_success(result: CommandResult, category: str, label: str) -> None:
    """Convert a bounded command result into a stable failure without leaking output."""

    if result.timed_out:
        raise AcceptanceFailure(category, f"{label} timed out.")
    if result.return_code != 0:
        raise AcceptanceFailure(category, f"{label} exited with code {result.return_code}.")


def topic_list_has_type(output: str, topic: str, message_type: str) -> bool:
    """Return whether ``ros2 topic list -t`` reports the expected typed topic."""

    return f"{topic} [{message_type}]" in output


def wait_for_unity_subscription_topic(
    ros2_executable: pathlib.Path,
    env: Mapping[str, str],
    topic: str,
    message_type: str,
    timeout_seconds: float,
) -> str:
    """Poll the ROS graph until Unity exposes the expected typed subscription topic."""

    deadline = time.monotonic() + timeout_seconds
    last_output = ""
    while True:
        remaining = deadline - time.monotonic()
        if remaining < 0.0:
            break
        result = run_bounded_command(
            [str(ros2_executable), "topic", "list", "-t", "--no-daemon"],
            env,
            max(0.25, min(5.0, remaining or 0.25)),
            "ros2 topic list",
        )
        if not result.timed_out and result.return_code == 0:
            last_output = result.output
            if topic_list_has_type(last_output, topic, message_type):
                return last_output
        if time.monotonic() >= deadline:
            break
        time.sleep(min(0.5, max(0.0, deadline - time.monotonic())))
    raise AcceptanceFailure("DISCOVERY", "Unity did not expose the expected typed ROS2 subscription before timeout.")


def _parse_subscription_qos(section: str) -> tuple[str, str, int, str] | None:
    """Parse the portable subset of a verbose ROS2 subscription QoS block."""

    reliability_match = re.search(r"(?im)^\s*Reliability\s*:\s*([A-Z_]+)\s*$", section)
    history_match = re.search(
        r"(?im)^\s*History(?:\s*\(\s*Depth\s*\))?\s*:\s*([A-Z_]+)(?:\s*\(\s*([0-9]+)\s*\))?\s*$",
        section,
    )
    depth_match = re.search(r"(?im)^\s*Depth\s*:\s*([0-9]+)\s*$", section)
    durability_match = re.search(r"(?im)^\s*Durability\s*:\s*([A-Z_]+)\s*$", section)
    if reliability_match is None or history_match is None or durability_match is None:
        return None
    depth_text = history_match.group(2) or (depth_match.group(1) if depth_match is not None else None)
    if depth_text is None:
        return None
    return (
        reliability_match.group(1).lower(),
        history_match.group(1).lower(),
        int(depth_text),
        durability_match.group(1).lower(),
    )


def validate_unity_subscription_endpoint(topic_info: str, spec: MessageSpec) -> EndpointEvidence:
    """Require the topic type, a subscription endpoint, and the complete Phase179 QoS contract."""

    if spec.message_type not in topic_info:
        raise AcceptanceFailure("ENDPOINT", "ROS2 topic info did not report the expected message type.")
    count_match = re.search(r"Subscription count:\s*([0-9]+)", topic_info, re.IGNORECASE)
    subscription_count = int(count_match.group(1)) if count_match is not None else 0
    if subscription_count <= 0:
        raise AcceptanceFailure("ENDPOINT", "ROS2 topic info did not report a Unity subscription endpoint.")
    subscriptions = re.split(r"(?im)^\s*Subscription\s*#\d+\s*:\s*$", topic_info)[1:]
    if not subscriptions:
        raise AcceptanceFailure("ENDPOINT", "ROS2 topic info did not expose subscription QoS details.")
    expected = (spec.qos_reliability, spec.qos_history, spec.qos_depth, spec.qos_durability)
    for section in subscriptions:
        observed = _parse_subscription_qos(section)
        if observed == expected:
            return EndpointEvidence(spec.message_type, subscription_count, *observed)
    raise AcceptanceFailure("ENDPOINT", "Unity subscription QoS did not match the complete Phase179 contract.")


def query_unity_subscription_endpoint(
    ros2_executable: pathlib.Path,
    env: Mapping[str, str],
    topic: str,
    spec: MessageSpec,
    timeout_seconds: float,
) -> EndpointEvidence:
    """Capture and validate verbose endpoint evidence for one native topic."""

    deadline = time.monotonic() + timeout_seconds
    last_failure: AcceptanceFailure | None = None
    while True:
        remaining = deadline - time.monotonic()
        if remaining < 0.0:
            break
        result = run_bounded_command(
            [str(ros2_executable), "topic", "info", topic, "-v", "--no-daemon"],
            env,
            max(0.25, min(5.0, remaining or 0.25)),
            "ros2 topic info",
        )
        try:
            require_command_success(result, "ENDPOINT", "ros2 topic info")
            return validate_unity_subscription_endpoint(result.output, spec)
        except AcceptanceFailure as exc:
            last_failure = exc
        if time.monotonic() >= deadline:
            break
        time.sleep(min(0.5, max(0.0, deadline - time.monotonic())))
    if last_failure is not None:
        raise last_failure
    raise AcceptanceFailure("ENDPOINT", "ROS2 topic info did not produce endpoint evidence before timeout.")


def _parse_marker_fields(text: str) -> dict[str, str]:
    """Parse one bounded key=value marker line generated by the Unity sample."""

    return dict(re.findall(r"\b([A-Za-z][A-Za-z0-9_]*)=([^\s]+)", text))


def parse_unity_markers(text: str) -> list[UnityMarker]:
    """Parse valid applied markers, retaining only copied bounded JSON values."""

    markers: list[UnityMarker] = []
    lines = text.splitlines()
    for index, line in enumerate(lines):
        marker_index = line.find(UNITY_APPLIED_MARKER)
        if marker_index < 0:
            continue
        fields_text = line[marker_index + len(UNITY_APPLIED_MARKER) :].strip()
        if not fields_text and index + 1 < len(lines):
            fields_text = lines[index + 1].strip()
        fields = _parse_marker_fields(fields_text)
        try:
            value = json.loads(fields["value"]) if "value" in fields else None
            if value is not None and not isinstance(value, dict):
                continue
            markers.append(
                UnityMarker(
                    session=int(fields["session"]),
                    topic=fields["topic"],
                    token=fields["token"],
                    received=int(fields["received"]),
                    applied=int(fields["applied"]),
                    replaced=int(fields["replaced"]),
                    value=value,
                )
            )
        except (KeyError, TypeError, ValueError, json.JSONDecodeError):
            continue
    return markers


def parse_unity_ready_markers(text: str) -> list[UnityReadyMarker]:
    """Parse bounded native-runtime identity markers emitted by the Unity acceptance receiver."""

    markers: list[UnityReadyMarker] = []
    lines = text.splitlines()
    for index, line in enumerate(lines):
        marker_index = line.find(UNITY_READY_MARKER)
        if marker_index < 0:
            continue
        fields_text = line[marker_index + len(UNITY_READY_MARKER) :].strip()
        if not fields_text and index + 1 < len(lines):
            fields_text = lines[index + 1].strip()
        fields = _parse_marker_fields(fields_text)
        try:
            markers.append(UnityReadyMarker(fields["runtime"], fields["rmw"], fields["token"]))
        except KeyError:
            continue
    return markers


def find_matching_unity_ready_marker(
    text: str,
    runtime: str,
    rmw: str,
    token: str | None,
    excluded_tokens: Sequence[str] = (),
) -> UnityReadyMarker:
    """Return the current Unity runtime identity, rejecting any READY token captured before a local run."""

    markers = parse_unity_ready_markers(text)
    if not markers:
        raise AcceptanceFailure("READY_TIMEOUT", "Unity did not yet emit a native runtime READY marker.")
    matching_identity = [
        marker
        for marker in markers
        if marker.runtime == runtime and marker.rmw == rmw and (token is None or marker.token == token)
    ]
    if not matching_identity:
        raise AcceptanceFailure("READY_MISMATCH", "Unity READY marker did not match the requested runtime, RMW, and optional token identity.")
    excluded = frozenset(excluded_tokens)
    for marker in reversed(matching_identity):
        if marker.token not in excluded:
            return marker
    raise AcceptanceFailure("READY_STALE", "Unity READY marker was already present before this local acceptance run.")


def _read_unity_log(log_path: pathlib.Path, start_offset: int | None) -> str:
    """Read all or only post-publication Unity log content without accepting a truncated log."""

    if not log_path.is_file():
        raise AcceptanceFailure("UNITY_LOG", "Unity log is unavailable while waiting for acceptance evidence.")
    if start_offset is None:
        return log_path.read_text(encoding="utf-8", errors="replace")
    if start_offset < 0:
        raise ValueError("start_offset must be non-negative")
    if log_path.stat().st_size < start_offset:
        raise AcceptanceFailure("UNITY_LOG", "Unity log was truncated during acceptance observation.")
    with log_path.open("rb") as stream:
        stream.seek(start_offset)
        return stream.read().decode("utf-8", errors="replace")


def capture_unity_ready_marker_tokens(log_path: pathlib.Path, runtime: str, rmw: str) -> frozenset[str]:
    """Snapshot matching READY tokens before local launch without assuming Unity's Editor.log grows append-only."""

    return frozenset(
        marker.token
        for marker in parse_unity_ready_markers(_read_unity_log(log_path, None))
        if marker.runtime == runtime and marker.rmw == rmw
    )


def find_matching_unity_marker(
    text: str,
    topic: str,
    token: str,
    expected_value: dict[str, object],
) -> UnityMarker:
    """Return a fully matching marker or a stable proof/value failure."""

    matches = [marker for marker in parse_unity_markers(text) if marker.topic == topic and marker.token == token]
    if not matches:
        raise AcceptanceFailure("UNITY_TIMEOUT", "Unity did not yet emit a matching applied marker.")
    for marker in reversed(matches):
        if marker.received <= 0 or marker.applied <= 0 or marker.applied > marker.received or marker.replaced < 0:
            continue
        if marker.value == expected_value:
            return marker
    raise AcceptanceFailure("VALUE_MISMATCH", "Unity marker counters or copied value did not match the published contract.")


def wait_for_unity_marker(
    log_path: pathlib.Path,
    topic: str,
    token: str,
    expected_value: dict[str, object],
    timeout_seconds: float,
    start_offset: int | None = None,
) -> UnityMarker:
    """Wait for a matching Unity marker, optionally only from post-publication log content."""

    deadline = time.monotonic() + timeout_seconds
    last_value_failure: AcceptanceFailure | None = None
    while True:
        if log_path.is_file():
            text = _read_unity_log(log_path, start_offset)
            try:
                return find_matching_unity_marker(text, topic, token, expected_value)
            except AcceptanceFailure as exc:
                if exc.category == "VALUE_MISMATCH":
                    last_value_failure = exc
        if time.monotonic() >= deadline:
            if last_value_failure is not None:
                raise last_value_failure
            raise AcceptanceFailure("UNITY_TIMEOUT", "Unity did not emit the matching applied marker before timeout.")
        time.sleep(min(0.25, max(0.0, deadline - time.monotonic())))


def wait_for_unity_ready_marker(
    log_path: pathlib.Path,
    runtime: str,
    rmw: str,
    token: str | None,
    timeout_seconds: float,
    start_offset: int | None = None,
    excluded_tokens: Sequence[str] = (),
) -> UnityReadyMarker:
    """Wait for a Unity native-runtime marker after an optional log offset and token check."""

    deadline = time.monotonic() + timeout_seconds
    last_mismatch: AcceptanceFailure | None = None
    while True:
        if log_path.is_file():
            try:
                return find_matching_unity_ready_marker(
                    _read_unity_log(log_path, start_offset), runtime, rmw, token, excluded_tokens
                )
            except AcceptanceFailure as exc:
                if exc.category in ("READY_MISMATCH", "READY_STALE"):
                    last_mismatch = exc
        if time.monotonic() >= deadline:
            if last_mismatch is not None:
                raise last_mismatch
            raise AcceptanceFailure("READY_TIMEOUT", "Unity did not emit the expected native runtime READY marker before timeout.")
        time.sleep(min(0.25, max(0.0, deadline - time.monotonic())))


def classify_verdict(
    *,
    unity_log_available: bool,
    message_results: Sequence[Mapping[str, object]],
    failure: AcceptanceFailure | None,
) -> str:
    """Classify results without treating publish-only evidence as a PASS."""

    if failure is not None:
        return f"FAIL_{failure.category}"
    if not unity_log_available:
        return "PEER_PUBLISH_COMPLETE_UNITY_PROOF_PENDING"
    if not message_results or not all(result.get("published") and result.get("unityProof") for result in message_results):
        return "FAIL_UNITY_TIMEOUT"
    return "PASS"


def classify_negative_verdict(
    *,
    negative_case: str,
    unity_log_available: bool,
    expectation_observed: bool,
    unity_ready: bool,
    contract_identity: bool,
    unity_no_apply: bool,
    failure: AcceptanceFailure | None,
) -> str:
    """Keep an expected rejection distinct from successful positive interoperability."""

    if failure is not None:
        return f"FAIL_{failure.category}"
    if not expectation_observed:
        return "FAIL_NEGATIVE_EXPECTATION"
    normalized_case = negative_case.upper().replace("-", "_")
    if not unity_log_available:
        return f"LOCAL_NEGATIVE_EVIDENCE_{normalized_case}_UNITY_PROOF_PENDING"
    if not unity_ready:
        return "FAIL_READY"
    if not contract_identity:
        return "FAIL_CONTRACT_IDENTITY"
    if not unity_no_apply:
        return "FAIL_NEGATIVE_APPLY"
    return f"EXPECTED_NEGATIVE_{normalized_case}"


def unity_log_offset(log_path: pathlib.Path) -> int:
    """Capture an append-only Unity log offset before a deliberate no-data probe."""

    if not log_path.is_file():
        raise AcceptanceFailure("UNITY_LOG", "--unity-log was supplied but does not exist for negative acceptance.")
    return log_path.stat().st_size


def wait_for_no_unity_apply_after_offset(
    log_path: pathlib.Path,
    topic: str,
    offset: int,
    timeout_seconds: float,
) -> None:
    """Require a bounded observation window with no new applied marker for a native topic."""

    deadline = time.monotonic() + timeout_seconds
    while True:
        if not log_path.is_file():
            raise AcceptanceFailure("UNITY_LOG", "Unity log disappeared during negative acceptance observation.")
        if log_path.stat().st_size < offset:
            raise AcceptanceFailure("UNITY_LOG", "Unity log was truncated during negative acceptance observation.")
        with log_path.open("rb") as stream:
            stream.seek(offset)
            appended = stream.read().decode("utf-8", errors="replace")
        if any(marker.topic == topic for marker in parse_unity_markers(appended)):
            raise AcceptanceFailure("NEGATIVE_APPLY", "Unity applied data during an expected-rejection probe.")
        if time.monotonic() >= deadline:
            return
        time.sleep(min(0.25, max(0.0, deadline - time.monotonic())))


def topic_list_has_topic(output: str, topic: str) -> bool:
    """Return whether ``ros2 topic list -t`` exposed any type for a named topic."""

    return any(line.strip().startswith(f"{topic} [") for line in output.splitlines())


def wait_for_unity_subscription_absence(
    ros2_executable: pathlib.Path,
    env: Mapping[str, str],
    topic: str,
    timeout_seconds: float,
) -> None:
    """Observe a bounded graph window where an intentionally mismatched-RMW Unity peer is absent."""

    deadline = time.monotonic() + timeout_seconds
    observed_graph = False
    while True:
        remaining = deadline - time.monotonic()
        if remaining < 0.0:
            break
        result = run_bounded_command(
            [str(ros2_executable), "topic", "list", "-t", "--no-daemon"],
            env,
            max(0.25, min(5.0, remaining or 0.25)),
            "ros2 topic list",
        )
        if not result.timed_out and result.return_code == 0:
            observed_graph = True
            if topic_list_has_topic(result.output, topic):
                raise AcceptanceFailure(
                    "NEGATIVE_EXPECTATION",
                    "A Unity endpoint was discovered despite the requested RMW mismatch; no fallback transport was attempted.",
                )
        if time.monotonic() >= deadline:
            break
        time.sleep(min(0.5, max(0.0, deadline - time.monotonic())))
    if not observed_graph:
        raise AcceptanceFailure("DISCOVERY", "ROS2 topic list did not produce graph evidence for the RMW mismatch probe.")


def _negative_command_outcome(result: CommandResult) -> str:
    """Return a safe, output-free description of one negative publication attempt."""

    if result.timed_out:
        return "timed-out-no-match"
    if result.return_code == 0:
        return "completed"
    return "rejected"


def verify_current_unity_ready_identity(args: argparse.Namespace, expected_rmw: str) -> bool:
    """Require an explicitly identified current Unity native runtime when log proof is requested."""

    if args.unity_log is None or args.unity_ready_token is None:
        return False
    wait_for_unity_ready_marker(
        args.unity_log,
        args.distro,
        expected_rmw,
        args.unity_ready_token,
        args.timeout_seconds,
    )
    return True


def run_negative_case(
    args: argparse.Namespace,
    env: Mapping[str, str],
    ros2_executable: pathlib.Path,
    token: str,
) -> dict[str, object]:
    """Run one bounded expected-rejection probe without mutating the selected ROS transport."""

    if args.negative_case is None or len(args.message_set) != 1:
        raise ValueError("run_negative_case requires one parsed negative case and one message contract")
    spec = MESSAGE_SPECS[args.message_set[0]]
    topic = topic_for_spec(args.topic_prefix, spec)
    result: dict[str, object] = {
        "name": spec.name,
        "topic": topic,
        "negativeCase": args.negative_case,
        "expectationObserved": False,
        "unityReady": False,
        "contractIdentity": False,
        "unityNoApply": False,
        "unityCounterUnchanged": False,
    }
    interface = run_bounded_command(
        [str(ros2_executable), "interface", "show", spec.message_type],
        env,
        min(10.0, args.timeout_seconds),
        "ros2 interface show",
    )
    require_command_success(interface, "ENDPOINT", "ros2 interface show")

    observation_seconds = min(3.0, args.timeout_seconds)
    if args.negative_case == "rmw-mismatch":
        result["expectedPeerRmw"] = args.negative_peer_rmw
        result["unityReady"] = verify_current_unity_ready_identity(args, args.negative_peer_rmw)
        result["contractIdentity"] = result["unityReady"]
        log_offset = unity_log_offset(args.unity_log) if bool(result["unityReady"]) else None
        wait_for_unity_subscription_absence(ros2_executable, env, topic, args.timeout_seconds)
        result["graph"] = {"topicVisible": False}
        result["expectationObserved"] = True
        if log_offset is not None:
            wait_for_no_unity_apply_after_offset(args.unity_log, topic, log_offset, observation_seconds)
            result["unityNoApply"] = True
            result["unityCounterUnchanged"] = True
        return result

    wait_for_unity_subscription_topic(ros2_executable, env, topic, spec.message_type, args.timeout_seconds)
    endpoint = query_unity_subscription_endpoint(ros2_executable, env, topic, spec, args.timeout_seconds)
    result["graph"] = {
        "messageType": endpoint.message_type,
        "subscriptionCount": endpoint.subscription_count,
        "qosReliability": endpoint.qos_reliability,
        "qosHistory": endpoint.qos_history,
        "qosDepth": endpoint.qos_depth,
        "qosDurability": endpoint.qos_durability,
    }
    result["unityReady"] = verify_current_unity_ready_identity(args, args.rmw)
    if bool(result["unityReady"]):
        baseline_offset = unity_log_offset(args.unity_log)
        positive = run_bounded_command(
            build_publish_command(ros2_executable, spec, token, args.topic_prefix),
            env,
            args.timeout_seconds,
            f"ros2 topic pub current-contract {spec.name}",
        )
        require_command_success(positive, "PUBLISH", f"ros2 topic pub current-contract {spec.name}")
        baseline = wait_for_unity_marker(
            args.unity_log,
            topic,
            token,
            spec.expected_value(token),
            args.timeout_seconds,
            start_offset=baseline_offset,
        )
        result["contractIdentity"] = True
        result["identitySession"] = baseline.session
    log_offset = unity_log_offset(args.unity_log) if bool(result["contractIdentity"]) else None
    topic = topic_for_spec(args.topic_prefix, spec)
    negative_command = build_negative_publish_command(ros2_executable, spec, token, args.negative_case, args.topic_prefix)
    result["attemptedMessageType"] = negative_command[negative_command.index(topic) + 1]
    result["attemptedQosReliability"] = negative_command[negative_command.index("--qos-reliability") + 1]
    published = run_bounded_command(
        negative_command,
        env,
        args.timeout_seconds,
        f"ros2 topic pub negative {args.negative_case}",
    )
    result["negativePublishOutcome"] = _negative_command_outcome(published)
    if args.negative_case == "type-mismatch":
        result["typeMismatchObserved"] = endpoint.message_type != result["attemptedMessageType"]
        if not result["typeMismatchObserved"]:
            raise AcceptanceFailure("NEGATIVE_EXPECTATION", "ROS2 graph did not retain a type mismatch for the negative probe.")
    else:
        result["qosMismatchObserved"] = endpoint.qos_reliability != result["attemptedQosReliability"]
        if not result["qosMismatchObserved"]:
            raise AcceptanceFailure("NEGATIVE_EXPECTATION", "ROS2 graph did not retain a QoS mismatch for the negative probe.")
    result["expectationObserved"] = True
    if log_offset is not None:
        wait_for_no_unity_apply_after_offset(args.unity_log, topic, log_offset, observation_seconds)
        result["unityNoApply"] = True
        result["unityCounterUnchanged"] = True
    return result


def sanitize_summary(value: object) -> object:
    """Remove machine-specific Zenoh paths and secret-bearing diagnostic text."""

    if isinstance(value, Mapping):
        sanitized: dict[str, object] = {}
        for key, child in value.items():
            lower = str(key).lower()
            if any(part in lower for part in _SENSITIVE_KEY_PARTS):
                continue
            if lower == "error":
                sanitized[str(key)] = "redacted"
                continue
            sanitized[str(key)] = sanitize_summary(child)
        return sanitized
    if isinstance(value, list):
        return [sanitize_summary(child) for child in value]
    if isinstance(value, str) and _SENSITIVE_VALUE_RE.search(value):
        return "redacted"
    return value


def write_summary(path: pathlib.Path, summary: Mapping[str, object]) -> None:
    """Persist portable evidence JSON outside package source directories."""

    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(sanitize_summary(summary), indent=2, sort_keys=True) + "\n", encoding="utf-8")


def configure_zenoh_topology(
    args: argparse.Namespace,
    env: dict[str, str],
) -> zenoh_topology.ZenohTopologyHandle:
    """Start or select one explicit Zenoh topology and wait for its real ready marker."""

    try:
        options = zenoh_topology.validate_topology_options(
            args.rmw,
            router=args.zenoh_router,
            no_router=args.no_zenoh_router,
            topology_id=args.zenoh_topology_id,
        )
        return zenoh_topology.start_topology(
            options,
            env=env,
            cwd=workspace_root(),
            log_path=args.summary_json.with_name(args.summary_json.stem + "-zenoh-router.log"),
            ready_timeout_seconds=args.timeout_seconds,
            ready_marker=args.zenoh_router_ready_marker,
        )
    except zenoh_topology.ZenohTopologyError as exc:
        raise AcceptanceFailure(exc.category, "Zenoh topology setup did not complete.") from exc
    except ValueError as exc:
        raise AcceptanceFailure("ENVIRONMENT", "Zenoh topology arguments were invalid.") from exc


def topology_summary(configured: zenoh_topology.ZenohTopologyHandle | str) -> dict[str, object]:
    """Return portable topology evidence without leaking router or session-config paths."""

    if isinstance(configured, str):
        return {"mode": configured, "readiness": configured}
    return {"mode": configured.mode, "readiness": configured.readiness}


def close_configured_topology(configured: zenoh_topology.ZenohTopologyHandle | str | None) -> None:
    """Release only a topology process actually owned by this helper."""

    if isinstance(configured, zenoh_topology.ZenohTopologyHandle):
        zenoh_topology.close_topology(configured)


def collect_optional_windows_peer_diagnostic(args: argparse.Namespace) -> str:
    """Use the shared Windows helper only when an operator explicitly asks for it."""

    if args.ros2_root is None:
        return "not-requested"
    try:
        import _ros2_windows_env as ros2env

        root = args.ros2_root.resolve(strict=True)
        pixi_python, ros2_script = ros2env.validate_ros2_root(root)
        env = ros2env.build_ros_env(root, args.rmw, args.discovery_range, str(args.domain_id), args.distro)
        result = ros2env.run_ros2(
            pixi_python,
            ros2_script,
            env,
            ["topic", "list", "-t", "--no-daemon"],
            check=False,
            timeout_seconds=5.0,
        )
        return "available" if result.returncode == 0 else "unavailable"
    except (FileNotFoundError, RuntimeError, subprocess.TimeoutExpired):
        return "unavailable"


def main(argv: Sequence[str] | None = None) -> int:
    """Run all selected native input probes and record an honest evidence verdict."""

    args = parse_args(argv)
    token = args.token or f"phase179-{uuid.uuid4().hex}"
    message_results: list[dict[str, object]] = []
    applied_markers: dict[str, UnityMarker] = {}
    summary: dict[str, object] = {
        "phase": 179,
        "role": "linux-ros2-peer",
        "distro": args.distro,
        "rmwImplementation": args.rmw,
        "domainId": args.domain_id,
        "discoveryRange": args.discovery_range,
        "token": token,
        "topicPrefix": args.topic_prefix,
        "messageSet": list(args.message_set),
        "unityLogProvided": args.unity_log is not None,
        "negativeCase": args.negative_case,
        "messageResults": message_results,
    }
    if args.profile_id is not None:
        summary["profileId"] = args.profile_id
        summary["surface"] = args.surface
    if args.zenoh_topology_id is not None:
        summary["zenohTopologyId"] = args.zenoh_topology_id
    if args.negative_peer_rmw is not None:
        summary["negativePeerRmw"] = args.negative_peer_rmw
    configured_topology: zenoh_topology.ZenohTopologyHandle | str | None = None
    failure: AcceptanceFailure | None = None
    exit_code = 1
    try:
        env = build_linux_environment(args)
        summary["optionalWindowsPeerDiagnostic"] = collect_optional_windows_peer_diagnostic(args)
        configured_topology = configure_zenoh_topology(args, env)
        summary["zenohTopology"] = topology_summary(configured_topology)
        ros2_executable = find_ros2_executable(env)

        if args.negative_case is not None:
            message_results.append(run_negative_case(args, env, ros2_executable, token))
        else:
            for name in args.message_set:
                spec = MESSAGE_SPECS[name]
                topic = topic_for_spec(args.topic_prefix, spec)
                result: dict[str, object] = {"name": name, "topic": topic, "published": False, "unityProof": False}
                message_results.append(result)

                interface = run_bounded_command(
                    [str(ros2_executable), "interface", "show", spec.message_type],
                    env,
                    min(10.0, args.timeout_seconds),
                    "ros2 interface show",
                )
                require_command_success(interface, "ENDPOINT", "ros2 interface show")
                wait_for_unity_subscription_topic(
                    ros2_executable,
                    env,
                    topic,
                    spec.message_type,
                    args.timeout_seconds,
                )
                endpoint = query_unity_subscription_endpoint(ros2_executable, env, topic, spec, args.timeout_seconds)
                result["graph"] = {
                    "messageType": endpoint.message_type,
                    "subscriptionCount": endpoint.subscription_count,
                    "qosReliability": endpoint.qos_reliability,
                    "qosHistory": endpoint.qos_history,
                    "qosDepth": endpoint.qos_depth,
                    "qosDurability": endpoint.qos_durability,
                }

                marker_offset = unity_log_offset(args.unity_log) if args.unity_log is not None else None
                published = run_bounded_command(
                    build_publish_command(ros2_executable, spec, token, args.topic_prefix),
                    env,
                    args.timeout_seconds,
                    f"ros2 topic pub {name}",
                )
                require_command_success(published, "PUBLISH", f"ros2 topic pub {name}")
                result["published"] = True

                if args.unity_log is not None:
                    marker = wait_for_unity_marker(
                        args.unity_log,
                        topic,
                        token,
                        spec.expected_value(token),
                        args.timeout_seconds,
                        start_offset=marker_offset,
                    )
                    result["unityProof"] = True
                    result["received"] = marker.received
                    result["applied"] = marker.applied
                    result["replaced"] = marker.replaced
                    applied_markers[name] = marker

        if args.string_burst_final_sequence is not None:
            string_spec = MESSAGE_SPECS["string"]
            string_topic = topic_for_spec(args.topic_prefix, string_spec)
            string_result = next(result for result in message_results if result["name"] == "string")
            burst_marker_offset = unity_log_offset(args.unity_log) if args.unity_log is not None else None
            run_string_burst(
                env,
                string_topic,
                token,
                args.string_burst_final_sequence,
                args.string_burst_rate_hz,
                args.timeout_seconds,
            )
            if args.unity_log is None:
                string_result["burst"] = {
                    "finalSequence": args.string_burst_final_sequence,
                    "total": args.string_burst_final_sequence + 1,
                    "unityProofPending": True,
                }
            else:
                final_marker = wait_for_unity_marker(
                    args.unity_log,
                    string_topic,
                    token,
                    expected_string_burst_value(token, args.string_burst_final_sequence),
                    args.timeout_seconds,
                    start_offset=burst_marker_offset,
                )
                string_result["burst"] = validate_string_burst_marker(
                    applied_markers["string"],
                    final_marker,
                    token,
                    args.string_burst_final_sequence,
                )

        if args.negative_case is not None:
            negative_result = message_results[0]
            summary["verdict"] = classify_negative_verdict(
                negative_case=args.negative_case,
                unity_log_available=args.unity_log is not None,
                expectation_observed=bool(negative_result.get("expectationObserved")),
                unity_ready=bool(negative_result.get("unityReady")),
                contract_identity=bool(negative_result.get("contractIdentity")),
                unity_no_apply=bool(negative_result.get("unityNoApply")),
                failure=None,
            )
            exit_code = 0 if str(summary["verdict"]).startswith("EXPECTED_NEGATIVE_") else 2
        else:
            summary["verdict"] = classify_verdict(
                unity_log_available=args.unity_log is not None,
                message_results=message_results,
                failure=None,
            )
            exit_code = 0 if summary["verdict"] == "PASS" else 2
    except AcceptanceFailure as exc:
        failure = exc
        summary["failureCategory"] = exc.category
    except KeyboardInterrupt:
        failure = AcceptanceFailure("INTERRUPTED", "Acceptance was interrupted by the operator.")
        summary["failureCategory"] = failure.category
    except (OSError, subprocess.SubprocessError) as exc:
        failure = AcceptanceFailure("ENVIRONMENT", "A helper-owned ROS2 process could not be started or completed.")
        summary["failureCategory"] = failure.category
    finally:
        if failure is not None:
            if args.negative_case is not None:
                negative_result = message_results[0] if message_results else {}
                summary["verdict"] = classify_negative_verdict(
                    negative_case=args.negative_case,
                    unity_log_available=args.unity_log is not None,
                    expectation_observed=bool(negative_result.get("expectationObserved")),
                    unity_ready=bool(negative_result.get("unityReady")),
                    contract_identity=bool(negative_result.get("contractIdentity")),
                    unity_no_apply=bool(negative_result.get("unityNoApply")),
                    failure=failure,
                )
            else:
                summary["verdict"] = classify_verdict(
                    unity_log_available=args.unity_log is not None,
                    message_results=message_results,
                    failure=failure,
                )
        try:
            write_summary(args.summary_json, summary)
            print(f"Summary: {args.summary_json}")
            print(f"Verdict: {summary['verdict']}")
        finally:
            close_configured_topology(configured_topology)
    return exit_code


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
