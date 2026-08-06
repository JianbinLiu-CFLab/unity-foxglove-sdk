#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Module: Scripts/smoke
# Purpose: Caller-owned Linux peer evidence for Phase181 generated ROS2 interfaces.

"""Run one explicit Linux peer against the locked Phase181 custom envelope.

The caller must already have sourced the matching Linux ROS2 distribution and
selected its RMW.  This helper never sources an arbitrary shell profile,
guesses a distribution, deletes a caller workspace, or treats a generated
message look-alike as the locked custom interface.  It stages or verifies the
exact ``Ros2Package~`` tree, builds it only below the caller-owned workspace,
and delegates all data/marker verdicts to the common Phase181 worker protocol.
"""

from __future__ import annotations

import argparse
import hashlib
import os
import pathlib
import shutil
import subprocess
import sys
import time
import uuid
from typing import Mapping, Sequence

import phase181_custom_ros2_peer as peer
import phase181_custom_ros2_peer_protocol as protocol


SUPPORTED_DISTROS = ("humble", "jazzy", "lyrical")
SUPPORTED_RMWS = ("rmw_fastrtps_cpp", "rmw_zenoh_cpp")


class LinuxPeerFailure(peer.PeerFailure):
    """Stable Linux peer failure with the shared Phase181 safe-code grammar."""


def workspace_root() -> pathlib.Path:
    """Reuse the repository-root resolution that does not expand junctions."""

    return peer.workspace_root()


def parse_domain_id(value: str) -> int:
    """Parse the portable ROS2 domain range without implicit wrapping."""

    try:
        parsed = int(value)
    except ValueError as exc:
        raise argparse.ArgumentTypeError("--domain-id must be an integer") from exc
    if not 0 <= parsed <= 232:
        raise argparse.ArgumentTypeError("--domain-id must be in the ROS2 range 0..232")
    return parsed


def positive_seconds(value: str) -> float:
    """Parse one bounded positive timeout for a caller-visible peer step."""

    try:
        parsed = float(value)
    except ValueError as exc:
        raise argparse.ArgumentTypeError("timeout must be numeric") from exc
    if parsed <= 0.0 or parsed > 600.0:
        raise argparse.ArgumentTypeError("timeout must be within 0..600 seconds")
    return parsed


def parse_profile_id(value: str) -> str:
    """Accept the same bounded profile grammar as the Windows wrappers."""

    normalized = value.strip()
    if peer._PROFILE_ID.fullmatch(normalized) is None:
        raise argparse.ArgumentTypeError("--profile-id must be a safe 1..64 character Phase181 profile id")
    return normalized


def parse_topology_id(value: str) -> str:
    """Accept a bounded opaque Zenoh topology identifier, never a config path."""

    normalized = value.strip()
    if not peer._safe_marker_token(normalized):
        raise argparse.ArgumentTypeError("--zenoh-topology-id must be a safe bounded opaque identifier")
    return normalized


def parse_args(argv: Sequence[str] | None = None) -> argparse.Namespace:
    """Parse an explicit Linux profile/role/surface command without ROS imports."""

    root = workspace_root()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--role", choices=peer.PROBE_ROLES, required=True)
    parser.add_argument("--profile-id", type=parse_profile_id, required=True)
    parser.add_argument("--surface", choices=("editor", "player"), required=True)
    parser.add_argument("--distro", choices=SUPPORTED_DISTROS, required=True)
    parser.add_argument("--rmw", choices=SUPPORTED_RMWS, required=True)
    parser.add_argument("--domain-id", type=parse_domain_id, default=0)
    parser.add_argument("--discovery-range", choices=("LOCALHOST", "SUBNET", "SYSTEM_DEFAULT", "OFF"), default="SUBNET")
    parser.add_argument("--workspace", type=pathlib.Path, required=True)
    parser.add_argument("--static-interface-package", type=pathlib.Path, default=root / "Packages" / peer.STATIC_INTERFACE_PACKAGE_ID)
    parser.add_argument("--interface-digest", default="")
    parser.add_argument("--unity-log", type=pathlib.Path, required=True)
    parser.add_argument("--unity-log-offset", type=int, default=None)
    parser.add_argument("--colcon", type=pathlib.Path)
    parser.add_argument("--python", type=pathlib.Path)
    parser.add_argument("--ready-timeout-seconds", type=positive_seconds, default=300.0)
    parser.add_argument("--apply-timeout-seconds", type=positive_seconds, default=120.0)
    parser.add_argument("--zenoh-topology-id", type=parse_topology_id)
    parser.add_argument("--summary-json", type=pathlib.Path)
    args = parser.parse_args(argv)
    if args.rmw == "rmw_zenoh_cpp" and args.distro != "lyrical":
        parser.error("rmw_zenoh_cpp is certified only with --distro lyrical")
    if args.rmw == "rmw_zenoh_cpp" and args.zenoh_topology_id is None:
        parser.error("--rmw rmw_zenoh_cpp requires an explicit --zenoh-topology-id")
    if args.rmw != "rmw_zenoh_cpp" and args.zenoh_topology_id is not None:
        parser.error("--zenoh-topology-id is valid only with --rmw rmw_zenoh_cpp")
    if args.surface == "player" and args.role != "orchestrate":
        parser.error("Player certification requires --role orchestrate for the complete shared protocol")
    return args


def validate_selected_linux_environment(args: argparse.Namespace, environment: Mapping[str, str]) -> None:
    """Require a caller-sourced ROS2 distro/RMW; never substitute a fallback."""

    distro = (environment.get("ROS_DISTRO") or "").strip().lower()
    rmw = (environment.get("RMW_IMPLEMENTATION") or "").strip()
    if (environment.get("ROS_VERSION") or "2").strip() != "2":
        raise LinuxPeerFailure("FAIL_ENVIRONMENT", "The caller-selected Linux environment is not ROS2.")
    if distro != args.distro:
        raise LinuxPeerFailure("FAIL_ENVIRONMENT", "ROS_DISTRO does not match the explicit Phase181 Linux profile.")
    if rmw != args.rmw:
        raise LinuxPeerFailure("FAIL_ENVIRONMENT", "RMW_IMPLEMENTATION does not match the explicit Phase181 Linux profile.")


def build_linux_environment(args: argparse.Namespace, source: Mapping[str, str] | None = None) -> dict[str, str]:
    """Copy a selected Linux ROS environment and apply only explicit peer knobs."""

    environment = peer.ros2env.sanitized_subprocess_env(
        dict(os.environ if source is None else source)
    )
    validate_selected_linux_environment(args, environment)
    environment["ROS_DOMAIN_ID"] = str(args.domain_id)
    environment["ROS_AUTOMATIC_DISCOVERY_RANGE"] = args.discovery_range
    environment.pop("ROS_LOCALHOST_ONLY", None)
    environment.pop("ROS_DISCOVERY_SERVER", None)
    topology_id = getattr(args, "zenoh_topology_id", None)
    if topology_id is not None:
        environment["UNITY2FOXGLOVE_ZENOH_TOPOLOGY_ID"] = topology_id
    else:
        environment.pop("UNITY2FOXGLOVE_ZENOH_TOPOLOGY_ID", None)
    return environment


def _prepend_search_paths(entries: Sequence[pathlib.Path], existing: str) -> str:
    """Prepend deterministic explicit paths without synthesizing empty entries."""

    values = [str(path) for path in entries]
    if existing:
        values.append(existing)
    return os.pathsep.join(values)


def _merged_install_python_paths(install: pathlib.Path) -> tuple[pathlib.Path, ...]:
    """Locate Python package roots emitted by explicit ``colcon --merge-install``."""

    candidate = pathlib.Path(install)
    if not candidate.is_dir():
        raise LinuxPeerFailure("FAIL_PEER_BUILD", "The explicit Linux colcon install root is unavailable.")
    paths: list[pathlib.Path] = []
    for relative_pattern in (
        "lib/python*/site-packages",
        "lib/python*/dist-packages",
        "local/lib/python*/site-packages",
        "local/lib/python*/dist-packages",
    ):
        paths.extend(path for path in candidate.glob(relative_pattern) if path.is_dir())
    unique = {path.resolve() for path in paths}
    if not unique:
        raise LinuxPeerFailure(
            "FAIL_PEER_BUILD",
            "The explicit Linux colcon install does not expose generated Python interface packages.",
        )
    return tuple(sorted(unique, key=lambda path: path.as_posix()))


def build_linux_worker_environment(
    environment: Mapping[str, str],
    workspace_install: pathlib.Path,
) -> dict[str, str]:
    """Make one built merged install importable without sourcing a shell profile."""

    install = pathlib.Path(workspace_install).resolve()
    python_paths = _merged_install_python_paths(install)
    worker_environment = dict(environment)
    for variable in ("AMENT_PREFIX_PATH", "CMAKE_PREFIX_PATH", "COLCON_PREFIX_PATH"):
        worker_environment[variable] = _prepend_search_paths((install,), worker_environment.get(variable, ""))
    worker_environment["PYTHONPATH"] = _prepend_search_paths(python_paths, worker_environment.get("PYTHONPATH", ""))
    worker_environment["PATH"] = _prepend_search_paths((install / "bin",), worker_environment.get("PATH", ""))
    return worker_environment


def _normalized_tree_digest(root: pathlib.Path) -> str:
    """Hash a ROS package tree deterministically without trusting install state."""

    candidate = pathlib.Path(root)
    if not candidate.is_dir():
        raise LinuxPeerFailure("FAIL_PEER_SOURCE", "The expected ROS source tree is unavailable.")
    entries: list[tuple[str, bytes]] = []
    for path in sorted(candidate.rglob("*"), key=lambda item: item.as_posix().lower()):
        if not path.is_file() or path.name.endswith(".meta"):
            continue
        relative = path.relative_to(candidate).as_posix()
        try:
            content = path.read_bytes().replace(b"\r\n", b"\n").replace(b"\r", b"\n")
        except OSError as exc:
            raise LinuxPeerFailure("FAIL_PEER_SOURCE", "The expected ROS source tree cannot be read.") from exc
        entries.append((relative, content))
    if not entries or len({relative.lower() for relative, _ in entries}) != len(entries):
        raise LinuxPeerFailure("FAIL_PEER_SOURCE", "The expected ROS source tree is empty or path-ambiguous.")
    digest = hashlib.sha256()
    for relative, content in entries:
        encoded = relative.encode("utf-8")
        digest.update(len(encoded).to_bytes(8, "big", signed=False))
        digest.update(encoded)
        digest.update(len(content).to_bytes(8, "big", signed=False))
        digest.update(content)
    return digest.hexdigest()


def _require_caller_workspace(workspace: pathlib.Path) -> pathlib.Path:
    """Refuse implicit build locations and never create/delete caller-owned roots."""

    candidate = pathlib.Path(workspace)
    if not candidate.is_absolute() or not candidate.is_dir():
        raise LinuxPeerFailure("FAIL_PEER_WORKSPACE", "The Linux peer requires an existing absolute caller-owned workspace.")
    return candidate.resolve()


def stage_or_verify_locked_ros_source(
    static_interface_package: pathlib.Path,
    workspace: pathlib.Path,
    ros_package_name: str,
) -> pathlib.Path:
    """Stage once or verify an existing caller workspace has exact locked source."""

    caller_workspace = _require_caller_workspace(workspace)
    source = pathlib.Path(static_interface_package) / "Ros2Package~"
    destination = caller_workspace / "src" / ros_package_name
    source_digest = _normalized_tree_digest(source)
    if destination.exists():
        if _normalized_tree_digest(destination) != source_digest:
            raise LinuxPeerFailure("FAIL_PEER_SOURCE", "The caller-owned Linux workspace contains a stale custom interface source tree.")
        return destination
    try:
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copytree(source, destination, ignore=shutil.ignore_patterns("*.meta", "build", "install", "log", "__pycache__"))
    except OSError as exc:
        raise LinuxPeerFailure("FAIL_PEER_SOURCE", "The exact locked ROS source could not be staged into the caller workspace.") from exc
    if _normalized_tree_digest(destination) != source_digest:
        raise LinuxPeerFailure("FAIL_PEER_SOURCE", "The staged Linux custom interface source does not match the locked source tree.")
    return destination


def resolve_linux_colcon(explicit: pathlib.Path | None) -> pathlib.Path:
    """Resolve one explicit/selected colcon executable after the caller sources ROS2."""

    candidate = pathlib.Path(explicit) if explicit is not None else pathlib.Path(shutil.which("colcon") or "")
    if not candidate.is_absolute() or not candidate.is_file():
        raise LinuxPeerFailure("FAIL_PEER_TOOLCHAIN", "The selected Linux ROS2 environment does not provide an explicit colcon executable.")
    return candidate.resolve()


def resolve_linux_python(explicit: pathlib.Path | None) -> pathlib.Path:
    """Resolve the Python process that will import the built generated ROS package."""

    candidate = pathlib.Path(explicit) if explicit is not None else pathlib.Path(sys.executable)
    if not candidate.is_absolute() or not candidate.is_file():
        raise LinuxPeerFailure("FAIL_PEER_TOOLCHAIN", "The selected Linux ROS2 Python executable is unavailable.")
    return candidate.resolve()


def build_linux_worker_command(
    python_executable: pathlib.Path,
    *,
    workspace: pathlib.Path,
    interface_digest: str,
    role: str,
    unity_log: pathlib.Path,
    result_json: pathlib.Path,
    distro: str,
    rmw: str,
    domain_id: int,
    surface: str,
    unity_log_offset: int = 0,
    static_interface_package: pathlib.Path | None = None,
    ready_timeout_seconds: float = 300.0,
    apply_timeout_seconds: float = 120.0,
) -> list[str]:
    """Build a shared-worker argv for one named Linux directional role."""

    return peer.build_worker_command(
        python_executable,
        role="linux-peer",
        probe_role=role,
        surface=surface,
        workspace=workspace,
        interface_digest=interface_digest,
        token="phase181-linux-" + uuid.uuid4().hex,
        unity_log=unity_log,
        result_json=result_json,
        distro=distro,
        rmw=rmw,
        domain_id=domain_id,
        unity_log_offset=unity_log_offset,
        static_interface_package=static_interface_package,
        ready_timeout_seconds=ready_timeout_seconds,
        apply_timeout_seconds=apply_timeout_seconds,
    )


def _summary_path(args: argparse.Namespace, workspace: pathlib.Path) -> pathlib.Path:
    """Keep the Linux result in the caller-owned workspace unless explicitly placed."""

    return pathlib.Path(args.summary_json) if args.summary_json is not None else workspace / "phase181-linux-peer-summary.json"


def run_linux_peer(args: argparse.Namespace) -> int:
    """Build/exercise one exact Linux peer and persist a redacted durable summary."""

    caller_workspace = _require_caller_workspace(args.workspace)
    summary_path = _summary_path(args, caller_workspace)
    summary: dict[str, object] = {
        "phase": 181,
        "role": "linux-peer",
        "probeRole": args.role,
        "surface": args.surface,
        "transportScope": "linux-peer",
        "profileId": args.profile_id,
        "distro": args.distro,
        "rmwImplementation": args.rmw,
        "domainId": args.domain_id,
        "processOwnership": {"workspaceOwned": False},
        "commandLabels": {},
    }
    worker_process: subprocess.Popen[str] | None = None
    worker_stream = None
    failure: LinuxPeerFailure | None = None
    exit_code = 1
    try:
        environment = build_linux_environment(args)
        static_package = pathlib.Path(args.static_interface_package)
        lock = peer.load_static_interface_lock(static_package)
        try:
            protocol.require_interface_digest(lock.interface_digest, args.interface_digest or lock.interface_digest)
        except protocol.ProtocolFailure as exc:
            raise LinuxPeerFailure(exc.code, "The requested Linux peer digest does not match the exact static source lock.") from exc
        summary.update(
            {
                "interfacePackage": peer.STATIC_INTERFACE_PACKAGE_ID,
                "rosPackageName": lock.ros_package_name,
                "interfaceRevision": lock.interface_revision,
                "interfaceDigest": lock.interface_digest,
                "interfaceDigestPrefix": protocol.digest_prefix(lock.interface_digest),
            }
        )
        stage_or_verify_locked_ros_source(static_package, caller_workspace, lock.ros_package_name)
        colcon = resolve_linux_colcon(args.colcon)
        colcon_command = peer.build_colcon_command(colcon, lock.ros_package_name)
        summary["commandLabels"] = {"colcon": protocol.bounded_command_label(colcon_command)}
        peer.run_logged_owned_command(
            colcon_command,
            cwd=caller_workspace,
            env=environment,
            log_path=caller_workspace / "phase181-linux-colcon.log",
            timeout_seconds=peer.peer_build_timeout_seconds(),
            failure_code="FAIL_PEER_BUILD",
            stream_output=True,
            output_prefix="[phase181:linux-peer][build] ",
        )

        install = caller_workspace / "install"
        worker_environment = build_linux_worker_environment(environment, install)
        worker_result = caller_workspace / "phase181-linux-worker-result.json"
        marker_offset = args.unity_log_offset if args.unity_log_offset is not None else protocol.log_offset(args.unity_log)
        worker_command = build_linux_worker_command(
            resolve_linux_python(args.python),
            workspace=caller_workspace,
            interface_digest=lock.interface_digest,
            role=args.role,
            unity_log=args.unity_log,
            result_json=worker_result,
            distro=args.distro,
            rmw=args.rmw,
            domain_id=args.domain_id,
            surface=args.surface,
            unity_log_offset=marker_offset,
            static_interface_package=static_package,
            ready_timeout_seconds=args.ready_timeout_seconds,
            apply_timeout_seconds=args.apply_timeout_seconds,
        )
        summary["commandLabels"] = {
            **summary["commandLabels"],
            "worker": protocol.bounded_command_label(worker_command),
        }
        worker_stream = (caller_workspace / "phase181-linux-worker.log").open("w", encoding="utf-8", errors="replace")
        worker_process = subprocess.Popen(
            worker_command,
            cwd=str(caller_workspace),
            env=worker_environment,
            text=True,
            stdout=worker_stream,
            stderr=subprocess.STDOUT,
            shell=False,
            **peer.worker_launch_options(),
        )
        summary["processOwnership"] = {"workspaceOwned": False, "workerPid": worker_process.pid}
        try:
            worker_exit = worker_process.wait(timeout=args.ready_timeout_seconds + args.apply_timeout_seconds + 30.0)
        except subprocess.TimeoutExpired as exc:
            peer._terminate_owned_child(worker_process)
            raise LinuxPeerFailure("FAIL_WORKER_TIMEOUT", "The Linux custom ROS2 peer exceeded its bounded acceptance window.") from exc
        worker_evidence = peer.read_successful_worker_result(worker_result, lock)
        if worker_exit != 0:
            raise LinuxPeerFailure("FAIL_WORKER_EXIT", "The Linux typed worker returned a nonzero status after PASS evidence.")
        summary["unityMarkerOffsets"] = {"start": marker_offset, "end": worker_evidence.get("markerOffsetEnd")}
        summary["workerEvidence"] = worker_evidence
        summary["verdict"] = "PASS"
        exit_code = 0
    except LinuxPeerFailure as exc:
        failure = exc
    except peer.PeerFailure as exc:
        failure = LinuxPeerFailure(exc.code, "The shared custom-interface peer preflight failed.")
    except (OSError, subprocess.SubprocessError):
        failure = LinuxPeerFailure("FAIL_ENVIRONMENT", "A Linux peer-owned helper process could not start or complete.")
    finally:
        if worker_process is not None and worker_process.poll() is None:
            peer._terminate_owned_child(worker_process)
        if worker_stream is not None:
            worker_stream.close()
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


def main(argv: Sequence[str] | None = None) -> int:
    """Run one caller-owned Linux custom-interface peer role."""

    return run_linux_peer(parse_args(argv))


if __name__ == "__main__":
    raise SystemExit(main())
