#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Fail-closed Phase186-H Bridge acceptance coordinator.

The coordinator owns current-run identity, exact repository/Unity/ROS
preflight, IPv4 loopback reservations, evidence paths, actor lifetime, terminal
classification, and cleanup.  A build or tooling PASS is deliberately never
promoted into a live PASS.
"""

from __future__ import annotations

import argparse
import contextlib
import dataclasses
import hashlib
import json
import os
import pathlib
import re
import secrets
import socket
import subprocess
import sys
import tempfile
import time
from collections.abc import Mapping, Sequence
from typing import Any


SCRIPT_DIRECTORY = pathlib.Path(__file__).resolve().parent
if str(SCRIPT_DIRECTORY) not in sys.path:
    sys.path.insert(0, str(SCRIPT_DIRECTORY))

try:
    from Scripts.smoke.foxrun import phase186_bridge_acceptance_protocol as protocol
except ImportError:  # Direct script execution from outside the repository root.
    import phase186_bridge_acceptance_protocol as protocol


EXIT_PASS = 0
EXIT_FAIL = 1
EXIT_USAGE = 2
EXIT_NOT_RUN = 3
MAX_RESCUE_LOG_BYTES = 4 * 1024 * 1024
_UNITY_VERSION = re.compile(r"\A[0-9]+\.[0-9]+\.[0-9]+[a-z][0-9]+\Z")


class AcceptanceFailure(protocol.ProtocolFailure):
    """Stable coordinator failure."""


class LivePrerequisiteMissing(AcceptanceFailure):
    """A specifically named prerequisite is not provisioned."""


@dataclasses.dataclass(frozen=True)
class UnityEditorIdentity:
    """Exact Editor executable selected by the project version."""

    path: pathlib.Path
    version: str


@dataclasses.dataclass
class LoopbackPortReservation:
    """One held IPv4 loopback socket reservation."""

    socket: socket.socket
    host: str
    port: int

    def close(self) -> None:
        self.socket.close()

    def __enter__(self) -> "LoopbackPortReservation":
        return self

    def __exit__(self, _type, _value, _traceback) -> None:
        self.close()


def repository_root() -> pathlib.Path:
    """Locate the repository without walking local ROS junctions."""

    for candidate in (SCRIPT_DIRECTORY, *SCRIPT_DIRECTORY.parents):
        if (candidate / "Packages").is_dir() and (candidate / "Scripts").is_dir():
            return candidate
    raise AcceptanceFailure("FAIL_PREFLIGHT", "repository root could not be located")


def parse_args(argv: Sequence[str] | None = None) -> argparse.Namespace:
    """Parse the bounded parent/worker surface."""

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--case", choices=tuple(protocol.CASES), required=True)
    parser.add_argument("--manual", action="store_true")
    parser.add_argument("--expected-head", required=True)
    parser.add_argument("--output-root", type=pathlib.Path, required=True)
    parser.add_argument("--unity-editor", type=pathlib.Path)
    parser.add_argument("--run-id")
    parser.add_argument("--bridge-port", type=int)
    parser.add_argument("--domain-id", type=int)
    parser.add_argument(
        "--preflight-only",
        action="store_true",
        help="Write preflight evidence without claiming a live PASS.",
    )
    parser.add_argument(
        "--manual-timeout-seconds",
        type=float,
        default=1800.0,
    )
    return parser.parse_args(argv)


def validate_arguments(args: argparse.Namespace) -> argparse.Namespace:
    """Reject contradictory modes and unsafe identifiers before I/O."""

    contract = protocol.require_case(args.case)
    protocol.require_head(args.expected_head)
    if bool(args.manual) is not contract.manual:
        raise protocol.ProtocolFailure(
            "FAIL_PREFLIGHT",
            "--manual must be present exactly for the two blocking manual cases",
        )
    if args.run_id is not None:
        protocol.require_run_id(args.run_id)
    if args.bridge_port is not None and not 1 <= args.bridge_port <= 65535:
        raise protocol.ProtocolFailure("FAIL_PREFLIGHT", "bridge port is outside 1..65535")
    if args.domain_id is not None and not 0 <= args.domain_id <= 232:
        raise protocol.ProtocolFailure("FAIL_PREFLIGHT", "domain ID is outside 0..232")
    if not isinstance(args.manual_timeout_seconds, (int, float)) or not 1 <= float(
        args.manual_timeout_seconds
    ) <= 7200:
        raise protocol.ProtocolFailure(
            "FAIL_PREFLIGHT", "manual timeout must be in [1, 7200] seconds"
        )
    return args


def git_head(repository: pathlib.Path) -> str:
    """Read the exact current Git commit."""

    try:
        completed = subprocess.run(
            ["git", "rev-parse", "HEAD"],
            cwd=repository,
            check=True,
            capture_output=True,
            text=True,
            timeout=30,
        )
    except (OSError, subprocess.SubprocessError) as exc:
        raise AcceptanceFailure("FAIL_PREFLIGHT", "Git HEAD could not be read") from exc
    return protocol.require_head(completed.stdout.strip())


def require_exact_head(repository: pathlib.Path, expected_head: str) -> str:
    """Reject a stale requested SHA even if its text is well formed."""

    expected = protocol.require_head(expected_head)
    actual = git_head(repository)
    if actual != expected:
        raise AcceptanceFailure(
            "FAIL_PREFLIGHT", f"current Git HEAD {actual} differs from expected {expected}"
        )
    return actual


def require_clean_tracked_tree(repository: pathlib.Path) -> None:
    """Require a clean tracked tree/index while ignoring operator-only files."""

    try:
        completed = subprocess.run(
            ["git", "status", "--porcelain=v1", "--untracked-files=no"],
            cwd=repository,
            check=True,
            capture_output=True,
            text=True,
            timeout=30,
        )
    except (OSError, subprocess.SubprocessError) as exc:
        raise AcceptanceFailure("FAIL_PREFLIGHT", "tracked Git status could not be read") from exc
    if completed.stdout.strip():
        raise AcceptanceFailure(
            "FAIL_PREFLIGHT", "live acceptance requires a clean tracked tree and index"
        )


def resolve_unity_editor(
    project: pathlib.Path,
    explicit_editor: pathlib.Path | None,
) -> UnityEditorIdentity:
    """Resolve the exact Unity version declared by the project."""

    version_file = pathlib.Path(project) / "ProjectSettings" / "ProjectVersion.txt"
    try:
        text = version_file.read_text(encoding="utf-8")
    except OSError as exc:
        raise LivePrerequisiteMissing(
            "NOT_RUN_UNITY_PROJECT_VERSION", "Unity project version file is unavailable"
        ) from exc
    match = re.search(r"(?m)^m_EditorVersion: ([^\r\n]+)$", text)
    if match is None or _UNITY_VERSION.fullmatch(match.group(1)) is None:
        raise LivePrerequisiteMissing(
            "NOT_RUN_UNITY_PROJECT_VERSION", "Unity project version is malformed"
        )
    version = match.group(1)
    editor = (
        pathlib.Path(explicit_editor)
        if explicit_editor is not None
        else pathlib.Path(r"C:\Program Files\Unity\Hub\Editor")
        / version
        / "Editor"
        / "Unity.exe"
    )
    try:
        editor = editor.resolve(strict=True)
    except OSError as exc:
        raise LivePrerequisiteMissing(
            "NOT_RUN_UNITY_EDITOR",
            f"Unity {version} executable is not installed at the selected path",
        ) from exc
    if not editor.is_file() or editor.name.lower() != "unity.exe":
        raise LivePrerequisiteMissing(
            "NOT_RUN_UNITY_EDITOR", "selected Unity executable is not Unity.exe"
        )
    return UnityEditorIdentity(editor, version)


def reserve_loopback_port(port: int | None = None) -> LoopbackPortReservation:
    """Hold an exclusive IPv4 loopback TCP port until actor handoff."""

    if port is not None and not 1 <= port <= 65535:
        raise AcceptanceFailure("FAIL_PREFLIGHT", "requested port is outside 1..65535")
    owned = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    try:
        if os.name == "nt":
            owned.setsockopt(socket.SOL_SOCKET, socket.SO_EXCLUSIVEADDRUSE, 1)
        else:
            owned.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 0)
        owned.bind(("127.0.0.1", 0 if port is None else port))
        host, selected = owned.getsockname()[:2]
        if host != "127.0.0.1" or not 1 <= int(selected) <= 65535:
            raise AcceptanceFailure(
                "FAIL_PREFLIGHT", "port reservation did not bind IPv4 loopback"
            )
        return LoopbackPortReservation(owned, host, int(selected))
    except Exception:
        owned.close()
        raise


def _read_json_object(path: pathlib.Path, label: str) -> Mapping[str, Any]:
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise AcceptanceFailure("FAIL_PREFLIGHT", f"{label} is unavailable or invalid") from exc
    if not isinstance(value, Mapping):
        raise AcceptanceFailure("FAIL_PREFLIGHT", f"{label} must be a JSON object")
    return value


def validate_package_manifests(repository: pathlib.Path) -> dict[str, Any]:
    """Prove the ROS-free Bridge dependency boundary from current manifests."""

    root = pathlib.Path(repository)
    sdk = _read_json_object(
        root / "Packages" / "dev.unity2foxglove.sdk" / "package.json",
        "SDK package manifest",
    )
    bridge = _read_json_object(
        root / "Packages" / "dev.unity2foxglove.ros2bridge" / "package.json",
        "Bridge package manifest",
    )
    if sdk.get("name") != "dev.unity2foxglove.sdk":
        raise AcceptanceFailure("FAIL_PREFLIGHT", "SDK package ID differs from authority")
    if bridge.get("name") != "dev.unity2foxglove.ros2bridge":
        raise AcceptanceFailure("FAIL_PREFLIGHT", "Bridge package ID differs from authority")
    dependencies = bridge.get("dependencies")
    if not isinstance(dependencies, Mapping):
        raise AcceptanceFailure("FAIL_PREFLIGHT", "Bridge dependencies must be an object")
    if "dev.unity2foxglove.sdk" not in dependencies:
        raise AcceptanceFailure("FAIL_PREFLIGHT", "Bridge does not depend on the SDK")
    forbidden = sorted(
        key
        for key in dependencies
        if key.startswith("dev.unity2foxglove.ros2forunity")
        or key.startswith("dev.unity2foxglove.ros2.")
    )
    if forbidden:
        raise AcceptanceFailure(
            "FAIL_PREFLIGHT", "Bridge manifest depends on R2FU/ROS runtime: " + ", ".join(forbidden)
        )
    return {
        "sdkPackage": str(sdk["name"]),
        "sdkVersion": str(sdk.get("version", "")),
        "bridgePackage": str(bridge["name"]),
        "bridgeVersion": str(bridge.get("version", "")),
        "bridgeDependencies": dict(dependencies),
    }


def sha256_file(path: pathlib.Path) -> str:
    digest = hashlib.sha256()
    with pathlib.Path(path).open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def validate_static_authority(repository: pathlib.Path) -> dict[str, Any]:
    """Lock tracked protocol, fixture, harness, and analyzer inputs."""

    root = pathlib.Path(repository)
    fixture = (
        root
        / "Tools"
        / "ros2_bridge"
        / "unity2foxglove_ros2_bridge"
        / "test"
        / "fixtures"
        / "u2r2_protocol_vectors.json"
    )
    bridge_source = (
        root
        / "Tools"
        / "ros2_bridge"
        / "unity2foxglove_ros2_bridge"
        / "src"
        / "unity2foxglove_ros2_bridge.cpp"
    )
    analyzer = (
        root
        / "Packages"
        / "dev.unity2foxglove.sdk"
        / "Editor"
        / "SourceGenerators"
        / "analyzers"
        / "dotnet"
        / "cs"
        / "FoxgloveLogSourceGenerator.dll"
    )
    for label, path in (
        ("U2R2 fixture", fixture),
        ("Bridge source", bridge_source),
        ("FoxRun analyzer", analyzer),
    ):
        if not path.is_file():
            raise LivePrerequisiteMissing(
                "NOT_RUN_TRACKED_AUTHORITY", f"{label} is absent: {path}"
            )
    return {
        "fixturePath": str(fixture.resolve()),
        "fixtureSha256": sha256_file(fixture),
        "bridgeSourcePath": str(bridge_source.resolve()),
        "bridgeSourceSha256": sha256_file(bridge_source),
        "analyzerPath": str(analyzer.resolve()),
        "analyzerSha256": sha256_file(analyzer),
        "interfaceType": protocol.INTERFACE_TYPE,
        "interfaceDigest": protocol.INTERFACE_DIGEST,
    }


def find_current_manual_marker(
    lines: Sequence[str],
    *,
    case_id: str,
    run_id: str,
    token: str,
    head: str,
) -> str:
    """Return only the exact current-run Unity completion marker."""

    scanned = 0
    for line in reversed(tuple(lines)):
        scanned += len(line.encode("utf-8", errors="replace"))
        if scanned > MAX_RESCUE_LOG_BYTES:
            break
        candidate = line.strip()
        if not candidate.startswith(protocol.MANUAL_COMPLETE_PREFIX + " "):
            continue
        try:
            protocol.parse_manual_completion_marker(
                candidate,
                case_id=case_id,
                run_id=run_id,
                token=token,
                head=head,
            )
        except protocol.ProtocolFailure:
            continue
        return candidate
    raise AcceptanceFailure(
        "FAIL_TERMINAL", "no exact current-run manual completion marker was found"
    )


def validate_cleanup_evidence(value: Mapping[str, Any]) -> None:
    """Require all owned resources to be absent after teardown."""

    expected = {
        "complete",
        "residualProcesses",
        "residualPorts",
        "residualOverlays",
        "residualTemporaryProjects",
    }
    if not isinstance(value, Mapping) or set(value) != expected:
        raise AcceptanceFailure("FAIL_CLEANUP", "cleanup evidence keys differ")
    if value["complete"] is not True:
        raise AcceptanceFailure("FAIL_CLEANUP", "cleanup did not complete")
    for key in expected - {"complete"}:
        if not isinstance(value[key], list) or value[key]:
            raise AcceptanceFailure("FAIL_CLEANUP", f"cleanup retained {key}")


def promote_build_to_live_summary(_build_summary: Mapping[str, Any]) -> None:
    """Reject the forbidden build-PASS to live-PASS conversion by construction."""

    raise AcceptanceFailure(
        "FAIL_EVIDENCE", "build/tooling evidence cannot be promoted to a live PASS"
    )


def write_json_atomic(path: pathlib.Path, value: Mapping[str, Any]) -> None:
    """Persist one JSON object by atomic replacement within its owned directory."""

    target = pathlib.Path(path)
    target.parent.mkdir(parents=True, exist_ok=True)
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


def persist_not_run(
    output: pathlib.Path,
    *,
    run_id: str,
    token: str,
    case_id: str,
    head: str,
    prerequisite: str,
) -> dict[str, Any]:
    """Persist an honest blocking result after prerequisite preflight."""

    root = pathlib.Path(output).resolve()
    result = protocol.make_not_run_summary(
        run_id=run_id,
        token=token,
        case_id=case_id,
        head=head,
        prerequisite=prerequisite,
        evidence_root=str(root),
    )
    write_json_atomic(root / "terminal-summary.json", result)
    (root / "terminal-marker.txt").write_text(
        protocol.format_terminal_line(result) + "\n", encoding="utf-8"
    )
    return result


def _new_run_identity(case_id: str, requested_run_id: str | None) -> tuple[str, str]:
    token = "p186h_" + secrets.token_hex(16)
    if requested_run_id is not None:
        return protocol.require_run_id(requested_run_id), token
    suffix = secrets.token_hex(6)
    case_slug = case_id.replace("manual-", "")[:28]
    return protocol.require_run_id(f"phase186h-{case_slug}-{suffix}"), token


def _owned_run_root(repository: pathlib.Path, requested: pathlib.Path, run_id: str) -> pathlib.Path:
    root = pathlib.Path(requested)
    if not root.is_absolute():
        root = repository / root
    root = root.resolve()
    phase_root = (repository / "build" / "phase186").resolve()
    if root != phase_root and phase_root not in root.parents:
        raise AcceptanceFailure(
            "FAIL_PREFLIGHT", "output root must stay below repository build/phase186"
        )
    run_root = root / run_id
    if run_root.exists() and any(run_root.iterdir()):
        raise AcceptanceFailure(
            "FAIL_PREFLIGHT", "owned run directory already exists and is not empty"
        )
    run_root.mkdir(parents=True, exist_ok=True)
    return run_root


def _preflight(
    repository: pathlib.Path,
    args: argparse.Namespace,
    run_root: pathlib.Path,
    run_id: str,
    token: str,
    bridge_port: int,
) -> dict[str, Any]:
    head = require_exact_head(repository, args.expected_head)
    require_clean_tracked_tree(repository)
    project = repository / "Unity2Foxglove"
    unity = resolve_unity_editor(project, args.unity_editor)
    packages = validate_package_manifests(repository)
    authority = validate_static_authority(repository)
    contract = protocol.require_case(args.case)
    row = protocol.require_row(contract.row_id) if contract.row_id else None
    domain_id = args.domain_id if args.domain_id is not None else (row.domain_id if row else 186)
    if contract.row_id is not None:
        config = protocol.make_run_config(
            repository=repository,
            project=project,
            output_root=run_root,
            run_id=run_id,
            token=token,
            case_id=contract.case_id,
            head=head,
            bridge_port=bridge_port,
            domain_id=domain_id,
        )
        protocol.validate_run_config(config, repository)
        write_json_atomic(run_root / "run-config.json", config)
    return {
        "schemaVersion": 1,
        "runId": run_id,
        "caseId": contract.case_id,
        "rowId": contract.row_id,
        "tokenHash": protocol.token_sha256(token),
        "head": head,
        "unity": {"path": str(unity.path), "version": unity.version},
        "bridgeEndpoint": {"host": "127.0.0.1", "port": bridge_port},
        "domainId": domain_id,
        "packages": packages,
        "authority": authority,
        "verdict": "PREFLIGHT PASS",
        "liveVerdict": "NOT CLAIMED",
        "createdAt": protocol.timestamp(),
    }


def main(argv: Sequence[str] | None = None) -> int:
    """Run preflight or stop honestly before unimplemented live execution."""

    args = validate_arguments(parse_args(argv))
    repository = repository_root()
    run_id, token = _new_run_identity(args.case, args.run_id)
    run_root: pathlib.Path | None = None
    try:
        run_root = _owned_run_root(repository, args.output_root, run_id)
        with reserve_loopback_port(args.bridge_port) as reservation:
            preflight = _preflight(
                repository,
                args,
                run_root,
                run_id,
                token,
                reservation.port,
            )
            write_json_atomic(run_root / "preflight.json", preflight)
        if args.preflight_only:
            print(
                "PHASE186_PREFLIGHT_PASS"
                + f" run={run_id} case={args.case} tokenHash={protocol.token_sha256(token)}"
                + f" head={args.expected_head}",
                flush=True,
            )
            return EXIT_PASS
        result = persist_not_run(
            run_root,
            run_id=run_id,
            token=token,
            case_id=args.case,
            head=args.expected_head,
            prerequisite="controlled Unity/sidecar live actor phase is not active",
        )
        print(protocol.format_terminal_line(result), flush=True)
        return EXIT_NOT_RUN
    except LivePrerequisiteMissing as exc:
        if run_root is None:
            print(str(exc), file=sys.stderr)
            return EXIT_NOT_RUN
        result = persist_not_run(
            run_root,
            run_id=run_id,
            token=token,
            case_id=args.case,
            head=args.expected_head,
            prerequisite=str(exc),
        )
        print(protocol.format_terminal_line(result), flush=True)
        return EXIT_NOT_RUN
    except protocol.ProtocolFailure as exc:
        print(str(exc), file=sys.stderr)
        return EXIT_FAIL


if __name__ == "__main__":
    raise SystemExit(main())
