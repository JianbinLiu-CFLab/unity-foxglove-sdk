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
values.  Neither half is a final interoperability PASS on its own.  Each
named profile wrapper also exposes a deliberately separate Windows-local
loopback command for its immediate Unity manual smoke path.
"""
from __future__ import annotations
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase179_foxrun_ros2_matrix_profiles.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
import argparse
import contextlib
import hashlib
import json
import os
import pathlib
import subprocess
import sys
import time
import uuid
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
LOCAL_EDITOR_DEFAULT_PROFILE_IDS = frozenset(
    {"humble-fastrtps", "jazzy-fastrtps", "lyrical-fastrtps", "lyrical-zenoh"}
)
LOCAL_ZENOH_TOPOLOGY_ID = "phase179-lyrical-zenoh-local-router"
WINDOWS_LOCAL_PUBLISH_MAX_WAIT_DISTROS = frozenset({"jazzy", "lyrical"})
WINDOWS_RCLPY_ENDPOINT_PROBE = pathlib.Path(__file__).with_name("phase179_windows_rclpy_endpoint_probe.py")
LOCAL_EDITOR_READY_TIMEOUT_SECONDS = 300
LOCAL_EDITOR_APPLY_TIMEOUT_SECONDS = 90
LOCAL_EDITOR_CORRELATION_MESSAGE = "string"
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
def profile_wrapper_argv(profile_id: str, argv: Sequence[str]) -> list[str]:
    """Return the user-facing wrapper argv, defaulting every named row to local Editor acceptance."""

    supplied = list(argv)
    profile = PROFILES.get(profile_id)
    if supplied or profile is None or profile_id not in LOCAL_EDITOR_DEFAULT_PROFILE_IDS:
        return supplied

    result = [
        "--role",
        "windows-local-editor",
        "--ready-timeout-seconds",
        str(LOCAL_EDITOR_READY_TIMEOUT_SECONDS),
        "--apply-timeout-seconds",
        str(LOCAL_EDITOR_APPLY_TIMEOUT_SECONDS),
    ]
    if profile.rmw == zenoh_topology.ZENOH_RMW:
        ros2_root = ros2env.default_ros2_root(profile.distro, inbound.workspace_root())
        result.extend(
            [
                "--zenoh-router",
                str(ros2_root / "Lib" / "rmw_zenoh_cpp" / "rmw_zenohd.exe"),
                "--zenoh-topology-id",
                LOCAL_ZENOH_TOPOLOGY_ID,
            ]
        )
    return result
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
        "windows-local-editor": "windows-local-editor",
        "windows-player": "windows-player",
        "correlate": f"combined-{surface}",
    }
    if role not in stems:
        raise ValueError("role must be one of linux-peer, windows-editor, windows-local-editor, windows-player, correlate")
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
    *,
    consumed_receipts_path: pathlib.Path | None = None,
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

    receipt_payload = json.dumps(
        {"linux": linux_summary, "windows": windows_summary},
        sort_keys=True,
        separators=(",", ":"),
    ).encode("utf-8")
    receipt_digest = hashlib.sha256(receipt_payload).hexdigest()
    if consumed_receipts_path is not None:
        receipt_path = pathlib.Path(consumed_receipts_path)
        try:
            consumed = json.loads(receipt_path.read_text(encoding="utf-8")) if receipt_path.exists() else []
        except (OSError, json.JSONDecodeError) as exc:
            raise MatrixFailure("RECEIPT", "The correlation receipt ledger is unavailable or malformed.") from exc
        if not isinstance(consumed, list) or any(not isinstance(item, str) for item in consumed):
            raise MatrixFailure("RECEIPT", "The correlation receipt ledger has an invalid shape.")
        if receipt_digest in consumed:
            raise MatrixFailure("REPLAY", "The exact Phase179 evidence pair was already consumed by a prior correlation.")
        receipt_path.parent.mkdir(parents=True, exist_ok=True)
        receipt_path.write_text(json.dumps([*consumed, receipt_digest], indent=2) + "\n", encoding="utf-8")

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
        "correlationReceipt": receipt_digest,
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


__all__ = [name for name in globals() if not name.startswith("__")]
