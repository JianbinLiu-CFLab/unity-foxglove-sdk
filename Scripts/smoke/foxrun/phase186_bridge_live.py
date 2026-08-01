#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Owned Windows process orchestration for Phase186-H live acceptance."""

from __future__ import annotations

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


class LiveFailure(protocol.ProtocolFailure):
    """Stable live orchestration failure."""


class LiveNotRun(LiveFailure):
    """One exact live prerequisite is missing."""

    def __init__(self, prerequisite: str):
        self.prerequisite = str(prerequisite)[:512]
        super().__init__("NOT_RUN_LIVE_PREREQUISITE", self.prerequisite)


@dataclasses.dataclass(frozen=True)
class PreparedRuntime:
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
        self._job = process_support.WindowsKillOnCloseJob()
        self._owner = process_support.OwnedProcessSet(self._job)
        self._records: dict[str, ProcessRecord] = {}

    @staticmethod
    def _same_path(left: pathlib.Path, right: pathlib.Path) -> bool:
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
        record = self._records[key]
        record.owner_requested = record.process.poll() is None
        return self._owner.stop(key)

    def close(self) -> None:
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
        return self._records[key]

    def has_record(self, key: str) -> bool:
        return key in self._records

    def poll(self, key: str) -> int | None:
        return self._records[key].process.poll()

    def actor_evidence(
        self,
        logical_role: str,
        *,
        preferred_key: str | None = None,
        allow_role_alias: bool = False,
    ) -> dict[str, Any]:
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
        return sorted(
            record.process.pid
            for record in self._records.values()
            if record.process.poll() is None
        )


def _choose_port(excluded: set[int]) -> int:
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
            "ZENOH_ROUTER_CONFIG_URI",
            "ZENOH_SESSION_CONFIG_URI",
            "ZENOH_CONFIG_OVERRIDE",
        }:
            environment.pop(key, None)
    return environment


def _build_runtime_environment(
    source: Mapping[str, str],
    ros2_root: pathlib.Path,
    overlay_install: pathlib.Path,
    *,
    distro: str,
    rmw: str,
    domain_id: int,
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
        "LOCALHOST",
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
) -> PreparedRuntime:
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
        topology_id="phase186h-" + str(config["tokenHash"])[:12],
        zenoh_session_config=zenoh_session,
    )
    environment["ROS_AUTOMATIC_DISCOVERY_RANGE"] = "LOCALHOST"
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


def _wait_port(host: str, port: int, process: ProcessRecord, timeout_seconds: float) -> None:
    deadline = time.monotonic() + timeout_seconds
    while time.monotonic() < deadline:
        if process.process.poll() is not None:
            raise LiveFailure("FAIL_PROCESS_EXIT", f"{process.logical_role} exited before readiness")
        try:
            with socket.create_connection((host, port), timeout=0.2):
                return
        except OSError:
            time.sleep(0.05)
    raise LiveFailure("FAIL_RUNTIME_SELECTION", f"{process.logical_role} listener expired")


def _wait_sidecar(config: Mapping[str, Any], process: ProcessRecord) -> Mapping[str, Any]:
    deadline = time.monotonic() + 120.0
    last: Exception | None = None
    while time.monotonic() < deadline:
        if process.process.poll() is not None:
            raise LiveFailure("FAIL_PROCESS_EXIT", "sidecar exited before health readiness")
        try:
            return live_peer._health(config, str(config["token"]) + "-parent-health")
        except (OSError, protocol.ProtocolFailure) as exc:
            last = exc
            time.sleep(0.1)
    raise LiveFailure(
        "FAIL_BRIDGE",
        "sidecar health readiness expired"
        + (f" ({type(last).__name__})" if last is not None else ""),
    )


def _launch_sidecar(
    owner: OwnedLiveProcesses,
    runtime: PreparedRuntime,
    config: Mapping[str, Any],
    key: str,
) -> tuple[ProcessRecord, Mapping[str, Any]]:
    command = [
        str(runtime.bridge_executable),
        "--host",
        str(config["bridgeHost"]),
        "--port",
        str(config["bridgePort"]),
        "--payload-format",
        "cdr-with-encapsulation",
    ]
    process = owner.launch(
        key,
        "sidecar",
        command,
        cwd=runtime.bridge_executable.parent,
        environment=runtime.environment,
        output_root=pathlib.Path(str(config["outputRoot"])),
    )
    return process, _wait_sidecar(config, process)


def _worker_command(python: pathlib.Path, role: str, config_path: pathlib.Path) -> list[str]:
    return [
        str(python),
        str(pathlib.Path(live_peer.__file__).resolve()),
        "--role",
        role,
        "--run-config",
        str(config_path),
    ]


def _read_actor_document(
    config: Mapping[str, Any],
    role: str,
    kind: str,
) -> Mapping[str, Any] | None:
    path = pathlib.Path(str(config["outputRoot"])) / "actors" / f"{role}-{kind}.json"
    if not path.is_file():
        return None
    try:
        if path.stat().st_size <= 0 or path.stat().st_size > MAX_DOCUMENT_BYTES:
            raise OSError("actor document size differs")
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as exc:
        raise LiveFailure("FAIL_EVIDENCE", f"{role} {kind} evidence is invalid") from exc
    expected = {
        "schemaVersion",
        "runId",
        "caseId",
        "runtimeRowId",
        "tokenHash",
        "head",
        "role",
        "kind",
        "pid",
        "verdict",
        "evidence",
        "createdAt",
    }
    if (
        not isinstance(value, Mapping)
        or set(value) != expected
        or value["schemaVersion"] != 1
        or value["runId"] != config["runId"]
        or value["caseId"] != config["caseId"]
        or value["runtimeRowId"] != config["runtimeRowId"]
        or value["tokenHash"] != config["tokenHash"]
        or value["head"] != config["head"]
        or value["role"] != role
        or value["kind"] != kind
        or value["verdict"] != ("READY" if kind == "ready" else "PASS")
        or not isinstance(value["evidence"], Mapping)
    ):
        raise LiveFailure("FAIL_EVIDENCE", f"{role} {kind} identity differs")
    return value


def _wait_actor_document(
    config: Mapping[str, Any],
    owner: OwnedLiveProcesses,
    role: str,
    kind: str,
    timeout_seconds: float,
    *,
    owner_role: str | None = None,
) -> Mapping[str, Any]:
    process_role = owner_role or role
    deadline = time.monotonic() + timeout_seconds
    while time.monotonic() < deadline:
        document = _read_actor_document(config, role, kind)
        if document is not None:
            return document
        if owner.poll(process_role) is not None:
            failure = (
                pathlib.Path(str(config["outputRoot"]))
                / "actors"
                / f"{process_role}-failure.json"
            )
            detail = failure.read_text(encoding="utf-8")[:512] if failure.is_file() else ""
            raise LiveFailure(
                "FAIL_PROCESS_EXIT",
                f"{role} exited before {kind}: {detail}",
            )
        time.sleep(0.05)
    raise LiveFailure("FAIL_TERMINAL", f"{role} {kind} evidence expired")


def _unity_command(
    unity: pathlib.Path,
    config: Mapping[str, Any],
) -> list[str]:
    return [
        str(unity),
        "-batchmode",
        "-nographics",
        "-projectPath",
        str(config["projectPath"]),
        "-executeMethod",
        UNITY_EXECUTE_METHOD,
        "-phase186RunConfig",
        str(pathlib.Path(str(config["outputRoot"])) / "run-config.json"),
        "-logFile",
        str(config["unityLog"]),
    ]


def _marker_in_log(config: Mapping[str, Any], prefix: str) -> bool:
    return live_peer._has_unity_marker(config, prefix)


def _manual_marker_in_log(config: Mapping[str, Any]) -> bool:
    for line in live_peer._read_log(pathlib.Path(str(config["unityLog"]))).splitlines():
        try:
            protocol.parse_manual_completion_marker(
                line.strip(),
                case_id=str(config["caseId"]),
                run_id=str(config["runId"]),
                token=str(config["token"]),
                head=str(config["head"]),
            )
            return True
        except protocol.ProtocolFailure:
            continue
    return False


def _wait_unity_ready(
    config: Mapping[str, Any],
    owner: OwnedLiveProcesses,
    timeout_seconds: float = protocol.COORDINATOR_UNITY_READY_TIMEOUT_SECONDS,
) -> None:
    deadline = time.monotonic() + timeout_seconds
    while time.monotonic() < deadline:
        if _marker_in_log(config, "PHASE186_ACCEPTANCE_READY"):
            return
        if owner.poll("unity") is not None:
            raise LiveFailure("FAIL_PROCESS_EXIT", "Unity exited before readiness")
        time.sleep(0.05)
    raise LiveFailure("FAIL_TERMINAL", "Unity readiness expired")


def _write_identity_gate(config: Mapping[str, Any], key: str) -> pathlib.Path:
    if key not in {"externalGate", "exerciseGate"}:
        raise LiveFailure("FAIL_PROTOCOL", "unknown Unity gate key")
    target = pathlib.Path(str(config[key]))
    value = {
        "schemaVersion": 1,
        "runId": config["runId"],
        "caseId": config["caseId"],
        "tokenHash": config["tokenHash"],
        "head": config["head"],
        "ready": True,
    }
    with tempfile.NamedTemporaryFile(
        mode="w",
        encoding="utf-8",
        newline="\n",
        dir=target.parent,
        prefix=target.name + ".",
        suffix=".tmp",
        delete=False,
    ) as stream:
        json.dump(value, stream, indent=2, sort_keys=True)
        stream.write("\n")
        temporary = pathlib.Path(stream.name)
    os.replace(temporary, target)
    return target


def _write_gate(config: Mapping[str, Any]) -> pathlib.Path:
    return _write_identity_gate(config, "externalGate")


def _write_exercise_gate(config: Mapping[str, Any]) -> pathlib.Path:
    return _write_identity_gate(config, "exerciseGate")


def _parse_unity_evidence(config: Mapping[str, Any]) -> Mapping[str, Any]:
    log = pathlib.Path(str(config["unityLog"]))
    text = live_peer._read_log(log)
    if not _marker_in_log(config, "PHASE186_ACCEPTANCE_PASS"):
        raise LiveFailure("FAIL_TERMINAL", "exact Unity PASS marker is absent")
    evidence_line = next(
        (
            line
            for line in reversed(text.splitlines())
            if line.startswith("PHASE186_ACCEPTANCE_EVIDENCE ")
            and f"run={config['runId']}" in line
            and f"case={config['caseId']}" in line
            and f"tokenHash={config['tokenHash']}" in line
        ),
        None,
    )
    if evidence_line is None:
        raise LiveFailure("FAIL_EVIDENCE", "exact Unity evidence marker is absent")
    fields: dict[str, str] = {}
    for part in evidence_line.split()[1:]:
        if "=" in part:
            key, value = part.split("=", 1)
            fields[key] = value
    numeric = {
        key: int(value)
        for key, value in fields.items()
        if key not in {"run", "case", "tokenHash"}
    }
    if numeric.get("sent", 0) <= 0 and any(
        live_peer._is_publish(kind)
        for kind in protocol.CASE_CONTRACT_KINDS[str(config["caseId"])]
    ):
        raise LiveFailure("FAIL_EVIDENCE", "Unity reported no sent Bridge frames")
    if config["caseId"] == "slow-main-thread-640hz" and numeric.get("replaced", 0) <= 0:
        raise LiveFailure("FAIL_EVIDENCE", "slow-main-thread case reported no replacement")
    if config["caseId"] in {"reconnect-degraded-recovery", "lifecycle"}:
        if numeric.get("disconnectTransitions", 0) <= 0 or numeric.get("connectTransitions", 0) < 2:
            raise LiveFailure("FAIL_EVIDENCE", "reconnect transition evidence is incomplete")
    document = {
        "marker": evidence_line,
        "fields": numeric,
        "unityVersion": _unity_version(text),
    }
    path = pathlib.Path(str(config["outputRoot"])) / "unity-evidence.json"
    live_peer._write_json_atomic(path, document)
    return document


def _unity_version(log_text: str) -> str:
    for line in log_text.splitlines():
        if "Version is '" in line:
            return line.split("Version is '", 1)[1].split("'", 1)[0]
        if "Initialize engine version:" in line:
            return line.split("Initialize engine version:", 1)[1].strip()
    return "unknown"


def _manual_editor_log() -> pathlib.Path:
    local = os.environ.get("LOCALAPPDATA")
    if not local:
        raise LiveNotRun("LOCALAPPDATA for the Unity Editor log")
    return pathlib.Path(local) / "Unity" / "Editor" / "Editor.log"


def _mirror_manual_log(config: Mapping[str, Any]) -> None:
    source = _manual_editor_log()
    target = pathlib.Path(str(config["unityLog"]))
    text = live_peer._read_log(source)
    target.write_text(text, encoding="utf-8", newline="\n")


def _write_manual_pointer(repository: pathlib.Path, config: Mapping[str, Any]) -> pathlib.Path:
    pointer = repository / MANUAL_POINTER
    pointer.parent.mkdir(parents=True, exist_ok=True)
    if pointer.exists():
        try:
            current = json.loads(pointer.read_text(encoding="utf-8"))
        except (OSError, UnicodeError, json.JSONDecodeError) as exc:
            raise LiveFailure("FAIL_PREFLIGHT", "manual pointer is foreign or malformed") from exc
        if current.get("tokenHash") != config["tokenHash"]:
            raise LiveFailure("FAIL_PREFLIGHT", "another Phase186 manual run is active")
    shutil.copyfile(
        pathlib.Path(str(config["outputRoot"])) / "run-config.json",
        pointer,
    )
    return pointer


def _remove_manual_pointer(pointer: pathlib.Path | None, config: Mapping[str, Any]) -> None:
    if pointer is None or not pointer.exists():
        return
    try:
        current = json.loads(pointer.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as exc:
        raise LiveFailure("FAIL_CLEANUP", "manual pointer changed during the run") from exc
    if current.get("tokenHash") != config["tokenHash"]:
        raise LiveFailure("FAIL_CLEANUP", "manual pointer ownership changed")
    pointer.unlink()


def _worker_roles(config: Mapping[str, Any]) -> tuple[str, ...]:
    return tuple(
        role for role in sorted(config["requiredActors"]) if role in WORKER_ROLES
    )


def _worker_process_roles(config: Mapping[str, Any]) -> tuple[str, ...]:
    """Return physical workers, cohosting graph APIs in the external ROS peer."""

    roles = _worker_roles(config)
    if "ros-peer" in roles and "graph-observer" in roles:
        return tuple(role for role in roles if role != "graph-observer")
    return roles


def _owner_role_for_document(config: Mapping[str, Any], role: str) -> str:
    """Resolve the owned process that is responsible for one logical document."""

    roles = _worker_roles(config)
    if role == "graph-observer" and "ros-peer" in roles:
        return "ros-peer"
    return role


def _observation_path(
    config: Mapping[str, Any], worker_results: Mapping[str, Mapping[str, Any]], name: str
) -> pathlib.Path:
    output = pathlib.Path(str(config["outputRoot"]))
    if name == "unity":
        return output / "unity-evidence.json"
    if name == "bridge":
        return output / "bridge-evidence.json"
    if name == "packages":
        return output / "preflight.json"
    if name == "resources":
        return output / "cleanup.json"
    if name in {"graph", "qos"} and "graph-observer" in worker_results:
        return output / "actors" / "graph-observer-result.json"
    if name in {"data", "origin", "peer"}:
        for role in ("ros-peer", "hostile-peer", "wire-peer", "foxglove-client"):
            if role in worker_results:
                return output / "actors" / f"{role}-result.json"
    raise LiveFailure("FAIL_EVIDENCE", f"no live evidence path exists for {name}")


def _cleanup_document(
    config: Mapping[str, Any],
    owner: OwnedLiveProcesses,
    pointer: pathlib.Path | None,
    *,
    extra_endpoints: Sequence[tuple[str, int]] = (),
    cleanup_errors: Sequence[str] = (),
) -> dict[str, Any]:
    residual_ports: list[int] = []
    endpoints = (
        (str(config["bridgeHost"]), int(config["bridgePort"])),
        (str(config["foxgloveHost"]), int(config["foxglovePort"])),
        *tuple(extra_endpoints),
    )
    for host, port in endpoints:
        try:
            with socket.create_connection((host, port), timeout=0.1):
                residual_ports.append(port)
        except OSError:
            pass
    residual_files = []
    for candidate in (
        pointer,
        pathlib.Path(str(config["externalGate"])),
        pathlib.Path(str(config["exerciseGate"])),
    ):
        if candidate is not None and candidate.exists():
            residual_files.append(str(candidate.resolve()))
    bounded_errors = [str(value)[:512] for value in cleanup_errors]
    result = {
        "complete": not owner.residual_pids()
        and not residual_ports
        and not residual_files
        and not bounded_errors,
        "cleanupErrors": bounded_errors,
        "residualProcesses": owner.residual_pids(),
        "residualPorts": residual_ports,
        "residualOverlays": [],
        "residualTemporaryProjects": residual_files,
    }
    live_peer._write_json_atomic(
        pathlib.Path(str(config["outputRoot"])) / "cleanup.json", result
    )
    return result


def run_live(
    repository: pathlib.Path,
    config: Mapping[str, Any],
    *,
    unity_editor: pathlib.Path,
    manual_timeout_seconds: float,
) -> tuple[dict[str, Any], dict[str, Any], dict[str, Any]]:
    """Run one exact live case and return terminal actor/observation/cleanup sections."""

    output = pathlib.Path(str(config["outputRoot"]))
    config_path = output / "run-config.json"
    runtime = prepare_runtime(repository, config)
    owner = OwnedLiveProcesses()
    pointer: pathlib.Path | None = None
    worker_results: dict[str, Mapping[str, Any]] = {}
    health_generations: list[Mapping[str, Any]] = []
    failure: BaseException | None = None
    cleanup_errors: list[str] = []
    try:
        if runtime.zenoh_router is not None:
            router = owner.launch(
                "zenoh-router",
                "zenoh-router",
                [str(runtime.zenoh_router)],
                cwd=runtime.zenoh_router.parent,
                environment=runtime.zenoh_router_environment or runtime.environment,
                output_root=output,
            )
            assert runtime.zenoh_endpoint is not None
            _wait_port(*runtime.zenoh_endpoint, router, 60.0)
        _sidecar, health = _launch_sidecar(owner, runtime, config, "sidecar-1")
        health_generations.append(health)

        for role in _worker_process_roles(config):
            python = (
                runtime.python_executable
                if role in {"ros-peer", "graph-observer"}
                else pathlib.Path(sys.executable).resolve(strict=True)
            )
            environment = (
                runtime.environment
                if role in {"ros-peer", "graph-observer"}
                else _clean_unity_environment(os.environ)
            )
            owner.launch(
                role,
                role,
                _worker_command(python, role, config_path),
                cwd=repository,
                environment=environment,
                output_root=output,
            )
            _wait_actor_document(config, owner, role, "ready", 180.0)
            if role == "ros-peer" and "graph-observer" in _worker_roles(config):
                _wait_actor_document(
                    config,
                    owner,
                    "graph-observer",
                    "ready",
                    180.0,
                    owner_role="ros-peer",
                )

        if config["caseId"] == "frozen-v1":
            for role in _worker_roles(config):
                worker_results[role] = _wait_actor_document(
                    config, owner, role, "result", 180.0
                )
        elif bool(config["manual"]):
            pointer = _write_manual_pointer(repository, config)
            print(
                "PHASE186_MANUAL_READY"
                + f" case={config['caseId']} run={config['runId']}"
                + f" tokenHash={config['tokenHash']} head={config['head']}"
                + f" pointer={pointer}",
                flush=True,
            )
            deadline = time.monotonic() + manual_timeout_seconds
            gate_written = False
            while time.monotonic() < deadline:
                _mirror_manual_log(config)
                for role in _worker_roles(config):
                    if role not in worker_results:
                        document = _read_actor_document(config, role, "result")
                        if document is not None:
                            worker_results[role] = document
                if len(worker_results) == len(_worker_roles(config)) and not gate_written:
                    _write_gate(config)
                    gate_written = True
                if _manual_marker_in_log(config):
                    break
                time.sleep(0.1)
            else:
                raise LiveFailure("FAIL_TERMINAL", "manual completion marker expired")
            if len(worker_results) != len(_worker_roles(config)):
                raise LiveFailure("FAIL_EVIDENCE", "manual live actor evidence is incomplete")
            document = {
                "marker": "PHASE186_MANUAL_COMPLETE",
                "unityVersion": "user-owned-editor",
            }
            live_peer._write_json_atomic(output / "unity-evidence.json", document)
        else:
            owner.launch(
                "unity",
                "unity",
                _unity_command(unity_editor, config),
                cwd=repository,
                environment=_clean_unity_environment(os.environ),
                output_root=output,
            )
            _wait_unity_ready(config, owner)
            if config["caseId"] in {"reconnect-degraded-recovery", "lifecycle"}:
                time.sleep(0.75)
                owner.stop("sidecar-1")
                _wait_until_port_released(str(config["bridgeHost"]), int(config["bridgePort"]))
                _sidecar, health = _launch_sidecar(owner, runtime, config, "sidecar-2")
                health_generations.append(health)
            roles = _worker_roles(config)
            if config["caseId"] == "fanout-fairness-health":
                worker_results["graph-observer"] = _wait_actor_document(
                    config,
                    owner,
                    "graph-observer",
                    "result",
                    240.0,
                    owner_role="ros-peer",
                )
                _write_exercise_gate(config)
            for role in roles:
                if role in worker_results:
                    continue
                worker_results[role] = _wait_actor_document(
                    config,
                    owner,
                    role,
                    "result",
                    240.0,
                    owner_role=_owner_role_for_document(config, role),
                )
            _write_gate(config)
            unity = owner.record("unity").process
            try:
                exit_code = unity.wait(timeout=240.0)
            except subprocess.TimeoutExpired as exc:
                raise LiveFailure("FAIL_TERMINAL", "Unity terminal exit expired") from exc
            if exit_code != 0:
                raise LiveFailure("FAIL_PROCESS_EXIT", f"Unity exited {exit_code}")
            _parse_unity_evidence(config)

        bridge_document = {
            "runtimeRowId": runtime.row_id,
            "distro": runtime.distro,
            "rmw": runtime.rmw,
            "healthGenerations": health_generations,
            "buildSummary": str(
                repository
                / "build"
                / "phase186"
                / "bridge"
                / runtime.row_id
                / "build-summary.json"
            ),
        }
        live_peer._write_json_atomic(output / "bridge-evidence.json", bridge_document)
    except BaseException as exc:
        failure = exc
    finally:
        try:
            _remove_manual_pointer(pointer, config)
        except BaseException as exc:
            cleanup_errors.append(f"manual pointer cleanup: {exc}")
        for key, label in (
            ("externalGate", "external gate"),
            ("exerciseGate", "exercise gate"),
        ):
            try:
                candidate = pathlib.Path(str(config[key]))
                if candidate.exists():
                    candidate.unlink()
            except BaseException as exc:
                cleanup_errors.append(f"{label} cleanup: {exc}")
        try:
            owner.close()
        except BaseException as exc:
            cleanup_errors.append(f"owned process cleanup: {exc}")

    cleanup = _cleanup_document(
        config,
        owner,
        pointer,
        extra_endpoints=(runtime.zenoh_endpoint,) if runtime.zenoh_endpoint else (),
        cleanup_errors=cleanup_errors,
    )
    if failure is not None:
        raise failure
    if not cleanup["complete"]:
        raise LiveFailure("FAIL_CLEANUP", "owned live cleanup is incomplete")

    actors: dict[str, Any] = {}
    required = set(config["requiredActors"])
    for role in sorted(required):
        if role == "sidecar":
            preferred = "sidecar-2" if owner.has_record("sidecar-2") else "sidecar-1"
            actors[role] = owner.actor_evidence(role, preferred_key=preferred)
        elif role == "graph-observer" and "ros-peer" in required:
            actors[role] = owner.actor_evidence(
                role,
                preferred_key="ros-peer",
                allow_role_alias=True,
            )
        else:
            actors[role] = owner.actor_evidence(role)
    observations = {
        name: {
            "observed": True,
            "source": _observation_source(name),
            "path": str(_observation_path(config, worker_results, name).resolve()),
        }
        for name in sorted(protocol.CASES[str(config["caseId"])].required_observations)
    }
    return actors, observations, cleanup


def _observation_source(name: str) -> str:
    return {
        "unity": "live-unity-editor",
        "bridge": "live-sidecar-health",
        "peer": "live-independent-peer",
        "graph": "live-rclpy-graph-api",
        "qos": "live-rclpy-endpoint-info",
        "data": "live-correlated-payload",
        "origin": "live-publisher-origin",
        "resources": "live-owned-cleanup",
        "packages": "current-package-composition",
    }[name]


def _wait_until_port_released(host: str, port: int) -> None:
    deadline = time.monotonic() + 15.0
    while time.monotonic() < deadline:
        try:
            with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as probe:
                if os.name == "nt":
                    probe.setsockopt(socket.SOL_SOCKET, socket.SO_EXCLUSIVEADDRUSE, 1)
                probe.bind((host, port))
                return
        except OSError:
            time.sleep(0.05)
    raise LiveFailure("FAIL_CLEANUP", "sidecar port was not released for reconnect")
