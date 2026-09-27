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
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase186_bridge_acceptance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE

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
REPOSITORY_ROOT = SCRIPT_DIRECTORY.parents[2]
if str(REPOSITORY_ROOT) not in sys.path:
    sys.path.insert(0, str(REPOSITORY_ROOT))
if str(SCRIPT_DIRECTORY) not in sys.path:
    sys.path.insert(0, str(SCRIPT_DIRECTORY))

try:
    from Scripts.smoke.foxrun import phase186_bridge_acceptance_protocol as protocol
    from Scripts.smoke.foxrun import phase186_bridge_project as bridge_project
except ImportError:  # Direct script execution from outside the repository root.
    import phase186_bridge_acceptance_protocol as protocol
    import phase186_bridge_project as bridge_project


EXIT_PASS = 0
EXIT_FAIL = 1
EXIT_USAGE = 2
EXIT_NOT_RUN = 3
MAX_RESCUE_LOG_BYTES = 4 * 1024 * 1024
_UNITY_VERSION = re.compile(r"\A[0-9]+\.[0-9]+\.[0-9]+[a-z][0-9]+\Z")
UNITY_COMPOSITIONS = ("repository-all-providers", "bridge-only")
_ALL_PROVIDER_CASES = frozenset(
    {
        "frozen-v1",
        "fanout-fairness-health",
        "product-inspector",
        *protocol.MANUAL_CASE_IDS,
    }
)


class AcceptanceFailure(protocol.ProtocolFailure):
    """Stable coordinator failure."""


class LivePrerequisiteMissing(AcceptanceFailure):
    """A specifically named prerequisite is not provisioned."""


@dataclasses.dataclass(frozen=True)
class UnityEditorIdentity:
    """Exact Editor executable selected by the project version."""

    path: pathlib.Path
    version: str


@dataclasses.dataclass(frozen=True)
class InstalledUnityRunBinding:
    """Exact transient source owned by one Phase186 acceptance run."""

    path: pathlib.Path
    sha256: str


@dataclasses.dataclass
class LoopbackPortReservation:
    """One held IPv4 loopback socket reservation."""

    socket: socket.socket
    host: str
    port: int

    def close(self) -> None:
        """Release the owned acceptance resources."""
        self.socket.close()

    def __enter__(self) -> "LoopbackPortReservation":
        """Enter the managed acceptance context."""
        return self

    def __exit__(self, _type, _value, _traceback) -> None:
        """Exit the managed acceptance context."""
        self.close()


def repository_root() -> pathlib.Path:
    """Locate the repository without walking local ROS junctions."""

    for candidate in (SCRIPT_DIRECTORY, *SCRIPT_DIRECTORY.parents):
        if (candidate / "Packages").is_dir() and (candidate / "Scripts").is_dir():
            return candidate
    raise AcceptanceFailure("FAIL_PREFLIGHT", "repository root could not be located")


def parse_args(
    argv: Sequence[str] | None = None,
    *,
    expected_head_required: bool = True,
) -> argparse.Namespace:
    """Parse the bounded parent/worker surface."""

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--case", choices=tuple(protocol.CASES), required=True)
    parser.add_argument("--manual", action="store_true")
    parser.add_argument("--expected-head", required=expected_head_required)
    parser.add_argument("--output-root", type=pathlib.Path, required=True)
    parser.add_argument("--unity-editor", type=pathlib.Path)
    parser.add_argument("--run-id")
    parser.add_argument("--bridge-port", type=int)
    parser.add_argument("--foxglove-port", type=int)
    parser.add_argument("--domain-id", type=int)
    parser.add_argument("--runtime-row", choices=tuple(protocol.ROWS))
    parser.add_argument("--unity-composition", choices=UNITY_COMPOSITIONS)
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


def validate_arguments(
    args: argparse.Namespace,
    *,
    allow_missing_expected_head: bool = False,
) -> argparse.Namespace:
    """Reject contradictory modes and unsafe identifiers before I/O."""

    contract = protocol.require_case(args.case)
    expected_composition = unity_composition_for_case(contract.case_id)
    if args.unity_composition is None:
        args.unity_composition = expected_composition
    elif args.unity_composition != expected_composition:
        raise protocol.ProtocolFailure(
            "FAIL_PACKAGE_COMPOSITION",
            "Unity composition differs from the locked case authority",
        )
    if args.expected_head is None:
        if not allow_missing_expected_head or not bool(args.manual):
            raise protocol.ProtocolFailure(
                "FAIL_PREFLIGHT", "--expected-head is required"
            )
    else:
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
    if args.foxglove_port is not None and not 1 <= args.foxglove_port <= 65535:
        raise protocol.ProtocolFailure("FAIL_PREFLIGHT", "Foxglove port is outside 1..65535")
    if (
        args.bridge_port is not None
        and args.foxglove_port is not None
        and args.bridge_port == args.foxglove_port
    ):
        raise protocol.ProtocolFailure(
            "FAIL_PREFLIGHT", "Bridge and Foxglove ports must be distinct"
        )
    if contract.row_id is not None and args.runtime_row not in {None, contract.row_id}:
        raise protocol.ProtocolFailure(
            "FAIL_RUNTIME_SELECTION", "manual case runtime row differs from authority"
        )
    if (
        args.domain_id is not None
        and not 0 <= args.domain_id <= protocol.WINDOWS_SAFE_ROS_DOMAIN_ID_MAX
    ):
        raise protocol.ProtocolFailure(
            "FAIL_PREFLIGHT",
            "domain ID is outside 0.."
            + str(protocol.WINDOWS_SAFE_ROS_DOMAIN_ID_MAX),
        )
    if not isinstance(args.manual_timeout_seconds, (int, float)) or not 1 <= float(
        args.manual_timeout_seconds
    ) <= 7200:
        raise protocol.ProtocolFailure(
            "FAIL_PREFLIGHT", "manual timeout must be in [1, 7200] seconds"
        )
    return args


def unity_composition_for_case(case_id: str) -> str:
    """Handle unity composition for case for Phase186 acceptance."""
    protocol.require_case(case_id)
    return (
        "repository-all-providers"
        if case_id in _ALL_PROVIDER_CASES
        else "bridge-only"
    )


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
    """Read json object."""
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


def validate_unity_project_composition(
    repository: pathlib.Path,
    project: pathlib.Path,
    composition: str,
    run_id: str,
) -> dict[str, Any]:
    """Prove the exact Unity package composition used by this live case."""

    root = pathlib.Path(repository).resolve()
    selected = pathlib.Path(project).resolve()
    if composition == "bridge-only":
        expected = protocol.owned_unity_project_path(root, run_id)
        if selected != expected.resolve():
            raise AcceptanceFailure(
                "FAIL_PACKAGE_COMPOSITION",
                "Bridge-only project path differs from current-run ownership",
            )
        try:
            return dict(bridge_project.validate_bridge_only_manifest(selected))
        except bridge_project.BridgeOnlyProjectFailure as exc:
            raise AcceptanceFailure("FAIL_PACKAGE_COMPOSITION", str(exc)) from exc
    if composition != "repository-all-providers":
        raise AcceptanceFailure(
            "FAIL_PACKAGE_COMPOSITION", "unknown Unity package composition"
        )
    if selected != (root / "Unity2Foxglove").resolve():
        raise AcceptanceFailure(
            "FAIL_PACKAGE_COMPOSITION",
            "all-Providers case is not using the repository Unity project",
        )
    manifest = _read_json_object(
        selected / "Packages" / "manifest.json",
        "repository Unity manifest",
    )
    dependencies = manifest.get("dependencies")
    if not isinstance(dependencies, Mapping):
        raise AcceptanceFailure(
            "FAIL_PACKAGE_COMPOSITION", "repository Unity dependencies are malformed"
        )
    required = {
        "dev.unity2foxglove.sdk",
        "dev.unity2foxglove.ros2bridge",
        "dev.unity2foxglove.ros2forunity",
    }
    if not required.issubset(dependencies):
        raise AcceptanceFailure(
            "FAIL_PACKAGE_COMPOSITION",
            "all-Providers Unity project lacks one required Provider package",
        )
    return {
        "composition": "all-providers",
        "productPackages": sorted(
            key
            for key in dependencies
            if key.startswith("dev.unity2foxglove.")
        ),
        "manifest": str((selected / "Packages" / "manifest.json").resolve()),
        "manifestSha256": sha256_file(selected / "Packages" / "manifest.json"),
    }


__all__ = [name for name in globals() if not name.startswith("__")]
