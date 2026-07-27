#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Owned Phase184-G Unity Editor Batch and focused manual acceptance harness.

The parent owns every helper process and every durable artifact below one
``build/phase184/acceptance/<run-id>`` directory.  Worker entry points receive
only the already-validated immutable run configuration.  They never infer a
ROS installation, domain, endpoint, topic, or output path from ambient state.
"""

from __future__ import annotations

import argparse
import asyncio
import base64
import contextlib
import ctypes
import datetime as dt
import hashlib
import json
import os
import pathlib
import re
import secrets
import shutil
import signal
import socket
import struct
import subprocess
import sys
import threading
import time
import uuid
from dataclasses import dataclass
from typing import Any, Iterable, Mapping, Sequence, TextIO


SCRIPT_PATH = pathlib.Path(__file__).resolve()
SCRIPT_DIRECTORY = SCRIPT_PATH.parent
ROS2_SMOKE_DIRECTORY = SCRIPT_DIRECTORY.parent / "ros2"
for _import_directory in (SCRIPT_DIRECTORY, ROS2_SMOKE_DIRECTORY):
    if str(_import_directory) not in sys.path:
        sys.path.insert(0, str(_import_directory))

import phase184_profile_acceptance_protocol as protocol
import phase184_foxglove_desktop_live_protocol as desktop_live_protocol


WORKER_ROLES = ("foxglove-client", "ros2-peer", "graph-observer")
DESKTOP_CLIENT_BARRIER_ENV = "PHASE184H_DESKTOP_CLIENT_BARRIER"
UNITY_EXECUTE_METHOD = "Unity2Foxglove.Phase184BatchModeProfileProbe.Run"
INTERFACE_PACKAGE_ID = "dev.unity2foxglove.foxrun.ros2.interfaces"
LOCK_RELATIVE_PATH = pathlib.Path("RuntimeSupport/foxrun-ros2-interface-lock.json")
UNITY_ZENOH_SETTINGS_RELATIVE_PATH = pathlib.Path(
    "Unity2Foxglove/Library/Unity2Foxglove/R2fuZenohRouterSettings.json"
)
DEFAULT_UNITY_VERSION = "6000.3.14f1"
MAX_CONFIG_BYTES = 1024 * 1024
MAX_UNITY_ZENOH_SETTINGS_BYTES = 16 * 1024
MAX_FRAME_HEADER_BYTES = 64 * 1024
MAX_FRAME_PAYLOAD_BYTES = 64 * 1024 * 1024
U2R2_MAGIC = b"U2R2"
U2R2_VERSION = 1
FOXGLOVE_SUBPROTOCOL = "foxglove.sdk.v1"
FOXGLOVE_MESSAGE_OPCODE = 1
DEGRADED_CLIENT_READY_TOPIC = "/foxrun/phase184/degraded/client_ready"
_SAFE_MARKER_FIELD = re.compile(r"\A[A-Za-z0-9._:/,+-]{1,512}\Z")
_UNITY_VERSION = re.compile(r"\bVersion is '([^']+)'")
_PROCESS_IMPORTED_UNIX_SECONDS = time.time()
MANUAL_ENTRY_TIMEOUT_SECONDS = 900.0
MANUAL_REVIEW_TIMEOUT_SECONDS = 900.0
WINDOWS_SAFE_ROS_DOMAIN_ID_MAX = 166
_UNITY_RUNTIME_PACKAGE_PREFIX = "dev.unity2foxglove.ros2forunity.runtime."
_UNITY_TYPESUPPORT_PACKAGE_PREFIX = (
    "dev.unity2foxglove.foxrun.ros2.interfaces.typesupport."
)
_UNITY_RUNTIME_DEFINE = "UNITY2FOXGLOVE_ROS2_FOR_UNITY"
_UNITY_TYPESUPPORT_DEFINE = "UNITY2FOXGLOVE_FOXRUN_CUSTOM_ROS2_INTERFACES"
_BRIDGE_PACKAGE_NAME = "unity2foxglove_ros2_bridge"
_BRIDGE_CACHE_FORMAT = 1
_BRIDGE_CACHE_OWNER = "phase184g-windows-bridge-cache"
_BRIDGE_CACHE_OWNERSHIP_NAME = ".phase184g-bridge-cache-owned.json"
_BRIDGE_CACHE_MANIFEST_NAME = ".phase184g-bridge-cache.json"
_BRIDGE_SOURCE_IGNORES = frozenset(
    {"build", "install", "log", "bin", "obj", "__pycache__"}
)


class AcceptanceFailure(protocol.ProtocolFailure):
    """Stable Phase184-G failure that is safe to persist."""


@dataclass(frozen=True)
class TerminalMarker:
    """One exact current-run terminal marker from the dedicated Unity log."""

    verdict: str
    line: str
    fields: Mapping[str, str]


@dataclass(frozen=True)
class StaticInterfaceIdentity:
    """Locked Phase181 custom-interface facts consumed by Phase184-G."""

    package: str
    envelope_type: str
    payload_type: str
    digest: str
    revision: int


@dataclass(frozen=True)
class UnityZenohRouterEndpoint:
    """Exact loopback router endpoint that the selected Unity Editor will use."""

    endpoint: str
    host: str
    port: int


def repository_root() -> pathlib.Path:
    """Find the repository without traversing local ROS junctions."""

    for candidate in (SCRIPT_DIRECTORY, *SCRIPT_DIRECTORY.parents):
        if (candidate / "Packages").is_dir() and (candidate / "Scripts").is_dir():
            return candidate
    raise AcceptanceFailure("FAIL_PREFLIGHT", "Could not resolve the repository root.")


def _bridge_cache_install_path(
    repository: pathlib.Path,
    profile: str,
) -> pathlib.Path:
    """Return the profile-stable Bridge path used by Windows Firewall identity."""

    if profile not in protocol.PROFILE_CONTRACTS:
        raise AcceptanceFailure("FAIL_RUNTIME_SELECTION", "Unknown Bridge cache profile.")
    return (
        pathlib.Path(repository)
        / "build"
        / "phase184"
        / "bridge-cache"
        / profile
        / "bridge-overlay"
        / "install"
    ).resolve()


def parse_args(argv: Sequence[str] | None = None) -> argparse.Namespace:
    """Parse parent and worker surfaces without silently sharing options."""

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--worker", choices=WORKER_ROLES)
    parser.add_argument("--run-config", type=pathlib.Path)
    parser.add_argument("--case", choices=tuple(protocol.CASE_CONTRACTS))
    parser.add_argument("--profile", choices=tuple(protocol.PROFILE_CONTRACTS))
    parser.add_argument("--manual-editor", action="store_true")
    parser.add_argument("--wait-for-desktop-client", action="store_true")
    parser.add_argument("--unity-editor", type=pathlib.Path)
    parser.add_argument("--domain-id", type=int)
    parser.add_argument("--foxglove-port", type=int)
    parser.add_argument("--bridge-port", type=int)
    parser.add_argument("--run-id")
    parser.add_argument(
        "--retain-success-workspace",
        action="store_true",
        help="Retain the completed run directory and all evidence (the default durable behavior).",
    )
    return parser.parse_args(argv)


def validate_arguments(args: argparse.Namespace) -> argparse.Namespace:
    """Reject cross-mode options before reading or changing external state."""

    if args.worker is not None:
        if args.run_config is None:
            raise AcceptanceFailure("FAIL_PREFLIGHT", "Worker mode requires --run-config.")
        parent_values = (
            args.case,
            args.profile,
            args.manual_editor,
            args.wait_for_desktop_client,
            args.unity_editor,
            args.domain_id,
            args.foxglove_port,
            args.bridge_port,
            args.run_id,
            args.retain_success_workspace,
        )
        if any(value not in (None, False) for value in parent_values):
            raise AcceptanceFailure(
                "FAIL_PREFLIGHT",
                "Worker mode rejects parent-only case, profile, Unity, and allocation options.",
            )
        args.execution_mode = "worker"
        return args

    if args.run_config is not None:
        raise AcceptanceFailure(
            "FAIL_PREFLIGHT",
            "Parent mode owns run-config creation and rejects --run-config.",
        )
    if args.case is None:
        raise AcceptanceFailure("FAIL_PREFLIGHT", "Parent mode requires one --case.")
    if args.wait_for_desktop_client and (
        args.manual_editor or args.case != "foxglove-profile"
    ):
        raise AcceptanceFailure(
            "FAIL_PREFLIGHT",
            "Desktop client waiting is limited to the Batch foxglove-profile case.",
        )
    if args.case != "foxglove-profile" and args.profile is None:
        raise AcceptanceFailure(
            "FAIL_RUNTIME_SELECTION",
            "ROS-backed cases require their explicit representative --profile.",
        )
    contract = protocol.validate_case_profile(args.case, args.profile)
    args.profile = contract.profile
    if args.unity_editor is None:
        raise AcceptanceFailure(
            "FAIL_UNITY_STARTUP",
            "Parent mode requires an explicit Unity Editor executable.",
        )
    args.execution_mode = "manual" if args.manual_editor else "batch"
    return args


def build_unity_batch_command(
    editor: pathlib.Path,
    project: pathlib.Path,
    run_config: pathlib.Path,
    unity_log: pathlib.Path,
) -> list[str]:
    """Build the one direct owned Editor Batch command."""

    return [
        str(pathlib.Path(editor)),
        "-batchmode",
        "-nographics",
        "-projectPath",
        str(pathlib.Path(project)),
        "-executeMethod",
        UNITY_EXECUTE_METHOD,
        "-phase184RunConfig",
        str(pathlib.Path(run_config)),
        "-logFile",
        str(pathlib.Path(unity_log)),
    ]


def build_worker_command(
    python_executable: pathlib.Path,
    role: str,
    run_config: pathlib.Path,
) -> list[str]:
    """Build one exact worker argv with no shell or ambient fallback."""

    if role not in WORKER_ROLES:
        raise AcceptanceFailure("FAIL_PREFLIGHT", "Unknown Phase184-G worker role.")
    return [
        str(pathlib.Path(python_executable)),
        str(SCRIPT_PATH),
        "--worker",
        role,
        "--run-config",
        str(pathlib.Path(run_config)),
    ]


def build_bridge_command(
    bridge_executable: pathlib.Path,
    host: str,
    port: int,
) -> list[str]:
    """Build the installed native Bridge invocation with app arguments."""

    if host not in {"127.0.0.1", "localhost", "::1"} or not 1 <= int(port) <= 65535:
        raise AcceptanceFailure("FAIL_BRIDGE", "Bridge endpoint is not a valid loopback endpoint.")
    return [
        str(pathlib.Path(bridge_executable)),
        "--host",
        host,
        "--port",
        str(port),
        "--payload-format",
        "cdr-with-encapsulation",
    ]


def _without_desktop_client_barrier(
    source: Mapping[str, str],
) -> dict[str, str]:
    """Copy an environment without the parent-owned Desktop barrier seam."""

    environment = dict(source)
    environment.pop(DESKTOP_CLIENT_BARRIER_ENV, None)
    return environment


def _clean_environment(source: Mapping[str, str]) -> dict[str, str]:
    """Remove ambient ROS/topology selection while preserving required host basics."""

    environment = _without_desktop_client_barrier(source)
    for key in (
        "AMENT_PREFIX_PATH",
        "CMAKE_PREFIX_PATH",
        "COLCON_PREFIX_PATH",
        "PYTHONPATH",
        "ROS_DISTRO",
        "ROS_VERSION",
        "ROS_PYTHON_VERSION",
        "RMW_IMPLEMENTATION",
        "ROS_DOMAIN_ID",
        "ROS_LOCALHOST_ONLY",
        "ROS_DISCOVERY_SERVER",
        "ROS_AUTOMATIC_DISCOVERY_RANGE",
        "ZENOH_ROUTER_CONFIG_URI",
        "ZENOH_SESSION_CONFIG_URI",
        "ZENOH_CONFIG_OVERRIDE",
        "UNITY2FOXGLOVE_ZENOH_TOPOLOGY_ID",
    ):
        environment.pop(key, None)
    return environment


def build_ros_actor_environment(
    source: Mapping[str, str],
    *,
    bridge_install: pathlib.Path | None,
    peer_install: pathlib.Path,
    ros2_root: pathlib.Path,
    distro: str,
    rmw: str,
    domain_id: int,
    discovery_range: str,
    topology_id: str,
    zenoh_session_config: pathlib.Path | None,
) -> dict[str, str]:
    """Compose one explicit Bridge -> peer -> ROS prefix environment."""

    if distro not in {"humble", "jazzy", "lyrical"}:
        raise AcceptanceFailure("FAIL_RUNTIME_SELECTION", "Unsupported ROS distribution.")
    if rmw not in {"rmw_fastrtps_cpp", "rmw_zenoh_cpp"}:
        raise AcceptanceFailure("FAIL_RUNTIME_SELECTION", "Unsupported RMW implementation.")
    expected_discovery_range = (
        "SUBNET" if rmw == "rmw_fastrtps_cpp" else "LOCALHOST"
    )
    if (
        not 0 <= int(domain_id) <= 232
        or discovery_range != expected_discovery_range
    ):
        raise AcceptanceFailure("FAIL_PREFLIGHT", "ROS domain/discovery selection is invalid.")
    if rmw == "rmw_zenoh_cpp":
        if not topology_id or zenoh_session_config is None:
            raise AcceptanceFailure(
                "FAIL_RUNTIME_SELECTION",
                "Zenoh requires an owned topology identity and session configuration.",
            )
    elif topology_id or zenoh_session_config is not None:
        raise AcceptanceFailure(
            "FAIL_RUNTIME_SELECTION",
            "FastDDS cannot inherit Zenoh topology configuration.",
        )

    environment = _clean_environment(source)
    prefixes = [
        pathlib.Path(item)
        for item in (bridge_install, peer_install, ros2_root)
        if item is not None
    ]
    prefix_text = os.pathsep.join(str(item) for item in prefixes)
    environment["AMENT_PREFIX_PATH"] = prefix_text
    environment["CMAKE_PREFIX_PATH"] = prefix_text
    environment["COLCON_PREFIX_PATH"] = prefix_text
    python_entries = [
        str(item / "Lib" / "site-packages")
        for item in prefixes
    ]
    environment["PYTHONPATH"] = os.pathsep.join(python_entries)
    path_entries: list[str] = []
    for item in prefixes:
        path_entries.extend((str(item / "bin"), str(item / "Lib")))
    if environment.get("PATH"):
        path_entries.append(environment["PATH"])
    environment["PATH"] = os.pathsep.join(path_entries)
    environment["ROS_VERSION"] = "2"
    environment["ROS_PYTHON_VERSION"] = "3"
    environment["ROS_DISTRO"] = distro
    environment["RMW_IMPLEMENTATION"] = rmw
    environment["ROS_DOMAIN_ID"] = str(domain_id)
    environment["ROS_AUTOMATIC_DISCOVERY_RANGE"] = discovery_range
    if topology_id:
        environment["UNITY2FOXGLOVE_ZENOH_TOPOLOGY_ID"] = topology_id
    if zenoh_session_config is not None:
        environment["ZENOH_SESSION_CONFIG_URI"] = str(
            pathlib.Path(zenoh_session_config).resolve()
        )
    return environment


def load_static_interface_identity(repository: pathlib.Path) -> StaticInterfaceIdentity:
    """Read the tracked Phase181 lock that both Unity and ROS workers consume."""

    lock_path = (
        pathlib.Path(repository)
        / "Packages"
        / INTERFACE_PACKAGE_ID
        / LOCK_RELATIVE_PATH
    )
    try:
        lock = json.loads(lock_path.read_text(encoding="utf-8"))
        contract = lock["contracts"][0]
        package = str(lock["rosPackageName"])
        payload = str(contract["payloadMessageName"])
        envelope = str(contract["envelopeMessageName"])
        digest = str(lock["interfaceDigest"])
        revision = int(lock["interfaceRevision"])
    except (OSError, UnicodeError, json.JSONDecodeError, KeyError, IndexError, TypeError, ValueError) as exc:
        raise AcceptanceFailure(
            "FAIL_PREFLIGHT",
            "The tracked Phase181 interface lock is unavailable or malformed.",
        ) from exc
    if not re.fullmatch(r"[0-9a-f]{64}", digest):
        raise AcceptanceFailure("FAIL_PREFLIGHT", "The Phase181 interface digest is invalid.")
    return StaticInterfaceIdentity(
        package=package,
        envelope_type=f"{package}/msg/{envelope}",
        payload_type=f"{package}/msg/{payload}",
        digest=digest,
        revision=revision,
    )


def make_run_config(
    *,
    repository: pathlib.Path,
    run_id: str,
    token: str,
    case: str,
    profile: str,
    output_root: pathlib.Path,
    domain_id: int,
    foxglove_port: int,
    bridge_port: int,
    phase181_workspace: pathlib.Path,
    interface_package: str,
    interface_type: str,
    interface_digest: str,
    execution_mode: str = "batch",
) -> dict[str, object]:
    """Create the exact immutable coordination authority for one case."""

    contract = protocol.validate_case_profile(case, profile)
    selected = protocol.PROFILE_CONTRACTS[contract.profile]
    output = pathlib.Path(output_root).resolve()
    workspace = pathlib.Path(phase181_workspace).resolve()
    bridge_install = (
        _bridge_cache_install_path(repository, contract.profile)
        if "bridge" in contract.required_actors
        else (output / "bridge-overlay" / "install").resolve()
    )
    actors = sorted(
        contract.required_actors | frozenset(contract.deliberately_absent_actors)
    )
    config: dict[str, object] = {
        "schemaVersion": protocol.RUN_CONFIG_SCHEMA_VERSION,
        "executionMode": execution_mode,
        "runId": run_id,
        "token": token,
        "case": case,
        "profile": contract.profile,
        "projectPath": str((pathlib.Path(repository) / "Unity2Foxglove").resolve()),
        "outputRoot": str(output),
        "rosDistro": selected.runtime,
        "rmw": selected.rmw,
        "domainId": int(domain_id),
        "discoveryRange": selected.discovery_range,
        "zenohTopologyId": (
            f"phase184g-{run_id[-12:]}" if selected.rmw == "rmw_zenoh_cpp" else ""
        ),
        "phase181Workspace": str(workspace),
        "phase181Install": str((workspace / "install").resolve()),
        "bridgeOverlayInstall": str(bridge_install),
        "foxgloveHost": "127.0.0.1",
        "foxglovePort": int(foxglove_port),
        "bridgeHost": "127.0.0.1",
        "bridgePort": int(bridge_port),
        "interfacePackage": interface_package,
        "interfaceType": interface_type,
        "interfaceDigest": interface_digest,
        "topics": list(contract.topics),
        "observationWindows": {
            "positiveSeconds": 3,
            "negativeSeconds": 3,
            "streamProductionSeconds": 2,
            "terminalSeconds": 30,
            "teardownSeconds": 30,
        },
        "readyFiles": {
            actor: str((output / "ready" / f"{actor}.json").resolve())
            for actor in actors
        },
        "resultFiles": {
            actor: str((output / "results" / f"{actor}.json").resolve())
            for actor in actors
        },
        "unityLog": str((output / "unity-editor.log").resolve()),
    }
    protocol.validate_run_config(config, repository)
    return config


def write_private_json_atomic(path: pathlib.Path, value: Mapping[str, object]) -> None:
    """Atomically write private coordination state without redacting its token."""

    target = pathlib.Path(path)
    target.parent.mkdir(parents=True, exist_ok=True)
    temporary = target.with_name(target.name + "." + uuid.uuid4().hex + ".tmp")
    try:
        with temporary.open("x", encoding="utf-8", newline="\n") as stream:
            json.dump(value, stream, sort_keys=True, separators=(",", ":"), ensure_ascii=True)
            stream.write("\n")
            stream.flush()
            os.fsync(stream.fileno())
        with contextlib.suppress(OSError):
            os.chmod(temporary, 0o600)
        os.replace(temporary, target)
    except BaseException:
        with contextlib.suppress(OSError):
            temporary.unlink()
        raise


def load_run_config(path: pathlib.Path) -> dict[str, object]:
    """Read and validate one bounded immutable worker config."""

    config_path = pathlib.Path(path).resolve()
    try:
        if config_path.stat().st_size <= 0 or config_path.stat().st_size > MAX_CONFIG_BYTES:
            raise AcceptanceFailure("FAIL_PREFLIGHT", "run-config size is invalid.")
        value = json.loads(config_path.read_text(encoding="utf-8"))
    except AcceptanceFailure:
        raise
    except (OSError, UnicodeError, json.JSONDecodeError) as exc:
        raise AcceptanceFailure("FAIL_PREFLIGHT", "run-config is unavailable or malformed.") from exc
    if not isinstance(value, dict):
        raise AcceptanceFailure("FAIL_PREFLIGHT", "run-config root must be an object.")
    protocol.validate_run_config(value, repository_root())
    expected = pathlib.Path(str(value["outputRoot"])) / "run-config.json"
    if config_path != expected.resolve():
        raise AcceptanceFailure("FAIL_PREFLIGHT", "Worker config path is not its owned immutable path.")
    return value


class _Win32JobApi:
    """Minimal Job Object API with KILL_ON_JOB_CLOSE."""

    JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000
    PROCESS_SET_QUOTA = 0x0100
    PROCESS_TERMINATE = 0x0001
    PROCESS_QUERY_LIMITED_INFORMATION = 0x1000
    JobObjectExtendedLimitInformation = 9

    class _IO_COUNTERS(ctypes.Structure):
        _fields_ = [
            ("ReadOperationCount", ctypes.c_uint64),
            ("WriteOperationCount", ctypes.c_uint64),
            ("OtherOperationCount", ctypes.c_uint64),
            ("ReadTransferCount", ctypes.c_uint64),
            ("WriteTransferCount", ctypes.c_uint64),
            ("OtherTransferCount", ctypes.c_uint64),
        ]

    class _BASIC_LIMIT_INFORMATION(ctypes.Structure):
        _fields_ = [
            ("PerProcessUserTimeLimit", ctypes.c_int64),
            ("PerJobUserTimeLimit", ctypes.c_int64),
            ("LimitFlags", ctypes.c_uint32),
            ("MinimumWorkingSetSize", ctypes.c_size_t),
            ("MaximumWorkingSetSize", ctypes.c_size_t),
            ("ActiveProcessLimit", ctypes.c_uint32),
            ("Affinity", ctypes.c_size_t),
            ("PriorityClass", ctypes.c_uint32),
            ("SchedulingClass", ctypes.c_uint32),
        ]

    class _EXTENDED_LIMIT_INFORMATION(ctypes.Structure):
        pass

    _EXTENDED_LIMIT_INFORMATION._fields_ = [
        ("BasicLimitInformation", _BASIC_LIMIT_INFORMATION),
        ("IoInfo", _IO_COUNTERS),
        ("ProcessMemoryLimit", ctypes.c_size_t),
        ("JobMemoryLimit", ctypes.c_size_t),
        ("PeakProcessMemoryUsed", ctypes.c_size_t),
        ("PeakJobMemoryUsed", ctypes.c_size_t),
    ]

    def __init__(self) -> None:
        self.kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)

    def create_kill_on_close_job(self) -> int:
        handle = self.kernel32.CreateJobObjectW(None, None)
        if not handle:
            raise OSError(ctypes.get_last_error(), "CreateJobObjectW failed")
        info = self._EXTENDED_LIMIT_INFORMATION()
        info.BasicLimitInformation.LimitFlags = self.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
        ok = self.kernel32.SetInformationJobObject(
            handle,
            self.JobObjectExtendedLimitInformation,
            ctypes.byref(info),
            ctypes.sizeof(info),
        )
        if not ok:
            error = ctypes.get_last_error()
            self.kernel32.CloseHandle(handle)
            raise OSError(error, "SetInformationJobObject failed")
        return int(handle)

    def assign_pid(self, handle: int, pid: int) -> bool:
        access = (
            self.PROCESS_SET_QUOTA
            | self.PROCESS_TERMINATE
            | self.PROCESS_QUERY_LIMITED_INFORMATION
        )
        process_handle = self.kernel32.OpenProcess(access, False, int(pid))
        if not process_handle:
            return False
        try:
            return bool(self.kernel32.AssignProcessToJobObject(handle, process_handle))
        finally:
            self.kernel32.CloseHandle(process_handle)

    def close_handle(self, handle: int) -> None:
        self.kernel32.CloseHandle(handle)


class WindowsKillOnCloseJob:
    """Parent-held hard-close owner for every Batch helper child."""

    def __init__(self, *, api=None, platform_name: str | None = None) -> None:
        self._platform = platform_name or os.name
        self._api = api
        self._handle: int | None = None
        if self._platform == "nt":
            try:
                self._api = api or _Win32JobApi()
                self._handle = int(self._api.create_kill_on_close_job())
            except (OSError, AttributeError, ValueError) as exc:
                raise AcceptanceFailure(
                    "FAIL_PREFLIGHT",
                    "Windows kill-on-close Job Object could not be created.",
                ) from exc

    def assign(self, process) -> None:
        if self._platform != "nt":
            return
        if self._handle is None or self._api is None:
            raise AcceptanceFailure("FAIL_PREFLIGHT", "Windows Job Object is not active.")
        if not self._api.assign_pid(self._handle, int(process.pid)):
            raise AcceptanceFailure(
                "FAIL_PREFLIGHT",
                "A helper-owned child could not be assigned to the Windows Job Object.",
            )

    def close(self) -> None:
        if self._handle is None or self._api is None:
            return
        handle = self._handle
        self._handle = None
        self._api.close_handle(handle)

    def __enter__(self):
        return self

    def __exit__(self, _type, _value, _traceback):
        self.close()


def process_group_options(platform_name: str | None = None) -> dict[str, object]:
    """Give every child a graceful process-group boundary."""

    platform = platform_name or os.name
    if platform == "nt":
        return {"creationflags": getattr(subprocess, "CREATE_NEW_PROCESS_GROUP", 0x200)}
    return {"start_new_session": True}


def terminate_owned_process(process, *, grace_seconds: float = 10.0) -> int:
    """Gracefully stop only one caller-owned Popen, then bound the fallback."""

    exit_code = process.poll()
    if exit_code is not None:
        return int(exit_code)
    try:
        if os.name == "nt":
            process.send_signal(getattr(signal, "CTRL_BREAK_EVENT", signal.SIGTERM))
        else:
            os.killpg(process.pid, signal.SIGTERM)
        return int(process.wait(timeout=grace_seconds))
    except (OSError, ProcessLookupError, subprocess.TimeoutExpired):
        with contextlib.suppress(OSError):
            process.kill()
        try:
            return int(process.wait(timeout=3.0))
        except subprocess.TimeoutExpired:
            return -1


def process_exit_is_acceptable(
    role: str,
    exit_code: int,
    *,
    owner_requested: bool,
) -> bool:
    """Classify one raw child exit without rewriting Windows daemon evidence."""

    return protocol.process_exit_is_acceptable(
        role,
        exit_code,
        owner_requested=owner_requested,
    )


class OwnedProcessSet:
    """Single owner for named children and their retained exit evidence."""

    def __init__(self, job: WindowsKillOnCloseJob | None) -> None:
        self._job = job
        self._processes: dict[str, Any] = {}
        self._exit_codes: dict[str, int] = {}
        self._owner_stopped_roles: set[str] = set()
        self._closed = False

    def register(self, role: str, process):
        try:
            if self._closed or role in self._processes:
                raise AcceptanceFailure(
                    "FAIL_PREFLIGHT",
                    "Duplicate or late process registration.",
                )
            if self._job is not None:
                self._job.assign(process)
            self._processes[role] = process
            return process
        except BaseException:
            terminate_owned_process(process)
            raise

    def process(self, role: str):
        return self._processes.get(role)

    def stop(self, role: str) -> int:
        """Stop one registered child while retaining exact owner evidence."""

        if self._closed:
            raise AcceptanceFailure(
                "FAIL_CLEANUP",
                "A closed process owner cannot stop another child.",
            )
        process = self._processes.get(role)
        if process is None:
            raise AcceptanceFailure(
                "FAIL_CLEANUP",
                "The requested process role is not owned.",
            )
        if process.poll() is None:
            self._owner_stopped_roles.add(role)
        exit_code = terminate_owned_process(process)
        self._exit_codes[role] = exit_code
        return exit_code

    def close(self) -> None:
        if self._closed:
            return
        self._closed = True
        for role, process in reversed(tuple(self._processes.items())):
            if process.poll() is None:
                self._owner_stopped_roles.add(role)
            self._exit_codes[role] = terminate_owned_process(process)
        if self._job is not None:
            self._job.close()

    def exit_codes(self) -> dict[str, int]:
        result = dict(self._exit_codes)
        for role, process in self._processes.items():
            exit_code = process.poll()
            if exit_code is not None:
                result[role] = int(exit_code)
        return result

    def all_stopped(self) -> bool:
        """Return cleanup state without exposing the owner's process registry."""

        return all(process.poll() is not None for process in self._processes.values())

    def owner_stopped_roles(self) -> frozenset[str]:
        """Return actors whose termination was initiated by this exact owner."""

        return frozenset(self._owner_stopped_roles)

    def __enter__(self):
        return self

    def __exit__(self, _type, _value, _traceback):
        self.close()


def encode_u2r2_frame(header: Mapping[str, object], payload: bytes) -> bytes:
    """Encode one bounded U2R2 v1 frame."""

    header_bytes = json.dumps(
        dict(header),
        sort_keys=True,
        separators=(",", ":"),
        ensure_ascii=True,
    ).encode("utf-8")
    payload = bytes(payload)
    if not 0 < len(header_bytes) <= MAX_FRAME_HEADER_BYTES:
        raise AcceptanceFailure("FAIL_BRIDGE", "U2R2 header length is invalid.")
    if len(payload) > MAX_FRAME_PAYLOAD_BYTES:
        raise AcceptanceFailure("FAIL_BRIDGE", "U2R2 payload length is invalid.")
    return (
        U2R2_MAGIC
        + struct.pack("<HHII", U2R2_VERSION, 0, len(header_bytes), len(payload))
        + header_bytes
        + payload
    )


def decode_u2r2_frame(frame: bytes) -> tuple[dict[str, object], bytes]:
    """Decode one complete bounded U2R2 v1 frame."""

    data = bytes(frame)
    if len(data) < 16 or data[:4] != U2R2_MAGIC:
        raise AcceptanceFailure("FAIL_BRIDGE", "U2R2 response magic or length is invalid.")
    version, flags, header_size, payload_size = struct.unpack("<HHII", data[4:16])
    if version != U2R2_VERSION or flags != 0:
        raise AcceptanceFailure("FAIL_BRIDGE", "U2R2 response version or flags are invalid.")
    if (
        header_size <= 0
        or header_size > MAX_FRAME_HEADER_BYTES
        or payload_size > MAX_FRAME_PAYLOAD_BYTES
        or len(data) != 16 + header_size + payload_size
    ):
        raise AcceptanceFailure("FAIL_BRIDGE", "U2R2 response lengths are invalid.")
    try:
        header = json.loads(data[16 : 16 + header_size].decode("utf-8"))
    except (UnicodeError, json.JSONDecodeError) as exc:
        raise AcceptanceFailure("FAIL_BRIDGE", "U2R2 response JSON is invalid.") from exc
    if not isinstance(header, dict):
        raise AcceptanceFailure("FAIL_BRIDGE", "U2R2 response header must be an object.")
    return header, data[16 + header_size :]


def build_u2r2_health_frame(request_id: str) -> bytes:
    """Build one correlated Bridge health request."""

    if not _SAFE_MARKER_FIELD.fullmatch(request_id):
        raise AcceptanceFailure("FAIL_BRIDGE", "Bridge health request id is unsafe.")
    return encode_u2r2_frame(
        {"op": "health_ping", "requestId": request_id, "protocolVersion": 1},
        b"",
    )


def validate_bridge_health_response(
    header: Mapping[str, object],
    payload: bytes,
    request_id: str,
) -> None:
    """Require the exact sidecar identity and current health correlation."""

    expected = {
        "op": "health_pong",
        "requestId": request_id,
        "protocolVersion": 1,
        "status": "ok",
        "sidecarName": "unity2foxglove_ros2_bridge",
        "sidecarVersion": "0.1.0",
    }
    if dict(header) != expected or payload:
        raise AcceptanceFailure(
            "FAIL_BRIDGE",
            "Bridge health response was stale, incomplete, or from another sidecar.",
        )


def _parse_marker_fields(line: str) -> dict[str, str]:
    fields: dict[str, str] = {}
    for part in line.split()[1:]:
        if "=" not in part:
            continue
        key, value = part.split("=", 1)
        fields[key] = value
    return fields


def find_terminal_marker(
    lines: Iterable[str],
    case: str,
    token: str,
) -> TerminalMarker | None:
    """Find only the exact current case/token terminal marker."""

    result: TerminalMarker | None = None
    for raw in lines:
        line = raw.strip()
        if not (
            line.startswith("PHASE184G_CASE_PASS ")
            or line.startswith("PHASE184G_CASE_FAIL ")
        ):
            continue
        fields = _parse_marker_fields(line)
        if fields.get("case") != case or fields.get("token") != token:
            continue
        verdict = "PASS" if line.startswith("PHASE184G_CASE_PASS ") else "FAIL"
        result = TerminalMarker(verdict, line[:2048], fields)
    return result


def _actor_path(config: Mapping[str, object], collection: str, role: str) -> pathlib.Path:
    paths = config.get(collection)
    if not isinstance(paths, Mapping) or role not in paths:
        raise AcceptanceFailure("FAIL_PREFLIGHT", f"{role} has no configured {collection} path.")
    return pathlib.Path(str(paths[role]))


def write_actor_ready(
    config: Mapping[str, object],
    role: str,
    details: Mapping[str, object],
) -> None:
    """Atomically publish one current-run worker readiness document."""

    write_private_json_atomic(
        _actor_path(config, "readyFiles", role),
        {
            "schemaVersion": 1,
            "runId": config["runId"],
            "case": config["case"],
            "role": role,
            "tokenSha256": protocol.token_sha256(str(config["token"])),
            "ready": True,
            "details": dict(details),
        },
    )


def write_actor_result(
    config: Mapping[str, object],
    role: str,
    *,
    verdict: str,
    evidence: Mapping[str, object],
) -> None:
    """Atomically publish bounded current-run worker evidence."""

    write_private_json_atomic(
        _actor_path(config, "resultFiles", role),
        {
            "schemaVersion": 1,
            "runId": config["runId"],
            "case": config["case"],
            "role": role,
            "tokenSha256": protocol.token_sha256(str(config["token"])),
            "verdict": verdict,
            "evidence": dict(evidence),
        },
    )


def read_actor_document(
    config: Mapping[str, object],
    role: str,
    collection: str,
) -> dict[str, object]:
    """Read one exact actor document and reject stale or synthetic evidence."""

    path = _actor_path(config, collection, role)
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as exc:
        raise AcceptanceFailure(
            "FAIL_TERMINAL",
            f"{role} did not produce valid {collection} evidence.",
        ) from exc
    if not isinstance(value, dict):
        raise AcceptanceFailure("FAIL_TERMINAL", f"{role} evidence is not an object.")
    expected_identity = {
        "schemaVersion": 1,
        "runId": config["runId"],
        "case": config["case"],
        "role": role,
        "tokenSha256": protocol.token_sha256(str(config["token"])),
    }
    if any(value.get(key) != expected for key, expected in expected_identity.items()):
        raise AcceptanceFailure("FAIL_TERMINAL", f"{role} evidence is stale or mismatched.")
    if collection == "readyFiles":
        if set(value) != {*expected_identity, "ready", "details"} or value["ready"] is not True:
            raise AcceptanceFailure("FAIL_TERMINAL", f"{role} readiness is malformed.")
    elif collection == "resultFiles":
        if set(value) != {*expected_identity, "verdict", "evidence"}:
            raise AcceptanceFailure("FAIL_TERMINAL", f"{role} result is malformed.")
        if value["verdict"] != "PASS" or not isinstance(value["evidence"], Mapping):
            raise AcceptanceFailure("FAIL_TERMINAL", f"{role} did not report PASS evidence.")
    else:
        raise ValueError("Unknown actor document collection.")
    return value


def read_log_lines(path: pathlib.Path) -> list[str]:
    """Read one dedicated bounded-size current run log."""

    try:
        size = pathlib.Path(path).stat().st_size
        if size > 64 * 1024 * 1024:
            raise AcceptanceFailure("FAIL_TERMINAL", "Unity log exceeded the acceptance bound.")
        return pathlib.Path(path).read_text(encoding="utf-8", errors="replace").splitlines()
    except FileNotFoundError:
        return []
    except OSError as exc:
        raise AcceptanceFailure("FAIL_TERMINAL", "Unity log could not be read.") from exc


def wait_for_log_marker(
    config: Mapping[str, object],
    marker: str,
    timeout_seconds: float,
) -> str:
    """Wait for one current case/token marker in the dedicated Unity log."""

    deadline = time.monotonic() + timeout_seconds
    case = str(config["case"])
    token = str(config["token"])
    log_path = pathlib.Path(str(config["unityLog"]))
    while True:
        for line in read_log_lines(log_path):
            if marker not in line:
                continue
            fields = _parse_marker_fields(line.strip())
            if fields.get("case") == case and fields.get("token") == token:
                return line.strip()
        terminal = find_terminal_marker(read_log_lines(log_path), case, token)
        if terminal is not None and terminal.verdict == "FAIL":
            raise AcceptanceFailure("FAIL_TERMINAL", "Unity reported a correlated case failure.")
        remaining = deadline - time.monotonic()
        if remaining <= 0:
            raise AcceptanceFailure("FAIL_TERMINAL", f"Unity marker {marker} did not arrive.")
        time.sleep(min(0.1, remaining))


def wait_for_terminal_marker(
    config: Mapping[str, object],
    timeout_seconds: float,
) -> TerminalMarker:
    """Wait for the current run's correlated Unity PASS/FAIL."""

    deadline = time.monotonic() + timeout_seconds
    case = str(config["case"])
    token = str(config["token"])
    path = pathlib.Path(str(config["unityLog"]))
    while True:
        marker = find_terminal_marker(read_log_lines(path), case, token)
        if marker is not None:
            if marker.verdict != "PASS":
                raise AcceptanceFailure("FAIL_TERMINAL", "Unity reported a correlated case failure.")
            return marker
        remaining = deadline - time.monotonic()
        if remaining <= 0:
            raise AcceptanceFailure("FAIL_TERMINAL", "Unity emitted no correlated terminal marker.")
        time.sleep(min(0.1, remaining))


def _wait_for_unity_context(config: Mapping[str, object]) -> None:
    """Start finite actor windows only after the correlated Play session exists."""

    wait_for_log_marker(
        config,
        "PHASE184G_CONTEXT_READY",
        900.0,
    )


def _message_contains_stage(value: object, expected: str) -> bool:
    """Search decoded JSON/Protobuf values for one exact correlation stage."""

    if isinstance(value, str):
        return value == expected
    if isinstance(value, Mapping):
        return any(_message_contains_stage(nested, expected) for nested in value.values())
    if isinstance(value, (list, tuple)):
        return any(_message_contains_stage(nested, expected) for nested in value)
    descriptor = getattr(value, "DESCRIPTOR", None)
    if descriptor is not None:
        for field, nested in value.ListFields():
            if field.label == field.LABEL_REPEATED:
                if _message_contains_stage(list(nested), expected):
                    return True
            elif _message_contains_stage(nested, expected):
                return True
    return False


@dataclass(frozen=True)
class _FoxgloveChannel:
    channel_id: int
    topic: str
    encoding: str
    schema_name: str
    schema_encoding: str
    schema: str


async def _wait_for_foxglove_channels(
    websocket,
    topics: Sequence[str],
    timeout_seconds: float,
) -> dict[str, _FoxgloveChannel]:
    """Require exact current-run channel advertisements."""

    deadline = time.monotonic() + timeout_seconds
    channels: dict[str, _FoxgloveChannel] = {}
    expected = set(topics)
    while time.monotonic() < deadline:
        try:
            frame = await asyncio.wait_for(
                websocket.recv(),
                timeout=max(0.01, deadline - time.monotonic()),
            )
        except asyncio.TimeoutError:
            break
        if not isinstance(frame, str):
            continue
        try:
            message = json.loads(frame)
        except json.JSONDecodeError:
            continue
        if message.get("op") != "advertise":
            continue
        for item in message.get("channels", []):
            if not isinstance(item, Mapping):
                continue
            topic = str(item.get("topic", ""))
            if topic not in expected:
                continue
            channels[topic] = _FoxgloveChannel(
                int(item.get("id", 0)),
                topic,
                str(item.get("encoding", "")).lower(),
                str(item.get("schemaName", "")),
                str(item.get("schemaEncoding", "")).lower(),
                str(item.get("schema", "")),
            )
        if set(channels) == expected:
            return channels
    raise AcceptanceFailure("FAIL_CLIENT", "Foxglove did not advertise every exact case topic.")


def _dynamic_protobuf_class(channel: _FoxgloveChannel):
    """Build a dynamic message class from the advertised FileDescriptorSet."""

    if (
        channel.encoding != "protobuf"
        or channel.schema_encoding != "protobuf"
        or not channel.schema_name
        or not channel.schema
    ):
        raise AcceptanceFailure("FAIL_CLIENT", "Protobuf channel metadata is incomplete.")
    try:
        from google.protobuf import descriptor_pb2, descriptor_pool, message_factory

        descriptor_set = descriptor_pb2.FileDescriptorSet()
        descriptor_set.ParseFromString(base64.b64decode(channel.schema, validate=True))
        pool = descriptor_pool.DescriptorPool()
        pending = list(descriptor_set.file)
        while pending:
            progressed = False
            for item in tuple(pending):
                try:
                    pool.Add(item)
                except Exception:  # dependency may be later in the descriptor set
                    continue
                pending.remove(item)
                progressed = True
            if not progressed:
                raise ValueError("descriptor dependencies could not be resolved")
        descriptor = pool.FindMessageTypeByName(channel.schema_name)
        return message_factory.GetMessageClass(descriptor)
    except Exception as exc:
        raise AcceptanceFailure(
            "FAIL_CLIENT",
            "Advertised Protobuf schema could not create its dynamic message.",
        ) from exc


def _set_dynamic_dto(message, *, token: str, stage: str, count: int) -> None:
    """Populate the generated aggregate/DTO graph by semantic field name."""

    for field in message.DESCRIPTOR.fields:
        normalized = field.name.replace("_", "").lower()
        if field.label == field.LABEL_REPEATED:
            sequence = getattr(message, field.name)
            if field.type == field.TYPE_BYTES:
                setattr(message, field.name, bytes((0x18, 0x04, count & 0xFF)))
            elif field.type in {
                field.TYPE_INT32,
                field.TYPE_SINT32,
                field.TYPE_SFIXED32,
                field.TYPE_UINT32,
                field.TYPE_FIXED32,
                field.TYPE_INT64,
                field.TYPE_SINT64,
                field.TYPE_SFIXED64,
                field.TYPE_UINT64,
                field.TYPE_FIXED64,
            }:
                sequence.extend((count, count + 1, count + 2))
            continue
        if field.type == field.TYPE_MESSAGE:
            _set_dynamic_dto(
                getattr(message, field.name),
                token=token,
                stage=stage,
                count=count,
            )
        elif field.type == field.TYPE_STRING:
            setattr(message, field.name, token + "-" + stage)
        elif field.type == field.TYPE_BOOL:
            setattr(message, field.name, True)
        elif field.type == field.TYPE_ENUM:
            values = field.enum_type.values
            setattr(message, field.name, values[1].number if len(values) > 1 else values[0].number)
        elif field.type in {
            field.TYPE_INT32,
            field.TYPE_SINT32,
            field.TYPE_SFIXED32,
            field.TYPE_UINT32,
            field.TYPE_FIXED32,
            field.TYPE_INT64,
            field.TYPE_SINT64,
            field.TYPE_SFIXED64,
            field.TYPE_UINT64,
            field.TYPE_FIXED64,
        }:
            setattr(message, field.name, count)
        elif normalized.startswith("has"):
            setattr(message, field.name, True)


def _json_dto(token: str, stage: str, count: int) -> dict[str, object]:
    label = token + "-" + stage
    return {
        "Count": count,
        "Kind": 1,
        "Message": label,
        "Bytes": [0x18, 0x04, count & 0xFF],
        "Values": [count, count + 1, count + 2],
        "Nested": {"Enabled": True, "Label": label},
        "OptionalCount": count,
        "OptionalText": label,
    }


async def _foxglove_subscribe(websocket, channels: Mapping[str, _FoxgloveChannel]):
    subscription_to_topic: dict[int, str] = {}
    subscriptions: list[dict[str, int]] = []
    for index, topic in enumerate(channels, start=1):
        subscription_id = 184000 + index
        subscription_to_topic[subscription_id] = topic
        subscriptions.append(
            {"id": subscription_id, "channelId": channels[topic].channel_id}
        )
    await websocket.send(
        json.dumps(
            {"op": "subscribe", "subscriptions": subscriptions},
            separators=(",", ":"),
        )
    )
    return subscription_to_topic


async def _foxglove_advertise_and_send_json(
    websocket,
    topic: str,
    field_name: str,
    token: str,
    stage: str,
    count: int,
    channel_id: int,
    *,
    advertise: bool,
) -> None:
    if advertise:
        await websocket.send(
            json.dumps(
                {
                    "op": "advertise",
                    "channels": [{"id": channel_id, "topic": topic, "encoding": "json"}],
                },
                separators=(",", ":"),
            )
        )
    payload = json.dumps(
        {field_name: _json_dto(token, stage, count)},
        separators=(",", ":"),
    ).encode("utf-8")
    await websocket.send(
        bytes((FOXGLOVE_MESSAGE_OPCODE,))
        + struct.pack("<I", channel_id)
        + payload
    )


async def _receive_foxglove_stages(
    websocket,
    subscription_to_topic: Mapping[int, str],
    channels: Mapping[str, _FoxgloveChannel],
    expected: Mapping[str, set[str]],
    forbidden: Mapping[str, set[str]],
    timeout_seconds: float,
    minimum_observation_seconds: float = 0.0,
) -> tuple[dict[str, set[str]], list[str], float]:
    """Observe required stages and retain any forbidden remote republish."""

    observed = {topic: set() for topic in expected}
    forbidden_observed: list[str] = []
    protobuf_classes = {
        topic: _dynamic_protobuf_class(channel)
        for topic, channel in channels.items()
        if channel.encoding == "protobuf"
    }
    started = time.monotonic()
    deadline = started + timeout_seconds
    last_timestamp = 0.0
    while time.monotonic() < deadline:
        complete = all(
            observed[topic] >= stages
            for topic, stages in expected.items()
        )
        elapsed = time.monotonic() - started
        if complete and elapsed >= minimum_observation_seconds:
            return observed, forbidden_observed, last_timestamp
        remaining = deadline - time.monotonic()
        if complete:
            remaining = min(
                remaining,
                max(0.01, minimum_observation_seconds - elapsed),
            )
        try:
            frame = await asyncio.wait_for(
                websocket.recv(),
                timeout=max(0.01, min(0.1, remaining)),
            )
        except asyncio.TimeoutError:
            if (
                all(
                    observed[topic] >= stages
                    for topic, stages in expected.items()
                )
                and time.monotonic() - started >= minimum_observation_seconds
            ):
                return observed, forbidden_observed, last_timestamp
            if time.monotonic() >= deadline:
                break
            continue
        if not isinstance(frame, bytes) or len(frame) < 13 or frame[0] != FOXGLOVE_MESSAGE_OPCODE:
            continue
        subscription_id = struct.unpack_from("<I", frame, 1)[0]
        topic = subscription_to_topic.get(subscription_id)
        if topic is None:
            continue
        timestamp = struct.unpack_from("<Q", frame, 5)[0]
        payload = frame[13:]
        channel = channels[topic]
        try:
            if channel.encoding == "json":
                decoded: object = json.loads(payload.decode("utf-8"))
            elif channel.encoding == "protobuf":
                decoded = protobuf_classes[topic]()
                decoded.ParseFromString(payload)
            else:
                raise AcceptanceFailure("FAIL_CLIENT", "Unexpected Foxglove encoding.")
        except (UnicodeError, json.JSONDecodeError, ValueError) as exc:
            raise AcceptanceFailure("FAIL_CLIENT", "Foxglove payload could not be decoded.") from exc
        for stage in expected.get(topic, set()):
            if _message_contains_stage(decoded, stage):
                observed[topic].add(stage)
                last_timestamp = max(last_timestamp, float(timestamp))
        for stage in forbidden.get(topic, set()):
            if _message_contains_stage(decoded, stage):
                forbidden_observed.append(stage)
    missing = {
        topic: sorted(stages - observed.get(topic, set()))
        for topic, stages in expected.items()
        if stages - observed.get(topic, set())
    }
    raise AcceptanceFailure("FAIL_CLIENT", f"Foxglove delivery evidence is incomplete: {missing}.")


async def _run_foxglove_client_async(config: Mapping[str, object]) -> Mapping[str, object]:
    """Exercise the exact Foxglove half of the selected deep case."""

    try:
        import websockets
    except ImportError as exc:
        raise AcceptanceFailure("FAIL_CLIENT", "Python websockets is unavailable.") from exc

    case = str(config["case"])
    token = str(config["token"])
    topics = tuple(str(item) for item in config["topics"])
    windows = config["observationWindows"]
    result_timeout = float(windows["positiveSeconds"]) + 30.0
    write_actor_ready(
        config,
        "foxglove-client",
        {"state": "connect-loop-ready", "host": "loopback", "topicCount": len(topics)},
    )
    await asyncio.to_thread(_wait_for_unity_context, config)
    desktop_barrier = os.environ.get(DESKTOP_CLIENT_BARRIER_ENV)
    if desktop_barrier is not None:
        await asyncio.to_thread(
            desktop_live_protocol.wait_for_desktop_barrier,
            config,
            desktop_barrier,
        )
    url = f"ws://{config['foxgloveHost']}:{config['foxglovePort']}"
    connection_deadline = time.monotonic() + 120.0
    websocket = None
    while websocket is None:
        try:
            websocket = await websockets.connect(url, subprotocols=[FOXGLOVE_SUBPROTOCOL])
        except OSError:
            if time.monotonic() >= connection_deadline:
                raise AcceptanceFailure("FAIL_CLIENT", "Foxglove loopback connection did not become ready.")
            await asyncio.sleep(0.1)
    try:
        channels = await _wait_for_foxglove_channels(websocket, topics, 30.0)
        subscriptions = await _foxglove_subscribe(websocket, channels)
        encodings = sorted({channel.encoding for channel in channels.values()})
        if case == "foxglove-profile":
            if channels[topics[0]].encoding != "protobuf" or channels[topics[1]].encoding != "json":
                raise AcceptanceFailure("FAIL_CLIENT", "Inherited/explicit Foxglove encodings drifted.")
            await _foxglove_advertise_and_send_json(
                websocket,
                topics[1],
                "explicitJson",
                token,
                "profile-client-ready",
                18400,
                184901,
                advertise=True,
            )
            await asyncio.to_thread(
                wait_for_log_marker,
                config,
                "PHASE184G_PROFILE_CLIENT_READY",
                30.0,
            )
            observed, forbidden, timestamp = await _receive_foxglove_stages(
                websocket,
                subscriptions,
                channels,
                {
                    topics[0]: {token + "-profile-outbound"},
                    topics[1]: {token + "-json-outbound"},
                },
                {
                    topics[1]: {
                        token + "-profile-client-ready",
                    }
                },
                result_timeout,
            )
            del observed
            if forbidden:
                raise AcceptanceFailure(
                    "FAIL_ORIGIN",
                    "Foxglove client-readiness input was republished.",
                )
            await _foxglove_advertise_and_send_json(
                websocket,
                topics[1],
                "explicitJson",
                token,
                "profile-a",
                18403,
                184901,
                advertise=False,
            )
            await asyncio.to_thread(
                wait_for_log_marker,
                config,
                "PHASE184G_PROFILE_GATE_CLOSED",
                30.0,
            )
            await _foxglove_advertise_and_send_json(
                websocket,
                topics[1],
                "explicitJson",
                token,
                "profile-b",
                18404,
                184901,
                advertise=False,
            )
            await asyncio.to_thread(
                wait_for_log_marker,
                config,
                "PHASE184G_PROFILE_GATE_REOPENED",
                float(windows["negativeSeconds"]) + 10.0,
            )
            await _foxglove_advertise_and_send_json(
                websocket,
                topics[1],
                "explicitJson",
                token,
                "profile-b",
                18404,
                184901,
                advertise=False,
            )
            await asyncio.to_thread(
                wait_for_log_marker,
                config,
                "PHASE184G_PROFILE_LOCAL_MUTATED",
                30.0,
            )
            later, forbidden_origin, later_timestamp = await _receive_foxglove_stages(
                websocket,
                subscriptions,
                channels,
                {topics[1]: {token + "-profile-local-after-remote"}},
                {
                    topics[1]: {
                        token + "-profile-client-ready",
                        token + "-profile-a",
                        token + "-profile-b",
                    }
                },
                float(windows["negativeSeconds"]) + 10.0,
                minimum_observation_seconds=float(windows["negativeSeconds"]),
            )
            del later
            if forbidden_origin:
                raise AcceptanceFailure(
                    "FAIL_ORIGIN",
                    "Remote Foxglove input was republished during origin suppression.",
                )
            timestamp = max(timestamp, later_timestamp)
            await asyncio.to_thread(wait_for_terminal_marker, config, 30.0)
            return {
                "deliveryObserved": True,
                "channelEncodings": encodings,
                "sampleToken": protocol.token_sha256(token),
                "sampleStages": [
                    "profile-outbound",
                    "json-outbound",
                    "profile-a",
                    "profile-b",
                    "profile-local-after-remote",
                ],
                "timestamp": timestamp,
                "remoteApplied": True,
                "sameOriginDropped": True,
                "laterLocalPublished": True,
                "noDisabledApply": True,
                "recoveryApplied": True,
            }

        if case == "multi-target":
            await asyncio.to_thread(
                wait_for_log_marker,
                config,
                "PHASE184G_MULTI_LOCAL_ARMED",
                120.0,
            )
            observed, forbidden, timestamp = await _receive_foxglove_stages(
                websocket,
                subscriptions,
                channels,
                {topics[0]: {token + "-multi-local-1", token + "-multi-local-3"}},
                {topics[0]: {token + "-multi-remote-2"}},
                60.0,
            )
            del observed
            if forbidden:
                raise AcceptanceFailure("FAIL_ORIGIN", "Remote ROS input was republished to Foxglove.")
            await asyncio.to_thread(wait_for_terminal_marker, config, 30.0)
            return {
                "deliveryObserved": True,
                "channelEncodings": encodings,
                "sampleToken": protocol.token_sha256(token),
                "sampleStages": ["multi-local-1", "multi-local-3"],
                "timestamp": timestamp,
                "remoteRepublishObserved": False,
            }

        if case == "degraded-target":
            await _foxglove_advertise_and_send_json(
                websocket,
                DEGRADED_CLIENT_READY_TOPIC,
                "clientReady",
                token,
                "degraded-client-ready",
                18419,
                184902,
                advertise=True,
            )
            await asyncio.to_thread(
                wait_for_log_marker,
                config,
                "PHASE184G_DEGRADED_CLIENT_READY",
                30.0,
            )
            observed, forbidden, timestamp = await _receive_foxglove_stages(
                websocket,
                subscriptions,
                channels,
                {topics[0]: {token + "-degraded-local"}},
                {},
                float(windows["negativeSeconds"]) + 30.0,
            )
            del observed, forbidden
            await asyncio.to_thread(wait_for_terminal_marker, config, 30.0)
            return {
                "deliveryObserved": True,
                "channelEncodings": encodings,
                "sampleToken": protocol.token_sha256(token),
                "sampleStages": ["degraded-local"],
                "timestamp": timestamp,
            }
        raise AcceptanceFailure("FAIL_CLIENT", "Selected case does not own a Foxglove worker.")
    finally:
        await websocket.close()


def run_foxglove_client_worker(config: Mapping[str, object]) -> int:
    try:
        evidence = asyncio.run(_run_foxglove_client_async(config))
        write_actor_result(config, "foxglove-client", verdict="PASS", evidence=evidence)
        return 0
    except AcceptanceFailure as exc:
        write_actor_result(
            config,
            "foxglove-client",
            verdict=exc.code,
            evidence={"diagnostic": str(exc)[: protocol.MAX_DIAGNOSTIC_CHARACTERS]},
        )
        return 1
    except Exception as exc:
        write_actor_result(
            config,
            "foxglove-client",
            verdict="FAIL_CLIENT",
            evidence={"diagnostic": type(exc).__name__},
        )
        return 1


def _phase181_peer_module():
    try:
        import phase181_custom_ros2_peer as peer
    except ImportError as exc:
        raise AcceptanceFailure("FAIL_PEER", "Phase181 peer helpers are unavailable.") from exc
    return peer


def _ros_payload_fields(token: str, stage: str, count: int) -> dict[str, object]:
    label = token + "-" + stage
    return {
        "count": count,
        "kind": 1,
        "message": label,
        "has_message": True,
        "bytes": [0x18, 0x04, count & 0xFF],
        "has_bytes": True,
        "values": [count, count + 1, count + 2],
        "has_values": True,
        "nested": {"enabled": True, "label": label},
        "has_nested": True,
        "optional_count": count,
        "has_optional_count": True,
        "optional_text": label,
        "has_optional_text": True,
    }


def _ros_stage(envelope) -> str:
    payload = getattr(envelope, "payload", None)
    return str(getattr(payload, "message", ""))


def _ros_count(envelope) -> int:
    payload = getattr(envelope, "payload", None)
    return int(getattr(payload, "count", -1))


def _publisher_gid(message_info) -> str:
    raw = (
        message_info.get("publisher_gid")
        if isinstance(message_info, Mapping)
        else getattr(message_info, "publisher_gid", None)
    )
    if isinstance(raw, Mapping):
        raw = raw.get("data")
    elif raw is not None and not isinstance(raw, (bytes, bytearray, memoryview)):
        raw = getattr(raw, "data", raw)
    try:
        value = bytes(raw) if raw is not None else b""
    except (TypeError, ValueError):
        return ""
    return value.hex() if value and any(value) else ""


def _publication_sequence_number(message_info) -> int | None:
    """Read the per-writer DDS sequence exposed by Windows Jazzy rclpy."""

    raw = (
        message_info.get("publication_sequence_number")
        if isinstance(message_info, Mapping)
        else getattr(message_info, "publication_sequence_number", None)
    )
    if isinstance(raw, bool) or not isinstance(raw, int) or raw < 0:
        return None
    return raw


def _attribute_sample_publishers(
    *,
    direct_gids: Iterable[str],
    publication_sequences: Iterable[int | None],
    graph_publishers: Sequence[Mapping[str, object]],
    minimum_publishers: int,
) -> tuple[list[str], str]:
    """Attribute one logical sample without fabricating unavailable rclpy GIDs."""

    graph_gids = sorted(
        {
            str(item.get("gid", ""))
            for item in graph_publishers
            if str(item.get("gid", ""))
        }
    )
    if len(graph_gids) != minimum_publishers:
        return [], ""

    observed_gids = sorted(
        {
            str(gid)
            for gid in direct_gids
            if str(gid) and str(gid) in graph_gids
        }
    )
    if len(observed_gids) >= minimum_publishers:
        return observed_gids, "message-info-publisher-gid"

    sequences = [
        value
        for value in publication_sequences
        if isinstance(value, int) and not isinstance(value, bool) and value >= 0
    ]
    if minimum_publishers == 1 and sequences:
        return graph_gids, "sole-external-graph-gid"
    if minimum_publishers == 2 and len(sequences) != len(set(sequences)):
        return graph_gids, "publication-sequence-plus-graph-gid"
    return [], ""


def _helper_node_name(role: str, config: Mapping[str, object]) -> str:
    digest = protocol.token_sha256(str(config["token"]))[:12]
    return f"phase184g_{role.replace('-', '_')}_{digest}"


def _worker_progress(role: str, stage: str) -> None:
    """Emit one bounded stage marker to the owned worker log."""

    print(
        f"PHASE184G_WORKER_PROGRESS role={role} stage={stage}",
        flush=True,
    )


def _qos_profile(kind: str):
    """Return an explicit rclpy QoS contract for acceptance endpoints."""

    try:
        from rclpy.qos import (
            DurabilityPolicy,
            HistoryPolicy,
            QoSProfile,
            ReliabilityPolicy,
        )
    except ImportError as exc:
        raise AcceptanceFailure("FAIL_PEER", "rclpy QoS APIs are unavailable.") from exc
    if kind == "default":
        return QoSProfile(
            history=HistoryPolicy.KEEP_LAST,
            depth=10,
            reliability=ReliabilityPolicy.RELIABLE,
            durability=DurabilityPolicy.VOLATILE,
        )
    if kind == "sensor-data":
        return QoSProfile(
            history=HistoryPolicy.KEEP_LAST,
            depth=5,
            reliability=ReliabilityPolicy.BEST_EFFORT,
            durability=DurabilityPolicy.VOLATILE,
        )
    if kind == "system-default":
        return QoSProfile(
            history=HistoryPolicy.SYSTEM_DEFAULT,
            depth=0,
            reliability=ReliabilityPolicy.SYSTEM_DEFAULT,
            durability=DurabilityPolicy.SYSTEM_DEFAULT,
        )
    if kind == "keep-all":
        return QoSProfile(
            history=HistoryPolicy.KEEP_ALL,
            depth=0,
            reliability=ReliabilityPolicy.RELIABLE,
            durability=DurabilityPolicy.VOLATILE,
        )
    if kind == "keep-last-depth":
        return QoSProfile(
            history=HistoryPolicy.KEEP_LAST,
            depth=7,
            reliability=ReliabilityPolicy.BEST_EFFORT,
            durability=DurabilityPolicy.TRANSIENT_LOCAL,
        )
    raise AcceptanceFailure("FAIL_QOS", "Unknown acceptance QoS contract.")


def _load_ros_message_types(config: Mapping[str, object]):
    peer = _phase181_peer_module()
    static_package = repository_root() / "Packages" / INTERFACE_PACKAGE_ID
    lock = peer.load_static_interface_lock(static_package)
    if lock.interface_digest != config["interfaceDigest"]:
        raise AcceptanceFailure("FAIL_PEER", "Worker interface digest drifted.")
    try:
        envelope, payload, nested = peer._load_generated_message_types(lock)
    except peer.PeerFailure as exc:
        raise AcceptanceFailure("FAIL_PEER", str(exc)) from exc
    return peer, lock, envelope, payload, nested


def _make_ros_envelope(
    peer,
    node,
    envelope_type,
    payload_type,
    nested_type,
    *,
    token: str,
    stage: str,
    count: int,
    origin: str,
    sequence: int,
):
    return peer._make_envelope(
        node,
        envelope_type,
        payload_type,
        nested_type,
        _ros_payload_fields(token, stage, count),
        origin,
        sequence,
    )


def _spin_until(
    rclpy_module,
    node,
    predicate,
    timeout_seconds: float,
    failure_code: str,
    message: str,
) -> None:
    deadline = time.monotonic() + timeout_seconds
    while not predicate():
        remaining = deadline - time.monotonic()
        if remaining <= 0:
            raise AcceptanceFailure(failure_code, message)
        rclpy_module.spin_once(node, timeout_sec=min(0.05, remaining))


def _run_multi_target_peer(
    config: Mapping[str, object],
    rclpy_module,
    node,
    peer,
    envelope_type,
    payload_type,
    nested_type,
) -> Mapping[str, object]:
    topic = str(config["topics"][0])
    token = str(config["token"])
    _worker_progress("ros2-peer", "multi-qos")
    qos = _qos_profile("default")
    messages: list[tuple[object, str, int | None]] = []
    observed_sample_gids: set[tuple[str, str]] = set()

    def receive(message, info):
        gid = _publisher_gid(info)
        publication_sequence = _publication_sequence_number(info)
        stage = _ros_stage(message)
        messages.append((message, gid, publication_sequence))
        if stage in {
            token + "-multi-local-1",
            token + "-multi-local-3",
        }:
            evidence_key = gid or (
                "publication-sequence"
                if publication_sequence is not None
                else "unattributed"
            )
            if (stage, evidence_key) not in observed_sample_gids:
                observed_sample_gids.add((stage, evidence_key))
                _worker_progress(
                    "ros2-peer",
                    "multi-sample-"
                    + stage.rsplit("-", 1)[-1]
                    + "-gid-"
                    + (
                        gid
                        or (
                            "publication-sequence-"
                            + str(publication_sequence)
                            if publication_sequence is not None
                            else "unattributed"
                        )
                    ),
                )
        if len(messages) > 4096:
            del messages[: len(messages) - 4096]

    _worker_progress("ros2-peer", "multi-create-subscription")
    subscription = node.create_subscription(envelope_type, topic, receive, qos)
    _worker_progress("ros2-peer", "multi-create-publisher")
    publisher = node.create_publisher(envelope_type, topic, qos)
    _worker_progress("ros2-peer", "multi-endpoints-ready")
    write_actor_ready(
        config,
        "ros2-peer",
        {"state": "typed-endpoints-ready", "topicCount": 1},
    )
    _wait_for_unity_context(config)

    local1 = token + "-multi-local-1"
    wait_for_log_marker(config, "PHASE184G_MULTI_LOCAL_ARMED", 120.0)
    graph_topics = _wait_for_graph_snapshot(config, rclpy_module, node)
    graph_publishers = _external_endpoints(
        graph_topics[topic],
        "publishers",
        str(config["interfaceType"]),
    )

    def sample_attribution(stage: str) -> tuple[list[str], str]:
        matching = [
            (gid, sequence)
            for message, gid, sequence in messages
            if _ros_stage(message) == stage
        ]
        return _attribute_sample_publishers(
            direct_gids=(gid for gid, _sequence in matching),
            publication_sequences=(
                sequence for _gid, sequence in matching
            ),
            graph_publishers=graph_publishers,
            minimum_publishers=2,
        )

    def local_one_ready():
        gids, _source = sample_attribution(local1)
        return len(gids) >= 2

    _spin_until(
        rclpy_module,
        node,
        local_one_ready,
        60.0,
        "FAIL_FANOUT",
        "Native and Bridge did not both deliver local token 1.",
    )
    local_one = [
        (message, gid, sequence)
        for message, gid, sequence in messages
        if _ros_stage(message) == local1
    ]
    unity_origins = {
        str(getattr(message, "foxrun_origin_id", ""))
        for message, _gid, _sequence in local_one
        if getattr(message, "foxrun_origin_id", "")
    }
    if len(unity_origins) != 1:
        raise AcceptanceFailure("FAIL_ORIGIN", "Native and Bridge local token 1 origins differ.")
    unity_origin = next(iter(unity_origins))
    peer_origin = "phase184-peer-" + protocol.token_sha256(token)[:16]
    remote = _make_ros_envelope(
        peer,
        node,
        envelope_type,
        payload_type,
        nested_type,
        token=token,
        stage="multi-remote-2",
        count=18412,
        origin=peer_origin,
        sequence=18412,
    )
    for _ in range(3):
        publisher.publish(remote)
        rclpy_module.spin_once(node, timeout_sec=0.05)
    wait_for_log_marker(config, "PHASE184G_MULTI_REMOTE_APPLIED", 30.0)

    same_origin = _make_ros_envelope(
        peer,
        node,
        envelope_type,
        payload_type,
        nested_type,
        token=token,
        stage="multi-self-origin",
        count=18499,
        origin=unity_origin,
        sequence=18499,
    )
    for _ in range(3):
        publisher.publish(same_origin)
        rclpy_module.spin_once(node, timeout_sec=0.05)
    wait_for_log_marker(
        config,
        "PHASE184G_MULTI_LOCAL_MUTATED",
        float(config["observationWindows"]["negativeSeconds"]) + 20.0,
    )

    local3 = token + "-multi-local-3"

    def local_three_ready():
        gids, _source = sample_attribution(local3)
        return len(gids) >= 2

    _spin_until(
        rclpy_module,
        node,
        local_three_ready,
        30.0,
        "FAIL_FANOUT",
        "Native and Bridge did not both deliver later local token 3.",
    )
    graph_topics = _wait_for_graph_snapshot(config, rclpy_module, node)
    graph_publishers = _external_endpoints(
        graph_topics[topic],
        "publishers",
        str(config["interfaceType"]),
    )
    wait_for_terminal_marker(config, 30.0)
    local1_gids, local1_attribution = sample_attribution(local1)
    local3_gids, local3_attribution = sample_attribution(local3)
    if len(local1_gids) < 2 or len(local3_gids) < 2:
        raise AcceptanceFailure(
            "FAIL_FANOUT",
            "Native and Bridge sample attribution drifted before terminal evidence.",
        )
    del subscription
    return {
        "remoteApplied": True,
        "sameOriginDropped": True,
        "laterLocalPublished": True,
        "unityOriginDigest": hashlib.sha256(unity_origin.encode("utf-8")).hexdigest(),
        "local1PublisherGids": local1_gids,
        "local3PublisherGids": local3_gids,
        "local1Attribution": local1_attribution,
        "local3Attribution": local3_attribution,
        "distinctFanoutPublishers": len(local1_gids),
        "graphEvidence": {
            "source": "ros2-peer-rclpy-graph-api",
            "topics": graph_topics,
        },
    }


def _run_qos_peer(
    config: Mapping[str, object],
    rclpy_module,
    node,
    envelope_type,
) -> Mapping[str, object]:
    token = str(config["token"])
    topic_kinds = (
        (str(config["topics"][0]), "system-default", token + "-qos-system-default"),
        (str(config["topics"][1]), "keep-all", token + "-qos-keep-all"),
        (str(config["topics"][2]), "keep-last-depth", token + "-qos-keep-last-depth"),
    )
    received: dict[str, list[tuple[object, str, int | None]]] = {
        topic: [] for topic, _, _ in topic_kinds
    }
    subscriptions = []
    for topic, kind, _stage in topic_kinds:
        def receive(message, info, *, selected=topic):
            received[selected].append(
                (
                    message,
                    _publisher_gid(info),
                    _publication_sequence_number(info),
                )
            )

        subscriptions.append(
            node.create_subscription(envelope_type, topic, receive, _qos_profile(kind))
        )
    write_actor_ready(
        config,
        "ros2-peer",
        {"state": "qos-subscriptions-ready", "topicCount": len(topic_kinds)},
    )
    _wait_for_unity_context(config)
    graph_topics = _wait_for_graph_snapshot(config, rclpy_module, node)
    expected_type = str(config["interfaceType"])
    graph_publishers = {
        topic: _external_endpoints(
            graph_topics[topic],
            "publishers",
            expected_type,
        )
        for topic, _kind, _stage in topic_kinds
    }

    def topic_attribution(
        topic: str,
        stage: str,
    ) -> tuple[list[str], str]:
        matching = [
            (gid, sequence)
            for message, gid, sequence in received[topic]
            if _ros_stage(message) == stage
        ]
        return _attribute_sample_publishers(
            direct_gids=(gid for gid, _sequence in matching),
            publication_sequences=(
                sequence for _gid, sequence in matching
            ),
            graph_publishers=graph_publishers[topic],
            minimum_publishers=2,
        )

    def all_delivered():
        for topic, _kind, stage in topic_kinds:
            gids, _source = topic_attribution(topic, stage)
            if len(gids) < 2:
                return False
        return True

    _spin_until(
        rclpy_module,
        node,
        all_delivered,
        60.0,
        "FAIL_QOS",
        "QoS case did not deliver every topic from Native and Bridge.",
    )
    wait_for_terminal_marker(config, 30.0)
    del subscriptions
    return {
        "deliveryByTopic": {
            topic: topic_attribution(topic, stage)[0]
            for topic, _kind, stage in topic_kinds
        },
        "deliveryAttributionByTopic": {
            topic: topic_attribution(topic, stage)[1]
            for topic, _kind, stage in topic_kinds
        },
        "graphEvidence": {
            "source": "ros2-peer-rclpy-graph-api",
            "topics": graph_topics,
        },
    }


def _run_stream_peer(
    config: Mapping[str, object],
    rclpy_module,
    node,
    peer,
    envelope_type,
    payload_type,
    nested_type,
) -> Mapping[str, object]:
    stream_topic, origin_topic = (str(item) for item in config["topics"])
    token = str(config["token"])
    sensor_qos = _qos_profile("sensor-data")
    origin_messages: list[tuple[object, str, int | None]] = []

    def receive_origin(message, info):
        origin_messages.append(
            (
                message,
                _publisher_gid(info),
                _publication_sequence_number(info),
            )
        )
        if len(origin_messages) > 256:
            del origin_messages[: len(origin_messages) - 256]

    origin_subscription = node.create_subscription(
        envelope_type,
        origin_topic,
        receive_origin,
        sensor_qos,
    )
    stream_publisher = node.create_publisher(envelope_type, stream_topic, sensor_qos)
    origin_publisher = node.create_publisher(envelope_type, origin_topic, sensor_qos)
    write_actor_ready(
        config,
        "ros2-peer",
        {"state": "stream-publishers-ready", "topicCount": 2, "nominalHz": 640},
    )
    _wait_for_unity_context(config)

    warmup_stage = token + "-origin-warmup"
    _spin_until(
        rclpy_module,
        node,
        lambda: any(
            _ros_stage(message) == warmup_stage
            for message, _gid, _sequence in origin_messages
        ),
        60.0,
        "FAIL_ORIGIN",
        "Unity origin warmup was not observed.",
    )
    warmup = next(
        message
        for message, _gid, _sequence in origin_messages
        if _ros_stage(message) == warmup_stage
    )
    unity_origin = str(getattr(warmup, "foxrun_origin_id", ""))
    if not unity_origin:
        raise AcceptanceFailure("FAIL_ORIGIN", "Unity origin warmup had no origin id.")
    peer_origin = "phase184-peer-" + protocol.token_sha256(token)[:16]

    _worker_progress("ros2-peer", "stream-wait-transport-graph")
    _wait_for_stream_subscription(config, rclpy_module, node)
    offered = 1280
    period = 1.0 / 640.0
    started = time.perf_counter()
    for index in range(offered):
        sample = _make_ros_envelope(
            peer,
            node,
            envelope_type,
            payload_type,
            nested_type,
            token=token,
            stage=f"stream-{index}",
            count=index,
            origin=peer_origin,
            sequence=index + 1,
        )
        stream_publisher.publish(sample)
        rclpy_module.spin_once(node, timeout_sec=0.0)
        deadline = started + (index + 1) * period
        remaining = deadline - time.perf_counter()
        if remaining > 0:
            time.sleep(remaining)
    elapsed = time.perf_counter() - started
    if elapsed < 1.8 or elapsed > 3.5:
        raise AcceptanceFailure("FAIL_STREAM", "Nominal 640 Hz production interval drifted outside tolerance.")

    remote = _make_ros_envelope(
        peer,
        node,
        envelope_type,
        payload_type,
        nested_type,
        token=token,
        stage="origin-remote",
        count=18441,
        origin=peer_origin,
        sequence=18441,
    )
    for _ in range(3):
        origin_publisher.publish(remote)
        rclpy_module.spin_once(node, timeout_sec=0.05)
    wait_for_log_marker(config, "PHASE184G_STREAM_REMOTE_ORIGIN_APPLIED", 30.0)

    same_origin = _make_ros_envelope(
        peer,
        node,
        envelope_type,
        payload_type,
        nested_type,
        token=token,
        stage="origin-self",
        count=18498,
        origin=unity_origin,
        sequence=18498,
    )
    for _ in range(3):
        origin_publisher.publish(same_origin)
        rclpy_module.spin_once(node, timeout_sec=0.05)
    wait_for_log_marker(config, "PHASE184G_STREAM_LOCAL_ORIGIN_MUTATED", 30.0)

    local_stage = token + "-origin-local"
    _spin_until(
        rclpy_module,
        node,
        lambda: any(
            _ros_stage(message) == local_stage
            for message, _gid, _sequence in origin_messages
        ),
        30.0,
        "FAIL_ORIGIN",
        "Later local Zenoh origin mutation was not observed.",
    )
    graph_topics = _wait_for_graph_snapshot(config, rclpy_module, node)
    origin_publishers = _external_endpoints(
        graph_topics[origin_topic],
        "publishers",
        str(config["interfaceType"]),
    )
    local_observations = [
        (gid, sequence)
        for message, gid, sequence in origin_messages
        if _ros_stage(message) == local_stage
    ]
    local_origin_gids, local_origin_attribution = _attribute_sample_publishers(
        direct_gids=(gid for gid, _sequence in local_observations),
        publication_sequences=(
            sequence for _gid, sequence in local_observations
        ),
        graph_publishers=origin_publishers,
        minimum_publishers=1,
    )
    if not local_origin_gids:
        raise AcceptanceFailure(
            "FAIL_GRAPH",
            "Later local Zenoh origin sample had no publisher GID.",
        )
    terminal = wait_for_terminal_marker(config, 60.0)
    del origin_subscription
    return {
        "offered": offered,
        "nominalHz": 640,
        "productionElapsedSeconds": round(elapsed, 6),
        "remoteApplied": True,
        "sameOriginDropped": True,
        "laterLocalPublished": True,
        "unityOriginDigest": hashlib.sha256(unity_origin.encode("utf-8")).hexdigest(),
        "localOriginPublisherGids": local_origin_gids,
        "localOriginAttribution": local_origin_attribution,
        "terminalFields": dict(terminal.fields),
        "graphEvidence": {
            "source": "ros2-peer-rclpy-graph-api",
            "topics": graph_topics,
        },
    }


def run_ros2_peer_worker(config: Mapping[str, object]) -> int:
    """Run the selected typed peer using only the configured ROS Python."""

    role = "ros2-peer"
    try:
        _worker_progress(role, "import-rclpy")
        import rclpy

        _worker_progress(role, "load-message-types")
        peer, _lock, envelope, payload, nested = _load_ros_message_types(config)
        _worker_progress(role, "rclpy-init")
        rclpy.init(args=None)
        _worker_progress(role, "create-node")
        node = rclpy.create_node(_helper_node_name("peer", config))
        try:
            case = str(config["case"])
            _worker_progress(role, "run-" + case)
            if case == "multi-target":
                evidence = _run_multi_target_peer(
                    config, rclpy, node, peer, envelope, payload, nested
                )
            elif case == "qos-contract":
                evidence = _run_qos_peer(config, rclpy, node, envelope)
            elif case == "stream-640hz":
                evidence = _run_stream_peer(
                    config, rclpy, node, peer, envelope, payload, nested
                )
            else:
                raise AcceptanceFailure("FAIL_PEER", "Selected case has no ROS peer.")
        finally:
            node.destroy_node()
            rclpy.shutdown()
        write_actor_result(config, role, verdict="PASS", evidence=evidence)
        return 0
    except AcceptanceFailure as exc:
        write_actor_result(
            config,
            role,
            verdict=exc.code,
            evidence={"diagnostic": str(exc)[: protocol.MAX_DIAGNOSTIC_CHARACTERS]},
        )
        return 1
    except Exception as exc:
        write_actor_result(
            config,
            role,
            verdict="FAIL_PEER",
            evidence={"diagnostic": type(exc).__name__},
        )
        return 1


def _policy_name(value: object) -> str:
    name = getattr(value, "name", None)
    if isinstance(name, str):
        return name.lower()
    text = str(value)
    return text.rsplit(".", 1)[-1].lower()


def _endpoint_identity(info) -> str:
    namespace = str(getattr(info, "node_namespace", "") or "/")
    name = str(getattr(info, "node_name", ""))
    return namespace.rstrip("/") + "/" + name


def _endpoint_snapshot(info) -> dict[str, object]:
    qos = getattr(info, "qos_profile", None)
    raw_gid = getattr(info, "endpoint_gid", b"")
    try:
        gid = bytes(raw_gid).hex()
    except (TypeError, ValueError):
        gid = ""
    reliability = _policy_name(getattr(qos, "reliability", ""))
    durability = _policy_name(getattr(qos, "durability", ""))
    history = _policy_name(getattr(qos, "history", ""))
    depth = int(getattr(qos, "depth", 0))
    represented_axes = [
        axis
        for axis, represented in (
            ("reliability", reliability != "unknown"),
            ("durability", durability != "unknown"),
            ("history", history != "unknown"),
            ("depth", history != "unknown"),
        )
        if represented
    ]
    return {
        "node": _endpoint_identity(info),
        "gid": gid,
        "topicType": str(getattr(info, "topic_type", "")),
        "qos": {
            "reliability": reliability,
            "durability": durability,
            "history": history,
            "depth": depth,
            "representedAxes": represented_axes,
        },
    }


def _is_helper_endpoint(snapshot: Mapping[str, object]) -> bool:
    return str(snapshot.get("node", "")).rsplit("/", 1)[-1].startswith("phase184g_")


def _graph_for_topic(node, topic: str) -> dict[str, list[dict[str, object]]]:
    publishers = [
        _endpoint_snapshot(info)
        for info in node.get_publishers_info_by_topic(topic)
    ]
    subscriptions = [
        _endpoint_snapshot(info)
        for info in node.get_subscriptions_info_by_topic(topic)
    ]
    return {"publishers": publishers, "subscriptions": subscriptions}


def _expected_qos_by_topic(config: Mapping[str, object]) -> dict[str, dict[str, object]]:
    return protocol.expected_qos_by_topic(str(config["case"]))


def _normalized_policy(value: object) -> str:
    text = str(value).lower()
    aliases = {
        "qosreliabilitypolicy.reliable": "reliable",
        "qosreliabilitypolicy.best_effort": "best_effort",
        "qosreliabilitypolicy.system_default": "system_default",
        "qosdurabilitypolicy.volatile": "volatile",
        "qosdurabilitypolicy.transient_local": "transient_local",
        "qosdurabilitypolicy.system_default": "system_default",
        "qoshistorypolicy.keep_last": "keep_last",
        "qoshistorypolicy.keep_all": "keep_all",
        "qoshistorypolicy.system_default": "system_default",
    }
    return aliases.get(text, text)


_RESOLVED_SYSTEM_DEFAULT_POLICIES = {
    "reliability": frozenset({"system_default", "reliable", "best_effort"}),
    "durability": frozenset({"system_default", "volatile", "transient_local"}),
    "history": frozenset({"system_default", "keep_last", "keep_all"}),
}


def _observable_policy_matches(
    actual: object,
    expected: object,
    axis: str,
) -> bool:
    normalized_actual = _normalized_policy(actual)
    normalized_expected = _normalized_policy(expected)
    if normalized_actual == "unknown":
        return True
    if normalized_expected == "system_default":
        return normalized_actual in _RESOLVED_SYSTEM_DEFAULT_POLICIES[axis]
    return normalized_actual == normalized_expected


def _qos_equals(actual: Mapping[str, object], expected: Mapping[str, object]) -> bool:
    return (
        _normalized_policy(actual.get("reliability")) == expected["reliability"]
        and _normalized_policy(actual.get("durability")) == expected["durability"]
        and _normalized_policy(actual.get("history")) == expected["history"]
        and int(actual.get("depth", -1)) == int(expected["depth"])
    )


def _qos_observable_axes_match(
    actual: Mapping[str, object],
    expected: Mapping[str, object],
) -> bool:
    """Match FastDDS graph QoS without inventing unreported History/Depth."""

    reliability = _normalized_policy(actual.get("reliability"))
    durability = _normalized_policy(actual.get("durability"))
    history = _normalized_policy(actual.get("history"))
    depth = int(actual.get("depth", -1))
    return (
        _observable_policy_matches(reliability, expected["reliability"], "reliability")
        and _observable_policy_matches(durability, expected["durability"], "durability")
        and _observable_policy_matches(history, expected["history"], "history")
        and (
            depth == int(expected["depth"])
            or (history == "unknown" and depth == 0)
            or (
                expected["history"] == "system_default"
                and history != "unknown"
                and depth >= 0
            )
        )
    )


def _resolved_system_default_publishers_agree(
    publishers: Sequence[Mapping[str, object]],
    expected: Mapping[str, object],
) -> bool:
    for axis in ("reliability", "durability", "history"):
        if expected[axis] != "system_default":
            continue
        actual_values = {
            _normalized_policy(item["qos"].get(axis))
            for item in publishers
        }
        if len(actual_values) != 1:
            return False
    if expected["history"] == "system_default":
        depths = {int(item["qos"].get("depth", -1)) for item in publishers}
        if len(depths) != 1:
            return False
    return True


def _external_endpoints(
    graph: Mapping[str, Sequence[Mapping[str, object]]],
    direction: str,
    expected_type: str,
) -> list[dict[str, object]]:
    return [
        dict(item)
        for item in graph[direction]
        if not _is_helper_endpoint(item)
        and item.get("topicType") == expected_type
        and item.get("gid")
    ]


def _has_distinct_native_and_bridge_publishers(
    publishers: Sequence[Mapping[str, object]],
) -> bool:
    bridge = [
        item
        for item in publishers
        if str(item.get("node", "")).rstrip("/").endswith(
            "/unity2foxglove_ros2_bridge"
        )
    ]
    native = [
        item
        for item in publishers
        if item not in bridge
    ]
    return bool(
        bridge
        and native
        and {str(item.get("gid", "")) for item in bridge}
        .isdisjoint({str(item.get("gid", "")) for item in native})
    )


def _graph_ready(
    config: Mapping[str, object],
    graphs: Mapping[str, Mapping[str, Sequence[Mapping[str, object]]]],
) -> bool:
    case = str(config["case"])
    expected_type = str(config["interfaceType"])
    topics = [str(item) for item in config["topics"]]
    expected_qos = _expected_qos_by_topic(config)
    if case in {"multi-target", "qos-contract"}:
        for topic in topics:
            publishers = _external_endpoints(graphs[topic], "publishers", expected_type)
            if len(publishers) != 2 or len({item["gid"] for item in publishers}) != 2:
                return False
            if not _has_distinct_native_and_bridge_publishers(publishers):
                return False
            if any(
                not _qos_observable_axes_match(
                    item["qos"],
                    expected_qos[topic],
                )
                for item in publishers
            ):
                return False
            if not _resolved_system_default_publishers_agree(
                publishers,
                expected_qos[topic],
            ):
                return False
        return True
    if case == "stream-640hz":
        stream_subscriptions = _external_endpoints(
            graphs[topics[0]], "subscriptions", expected_type
        )
        origin_publishers = _external_endpoints(
            graphs[topics[1]], "publishers", expected_type
        )
        origin_subscriptions = _external_endpoints(
            graphs[topics[1]], "subscriptions", expected_type
        )
        required = (stream_subscriptions, origin_publishers, origin_subscriptions)
        return all(
            items
            and all(_qos_equals(item["qos"], expected_qos[topic]) for item in items)
            for items, topic in (
                (stream_subscriptions, topics[0]),
                (origin_publishers, topics[1]),
                (origin_subscriptions, topics[1]),
            )
        )
    if case == "degraded-target":
        return all(
            not _external_endpoints(graphs[topic], "publishers", expected_type)
            for topic in topics
        )
    return False


def _stream_subscription_ready(
    config: Mapping[str, object],
    graphs: Mapping[str, Mapping[str, Sequence[Mapping[str, object]]]],
) -> bool:
    """Require the exact external stream subscription before timed production."""

    stream_topic = str(config["topics"][0])
    graph = graphs.get(stream_topic)
    if not isinstance(graph, Mapping):
        return False
    subscriptions = graph.get("subscriptions")
    if not isinstance(subscriptions, Sequence):
        return False
    expected_type = str(config["interfaceType"])
    return any(
        isinstance(item, Mapping)
        and not _is_helper_endpoint(item)
        and str(item.get("node", "")).strip("/") != ""
        and item.get("topicType") == expected_type
        for item in subscriptions
    )


def _graph_evidence_from_topics(
    config: Mapping[str, object],
    graphs: Mapping[str, Mapping[str, Sequence[Mapping[str, object]]]],
) -> dict[str, object]:
    """Build strict graph evidence from one current rclpy topic snapshot."""

    if not _graph_ready(config, graphs):
        raise AcceptanceFailure(
            "FAIL_GRAPH",
            "Required transport endpoints and exact QoS were not observed.",
        )
    topics = [str(item) for item in config["topics"]]
    expected_type = str(config["interfaceType"])
    all_external: list[dict[str, object]] = []
    for topic in topics:
        for direction in ("publishers", "subscriptions"):
            all_external.extend(
                _external_endpoints(graphs[topic], direction, expected_type)
            )
    publishers_by_topic = {
        topic: [
            {"node": str(item["node"]), "gid": str(item["gid"])}
            for item in _external_endpoints(
                graphs[topic],
                "publishers",
                expected_type,
            )
        ]
        for topic in topics
    }
    gids = sorted(
        {
            str(item["gid"])
            for publishers in publishers_by_topic.values()
            for item in publishers
        }
    )
    nodes = sorted({str(item["node"]) for item in all_external})
    observed_qos = {
        topic: {
            direction: [
                item["qos"]
                for item in _external_endpoints(
                    graphs[topic],
                    direction,
                    expected_type,
                )
            ]
            for direction in ("publishers", "subscriptions")
        }
        for topic in topics
    }
    return {
        "endpointsObserved": True,
        "nodeIdentities": nodes,
        "publisherGids": gids,
        "publishersByTopic": publishers_by_topic,
        "negativeObservationSeconds": 0,
        "topics": dict(graphs),
        "requestedQos": _expected_qos_by_topic(config),
        "transportObservedQos": observed_qos,
        "qosMatches": True,
    }


def _write_graph_timeout_snapshot(
    config: Mapping[str, object],
    graphs: Mapping[str, Mapping[str, Sequence[Mapping[str, object]]]],
    *,
    stage: str = "graph",
) -> pathlib.Path:
    """Persist bounded raw graph facts only when an owned graph wait times out."""

    destination = (
        pathlib.Path(str(config["outputRoot"]))
        / "diagnostics"
        / f"ros2-peer-{stage}-timeout.json"
    )
    protocol.write_json_atomic(
        destination,
        {
            "case": str(config["case"]),
            "stage": stage,
            "topics": dict(graphs),
        },
        repo_root=repository_root(),
    )
    return destination


def _wait_for_stream_subscription(
    config: Mapping[str, object],
    rclpy_module,
    node,
    timeout_seconds: float = 30.0,
) -> dict[str, dict[str, list[dict[str, object]]]]:
    """Wait only for the typed Unity stream consumer needed before production."""

    stream_topic = str(config["topics"][0])
    deadline = time.monotonic() + timeout_seconds
    while True:
        rclpy_module.spin_once(node, timeout_sec=0.05)
        graphs = {stream_topic: _graph_for_topic(node, stream_topic)}
        if _stream_subscription_ready(config, graphs):
            return graphs
        if time.monotonic() >= deadline:
            _write_graph_timeout_snapshot(
                config,
                graphs,
                stage="stream-subscription",
            )
            raise AcceptanceFailure(
                "FAIL_GRAPH",
                "The ROS peer did not observe the typed Unity stream subscription.",
            )


def _wait_for_graph_snapshot(
    config: Mapping[str, object],
    rclpy_module,
    node,
    timeout_seconds: float = 30.0,
) -> dict[str, dict[str, list[dict[str, object]]]]:
    """Capture the exact transport graph from an already-owned ROS peer node."""

    topics = [str(item) for item in config["topics"]]
    deadline = time.monotonic() + timeout_seconds
    while True:
        rclpy_module.spin_once(node, timeout_sec=0.05)
        graphs = {topic: _graph_for_topic(node, topic) for topic in topics}
        if _graph_ready(config, graphs):
            return graphs
        if time.monotonic() >= deadline:
            _write_graph_timeout_snapshot(config, graphs)
            raise AcceptanceFailure(
                "FAIL_GRAPH",
                "The ROS peer did not capture the required transport graph.",
            )


def _wait_for_peer_result_document(
    config: Mapping[str, object],
    timeout_seconds: float = 180.0,
) -> dict[str, object]:
    """Wait for the peer's atomic PASS document without creating another ROS node."""

    path = _actor_path(config, "resultFiles", "ros2-peer")
    deadline = time.monotonic() + timeout_seconds
    while True:
        if path.is_file():
            try:
                return read_actor_document(
                    config,
                    "ros2-peer",
                    "resultFiles",
                )
            except AcceptanceFailure as exc:
                raise AcceptanceFailure(
                    "FAIL_GRAPH",
                    "The ROS peer did not produce auditable graph evidence.",
                ) from exc
        if time.monotonic() >= deadline:
            raise AcceptanceFailure(
                "FAIL_GRAPH",
                "The ROS peer graph snapshot did not arrive.",
            )
        time.sleep(0.1)


def _run_peer_graph_auditor(
    config: Mapping[str, object],
) -> Mapping[str, object]:
    """Independently validate a raw graph snapshot captured by the ROS peer."""

    write_actor_ready(
        config,
        "graph-observer",
        {"state": "peer-graph-auditor-ready", "topicCount": len(config["topics"])},
    )
    peer_result = _wait_for_peer_result_document(config)
    evidence = peer_result.get("evidence")
    graph_evidence = (
        evidence.get("graphEvidence")
        if isinstance(evidence, Mapping)
        else None
    )
    if (
        not isinstance(graph_evidence, Mapping)
        or graph_evidence.get("source") != "ros2-peer-rclpy-graph-api"
    ):
        raise AcceptanceFailure(
            "FAIL_GRAPH",
            "The ROS peer graph evidence source is missing or invalid.",
        )
    raw_topics = graph_evidence.get("topics")
    expected_topics = [str(item) for item in config["topics"]]
    if not isinstance(raw_topics, Mapping) or set(raw_topics) != set(expected_topics):
        raise AcceptanceFailure(
            "FAIL_GRAPH",
            "The ROS peer graph snapshot topics are incomplete.",
        )
    graphs: dict[str, dict[str, list[dict[str, object]]]] = {}
    for topic in expected_topics:
        raw_graph = raw_topics.get(topic)
        if not isinstance(raw_graph, Mapping):
            raise AcceptanceFailure("FAIL_GRAPH", "A graph topic entry is malformed.")
        graph: dict[str, list[dict[str, object]]] = {}
        for direction in ("publishers", "subscriptions"):
            entries = raw_graph.get(direction)
            if (
                not isinstance(entries, list)
                or any(not isinstance(entry, Mapping) for entry in entries)
            ):
                raise AcceptanceFailure(
                    "FAIL_GRAPH",
                    "A graph endpoint collection is malformed.",
                )
            graph[direction] = [dict(entry) for entry in entries]
        graphs[topic] = graph
    result = _graph_evidence_from_topics(config, graphs)
    wait_for_terminal_marker(config, 30.0)
    return result


def _run_graph_observer(config: Mapping[str, object], rclpy_module, node) -> Mapping[str, object]:
    case = str(config["case"])
    topics = [str(item) for item in config["topics"]]
    if case != "degraded-target":
        raise AcceptanceFailure(
            "FAIL_GRAPH",
            "Only the degraded case uses an independent rclpy graph node.",
        )
    write_actor_ready(
        config,
        "graph-observer",
        {"state": "graph-observer-ready", "topicCount": len(topics)},
    )
    _wait_for_unity_context(config)

    wait_for_log_marker(config, "PHASE184G_DEGRADED_WINDOW_STARTED", 60.0)
    deadline = (
        time.monotonic()
        + float(config["observationWindows"]["negativeSeconds"])
        + 0.25
    )
    final_graphs: dict[str, dict[str, list[dict[str, object]]]] = {}
    while time.monotonic() < deadline:
        rclpy_module.spin_once(node, timeout_sec=0.05)
        final_graphs = {topic: _graph_for_topic(node, topic) for topic in topics}
        if not _graph_ready(config, final_graphs):
            raise AcceptanceFailure(
                "FAIL_GRAPH",
                "Degraded case exposed a forbidden Native or Bridge publisher.",
            )
    wait_for_terminal_marker(config, 30.0)
    return {
        "endpointsObserved": True,
        "negativeWindowSeconds": config["observationWindows"]["negativeSeconds"],
        "noFallbackPublisher": True,
        "nodeIdentities": [],
        "publisherGids": [],
        "publishersByTopic": {topic: [] for topic in topics},
        "negativeObservationSeconds": config["observationWindows"]["negativeSeconds"],
        "topics": final_graphs,
        "requestedQos": {},
        "transportObservedQos": {},
        "qosMatches": True,
    }


def run_graph_observer_worker(config: Mapping[str, object]) -> int:
    role = "graph-observer"
    try:
        if str(config["case"]) == "degraded-target":
            import rclpy

            _load_ros_message_types(config)
            rclpy.init(args=None)
            node = rclpy.create_node(_helper_node_name("graph", config))
            try:
                evidence = _run_graph_observer(config, rclpy, node)
            finally:
                node.destroy_node()
                rclpy.shutdown()
        else:
            evidence = _run_peer_graph_auditor(config)
        write_actor_result(config, role, verdict="PASS", evidence=evidence)
        return 0
    except AcceptanceFailure as exc:
        write_actor_result(
            config,
            role,
            verdict=exc.code,
            evidence={"diagnostic": str(exc)[: protocol.MAX_DIAGNOSTIC_CHARACTERS]},
        )
        return 1
    except Exception as exc:
        write_actor_result(
            config,
            role,
            verdict="FAIL_GRAPH",
            evidence={"diagnostic": type(exc).__name__},
        )
        return 1


@dataclass
class PreparedRosRuntime:
    """All selected ROS/runtime state retained for one owned acceptance run."""

    peer: Any
    toolchain: Any
    lock: Any
    ros2_root: pathlib.Path
    peer_workspace: pathlib.Path
    peer_runtime_workspace: pathlib.Path
    build_environment: dict[str, str]
    actor_environment: dict[str, str]
    unity_environment: dict[str, str]
    bridge_install: pathlib.Path | None
    bridge_runtime_workspace: pathlib.Path | None
    zenoh_router: pathlib.Path | None
    zenoh_router_environment: dict[str, str] | None
    zenoh_router_config: pathlib.Path | None
    zenoh_session_config: pathlib.Path | None
    zenoh_router_endpoint: UnityZenohRouterEndpoint | None
    subst_roots: tuple[pathlib.Path, ...]


def choose_owned_loopback_port(excluded: Iterable[int] = ()) -> int:
    """Reserve and release one currently bindable IPv4 loopback port."""

    excluded_ports = {int(value) for value in excluded}
    for _ in range(32):
        with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as probe:
            exclusive = getattr(socket, "SO_EXCLUSIVEADDRUSE", None)
            if exclusive is not None:
                probe.setsockopt(socket.SOL_SOCKET, exclusive, 1)
            probe.bind(("127.0.0.1", 0))
            port = int(probe.getsockname()[1])
        if port not in excluded_ports:
            return port
    raise AcceptanceFailure("FAIL_PREFLIGHT", "Could not allocate a distinct loopback port.")


def require_available_loopback_port(port: int, label: str) -> int:
    """Fail before launch when an explicitly selected loopback port is occupied."""

    selected = int(port)
    if not 1 <= selected <= 65535:
        raise AcceptanceFailure("FAIL_PREFLIGHT", f"{label} port is outside 1..65535.")
    try:
        with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as probe:
            exclusive = getattr(socket, "SO_EXCLUSIVEADDRUSE", None)
            if exclusive is not None:
                probe.setsockopt(socket.SOL_SOCKET, exclusive, 1)
            probe.bind(("127.0.0.1", selected))
    except OSError as exc:
        raise AcceptanceFailure(
            "FAIL_PREFLIGHT",
            f"{label} loopback port is already in use.",
        ) from exc
    return selected


def load_unity_zenoh_router_endpoint(
    repository: pathlib.Path,
) -> UnityZenohRouterEndpoint:
    """Load the exact project setting that Unity applies before ROS starts."""

    settings_path = pathlib.Path(repository) / UNITY_ZENOH_SETTINGS_RELATIVE_PATH
    try:
        size = settings_path.stat().st_size
        if size <= 0 or size > MAX_UNITY_ZENOH_SETTINGS_BYTES:
            raise AcceptanceFailure(
                "FAIL_RUNTIME_SELECTION",
                "The Unity Zenoh router setting has an invalid size.",
            )
        document = json.loads(settings_path.read_text(encoding="utf-8"))
    except AcceptanceFailure:
        raise
    except (OSError, UnicodeError, json.JSONDecodeError) as exc:
        raise AcceptanceFailure(
            "FAIL_RUNTIME_SELECTION",
            "The Unity Zenoh router setting is unavailable or malformed.",
        ) from exc

    expected_keys = {
        "schemaVersion",
        "routerAddress",
        "routerPort",
        "endpoint",
    }
    if (
        not isinstance(document, dict)
        or set(document) != expected_keys
        or not isinstance(document.get("schemaVersion"), int)
        or isinstance(document.get("schemaVersion"), bool)
        or document["schemaVersion"] != 1
    ):
        raise AcceptanceFailure(
            "FAIL_RUNTIME_SELECTION",
            "The Unity Zenoh router setting has an unsupported schema.",
        )

    address = document["routerAddress"]
    port = document["routerPort"]
    if (
        not isinstance(address, str)
        or address not in {"localhost", "127.0.0.1"}
        or not isinstance(port, int)
        or isinstance(port, bool)
        or not 1 <= port <= 65535
    ):
        raise AcceptanceFailure(
            "FAIL_RUNTIME_SELECTION",
            "The Unity Zenoh router setting is not a valid loopback endpoint.",
        )

    endpoint = f"tcp/{address}:{port}"
    if document["endpoint"] != endpoint:
        raise AcceptanceFailure(
            "FAIL_RUNTIME_SELECTION",
            "The Unity Zenoh router setting contains inconsistent endpoint fields.",
        )
    return UnityZenohRouterEndpoint(
        endpoint=endpoint,
        host="127.0.0.1",
        port=port,
    )


def wait_for_owned_zenoh_router(
    process,
    log_path: pathlib.Path,
    endpoint: UnityZenohRouterEndpoint,
    timeout_seconds: float = 60.0,
) -> dict[str, object]:
    """Require both the owned marker and a live loopback listener."""

    deadline = time.monotonic() + timeout_seconds
    marker_observed = False
    while True:
        if process.poll() is not None:
            raise AcceptanceFailure(
                "FAIL_RUNTIME_SELECTION",
                "The owned Zenoh router exited before listener readiness.",
            )
        if not marker_observed:
            marker_observed = any(
                "Started Zenoh router with id " in line
                for line in read_log_lines(log_path)
            )
        if marker_observed:
            try:
                connection = socket.create_connection(
                    (endpoint.host, endpoint.port),
                    timeout=0.25,
                )
            except OSError:
                connection = None
            if connection is not None:
                connection.close()
                return {
                    "state": "owned-router-ready",
                    "endpoint": endpoint.endpoint,
                }
        remaining = deadline - time.monotonic()
        if remaining <= 0:
            raise AcceptanceFailure(
                "FAIL_RUNTIME_SELECTION",
                "The owned Zenoh router did not reach listener readiness.",
            )
        time.sleep(min(0.1, remaining))


def choose_domain_id(requested: int | None) -> int:
    """Return one explicit ROS domain without inheriting ambient process state."""

    if requested is not None:
        if not 0 <= int(requested) <= WINDOWS_SAFE_ROS_DOMAIN_ID_MAX:
            raise AcceptanceFailure(
                "FAIL_PREFLIGHT",
                "Windows ROS domain id must be in 0..166.",
            )
        return int(requested)
    return 64 + secrets.randbelow(96)


def _require_file(path: pathlib.Path, code: str, description: str) -> pathlib.Path:
    candidate = pathlib.Path(path).resolve()
    if not candidate.is_file():
        raise AcceptanceFailure(code, f"{description} is unavailable.")
    return candidate


def _new_run_identity(requested_run_id: str | None) -> tuple[str, str]:
    now = dt.datetime.now(dt.timezone.utc).strftime("%Y%m%d-%H%M%S")
    suffix = uuid.uuid4().hex[:10]
    run_id = requested_run_id or f"phase184g-{now}-{suffix}"
    if re.fullmatch(r"phase184g-[A-Za-z0-9][A-Za-z0-9._-]{7,79}", run_id) is None:
        raise AcceptanceFailure("FAIL_PREFLIGHT", "run id is unsafe or malformed.")
    return run_id, "p184g_" + uuid.uuid4().hex


def _prepare_run_directory(repository: pathlib.Path, run_id: str) -> pathlib.Path:
    output = (
        pathlib.Path(repository)
        / "build"
        / "phase184"
        / "acceptance"
        / run_id
    ).resolve()
    if output.exists():
        raise AcceptanceFailure(
            "FAIL_PREFLIGHT",
            "The selected Phase184-G run directory already exists.",
        )
    try:
        output.mkdir(parents=True, exist_ok=False)
        (output / "ready").mkdir()
        (output / "results").mkdir()
        write_private_json_atomic(
            output / ".phase184g-owned.json",
            {"schemaVersion": 1, "runId": run_id, "ownerPid": os.getpid()},
        )
    except OSError as exc:
        raise AcceptanceFailure(
            "FAIL_PREFLIGHT",
            "The owned Phase184-G run directory could not be created.",
        ) from exc
    return output


def _progress_snapshot(paths: Iterable[pathlib.Path]) -> tuple[tuple[str, int, int], ...]:
    """Return bounded file progress identities for process and tool-owned logs."""

    snapshot: list[tuple[str, int, int]] = []
    for raw_path in paths:
        path = pathlib.Path(raw_path)
        try:
            stat = path.stat()
            snapshot.append((str(path), int(stat.st_size), int(stat.st_mtime_ns)))
        except OSError:
            snapshot.append((str(path), -1, -1))
    return tuple(snapshot)


def _run_logged_preflight(
    command: Sequence[str],
    *,
    cwd: pathlib.Path,
    environment: Mapping[str, str],
    log_path: pathlib.Path,
    job: WindowsKillOnCloseJob | None,
    failure_code: str,
    operation: str,
    progress_paths: Iterable[pathlib.Path] = (),
) -> None:
    """Run one preparatory child with Job ownership and a no-progress watchdog."""

    log_path.parent.mkdir(parents=True, exist_ok=True)
    watchdog = protocol.ProgressWatchdog(operation)
    process = None
    try:
        with log_path.open("w", encoding="utf-8", errors="replace") as stream:
            process = subprocess.Popen(
                list(command),
                cwd=str(cwd),
                env=dict(environment),
                stdout=stream,
                stderr=subprocess.STDOUT,
                text=True,
                shell=False,
                **process_group_options(),
            )
            if job is not None:
                job.assign(process)
            observed_paths = (log_path, *(pathlib.Path(item) for item in progress_paths))
            last_progress = _progress_snapshot(observed_paths)
            watchdog.progress("preflight process started")
            while process.poll() is None:
                progress = _progress_snapshot(observed_paths)
                if progress != last_progress:
                    last_progress = progress
                    total_bytes = sum(max(0, item[1]) for item in progress)
                    watchdog.progress(f"log bytes={total_bytes}")
                try:
                    watchdog.check()
                except protocol.ProtocolFailure as exc:
                    terminate_owned_process(process)
                    raise AcceptanceFailure(failure_code, str(exc)) from exc
                time.sleep(0.1)
            exit_code = int(process.returncode)
    except AcceptanceFailure:
        if process is not None:
            terminate_owned_process(process)
        raise
    except OSError as exc:
        if process is not None:
            terminate_owned_process(process)
        raise AcceptanceFailure(failure_code, f"{operation} could not start.") from exc
    if exit_code != 0:
        raise AcceptanceFailure(
            failure_code,
            f"{operation} exited with code {exit_code}.",
        )


def _launch_logged_process(
    role: str,
    command: Sequence[str],
    *,
    cwd: pathlib.Path,
    environment: Mapping[str, str],
    log_path: pathlib.Path,
    owner: OwnedProcessSet,
    streams: list[TextIO],
):
    """Launch and register one long-lived named actor."""

    log_path.parent.mkdir(parents=True, exist_ok=True)
    stream: TextIO | None = None
    process = None
    try:
        stream = log_path.open("w", encoding="utf-8", errors="replace")
        process = subprocess.Popen(
            list(command),
            cwd=str(cwd),
            env=dict(environment),
            stdout=stream,
            stderr=subprocess.STDOUT,
            text=True,
            shell=False,
            **process_group_options(),
        )
        owner.register(role, process)
        streams.append(stream)
        return process
    except BaseException:
        if process is not None:
            terminate_owned_process(process)
        if stream is not None:
            with contextlib.suppress(Exception):
                stream.close()
        raise


def _wait_for_actor_readiness(
    config: Mapping[str, object],
    roles: Iterable[str],
    owner: OwnedProcessSet,
    timeout_seconds: float = 120.0,
) -> dict[str, dict[str, object]]:
    pending = set(roles)
    ready: dict[str, dict[str, object]] = {}
    deadline = time.monotonic() + timeout_seconds
    while pending:
        for role in tuple(pending):
            process = owner.process(role)
            if process is not None and process.poll() is not None:
                raise AcceptanceFailure(
                    protocol.failure_code(
                        "client" if role == "foxglove-client" else "peer"
                    ),
                    f"{role} exited before readiness.",
                )
            path = _actor_path(config, "readyFiles", role)
            if path.is_file():
                ready[role] = read_actor_document(config, role, "readyFiles")
                pending.remove(role)
        if not pending:
            break
        if time.monotonic() >= deadline:
            raise AcceptanceFailure(
                "FAIL_PREFLIGHT",
                "Required actor readiness expired: " + ",".join(sorted(pending)),
            )
        time.sleep(0.1)
    return ready


def _wait_for_actor_results(
    config: Mapping[str, object],
    roles: Iterable[str],
    owner: OwnedProcessSet,
    timeout_seconds: float = 60.0,
) -> dict[str, dict[str, object]]:
    pending = set(roles)
    results: dict[str, dict[str, object]] = {}
    deadline = time.monotonic() + timeout_seconds
    while pending:
        for role in tuple(pending):
            path = _actor_path(config, "resultFiles", role)
            if path.is_file():
                results[role] = read_actor_document(config, role, "resultFiles")
                pending.remove(role)
                continue
            process = owner.process(role)
            if process is not None and process.poll() is not None:
                raise AcceptanceFailure(
                    "FAIL_PROCESS_EXIT",
                    f"{role} exited without current PASS evidence.",
                )
        if not pending:
            break
        if time.monotonic() >= deadline:
            raise AcceptanceFailure(
                "FAIL_TERMINAL",
                "Required actor results expired: " + ",".join(sorted(pending)),
            )
        time.sleep(0.1)
    return results


def _read_one_u2r2_frame(connection: socket.socket) -> bytes:
    fixed = bytearray()
    while len(fixed) < 16:
        chunk = connection.recv(16 - len(fixed))
        if not chunk:
            raise AcceptanceFailure("FAIL_BRIDGE", "Bridge closed during health response.")
        fixed.extend(chunk)
    if bytes(fixed[:4]) != U2R2_MAGIC:
        raise AcceptanceFailure("FAIL_BRIDGE", "Bridge health response magic is invalid.")
    _version, _flags, header_size, payload_size = struct.unpack("<HHII", fixed[4:16])
    total = 16 + int(header_size) + int(payload_size)
    if (
        header_size <= 0
        or header_size > MAX_FRAME_HEADER_BYTES
        or payload_size > MAX_FRAME_PAYLOAD_BYTES
    ):
        raise AcceptanceFailure("FAIL_BRIDGE", "Bridge health response length is invalid.")
    while len(fixed) < total:
        chunk = connection.recv(total - len(fixed))
        if not chunk:
            raise AcceptanceFailure("FAIL_BRIDGE", "Bridge closed during health response.")
        fixed.extend(chunk)
    return bytes(fixed)


def wait_for_bridge_health(
    config: Mapping[str, object],
    process,
    timeout_seconds: float = 120.0,
) -> dict[str, object]:
    """Require one exact current-run response from a disposable health sidecar."""

    request_id = str(config["token"])
    deadline = time.monotonic() + timeout_seconds
    last_error: Exception | None = None
    while time.monotonic() < deadline:
        if process.poll() is not None:
            raise AcceptanceFailure("FAIL_BRIDGE", "Bridge exited before health readiness.")
        try:
            with socket.create_connection(
                (str(config["bridgeHost"]), int(config["bridgePort"])),
                timeout=1.0,
            ) as connection:
                connection.settimeout(2.0)
                connection.sendall(build_u2r2_health_frame(request_id))
                header, payload = decode_u2r2_frame(_read_one_u2r2_frame(connection))
                validate_bridge_health_response(header, payload, request_id)
                return dict(header)
        except (OSError, AcceptanceFailure) as exc:
            last_error = exc
            time.sleep(0.1)
    raise AcceptanceFailure(
        "FAIL_BRIDGE",
        "Bridge health readiness expired"
        + (f" ({type(last_error).__name__})." if last_error is not None else "."),
    )


_BRIDGE_PUBLISHER_LINE = re.compile(
    r"publisher (?P<topic>/\S+) (?P<type>\S+) "
    r"profile=(?P<profile>\S+) reliability=(?P<reliability>\S+) "
    r"durability=(?P<durability>\S+) history=(?P<history>\S+) "
    r"depth=(?P<depth>\d+)"
)


def parse_bridge_publisher_evidence(
    config: Mapping[str, object],
    log_path: pathlib.Path,
) -> dict[str, object]:
    """Parse only the dedicated current-run sidecar log."""

    expected = _expected_qos_by_topic(config)
    observed: dict[str, dict[str, object]] = {}
    for line in read_log_lines(log_path):
        match = _BRIDGE_PUBLISHER_LINE.search(line)
        if match is None:
            continue
        topic = match.group("topic")
        if topic not in expected:
            continue
        observed[topic] = {
            "profile": match.group("profile"),
            "reliability": match.group("reliability"),
            "durability": match.group("durability"),
            "history": match.group("history"),
            "depth": int(match.group("depth")),
        }
    if set(observed) != set(expected):
        raise AcceptanceFailure(
            "FAIL_BRIDGE",
            "Bridge did not log every expected publisher contract.",
        )
    for topic, actual in observed.items():
        requested = expected[topic]
        if _normalized_policy(actual["profile"]) != requested["profile"]:
            raise AcceptanceFailure(
                "FAIL_QOS",
                f"Bridge parsed QoS profile drifted for {topic}.",
            )
        comparable = {
            "reliability": actual["reliability"],
            "durability": actual["durability"],
            "history": actual["history"],
            "depth": actual["depth"],
        }
        if not _qos_equals(comparable, requested):
            raise AcceptanceFailure(
                "FAIL_QOS",
                f"Bridge parsed QoS drifted for {topic}.",
            )
    return {
        "nodeIdentity": "unity2foxglove_ros2_bridge",
        "publishers": observed,
    }


def _unity_version_from_log(log_path: pathlib.Path) -> str:
    for line in read_log_lines(log_path):
        match = _UNITY_VERSION.search(line)
        if match is not None and match.group(1).strip():
            return match.group(1).strip()
    raise AcceptanceFailure("FAIL_TERMINAL", "Unity log has no exact Editor version.")


def _marker_int(marker: TerminalMarker, name: str) -> int:
    value = marker.fields.get(name)
    try:
        parsed = int(value) if value is not None else -1
    except ValueError as exc:
        raise AcceptanceFailure(
            "FAIL_TERMINAL",
            f"Unity terminal field {name!r} is not an integer.",
        ) from exc
    if parsed < 0:
        raise AcceptanceFailure(
            "FAIL_TERMINAL",
            f"Unity terminal field {name!r} is missing or negative.",
        )
    return parsed


def _validated_stream_evidence(
    terminal: TerminalMarker,
    peer: Mapping[str, object],
) -> dict[str, object]:
    """Cross-check Unity ownership counters against the independent ROS producer."""

    received = _marker_int(terminal, "received")
    accepted = _marker_int(terminal, "accepted")
    drained = _marker_int(terminal, "drained")
    replaced = _marker_int(terminal, "replaced")
    rate_dropped = _marker_int(terminal, "rateDropped")
    high_water = _marker_int(terminal, "highWater")
    disposal_failures = _marker_int(terminal, "disposalFailures")
    last_sequence = _marker_int(terminal, "lastSequence")
    try:
        peer_offered = int(peer.get("offered", -1))
        nominal_hz = int(peer.get("nominalHz", -1))
        elapsed = float(peer.get("productionElapsedSeconds", -1.0))
    except (TypeError, ValueError) as exc:
        raise AcceptanceFailure(
            "FAIL_STREAM",
            "ROS stream producer evidence is malformed.",
        ) from exc
    if peer_offered != 1280:
        raise AcceptanceFailure(
            "FAIL_STREAM",
            "The ROS stream producer did not offer the locked 1280-sample run.",
        )
    if nominal_hz != 640 or elapsed < 1.8 or elapsed > 3.5:
        raise AcceptanceFailure(
            "FAIL_STREAM",
            "ROS stream production did not prove the nominal 640 Hz interval.",
        )
    if disposal_failures != 0:
        raise AcceptanceFailure(
            "FAIL_STREAM",
            "Unity reported a stream disposal failure.",
        )
    if (
        received <= 0
        or received > peer_offered
        or accepted + rate_dropped != received
        or drained + replaced != accepted
        or high_water != protocol.STREAM_CAPACITY
        or replaced <= 0
        or (last_sequence + 1) * 1000
        < peer_offered * protocol.MIN_STREAM_LAST_SEQUENCE_PERMILLE
    ):
        raise AcceptanceFailure(
            "FAIL_STREAM",
            "Unity stream counters do not prove bounded retained delivery.",
        )
    transport_dropped = peer_offered - received
    return {
        "offered": peer_offered,
        "received": received,
        "accepted": accepted,
        "replaced": replaced,
        "rateDropped": rate_dropped,
        "transportDropped": transport_dropped,
        "dropped": transport_dropped + rate_dropped,
        "drained": drained,
        "disposed": drained + replaced,
        "maximumQueueDepth": high_water,
        "lastSequence": last_sequence,
        "retainedOrdered": terminal.fields.get("ordered") == "True",
        "ownershipBalanced": terminal.fields.get("ownershipBalanced") == "True",
    }


def _ensure_acceptance_scene(
    editor: pathlib.Path,
    repository: pathlib.Path,
    output: pathlib.Path,
    job: WindowsKillOnCloseJob | None,
) -> pathlib.Path:
    scene = (
        pathlib.Path(repository)
        / "Unity2Foxglove"
        / "Assets"
        / "Scenes"
        / "ManualAcceptance"
        / "Phase184FoxRunProfileAcceptance.unity"
    )
    command = [
        str(editor),
        "-batchmode",
        "-nographics",
        "-quit",
        "-projectPath",
        str(pathlib.Path(repository) / "Unity2Foxglove"),
        "-executeMethod",
        "Unity2Foxglove.Phase184FoxRunProfileAcceptanceBuilder.CreateOrRefreshAcceptanceScene",
        "-logFile",
        str(output / "scene-builder.log"),
    ]
    _run_logged_preflight(
        command,
        cwd=repository,
        environment=_clean_environment(os.environ),
        log_path=output / "scene-builder-process.log",
        job=job,
        failure_code="FAIL_UNITY_STARTUP",
        operation="unity-startup",
        progress_paths=(output / "scene-builder.log",),
    )
    if not scene.is_file() or "PHASE184G_SCENE_BUILDER_PASS" not in "\n".join(
        read_log_lines(output / "scene-builder.log")
    ):
        raise AcceptanceFailure(
            "FAIL_UNITY_STARTUP",
            "Unity did not create and validate the Phase184 acceptance scene.",
        )
    return scene


def _select_unity_runtime(
    *,
    peer,
    editor: pathlib.Path,
    repository: pathlib.Path,
    output: pathlib.Path,
    distro: str,
    rmw: str,
    job: WindowsKillOnCloseJob | None,
) -> None:
    selection_log = output / "runtime-selection.log"
    current_selection = _current_unity_runtime_selection_evidence(
        repository,
        distro,
        rmw,
    )
    if current_selection is not None:
        try:
            selection_log.write_text(
                "PHASE184G_RUNTIME_SELECTION_REUSED "
                f"distro={current_selection['rosDistro']} "
                f"rmw={current_selection['rmwImplementation']} "
                f"runtime={current_selection['runtimePackage']} "
                f"typesupport={current_selection['typesupportPackage']}\n",
                encoding="utf-8",
            )
        except OSError as exc:
            raise AcceptanceFailure(
                "FAIL_RUNTIME_SELECTION",
                "The reused Unity runtime selection evidence could not be persisted.",
            ) from exc
        return

    command = peer.build_runtime_selection_batch_command(
        editor,
        repository / "Unity2Foxglove",
        selection_log,
        distro,
        rmw,
    )
    _run_logged_preflight(
        command,
        cwd=repository,
        environment=peer.ros2env.sanitized_subprocess_env(os.environ),
        log_path=output / "runtime-selection-process.log",
        job=job,
        failure_code="FAIL_RUNTIME_SELECTION",
        operation="runtime-selection",
        progress_paths=(selection_log,),
    )
    if peer._RUNTIME_SELECTION_READY_MARKER not in "\n".join(
        read_log_lines(selection_log)
    ):
        raise AcceptanceFailure(
            "FAIL_RUNTIME_SELECTION",
            "Unity runtime selection exited without its validated readiness marker.",
        )


def _current_unity_runtime_selection_evidence(
    repository: pathlib.Path,
    distro: str,
    rmw: str,
) -> dict[str, str] | None:
    """Prove an exact default-RMW selection before skipping Package Manager."""

    runtime_package = (
        f"dev.unity2foxglove.ros2forunity.runtime.{distro}.win64"
    )
    typesupport_package = (
        "dev.unity2foxglove.foxrun.ros2.interfaces.typesupport."
        f"{distro}.win64"
    )
    runtime_reference = f"file:../../Packages/{runtime_package}"
    typesupport_reference = f"file:../../Packages/{typesupport_package}"
    root = pathlib.Path(repository)
    project = root / "Unity2Foxglove"
    runtime_root = root / "Packages" / runtime_package
    typesupport_root = root / "Packages" / typesupport_package

    def read_mapping(path: pathlib.Path) -> Mapping[str, Any] | None:
        document = json.loads(path.read_text(encoding="utf-8"))
        return document if isinstance(document, Mapping) else None

    def selected_package_ids(
        dependencies: Mapping[str, Any],
        prefix: str,
    ) -> tuple[str, ...]:
        return tuple(
            sorted(
                key
                for key in dependencies
                if isinstance(key, str) and key.startswith(prefix)
            )
        )

    try:
        manifest = read_mapping(project / "Packages" / "manifest.json")
        lock = read_mapping(project / "Packages" / "packages-lock.json")
        runtime_manifest = read_mapping(
            runtime_root / "RuntimeSupport" / "runtime-manifest.json"
        )
        runtime_identity = read_mapping(runtime_root / "package.json")
        typesupport_identity = read_mapping(typesupport_root / "package.json")
        project_settings = (
            project / "ProjectSettings" / "ProjectSettings.asset"
        ).read_text(encoding="utf-8")
        if any(
            document is None
            for document in (
                manifest,
                lock,
                runtime_manifest,
                runtime_identity,
                typesupport_identity,
            )
        ):
            return None

        manifest_dependencies = manifest.get("dependencies")
        lock_dependencies = lock.get("dependencies")
        if not isinstance(manifest_dependencies, Mapping) or not isinstance(
            lock_dependencies,
            Mapping,
        ):
            return None
        if selected_package_ids(
            manifest_dependencies,
            _UNITY_RUNTIME_PACKAGE_PREFIX,
        ) != (runtime_package,):
            return None
        if selected_package_ids(
            manifest_dependencies,
            _UNITY_TYPESUPPORT_PACKAGE_PREFIX,
        ) != (typesupport_package,):
            return None
        if selected_package_ids(
            lock_dependencies,
            _UNITY_RUNTIME_PACKAGE_PREFIX,
        ) != (runtime_package,):
            return None
        if selected_package_ids(
            lock_dependencies,
            _UNITY_TYPESUPPORT_PACKAGE_PREFIX,
        ) != (typesupport_package,):
            return None
        if manifest_dependencies.get(runtime_package) != runtime_reference:
            return None
        if (
            manifest_dependencies.get(typesupport_package)
            != typesupport_reference
        ):
            return None

        runtime_lock = lock_dependencies.get(runtime_package)
        typesupport_lock = lock_dependencies.get(typesupport_package)
        if not isinstance(runtime_lock, Mapping) or not isinstance(
            typesupport_lock,
            Mapping,
        ):
            return None
        for entry, reference in (
            (runtime_lock, runtime_reference),
            (typesupport_lock, typesupport_reference),
        ):
            if (
                entry.get("version") != reference
                or entry.get("source") != "local"
                or type(entry.get("depth")) is not int
                or entry.get("depth") != 0
            ):
                return None

        runtime_version = runtime_identity.get("version")
        typesupport_dependencies = typesupport_identity.get("dependencies")
        lock_typesupport_dependencies = typesupport_lock.get("dependencies")
        if (
            runtime_identity.get("name") != runtime_package
            or not isinstance(runtime_version, str)
            or not runtime_version
            or typesupport_identity.get("name") != typesupport_package
            or typesupport_identity.get(
                "unity2foxgloveFoxRunCustomTypesupportAddOn"
            )
            is not True
            or not isinstance(typesupport_dependencies, Mapping)
            or typesupport_dependencies.get(runtime_package) != runtime_version
            or not isinstance(lock_typesupport_dependencies, Mapping)
            or lock_typesupport_dependencies.get(runtime_package)
            != runtime_version
        ):
            return None

        default_rmw = runtime_manifest.get(
            "defaultRmwImplementation",
            runtime_manifest.get("rmwImplementation"),
        )
        if (
            runtime_manifest.get("packageName") != runtime_package
            or runtime_manifest.get("rosDistro") != distro
            or runtime_manifest.get("platform") != "win64"
            or runtime_manifest.get("architecture") != "x86_64"
            or default_rmw != rmw
        ):
            return None

        defines_header = re.search(
            r"(?m)^(?P<indent>[ \t]*)scriptingDefineSymbols:[ \t]*$",
            project_settings,
        )
        if defines_header is None:
            return None
        defines_indent = len(defines_header.group("indent"))
        standalone_value: str | None = None
        for line in project_settings[defines_header.end() :].splitlines():
            if not line.strip():
                continue
            line_indent = len(line) - len(line.lstrip(" \t"))
            if line_indent <= defines_indent:
                break
            standalone_match = re.match(
                r"^[ \t]*Standalone:[ \t]*(.*)$",
                line,
            )
            if standalone_match is not None:
                standalone_value = standalone_match.group(1)
                break
        if standalone_value is None:
            return None
        standalone_defines = {
            value.strip()
            for value in standalone_value.split(";")
            if value.strip()
        }
        if not {
            _UNITY_RUNTIME_DEFINE,
            _UNITY_TYPESUPPORT_DEFINE,
        }.issubset(standalone_defines):
            return None
    except (OSError, UnicodeError, json.JSONDecodeError, TypeError, ValueError):
        return None

    return {
        "mode": "reused",
        "runtimePackage": runtime_package,
        "typesupportPackage": typesupport_package,
        "rosDistro": distro,
        "rmwImplementation": rmw,
    }


def _bridge_source_root(repository: pathlib.Path) -> pathlib.Path:
    return (
        pathlib.Path(repository)
        / "Tools"
        / "ros2_bridge"
        / "unity2foxglove_ros2_bridge"
    )


def _sha256_file(path: pathlib.Path) -> str:
    digest = hashlib.sha256()
    with pathlib.Path(path).open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def _bridge_source_digest(repository: pathlib.Path) -> str:
    """Hash every staged Bridge source path and byte without build noise."""

    source = _bridge_source_root(repository)
    if not (source / "package.xml").is_file():
        raise AcceptanceFailure("FAIL_BUILD", "The maintained Bridge source is absent.")
    digest = hashlib.sha256()
    files: list[pathlib.Path] = []
    for path in source.rglob("*"):
        relative = path.relative_to(source)
        if any(part in _BRIDGE_SOURCE_IGNORES for part in relative.parts):
            continue
        if path.is_symlink():
            raise AcceptanceFailure(
                "FAIL_BUILD",
                "The maintained Bridge source cannot contain symbolic links.",
            )
        if path.is_file():
            files.append(path)
    for path in sorted(files, key=lambda item: item.relative_to(source).as_posix()):
        relative = path.relative_to(source).as_posix().encode("utf-8")
        digest.update(len(relative).to_bytes(4, "little"))
        digest.update(relative)
        digest.update(path.stat().st_size.to_bytes(8, "little"))
        with path.open("rb") as stream:
            for block in iter(lambda: stream.read(1024 * 1024), b""):
                digest.update(block)
    return digest.hexdigest()


def _bridge_build_path_identity(path: pathlib.Path) -> dict[str, object]:
    candidate = pathlib.Path(path).resolve(strict=False)
    try:
        stat = candidate.stat()
    except OSError:
        return {"path": str(candidate), "available": False}
    return {
        "path": str(candidate),
        "available": True,
        "size": int(stat.st_size),
        "modifiedNs": int(stat.st_mtime_ns),
    }


def bridge_build_cache_key(
    repository: pathlib.Path,
    profile: str,
    distro: str,
    rmw: str,
    toolchain,
    build_command: Sequence[str],
    build_environment: Mapping[str, str],
) -> str:
    """Bind a reusable Windows Bridge build to all material local inputs."""

    if (
        profile not in protocol.PROFILE_CONTRACTS
        or not distro
        or not rmw
        or not build_command
    ):
        raise AcceptanceFailure("FAIL_BUILD", "Bridge cache identity is incomplete.")
    environment_keys = (
        "VCToolsVersion",
        "VisualStudioVersion",
        "WindowsSDKVersion",
        "VSCMD_ARG_TGT_ARCH",
        "OPENSSL_ROOT_DIR",
        "nlohmann_json_DIR",
        "tinyxml2_DIR",
    )
    payload = {
        "format": _BRIDGE_CACHE_FORMAT,
        "sourceSha256": _bridge_source_digest(repository),
        "profile": profile,
        "distro": distro,
        "rmw": rmw,
        "buildCommand": list(build_command),
        "environment": {
            key: str(build_environment.get(key, ""))
            for key in environment_keys
        },
        "toolchain": {
            "ros2Root": _bridge_build_path_identity(toolchain.ros2_root),
            "ros2LocalSetup": _bridge_build_path_identity(
                pathlib.Path(toolchain.ros2_root) / "local_setup.bat"
            ),
            "python": _bridge_build_path_identity(toolchain.python_executable),
            "colcon": _bridge_build_path_identity(toolchain.colcon_executable),
        },
    }
    encoded = json.dumps(
        payload,
        sort_keys=True,
        separators=(",", ":"),
        ensure_ascii=True,
    ).encode("utf-8")
    return hashlib.sha256(encoded).hexdigest()


def _bridge_cache_owner(profile: str) -> dict[str, object]:
    return {
        "schemaVersion": _BRIDGE_CACHE_FORMAT,
        "owner": _BRIDGE_CACHE_OWNER,
        "profile": profile,
    }


def _read_bridge_cache_json(path: pathlib.Path) -> Mapping[str, object] | None:
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError):
        return None
    return value if isinstance(value, Mapping) else None


def _bridge_cache_is_owned(overlay: pathlib.Path, profile: str) -> bool:
    marker = _read_bridge_cache_json(overlay / _BRIDGE_CACHE_OWNERSHIP_NAME)
    return marker == _bridge_cache_owner(profile)


def _bridge_cache_executable(overlay: pathlib.Path) -> pathlib.Path:
    return (
        overlay
        / "install"
        / "lib"
        / _BRIDGE_PACKAGE_NAME
        / f"{_BRIDGE_PACKAGE_NAME}.exe"
    )


def _bridge_cache_has_outputs(overlay: pathlib.Path) -> bool:
    install = overlay / "install"
    return (
        _bridge_cache_executable(overlay).is_file()
        and (install / "local_setup.bat").is_file()
        and (install / "share" / _BRIDGE_PACKAGE_NAME / "package.xml").is_file()
    )


def _bridge_cache_matches(
    overlay: pathlib.Path,
    profile: str,
    cache_key: str,
) -> bool:
    if not _bridge_cache_is_owned(overlay, profile) or not _bridge_cache_has_outputs(
        overlay
    ):
        return False
    manifest = _read_bridge_cache_json(overlay / _BRIDGE_CACHE_MANIFEST_NAME)
    if (
        manifest is None
        or set(manifest)
        != {
            "schemaVersion",
            "cacheKey",
            "profile",
            "executableSha256",
        }
        or manifest.get("schemaVersion") != _BRIDGE_CACHE_FORMAT
        or manifest.get("cacheKey") != cache_key
        or manifest.get("profile") != profile
    ):
        return False
    try:
        return manifest.get("executableSha256") == _sha256_file(
            _bridge_cache_executable(overlay)
        )
    except OSError:
        return False


def prepare_bridge_build_workspace(
    cache_root: pathlib.Path,
    profile: str,
    cache_key: str,
) -> tuple[pathlib.Path, bool]:
    """Reuse or replace only an exactly owned profile-stable Bridge cache."""

    if profile not in protocol.PROFILE_CONTRACTS or not re.fullmatch(
        r"[0-9a-f]{64}",
        cache_key,
    ):
        raise AcceptanceFailure("FAIL_BUILD", "Bridge cache key or profile is invalid.")
    root = pathlib.Path(cache_root).resolve(strict=False)
    raw_overlay = pathlib.Path(
        os.path.abspath(os.fspath(root / profile / "bridge-overlay"))
    )
    overlay = raw_overlay.resolve(strict=False)
    if (
        os.path.normcase(os.fspath(raw_overlay))
        != os.path.normcase(os.fspath(overlay))
        or overlay == root
    ):
        raise AcceptanceFailure("FAIL_BUILD", "Bridge cache path is redirected.")
    try:
        overlay.relative_to(root)
    except ValueError as exc:
        raise AcceptanceFailure("FAIL_BUILD", "Bridge cache path escaped its root.") from exc

    if overlay.exists():
        if _bridge_cache_matches(overlay, profile, cache_key):
            return overlay, True
        if not overlay.is_dir() or not _bridge_cache_is_owned(overlay, profile):
            raise AcceptanceFailure(
                "FAIL_BUILD",
                "Refusing to replace an unowned Bridge cache workspace.",
            )
        try:
            shutil.rmtree(overlay)
        except OSError as exc:
            raise AcceptanceFailure(
                "FAIL_BUILD",
                "The stale owned Bridge cache could not be replaced.",
            ) from exc

    try:
        overlay.mkdir(parents=True, exist_ok=False)
        write_private_json_atomic(
            overlay / _BRIDGE_CACHE_OWNERSHIP_NAME,
            _bridge_cache_owner(profile),
        )
    except OSError as exc:
        raise AcceptanceFailure(
            "FAIL_BUILD",
            "The owned Bridge cache workspace could not be created.",
        ) from exc
    return overlay, False


def seal_bridge_build_workspace(
    overlay: pathlib.Path,
    profile: str,
    cache_key: str,
) -> None:
    """Seal a tested Bridge cache with its exact executable digest."""

    candidate = pathlib.Path(overlay)
    if (
        profile not in protocol.PROFILE_CONTRACTS
        or not re.fullmatch(r"[0-9a-f]{64}", cache_key)
        or not _bridge_cache_is_owned(candidate, profile)
        or not _bridge_cache_has_outputs(candidate)
    ):
        raise AcceptanceFailure(
            "FAIL_BUILD",
            "The Bridge cache cannot be sealed without owned tested outputs.",
        )
    write_private_json_atomic(
        candidate / _BRIDGE_CACHE_MANIFEST_NAME,
        {
            "schemaVersion": _BRIDGE_CACHE_FORMAT,
            "cacheKey": cache_key,
            "profile": profile,
            "executableSha256": _sha256_file(
                _bridge_cache_executable(candidate)
            ),
        },
    )


def _copy_bridge_source(repository: pathlib.Path, overlay: pathlib.Path) -> None:
    source = _bridge_source_root(repository)
    destination = overlay / "src" / "unity2foxglove_ros2_bridge"
    if not (source / "package.xml").is_file() or destination.exists():
        raise AcceptanceFailure(
            "FAIL_BUILD",
            "The Bridge source or owned overlay destination is invalid.",
        )
    try:
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copytree(
            source,
            destination,
            ignore=shutil.ignore_patterns(
                *_BRIDGE_SOURCE_IGNORES,
            ),
        )
    except OSError as exc:
        raise AcceptanceFailure(
            "FAIL_BUILD",
            "The Bridge source could not be staged into its owned overlay.",
        ) from exc


def _paths_are_distinct(first: pathlib.Path, second: pathlib.Path) -> bool:
    """Compare runtime path spellings without resolving a subst/junction target."""

    left = os.path.normcase(os.path.abspath(os.fspath(first)))
    right = os.path.normcase(os.path.abspath(os.fspath(second)))
    return left != right


def prepare_windows_bridge_build_environment(
    environment: Mapping[str, str],
    ros2_root: pathlib.Path,
) -> dict[str, str]:
    """Pin native Bridge dependencies to the selected ROS 2 Pixi prefix."""

    result = dict(environment)
    library = (
        pathlib.Path(ros2_root)
        / ".pixi"
        / "envs"
        / "default"
        / "Library"
    )
    nlohmann_directory = library / "share" / "cmake" / "nlohmann_json"
    tinyxml_directory = library / "lib" / "cmake" / "tinyxml2"
    required = {
        "OpenSSL headers": library / "include" / "openssl" / "opensslv.h",
        "OpenSSL crypto library": library / "lib" / "libcrypto.lib",
        "OpenSSL TLS library": library / "lib" / "libssl.lib",
        "tinyxml2 CMake package": tinyxml_directory / "tinyxml2-config.cmake",
        "nlohmann_json CMake package": (
            nlohmann_directory / "nlohmann_jsonConfig.cmake"
        ),
    }
    missing = [label for label, path in required.items() if not path.is_file()]
    if missing:
        raise AcceptanceFailure(
            "FAIL_BUILD",
            "The selected Windows ROS 2 prefix is missing required Bridge "
            "build dependencies: "
            + ", ".join(missing)
            + ".",
        )

    prefix_entries = [str(library)]
    prefix_entries.extend(
        entry
        for entry in str(result.get("CMAKE_PREFIX_PATH", "")).split(os.pathsep)
        if entry and _paths_are_distinct(pathlib.Path(entry), library)
    )
    result["CMAKE_PREFIX_PATH"] = os.pathsep.join(prefix_entries)
    result["OPENSSL_ROOT_DIR"] = str(library)
    result["nlohmann_json_DIR"] = str(nlohmann_directory)
    result["tinyxml2_DIR"] = str(tinyxml_directory)
    return result


def _prepare_ros_runtime(
    *,
    config: Mapping[str, object],
    editor: pathlib.Path,
    repository: pathlib.Path,
    output: pathlib.Path,
    stack: contextlib.ExitStack,
    job: WindowsKillOnCloseJob | None,
) -> PreparedRosRuntime:
    """Select, build, and compose one exact Phase181-backed ROS environment."""

    peer = _phase181_peer_module()
    distro = str(config["rosDistro"])
    rmw = str(config["rmw"])
    profile = str(config["profile"])
    static_package = repository / "Packages" / INTERFACE_PACKAGE_ID
    lock = peer.load_static_interface_lock(static_package)
    if (
        lock.interface_digest != config["interfaceDigest"]
        or lock.ros_package_name != config["interfacePackage"]
        or f"{lock.ros_package_name}/msg/{lock.envelope_message_name}"
        != config["interfaceType"]
    ):
        raise AcceptanceFailure(
            "FAIL_PREFLIGHT",
            "The Phase181 static interface lock drifted before runtime preparation.",
        )

    _select_unity_runtime(
        peer=peer,
        editor=editor,
        repository=repository,
        output=output,
        distro=distro,
        rmw=rmw,
        job=job,
    )
    selected_addon = peer.require_selected_typesupport_addon(
        repository,
        distro,
    )
    runtime_plugins, custom_plugins = (
        peer.resolve_editor_batch_native_plugin_directories(
            repository,
            distro,
            selected_addon,
        )
    )

    ros2_root = (
        repository / "ros2-windows" / f"ros2_{distro}"
    )
    toolchain = peer.resolve_windows_peer_toolchain(ros2_root)
    ros_environment = peer.ros2env.build_ros_env(
        toolchain.ros2_root,
        rmw,
        str(config["discoveryRange"]),
        str(config["domainId"]),
        distro,
    )
    msvc_environment = peer.capture_windows_msvc_environment(ros_environment)
    build_environment = peer.merge_windows_peer_build_environment(
        ros_environment,
        msvc_environment,
    )

    validator_command = peer.build_addon_validator_command(
        repository,
        distro,
        rmw,
    )
    _run_logged_preflight(
        validator_command,
        cwd=repository,
        environment=peer.ros2env.sanitized_subprocess_env(os.environ),
        log_path=output / "typesupport-preflight.log",
        job=job,
        failure_code="FAIL_PREFLIGHT",
        operation="preflight",
    )

    colcon_command = peer.build_windows_colcon_command(
        toolchain.colcon_executable,
        lock.ros_package_name,
        toolchain.python_executable,
    )
    cache_key = peer.peer_build_cache_key(
        lock,
        profile,
        distro,
        rmw,
        toolchain,
        colcon_command,
    )
    peer_workspace, reused = peer.prepare_peer_build_workspace(
        repository / "build" / "phase181",
        profile,
        cache_key,
        lock.ros_package_name,
    )
    _physical_peer, runtime_peer = stack.enter_context(
        peer.temporary_short_windows_peer_workspace(peer_workspace)
    )
    subst_roots: list[pathlib.Path] = []
    if _paths_are_distinct(runtime_peer, peer_workspace):
        subst_roots.append(runtime_peer)
    if not reused:
        peer.stage_locked_ros_source(
            static_package,
            runtime_peer,
            lock.ros_package_name,
        )
        _run_logged_preflight(
            colcon_command,
            cwd=runtime_peer,
            environment=build_environment,
            log_path=output / "phase181-peer-build.log",
            job=job,
            failure_code="FAIL_BUILD",
            operation="build",
        )
        peer.seal_peer_build_workspace(
            peer_workspace,
            cache_key,
            lock.ros_package_name,
        )

    bridge_install: pathlib.Path | None = None
    bridge_runtime_workspace: pathlib.Path | None = None
    case = str(config["case"])
    if case in {"multi-target", "qos-contract"}:
        bridge_underlay = build_ros_actor_environment(
            build_environment,
            bridge_install=None,
            peer_install=runtime_peer / "install",
            ros2_root=toolchain.ros2_root,
            distro=distro,
            rmw=rmw,
            domain_id=int(config["domainId"]),
            discovery_range=str(config["discoveryRange"]),
            topology_id="",
            zenoh_session_config=None,
        )
        bridge_build_environment = peer.merge_windows_peer_build_environment(
            bridge_underlay,
            msvc_environment,
        )
        bridge_build_environment = prepare_windows_bridge_build_environment(
            bridge_build_environment,
            toolchain.ros2_root,
        )
        bridge_build_command = [
            str(toolchain.colcon_executable),
            "build",
            "--merge-install",
            "--packages-select",
            "unity2foxglove_ros2_bridge",
            "--cmake-args",
            "-G",
            "Ninja",
            "-DCMAKE_BUILD_TYPE=Release",
            "-DBUILD_TESTING=ON",
            "-DPython3_EXECUTABLE="
            + pathlib.Path(toolchain.python_executable).as_posix(),
            "-DPYTHON_EXECUTABLE="
            + pathlib.Path(toolchain.python_executable).as_posix(),
        ]
        bridge_cache_key = bridge_build_cache_key(
            repository,
            profile,
            distro,
            rmw,
            toolchain,
            bridge_build_command,
            bridge_build_environment,
        )
        overlay, bridge_cache_reused = prepare_bridge_build_workspace(
            repository / "build" / "phase184" / "bridge-cache",
            profile,
            bridge_cache_key,
        )
        bridge_install = overlay / "install"
        if bridge_install.resolve(strict=False) != pathlib.Path(
            str(config["bridgeOverlayInstall"])
        ).resolve(strict=False):
            raise AcceptanceFailure(
                "FAIL_PREFLIGHT",
                "The prepared Bridge cache does not match the immutable run configuration.",
            )
        _physical_bridge, runtime_bridge = stack.enter_context(
            peer.temporary_short_windows_peer_workspace(overlay)
        )
        bridge_runtime_workspace = runtime_bridge
        if _paths_are_distinct(runtime_bridge, overlay):
            subst_roots.append(runtime_bridge)
        if not bridge_cache_reused:
            _copy_bridge_source(repository, overlay)
            _run_logged_preflight(
                bridge_build_command,
                cwd=runtime_bridge,
                environment=bridge_build_environment,
                log_path=output / "bridge-build.log",
                job=job,
                failure_code="FAIL_BUILD",
                operation="build",
            )
            ctest = shutil.which(
                "ctest.exe",
                path=bridge_build_environment.get("PATH"),
            )
            if not ctest:
                raise AcceptanceFailure(
                    "FAIL_BUILD",
                    "The selected ROS/MSVC environment has no ctest executable.",
                )
            _run_logged_preflight(
                [ctest, "--output-on-failure", "-C", "Release"],
                cwd=runtime_bridge / "build" / _BRIDGE_PACKAGE_NAME,
                environment=bridge_build_environment,
                log_path=output / "bridge-tests.log",
                job=job,
                failure_code="FAIL_BUILD",
                operation="build",
            )
            seal_bridge_build_workspace(
                overlay,
                profile,
                bridge_cache_key,
            )
        manifest = _read_bridge_cache_json(
            overlay / _BRIDGE_CACHE_MANIFEST_NAME
        )
        if manifest is None or not _bridge_cache_matches(
            overlay,
            profile,
            bridge_cache_key,
        ):
            raise AcceptanceFailure(
                "FAIL_BUILD",
                "The tested Bridge cache did not retain exact sealed evidence.",
            )
        protocol.write_json_atomic(
            output / "bridge-cache-evidence.json",
            {
                "schemaVersion": _BRIDGE_CACHE_FORMAT,
                "profile": profile,
                "cacheKey": bridge_cache_key,
                "sourceSha256": _bridge_source_digest(repository),
                "executableSha256": manifest["executableSha256"],
                "reused": bridge_cache_reused,
                "buildVerdict": "CACHE_VALIDATED" if bridge_cache_reused else "PASS",
                "testVerdict": "CACHE_VALIDATED" if bridge_cache_reused else "PASS",
            },
            repo_root=repository,
        )

    zenoh_router: pathlib.Path | None = None
    zenoh_router_environment: dict[str, str] | None = None
    zenoh_router_config: pathlib.Path | None = None
    zenoh_session_config: pathlib.Path | None = None
    zenoh_router_endpoint: UnityZenohRouterEndpoint | None = None
    if rmw == "rmw_zenoh_cpp":
        try:
            import phase179_zenoh_topology as zenoh
        except ImportError as exc:
            raise AcceptanceFailure(
                "FAIL_RUNTIME_SELECTION",
                "Zenoh topology helpers are unavailable.",
            ) from exc
        zenoh_router = (
            toolchain.ros2_root
            / "Lib"
            / "rmw_zenoh_cpp"
            / "rmw_zenohd.exe"
        )
        _require_file(
            zenoh_router,
            "FAIL_RUNTIME_SELECTION",
            "Repository-local Zenoh router",
        )
        templates = (
            toolchain.ros2_root
            / "share"
            / "rmw_zenoh_cpp"
            / "config"
        )
        zenoh_router_endpoint = load_unity_zenoh_router_endpoint(repository)
        if zenoh_router_endpoint.port in {
            int(config["foxglovePort"]),
            int(config["bridgePort"]),
        }:
            raise AcceptanceFailure(
                "FAIL_PREFLIGHT",
                "The Unity Zenoh router port collides with another owned endpoint.",
            )
        require_available_loopback_port(
            zenoh_router_endpoint.port,
            "Zenoh router",
        )
        owned = zenoh.create_owned_local_router_config(
            router_template=templates / "DEFAULT_RMW_ZENOH_ROUTER_CONFIG.json5",
            session_template=templates / "DEFAULT_RMW_ZENOH_SESSION_CONFIG.json5",
            output_directory=output / "zenoh",
            endpoint=zenoh_router_endpoint.endpoint,
        )
        zenoh_router_config = owned.router_config
        zenoh_session_config = owned.session_config
        zenoh_router_environment = peer.ros2env.build_ros_env(
            toolchain.ros2_root,
            rmw,
            str(config["discoveryRange"]),
            str(config["domainId"]),
            distro,
        )
        zenoh_router_environment["ZENOH_ROUTER_CONFIG_URI"] = str(
            zenoh_router_config
        )
        zenoh_router_environment["ZENOH_SESSION_CONFIG_URI"] = str(
            zenoh_session_config
        )

    actor_environment = build_ros_actor_environment(
        build_environment,
        bridge_install=bridge_install,
        peer_install=runtime_peer / "install",
        ros2_root=toolchain.ros2_root,
        distro=distro,
        rmw=rmw,
        domain_id=int(config["domainId"]),
        discovery_range=str(config["discoveryRange"]),
        topology_id=str(config["zenohTopologyId"]),
        zenoh_session_config=zenoh_session_config,
    )
    unity_base = peer.build_player_environment(
        build_environment,
        distro=distro,
        rmw=rmw,
        domain_id=int(config["domainId"]),
        interface_revision=lock.interface_revision,
        interface_digest=lock.interface_digest,
        topology_id=str(config["zenohTopologyId"]) or None,
        zenoh_session_config=zenoh_session_config,
        discovery_range=str(config["discoveryRange"]),
    )
    custom_plugin_alias = stack.enter_context(
        peer.temporary_short_windows_plugin_alias(custom_plugins)
    )
    if _paths_are_distinct(custom_plugin_alias, custom_plugins):
        subst_roots.append(custom_plugin_alias)
    unity_environment = peer.build_editor_batch_environment(
        unity_base,
        runtime_plugins,
        custom_plugin_alias,
    )
    return PreparedRosRuntime(
        peer=peer,
        toolchain=toolchain,
        lock=lock,
        ros2_root=toolchain.ros2_root,
        peer_workspace=peer_workspace,
        peer_runtime_workspace=runtime_peer,
        build_environment=build_environment,
        actor_environment=actor_environment,
        unity_environment=unity_environment,
        bridge_install=bridge_install,
        bridge_runtime_workspace=bridge_runtime_workspace,
        zenoh_router=zenoh_router,
        zenoh_router_environment=zenoh_router_environment,
        zenoh_router_config=zenoh_router_config,
        zenoh_session_config=zenoh_session_config,
        zenoh_router_endpoint=zenoh_router_endpoint,
        subst_roots=tuple(subst_roots),
    )


def _start_case_workers_serially(
    *,
    config: Mapping[str, object],
    repository: pathlib.Path,
    output: pathlib.Path,
    runtime: PreparedRosRuntime | None,
    worker_roles: Iterable[str],
    owner: OwnedProcessSet,
    streams: list[TextIO],
    desktop_barrier: pathlib.Path | None = None,
) -> None:
    """Start one worker at a time so ROS participants cannot race initialization."""

    for role in sorted(worker_roles):
        if role == "foxglove-client":
            python_executable = pathlib.Path(sys.executable)
            environment = _clean_environment(os.environ)
            if desktop_barrier is not None:
                environment[DESKTOP_CLIENT_BARRIER_ENV] = str(desktop_barrier)
            cwd = repository
        else:
            if runtime is None:
                raise AcceptanceFailure(
                    "FAIL_RUNTIME_SELECTION",
                    f"{role} requires a selected ROS runtime.",
                )
            python_executable = pathlib.Path(runtime.toolchain.python_executable)
            environment = _without_desktop_client_barrier(
                runtime.actor_environment
            )
            cwd = runtime.peer_runtime_workspace
        _launch_logged_process(
            role,
            build_worker_command(
                python_executable,
                role,
                pathlib.Path(str(config["outputRoot"])) / "run-config.json",
            ),
            cwd=cwd,
            environment=environment,
            log_path=output / f"{role}.log",
            owner=owner,
            streams=streams,
        )
        _wait_for_actor_readiness(config, (role,), owner)


def _installed_bridge_executable(runtime: PreparedRosRuntime | None) -> pathlib.Path:
    if runtime is None or runtime.bridge_install is None:
        raise AcceptanceFailure(
            "FAIL_BRIDGE",
            "The selected case has no built Bridge overlay.",
        )
    return _require_file(
        runtime.bridge_install
        / "lib"
        / "unity2foxglove_ros2_bridge"
        / "unity2foxglove_ros2_bridge.exe",
        "FAIL_BRIDGE",
        "Installed native Bridge executable",
    )


def _start_bridge_actor(
    *,
    config: Mapping[str, object],
    output: pathlib.Path,
    runtime: PreparedRosRuntime | None,
    owner: OwnedProcessSet,
    streams: list[TextIO],
) -> dict[str, object]:
    """Start one Bridge and prove its correlated health before Unity starts."""

    if owner.process("bridge") is not None:
        raise AcceptanceFailure(
            "FAIL_BRIDGE",
            "The selected case attempted to start its Bridge more than once.",
        )
    bridge_executable = _installed_bridge_executable(runtime)
    log_path = output / "bridge.log"
    bridge = _launch_logged_process(
        "bridge",
        build_bridge_command(
            bridge_executable,
            str(config["bridgeHost"]),
            int(config["bridgePort"]),
        ),
        cwd=runtime.bridge_runtime_workspace or output,
        environment=_without_desktop_client_barrier(
            runtime.actor_environment
        ),
        log_path=log_path,
        owner=owner,
        streams=streams,
    )
    health = wait_for_bridge_health(config, bridge)
    ready = {
        "state": "u2r2-health-ready",
        "sidecarName": health["sidecarName"],
        "sidecarVersion": health["sidecarVersion"],
    }
    write_actor_ready(config, "bridge", ready)
    return {"bridge-health": health}


def _start_case_actors(
    *,
    config: Mapping[str, object],
    repository: pathlib.Path,
    output: pathlib.Path,
    runtime: PreparedRosRuntime | None,
    owner: OwnedProcessSet,
    streams: list[TextIO],
    desktop_barrier: pathlib.Path | None = None,
) -> tuple[set[str], dict[str, object]]:
    """Start every required non-Unity actor and return readiness evidence."""

    contract = protocol.CASE_CONTRACTS[str(config["case"])]
    parent_ready_roles: set[str] = set()
    parent_evidence: dict[str, object] = {}

    if "zenoh-router" in contract.required_actors:
        if (
            runtime is None
            or runtime.zenoh_router is None
            or runtime.zenoh_router_environment is None
            or runtime.zenoh_router_endpoint is None
        ):
            raise AcceptanceFailure(
                "FAIL_RUNTIME_SELECTION",
                "The stream case has no prepared owned Zenoh router.",
            )
        router = _launch_logged_process(
            "zenoh-router",
            [str(runtime.zenoh_router)],
            cwd=repository,
            environment=_without_desktop_client_barrier(
                runtime.zenoh_router_environment
            ),
            log_path=output / "zenoh-router.log",
            owner=owner,
            streams=streams,
        )
        ready = wait_for_owned_zenoh_router(
            router,
            output / "zenoh-router.log",
            runtime.zenoh_router_endpoint,
        )
        ready["topologyId"] = config["zenohTopologyId"]
        write_actor_ready(config, "zenoh-router", ready)
        parent_ready_roles.add("zenoh-router")
        parent_evidence["zenoh-router"] = ready

    if "bridge" in contract.required_actors:
        parent_evidence.update(
            _start_bridge_actor(
                config=config,
                output=output,
                runtime=runtime,
                owner=owner,
                streams=streams,
            )
        )
        parent_ready_roles.add("bridge")

    worker_roles = set(contract.required_actors) - {"bridge", "zenoh-router"}
    _start_case_workers_serially(
        config=config,
        repository=repository,
        output=output,
        runtime=runtime,
        worker_roles=worker_roles,
        owner=owner,
        streams=streams,
        desktop_barrier=desktop_barrier,
    )
    all_ready = worker_roles | parent_ready_roles
    expected_ready = set(contract.required_actors)
    if all_ready != expected_ready:
        raise AcceptanceFailure(
            "FAIL_PREFLIGHT",
            "Actor readiness did not cover the exact selected case.",
        )
    return worker_roles, parent_evidence


def _wait_for_unity_exit(
    config: Mapping[str, object],
    unity,
    owner: OwnedProcessSet,
    worker_roles: Iterable[str],
) -> TerminalMarker:
    unity_log = pathlib.Path(str(config["unityLog"]))
    watchdog = protocol.ProgressWatchdog("unity-startup")
    last_progress = _progress_snapshot((unity_log,))
    watchdog.progress("Unity Batch process started")
    while unity.poll() is None:
        progress = _progress_snapshot((unity_log,))
        if progress != last_progress:
            last_progress = progress
            watchdog.progress(f"Unity log bytes={max(0, progress[0][1])}")
        marker = find_terminal_marker(
            read_log_lines(unity_log),
            str(config["case"]),
            str(config["token"]),
        )
        if marker is not None and marker.verdict == "FAIL":
            raise AcceptanceFailure(
                "FAIL_TERMINAL",
                "Unity reported a correlated case failure.",
            )
        for role in worker_roles:
            process = owner.process(role)
            if process is None or process.poll() is None:
                continue
            result_path = _actor_path(config, "resultFiles", role)
            if not result_path.is_file():
                raise AcceptanceFailure(
                    "FAIL_PROCESS_EXIT",
                    f"{role} exited before producing result evidence.",
                )
            read_actor_document(config, role, "resultFiles")
        try:
            watchdog.check()
        except protocol.ProtocolFailure as exc:
            raise AcceptanceFailure("FAIL_UNITY_STARTUP", str(exc)) from exc
        time.sleep(0.1)
    if int(unity.returncode) != 0:
        raise AcceptanceFailure(
            "FAIL_PROCESS_EXIT",
            f"Unity Batch exited with code {unity.returncode}.",
        )
    return wait_for_terminal_marker(config, 5.0)


def _write_parent_actor_results(
    config: Mapping[str, object],
    output: pathlib.Path,
    parent_evidence: Mapping[str, object],
) -> None:
    contract = protocol.CASE_CONTRACTS[str(config["case"])]
    if "bridge" in contract.required_actors:
        health = parent_evidence.get("bridge-health")
        if not isinstance(health, Mapping):
            raise AcceptanceFailure(
                "FAIL_BRIDGE",
                "Bridge health evidence is absent.",
            )
        validate_bridge_health_response(health, b"", str(config["token"]))
        bridge = parse_bridge_publisher_evidence(config, output / "bridge.log")
        bridge.update(
            {
                "healthReady": True,
                "healthProcess": "bridge",
                "publisherProcess": "bridge",
                "sameProcessHealthAndPublisher": True,
                "sidecarVersion": health["sidecarVersion"],
                "healthTokenSha256": protocol.token_sha256(str(config["token"])),
            }
        )
        write_actor_result(config, "bridge", verdict="PASS", evidence=bridge)
    if "zenoh-router" in contract.required_actors:
        evidence = parent_evidence.get("zenoh-router")
        if not isinstance(evidence, Mapping):
            raise AcceptanceFailure(
                "FAIL_TERMINAL",
                "Owned Zenoh router evidence is absent.",
            )
        write_actor_result(
            config,
            "zenoh-router",
            verdict="PASS",
            evidence={
                **dict(evidence),
                "sessionConfigOwned": True,
                "routerConfigOwned": True,
            },
        )


def _required_section(values: Mapping[str, object]) -> dict[str, object]:
    return {"applicability": "required", **dict(values)}


def _not_applicable_section(rule: protocol.ApplicabilityRule) -> dict[str, object]:
    if rule.required or not rule.reason:
        raise ValueError("Only an approved N/A rule can create an N/A section.")
    return {"applicability": "not_applicable", "reason": rule.reason}


def _actor_evidence(
    results: Mapping[str, Mapping[str, object]],
    role: str,
) -> Mapping[str, object]:
    result = results.get(role)
    evidence = result.get("evidence") if isinstance(result, Mapping) else None
    if (
        not isinstance(result, Mapping)
        or result.get("verdict") != "PASS"
        or not isinstance(evidence, Mapping)
    ):
        raise AcceptanceFailure(
            "FAIL_TERMINAL",
            f"{role} has no usable PASS evidence object.",
        )
    return evidence


def build_pass_summary(
    *,
    config: Mapping[str, object],
    terminal: TerminalMarker,
    results: Mapping[str, Mapping[str, object]],
    process_exit_codes: Mapping[str, int],
    unity_version: str,
    cleanup: Mapping[str, bool],
    owner_stopped_roles: Iterable[str] = (),
) -> dict[str, object]:
    """Compose one strict case-specific PASS summary from independent evidence."""

    case = str(config["case"])
    token = str(config["token"])
    contract = protocol.CASE_CONTRACTS[case]
    if set(results) != set(contract.required_actors):
        raise AcceptanceFailure(
            "FAIL_TERMINAL",
            "Actor results do not cover the exact selected case.",
        )
    for role in contract.required_actors:
        _actor_evidence(results, role)
    expected_qos = _expected_qos_by_topic(config)
    fox = (
        _actor_evidence(results, "foxglove-client")
        if "foxglove-client" in results
        else {}
    )
    graph = (
        _actor_evidence(results, "graph-observer")
        if "graph-observer" in results
        else {}
    )
    peer = (
        _actor_evidence(results, "ros2-peer")
        if "ros2-peer" in results
        else {}
    )
    bridge = (
        _actor_evidence(results, "bridge")
        if "bridge" in results
        else {}
    )

    if case == "foxglove-profile":
        source = "Foxglove"
        targets = ["Foxglove"]
        publish_encoding = "protobuf,json"
        subscribe_encoding = "protobuf,json"
        target_values = {
            "states": {"foxglove": "Ready"},
            "diagnosticCounts": {"warning": 0, "error": 0},
            "healthyDelivery": bool(fox.get("deliveryObserved")),
            "statusEvidence": {},
        }
        origin_values = {
            "remoteApplied": bool(fox.get("remoteApplied")),
            "sameOriginDropped": bool(fox.get("sameOriginDropped")),
            "laterLocalPublished": bool(fox.get("laterLocalPublished")),
        }
    elif case == "multi-target":
        source = "Ros2Native"
        targets = ["Foxglove", "Ros2Native", "Ros2Bridge"]
        publish_encoding = "protobuf"
        subscribe_encoding = "protobuf"
        target_values = {
            "states": {
                "foxglove": "Ready",
                "ros2Native": "Ready",
                "ros2Bridge": "Ready",
            },
            "diagnosticCounts": {"warning": 0, "error": 0},
            "healthyDelivery": (
                bool(fox.get("deliveryObserved"))
                and int(peer.get("distinctFanoutPublishers", 0)) >= 2
            ),
            "statusEvidence": {},
        }
        origin_values = {
            "remoteApplied": bool(peer.get("remoteApplied")),
            "sameOriginDropped": bool(peer.get("sameOriginDropped")),
            "laterLocalPublished": bool(peer.get("laterLocalPublished")),
        }
    elif case == "degraded-target":
        bridge_diagnostics = _marker_int(terminal, "bridgeDiagnostics")
        aggregate_status = terminal.fields.get("status")
        succeeded_targets = terminal.fields.get("succeeded")
        failed_targets = terminal.fields.get("failed")
        foxglove_state = terminal.fields.get("foxgloveState")
        bridge_state = terminal.fields.get("ros2BridgeState")
        if (
            aggregate_status != "Degraded"
            or succeeded_targets != "Foxglove"
            or failed_targets != "Ros2Bridge"
            or foxglove_state != "Ready"
            or bridge_state != "Unavailable"
            or bridge_diagnostics != 1
        ):
            raise AcceptanceFailure(
                "FAIL_FANOUT",
                "Unity did not report the exact degraded Bridge status transition.",
            )
        source = "None"
        targets = ["Foxglove", "Ros2Bridge"]
        publish_encoding = "protobuf"
        subscribe_encoding = "not_applicable"
        target_values = {
            "states": {
                "foxglove": foxglove_state,
                "ros2Bridge": bridge_state,
            },
            "diagnosticCounts": {"bridge": bridge_diagnostics, "error": 0},
            "healthyDelivery": (
                bool(fox.get("deliveryObserved"))
                and bool(graph.get("noFallbackPublisher"))
            ),
            "statusEvidence": {
                "aggregate": aggregate_status,
                "succeeded": succeeded_targets,
                "failed": failed_targets,
                "bridgeDiagnostics": bridge_diagnostics,
            },
        }
        origin_values = {}
    elif case == "qos-contract":
        source = "None"
        targets = ["Ros2Native", "Ros2Bridge"]
        publish_encoding = "protobuf"
        subscribe_encoding = "not_applicable"
        target_values = {
            "states": {topic: "Ready" for topic in config["topics"]},
            "diagnosticCounts": {"warning": 0, "error": 0},
            "healthyDelivery": all(
                len(gids) >= 2
                for gids in peer.get("deliveryByTopic", {}).values()
            ),
            "statusEvidence": {},
        }
        origin_values = {}
    elif case == "stream-640hz":
        source = "Ros2Native"
        targets = ["Ros2Native"]
        publish_encoding = "protobuf"
        subscribe_encoding = "protobuf"
        target_values = {
            "states": {"ros2Native": "Ready"},
            "diagnosticCounts": {"warning": 0, "error": 0},
            "healthyDelivery": bool(graph.get("endpointsObserved")),
            "statusEvidence": {},
        }
        origin_values = {
            "remoteApplied": bool(peer.get("remoteApplied")),
            "sameOriginDropped": bool(peer.get("sameOriginDropped")),
            "laterLocalPublished": bool(peer.get("laterLocalPublished")),
        }
    else:
        raise AcceptanceFailure("FAIL_TERMINAL", "Unknown case summary mapping.")

    transport_observed: dict[str, object] = {
        "graph": dict(graph.get("transportObservedQos", {})),
    }
    if bridge:
        if bridge.get("healthReady") is not True:
            raise AcceptanceFailure(
                "FAIL_BRIDGE",
                "Bridge result has no current health readiness evidence.",
            )
        publishers = bridge.get("publishers")
        if not isinstance(publishers, Mapping):
            raise AcceptanceFailure(
                "FAIL_BRIDGE",
                "Bridge result has no parsed publisher evidence.",
            )
        transport_observed["bridge"] = dict(publishers)

    sample_publisher_gids: dict[str, object] = {}
    if case == "multi-target":
        for suffix, field in (
            ("multi-local-1", "local1PublisherGids"),
            ("multi-local-3", "local3PublisherGids"),
        ):
            attribution_field = (
                "local1Attribution"
                if suffix == "multi-local-1"
                else "local3Attribution"
            )
            sample_publisher_gids[suffix] = {
                "sampleSha256": protocol.token_sha256(token + "-" + suffix),
                "publisherGids": list(peer.get(field, [])),
                "attribution": str(peer.get(attribution_field, "")),
            }
    elif case == "qos-contract":
        suffixes = (
            "qos-system-default",
            "qos-keep-all",
            "qos-keep-last-depth",
        )
        delivery = peer.get("deliveryByTopic", {})
        if not isinstance(delivery, Mapping):
            raise AcceptanceFailure(
                "FAIL_QOS",
                "QoS peer delivery evidence is malformed.",
            )
        for topic, suffix in zip(config["topics"], suffixes):
            sample_publisher_gids[str(topic)] = {
                "sampleSha256": protocol.token_sha256(token + "-" + suffix),
                "publisherGids": list(delivery.get(topic, [])),
                "attribution": str(
                    peer.get("deliveryAttributionByTopic", {}).get(topic, "")
                ),
            }
    elif case == "stream-640hz":
        sample_publisher_gids["origin-local"] = {
            "sampleSha256": protocol.token_sha256(token + "-origin-local"),
            "publisherGids": list(peer.get("localOriginPublisherGids", [])),
            "attribution": str(peer.get("localOriginAttribution", "")),
        }

    section_values: dict[str, Mapping[str, object]] = {
        "foxglove": {
            "deliveryObserved": bool(fox.get("deliveryObserved")),
            "channelEncodings": list(fox.get("channelEncodings", [])),
            "sampleToken": str(fox.get("sampleToken", "")),
            "sampleStages": list(fox.get("sampleStages", [])),
            "timestamp": float(fox.get("timestamp", 0.0)),
        },
        "rosGraph": {
            "endpointsObserved": bool(graph.get("endpointsObserved")),
            "nodeIdentities": list(graph.get("nodeIdentities", [])),
            "publisherGids": list(graph.get("publisherGids", [])),
            "publishersByTopic": dict(graph.get("publishersByTopic", {})),
            "samplePublisherGids": sample_publisher_gids,
            "negativeObservationSeconds": float(
                graph.get("negativeObservationSeconds", 0)
            ),
        },
        "qos": {
            "requested": expected_qos,
            "transportObserved": transport_observed,
            "matches": bool(graph.get("qosMatches")),
        },
        "targets": target_values,
        "origin": origin_values,
        "stream": {},
    }
    if case == "stream-640hz":
        section_values["stream"] = _validated_stream_evidence(terminal, peer)

    sections: dict[str, object] = {}
    for name, rule in contract.applicability.items():
        sections[name] = (
            _required_section(section_values[name])
            if rule.required
            else _not_applicable_section(rule)
        )

    owner_stopped = frozenset(owner_stopped_roles)
    process_entries = [
        {
            "role": role,
            "started": True,
            "exitCode": int(process_exit_codes[role]),
            "termination": (
                "owner_requested" if role in owner_stopped else "self"
            ),
        }
        for role in sorted(contract.required_actors | {"unity"})
    ]
    process_entries.extend(
        {"role": role, "started": False, "reason": reason}
        for role, reason in sorted(contract.deliberately_absent_actors.items())
    )
    summary = {
        "summarySchemaVersion": protocol.SUMMARY_SCHEMA_VERSION,
        "identity": {
            "runId": config["runId"],
            "case": case,
            "tokenSha256": protocol.token_sha256(token),
            "unityVersion": unity_version,
            "interfaceIdentity": config["interfaceType"],
            "interfaceDigest": config["interfaceDigest"],
        },
        "profile": {
            "profile": config["profile"],
            "runtime": config["rosDistro"],
            "rmw": config["rmw"],
            "source": source,
            "targets": targets,
            "publishEncoding": publish_encoding,
            "subscribeEncoding": subscribe_encoding,
            "requestedQos": expected_qos,
        },
        **sections,
        "processes": process_entries,
        "cleanup": dict(cleanup),
        "verdict": "PASS",
    }
    protocol.validate_summary(
        summary,
        expected_case=case,
        expected_token=token,
    )
    return summary


def _wait_for_clean_worker_exits(
    owner: OwnedProcessSet,
    roles: Iterable[str],
    timeout_seconds: float = 30.0,
) -> None:
    deadline = time.monotonic() + timeout_seconds
    for role in roles:
        process = owner.process(role)
        if process is None:
            raise AcceptanceFailure(
                "FAIL_PROCESS_EXIT",
                f"{role} was not registered with the process owner.",
            )
        remaining = deadline - time.monotonic()
        if remaining <= 0:
            raise AcceptanceFailure(
                "FAIL_PROCESS_EXIT",
                "Worker exit window expired.",
            )
        try:
            exit_code = int(process.wait(timeout=remaining))
        except subprocess.TimeoutExpired as exc:
            raise AcceptanceFailure(
                "FAIL_PROCESS_EXIT",
                f"{role} did not exit after terminal evidence.",
            ) from exc
        if exit_code != 0:
            raise AcceptanceFailure(
                "FAIL_PROCESS_EXIT",
                f"{role} exited with code {exit_code}.",
            )


def _cleanup_evidence(
    output: pathlib.Path,
    owner: OwnedProcessSet,
    subst_roots: Iterable[pathlib.Path],
) -> dict[str, bool]:
    return {
        "processes": owner.all_stopped(),
        "files": not any(output.rglob("*.tmp")),
        "junctions": True,
        "subst": all(not pathlib.Path(root).exists() for root in subst_roots),
    }


def _write_failure_record(
    output: pathlib.Path | None,
    repository: pathlib.Path,
    *,
    case: str | None,
    run_id: str | None,
    failure: AcceptanceFailure,
) -> None:
    if output is None:
        return
    protocol.write_json_atomic(
        output / "failure.json",
        {
            "phase": "184-G",
            "runId": run_id or "unallocated",
            "case": case or "unselected",
            "verdict": failure.code,
            "diagnostic": str(failure),
        },
        repo_root=repository,
    )


def default_unity_editor_log_path() -> pathlib.Path:
    """Return the interactive Editor log without relying on a shell profile."""

    local_app_data = os.environ.get("LOCALAPPDATA")
    if local_app_data:
        return pathlib.Path(local_app_data) / "Unity" / "Editor" / "Editor.log"
    return pathlib.Path.home() / "AppData" / "Local" / "Unity" / "Editor" / "Editor.log"


class EditorLogMirror:
    """Mirror only post-capture Editor output plus current-token rescue markers."""

    _MAX_SOURCE_BYTES = 64 * 1024 * 1024
    _RESCUE_SCAN_INTERVAL_SECONDS = 1.0

    def __init__(
        self,
        source: pathlib.Path,
        destination: pathlib.Path,
        token: str,
    ) -> None:
        self._source = pathlib.Path(source)
        self._destination = pathlib.Path(destination)
        self._token = token
        self._identity: tuple[int, int] | None = None
        self._offset = 0
        self._seen_token_lines: set[str] = set()
        self._next_rescue_scan = 0.0

    @staticmethod
    def _stat_identity(stat: os.stat_result) -> tuple[int, int]:
        return int(stat.st_dev), int(stat.st_ino)

    def capture(self) -> None:
        """Capture the current file identity/EOF and reset the owned mirror."""

        self._destination.parent.mkdir(parents=True, exist_ok=True)
        self._destination.write_text("", encoding="utf-8")
        self._next_rescue_scan = 0.0
        try:
            stat = self._source.stat()
        except FileNotFoundError:
            self._identity = None
            self._offset = 0
            return
        self._identity = self._stat_identity(stat)
        self._offset = int(stat.st_size)

    def _append(self, text: str) -> None:
        if not text:
            return
        with self._destination.open("a", encoding="utf-8", newline="") as stream:
            stream.write(text)
            stream.flush()

    def poll(self) -> None:
        """Copy fresh bytes, resetting safely after truncation or replacement."""

        try:
            stat = self._source.stat()
        except FileNotFoundError:
            return
        identity = self._stat_identity(stat)
        if (
            self._identity is None
            or identity != self._identity
            or int(stat.st_size) < self._offset
        ):
            self._identity = identity
            self._offset = 0

        try:
            with self._source.open("rb") as stream:
                stream.seek(self._offset)
                appended = stream.read()
                self._offset = stream.tell()
        except OSError as exc:
            raise AcceptanceFailure(
                "FAIL_TERMINAL",
                "The interactive Unity Editor log could not be mirrored.",
            ) from exc

        text = appended.decode("utf-8", errors="replace")
        self._append(text)
        token_field = "token=" + self._token
        for line in text.splitlines():
            if token_field in line:
                self._seen_token_lines.add(line)

        # Unity can reuse Editor.log storage below an apparent EOF. A unique
        # current-run token makes a bounded full-file marker rescue unambiguous.
        if stat.st_size > self._MAX_SOURCE_BYTES:
            raise AcceptanceFailure(
                "FAIL_TERMINAL",
                "The interactive Unity Editor log exceeded the acceptance bound.",
            )
        now = time.monotonic()
        if now < self._next_rescue_scan:
            return
        self._next_rescue_scan = now + self._RESCUE_SCAN_INTERVAL_SECONDS
        try:
            current = self._source.read_text(
                encoding="utf-8",
                errors="replace",
            )
        except OSError as exc:
            raise AcceptanceFailure(
                "FAIL_TERMINAL",
                "The interactive Unity Editor log could not be rescanned.",
            ) from exc
        rescued: list[str] = []
        for line in current.splitlines():
            if token_field not in line or line in self._seen_token_lines:
                continue
            self._seen_token_lines.add(line)
            rescued.append(line)
        if rescued:
            self._append("\n".join(rescued) + "\n")


def _process_creation_unix_seconds(pid: int) -> float | None:
    """Read one process creation time without mutating or opening broad state."""

    if pid <= 0:
        return None
    if os.name == "nt":
        class FileTime(ctypes.Structure):
            _fields_ = [
                ("low", ctypes.c_uint32),
                ("high", ctypes.c_uint32),
            ]

        kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
        open_process = kernel32.OpenProcess
        open_process.argtypes = [ctypes.c_uint32, ctypes.c_int, ctypes.c_uint32]
        open_process.restype = ctypes.c_void_p
        get_times = kernel32.GetProcessTimes
        get_times.argtypes = [
            ctypes.c_void_p,
            ctypes.POINTER(FileTime),
            ctypes.POINTER(FileTime),
            ctypes.POINTER(FileTime),
            ctypes.POINTER(FileTime),
        ]
        get_times.restype = ctypes.c_int
        close_handle = kernel32.CloseHandle
        close_handle.argtypes = [ctypes.c_void_p]
        close_handle.restype = ctypes.c_int
        handle = open_process(0x1000, 0, int(pid))
        if not handle:
            return None
        creation = FileTime()
        exit_time = FileTime()
        kernel_time = FileTime()
        user_time = FileTime()
        try:
            if not get_times(
                handle,
                ctypes.byref(creation),
                ctypes.byref(exit_time),
                ctypes.byref(kernel_time),
                ctypes.byref(user_time),
            ):
                return None
        finally:
            close_handle(handle)
        ticks = (int(creation.high) << 32) | int(creation.low)
        return ticks / 10_000_000.0 - 11_644_473_600.0
    if pid == os.getpid():
        return _PROCESS_IMPORTED_UNIX_SECONDS
    try:
        import psutil

        return float(psutil.Process(pid).create_time())
    except (ImportError, OSError, ValueError):
        return None


def _write_manual_pointer(
    pointer: pathlib.Path,
    run_config: pathlib.Path,
    token: str,
    *,
    helper_pid: int,
    helper_created: float,
    expires_utc: dt.datetime,
) -> None:
    """Publish the one bounded private pointer consumed by interactive Unity."""

    expiry = expires_utc.astimezone(dt.timezone.utc).isoformat().replace("+00:00", "Z")
    write_private_json_atomic(
        pointer,
        {
            "runConfig": str(pathlib.Path(run_config).resolve()),
            "token": token,
            "helperPid": int(helper_pid),
            "helperCreationUnixSeconds": float(helper_created),
            "expiresUtc": expiry,
        },
    )


def _remove_manual_pointer_if_owned(
    pointer: pathlib.Path,
    token: str,
    helper_pid: int,
) -> bool:
    """Remove only the exact pointer written by this helper identity."""

    try:
        value = json.loads(pathlib.Path(pointer).read_text(encoding="utf-8"))
    except (FileNotFoundError, UnicodeError, json.JSONDecodeError, OSError):
        return False
    if (
        not isinstance(value, Mapping)
        or value.get("token") != token
        or value.get("helperPid") != int(helper_pid)
    ):
        return False
    try:
        pathlib.Path(pointer).unlink()
        return True
    except FileNotFoundError:
        return True
    except OSError:
        return False


def _recover_abandoned_manual_pointer(pointer: pathlib.Path) -> None:
    """Clear only a malformed or demonstrably dead prior helper pointer."""

    path = pathlib.Path(pointer)
    if not path.is_file():
        return
    try:
        if path.stat().st_size <= 0 or path.stat().st_size > MAX_CONFIG_BYTES:
            raise ValueError("pointer size")
        value = json.loads(path.read_text(encoding="utf-8"))
        pid = int(value["helperPid"])
        created = float(value["helperCreationUnixSeconds"])
    except (OSError, UnicodeError, json.JSONDecodeError, KeyError, TypeError, ValueError):
        with contextlib.suppress(FileNotFoundError):
            path.unlink()
        return
    actual = _process_creation_unix_seconds(pid)
    if actual is not None and abs(actual - created) <= 2.0:
        raise AcceptanceFailure(
            "FAIL_PREFLIGHT",
            "Another live Phase184 manual helper already owns manual-active.json.",
        )
    try:
        path.unlink()
    except OSError as exc:
        raise AcceptanceFailure(
            "FAIL_CLEANUP",
            "An abandoned Phase184 manual pointer could not be removed.",
        ) from exc


def _manual_exit_seen(config: Mapping[str, object]) -> bool:
    case = str(config["case"])
    token = str(config["token"])
    for line in read_log_lines(pathlib.Path(str(config["unityLog"]))):
        if "PHASE184G_MANUAL_PLAY_EXITED" not in line:
            continue
        fields = _parse_marker_fields(line)
        if fields.get("case") == case and fields.get("token") == token:
            return True
    return False


def _wait_for_manual_session(
    config: Mapping[str, object],
    mirror: EditorLogMirror,
    owner: OwnedProcessSet,
    worker_roles: Iterable[str],
) -> TerminalMarker:
    """Wait for current-token Play entry, route proof, and user-owned Play exit."""

    entry_deadline = time.monotonic() + MANUAL_ENTRY_TIMEOUT_SECONDS
    review_deadline: float | None = None
    terminal: TerminalMarker | None = None
    context_ready = False
    announced_terminal = False
    while True:
        mirror.poll()
        lines = read_log_lines(pathlib.Path(str(config["unityLog"])))
        case = str(config["case"])
        token = str(config["token"])
        if not context_ready:
            for line in lines:
                if "PHASE184G_CONTEXT_READY" not in line:
                    continue
                fields = _parse_marker_fields(line)
                if fields.get("case") == case and fields.get("token") == token:
                    context_ready = True
                    review_deadline = time.monotonic() + MANUAL_REVIEW_TIMEOUT_SECONDS
                    break
        observed_terminal = find_terminal_marker(lines, case, token)
        if observed_terminal is not None:
            terminal = observed_terminal
        if terminal is not None and terminal.verdict == "FAIL":
            raise AcceptanceFailure(
                "FAIL_TERMINAL",
                "The interactive Unity route reported a correlated failure.",
            )
        exited = _manual_exit_seen(config)
        if exited and terminal is None:
            raise AcceptanceFailure(
                "FAIL_MANUAL_STOPPED_EARLY",
                "Play Mode exited before the correlated route proof completed.",
            )
        if terminal is not None and not announced_terminal:
            announced_terminal = True
            print(
                "[phase184] Automated route evidence is complete. "
                "Finish the visible checklist, then exit Play Mode.",
                flush=True,
            )
        if terminal is not None and exited:
            return terminal

        for role in worker_roles:
            process = owner.process(role)
            if process is None or process.poll() is None:
                continue
            result_path = _actor_path(config, "resultFiles", role)
            if not result_path.is_file():
                raise AcceptanceFailure(
                    "FAIL_PROCESS_EXIT",
                    f"{role} exited before current manual evidence.",
                )

        now = time.monotonic()
        if not context_ready and now >= entry_deadline:
            raise AcceptanceFailure(
                "FAIL_UNITY_STARTUP",
                "The user-owned Editor did not enter the current Play session within 900 seconds.",
            )
        if context_ready and review_deadline is not None and now >= review_deadline:
            raise AcceptanceFailure(
                "FAIL_TERMINAL",
                "The current manual Play session did not finish within its review window.",
            )
        time.sleep(0.1)


@dataclass(frozen=True)
class PreparedParentRun:
    """Immutable allocation shared by Batch and manual parent modes."""

    repository: pathlib.Path
    editor: pathlib.Path
    run_id: str
    token: str
    output: pathlib.Path
    config: Mapping[str, object]
    config_path: pathlib.Path


def _prepare_parent_run(
    args: argparse.Namespace,
    execution_mode: str,
) -> PreparedParentRun:
    """Allocate one run and persist a failure record after its root exists."""

    repository = repository_root()
    editor = _require_file(
        args.unity_editor,
        "FAIL_UNITY_STARTUP",
        "Explicit Unity Editor executable",
    )
    run_id, token = _new_run_identity(args.run_id)
    output = _prepare_run_directory(repository, run_id)
    try:
        identity = load_static_interface_identity(repository)
        domain_id = choose_domain_id(args.domain_id)
        foxglove_port = (
            int(args.foxglove_port)
            if args.foxglove_port is not None
            else choose_owned_loopback_port()
        )
        bridge_port = (
            int(args.bridge_port)
            if args.bridge_port is not None
            else choose_owned_loopback_port((foxglove_port,))
        )
        if foxglove_port == bridge_port:
            raise AcceptanceFailure(
                "FAIL_PREFLIGHT",
                "Foxglove and Bridge ports must be distinct.",
            )
        require_available_loopback_port(foxglove_port, "Foxglove")
        require_available_loopback_port(bridge_port, "Bridge")
        profile = str(args.profile)
        peer_workspace = (
            repository / "build" / "phase181" / profile / "peer-workspace"
        )
        config = make_run_config(
            repository=repository,
            run_id=run_id,
            token=token,
            case=str(args.case),
            profile=profile,
            output_root=output,
            domain_id=domain_id,
            foxglove_port=foxglove_port,
            bridge_port=bridge_port,
            phase181_workspace=peer_workspace,
            interface_package=identity.package,
            interface_type=identity.envelope_type,
            interface_digest=identity.digest,
            execution_mode=execution_mode,
        )
        config_path = output / "run-config.json"
        write_private_json_atomic(config_path, config)
        return PreparedParentRun(
            repository=repository,
            editor=editor,
            run_id=run_id,
            token=token,
            output=output,
            config=config,
            config_path=config_path,
        )
    except AcceptanceFailure as exc:
        _write_failure_record(
            output,
            repository,
            case=str(args.case),
            run_id=run_id,
            failure=exc,
        )
        raise
    except Exception as exc:
        failure = AcceptanceFailure(
            "FAIL_PREFLIGHT",
            "Unexpected run allocation failure: " + type(exc).__name__,
        )
        _write_failure_record(
            output,
            repository,
            case=str(args.case),
            run_id=run_id,
            failure=failure,
        )
        raise failure from exc


def run_manual_parent(args: argparse.Namespace) -> int:
    """Own external dependencies while leaving interactive Unity entirely user-owned."""

    if str(args.case) not in {"multi-target", "stream-640hz"}:
        raise AcceptanceFailure(
            "FAIL_PREFLIGHT",
            "Manual Editor mode is limited to the two approved Phase184-G suites.",
        )
    prepared = _prepare_parent_run(args, "manual")
    repository = prepared.repository
    editor = prepared.editor
    run_id = prepared.run_id
    token = prepared.token
    output = prepared.output
    config = prepared.config
    config_path = prepared.config_path
    pointer = repository / "build" / "phase184" / "acceptance" / "manual-active.json"

    stack = contextlib.ExitStack()
    owner: OwnedProcessSet | None = None
    streams: list[TextIO] = []
    runtime: PreparedRosRuntime | None = None
    terminal: TerminalMarker | None = None
    results: dict[str, dict[str, object]] = {}
    process_codes: dict[str, int] = {}
    owner_stopped_roles: frozenset[str] = frozenset()
    cleanup = {"processes": False, "files": False, "junctions": False, "subst": False}
    pointer_written = False
    failure: AcceptanceFailure | None = None
    try:
        _recover_abandoned_manual_pointer(pointer)
        job = WindowsKillOnCloseJob()
        owner = OwnedProcessSet(job)
        _ensure_acceptance_scene(editor, repository, output, job)
        runtime = _prepare_ros_runtime(
            config=config,
            editor=editor,
            repository=repository,
            output=output,
            stack=stack,
            job=job,
        )
        mirror = EditorLogMirror(
            default_unity_editor_log_path(),
            pathlib.Path(str(config["unityLog"])),
            token,
        )
        mirror.capture()
        worker_roles, parent_evidence = _start_case_actors(
            config=config,
            repository=repository,
            output=output,
            runtime=runtime,
            owner=owner,
            streams=streams,
        )
        helper_created = _process_creation_unix_seconds(os.getpid())
        if helper_created is None:
            raise AcceptanceFailure(
                "FAIL_PREFLIGHT",
                "The manual helper process creation time is unavailable.",
            )
        _write_manual_pointer(
            pointer,
            config_path,
            token,
            helper_pid=os.getpid(),
            helper_created=helper_created,
            expires_utc=dt.datetime.now(dt.timezone.utc) + dt.timedelta(hours=1),
        )
        pointer_written = True
        print(
            "[phase184] External endpoints are ready. Open the Phase184 acceptance "
            "scene, select the Manager, and Enter Play Mode now.",
            flush=True,
        )
        terminal = _wait_for_manual_session(
            config,
            mirror,
            owner,
            worker_roles,
        )
        _write_parent_actor_results(config, output, parent_evidence)
        results = _wait_for_actor_results(
            config,
            protocol.CASE_CONTRACTS[str(config["case"])].required_actors,
            owner,
        )
        _wait_for_clean_worker_exits(owner, worker_roles)
    except AcceptanceFailure as exc:
        failure = exc
    except Exception as exc:
        failure = AcceptanceFailure(
            "FAIL_PREFLIGHT",
            "Unexpected manual parent failure: " + type(exc).__name__,
        )
    finally:
        if pointer_written and not _remove_manual_pointer_if_owned(
            pointer,
            token,
            os.getpid(),
        ):
            failure = failure or AcceptanceFailure(
                "FAIL_CLEANUP",
                "The owned manual-active pointer could not be removed.",
            )
        if owner is not None:
            try:
                owner.close()
                process_codes = owner.exit_codes()
                owner_stopped_roles = owner.owner_stopped_roles()
            except Exception:
                failure = failure or AcceptanceFailure(
                    "FAIL_CLEANUP",
                    "The manual process owner could not close every child.",
                )
        for stream in streams:
            with contextlib.suppress(Exception):
                stream.close()
        subst_roots = runtime.subst_roots if runtime is not None else ()
        try:
            stack.close()
        except Exception:
            failure = failure or AcceptanceFailure(
                "FAIL_CLEANUP",
                "The manual short-path cleanup stack failed.",
            )
        if owner is not None:
            try:
                cleanup = _cleanup_evidence(output, owner, subst_roots)
            except Exception:
                failure = failure or AcceptanceFailure(
                    "FAIL_CLEANUP",
                    "The manual cleanup evidence could not be collected.",
                )

    if failure is not None:
        _write_failure_record(
            output,
            repository,
            case=str(args.case),
            run_id=run_id,
            failure=failure,
        )
        raise failure
    if terminal is None or owner is None:
        raise AcceptanceFailure(
            "FAIL_TERMINAL",
            "The manual helper reached no current terminal evidence.",
        )
    required = protocol.CASE_CONTRACTS[str(config["case"])].required_actors
    missing = set(required) - set(process_codes)
    unacceptable = {
        role: process_codes[role]
        for role in required & set(process_codes)
        if not process_exit_is_acceptable(
            role,
            process_codes[role],
            owner_requested=role in owner_stopped_roles,
        )
    }
    if missing or unacceptable or not all(cleanup.values()):
        failure = AcceptanceFailure(
            "FAIL_CLEANUP",
            "Manual helper cleanup was incomplete; "
            f"missing={sorted(missing)}, unacceptable={unacceptable}.",
        )
        _write_failure_record(
            output,
            repository,
            case=str(args.case),
            run_id=run_id,
            failure=failure,
        )
        raise failure

    protocol.write_json_atomic(
        output / "manual-evidence.json",
        {
            "schemaVersion": 1,
            "evidenceType": "MANUAL_EVIDENCE",
            "runId": run_id,
            "case": config["case"],
            "profile": config["profile"],
            "tokenSha256": protocol.token_sha256(token),
            "routeTerminal": terminal.verdict,
            "actorEvidenceComplete": set(results) == set(required),
            "processExitCodes": process_codes,
            "processTerminations": {
                role: (
                    "owner_requested"
                    if role in owner_stopped_roles
                    else "self"
                )
                for role in sorted(required)
            },
            "cleanup": cleanup,
            "status": "USER_CONFIRMATION_REQUIRED",
        },
        repo_root=repository,
    )
    print(
        f"PHASE184G_MANUAL_ROUTE_READY case={config['case']} "
        f"evidence={output / 'manual-evidence.json'}",
        flush=True,
    )
    return 0


def run_batch_parent(args: argparse.Namespace) -> int:
    prepared = _prepare_parent_run(args, "batch")
    repository = prepared.repository
    editor = prepared.editor
    run_id = prepared.run_id
    output = prepared.output
    config = prepared.config
    config_path = prepared.config_path
    desktop_barrier = (
        desktop_live_protocol.resolve_desktop_client_barrier_path(output)
        if args.wait_for_desktop_client
        else None
    )

    stack = contextlib.ExitStack()
    owner: OwnedProcessSet | None = None
    streams: list[TextIO] = []
    runtime: PreparedRosRuntime | None = None
    terminal: TerminalMarker | None = None
    results: dict[str, dict[str, object]] = {}
    process_codes: dict[str, int] = {}
    owner_stopped_roles: frozenset[str] = frozenset()
    cleanup: dict[str, bool] = {
        "processes": False,
        "files": False,
        "junctions": False,
        "subst": False,
    }
    failure: AcceptanceFailure | None = None
    try:
        job = WindowsKillOnCloseJob()
        owner = OwnedProcessSet(job)
        _ensure_acceptance_scene(editor, repository, output, job)
        if str(config["rosDistro"]) != "core":
            runtime = _prepare_ros_runtime(
                config=config,
                editor=editor,
                repository=repository,
                output=output,
                stack=stack,
                job=job,
            )
        worker_roles, parent_evidence = _start_case_actors(
            config=config,
            repository=repository,
            output=output,
            runtime=runtime,
            owner=owner,
            streams=streams,
            desktop_barrier=desktop_barrier,
        )
        unity_environment = (
            _without_desktop_client_barrier(runtime.unity_environment)
            if runtime is not None
            else _clean_environment(os.environ)
        )
        unity = _launch_logged_process(
            "unity",
            build_unity_batch_command(
                editor,
                repository / "Unity2Foxglove",
                config_path,
                pathlib.Path(str(config["unityLog"])),
            ),
            cwd=repository,
            environment=unity_environment,
            log_path=output / "unity-process.log",
            owner=owner,
            streams=streams,
        )
        terminal = _wait_for_unity_exit(config, unity, owner, worker_roles)
        _write_parent_actor_results(config, output, parent_evidence)
        results = _wait_for_actor_results(
            config,
            protocol.CASE_CONTRACTS[str(config["case"])].required_actors,
            owner,
        )
        _wait_for_clean_worker_exits(owner, worker_roles)
    except AcceptanceFailure as exc:
        failure = exc
    except Exception as exc:
        failure = AcceptanceFailure(
            "FAIL_PREFLIGHT",
            "Unexpected parent failure: " + type(exc).__name__,
        )
    finally:
        if owner is not None:
            try:
                owner.close()
                process_codes = owner.exit_codes()
                owner_stopped_roles = owner.owner_stopped_roles()
            except Exception:
                failure = failure or AcceptanceFailure(
                    "FAIL_CLEANUP",
                    "The Batch process owner could not close every child.",
                )
        for stream in streams:
            with contextlib.suppress(Exception):
                stream.close()
        subst_roots = runtime.subst_roots if runtime is not None else ()
        try:
            stack.close()
        except Exception:
            failure = failure or AcceptanceFailure(
                "FAIL_CLEANUP",
                "The Batch short-path cleanup stack failed.",
            )
        if owner is not None:
            try:
                cleanup = _cleanup_evidence(output, owner, subst_roots)
            except Exception:
                failure = failure or AcceptanceFailure(
                    "FAIL_CLEANUP",
                    "The Batch cleanup evidence could not be collected.",
                )

    if failure is not None:
        _write_failure_record(
            output,
            repository,
            case=str(args.case),
            run_id=run_id,
            failure=failure,
        )
        raise failure
    if terminal is None or owner is None:
        raise AcceptanceFailure(
            "FAIL_TERMINAL",
            "The Batch parent reached no terminal evidence.",
        )
    required_processes = (
        protocol.CASE_CONTRACTS[str(config["case"])].required_actors
        | {"unity"}
    )
    missing_codes = set(required_processes) - set(process_codes)
    unacceptable = {
        role: process_codes[role]
        for role in required_processes & set(process_codes)
        if not process_exit_is_acceptable(
            role,
            process_codes[role],
            owner_requested=role in owner_stopped_roles,
        )
    }
    if missing_codes or unacceptable:
        failure = AcceptanceFailure(
            "FAIL_PROCESS_EXIT",
            "Process exits are incomplete; "
            f"missing={sorted(missing_codes)}, unacceptable={unacceptable}.",
        )
        _write_failure_record(
            output,
            repository,
            case=str(args.case),
            run_id=run_id,
            failure=failure,
        )
        raise failure
    if not all(cleanup.values()):
        failure = AcceptanceFailure(
            "FAIL_CLEANUP",
            "Owned process/file/subst cleanup evidence is incomplete.",
        )
        _write_failure_record(
            output,
            repository,
            case=str(args.case),
            run_id=run_id,
            failure=failure,
        )
        raise failure

    summary = build_pass_summary(
        config=config,
        terminal=terminal,
        results=results,
        process_exit_codes=process_codes,
        unity_version=_unity_version_from_log(
            pathlib.Path(str(config["unityLog"]))
        ),
        cleanup=cleanup,
        owner_stopped_roles=owner_stopped_roles,
    )
    protocol.write_json_atomic(
        output / "summary.json",
        summary,
        repo_root=repository,
    )
    print(
        f"PHASE184G_BATCH_CASE_PASS case={config['case']} "
        f"profile={config['profile']} summary={output / 'summary.json'}",
        flush=True,
    )
    return 0


def _worker_main(args: argparse.Namespace) -> int:
    config = load_run_config(args.run_config)
    if args.worker == "foxglove-client":
        return run_foxglove_client_worker(config)
    if args.worker == "ros2-peer":
        return run_ros2_peer_worker(config)
    if args.worker == "graph-observer":
        return run_graph_observer_worker(config)
    raise AcceptanceFailure("FAIL_PREFLIGHT", "Unknown worker role.")


def main(argv: Sequence[str] | None = None) -> int:
    try:
        args = validate_arguments(parse_args(argv))
        if args.execution_mode == "worker":
            return _worker_main(args)
        if args.execution_mode == "manual":
            return run_manual_parent(args)
        return run_batch_parent(args)
    except AcceptanceFailure as exc:
        print(exc.code, file=sys.stderr, flush=True)
        return 1
    except KeyboardInterrupt:
        print("FAIL_CLEANUP", file=sys.stderr, flush=True)
        return 130


if __name__ == "__main__":
    raise SystemExit(main())
