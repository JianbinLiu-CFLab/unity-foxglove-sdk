#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Module: Scripts/smoke/foxrun
# Purpose: Prove one portable local-origin primitive across the Phase186 ROS/RMW matrix.

"""Run a real generic publisher/subscriber origin-classification probe.

The probe uses ``GenericSubscription.take_serialized`` plus the returned
``rmw_message_info.publisher_gid``.  It compares that GID with the
process-owned generic publisher GID and separately observes an independently
owned publisher on the same topic/type.  All four maintained rows must select
this exact mechanism before Phase186 may claim portable loop suppression.
"""

from __future__ import annotations

import argparse
import json
import os
import pathlib
import platform
import subprocess
import sys
import tempfile
import time
import uuid
from collections.abc import Mapping, Sequence
from types import MappingProxyType


SCRIPT_DIRECTORY = pathlib.Path(__file__).resolve().parent
if str(SCRIPT_DIRECTORY) not in sys.path:
    sys.path.insert(0, str(SCRIPT_DIRECTORY))

import phase186_bridge_build as build
import phase186_bridge_acceptance_protocol as acceptance_protocol


SELECTED_MECHANISM = "publisher_gid_take_serialized"
SUMMARY_SCHEMA_VERSION = 1
DOMAIN_IDS: Mapping[str, int] = MappingProxyType(
    {row_id: row.domain_id for row_id, row in acceptance_protocol.ROWS.items()}
)


class ProbeFailure(RuntimeError):
    """Stable fail-closed capability-probe error."""


def validate_row_result(
    value: Mapping[str, object],
    row: build.BridgeRow,
) -> None:
    """Require direct ROS observations for one exact row."""

    if not isinstance(value, Mapping):
        raise ProbeFailure("row result is not an object")
    expected = {
        "schemaVersion": SUMMARY_SCHEMA_VERSION,
        "rowId": row.row_id,
        "distro": row.distro,
        "requestedRmw": row.rmw,
        "observedRmw": row.rmw,
        "verdict": "PASS",
        "platform": "Windows",
        "domainOwned": True,
        "ambientDomainRejected": True,
        "canonicalType": build.INTERFACE_TYPE,
        "interfaceDigest": build.INTERFACE_DIGEST,
        "mechanism": SELECTED_MECHANISM,
    }
    for key, expected_value in expected.items():
        if value.get(key) != expected_value:
            raise ProbeFailure("row result mismatch for " + key)
    domain = value.get("domainId")
    if not isinstance(domain, int) or domain != DOMAIN_IDS[row.row_id]:
        raise ProbeFailure("row result did not use its owned domain")
    overlay = value.get("overlayAuthority")
    if (
        not isinstance(overlay, Mapping)
        or overlay.get("validated") is not True
        or overlay.get("rowId") != row.row_id
    ):
        raise ProbeFailure("row result lacks exact overlay authority")
    observations = value.get("rosObservations")
    expected_observations = {
        "localSeen": True,
        "localGidMatched": True,
        "ignoreLocalSawLocal": False,
        "externalSeen": True,
        "externalGidMatched": False,
        "ignoreLocalSawExternal": True,
    }
    if not isinstance(observations, Mapping):
        raise ProbeFailure("row result lacks direct ROS observations")
    for key, expected_value in expected_observations.items():
        if observations.get(key) is not expected_value:
            raise ProbeFailure("ROS observation mismatch for " + key)
    owned = value.get("ownedProcesses")
    if (
        not isinstance(owned, Mapping)
        or not isinstance(owned.get("subscriberPid"), int)
        or not isinstance(owned.get("publisherPid"), int)
        or owned.get("cleanupComplete") is not True
    ):
        raise ProbeFailure("row result lacks owned-process cleanup proof")
    if row.rmw == "rmw_zenoh_cpp":
        topology = value.get("zenohTopology")
        if (
            not isinstance(topology, Mapping)
            or topology.get("owned") is not True
            or not topology.get("topologyId")
            or not isinstance(topology.get("routerPid"), int)
            or not topology.get("sessionConfig")
        ):
            raise ProbeFailure("Zenoh row lacks owned topology evidence")


def validate_matrix(
    rows: Mapping[str, Mapping[str, object]],
) -> dict[str, object]:
    """Require the exact four-row matrix and one identical mechanism."""

    if not isinstance(rows, Mapping) or tuple(rows) != tuple(build.ROWS):
        raise ProbeFailure(
            "capability matrix must contain the exact maintained rows in order"
        )
    mechanisms: set[str] = set()
    for row_id, row in build.ROWS.items():
        value = rows.get(row_id)
        if not isinstance(value, Mapping):
            raise ProbeFailure("capability matrix row is missing: " + row_id)
        validate_row_result(value, row)
        mechanisms.add(str(value.get("mechanism")))
    if mechanisms != {SELECTED_MECHANISM}:
        raise ProbeFailure("capability matrix selected different mechanisms")
    return {
        "schemaVersion": SUMMARY_SCHEMA_VERSION,
        "verdict": "PASS",
        "rows": list(build.ROWS),
        "selectedMechanism": SELECTED_MECHANISM,
        "canonicalType": build.INTERFACE_TYPE,
        "interfaceDigest": build.INTERFACE_DIGEST,
    }


def _write_json_atomic(path: pathlib.Path, value: Mapping[str, object]) -> None:
    """Write one probe evidence object by atomic file replacement."""

    path.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile(
        mode="w",
        encoding="utf-8",
        newline="\n",
        dir=path.parent,
        prefix=path.name + ".",
        suffix=".tmp",
        delete=False,
    ) as stream:
        json.dump(value, stream, indent=2, sort_keys=True)
        stream.write("\n")
        temporary = pathlib.Path(stream.name)
    os.replace(temporary, path)


def _read_json(path: pathlib.Path) -> Mapping[str, object]:
    """Read a required JSON evidence object or fail closed."""

    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise ProbeFailure("required JSON evidence is unavailable: " + str(path)) from exc
    if not isinstance(value, Mapping):
        raise ProbeFailure("required JSON evidence is not an object: " + str(path))
    return value


def _load_last_json_line(path: pathlib.Path) -> Mapping[str, object]:
    """Load the last valid JSON object emitted to an owned probe log."""

    try:
        lines = path.read_text(encoding="utf-8", errors="replace").splitlines()
    except OSError as exc:
        raise ProbeFailure("owned probe log is unavailable") from exc
    for line in reversed(lines):
        if not line.startswith("{"):
            continue
        try:
            value = json.loads(line)
        except json.JSONDecodeError:
            continue
        if isinstance(value, Mapping):
            return value
    raise ProbeFailure("owned probe emitted no machine-readable result")


def _wait_for_marker(
    path: pathlib.Path,
    process: subprocess.Popen[str],
    marker: str,
    timeout_seconds: float,
) -> None:
    """Wait until an owned process emits the required readiness marker."""

    deadline = time.monotonic() + timeout_seconds
    while time.monotonic() < deadline:
        if path.is_file() and marker in path.read_text(
            encoding="utf-8", errors="replace"
        ):
            return
        if process.poll() is not None:
            raise ProbeFailure("subscriber exited before its ready marker")
        time.sleep(0.05)
    raise ProbeFailure("subscriber did not reach its ready marker")


def _terminate_owned(process: subprocess.Popen[str] | None) -> str | None:
    """Terminate and reap one process, returning a bounded cleanup diagnostic."""

    if process is None:
        return None
    pid = int(getattr(process, "pid", 0) or 0)
    try:
        if process.poll() is not None:
            return None
        process.kill()
        process.wait(timeout=10)
    except (OSError, subprocess.TimeoutExpired) as exc:
        return (
            f"owned process {pid} could not be terminated or reaped "
            f"({type(exc).__name__})"
        )
    return None


def _record_cleanup_failures(
    result: dict[str, object],
    diagnostics: Sequence[str],
) -> None:
    """Make owned-process cleanup failures part of the durable row result."""

    failures = tuple(value for value in diagnostics if value)
    if not failures:
        return
    cleanup_message = "owned-process cleanup failed: " + "; ".join(failures)
    existing = result.get("failure")
    result["failure"] = (
        str(existing) + "; " + cleanup_message
        if isinstance(existing, str) and existing
        else cleanup_message
    )
    result["verdict"] = "FAIL"
    owned = result.get("ownedProcesses")
    if isinstance(owned, dict):
        owned["cleanupComplete"] = False


def _process_options() -> dict[str, object]:
    """Return platform-specific options for an owned process group."""

    if os.name == "nt":
        return {
            "creationflags": int(
                getattr(subprocess, "CREATE_NEW_PROCESS_GROUP", 0)
            )
        }
    return {"start_new_session": True}


def _generate_serialized_payload(
    python_executable: pathlib.Path,
    environment: Mapping[str, str],
    origin_id: str,
) -> str:
    """Generate the canonical serialized Phase181 envelope payload."""

    code = (
        "from rclpy.serialization import serialize_message;"
        "from unity2foxglove_foxrun_interfaces_v1.msg import "
        "Phase181State48D288ED82F1Envelope as E;"
        "m=E();"
        "m.foxrun_origin_id=" + repr(origin_id) + ";"
        "m.foxrun_sequence=1;"
        "m.payload.message='phase186';"
        "m.payload.foxrun_has_message=True;"
        "print(bytes(serialize_message(m)).hex())"
    )
    try:
        result = subprocess.run(
            [str(python_executable), "-c", code],
            env=dict(environment),
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            text=True,
            errors="replace",
            timeout=30,
            check=False,
            shell=False,
        )
    except OSError as exc:
        raise ProbeFailure("row-local ROS Python could not start") from exc
    payload = result.stdout.strip().splitlines()[-1] if result.stdout.strip() else ""
    if result.returncode != 0 or not payload or any(
        character not in "0123456789abcdefABCDEF" for character in payload
    ):
        raise ProbeFailure("row-local custom envelope serialization failed")
    return payload


def _runtime_environment(
    repository: pathlib.Path,
    row: build.BridgeRow,
    overlay: Mapping[str, object],
    domain_id: int,
) -> tuple[dict[str, str], pathlib.Path, object]:
    """Construct the isolated runtime environment for one capability row."""

    peer = build._load_phase181_peer(repository)
    ros2_root = pathlib.Path(
        os.environ.get(
            "PHASE186_ROS2_" + row.distro.upper() + "_ROOT",
            str(repository / "ros2-windows" / ("ros2_" + row.distro)),
        )
    )
    toolchain = peer.resolve_windows_peer_toolchain(ros2_root)
    base = peer.ros2env.build_ros_env(
        toolchain.ros2_root,
        row.rmw,
        "LOCALHOST",
        str(domain_id),
        row.distro,
    )
    install = pathlib.Path(str(overlay["installPrefix"]))
    environment = peer.build_peer_environment(
        base,
        toolchain.ros2_root,
        install,
        distro=row.distro,
        rmw=row.rmw,
        domain_id=domain_id,
        topology_id=None,
        zenoh_session_config=None,
    )
    environment["ROS_DOMAIN_ID"] = str(domain_id)
    environment["RMW_IMPLEMENTATION"] = row.rmw
    environment["ROS_AUTOMATIC_DISCOVERY_RANGE"] = "LOCALHOST"
    environment.pop("ROS_LOCALHOST_ONLY", None)
    environment.pop("ROS_DISCOVERY_SERVER", None)
    return environment, toolchain.python_executable, peer


def run_row(
    repository: pathlib.Path,
    row: build.BridgeRow,
    output_root: pathlib.Path,
    *,
    timeout_seconds: float,
) -> dict[str, object]:
    """Run one live row using only artifacts certified by the row build."""

    repository = pathlib.Path(repository).resolve()
    output_root = pathlib.Path(output_root).resolve()
    row_root = output_root / row.row_id
    result_path = row_root / "capability-result.json"
    started = build.timestamp()
    subscriber: subprocess.Popen[str] | None = None
    publisher: subprocess.Popen[str] | None = None
    subscriber_log_stream = None
    publisher_log_stream = None
    topology_handle = None
    zenoh_evidence: dict[str, object] | None = None
    subscriber_pid = 0
    publisher_pid = 0
    result: dict[str, object] | None = None
    try:
        if os.name != "nt" or platform.system() != "Windows":
            raise build.LivePrerequisiteMissing("Windows-native execution")
        build_summary_path = row_root / "build-summary.json"
        build_summary = _read_json(build_summary_path)
        build.validate_build_summary(build_summary, row)
        overlay = _read_json(row_root / "overlay-authority.json")
        build.validate_overlay_authority(overlay, row, row_root)
        executable = pathlib.Path(
            str(
                (
                    build_summary.get("probeExecutable")
                    if isinstance(build_summary.get("probeExecutable"), Mapping)
                    else {}
                ).get("path", "")
            )
        )
        expected_executable_hash = (
            build_summary.get("probeExecutable")
            if isinstance(build_summary.get("probeExecutable"), Mapping)
            else {}
        ).get("sha256")
        if (
            not executable.is_file()
            or build.sha256_file(executable) != expected_executable_hash
        ):
            raise ProbeFailure("certified origin probe executable is stale")

        domain_id = DOMAIN_IDS[row.row_id]
        environment, python_executable, peer = _runtime_environment(
            repository,
            row,
            overlay,
            domain_id,
        )
        if environment.get("ROS_DOMAIN_ID") != str(domain_id):
            raise ProbeFailure("owned ROS domain was not installed")

        if row.rmw == "rmw_zenoh_cpp":
            ros_scripts = repository / "Scripts" / "smoke" / "ros2"
            if str(ros_scripts) not in sys.path:
                sys.path.insert(0, str(ros_scripts))
            import phase179_zenoh_topology as zenoh

            ros2_root = pathlib.Path(
                os.environ.get(
                    "PHASE186_ROS2_LYRICAL_ROOT",
                    str(repository / "ros2-windows" / "ros2_lyrical"),
                )
            )
            router = ros2_root / "Lib" / "rmw_zenoh_cpp" / "rmw_zenohd.exe"
            templates = ros2_root / "share" / "rmw_zenoh_cpp" / "config"
            topology_id = "phase186-" + row.row_id + "-" + uuid.uuid4().hex[:12]
            owned_config = zenoh.create_owned_local_router_config(
                router_template=templates / "DEFAULT_RMW_ZENOH_ROUTER_CONFIG.json5",
                session_template=templates / "DEFAULT_RMW_ZENOH_SESSION_CONFIG.json5",
                output_directory=row_root / "zenoh",
            )
            options = zenoh.validate_topology_options(
                row.rmw,
                router=router,
                no_router=False,
                topology_id=topology_id,
            )
            router_environment = dict(environment)
            topology_handle = zenoh.start_topology(
                options,
                env=router_environment,
                cwd=row_root,
                log_path=row_root / "zenoh-router.log",
                ready_timeout_seconds=min(timeout_seconds, 30.0),
                owned_config=owned_config,
            )
            environment["ZENOH_SESSION_CONFIG_URI"] = str(
                owned_config.session_config
            )
            zenoh_evidence = {
                "owned": True,
                "topologyId": topology_id,
                "routerPid": topology_handle.process.pid,
                "sessionConfig": str(owned_config.session_config),
                "routerConfig": str(owned_config.router_config),
                "endpoint": owned_config.endpoint,
            }

        payload_hex = _generate_serialized_payload(
            python_executable,
            environment,
            "phase186-" + row.row_id,
        )
        topic = "/phase186/origin/" + row.row_id.replace("-", "_")
        common = [
            str(executable),
            "--topic",
            topic,
            "--type",
            build.INTERFACE_TYPE,
            "--payload-hex",
            payload_hex,
            "--timeout-ms",
            str(int(timeout_seconds * 1000)),
        ]
        subscriber_log = row_root / "origin-subscriber.log"
        publisher_log = row_root / "origin-publisher.log"
        subscriber_log_stream = subscriber_log.open(
            "w", encoding="utf-8", newline="\n"
        )
        subscriber = subprocess.Popen(
            [common[0], "--role", "subscriber", *common[1:]],
            cwd=str(row_root),
            env=environment,
            stdout=subscriber_log_stream,
            stderr=subprocess.STDOUT,
            text=True,
            shell=False,
            **_process_options(),
        )
        subscriber_pid = subscriber.pid
        _wait_for_marker(
            subscriber_log,
            subscriber,
            "PHASE186_ORIGIN_PROBE_READY",
            min(timeout_seconds, 30.0),
        )
        publisher_log_stream = publisher_log.open(
            "w", encoding="utf-8", newline="\n"
        )
        publisher = subprocess.Popen(
            [common[0], "--role", "publisher", *common[1:]],
            cwd=str(row_root),
            env=environment,
            stdout=publisher_log_stream,
            stderr=subprocess.STDOUT,
            text=True,
            shell=False,
            **_process_options(),
        )
        publisher_pid = publisher.pid
        publisher_exit = publisher.wait(timeout=timeout_seconds)
        subscriber_exit = subscriber.wait(timeout=timeout_seconds)
        publisher_log_stream.close()
        publisher_log_stream = None
        subscriber_log_stream.close()
        subscriber_log_stream = None
        if publisher_exit != 0 or subscriber_exit != 0:
            raise ProbeFailure("owned origin probe process returned nonzero")
        subscriber_result = _load_last_json_line(subscriber_log)
        publisher_result = _load_last_json_line(publisher_log)
        observed_rmw = subscriber_result.get("observedRmw")
        if (
            publisher_result.get("observedRmw") != observed_rmw
            or observed_rmw != row.rmw
        ):
            raise ProbeFailure("requested RMW differs from process observations")
        result = {
            "schemaVersion": SUMMARY_SCHEMA_VERSION,
            "rowId": row.row_id,
            "distro": row.distro,
            "requestedRmw": row.rmw,
            "observedRmw": observed_rmw,
            "verdict": "PASS",
            "platform": platform.system(),
            "domainId": domain_id,
            "domainOwned": True,
            "ambientDomainRejected": True,
            "canonicalType": build.INTERFACE_TYPE,
            "interfaceDigest": build.INTERFACE_DIGEST,
            "overlayAuthority": {
                "validated": True,
                "rowId": row.row_id,
                "installPrefix": overlay["installPrefix"],
                "localSetupSha256": overlay["localSetupSha256"],
            },
            "mechanism": subscriber_result.get("mechanism"),
            "rosObservations": {
                key: subscriber_result.get(key)
                for key in (
                    "localSeen",
                    "localGidMatched",
                    "ignoreLocalSawLocal",
                    "externalSeen",
                    "externalGidMatched",
                    "ignoreLocalSawExternal",
                )
            },
            "ownedProcesses": {
                "subscriberPid": subscriber_pid,
                "publisherPid": publisher_pid,
                "subscriberExitCode": subscriber_exit,
                "publisherExitCode": publisher_exit,
                "cleanupComplete": (
                    subscriber.poll() is not None and publisher.poll() is not None
                ),
            },
            "evidence": {
                "subscriberLog": str(subscriber_log),
                "publisherLog": str(publisher_log),
                "buildSummary": str(build_summary_path),
                "overlayAuthority": str(row_root / "overlay-authority.json"),
            },
            "startedAt": started,
            "finishedAt": build.timestamp(),
        }
        if zenoh_evidence is not None:
            result["zenohTopology"] = zenoh_evidence
        validate_row_result(result, row)
        _write_json_atomic(result_path, result)
        return result
    except build.LivePrerequisiteMissing as exc:
        result = {
            **build.not_run_summary(row, str(exc)),
            "startedAt": started,
            "finishedAt": build.timestamp(),
        }
        _write_json_atomic(result_path, result)
        return result
    except Exception as exc:
        result = {
            "schemaVersion": SUMMARY_SCHEMA_VERSION,
            "rowId": row.row_id,
            "distro": row.distro,
            "requestedRmw": row.rmw,
            "verdict": "FAIL",
            "platform": platform.system(),
            "canonicalType": build.INTERFACE_TYPE,
            "interfaceDigest": build.INTERFACE_DIGEST,
            "failure": str(exc),
            "startedAt": started,
            "finishedAt": build.timestamp(),
        }
        _write_json_atomic(result_path, result)
        return result
    finally:
        cleanup_failures = tuple(
            diagnostic
            for diagnostic in (
                _terminate_owned(publisher),
                _terminate_owned(subscriber),
            )
            if diagnostic is not None
        )
        if result is not None and cleanup_failures:
            _record_cleanup_failures(result, cleanup_failures)
            result["finishedAt"] = build.timestamp()
            _write_json_atomic(result_path, result)
        if publisher_log_stream is not None:
            publisher_log_stream.close()
        if subscriber_log_stream is not None:
            subscriber_log_stream.close()
        if topology_handle is not None:
            try:
                import phase179_zenoh_topology as zenoh

                zenoh.close_topology(topology_handle)
            except Exception:
                pass


def parse_args(argv: Sequence[str] | None = None) -> argparse.Namespace:
    """Parse Bridge capability-probe command-line arguments."""

    parser = argparse.ArgumentParser(description=__doc__)
    selection = parser.add_mutually_exclusive_group(required=True)
    selection.add_argument("--row", choices=tuple(build.ROWS))
    selection.add_argument("--all-supported-rows", action="store_true")
    parser.add_argument(
        "--output-root",
        type=pathlib.Path,
        default=None,
    )
    parser.add_argument(
        "--timeout-seconds",
        type=float,
        default=60.0,
    )
    parser.add_argument(
        "--skip-build",
        action="store_true",
        help="Require an already passing exact-row build summary.",
    )
    return parser.parse_args(argv)


def main(argv: Sequence[str] | None = None) -> int:
    """Run selected capability rows and validate the resulting matrix."""

    args = parse_args(argv)
    if args.timeout_seconds <= 0 or args.timeout_seconds > 300:
        raise SystemExit("--timeout-seconds must be in (0, 300]")
    repository = build.repository_root()
    output_root = (
        args.output_root
        if args.output_root is not None
        else repository / "build" / "phase186" / "bridge"
    )
    selected = (
        tuple(build.ROWS)
        if args.all_supported_rows
        else (args.row,)
    )
    results: dict[str, Mapping[str, object]] = {}
    exit_code = 0
    for row_id in selected:
        row = build.require_row(row_id)
        if not args.skip_build:
            built = build.run_row(
                repository,
                row,
                output_root,
                run_tests=True,
            )
            if built.get("verdict") != "PASS":
                results[row_id] = built
                exit_code = max(
                    exit_code,
                    build.verdict_exit_code(built.get("verdict")),
                )
                continue
        print("[phase186-probe] starting " + row_id, flush=True)
        result = run_row(
            repository,
            row,
            output_root,
            timeout_seconds=args.timeout_seconds,
        )
        results[row_id] = result
        print(
            "[phase186-probe] "
            + row_id
            + " => "
            + str(result.get("verdict")),
            flush=True,
        )
        exit_code = max(
            exit_code,
            build.verdict_exit_code(result.get("verdict")),
        )
    if args.all_supported_rows:
        matrix_path = pathlib.Path(output_root) / "capability-matrix.json"
        try:
            matrix = validate_matrix(results)
            matrix = {
                **matrix,
                "rowEvidence": {
                    row_id: str(
                        pathlib.Path(output_root)
                        / row_id
                        / "capability-result.json"
                    )
                    for row_id in build.ROWS
                },
                "finishedAt": build.timestamp(),
            }
        except ProbeFailure as exc:
            matrix = {
                "schemaVersion": SUMMARY_SCHEMA_VERSION,
                "verdict": "FAIL",
                "failure": str(exc),
                "rows": list(results),
                "selectedMechanism": "",
                "canonicalType": build.INTERFACE_TYPE,
                "interfaceDigest": build.INTERFACE_DIGEST,
                "finishedAt": build.timestamp(),
            }
            exit_code = max(exit_code, 1)
        _write_json_atomic(matrix_path, matrix)
    return exit_code


if __name__ == "__main__":
    raise SystemExit(main())
