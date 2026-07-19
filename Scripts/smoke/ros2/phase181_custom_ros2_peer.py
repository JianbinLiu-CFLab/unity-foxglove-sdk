#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Module: Scripts/smoke
# Purpose: Typed peer harness for one locked Phase181 custom ROS2 envelope.

"""Build and run a peer for the locked Phase181 custom ROS2 interface.

The outer command owns only a disposable workspace below ``build/phase181``.
It never sources a shell profile, invokes a bare ``ros2`` command, or treats
one-sided graph/Unity evidence as a passing interop result.  The ``--worker``
entry point is intentionally separate so the peer uses the pinned Python from
the selected ROS2 distribution after the exact interface source is built.
"""

from __future__ import annotations

import argparse
import hashlib
import importlib
import json
import os
import pathlib
import re
import shutil
import subprocess
import sys
import time
import uuid
from dataclasses import dataclass
from typing import Mapping, Sequence

import _ros2_windows_env as ros2env
import phase181_custom_ros2_peer_protocol as protocol


STATIC_INTERFACE_PACKAGE_ID = "dev.unity2foxglove.foxrun.ros2.interfaces"
ROS_PACKAGE_NAME = "unity2foxglove_foxrun_interfaces_v1"
LOCK_RELATIVE_PATH = pathlib.PurePosixPath("RuntimeSupport/foxrun-ros2-interface-lock.json")
DEFAULT_TOPIC_PREFIX = "/foxrun/phase181/custom"
DEFAULT_TOPICS = {
    "publish": DEFAULT_TOPIC_PREFIX + "/publish",
    "subscribe": DEFAULT_TOPIC_PREFIX + "/subscribe",
    "bidirectional": DEFAULT_TOPIC_PREFIX + "/bidirectional",
}
PROBE_ROLES = ("correlate", "publisher", "subscriber", "bidirectional", "orchestrate")
_SHA256 = re.compile(r"^[0-9a-f]{64}$")
_PROFILE_ID = re.compile(r"^[a-z0-9][a-z0-9-]{0,63}$")
OWNERSHIP_MARKER_NAME = ".phase181-peer-owned"
_OWNERSHIP_MARKER_CONTENT = "phase181-peer-workspace-v1\n"


class PeerFailure(protocol.ProtocolFailure):
    """Stable peer failure category that remains safe to persist in a summary."""


@dataclass(frozen=True)
class StaticInterfaceLock:
    """The immutable interface facts required by every peer and Unity surface."""

    ros_package_name: str
    interface_revision: int
    interface_digest: str
    payload_message_name: str
    envelope_message_name: str


@dataclass(frozen=True)
class WindowsPeerToolchain:
    """Pinned Windows ROS2 executables required to build one disposable peer."""

    ros2_root: pathlib.Path
    python_executable: pathlib.Path
    colcon_executable: pathlib.Path


def workspace_root() -> pathlib.Path:
    """Find the repository root without expanding local ROS junctions."""

    for candidate in (pathlib.Path(__file__).resolve().parent, *pathlib.Path(__file__).resolve().parents):
        if (candidate / "Packages").is_dir() and (candidate / "Scripts").is_dir():
            return candidate
    return pathlib.Path.cwd()


def default_static_interface_package(root: pathlib.Path | None = None) -> pathlib.Path:
    """Return the tracked static source package, never a generated runtime add-on."""

    return (root or workspace_root()) / "Packages" / STATIC_INTERFACE_PACKAGE_ID


def resolve_windows_peer_toolchain(ros2_root: pathlib.Path) -> WindowsPeerToolchain:
    """Resolve only the selected repository-local ROS2 Python and colcon executable."""

    root = pathlib.Path(ros2_root)
    try:
        python_executable, _ = ros2env.validate_ros2_root(root)
    except FileNotFoundError as exc:
        raise PeerFailure("FAIL_PEER_TOOLCHAIN", "The selected repository-local Windows ROS2 root is unavailable.") from exc
    colcon_executable = root / ".pixi" / "envs" / "default" / "Scripts" / "colcon.exe"
    if not colcon_executable.is_file():
        raise PeerFailure("FAIL_PEER_TOOLCHAIN", "The selected ROS2 environment does not contain the required colcon executable.")
    return WindowsPeerToolchain(root, python_executable, colcon_executable)


def build_addon_validator_command(repository: pathlib.Path, distro: str, rmw: str) -> list[str]:
    """Build the bounded add-on preflight command for exactly one profile."""

    if distro not in {"humble", "jazzy", "lyrical"} or not rmw.startswith("rmw_"):
        raise PeerFailure("FAIL_TYPESUPPORT_PREFLIGHT", "The custom typesupport profile is not valid.")
    validator = pathlib.Path(repository) / "Scripts" / "ros2forunity" / "interfaces" / "validate_foxrun_custom_typesupport_addon.py"
    if not validator.is_file():
        raise PeerFailure("FAIL_TYPESUPPORT_PREFLIGHT", "The custom typesupport validator is unavailable.")
    return [sys.executable, str(validator), "--distro", distro, "--require-rmw", rmw]


def require_selected_typesupport_addon(repository: pathlib.Path, distro: str) -> str:
    """Require the Unity project to select exactly the matching runtime/add-on pair."""

    if distro not in {"humble", "jazzy", "lyrical"}:
        raise PeerFailure("FAIL_TYPESUPPORT_SELECTION", "The selected ROS2 distribution is not supported by the custom add-on.")
    manifest_path = pathlib.Path(repository) / "Unity2Foxglove" / "Packages" / "manifest.json"
    try:
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        raise PeerFailure("FAIL_TYPESUPPORT_SELECTION", "Unity's package manifest is unavailable or malformed.") from exc
    dependencies = manifest.get("dependencies") if isinstance(manifest, Mapping) else None
    if not isinstance(dependencies, Mapping):
        raise PeerFailure("FAIL_TYPESUPPORT_SELECTION", "Unity's package manifest has no dependency map.")
    runtime = "dev.unity2foxglove.ros2forunity.runtime." + distro + ".win64"
    expected = "dev.unity2foxglove.foxrun.ros2.interfaces.typesupport." + distro + ".win64"
    prefix = "dev.unity2foxglove.foxrun.ros2.interfaces.typesupport."
    active = sorted(name for name in dependencies if isinstance(name, str) and name.startswith(prefix))
    if runtime not in dependencies or active != [expected]:
        raise PeerFailure("FAIL_TYPESUPPORT_SELECTION", "Unity has not selected exactly one matching custom typesupport add-on.")
    return expected


def run_logged_owned_command(
    command: Sequence[str],
    *,
    cwd: pathlib.Path,
    env: Mapping[str, str],
    log_path: pathlib.Path,
    timeout_seconds: float,
    failure_code: str,
    runner=None,
) -> None:
    """Run one helper-owned command with bounded output retained only in owned storage."""

    if not command or timeout_seconds <= 0.0 or not failure_code.startswith("FAIL_"):
        raise ValueError("Phase181 owned commands require a command, positive timeout, and stable failure code.")
    execute = runner or subprocess.run
    try:
        pathlib.Path(log_path).parent.mkdir(parents=True, exist_ok=True)
        with pathlib.Path(log_path).open("w", encoding="utf-8", errors="replace") as log_stream:
            result = execute(
                list(command),
                cwd=str(cwd),
                env=dict(env),
                stdout=log_stream,
                stderr=subprocess.STDOUT,
                text=True,
                check=False,
                timeout=timeout_seconds,
                shell=False,
            )
    except subprocess.TimeoutExpired as exc:
        raise PeerFailure(failure_code, "A helper-owned command did not finish before its bounded timeout.") from exc
    except OSError as exc:
        raise PeerFailure(failure_code, "A helper-owned command could not be started.") from exc
    if result.returncode != 0:
        raise PeerFailure(failure_code, "A helper-owned command returned a nonzero status.")


def worker_launch_options(platform_name: str | None = None) -> dict[str, object]:
    """Create a private process group for a helper-owned worker only."""

    if (platform_name or os.name) == "nt":
        return {"creationflags": int(getattr(subprocess, "CREATE_NEW_PROCESS_GROUP", 0))}
    return {"start_new_session": True}


def read_successful_worker_result(path: pathlib.Path, lock: StaticInterfaceLock) -> Mapping[str, object]:
    """Accept only one complete worker result for the exact locked interface digest."""

    try:
        parsed = json.loads(pathlib.Path(path).read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        raise PeerFailure("FAIL_PEER_RESULT", "The helper-owned typed worker did not produce a valid result summary.") from exc
    if not isinstance(parsed, Mapping):
        raise PeerFailure("FAIL_PEER_RESULT", "The helper-owned typed worker result was not an object.")
    try:
        protocol.require_interface_digest(lock.interface_digest, parsed.get("interfaceDigest", ""))
    except protocol.ProtocolFailure as exc:
        raise PeerFailure(exc.code, "The typed worker result did not match the locked interface digest.") from exc
    verdict = parsed.get("verdict")
    if verdict == "PASS":
        return parsed
    if isinstance(verdict, str) and verdict.startswith("FAIL_"):
        raise PeerFailure(verdict, "The helper-owned typed worker did not complete every required proof.")
    raise PeerFailure("FAIL_PEER_RESULT", "The helper-owned typed worker emitted an invalid verdict.")


def require_matching_unity_readiness(
    ready: protocol.UnityMarker,
    interface_ready: protocol.UnityMarker,
    lock: StaticInterfaceLock,
    distro: str,
    rmw: str,
    expected_token: str | None = None,
) -> str:
    """Return Unity's correlation token only for the selected runtime and static source."""

    token = ready.fields.get("token")
    if not _safe_marker_token(token) or interface_ready.fields.get("token") != token:
        raise PeerFailure("FAIL_READY_TOKEN", "Unity custom-interface readiness did not provide one usable correlation token.")
    if expected_token is not None and token != expected_token:
        raise PeerFailure("FAIL_READY_TOKEN", "Unity Player readiness did not report the helper-owned correlation token.")
    if ready.fields.get("runtime") != distro or ready.fields.get("rmw") != rmw:
        raise PeerFailure("FAIL_RUNTIME_IDENTITY", "Unity readiness did not report the selected ROS2 runtime and RMW.")
    if interface_ready.fields.get("digest") != protocol.digest_prefix(lock.interface_digest):
        raise PeerFailure("FAIL_INTERFACE_DIGEST", "Unity readiness did not report the locked custom interface digest prefix.")
    return token


def worker_phase_deadline(
    run_token: str | None,
    ready_deadline: float,
    apply_deadline: float | None,
) -> float:
    """Use the full readiness window first, then a separate full apply window."""

    if run_token is None:
        return ready_deadline
    if apply_deadline is None:
        raise PeerFailure("FAIL_STATE_TRANSITION", "The custom probe window was not armed after Unity correlation.")
    return apply_deadline


def load_static_interface_lock(static_interface_package: pathlib.Path) -> StaticInterfaceLock:
    """Load the single locked custom envelope identity and fail closed on drift."""

    lock_path = pathlib.Path(static_interface_package) / LOCK_RELATIVE_PATH
    try:
        raw = json.loads(lock_path.read_text(encoding="utf-8"))
    except (OSError, UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise PeerFailure("FAIL_INTERFACE_DIGEST", "The static interface lock is unavailable or malformed.") from exc
    if not isinstance(raw, Mapping):
        raise PeerFailure("FAIL_INTERFACE_DIGEST", "The static interface lock is not an object.")
    digest = raw.get("interfaceDigest")
    package_name = raw.get("rosPackageName")
    revision = raw.get("interfaceRevision")
    contracts = raw.get("contracts")
    if (
        raw.get("unityPackageId") != STATIC_INTERFACE_PACKAGE_ID
        or package_name != ROS_PACKAGE_NAME
        or not isinstance(revision, int)
        or revision <= 0
        or not isinstance(digest, str)
        or _SHA256.fullmatch(digest) is None
        or not isinstance(contracts, list)
        or len(contracts) != 1
        or not isinstance(contracts[0], Mapping)
    ):
        raise PeerFailure("FAIL_INTERFACE_DIGEST", "The static interface identity is incomplete.")
    contract = contracts[0]
    payload_name = contract.get("payloadMessageName")
    envelope_name = contract.get("envelopeMessageName")
    if not isinstance(payload_name, str) or not payload_name or not isinstance(envelope_name, str) or not envelope_name:
        raise PeerFailure("FAIL_INTERFACE_DIGEST", "The static interface message identity is incomplete.")
    if compute_static_source_digest(static_interface_package) != digest:
        raise PeerFailure("FAIL_INTERFACE_DIGEST", "The static interface lock does not match its source package.")
    return StaticInterfaceLock(
        ros_package_name=package_name,
        interface_revision=revision,
        interface_digest=digest,
        payload_message_name=payload_name,
        envelope_message_name=envelope_name,
    )


def _append_digest_frame(digest: "hashlib._Hash", content: bytes) -> None:
    """Append the same public length framing used by the static interface package lock."""

    digest.update(len(content).to_bytes(8, byteorder="big", signed=False))
    digest.update(content)


def compute_static_source_digest(static_interface_package: pathlib.Path) -> str:
    """Compute the exact public static-source digest without loading ROS or Unity."""

    root = pathlib.Path(static_interface_package)
    lock_relative = LOCK_RELATIVE_PATH.as_posix()
    files: list[tuple[str, bytes]] = []
    try:
        candidates = list(root.rglob("*"))
    except OSError as exc:
        raise PeerFailure("FAIL_INTERFACE_DIGEST", "The static interface source cannot be enumerated.") from exc
    for candidate in candidates:
        if not candidate.is_file():
            continue
        relative = candidate.relative_to(root).as_posix()
        if relative == lock_relative or relative.endswith(".meta"):
            continue
        try:
            text = candidate.read_text(encoding="utf-8")
        except (OSError, UnicodeDecodeError) as exc:
            raise PeerFailure("FAIL_INTERFACE_DIGEST", "The static interface source is not valid UTF-8 text.") from exc
        if text.startswith("\ufeff"):
            raise PeerFailure("FAIL_INTERFACE_DIGEST", "The static interface source contains an unsupported byte-order mark.")
        files.append((relative, text.replace("\r\n", "\n").replace("\r", "\n").encode("utf-8")))
    if not files or len({relative.lower() for relative, _ in files}) != len(files):
        raise PeerFailure("FAIL_INTERFACE_DIGEST", "The static interface source is empty or has ambiguous paths.")
    digest = hashlib.sha256()
    _append_digest_frame(digest, b"unity2foxglove:foxrun-ros2-interface-digest:v1")
    _append_digest_frame(digest, b"1")
    for relative, content in sorted(files, key=lambda item: item[0]):
        _append_digest_frame(digest, relative.encode("utf-8"))
        _append_digest_frame(digest, content)
    return digest.hexdigest()


def _require_owned_workspace_path(path: pathlib.Path, build_root: pathlib.Path) -> pathlib.Path:
    """Accept exactly one named peer-workspace child below the caller's build root."""

    candidate = pathlib.Path(path).resolve()
    root = pathlib.Path(build_root).resolve()
    try:
        relative = candidate.relative_to(root)
    except ValueError as exc:
        raise PeerFailure("FAIL_PEER_WORKSPACE", "The peer workspace must remain below the Phase181 build root.") from exc
    if len(relative.parts) != 2 or relative.parts[1] != "peer-workspace" or _PROFILE_ID.fullmatch(relative.parts[0]) is None:
        raise PeerFailure("FAIL_PEER_WORKSPACE", "The peer workspace path is not a named owned profile workspace.")
    return candidate


def prepare_owned_workspace(build_root: pathlib.Path, profile_id: str) -> pathlib.Path:
    """Create a fresh, marked workspace and remove only a previously marked equivalent one."""

    if _PROFILE_ID.fullmatch(profile_id) is None:
        raise PeerFailure("FAIL_PEER_WORKSPACE", "The profile identifier is not safe for an owned workspace path.")
    root = pathlib.Path(build_root).resolve()
    workspace = _require_owned_workspace_path(root / profile_id / "peer-workspace", root)
    if workspace.exists():
        cleanup_owned_workspace(workspace, root)
    try:
        workspace.mkdir(parents=True, exist_ok=False)
        (workspace / OWNERSHIP_MARKER_NAME).write_text(_OWNERSHIP_MARKER_CONTENT, encoding="utf-8")
    except OSError as exc:
        raise PeerFailure("FAIL_PEER_WORKSPACE", "The owned peer workspace could not be prepared.") from exc
    return workspace


def cleanup_owned_workspace(workspace: pathlib.Path, build_root: pathlib.Path) -> None:
    """Delete only a workspace created by :func:`prepare_owned_workspace`."""

    candidate = _require_owned_workspace_path(workspace, build_root)
    marker = candidate / OWNERSHIP_MARKER_NAME
    try:
        owned = marker.read_text(encoding="utf-8") == _OWNERSHIP_MARKER_CONTENT
    except OSError:
        owned = False
    if not owned:
        raise PeerFailure("FAIL_PEER_WORKSPACE", "Refusing to delete a workspace without the Phase181 ownership marker.")
    try:
        shutil.rmtree(candidate)
    except OSError as exc:
        raise PeerFailure("FAIL_PEER_WORKSPACE", "The owned peer workspace could not be cleaned up.") from exc


def build_colcon_command(colcon: pathlib.Path, ros_package_name: str) -> list[str]:
    """Build only the locked interface package with an explicit colcon executable."""

    if not ros_package_name or "/" in ros_package_name or "\\" in ros_package_name:
        raise PeerFailure("FAIL_PEER_SOURCE", "The ROS package name is not safe for an explicit colcon selection.")
    return [str(pathlib.Path(colcon)), "build", "--merge-install", "--packages-select", ros_package_name]


def stage_locked_ros_source(
    static_interface_package: pathlib.Path,
    workspace: pathlib.Path,
    ros_package_name: str,
) -> pathlib.Path:
    """Copy the locked ``Ros2Package~`` source into a fresh caller-owned workspace."""

    source = pathlib.Path(static_interface_package) / "Ros2Package~"
    destination = pathlib.Path(workspace) / "src" / ros_package_name
    if not source.is_dir() or not (source / "package.xml").is_file() or not (source / "CMakeLists.txt").is_file():
        raise PeerFailure("FAIL_PEER_SOURCE", "The locked ROS source package is missing required build inputs.")
    if destination.exists():
        raise PeerFailure("FAIL_PEER_SOURCE", "The owned peer workspace already contains a source package.")
    try:
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copytree(source, destination, ignore=shutil.ignore_patterns("*.meta", "bin", "obj", "build", "install", "log"))
    except OSError as exc:
        raise PeerFailure("FAIL_PEER_SOURCE", "The locked ROS source package could not be staged.") from exc
    return destination


def build_peer_environment(
    source: Mapping[str, str],
    ros2_root: pathlib.Path,
    workspace_install: pathlib.Path,
    *,
    distro: str,
    rmw: str,
    domain_id: int,
    topology_id: str | None = None,
) -> dict[str, str]:
    """Build an explicit secret-free environment for one owned peer workspace."""

    root = pathlib.Path(ros2_root)
    install = pathlib.Path(workspace_install)
    env = ros2env.sanitized_subprocess_env(dict(source))
    existing_path = env.get("PATH", "")
    existing_pythonpath = env.get("PYTHONPATH", "")
    prefixes = [str(install), str(root)]
    env["AMENT_PREFIX_PATH"] = os.pathsep.join(prefixes)
    env["CMAKE_PREFIX_PATH"] = os.pathsep.join(prefixes)
    env["COLCON_PREFIX_PATH"] = os.pathsep.join(prefixes)
    env["PYTHONPATH"] = os.pathsep.join(
        [str(install / "Lib" / "site-packages"), str(root / "Lib" / "site-packages"), existing_pythonpath]
    ).strip(os.pathsep)
    env["PATH"] = os.pathsep.join([str(install / "bin"), str(install / "Lib"), str(root / "bin"), existing_path]).strip(os.pathsep)
    env["ROS_VERSION"] = "2"
    env["ROS_PYTHON_VERSION"] = "3"
    env["ROS_DISTRO"] = distro
    env["RMW_IMPLEMENTATION"] = rmw
    env["ROS_DOMAIN_ID"] = str(domain_id)
    env.pop("ROS_LOCALHOST_ONLY", None)
    env.pop("ROS_DISCOVERY_SERVER", None)
    if topology_id:
        env["UNITY2FOXGLOVE_ZENOH_TOPOLOGY_ID"] = topology_id
    return env


def build_player_environment(
    source: Mapping[str, str],
    *,
    distro: str,
    rmw: str,
    domain_id: int,
    interface_revision: int,
    interface_digest: str,
    topology_id: str | None = None,
    discovery_range: str = "SUBNET",
) -> dict[str, str]:
    """Build the Player's explicit, non-secret custom-interface environment.

    The Player receives only stable transport selection and static-interface
    identity.  Its opaque run token is passed as a command-line argument to
    the acceptance component and is deliberately excluded from this map and
    every persisted summary.
    """

    if distro not in {"humble", "jazzy", "lyrical"} or not rmw.startswith("rmw_"):
        raise PeerFailure("FAIL_ARGUMENTS", "The Player profile does not identify one supported ROS2 runtime and RMW.")
    if not isinstance(domain_id, int) or domain_id < 0 or domain_id > 232:
        raise PeerFailure("FAIL_ARGUMENTS", "The Player ROS domain id is outside the supported range.")
    if not isinstance(interface_revision, int) or interface_revision <= 0:
        raise PeerFailure("FAIL_INTERFACE_DIGEST", "The Player interface revision is invalid.")
    protocol.require_interface_digest(interface_digest, interface_digest)
    if discovery_range not in {"LOCALHOST", "SUBNET", "SYSTEM_DEFAULT", "OFF"}:
        raise PeerFailure("FAIL_ARGUMENTS", "The Player discovery range is not a supported explicit value.")
    if topology_id is not None and not _safe_marker_token(topology_id):
        raise PeerFailure("FAIL_ZENOH_TOPOLOGY", "The Player Zenoh topology identity is not a safe bounded token.")

    env = ros2env.sanitized_subprocess_env(dict(source))
    # A Player launch is not allowed to quietly inherit a previous run's
    # discovery/configuration selection.  The outer wrapper owns the topology
    # choice and passes only its opaque ID here.
    for key in (
        "ROS_LOCALHOST_ONLY",
        "ROS_DISCOVERY_SERVER",
        "ZENOH_SESSION_CONFIG_URI",
        "ZENOH_CONFIG_OVERRIDE",
    ):
        env.pop(key, None)
    env["ROS_VERSION"] = "2"
    env["ROS_PYTHON_VERSION"] = "3"
    env["ROS_DISTRO"] = distro
    env["RMW_IMPLEMENTATION"] = rmw
    env["ROS_DOMAIN_ID"] = str(domain_id)
    env["ROS_AUTOMATIC_DISCOVERY_RANGE"] = discovery_range
    env["UNITY2FOXGLOVE_FOXRUN_INTERFACE_REVISION"] = str(interface_revision)
    env["UNITY2FOXGLOVE_FOXRUN_INTERFACE_DIGEST"] = interface_digest
    if topology_id is not None:
        env["UNITY2FOXGLOVE_ZENOH_TOPOLOGY_ID"] = topology_id
    else:
        env.pop("UNITY2FOXGLOVE_ZENOH_TOPOLOGY_ID", None)
    return env


def build_player_command(
    player: pathlib.Path,
    player_log: pathlib.Path,
    token: str,
    timeout_seconds: float,
) -> list[str]:
    """Build one direct Player argv with bounded auto-quit and no shell."""

    if not _safe_marker_token(token):
        raise PeerFailure("FAIL_READY_TOKEN", "The Player correlation token is not safe.")
    timeout = _require_positive_timeout(timeout_seconds, "The Player auto-quit timeout")
    return [
        str(pathlib.Path(player)),
        "-batchmode",
        "-nographics",
        "-logFile",
        str(pathlib.Path(player_log)),
        "--phase181-custom-ros2-player-auto-quit",
        "--phase181-custom-ros2-token",
        token,
        "--phase181-custom-ros2-timeout-seconds",
        format(timeout, "g"),
    ]


def require_player_exit_code(exit_code: int | None) -> None:
    """Accept only the Player's explicit zero success exit after terminal proof."""

    if exit_code != 0:
        raise PeerFailure("FAIL_PLAYER_EXIT", "The Player did not exit with its required zero success code.")


def build_worker_command(
    python_executable: pathlib.Path,
    *,
    role: str,
    probe_role: str = "orchestrate",
    surface: str = "editor",
    workspace: pathlib.Path,
    interface_digest: str,
    token: str,
    unity_log: pathlib.Path | None = None,
    result_json: pathlib.Path | None = None,
    distro: str | None = None,
    rmw: str | None = None,
    domain_id: int | None = None,
    unity_log_offset: int | None = None,
    static_interface_package: pathlib.Path | None = None,
    ready_timeout_seconds: float | None = None,
    apply_timeout_seconds: float | None = None,
) -> list[str]:
    """Build the pinned-Python worker command without an ambient ROS CLI lookup."""

    protocol.require_interface_digest(interface_digest, interface_digest)
    _normalize_probe_role(probe_role)
    if surface not in {"editor", "player"}:
        raise PeerFailure("FAIL_ARGUMENTS", "The worker surface must be editor or player.")
    command = [
        str(python_executable),
        str(pathlib.Path(__file__).resolve()),
        "--worker",
        "--role",
        role,
        "--probe-role",
        probe_role,
        "--surface",
        surface,
        "--workspace",
        str(workspace),
        "--interface-digest",
        interface_digest,
        "--token",
        token,
    ]
    if unity_log is not None:
        command.extend(["--unity-log", str(unity_log)])
    if result_json is not None:
        command.extend(["--worker-result-json", str(result_json)])
    if distro is not None:
        command.extend(["--distro", distro])
    if rmw is not None:
        command.extend(["--rmw", rmw])
    if domain_id is not None:
        command.extend(["--domain-id", str(domain_id)])
    if unity_log_offset is not None:
        command.extend(["--unity-log-offset", str(unity_log_offset)])
    if static_interface_package is not None:
        command.extend(["--static-interface-package", str(static_interface_package)])
    if ready_timeout_seconds is not None:
        command.extend(["--ready-timeout-seconds", str(ready_timeout_seconds)])
    if apply_timeout_seconds is not None:
        command.extend(["--apply-timeout-seconds", str(apply_timeout_seconds)])
    return command


def _normalize_probe_role(value: str) -> str:
    """Accept one bounded named direction role for a shared generated peer."""

    if value not in PROBE_ROLES:
        raise PeerFailure("FAIL_ARGUMENTS", "The custom-interface peer role is not supported.")
    return value


def _role_requires(role: str) -> tuple[bool, bool, bool]:
    """Return whether a role needs PublishOnly, P&S, and null/empty proof."""

    normalized = _normalize_probe_role(role)
    return (
        normalized in {"subscriber", "orchestrate"},
        normalized in {"bidirectional", "orchestrate"},
        normalized in {"bidirectional", "orchestrate"},
    )


def classify_evidence(evidence: Mapping[str, object], probe_role: str = "orchestrate") -> str:
    """Return PASS only for the exact directional proof owned by a peer role."""

    requires_outbound, requires_bidirectional, requires_nullable_empty = _role_requires(probe_role)
    if evidence.get("interfaceDigestMatches") is not True:
        return "FAIL_INTERFACE_DIGEST"
    if evidence.get("graphEvidence") is not True:
        return "FAIL_GRAPH_EVIDENCE"
    if evidence.get("inboundApplied") is not True:
        return "FAIL_REMOTE_APPLY"
    if requires_outbound and evidence.get("outboundObserved") is not True:
        return "FAIL_OUTBOUND_EVIDENCE"
    if requires_bidirectional and evidence.get("sameOriginDropped") is not True:
        return "FAIL_SAME_ORIGIN"
    if requires_bidirectional and evidence.get("remoteOriginApplied") is not True:
        return "FAIL_REMOTE_APPLY"
    if requires_nullable_empty and evidence.get("nullableEmptyObserved") is not True:
        return "FAIL_PAYLOAD_SHAPE"
    if evidence.get("unityTerminalPass") is not True:
        return "FAIL_UNITY_EVIDENCE"
    if evidence.get("cleanStop") is not True:
        return "FAIL_CLEAN_STOP"
    return "PASS"


def can_complete_live_evidence(evidence: Mapping[str, object], probe_role: str = "orchestrate") -> bool:
    """Check business proof before the worker's finally block proves clean stop.

    The final endpoint teardown is intentionally performed after the probe loop.
    Treating that future teardown fact as a prerequisite for leaving the loop
    creates a self-deadlocking timeout even when all typed-direction evidence
    is already present.
    """

    completion_view = dict(evidence)
    completion_view["cleanStop"] = True
    return classify_evidence(completion_view, probe_role) == "PASS"


def has_role_transport_evidence(evidence: Mapping[str, object], probe_role: str) -> bool:
    """Check the directional transport facts before terminal/teardown proof exists."""

    completion_view = dict(evidence)
    completion_view["unityTerminalPass"] = True
    completion_view["cleanStop"] = True
    return classify_evidence(completion_view, probe_role) == "PASS"


def custom_payload_fields(token: str, *, null_empty: bool) -> dict[str, object]:
    """Return the exact ordinary DTO cases that the generated envelope must preserve."""

    if not token or len(token) > 96:
        raise PeerFailure("FAIL_PAYLOAD_SHAPE", "The correlation token is not safe for the bounded custom DTO probe.")
    if null_empty:
        return {
            "count": 182,
            "kind": 1,
            "message": "",
            "has_message": True,
            "bytes": [],
            "has_bytes": True,
            "values": [],
            "has_values": True,
            "nested": {"enabled": False, "label": ""},
            "has_nested": False,
            "optional_count": 0,
            "has_optional_count": False,
            "optional_text": "",
            "has_optional_text": False,
        }
    return {
        "count": 181,
        "kind": 1,
        "message": token,
        "has_message": True,
        "bytes": [0x18, 0x01, 0x81],
        "has_bytes": True,
        "values": [181, 182, 183],
        "has_values": True,
        "nested": {"enabled": True, "label": token},
        "has_nested": True,
        "optional_count": 181,
        "has_optional_count": True,
        "optional_text": token,
        "has_optional_text": True,
    }


def is_peer_remote_origin(origin: str, token: str) -> bool:
    """Recognize only origins emitted by this peer, including its null/empty probe."""

    return origin in {"remote-" + token, "remote-final-" + token}


def write_worker_result(path: pathlib.Path, result: Mapping[str, object]) -> None:
    """Write one atomic, redacted worker result usable by its owning outer helper."""

    protocol.write_summary_atomic(path, result)


def _safe_marker_token(value: str | None) -> bool:
    """Accept only the local opaque token grammar shared with the Unity component."""

    if not isinstance(value, str) or not value or len(value) > 96:
        return False
    return all(character.isalnum() or character in "-_." for character in value)


def _matching_marker(
    markers: Sequence[protocol.UnityMarker],
    name: str,
    token: str | None = None,
    topic: str | None = None,
) -> protocol.UnityMarker | None:
    """Find a marker with the same generated run token and, if needed, topic."""

    for marker in markers:
        if marker.name != name:
            continue
        if token is not None and marker.fields.get("token") != token:
            continue
        if topic is not None and marker.fields.get("topic") != topic:
            continue
        return marker
    return None


def _append_unique_markers(
    target: list[protocol.UnityMarker],
    seen: set[str],
    appended: Sequence[protocol.UnityMarker],
) -> None:
    """Keep one ordered record for each exact marker line across log polling."""

    for marker in appended:
        if marker.raw in seen:
            continue
        seen.add(marker.raw)
        target.append(marker)


def observe_no_late_unity_apply(
    unity_log: pathlib.Path,
    offset: int,
    token: str,
    *,
    observation_seconds: float = 0.25,
) -> tuple[bool, int]:
    """Observe one bounded post-stop window for a correlated late Unity apply."""

    if not _safe_marker_token(token):
        raise PeerFailure("FAIL_READY_TOKEN", "The post-stop Unity marker token is invalid.")
    if observation_seconds < 0.0 or observation_seconds > 5.0:
        raise PeerFailure("FAIL_ARGUMENTS", "The post-stop observation window is outside the bounded acceptance range.")
    marker_offset = offset
    deadline = time.monotonic() + observation_seconds
    while True:
        markers, marker_offset = protocol.read_new_markers(unity_log, marker_offset)
        if any(
            marker.name == "PHASE181_CUSTOM_ROS2_APPLIED" and marker.fields.get("token") == token
            for marker in markers
        ):
            return False, marker_offset
        remaining = deadline - time.monotonic()
        if remaining <= 0.0:
            return True, marker_offset
        time.sleep(min(0.05, remaining))


def _load_generated_message_types(lock: StaticInterfaceLock):
    """Load generated Python classes only inside the selected ROS2 worker process."""

    module = importlib.import_module(lock.ros_package_name + ".msg")
    try:
        return (
            getattr(module, lock.envelope_message_name),
            getattr(module, lock.payload_message_name),
            getattr(module, "Phase181NestedState3281D0E21244"),
        )
    except AttributeError as exc:
        raise PeerFailure("FAIL_INTERFACE_DIGEST", "The peer workspace does not expose the locked generated message classes.") from exc


def create_typed_worker_endpoints(
    rclpy_module,
    envelope_type,
    node_name: str,
    qos,
    publish_topic: str,
    subscribe_topic: str,
    bidirectional_topic: str,
):
    """Create one peer node and dispose it if endpoint setup only partially succeeds."""

    node = rclpy_module.create_node(node_name)
    received_publish: list[object] = []
    received_bidirectional: list[object] = []
    try:
        node.create_subscription(envelope_type, publish_topic, received_publish.append, qos)
        node.create_subscription(envelope_type, bidirectional_topic, received_bidirectional.append, qos)
        subscribe_publisher = node.create_publisher(envelope_type, subscribe_topic, qos)
        bidirectional_publisher = node.create_publisher(envelope_type, bidirectional_topic, qos)
    except BaseException as exc:
        try:
            node.destroy_node()
        except Exception:  # noqa: BLE001 - retain the setup failure as the bounded worker cause.
            pass
        if isinstance(exc, PeerFailure) or not isinstance(exc, Exception):
            raise
        raise PeerFailure("FAIL_PEER_RUNTIME", "The typed peer could not create every generated endpoint.") from exc
    return node, received_publish, received_bidirectional, subscribe_publisher, bidirectional_publisher


def _assign_payload(payload, nested_type, fields: Mapping[str, object]) -> None:
    """Assign the locked ordinary DTO graph to one generated payload message."""

    payload.count = int(fields["count"])
    payload.kind = int(fields["kind"])
    payload.message = str(fields["message"])
    payload.foxrun_has_message = bool(fields["has_message"])
    payload.bytes = list(fields["bytes"])
    payload.foxrun_has_bytes = bool(fields["has_bytes"])
    payload.values = list(fields["values"])
    payload.foxrun_has_values = bool(fields["has_values"])
    nested_fields = fields["nested"]
    nested = nested_type()
    nested.enabled = bool(nested_fields["enabled"])
    nested.label = str(nested_fields["label"])
    nested.foxrun_has_label = bool(fields["has_nested"])
    payload.nested = nested
    payload.foxrun_has_nested = bool(fields["has_nested"])
    payload.optional_count = int(fields["optional_count"])
    payload.foxrun_has_optional_count = bool(fields["has_optional_count"])
    payload.optional_text = str(fields["optional_text"])
    payload.foxrun_has_optional_text = bool(fields["has_optional_text"])


def _make_envelope(node, envelope_type, payload_type, nested_type, fields: Mapping[str, object], origin: str, sequence: int):
    """Create one typed envelope with a real ROS clock stamp and stable origin sequence."""

    envelope = envelope_type()
    envelope.foxrun_origin_id = origin
    envelope.foxrun_sequence = sequence
    envelope.foxrun_stamp = node.get_clock().now().to_msg()
    payload = payload_type()
    _assign_payload(payload, nested_type, fields)
    envelope.payload = payload
    return envelope


def _payload_evidence(envelope) -> dict[str, object]:
    """Copy only small payload facts from a received generated envelope."""

    payload = envelope.payload
    nested = payload.nested
    return {
        "count": int(payload.count),
        "kind": int(payload.kind),
        "message": str(payload.message),
        "has_message": bool(payload.foxrun_has_message),
        "bytes": list(payload.bytes),
        "has_bytes": bool(payload.foxrun_has_bytes),
        "values": list(payload.values),
        "has_values": bool(payload.foxrun_has_values),
        "nested": {"enabled": bool(nested.enabled), "label": str(nested.label)},
        "has_nested": bool(payload.foxrun_has_nested),
        "optional_count": int(payload.optional_count),
        "has_optional_count": bool(payload.foxrun_has_optional_count),
        "optional_text": str(payload.optional_text),
        "has_optional_text": bool(payload.foxrun_has_optional_text),
    }


def _envelope_metadata(envelope) -> dict[str, object]:
    """Read only the portable envelope timestamp and sequence needed for peer proof."""

    stamp = getattr(envelope, "foxrun_stamp", None)
    return {
        "foxrun_sequence": getattr(envelope, "foxrun_sequence", None),
        "foxrun_stamp": {
            "sec": getattr(stamp, "sec", None),
            "nanosec": getattr(stamp, "nanosec", None),
        },
    }


def _external_endpoint_exists(infos, node_name: str, expected_type: str) -> bool:
    """Require a matching endpoint that does not belong to this helper node."""

    for info in infos:
        if getattr(info, "topic_type", "") != expected_type:
            continue
        if getattr(info, "node_name", "") != node_name:
            return True
    return False


def external_endpoint_has_reliability(
    infos,
    node_name: str,
    expected_type: str,
    expected_reliability: int,
) -> bool:
    """Require observable external endpoint type, owner, and effective reliability."""

    for info in infos:
        if getattr(info, "topic_type", "") != expected_type or getattr(info, "node_name", "") == node_name:
            continue
        qos = getattr(info, "qos_profile", None)
        reliability = getattr(qos, "reliability", None)
        try:
            actual = int(getattr(reliability, "value", reliability))
        except (TypeError, ValueError):
            continue
        if actual == int(getattr(expected_reliability, "value", expected_reliability)):
            return True
    return False


def _worker_result_base(lock: StaticInterfaceLock, verdict: str, **values: object) -> dict[str, object]:
    """Return summary-safe worker evidence without exposing raw process commands."""

    result: dict[str, object] = {
        "phase": 181,
        "interfacePackage": STATIC_INTERFACE_PACKAGE_ID,
        "rosPackageName": lock.ros_package_name,
        "interfaceRevision": lock.interface_revision,
        "interfaceDigest": lock.interface_digest,
        "interfaceDigestPrefix": protocol.digest_prefix(lock.interface_digest),
        "verdict": verdict,
    }
    result.update(values)
    return result


def run_typed_worker(args: argparse.Namespace) -> int:
    """Run the real generated-envelope peer loop after an owned workspace is built."""

    if args.unity_log is None or args.worker_result_json is None:
        raise PeerFailure("FAIL_WORKER_ARGUMENTS", "The typed worker requires a Unity log and owned result path.")
    root = workspace_root()
    static_package = pathlib.Path(args.static_interface_package or default_static_interface_package(root))
    lock = load_static_interface_lock(static_package)
    protocol.require_interface_digest(lock.interface_digest, args.interface_digest)
    probe_role = _normalize_probe_role(args.probe_role)
    if args.surface == "player" and probe_role != "orchestrate":
        raise PeerFailure("FAIL_ARGUMENTS", "Windows Player acceptance requires the complete orchestrated custom-interface proof.")
    requires_outbound, requires_bidirectional, _ = _role_requires(probe_role)
    try:
        import rclpy
        from rclpy.qos import HistoryPolicy, QoSProfile, ReliabilityPolicy
    except ImportError as exc:
        raise PeerFailure("FAIL_PEER_RUNTIME", "The selected peer Python cannot import rclpy.") from exc

    envelope_type, payload_type, nested_type = _load_generated_message_types(lock)
    qos = QoSProfile(depth=10, reliability=ReliabilityPolicy.RELIABLE, history=HistoryPolicy.KEEP_LAST)
    rclpy.init(args=None)
    try:
        (
            node,
            received_publish,
            received_bidirectional,
            subscribe_publisher,
            bidirectional_publisher,
        ) = create_typed_worker_endpoints(
            rclpy,
            envelope_type,
            "phase181_custom_peer_" + str(os.getpid()),
            qos,
            DEFAULT_TOPICS["publish"],
            DEFAULT_TOPICS["subscribe"],
            DEFAULT_TOPICS["bidirectional"],
        )
    except BaseException:
        try:
            rclpy.shutdown()
        except Exception:  # noqa: BLE001 - setup failure remains the terminal worker cause.
            pass
        raise
    expected_type = lock.ros_package_name + "/msg/" + lock.envelope_message_name

    start_offset = max(0, args.unity_log_offset)
    marker_offset = start_offset
    markers: list[protocol.UnityMarker] = []
    marker_seen: set[str] = set()
    run_token: str | None = None
    remote_origin = ""
    sequence = 1
    first_inbound_sent = False
    first_bidirectional_sent = False
    replay_sent = False
    final_bidirectional_sent = False
    next_publish_time = 0.0
    observed_outbound_messages: set[int] = set()
    observed_bidirectional_messages: set[int] = set()
    previous_outbound_sequence: int | None = None
    previous_unity_bidirectional_sequence: int | None = None
    worker_state = protocol.EvidenceStateMachine()
    worker_state.transition(protocol.ProtocolState.PEER_SOURCE_READY)
    worker_state.transition(protocol.ProtocolState.STRING_SUBSCRIBER_WAITING)
    ready_deadline = time.monotonic() + args.ready_timeout_seconds
    apply_deadline: float | None = None
    evidence: dict[str, object] = {
        "interfaceDigestMatches": True,
        "graphEvidence": False,
        "outboundObserved": False,
        "inboundApplied": False,
        "sameOriginDropped": False,
        "remoteOriginApplied": False,
        "nullableEmptyObserved": False,
        "unityTerminalPass": False,
        "cleanStop": False,
    }
    terminal_error: PeerFailure | None = None

    try:
        while time.monotonic() < worker_phase_deadline(run_token, ready_deadline, apply_deadline):
            rclpy.spin_once(node, timeout_sec=0.05)
            newly_observed, marker_offset = protocol.read_new_markers(args.unity_log, marker_offset)
            _append_unique_markers(markers, marker_seen, newly_observed)
            if run_token is None:
                ready = _matching_marker(markers, "PHASE181_CUSTOM_ROS2_READY")
                interface_ready = _matching_marker(markers, "PHASE181_CUSTOM_INTERFACE_READY")
                if ready is not None and interface_ready is not None:
                    run_token = require_matching_unity_readiness(
                        ready,
                        interface_ready,
                        lock,
                        args.distro,
                        args.rmw,
                        args.token if args.role == "windows-player" else None,
                    )
                    remote_origin = "remote-" + run_token
                    worker_state.transition(protocol.ProtocolState.UNITY_READY)
                    apply_deadline = time.monotonic() + args.apply_timeout_seconds

            if run_token is None:
                continue

            publish_publishers = node.get_publishers_info_by_topic(DEFAULT_TOPICS["publish"])
            subscribe_subscriptions = node.get_subscriptions_info_by_topic(DEFAULT_TOPICS["subscribe"])
            bidirectional_publishers = node.get_publishers_info_by_topic(DEFAULT_TOPICS["bidirectional"])
            bidirectional_subscriptions = node.get_subscriptions_info_by_topic(DEFAULT_TOPICS["bidirectional"])
            graph_checks = [
                external_endpoint_has_reliability(
                    subscribe_subscriptions,
                    node.get_name(),
                    expected_type,
                    ReliabilityPolicy.RELIABLE,
                ),
            ]
            if requires_outbound:
                graph_checks.append(_external_endpoint_exists(publish_publishers, node.get_name(), expected_type))
            if requires_bidirectional:
                graph_checks.extend(
                    (
                        _external_endpoint_exists(bidirectional_publishers, node.get_name(), expected_type),
                        external_endpoint_has_reliability(
                            bidirectional_subscriptions,
                            node.get_name(),
                            expected_type,
                            ReliabilityPolicy.RELIABLE,
                        ),
                    )
                )
            external_graph = all(graph_checks)
            evidence["graphEvidence"] = bool(external_graph)

            now = time.monotonic()
            subscribe_applied = _matching_marker(
                markers, "PHASE181_CUSTOM_ROS2_APPLIED", run_token, DEFAULT_TOPICS["subscribe"]
            ) is not None
            if not first_inbound_sent or (not subscribe_applied and now >= next_publish_time):
                subscribe_publisher.publish(
                    _make_envelope(
                        node,
                        envelope_type,
                        payload_type,
                        nested_type,
                        custom_payload_fields(run_token, null_empty=False),
                        remote_origin,
                        sequence,
                    )
                )
                sequence += 1
                first_inbound_sent = True
                next_publish_time = now + 0.75

            if subscribe_applied and not evidence["inboundApplied"]:
                evidence["inboundApplied"] = True
                worker_state.transition(protocol.ProtocolState.STRING_CORRELATED)

            if evidence["inboundApplied"] and requires_bidirectional and not first_bidirectional_sent:
                bidirectional_publisher.publish(
                    _make_envelope(
                        node,
                        envelope_type,
                        payload_type,
                        nested_type,
                        custom_payload_fields(run_token, null_empty=False),
                        remote_origin,
                        sequence,
                    )
                )
                sequence += 1
                first_bidirectional_sent = True
                worker_state.transition(protocol.ProtocolState.PROBES_RUNNING)
            elif evidence["inboundApplied"] and not requires_bidirectional and worker_state.state == protocol.ProtocolState.STRING_CORRELATED:
                worker_state.transition(protocol.ProtocolState.PROBES_RUNNING)

            matching_outbound = [
                message
                for message in received_publish
                if _payload_evidence(message).get("message") == "unity-publish"
            ]
            for message in matching_outbound:
                if id(message) in observed_outbound_messages:
                    continue
                observed_outbound_messages.add(id(message))
                if _payload_evidence(message) != custom_payload_fields("unity-publish", null_empty=False):
                    raise PeerFailure("FAIL_PAYLOAD_SHAPE", "Unity's native custom PublishOnly envelope did not preserve the locked payload.")
                previous_outbound_sequence = protocol.require_envelope_metadata(
                    _envelope_metadata(message),
                    previous_outbound_sequence,
                )
            evidence["outboundObserved"] = bool(observed_outbound_messages)

            unity_echo = next(
                (
                    message
                    for message in received_bidirectional
                    if getattr(message, "foxrun_origin_id", "")
                    and not is_peer_remote_origin(getattr(message, "foxrun_origin_id", ""), run_token)
                    and _payload_evidence(message).get("message") == run_token
                ),
                None,
            )
            if first_bidirectional_sent and unity_echo is not None and not replay_sent:
                if _payload_evidence(unity_echo) != custom_payload_fields(run_token, null_empty=False):
                    raise PeerFailure("FAIL_PAYLOAD_SHAPE", "Unity's custom P&S echo did not preserve the correlated remote DTO.")
                previous_unity_bidirectional_sequence = protocol.require_envelope_metadata(
                    _envelope_metadata(unity_echo),
                    previous_unity_bidirectional_sequence,
                )
                observed_bidirectional_messages.add(id(unity_echo))
                bidirectional_publisher.publish(unity_echo)
                replay_sent = True

            same_origin_dropped = _matching_marker(
                markers,
                "PHASE181_CUSTOM_ROS2_SAME_ORIGIN_DROPPED",
                run_token,
                DEFAULT_TOPICS["bidirectional"],
            ) is not None
            evidence["sameOriginDropped"] = bool(same_origin_dropped)
            if requires_bidirectional and same_origin_dropped and not final_bidirectional_sent:
                bidirectional_publisher.publish(
                    _make_envelope(
                        node,
                        envelope_type,
                        payload_type,
                        nested_type,
                        custom_payload_fields(run_token, null_empty=True),
                        "remote-final-" + run_token,
                        sequence,
                    )
                )
                sequence += 1
                final_bidirectional_sent = True

            nullable_echo = next(
                (
                    message
                    for message in received_bidirectional
                    if id(message) not in observed_bidirectional_messages
                    and getattr(message, "foxrun_origin_id", "")
                    and not is_peer_remote_origin(getattr(message, "foxrun_origin_id", ""), run_token)
                    and _payload_evidence(message).get("message") == ""
                ),
                None,
            )
            if nullable_echo is not None:
                protocol.require_nullable_empty_payload(_payload_evidence(nullable_echo))
                previous_unity_bidirectional_sequence = protocol.require_envelope_metadata(
                    _envelope_metadata(nullable_echo),
                    previous_unity_bidirectional_sequence,
                )
                observed_bidirectional_messages.add(id(nullable_echo))
                evidence["nullableEmptyObserved"] = True

            bidirectional_applies = [
                marker
                for marker in markers
                if marker.name == "PHASE181_CUSTOM_ROS2_APPLIED"
                and marker.fields.get("token") == run_token
                and marker.fields.get("topic") == DEFAULT_TOPICS["bidirectional"]
            ]
            if requires_bidirectional and len(bidirectional_applies) >= 2:
                evidence["remoteOriginApplied"] = True

            if has_role_transport_evidence(evidence, probe_role) and worker_state.state == protocol.ProtocolState.PROBES_RUNNING:
                worker_state.transition(protocol.ProtocolState.UNITY_APPLIED)
                worker_state.transition(protocol.ProtocolState.ORIGIN_CHECKED)

            if args.surface == "player":
                evidence["unityTerminalPass"] = _matching_marker(markers, "PHASE181_CUSTOM_ROS2_PASS", run_token) is not None
            else:
                # Editor has no terminal marker by design; its complete correlated
                # marker set is the equivalent bounded Unity-side proof.
                evidence["unityTerminalPass"] = has_role_transport_evidence(evidence, probe_role)

            if can_complete_live_evidence(evidence, probe_role):
                break
        else:
            if run_token is None:
                terminal_error = PeerFailure("FAIL_READY_TIMEOUT", "Unity did not emit correlated custom interface readiness.")
            elif (
                args.surface == "player"
                and evidence["interfaceDigestMatches"]
                and evidence["graphEvidence"]
                and evidence["outboundObserved"]
                and evidence["inboundApplied"]
                and evidence["sameOriginDropped"]
                and evidence["remoteOriginApplied"]
                and evidence["nullableEmptyObserved"]
                and not evidence["unityTerminalPass"]
            ):
                terminal_error = PeerFailure("FAIL_UNITY_TIMEOUT", "The Player exchanged data but did not emit its terminal marker.")
            else:
                terminal_error = PeerFailure(classify_evidence(evidence, probe_role), "The typed custom envelope proof did not complete.")
    except PeerFailure as exc:
        terminal_error = exc
    except Exception as exc:  # noqa: BLE001 - a peer must return a bounded failure instead of leaking a stack into summary.
        terminal_error = PeerFailure("FAIL_PEER_RUNTIME", "The typed worker stopped before completing its bounded probe.")
    finally:
        teardown_failed = False
        try:
            node.destroy_node()
        except Exception:  # noqa: BLE001 - a teardown error must become a bounded acceptance failure.
            teardown_failed = True
        try:
            rclpy.shutdown()
        except Exception:  # noqa: BLE001 - do not leak a native teardown stack into the summary.
            teardown_failed = True
        if run_token is not None:
            no_late_apply, marker_offset = observe_no_late_unity_apply(
                args.unity_log,
                marker_offset,
                run_token,
            )
            evidence["postStopMarkerOffset"] = marker_offset
            evidence["cleanStop"] = no_late_apply and not teardown_failed
            if not no_late_apply and terminal_error is None:
                terminal_error = PeerFailure("FAIL_LATE_APPLY", "Unity applied a correlated custom envelope after the peer stop offset.")
        else:
            evidence["cleanStop"] = not teardown_failed
        if teardown_failed and terminal_error is None:
            terminal_error = PeerFailure("FAIL_CLEAN_STOP", "The helper-owned generated ROS2 endpoints did not stop cleanly.")

    verdict = terminal_error.code if terminal_error is not None else classify_evidence(evidence, probe_role)
    if terminal_error is None and verdict == "PASS":
        worker_state.transition(protocol.ProtocolState.CLEAN_STOP)
        worker_state.transition(protocol.ProtocolState.PASS)
    result = _worker_result_base(
        lock,
        verdict,
        role=args.role,
        probeRole=probe_role,
        surface=args.surface,
        markerOffsetStart=start_offset,
        markerOffsetEnd=marker_offset,
        unityRuntime=args.distro,
        unityRmw=args.rmw,
        unityDigestPrefix=protocol.digest_prefix(lock.interface_digest),
        stateTransitions=[transition.state.value for transition in worker_state.transitions],
        evidence=evidence,
        markerNames=[marker.name for marker in markers],
        token=run_token or args.token,
    )
    if terminal_error is not None:
        result["error"] = str(terminal_error)
    write_worker_result(args.worker_result_json, result)
    print(verdict)
    return 0 if verdict == "PASS" else 1


def default_unity_editor_log_path() -> pathlib.Path:
    """Return Unity's normal Windows Editor log without depending on a shell profile."""

    local_app_data = os.environ.get("LOCALAPPDATA")
    if local_app_data:
        return pathlib.Path(local_app_data) / "Unity" / "Editor" / "Editor.log"
    return pathlib.Path.home() / "AppData" / "Local" / "Unity" / "Editor" / "Editor.log"


def _profile_build_directory(repository: pathlib.Path, profile_id: str) -> pathlib.Path:
    """Return one stable summary directory without allowing arbitrary output roots."""

    if _PROFILE_ID.fullmatch(profile_id) is None:
        raise PeerFailure("FAIL_PROFILE", "The Phase181 profile identifier is not safe.")
    return pathlib.Path(repository) / "build" / "phase181" / profile_id


def _require_positive_timeout(value: float, name: str) -> float:
    """Reject invalid operator timeout input before launching an owned process."""

    if value <= 0.0 or value > 600.0:
        raise PeerFailure("FAIL_ARGUMENTS", name + " must be within the bounded Phase181 acceptance range.")
    return value


def _terminate_owned_child(process: subprocess.Popen[str]) -> None:
    """Terminate just one helper-created process tree on the current host."""

    if process.poll() is not None:
        return
    if os.name == "nt":
        subprocess.run(
            ["taskkill", "/PID", str(process.pid), "/T", "/F"],
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            check=False,
            shell=False,
        )
        try:
            process.wait(timeout=5.0)
        except subprocess.TimeoutExpired:
            pass
        return
    protocol.terminate_owned_process(process)


def _require_player_path(player: pathlib.Path | None) -> pathlib.Path:
    """Accept only one existing absolute WindowsStandalone64 Player executable."""

    if player is None:
        raise PeerFailure("FAIL_PLAYER_BUILD", "The Windows Player path is required for Player acceptance.")
    candidate = pathlib.Path(player)
    if not candidate.is_absolute() or not candidate.is_file():
        raise PeerFailure("FAIL_PLAYER_BUILD", "The Windows Player path must identify an existing absolute executable.")
    return candidate.resolve()


def run_windows_local_editor(args: argparse.Namespace) -> int:
    """Run the owned Windows-local Editor proof for one explicit profile."""

    if args.surface != "editor":
        raise PeerFailure("FAIL_ARGUMENTS", "The Windows-local Editor path requires the editor surface.")
    return _run_windows_surface(args, surface="editor")


def run_windows_player(args: argparse.Namespace) -> int:
    """Run the same correlated protocol against one helper-owned Player."""

    if args.surface != "player":
        raise PeerFailure("FAIL_ARGUMENTS", "The Windows Player path requires the player surface.")
    return _run_windows_surface(args, surface="player")


def _run_windows_surface(args: argparse.Namespace, *, surface: str) -> int:
    """Share strict preflight, source staging, and evidence between Editor/Player."""

    if surface not in {"editor", "player"}:
        raise ValueError("Phase181 Windows surface must be editor or player.")
    success_verdict = getattr(args, "success_verdict", "PASS")
    if not isinstance(success_verdict, str) or not re.fullmatch(r"(?:PASS|PHASE181_[A-Z0-9_]+_PASS)", success_verdict):
        raise PeerFailure("FAIL_ARGUMENTS", "The requested Windows acceptance success verdict is not a stable Phase181 identifier.")
    repository = workspace_root()
    profile_id = args.profile_id or (args.distro + "-" + args.rmw.removeprefix("rmw_").replace("_cpp", ""))
    output_directory = _profile_build_directory(repository, profile_id)
    summary_name = "windows-local-editor.json" if surface == "editor" else "windows-player.json"
    summary_path = pathlib.Path(args.summary_json) if args.summary_json is not None else output_directory / summary_name
    summary: dict[str, object] = {
        "phase": 181,
        "role": "windows-local-editor" if surface == "editor" else "windows-player",
        "surface": surface,
        "transportScope": "windows-local-loopback",
        "profileId": profile_id,
        "distro": args.distro,
        "rmwImplementation": args.rmw,
        "domainId": args.domain_id,
        "commandLabels": {},
        "processOwnership": {},
    }
    failure: PeerFailure | None = None
    workspace: pathlib.Path | None = None
    worker_process: subprocess.Popen[str] | None = None
    player_process: subprocess.Popen[str] | None = None
    worker_stream = None
    exit_code = 1
    try:
        if args.workspace is not None:
            raise PeerFailure("FAIL_PEER_WORKSPACE", "The profile helper owns its peer workspace and does not accept an external workspace.")
        ready_timeout = _require_positive_timeout(args.ready_timeout_seconds, "The Unity readiness timeout")
        apply_timeout = _require_positive_timeout(args.apply_timeout_seconds, "The Unity apply timeout")
        if args.rmw == "rmw_zenoh_cpp" and not args.zenoh_topology_id:
            raise PeerFailure("FAIL_ZENOH_TOPOLOGY", "Zenoh custom-interface acceptance requires an explicit topology identity.")

        static_package = pathlib.Path(args.static_interface_package or default_static_interface_package(repository))
        lock = load_static_interface_lock(static_package)
        try:
            protocol.require_interface_digest(lock.interface_digest, args.interface_digest or lock.interface_digest)
        except protocol.ProtocolFailure as exc:
            raise PeerFailure(exc.code, "The requested custom interface digest is not the locked source digest.") from exc
        summary.update(
            {
                "interfacePackage": STATIC_INTERFACE_PACKAGE_ID,
                "rosPackageName": lock.ros_package_name,
                "interfaceRevision": lock.interface_revision,
                "interfaceDigest": lock.interface_digest,
                "interfaceDigestPrefix": protocol.digest_prefix(lock.interface_digest),
            }
        )
        summary["selectedTypesupportAddon"] = require_selected_typesupport_addon(repository, args.distro)

        ros2_root = pathlib.Path(args.ros2_root or ros2env.default_ros2_root(args.distro, repository))
        toolchain = resolve_windows_peer_toolchain(ros2_root)
        workspace = prepare_owned_workspace(repository / "build" / "phase181", profile_id)
        summary["processOwnership"] = {"workspaceOwned": True}

        validator_command = build_addon_validator_command(repository, args.distro, args.rmw)
        summary["commandLabels"] = {"addonValidator": protocol.bounded_command_label(validator_command)}
        run_logged_owned_command(
            validator_command,
            cwd=repository,
            env=ros2env.sanitized_subprocess_env(os.environ),
            log_path=workspace / "typesupport-preflight.log",
            timeout_seconds=min(60.0, ready_timeout),
            failure_code="FAIL_TYPESUPPORT_PREFLIGHT",
        )
        summary["typesupportPreflight"] = "passed"

        stage_locked_ros_source(static_package, workspace, lock.ros_package_name)
        build_environment = ros2env.build_ros_env(
            toolchain.ros2_root,
            args.rmw,
            args.discovery_range,
            str(args.domain_id),
            args.distro,
        )
        colcon_command = build_colcon_command(toolchain.colcon_executable, lock.ros_package_name)
        summary["commandLabels"] = {
            **summary["commandLabels"],
            "colcon": protocol.bounded_command_label(colcon_command),
        }
        run_logged_owned_command(
            colcon_command,
            cwd=workspace,
            env=build_environment,
            log_path=workspace / "colcon-build.log",
            timeout_seconds=min(300.0, ready_timeout),
            failure_code="FAIL_PEER_BUILD",
        )

        peer_environment = build_peer_environment(
            build_environment,
            toolchain.ros2_root,
            workspace / "install",
            distro=args.distro,
            rmw=args.rmw,
            domain_id=args.domain_id,
            topology_id=args.zenoh_topology_id or None,
        )
        run_token = "phase181-peer-" + uuid.uuid4().hex
        if surface == "player":
            player = _require_player_path(args.player)
            unity_log = pathlib.Path(args.player_log or output_directory / "windows-player.log")
            unity_log_offset = protocol.log_offset(unity_log)
            player_timeout = min(600.0, ready_timeout + apply_timeout + 30.0)
            player_command = build_player_command(player, unity_log, run_token, player_timeout)
            player_environment = build_player_environment(
                build_environment,
                distro=args.distro,
                rmw=args.rmw,
                domain_id=args.domain_id,
                interface_revision=lock.interface_revision,
                interface_digest=lock.interface_digest,
                topology_id=args.zenoh_topology_id or None,
                discovery_range=args.discovery_range,
            )
            summary["commandLabels"] = {
                **summary["commandLabels"],
                "player": protocol.bounded_command_label(player_command),
            }
            player_process = subprocess.Popen(
                player_command,
                cwd=str(player.parent),
                env=player_environment,
                stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL,
                text=True,
                shell=False,
                **worker_launch_options(),
            )
            summary["processOwnership"] = {
                **summary["processOwnership"],
                "playerPid": player_process.pid,
            }
            worker_role = "windows-player"
        else:
            unity_log = pathlib.Path(args.unity_log or default_unity_editor_log_path())
            unity_log_offset = protocol.log_offset(unity_log)
            worker_role = "windows-local-editor"

        worker_result_path = workspace / "worker-result.json"
        worker_command = build_worker_command(
            toolchain.python_executable,
            role=worker_role,
            surface=surface,
            workspace=workspace,
            interface_digest=lock.interface_digest,
            token=run_token,
            unity_log=unity_log,
            result_json=worker_result_path,
            distro=args.distro,
            rmw=args.rmw,
            domain_id=args.domain_id,
            unity_log_offset=unity_log_offset,
            static_interface_package=static_package,
            ready_timeout_seconds=ready_timeout,
            apply_timeout_seconds=apply_timeout,
        )
        summary["commandLabels"] = {
            **summary["commandLabels"],
            "worker": protocol.bounded_command_label(worker_command),
        }
        if surface == "editor":
            print(
                "[phase181:" + profile_id + "] Repo-local custom String envelope peer is waiting for its Unity subscription "
                + "for up to " + format(ready_timeout, "g") + " seconds; enter Play Mode now.",
                flush=True,
            )
        worker_log_path = workspace / "peer-worker.log"
        worker_stream = worker_log_path.open("w", encoding="utf-8", errors="replace")
        worker_process = subprocess.Popen(
            worker_command,
            cwd=str(workspace),
            env=peer_environment,
            text=True,
            stdout=worker_stream,
            stderr=subprocess.STDOUT,
            shell=False,
            **worker_launch_options(),
        )
        summary["processOwnership"] = {
            **summary["processOwnership"],
            "workerPid": worker_process.pid,
        }
        try:
            worker_exit = worker_process.wait(timeout=ready_timeout + apply_timeout + 30.0)
        except subprocess.TimeoutExpired as exc:
            _terminate_owned_child(worker_process)
            raise PeerFailure("FAIL_WORKER_TIMEOUT", "The helper-owned custom ROS2 peer exceeded its bounded acceptance window.") from exc
        worker_result = read_successful_worker_result(worker_result_path, lock)
        if worker_exit != 0:
            raise PeerFailure("FAIL_WORKER_EXIT", "The typed worker reported PASS but returned a nonzero operating-system exit code.")
        if player_process is not None:
            try:
                player_exit = player_process.wait(timeout=30.0)
            except subprocess.TimeoutExpired as exc:
                _terminate_owned_child(player_process)
                raise PeerFailure("FAIL_PLAYER_EXIT", "The Player emitted peer evidence but did not exit after its terminal marker.") from exc
            summary["playerExitCode"] = player_exit
            require_player_exit_code(player_exit)
        summary["unityMarkerOffsets"] = {
            "start": unity_log_offset,
            "end": worker_result.get("markerOffsetEnd"),
        }
        summary["workerEvidence"] = worker_result
        summary["verdict"] = success_verdict
        exit_code = 0
    except PeerFailure as exc:
        failure = exc
    except KeyboardInterrupt:
        failure = PeerFailure("FAIL_INTERRUPTED", "The operator interrupted the helper-owned custom ROS2 peer.")
    except (OSError, subprocess.SubprocessError):
        failure = PeerFailure("FAIL_ENVIRONMENT", "A helper-owned custom ROS2 process could not be started or completed.")
    finally:
        if worker_process is not None and worker_process.poll() is None:
            _terminate_owned_child(worker_process)
        if player_process is not None and player_process.poll() is None:
            _terminate_owned_child(player_process)
        if worker_stream is not None:
            worker_stream.close()
        if workspace is not None:
            try:
                cleanup_owned_workspace(workspace, repository / "build" / "phase181")
            except PeerFailure as cleanup_error:
                if failure is None:
                    failure = cleanup_error
                    exit_code = 1
        if failure is not None:
            summary["failureCode"] = failure.code
            summary["error"] = str(failure)
            summary["verdict"] = failure.code
        elif "verdict" not in summary:
            summary["verdict"] = "FAIL_UNKNOWN"
            exit_code = 1
        protocol.write_summary_atomic(summary_path, summary)
        print("Summary: " + str(summary_path))
        print("Verdict: " + str(summary["verdict"]))
    return exit_code


def parse_args(argv: Sequence[str] | None = None) -> argparse.Namespace:
    """Parse generic peer/worker options; profile wrappers supply all normal defaults."""

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--worker", action="store_true", help="Run inside the selected ROS2 Python environment.")
    parser.add_argument("--role", choices=("windows-local-editor", "linux-peer", "windows-player"), required=True)
    parser.add_argument("--probe-role", choices=PROBE_ROLES, default="orchestrate")
    parser.add_argument("--profile-id", default="")
    parser.add_argument("--surface", choices=("editor", "player"), default="editor")
    parser.add_argument("--distro", choices=("humble", "jazzy", "lyrical"), default="jazzy")
    parser.add_argument("--rmw", default="rmw_fastrtps_cpp")
    parser.add_argument("--domain-id", type=int, default=0)
    parser.add_argument("--discovery-range", choices=("LOCALHOST", "SUBNET", "SYSTEM_DEFAULT", "OFF"), default="SUBNET")
    parser.add_argument("--token", default="")
    parser.add_argument("--workspace", type=pathlib.Path)
    parser.add_argument("--ros2-root", type=pathlib.Path)
    parser.add_argument("--ros2-python", type=pathlib.Path)
    parser.add_argument("--colcon", type=pathlib.Path)
    parser.add_argument("--static-interface-package", type=pathlib.Path)
    parser.add_argument("--unity-log", type=pathlib.Path)
    parser.add_argument("--player", type=pathlib.Path)
    parser.add_argument("--player-log", type=pathlib.Path)
    parser.add_argument("--unity-log-offset", type=int, default=0)
    parser.add_argument("--worker-result-json", type=pathlib.Path)
    parser.add_argument("--summary-json", type=pathlib.Path)
    parser.add_argument("--interface-digest", default="")
    parser.add_argument("--zenoh-topology-id", default="")
    parser.add_argument("--ready-timeout-seconds", type=float, default=300.0)
    parser.add_argument("--apply-timeout-seconds", type=float, default=120.0)
    return parser.parse_args(argv)


def main(argv: Sequence[str] | None = None) -> int:
    """Run advanced profile arguments; ordinary operators use one named thin wrapper."""

    args = parse_args(argv)
    if args.worker:
        return worker_main(args)
    if args.role == "windows-local-editor":
        return run_windows_local_editor(args)
    if args.role == "windows-player":
        return run_windows_player(args)
    print("Phase181 Linux and Player modes require their dedicated role helpers.", file=sys.stderr)
    return 2


def worker_main(args: argparse.Namespace) -> int:
    """Run the generated-envelope worker and return only one bounded terminal code."""

    try:
        return run_typed_worker(args)
    except protocol.ProtocolFailure as exc:
        if args.worker_result_json is not None:
            write_worker_result(args.worker_result_json, {"phase": 181, "verdict": exc.code, "error": str(exc)})
        print(exc.code, file=sys.stderr)
        return 1
    except Exception:  # noqa: BLE001 - native initialization must never leak an unbounded worker failure.
        if args.worker_result_json is not None:
            write_worker_result(
                args.worker_result_json,
                {"phase": 181, "verdict": "FAIL_PEER_RUNTIME", "error": "unhandled worker runtime failure"},
            )
        print("FAIL_PEER_RUNTIME", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
