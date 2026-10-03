#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Owned Windows process orchestration for Phase186-H live acceptance."""

from __future__ import annotations
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase186_bridge_live.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE

import contextlib
import dataclasses
import json
import os
import pathlib
import shutil
import socket
import subprocess
import sys
import tempfile
import time
from collections.abc import Mapping, Sequence
from typing import Any, BinaryIO

import psutil

from Scripts.smoke.foxrun import phase184_profile_acceptance as process_support
from Scripts.smoke.foxrun import phase186_bridge_acceptance_protocol as protocol
from Scripts.smoke.foxrun import phase186_bridge_build as bridge_build
from Scripts.smoke.foxrun import phase186_bridge_live_peer as live_peer
from Scripts.smoke.ros2 import phase179_zenoh_topology
from Scripts.smoke.ros2 import phase181_custom_ros2_peer as phase181_peer


WORKER_ROLES = frozenset(live_peer.ROLES)
UNITY_EXECUTE_METHOD = (
    "Unity2Foxglove.Phase186BatchModeRos2BridgeProbe.Run"
)
MANUAL_POINTER = pathlib.Path(
    "Unity2Foxglove/Library/Phase186Acceptance/current-run.json"
)
MAX_DOCUMENT_BYTES = 4 * 1024 * 1024
# The worker owns a full 300-second operation window.  The coordinator must
# outlive that window so a terminal actor document written at the boundary is
# not misclassified as expired.
LIVE_ACTOR_RESULT_TIMEOUT_SECONDS = (
    live_peer.LIVE_ACTOR_OPERATION_TIMEOUT_SECONDS + 30.0
)
MANUAL_EDITOR_RELEASE_TIMEOUT_SECONDS = 300.0


class LiveFailure(protocol.ProtocolFailure):
    """Stable live orchestration failure."""


class LiveNotRun(LiveFailure):
    """One exact live prerequisite is missing."""

    def __init__(self, prerequisite: str):
        """Initialize the helper state."""
        self.prerequisite = str(prerequisite)[:512]
        super().__init__("NOT_RUN_LIVE_PREREQUISITE", self.prerequisite)


@dataclasses.dataclass(frozen=True)
class PreparedRuntime:
    """Represent prepared runtime."""
    row_id: str
    distro: str
    rmw: str
    ros2_root: pathlib.Path
    overlay_install: pathlib.Path
    python_executable: pathlib.Path
    bridge_executable: pathlib.Path
    environment: Mapping[str, str]
    build_summary: Mapping[str, Any]
    zenoh_router: pathlib.Path | None = None
    zenoh_router_environment: Mapping[str, str] | None = None
    zenoh_endpoint: tuple[str, int] | None = None


@dataclasses.dataclass
class ProcessRecord:
    """Represent process record."""
    key: str
    logical_role: str
    executable: pathlib.Path
    process: subprocess.Popen[bytes]
    stdout: BinaryIO
    stderr: BinaryIO
    identity_verified: bool
    owner_requested: bool = False


class OwnedLiveProcesses:
    """One kill-on-close owner plus exact root-process evidence."""

    def __init__(self) -> None:
        """Initialize the helper state."""
        self._job = process_support.WindowsKillOnCloseJob()
        self._owner = process_support.OwnedProcessSet(self._job)
        self._records: dict[str, ProcessRecord] = {}

    @staticmethod
    def _same_path(left: pathlib.Path, right: pathlib.Path) -> bool:
        """Handle same path for Phase186 acceptance."""
        return os.path.normcase(str(left.resolve())) == os.path.normcase(str(right.resolve()))

    def launch(
        self,
        key: str,
        logical_role: str,
        command: Sequence[str],
        *,
        cwd: pathlib.Path,
        environment: Mapping[str, str],
        output_root: pathlib.Path,
    ) -> ProcessRecord:
        """Handle launch for Phase186 acceptance."""
        if key in self._records or not command:
            raise LiveFailure("FAIL_PROCESS_IDENTITY", "duplicate or empty process launch")
        executable = pathlib.Path(command[0]).resolve(strict=True)
        stdout_path = output_root / "processes" / f"{key}.stdout.log"
        stderr_path = output_root / "processes" / f"{key}.stderr.log"
        stdout_path.parent.mkdir(parents=True, exist_ok=True)
        stdout = stdout_path.open("wb")
        stderr = stderr_path.open("wb")
        try:
            process = subprocess.Popen(
                list(command),
                cwd=cwd,
                env=dict(environment),
                stdin=subprocess.DEVNULL,
                stdout=stdout,
                stderr=stderr,
                shell=False,
                **process_support.process_group_options(),
            )
            self._owner.register(key, process)
            identity_verified = False
            deadline = time.monotonic() + 5.0
            while time.monotonic() < deadline and process.poll() is None:
                try:
                    actual = pathlib.Path(psutil.Process(process.pid).exe())
                    identity_verified = self._same_path(actual, executable)
                except (OSError, psutil.Error):
                    identity_verified = False
                if identity_verified:
                    break
                time.sleep(0.02)
            if not identity_verified:
                raise LiveFailure(
                    "FAIL_PROCESS_IDENTITY",
                    f"{logical_role} executable identity could not be proven",
                )
            record = ProcessRecord(
                key,
                logical_role,
                executable,
                process,
                stdout,
                stderr,
                True,
            )
            self._records[key] = record
            return record
        except BaseException:
            stdout.close()
            stderr.close()
            raise

    def stop(self, key: str) -> int:
        """Handle stop for Phase186 acceptance."""
        record = self._records[key]
        record.owner_requested = record.process.poll() is None
        return self._owner.stop(key)

    def close(self) -> None:
        """Release the owned acceptance resources."""
        for record in self._records.values():
            if record.process.poll() is None:
                record.owner_requested = True
        self._owner.close()
        for record in self._records.values():
            with contextlib.suppress(OSError):
                record.stdout.close()
            with contextlib.suppress(OSError):
                record.stderr.close()

    def record(self, key: str) -> ProcessRecord:
        """Handle record for Phase186 acceptance."""
        return self._records[key]

    def has_record(self, key: str) -> bool:
        """Return whether record."""
        return key in self._records

    def poll(self, key: str) -> int | None:
        """Handle poll for Phase186 acceptance."""
        return self._records[key].process.poll()

    def actor_evidence(
        self,
        logical_role: str,
        *,
        preferred_key: str | None = None,
        allow_role_alias: bool = False,
    ) -> dict[str, Any]:
        """Handle actor evidence for Phase186 acceptance."""
        candidates = [
            record for record in self._records.values() if record.logical_role == logical_role
        ]
        if preferred_key is not None and allow_role_alias:
            selected = self._records.get(preferred_key)
            candidates = [] if selected is None else [selected]
        elif preferred_key is not None:
            candidates = [record for record in candidates if record.key == preferred_key]
        if not candidates:
            raise LiveFailure("FAIL_PROCESS_IDENTITY", f"{logical_role} process is absent")
        record = candidates[-1]
        exit_code = record.process.poll()
        if exit_code is None:
            raise LiveFailure("FAIL_CLEANUP", f"{logical_role} process is still running")
        return {
            "pid": int(record.process.pid),
            "executable": str(record.executable),
            "started": True,
            "ready": True,
            "identityVerified": record.identity_verified,
            "exited": True,
            "exitCode": int(exit_code),
            "termination": "owner-requested" if record.owner_requested else "self",
            "processRole": record.logical_role,
            "cohosted": record.logical_role != logical_role,
        }

    def residual_pids(self) -> list[int]:
        """Handle residual pids for Phase186 acceptance."""
        return sorted(
            record.process.pid
            for record in self._records.values()
            if record.process.poll() is None
        )


def _choose_port(excluded: set[int]) -> int:
    """Handle choose port for Phase186 acceptance."""
    for _ in range(32):
        with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as probe:
            if os.name == "nt":
                probe.setsockopt(socket.SOL_SOCKET, socket.SO_EXCLUSIVEADDRUSE, 1)
            probe.bind(("127.0.0.1", 0))
            selected = int(probe.getsockname()[1])
        if selected not in excluded:
            return selected
    raise LiveFailure("FAIL_PREFLIGHT", "distinct loopback port could not be selected")


def _clean_unity_environment(source: Mapping[str, str]) -> dict[str, str]:
    """Clean unity environment."""
    environment = dict(source)
    for key in tuple(environment):
        folded = key.upper()
        if folded in {
            "AMENT_PREFIX_PATH",
            "CMAKE_PREFIX_PATH",
            "COLCON_PREFIX_PATH",
            "PYTHONPATH",
            "ROS_VERSION",
            "ROS_PYTHON_VERSION",
            "ROS_DISTRO",
            "RMW_IMPLEMENTATION",
            "ROS_DOMAIN_ID",
            "ROS_LOCALHOST_ONLY",
            "ROS_AUTOMATIC_DISCOVERY_RANGE",
            "ROS_DISCOVERY_SERVER",
            "FASTDDS_BUILTIN_TRANSPORTS",
            "ZENOH_ROUTER_CONFIG_URI",
            "ZENOH_SESSION_CONFIG_URI",
            "ZENOH_CONFIG_OVERRIDE",
        }:
            environment.pop(key, None)
    return environment


def _live_discovery_range(config: Mapping[str, Any]) -> str:
    """Match the packaged FastDDS policy already certified by Phase184."""

    if (
        config.get("caseId") == "fanout-fairness-health"
        and config.get("rmw") == "rmw_fastrtps_cpp"
    ):
        return "SUBNET"
    return "LOCALHOST"


def _build_unity_environment(
    source: Mapping[str, str],
    config: Mapping[str, Any],
) -> dict[str, str]:
    """Keep Bridge-only Unity ROS-free and bind all-Provider fanout exactly."""

    environment = _clean_unity_environment(source)
    if config.get("caseId") != "fanout-fairness-health":
        return environment

    domain_id = config.get("domainId")
    if type(domain_id) is not int or not (
        0 <= domain_id <= protocol.WINDOWS_SAFE_ROS_DOMAIN_ID_MAX
    ):
        raise LiveFailure(
            "FAIL_PROTOCOL",
            "all-Provider Unity run has an invalid ROS domain",
        )
    environment["ROS_DOMAIN_ID"] = str(domain_id)
    environment["ROS_AUTOMATIC_DISCOVERY_RANGE"] = _live_discovery_range(config)
    if config.get("rmw") == "rmw_fastrtps_cpp":
        # Keep the packaged R2FU participant on the same proven Windows
        # transport policy as every repository-owned FastDDS live runner.
        # FastDDS shared-memory setup can otherwise block rcl_node_init before
        # the first Unity publisher exists.
        environment["FASTDDS_BUILTIN_TRANSPORTS"] = "UDPv4"
    return environment


def _build_runtime_environment(
    source: Mapping[str, str],
    ros2_root: pathlib.Path,
    overlay_install: pathlib.Path,
    *,
    distro: str,
    rmw: str,
    domain_id: int,
    discovery_range: str,
    topology_id: str,
    zenoh_session_config: pathlib.Path | None,
) -> dict[str, str]:
    """Build one exact ROS environment including distribution-owned DLLs."""

    # build_ros_env intentionally replaces PATH instead of appending the
    # caller's Python/Conda/Codex paths.  A same-named ambient DLL can satisfy
    # the Windows loader and still fail rclpy with an ABI-missing procedure.
    _ = source
    environment = phase181_peer.ros2env.build_ros_env(
        ros2_root,
        rmw,
        discovery_range,
        str(domain_id),
        distro,
    )
    pixi = ros2_root / ".pixi" / "envs" / "default"
    prefixes = [
        overlay_install / "bin",
        overlay_install / "Lib",
        ros2_root / "bin",
        *phase181_peer.ros2env.ros2_opt_bin_paths(ros2_root),
        pixi,
        pixi / "Library" / "bin",
        pixi / "Scripts",
    ]
    existing = environment.get("PATH", "")
    environment["PATH"] = os.pathsep.join(
        [*(str(path) for path in prefixes if path.is_dir()), existing]
    ).strip(os.pathsep)
    existing_python = environment.get("PYTHONPATH", "")
    environment["PYTHONPATH"] = os.pathsep.join(
        [str(overlay_install / "Lib" / "site-packages"), existing_python]
    ).strip(os.pathsep)
    prefixes = (str(overlay_install), str(ros2_root))
    for name in ("AMENT_PREFIX_PATH", "CMAKE_PREFIX_PATH", "COLCON_PREFIX_PATH"):
        environment[name] = os.pathsep.join(prefixes)
    phase181_peer.apply_explicit_zenoh_session_config(
        environment,
        rmw=rmw,
        zenoh_session_config=zenoh_session_config,
    )
    environment["UNITY2FOXGLOVE_ZENOH_TOPOLOGY_ID"] = topology_id
    return environment


def prepare_runtime(
    repository: pathlib.Path,
    config: Mapping[str, Any],
    *,
    reporter: Any | None = None,
) -> PreparedRuntime:
    """Handle prepare runtime for Phase186 acceptance."""
    if reporter is not None:
        reporter.transition(
            "2/5", "preparing exact runtime and Bridge build"
        )
    row_id = str(config["runtimeRowId"] or "")
    if not row_id:
        raise LiveNotRun("an exact Phase186 ROS/RMW runtime row")
    row = bridge_build.require_row(row_id)
    build_root = repository / "build" / "phase186" / "bridge"
    summary = bridge_build.run_row(
        repository,
        row,
        build_root,
        run_tests=True,
    )
    verdict = str(summary.get("verdict", ""))
    if verdict == "NOT RUN":
        raise LiveNotRun(
            str(summary.get("missingPrerequisite", row_id + " runtime"))
        )
    if verdict != "PASS":
        raise LiveFailure("FAIL_BUILD", f"{row_id} Bridge build/test did not pass")
    ros2_root = pathlib.Path(str(summary["ros2Root"])).resolve(strict=True)
    overlay = pathlib.Path(
        str(summary["overlayAuthority"]["installPrefix"])
    ).resolve(strict=True)
    toolchain = phase181_peer.resolve_windows_peer_toolchain(ros2_root)
    zenoh_router: pathlib.Path | None = None
    zenoh_environment: Mapping[str, str] | None = None
    zenoh_endpoint: tuple[str, int] | None = None
    zenoh_session: pathlib.Path | None = None
    if row.rmw == "rmw_zenoh_cpp":
        port = _choose_port({int(config["bridgePort"]), int(config["foxglovePort"])})
        endpoint = f"tcp/127.0.0.1:{port}"
        templates = ros2_root / "share" / "rmw_zenoh_cpp" / "config"
        owned = phase179_zenoh_topology.create_owned_local_router_config(
            router_template=templates / "DEFAULT_RMW_ZENOH_ROUTER_CONFIG.json5",
            session_template=templates / "DEFAULT_RMW_ZENOH_SESSION_CONFIG.json5",
            output_directory=pathlib.Path(str(config["outputRoot"])) / "zenoh",
            endpoint=endpoint,
        )
        zenoh_session = owned.session_config
        zenoh_router = (ros2_root / "Lib" / "rmw_zenoh_cpp" / "rmw_zenohd.exe").resolve(
            strict=True
        )
        router_env = phase181_peer.ros2env.build_ros_env(
            ros2_root,
            row.rmw,
            "LOCALHOST",
            str(config["domainId"]),
            row.distro,
        )
        router_env["ZENOH_ROUTER_CONFIG_URI"] = str(owned.router_config)
        router_env["ZENOH_SESSION_CONFIG_URI"] = str(owned.session_config)
        zenoh_environment = router_env
        zenoh_endpoint = ("127.0.0.1", port)
    environment = _build_runtime_environment(
        os.environ,
        ros2_root,
        overlay,
        distro=row.distro,
        rmw=row.rmw,
        domain_id=int(config["domainId"]),
        discovery_range=_live_discovery_range(config),
        topology_id="phase186h-" + str(config["tokenHash"])[:12],
        zenoh_session_config=zenoh_session,
    )
    bridge_executable = (
        build_root / row_id / "cpp-build" / "unity2foxglove_ros2_bridge.exe"
    ).resolve(strict=True)
    return PreparedRuntime(
        row_id,
        row.distro,
        row.rmw,
        ros2_root,
        overlay,
        pathlib.Path(toolchain.python_executable).resolve(strict=True),
        bridge_executable,
        environment,
        summary,
        zenoh_router,
        zenoh_environment,
        zenoh_endpoint,
    )


__all__ = [name for name in globals() if not name.startswith("__")]
