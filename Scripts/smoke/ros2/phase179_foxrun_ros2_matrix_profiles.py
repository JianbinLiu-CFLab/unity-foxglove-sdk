#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Module: Scripts/smoke
# Purpose: Named Phase179 Linux-to-Unity interoperability matrix profiles.

"""Pin each certified Phase179 ROS2 interop row to one visible profile.

This module deliberately separates complementary evidence.  A Linux peer proves
that it discovered Unity's subscriptions and published the bounded messages.
An Editor or Player host proves Unity's active runtime identity and copied
values.  Neither half is a final interoperability PASS on its own.
"""

from __future__ import annotations

import argparse
import json
import pathlib
import subprocess
import sys
import time
from dataclasses import dataclass
from typing import Mapping, Sequence

import _ros2_windows_env as ros2env
import phase179_foxrun_ros2_inbound_acceptance as inbound
import phase179_foxrun_ros2_player_host as player_host
import phase179_zenoh_topology as zenoh_topology


DEFAULT_TOPIC_PREFIX = "/foxrun/phase179"
DEFAULT_MESSAGE_SET = ("string", "twist", "joy")
PROFILE_OVERRIDE_OPTIONS = ("--distro", "--rmw", "--topic-prefix", "--message-set")
WRAPPER_FILENAMES = {
    "humble-fastrtps": "phase179_humble_fastrtps_acceptance.py",
    "jazzy-fastrtps": "phase179_jazzy_fastrtps_acceptance.py",
    "lyrical-fastrtps": "phase179_lyrical_fastrtps_acceptance.py",
    "lyrical-zenoh": "phase179_lyrical_zenoh_acceptance.py",
}


class MatrixFailure(RuntimeError):
    """A stable matrix-evidence failure category without raw host diagnostics."""

    def __init__(self, category: str, message: str) -> None:
        """Initialize a portable failure category."""

        super().__init__(message)
        self.category = category


@dataclass(frozen=True)
class MatrixProfile:
    """One immutable distro/RMW row certified by Phase179."""

    profile_id: str
    distro: str
    rmw: str
    topic_prefix: str = DEFAULT_TOPIC_PREFIX
    message_set: tuple[str, ...] = DEFAULT_MESSAGE_SET


PROFILES: dict[str, MatrixProfile] = {
    "humble-fastrtps": MatrixProfile("humble-fastrtps", "humble", "rmw_fastrtps_cpp"),
    "jazzy-fastrtps": MatrixProfile("jazzy-fastrtps", "jazzy", "rmw_fastrtps_cpp"),
    "lyrical-fastrtps": MatrixProfile("lyrical-fastrtps", "lyrical", "rmw_fastrtps_cpp"),
    "lyrical-zenoh": MatrixProfile("lyrical-zenoh", "lyrical", "rmw_zenoh_cpp"),
}


def workspace_root() -> pathlib.Path:
    """Return the repository root without traversing local ROS installation junctions."""

    return inbound.workspace_root()


def validate_no_profile_overrides(argv: Sequence[str]) -> None:
    """Reject options that would make a named row describe a different transport."""

    for argument in argv:
        for option in PROFILE_OVERRIDE_OPTIONS:
            if argument == option or argument.startswith(option + "="):
                raise ValueError(f"{option} is fixed by the selected Phase179 matrix profile.")


def profile_evidence_path(
    profile: MatrixProfile,
    *,
    role: str,
    surface: str,
    workspace_root: pathlib.Path | None = None,
) -> pathlib.Path:
    """Return the profile-specific evidence path for one non-final evidence role."""

    if surface not in ("editor", "player"):
        raise ValueError("surface must be editor or player")
    stems = {
        "linux-peer": f"linux-{surface}",
        "windows-editor": "windows-editor",
        "windows-player": "windows-player",
        "correlate": f"combined-{surface}",
    }
    if role not in stems:
        raise ValueError("role must be one of linux-peer, windows-editor, windows-player, correlate")
    root = workspace_root or inbound.workspace_root()
    return root / "build" / "phase179" / profile.profile_id / f"{stems[role]}.json"


def build_linux_peer_argv(
    profile: MatrixProfile,
    *,
    surface: str,
    token: str,
    domain_id: int,
    discovery_range: str,
    summary_json: pathlib.Path,
) -> list[str]:
    """Build profile-pinned argv for the existing Linux peer helper."""

    return [
        "--distro",
        profile.distro,
        "--rmw",
        profile.rmw,
        "--domain-id",
        str(domain_id),
        "--discovery-range",
        discovery_range,
        "--topic-prefix",
        profile.topic_prefix,
        "--message-set",
        ",".join(profile.message_set),
        "--token",
        token,
        "--profile-id",
        profile.profile_id,
        "--surface",
        surface,
        "--summary-json",
        str(summary_json),
    ]


def build_windows_player_argv(
    profile: MatrixProfile,
    *,
    player: pathlib.Path,
    player_log: pathlib.Path,
    token: str,
    domain_id: int,
    discovery_range: str,
    summary_json: pathlib.Path,
) -> list[str]:
    """Build profile-pinned argv for the existing Windows Player host helper."""

    return [
        "--player",
        str(player),
        "--distro",
        profile.distro,
        "--rmw",
        profile.rmw,
        "--domain-id",
        str(domain_id),
        "--discovery-range",
        discovery_range,
        "--token",
        token,
        "--player-log",
        str(player_log),
        "--message-set",
        ",".join(profile.message_set),
        "--topic-prefix",
        profile.topic_prefix,
        "--profile-id",
        profile.profile_id,
        "--surface",
        "player",
        "--summary-json",
        str(summary_json),
    ]


def _expect(summary: Mapping[str, object], key: str, expected: object, category: str) -> None:
    """Require an exact non-secret summary field."""

    if summary.get(key) != expected:
        raise MatrixFailure(category, f"Evidence did not match the required {key}.")


def _expect_common_envelope(
    summary: Mapping[str, object],
    profile: MatrixProfile,
    *,
    role: str,
    surface: str,
    token: str,
) -> None:
    """Require the immutable evidence identity common to every half-summary."""

    _expect(summary, "phase", 179, "PHASE")
    _expect(summary, "role", role, "ROLE")
    _expect(summary, "profileId", profile.profile_id, "PROFILE")
    _expect(summary, "surface", surface, "SURFACE")
    _expect(summary, "distro", profile.distro, "DISTRO")
    _expect(summary, "rmwImplementation", profile.rmw, "RMW")
    _expect(summary, "token", token, "TOKEN")
    _expect(summary, "topicPrefix", profile.topic_prefix, "TOPIC_PREFIX")
    _expect(summary, "messageSet", list(profile.message_set), "MESSAGE_SET")
    if not isinstance(summary.get("domainId"), int):
        raise MatrixFailure("DOMAIN", "Evidence did not record a valid ROS domain id.")
    if not isinstance(summary.get("discoveryRange"), str) or not str(summary["discoveryRange"]):
        raise MatrixFailure("DISCOVERY", "Evidence did not record a discovery range.")
    topology_id = summary.get("zenohTopologyId")
    if profile.rmw == zenoh_topology.ZENOH_RMW:
        if not isinstance(topology_id, str) or not topology_id:
            raise MatrixFailure("ZENOH_TOPOLOGY", "Zenoh evidence did not carry an opaque topology identity.")
    elif topology_id is not None:
        raise MatrixFailure("ZENOH_TOPOLOGY", "FastDDS evidence must not claim a Zenoh topology identity.")


def _validate_linux_message_results(summary: Mapping[str, object], profile: MatrixProfile) -> None:
    """Require typed graph evidence and a successful bounded publication for every profile message."""

    results = summary.get("messageResults")
    if not isinstance(results, list) or [result.get("name") if isinstance(result, Mapping) else None for result in results] != list(profile.message_set):
        raise MatrixFailure("LINUX_MESSAGES", "Linux evidence did not record every required message in canonical order.")
    for name, result in zip(profile.message_set, results):
        if not isinstance(result, Mapping) or result.get("published") is not True:
            raise MatrixFailure("LINUX_PUBLICATION", "Linux evidence did not prove every bounded publication.")
        spec = inbound.MESSAGE_SPECS[name]
        graph = result.get("graph")
        if not isinstance(graph, Mapping):
            raise MatrixFailure("LINUX_GRAPH", "Linux evidence did not record the Unity graph contract.")
        expected_graph = {
            "messageType": spec.message_type,
            "qosReliability": spec.qos_reliability,
            "qosHistory": spec.qos_history,
            "qosDepth": spec.qos_depth,
            "qosDurability": spec.qos_durability,
        }
        for key, expected in expected_graph.items():
            if graph.get(key) != expected:
                raise MatrixFailure("LINUX_GRAPH", "Linux graph evidence did not match the required native contract.")
        if not isinstance(graph.get("subscriptionCount"), int) or int(graph["subscriptionCount"]) <= 0:
            raise MatrixFailure("LINUX_GRAPH", "Linux graph evidence did not prove a Unity subscription endpoint.")


def validate_linux_peer_result(
    exit_code: int,
    summary: Mapping[str, object],
    profile: MatrixProfile,
    *,
    surface: str,
    token: str,
) -> None:
    """Accept the Linux helper's documented exit 2 only for complete pending half-evidence."""

    if exit_code != 2:
        raise MatrixFailure("LINUX_EXIT", "Linux peer did not return its documented correlation-pending exit code.")
    _expect_common_envelope(summary, profile, role="linux-ros2-peer", surface=surface, token=token)
    _expect(summary, "verdict", "PEER_PUBLISH_COMPLETE_UNITY_PROOF_PENDING", "LINUX_VERDICT")
    if summary.get("unityLogProvided") is not False:
        raise MatrixFailure("LINUX_UNITY_PROOF", "The matrix Linux role must not turn local log access into a unilateral PASS.")
    _validate_linux_message_results(summary, profile)


def _validate_windows_player_summary(summary: Mapping[str, object], profile: MatrixProfile, token: str) -> None:
    """Require the Player host's fixed envelope and copied-value completion evidence."""

    _expect_common_envelope(summary, profile, role="windows-player-host", surface="player", token=token)
    _expect(summary, "verdict", "PLAYER_PROOF_COMPLETE_LINUX_PEER_CORRELATION_PENDING", "PLAYER_VERDICT")
    if summary.get("ready") is not True:
        raise MatrixFailure("PLAYER_READY", "Player evidence did not prove the requested active runtime identity.")
    if summary.get("allRequiredApplied") is not True:
        raise MatrixFailure("PLAYER_APPLIED", "Player evidence did not prove all copied values.")
    if summary.get("playerExitCode") != 0:
        raise MatrixFailure("PLAYER_EXIT", "Player evidence did not record a successful zero operating-system exit code.")


def validate_windows_player_result(
    exit_code: int,
    summary: Mapping[str, object],
    profile: MatrixProfile,
    *,
    token: str,
) -> None:
    """Accept the Player host's documented exit 2 only for complete pending Unity proof."""

    if exit_code != 2:
        raise MatrixFailure("PLAYER_HOST_EXIT", "Player host did not return its documented correlation-pending exit code.")
    _validate_windows_player_summary(summary, profile, token)


def _validate_windows_editor_summary(summary: Mapping[str, object], profile: MatrixProfile, token: str) -> None:
    """Require the Editor host's fixed envelope and copied-value completion evidence."""

    _expect_common_envelope(summary, profile, role="windows-editor-host", surface="editor", token=token)
    _expect(summary, "verdict", "WINDOWS_EDITOR_PROOF_COMPLETE_LINUX_PEER_CORRELATION_PENDING", "EDITOR_VERDICT")
    if summary.get("ready") is not True:
        raise MatrixFailure("EDITOR_READY", "Editor evidence did not prove the requested active runtime identity.")
    if summary.get("allRequiredApplied") is not True:
        raise MatrixFailure("EDITOR_APPLIED", "Editor evidence did not prove all copied values.")
    results = summary.get("messageResults")
    if not isinstance(results, list) or [result.get("name") if isinstance(result, Mapping) else None for result in results] != list(profile.message_set):
        raise MatrixFailure("EDITOR_APPLIED", "Editor evidence did not serialize every required copied-value marker.")
    for name, result in zip(profile.message_set, results):
        if not isinstance(result, Mapping):
            raise MatrixFailure("EDITOR_APPLIED", "Editor copied-value evidence had an invalid message record.")
        spec = inbound.MESSAGE_SPECS[name]
        if result.get("topic") != inbound.topic_for_spec(profile.topic_prefix, spec):
            raise MatrixFailure("EDITOR_APPLIED", "Editor copied-value evidence named an unexpected topic.")
        if result.get("value") != spec.expected_value(token):
            raise MatrixFailure("EDITOR_APPLIED", "Editor copied-value evidence did not match the fixed native payload.")
        if not isinstance(result.get("received"), int) or int(result["received"]) <= 0:
            raise MatrixFailure("EDITOR_APPLIED", "Editor copied-value evidence did not record a received message.")
        if not isinstance(result.get("applied"), int) or int(result["applied"]) <= 0:
            raise MatrixFailure("EDITOR_APPLIED", "Editor copied-value evidence did not record an applied message.")
        if int(result["applied"]) > int(result["received"]):
            raise MatrixFailure("EDITOR_APPLIED", "Editor copied-value counters were inconsistent.")
        if not isinstance(result.get("replaced"), int) or int(result["replaced"]) < 0:
            raise MatrixFailure("EDITOR_APPLIED", "Editor copied-value replacement counter was invalid.")


def correlate_summaries(
    profile: MatrixProfile,
    surface: str,
    linux_summary: Mapping[str, object],
    windows_summary: Mapping[str, object],
) -> dict[str, object]:
    """Join matching Linux and Unity half-evidence; only this function emits a final PASS verdict."""

    if surface not in ("editor", "player"):
        raise MatrixFailure("SURFACE", "Correlation surface must be editor or player.")
    token = linux_summary.get("token")
    if not isinstance(token, str) or not token:
        raise MatrixFailure("TOKEN", "Linux summary did not record a usable correlation token.")
    _expect_common_envelope(linux_summary, profile, role="linux-ros2-peer", surface=surface, token=token)
    if linux_summary.get("verdict") not in {"PEER_PUBLISH_COMPLETE_UNITY_PROOF_PENDING", "PASS"}:
        raise MatrixFailure("LINUX_VERDICT", "Linux summary was not complete publication evidence.")
    _validate_linux_message_results(linux_summary, profile)

    if surface == "player":
        _validate_windows_player_summary(windows_summary, profile, token)
    else:
        _validate_windows_editor_summary(windows_summary, profile, token)

    for key in ("phase", "profileId", "surface", "distro", "rmwImplementation", "domainId", "discoveryRange", "token", "topicPrefix", "messageSet"):
        if linux_summary.get(key) != windows_summary.get(key):
            category = "TOKEN" if key == "token" else "ENVELOPE"
            raise MatrixFailure(category, "Linux and Windows evidence did not describe the same acceptance run.")
    if profile.rmw == zenoh_topology.ZENOH_RMW and linux_summary.get("zenohTopologyId") != windows_summary.get("zenohTopologyId"):
        raise MatrixFailure("ZENOH_TOPOLOGY", "Linux and Windows evidence did not name the same Zenoh topology.")

    label = profile.profile_id.upper().replace("-", "_")
    return {
        "phase": 179,
        "profileId": profile.profile_id,
        "surface": surface,
        "distro": profile.distro,
        "rmwImplementation": profile.rmw,
        "domainId": linux_summary["domainId"],
        "discoveryRange": linux_summary["discoveryRange"],
        "token": token,
        "topicPrefix": profile.topic_prefix,
        "messageSet": list(profile.message_set),
        **({"zenohTopologyId": linux_summary["zenohTopologyId"]} if profile.rmw == zenoh_topology.ZENOH_RMW else {}),
        "verdict": f"PHASE179_{label}_{surface.upper()}_PASS",
    }


def resolve_windows_ros2_root(
    profile: MatrixProfile,
    args: object,
    *,
    workspace_root: pathlib.Path | None = None,
) -> tuple[pathlib.Path, pathlib.Path, pathlib.Path]:
    """Resolve only the repo-local Windows ROS2 entry point for Editor CLI evidence."""

    root = getattr(args, "ros2_root", None)
    root = pathlib.Path(root) if root is not None else ros2env.default_ros2_root(profile.distro, workspace_root or inbound.workspace_root())
    try:
        python, ros2_script = ros2env.validate_ros2_root(root)
    except FileNotFoundError as exc:
        raise MatrixFailure("WINDOWS_ROS2", "The selected repo-local Windows ROS2 entry point is unavailable.") from exc
    return root, python, ros2_script


def validate_windows_subscription_endpoints(
    python: pathlib.Path,
    ros2_script: pathlib.Path,
    env: dict[str, str],
    profile: MatrixProfile,
    *,
    timeout_seconds: float,
) -> list[dict[str, object]]:
    """Validate all fixed Unity subscription contracts using array-argv repo-local Windows ROS2 commands."""

    deadline = time.monotonic() + timeout_seconds
    evidence: list[dict[str, object]] = []
    for name in profile.message_set:
        spec = inbound.MESSAGE_SPECS[name]
        topic = inbound.topic_for_spec(profile.topic_prefix, spec)
        last_failure: MatrixFailure | None = None
        completed = False
        while True:
            remaining = deadline - time.monotonic()
            if remaining <= 0.0:
                break
            try:
                listed = ros2env.run_ros2(
                    python,
                    ros2_script,
                    env,
                    ["topic", "list", "-t", "--no-daemon"],
                    check=False,
                    timeout_seconds=max(0.05, min(5.0, remaining)),
                )
                if listed.returncode != 0 or not inbound.topic_list_has_type(listed.stdout, topic, spec.message_type):
                    raise MatrixFailure("WINDOWS_DISCOVERY", "Windows ROS2 CLI did not yet expose the typed Unity subscription.")
                details = ros2env.run_ros2(
                    python,
                    ros2_script,
                    env,
                    ["topic", "info", "-v", topic, "--no-daemon"],
                    check=False,
                    timeout_seconds=max(0.05, min(5.0, remaining)),
                )
                if details.returncode != 0:
                    raise MatrixFailure("WINDOWS_ENDPOINT", "Windows ROS2 CLI could not query the Unity subscription endpoint.")
                endpoint = inbound.validate_unity_subscription_endpoint(details.stdout, spec)
                evidence.append(
                    {
                        "name": name,
                        "topic": topic,
                        "messageType": endpoint.message_type,
                        "subscriptionCount": endpoint.subscription_count,
                        "qosReliability": endpoint.qos_reliability,
                        "qosHistory": endpoint.qos_history,
                        "qosDepth": endpoint.qos_depth,
                        "qosDurability": endpoint.qos_durability,
                    }
                )
                completed = True
                break
            except (MatrixFailure, inbound.AcceptanceFailure) as exc:
                last_failure = MatrixFailure(exc.category, "Windows ROS2 endpoint evidence is not yet complete.")
            except (OSError, subprocess.TimeoutExpired):
                last_failure = MatrixFailure("WINDOWS_ENDPOINT", "Windows ROS2 endpoint evidence command did not complete.")
            if time.monotonic() >= deadline:
                break
            time.sleep(min(0.25, max(0.0, deadline - time.monotonic())))
        if not completed:
            if last_failure is not None:
                raise last_failure
            raise MatrixFailure("WINDOWS_ENDPOINT", "Windows ROS2 endpoint evidence timed out.")
    return evidence


def _append_zenoh_topology_argv(argv: list[str], profile: MatrixProfile, args: argparse.Namespace) -> None:
    """Append the explicit Zenoh topology selection only for the Lyrical/Zenoh row."""

    if profile.rmw != zenoh_topology.ZENOH_RMW:
        return
    argv.extend(
        [
            "--zenoh-topology-id",
            args.zenoh_topology_id,
            "--zenoh-router-ready-marker",
            args.zenoh_router_ready_marker,
        ]
    )
    if args.zenoh_router is not None:
        argv.extend(["--zenoh-router", str(args.zenoh_router)])
    else:
        argv.append("--no-zenoh-router")


def _topology_options(profile: MatrixProfile, args: argparse.Namespace) -> zenoh_topology.ZenohTopologyOptions:
    """Resolve the profile's topology once without starting a process."""

    try:
        return zenoh_topology.validate_topology_options(
            profile.rmw,
            router=args.zenoh_router,
            no_router=args.no_zenoh_router,
            topology_id=args.zenoh_topology_id,
        )
    except zenoh_topology.ZenohTopologyError as exc:
        raise MatrixFailure(exc.category, "The selected Phase179 profile requires an explicit Zenoh topology.") from exc
    except ValueError as exc:
        raise MatrixFailure("ZENOH_TOPOLOGY", "Zenoh topology arguments do not match the selected profile.") from exc


def _read_summary(path: pathlib.Path) -> Mapping[str, object]:
    """Read one helper-written portable JSON summary without exposing raw parse diagnostics."""

    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        raise MatrixFailure("SUMMARY", "A required Phase179 evidence summary could not be read.") from exc
    if not isinstance(value, Mapping):
        raise MatrixFailure("SUMMARY", "A required Phase179 evidence summary was not a JSON object.")
    return value


def _write_summary(path: pathlib.Path, summary: Mapping[str, object]) -> None:
    """Persist only the common sanitized evidence schema outside package source directories."""

    inbound.write_summary(path, summary)


def _parser(profile: MatrixProfile) -> argparse.ArgumentParser:
    """Create the common named-profile parser without ever exposing a distro/RMW override."""

    parser = argparse.ArgumentParser(
        description=(
            f"Phase179 fixed profile {profile.profile_id} ({profile.distro}/{profile.rmw}). "
            "Use one role at a time; only correlate can emit a final PASS."
        )
    )
    parser.add_argument("--role", choices=("linux-peer", "windows-editor", "windows-player", "correlate"), required=True)
    parser.add_argument("--surface", choices=("editor", "player"), default=None)
    parser.add_argument("--domain-id", type=inbound.parse_domain_id, default=0)
    parser.add_argument("--discovery-range", default="SUBNET")
    parser.add_argument("--token", type=inbound.parse_token, default=None)
    parser.add_argument("--summary-json", type=pathlib.Path, default=None)
    parser.add_argument("--linux-summary-json", type=pathlib.Path, default=None)
    parser.add_argument("--windows-summary-json", type=pathlib.Path, default=None)
    parser.add_argument("--timeout-seconds", type=inbound.positive_seconds, default=45.0)
    parser.add_argument("--ready-timeout-seconds", type=inbound.positive_seconds, default=45.0)
    parser.add_argument("--apply-timeout-seconds", type=inbound.positive_seconds, default=45.0)
    parser.add_argument("--exit-timeout-seconds", type=inbound.positive_seconds, default=120.0)
    parser.add_argument("--string-burst-final-sequence", type=inbound.nonnegative_sequence, default=None)
    parser.add_argument("--string-burst-rate-hz", type=inbound.positive_seconds, default=500.0)
    parser.add_argument("--unity-log", type=pathlib.Path, default=None)
    parser.add_argument("--player", type=pathlib.Path, default=None)
    parser.add_argument("--player-log", type=pathlib.Path, default=None)
    parser.add_argument("--ros2-root", type=pathlib.Path, default=None)
    parser.add_argument("--zenoh-router", type=pathlib.Path, default=None)
    parser.add_argument("--no-zenoh-router", action="store_true")
    parser.add_argument("--zenoh-topology-id", type=inbound.parse_token, default=None)
    parser.add_argument("--zenoh-router-ready-marker", type=inbound.parse_ready_marker, default="Started")
    return parser


def parse_profile_args(profile: MatrixProfile, argv: Sequence[str]) -> argparse.Namespace:
    """Parse one fixed-profile command and reject role combinations that cannot yield sound evidence."""

    parser = _parser(profile)
    try:
        validate_no_profile_overrides(argv)
    except ValueError as exc:
        parser.error(str(exc))
    args = parser.parse_args(argv)

    if args.role == "windows-player":
        if args.surface not in (None, "player"):
            parser.error("--role windows-player requires --surface player when a surface is supplied")
        args.surface = "player"
    elif args.role in ("linux-peer", "windows-editor", "correlate") and args.surface is None:
        parser.error(f"--role {args.role} requires --surface editor or player")

    if args.role in ("linux-peer", "windows-editor", "windows-player") and args.token is None:
        parser.error(f"--role {args.role} requires --token for two-sided evidence correlation")
    if args.role == "windows-editor" and args.surface != "editor":
        parser.error("--role windows-editor requires --surface editor")
    if args.role == "linux-peer" and args.surface not in ("editor", "player"):
        parser.error("--role linux-peer requires --surface editor or player")
    if args.role == "windows-editor" and args.unity_log is None:
        parser.error("--role windows-editor requires --unity-log")
    if args.role == "windows-player" and (args.player is None or args.player_log is None):
        parser.error("--role windows-player requires --player and --player-log")
    if args.role == "correlate" and (args.linux_summary_json is None or args.windows_summary_json is None):
        parser.error("--role correlate requires --linux-summary-json and --windows-summary-json")
    if args.role != "correlate" and (args.linux_summary_json is not None or args.windows_summary_json is not None):
        parser.error("--linux-summary-json and --windows-summary-json are valid only with --role correlate")
    if args.role == "correlate" and (args.zenoh_router is not None or args.no_zenoh_router or args.zenoh_topology_id is not None):
        parser.error("--role correlate reads Zenoh topology identity from its two summaries; do not supply topology ownership arguments")

    if args.string_burst_final_sequence is not None and args.role not in ("linux-peer", "windows-player"):
        parser.error("--string-burst-final-sequence is supported only by linux-peer and windows-player roles")
    if args.role != "linux-peer" and args.string_burst_rate_hz != 500.0:
        parser.error("--string-burst-rate-hz is valid only with --role linux-peer")

    if args.role != "correlate":
        try:
            _topology_options(profile, args)
        except MatrixFailure as exc:
            parser.error(str(exc))

    if args.summary_json is None:
        args.summary_json = profile_evidence_path(profile, role=args.role, surface=args.surface)
    return args


def run_linux_peer(profile: MatrixProfile, args: argparse.Namespace) -> int:
    """Delegate one Linux half-evidence run while explicitly retaining its documented pending exit code."""

    child_argv = build_linux_peer_argv(
        profile,
        surface=args.surface,
        token=args.token,
        domain_id=args.domain_id,
        discovery_range=args.discovery_range,
        summary_json=args.summary_json,
    )
    child_argv.extend(["--timeout-seconds", str(args.timeout_seconds)])
    if args.string_burst_final_sequence is not None:
        child_argv.extend(
            [
                "--string-burst-final-sequence",
                str(args.string_burst_final_sequence),
                "--string-burst-rate-hz",
                str(args.string_burst_rate_hz),
            ]
        )
    _append_zenoh_topology_argv(child_argv, profile, args)
    exit_code = inbound.main(child_argv)
    try:
        validate_linux_peer_result(exit_code, _read_summary(args.summary_json), profile, surface=args.surface, token=args.token)
    except MatrixFailure as exc:
        print(f"[phase179:{profile.profile_id}] Linux half-evidence rejected: {exc.category}", file=sys.stderr, flush=True)
        return 1
    print(
        f"[phase179:{profile.profile_id}] Linux publication half-evidence complete for {args.surface}; correlation pending.",
        flush=True,
    )
    return 2


def run_windows_player(profile: MatrixProfile, args: argparse.Namespace) -> int:
    """Delegate one Player half-evidence run without injecting Windows ROS2 CLI DLL paths into the Player."""

    child_argv = build_windows_player_argv(
        profile,
        player=args.player,
        player_log=args.player_log,
        token=args.token,
        domain_id=args.domain_id,
        discovery_range=args.discovery_range,
        summary_json=args.summary_json,
    )
    child_argv.extend(
        [
            "--ready-timeout-seconds",
            str(args.ready_timeout_seconds),
            "--exit-timeout-seconds",
            str(args.exit_timeout_seconds),
        ]
    )
    if args.string_burst_final_sequence is not None:
        child_argv.extend(["--string-burst-final-sequence", str(args.string_burst_final_sequence)])
    _append_zenoh_topology_argv(child_argv, profile, args)
    exit_code = player_host.main(child_argv)
    try:
        validate_windows_player_result(exit_code, _read_summary(args.summary_json), profile, token=args.token)
    except MatrixFailure as exc:
        print(f"[phase179:{profile.profile_id}] Player half-evidence rejected: {exc.category}", file=sys.stderr, flush=True)
        return 1
    print(f"[phase179:{profile.profile_id}] Player copied-value half-evidence complete; correlation pending.", flush=True)
    return 2


def _editor_marker_evidence(profile: MatrixProfile, token: str, markers: Mapping[str, inbound.UnityMarker]) -> list[dict[str, object]]:
    """Serialize bounded copied values that were already checked against the fixed profile contract."""

    result: list[dict[str, object]] = []
    for name in profile.message_set:
        marker = markers[name]
        spec = inbound.MESSAGE_SPECS[name]
        if marker.value != spec.expected_value(token):
            raise MatrixFailure("EDITOR_APPLIED", "Editor copied-value evidence did not match the fixed profile payload.")
        result.append(
            {
                "name": name,
                "topic": marker.topic,
                "received": marker.received,
                "applied": marker.applied,
                "replaced": marker.replaced,
                "value": marker.value,
            }
        )
    return result


def run_windows_editor_host(profile: MatrixProfile, args: argparse.Namespace) -> int:
    """Collect the Windows Editor half-evidence using fresh log offsets and repo-local ROS2 CLI preflight."""

    summary: dict[str, object] = {
        "phase": 179,
        "role": "windows-editor-host",
        "profileId": profile.profile_id,
        "surface": "editor",
        "distro": profile.distro,
        "rmwImplementation": profile.rmw,
        "domainId": args.domain_id,
        "discoveryRange": args.discovery_range,
        "token": args.token,
        "topicPrefix": profile.topic_prefix,
        "messageSet": list(profile.message_set),
        "ready": False,
        "allRequiredApplied": False,
        "messageResults": [],
    }
    if profile.rmw == zenoh_topology.ZENOH_RMW:
        summary["zenohTopologyId"] = args.zenoh_topology_id
    topology_handle: zenoh_topology.ZenohTopologyHandle | None = None
    exit_code = 1
    failure_category: str | None = None
    try:
        ros2_root, python, ros2_script = resolve_windows_ros2_root(profile, args)
        env = ros2env.build_ros_env(
            ros2_root,
            profile.rmw,
            args.discovery_range,
            str(args.domain_id),
            profile.distro,
        )
        options = _topology_options(profile, args)
        topology_handle = zenoh_topology.start_topology(
            options,
            env=env,
            cwd=workspace_root(),
            log_path=args.summary_json.with_name(args.summary_json.stem + "-zenoh-router.log"),
            ready_timeout_seconds=args.ready_timeout_seconds,
            ready_marker=args.zenoh_router_ready_marker,
        )
        summary["zenohTopology"] = inbound.topology_summary(topology_handle)

        ready_offset = inbound.unity_log_offset(args.unity_log)
        inbound.wait_for_unity_ready_marker(
            args.unity_log,
            profile.distro,
            profile.rmw,
            args.token,
            args.ready_timeout_seconds,
            start_offset=ready_offset,
        )
        summary["ready"] = True
        apply_offset = inbound.unity_log_offset(args.unity_log)
        summary["endpointEvidence"] = validate_windows_subscription_endpoints(
            python,
            ros2_script,
            env,
            profile,
            timeout_seconds=args.ready_timeout_seconds,
        )
        summary["windowsRos2Preflight"] = "passed"
        print(
            f"[phase179:{profile.profile_id}] Editor READY and Windows ROS2 subscription preflight complete; "
            f"run the matching Linux peer with token {args.token}.",
            flush=True,
        )
        markers: dict[str, inbound.UnityMarker] = {}
        for name in profile.message_set:
            spec = inbound.MESSAGE_SPECS[name]
            markers[name] = inbound.wait_for_unity_marker(
                args.unity_log,
                inbound.topic_for_spec(profile.topic_prefix, spec),
                args.token,
                spec.expected_value(args.token),
                args.apply_timeout_seconds,
                start_offset=apply_offset,
            )
        summary["messageResults"] = _editor_marker_evidence(profile, args.token, markers)
        summary["allRequiredApplied"] = True
        summary["verdict"] = "WINDOWS_EDITOR_PROOF_COMPLETE_LINUX_PEER_CORRELATION_PENDING"
        exit_code = 2
    except MatrixFailure as exc:
        failure_category = exc.category
    except inbound.AcceptanceFailure as exc:
        failure_category = exc.category
    except zenoh_topology.ZenohTopologyError as exc:
        failure_category = exc.category
    except KeyboardInterrupt:
        failure_category = "INTERRUPTED"
    except (OSError, RuntimeError, subprocess.SubprocessError):
        failure_category = "ENVIRONMENT"
    finally:
        if failure_category is not None:
            summary["failureCategory"] = failure_category
            summary["verdict"] = f"FAIL_{failure_category}"
        elif "verdict" not in summary:
            summary["verdict"] = "FAIL_UNKNOWN"
        try:
            _write_summary(args.summary_json, summary)
            print(f"Summary: {args.summary_json}")
            print(f"Verdict: {summary['verdict']}")
        finally:
            if topology_handle is not None:
                zenoh_topology.close_topology(topology_handle)
    return exit_code


def run_correlation(profile: MatrixProfile, args: argparse.Namespace) -> int:
    """Read two half-summaries and write the only final matrix PASS artifact."""

    label = profile.profile_id.upper().replace("-", "_")
    output: dict[str, object]
    exit_code: int
    try:
        output = correlate_summaries(
            profile,
            args.surface,
            _read_summary(args.linux_summary_json),
            _read_summary(args.windows_summary_json),
        )
        exit_code = 0
    except MatrixFailure as exc:
        output = {
            "phase": 179,
            "profileId": profile.profile_id,
            "surface": args.surface,
            "distro": profile.distro,
            "rmwImplementation": profile.rmw,
            "verdict": f"PHASE179_{label}_{args.surface.upper()}_FAIL_{exc.category}",
            "failureCategory": exc.category,
        }
        exit_code = 1
    _write_summary(args.summary_json, output)
    print(f"Summary: {args.summary_json}")
    print(f"Verdict: {output['verdict']}")
    return exit_code


def run_profile(profile_id: str, argv: Sequence[str] | None = None) -> int:
    """Run exactly one named profile role; the wrapper never permits a distro/RMW reinterpretation."""

    try:
        profile = PROFILES[profile_id]
    except KeyError:
        print(f"Unknown Phase179 profile: {profile_id}", file=sys.stderr)
        return 1
    args = parse_profile_args(profile, list(argv or ()))
    if args.role == "linux-peer":
        return run_linux_peer(profile, args)
    if args.role == "windows-player":
        return run_windows_player(profile, args)
    if args.role == "windows-editor":
        return run_windows_editor_host(profile, args)
    return run_correlation(profile, args)
