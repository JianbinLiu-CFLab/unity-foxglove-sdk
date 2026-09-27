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
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase184_profile_acceptance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE

import argparse
import asyncio
import base64
import contextlib
import ctypes
import datetime as dt
import hashlib
import json
import math
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


__all__ = [name for name in globals() if not name.startswith("__")]
