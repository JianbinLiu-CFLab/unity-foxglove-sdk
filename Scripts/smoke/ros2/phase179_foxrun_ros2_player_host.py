#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Module: Scripts/smoke
# Purpose: WindowsStandalone64 host evidence for Phase179 FoxRun native subscriptions.

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


def _marker_fields(line: str, marker: str) -> dict[str, str] | None:
    """Return bounded key=value fields after an exact Phase179 marker."""

    index = line.find(marker)
    if index < 0:
        return None
    return dict(re.findall(r"\b([A-Za-z][A-Za-z0-9_]*)=([^\s]+)", line[index + len(marker) :]))


def find_ready_marker(text: str, distro: str, rmw: str, token: str) -> dict[str, str]:
    """Require the Player itself to report its active runtime/RMW/token selection."""

    saw_marker = False
    for line in text.splitlines():
        fields = _marker_fields(line, READY_MARKER)
        if fields is None:
            continue
        saw_marker = True
        if fields.get("runtime") == distro and fields.get("rmw") == rmw and fields.get("token") == token:
            return fields
    if saw_marker:
        raise PlayerHostFailure("READY_MISMATCH", "Player READY marker did not report the requested runtime, RMW, and token.")
    raise PlayerHostFailure("READY_TIMEOUT", "Player did not emit its Phase179 READY marker.")


def find_completion_marker(text: str, token: str) -> dict[str, str]:
    """Require a success completion marker with the same correlation token."""

    saw_marker = False
    for line in text.splitlines():
        fields = _marker_fields(line, COMPLETE_MARKER)
        if fields is None:
            continue
        saw_marker = True
        if fields.get("token") == token and fields.get("outcome") == "success" and fields.get("exitCode") == "0":
            return fields
    if saw_marker:
        raise PlayerHostFailure("COMPLETE_MISMATCH", "Player completion marker was not a successful zero-exit result for this token.")
    raise PlayerHostFailure("COMPLETE_TIMEOUT", "Player did not emit its Phase179 completion marker.")


def wait_for_ready_marker(
    player_log: pathlib.Path,
    distro: str,
    rmw: str,
    token: str,
    timeout_seconds: float,
) -> dict[str, str]:
    """Poll the growing Player log until a matching active-runtime READY marker exists."""

    deadline = time.monotonic() + timeout_seconds
    last_mismatch: PlayerHostFailure | None = None
    while True:
        if player_log.is_file():
            try:
                return find_ready_marker(player_log.read_text(encoding="utf-8", errors="replace"), distro, rmw, token)
            except PlayerHostFailure as exc:
                if exc.category == "READY_MISMATCH":
                    last_mismatch = exc
        if time.monotonic() >= deadline:
            if last_mismatch is not None:
                raise last_mismatch
            raise PlayerHostFailure("READY_TIMEOUT", "Player did not become ready before timeout.")
        time.sleep(min(0.25, max(0.0, deadline - time.monotonic())))


def verify_required_applied_markers(
    text: str,
    token: str,
    string_burst_final_sequence: int | None = None,
) -> dict[str, object]:
    """Require copied, exact values for every non-optional Phase179 message contract."""

    markers: dict[str, inbound.UnityMarker] = {}
    for name in REQUIRED_MESSAGE_NAMES:
        spec = inbound.MESSAGE_SPECS[name]
        try:
            markers[name] = inbound.find_matching_unity_marker(
                text,
                inbound.topic_for_spec("/foxrun/phase179", spec),
                token,
                spec.expected_value(token),
            )
        except inbound.AcceptanceFailure as exc:
            category = "VALUE_MISMATCH" if exc.category == "VALUE_MISMATCH" else "APPLIED_MARKERS"
            raise PlayerHostFailure(category, "Player did not prove all required copied native values.") from exc
    evidence: dict[str, object] = {}
    if string_burst_final_sequence is not None:
        try:
            final_marker = inbound.find_matching_unity_marker(
                text,
                inbound.topic_for_spec("/foxrun/phase179", inbound.MESSAGE_SPECS["string"]),
                token,
                inbound.expected_string_burst_value(token, string_burst_final_sequence),
            )
            evidence["stringBurst"] = inbound.validate_string_burst_marker(
                markers["string"],
                final_marker,
                token,
                string_burst_final_sequence,
            )
        except inbound.AcceptanceFailure as exc:
            category = "VALUE_MISMATCH" if exc.category == "VALUE_MISMATCH" else "BURST"
            raise PlayerHostFailure(category, "Player did not prove the requested latest-wins String burst.") from exc
    return evidence


def classify_player_verdict(
    *,
    ready: bool,
    all_applied: bool,
    exit_code: int | None,
    failure: PlayerHostFailure | None,
) -> str:
    """Classify Player evidence strictly; launch success alone is not accepted."""

    if failure is not None:
        return f"FAIL_{failure.category}"
    if not ready:
        return "FAIL_READY"
    if not all_applied:
        return "FAIL_APPLIED_MARKERS"
    if exit_code != 0:
        return "FAIL_EXIT_CODE"
    return "PLAYER_PROOF_COMPLETE_LINUX_PEER_CORRELATION_PENDING"


def sanitize_summary(value: object) -> object:
    """Use the same portable evidence redaction policy as the Linux helper."""

    return inbound.sanitize_summary(value)


def write_summary(path: pathlib.Path, summary: Mapping[str, object]) -> None:
    """Write only sanitized public evidence outside Unity package source directories."""

    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(sanitize_summary(summary), indent=2, sort_keys=True) + "\n", encoding="utf-8")


def configure_zenoh_topology(
    args: argparse.Namespace,
    env: dict[str, str],
) -> zenoh_topology.ZenohTopologyHandle:
    """Start or select one explicit Zenoh topology before launching the Player."""

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
            cwd=inbound.workspace_root(),
            log_path=args.summary_json.with_name(args.summary_json.stem + "-zenoh-router.log"),
            ready_timeout_seconds=args.ready_timeout_seconds,
            ready_marker=args.zenoh_router_ready_marker,
        )
    except zenoh_topology.ZenohTopologyError as exc:
        raise PlayerHostFailure(exc.category, "Zenoh topology setup did not complete.") from exc
    except ValueError as exc:
        raise PlayerHostFailure("ENVIRONMENT", "Zenoh topology arguments were invalid.") from exc


def validate_player_path(player: pathlib.Path) -> pathlib.Path:
    """Reject relative or missing Player inputs before spawning any process."""

    if not player.is_absolute():
        raise PlayerHostFailure("ENVIRONMENT", "--player must be an absolute WindowsStandalone64 executable path.")
    if not player.is_file():
        raise PlayerHostFailure("ENVIRONMENT", "--player does not exist or is not a file.")
    return player.resolve()


def main(argv: Sequence[str] | None = None) -> int:
    """Launch the Player, wait for proof, then emit a correlated portable summary."""

    args = parse_args(argv)
    summary: dict[str, object] = {
        "phase": 179,
        "role": "windows-player-host",
        "distro": args.distro,
        "rmwImplementation": args.rmw,
        "domainId": args.domain_id,
        "discoveryRange": args.discovery_range,
        "token": args.token,
        "topicPrefix": args.topic_prefix,
        "messageSet": list(args.message_set),
        "ready": False,
        "allRequiredApplied": False,
    }
    if args.profile_id is not None:
        summary["profileId"] = args.profile_id
        summary["surface"] = args.surface
    if args.zenoh_topology_id is not None:
        summary["zenohTopologyId"] = args.zenoh_topology_id
    configured_topology: zenoh_topology.ZenohTopologyHandle | str | None = None
    owned_processes: list[subprocess.Popen[str]] = []
    player: subprocess.Popen[str] | None = None
    failure: PlayerHostFailure | None = None
    exit_code: int | None = None
    try:
        player_path = validate_player_path(args.player)
        args.player_log.parent.mkdir(parents=True, exist_ok=True)
        env = build_player_environment(args)
        configured_topology = configure_zenoh_topology(args, env)
        summary["zenohTopology"] = inbound.topology_summary(configured_topology)
        player = launch_owned_process(
            build_player_command(
                player_path,
                args.player_log,
                args.token,
                string_burst_final_sequence=args.string_burst_final_sequence,
            ),
            player_path.parent,
            env,
        )
        owned_processes.append(player)

        wait_for_ready_marker(args.player_log, args.distro, args.rmw, args.token, args.ready_timeout_seconds)
        summary["ready"] = True
        print(
            "[phase179-player-host] Player READY with requested runtime/RMW; waiting for Linux peer token " + args.token,
            flush=True,
        )
        player_result = wait_for_owned_process(player, args.exit_timeout_seconds)
        exit_code = player_result.return_code
        if player_result.timed_out:
            raise PlayerHostFailure("EXIT_TIMEOUT", "Player did not exit before the bounded deadline.")
        if exit_code != 0:
            raise PlayerHostFailure("EXIT_CODE", "Player exited with a non-zero operating-system exit code.")

        log_text = args.player_log.read_text(encoding="utf-8", errors="replace") if args.player_log.is_file() else ""
        applied_evidence = verify_required_applied_markers(
            log_text,
            args.token,
            string_burst_final_sequence=args.string_burst_final_sequence,
        )
        summary["allRequiredApplied"] = True
        if "stringBurst" in applied_evidence:
            summary["stringBurst"] = applied_evidence["stringBurst"]
        find_completion_marker(log_text, args.token)
    except PlayerHostFailure as exc:
        failure = exc
        summary["failureCategory"] = exc.category
    except KeyboardInterrupt:
        failure = PlayerHostFailure("INTERRUPTED", "Player host was interrupted by the operator.")
        summary["failureCategory"] = failure.category
    except (OSError, subprocess.SubprocessError):
        failure = PlayerHostFailure("ENVIRONMENT", "A helper-owned Player or router process could not be started or completed.")
        summary["failureCategory"] = failure.category
    finally:
        for process in reversed(owned_processes):
            terminate_owned_process(process)
        summary["playerExitCode"] = exit_code
        summary["verdict"] = classify_player_verdict(
            ready=bool(summary["ready"]),
            all_applied=bool(summary["allRequiredApplied"]),
            exit_code=exit_code,
            failure=failure,
        )
        try:
            write_summary(args.summary_json, summary)
            print(f"Summary: {args.summary_json}")
            print(f"Verdict: {summary['verdict']}")
        finally:
            inbound.close_configured_topology(configured_topology)
    return 2 if summary["verdict"] == "PLAYER_PROOF_COMPLETE_LINUX_PEER_CORRELATION_PENDING" else 1


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
