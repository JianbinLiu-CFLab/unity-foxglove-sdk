#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Owned Windows coordinator for Phase184-H Foxglove Desktop acceptance."""

from __future__ import annotations

import argparse
import contextlib
import ctypes
import dataclasses
import datetime as dt
import hashlib
import json
import math
import ntpath
import os
import pathlib
import platform
import re
import socket
import stat
import subprocess
import sys
import time
import uuid
from collections.abc import Mapping, Sequence
from ctypes import wintypes
from typing import Any, Callable
from urllib.parse import quote, urlencode

SCRIPT_PATH = pathlib.Path(__file__).resolve()
REPOSITORY_ROOT = SCRIPT_PATH.parents[3]
if str(REPOSITORY_ROOT) not in sys.path:
    sys.path.insert(0, str(REPOSITORY_ROOT))

from Scripts.smoke.foxrun import phase184_foxglove_cli_install as cli_install
from Scripts.smoke.foxrun import phase184_foxglove_desktop_live_protocol as protocol
from Scripts.smoke.foxrun import phase184_profile_acceptance_protocol as base_protocol
from Scripts.smoke.foxrun import phase184_windows_job_owner as job_owner


BASE_CASE = "foxglove-profile"
DATA_SOURCE = "foxglove-websocket"
LOOPBACK_HOST = "127.0.0.1"
DEFAULT_CLI_RECEIPT = (
    REPOSITORY_ROOT
    / "build"
    / "phase184"
    / "tooling"
    / "foxglove-cli-install-receipt.json"
)

SUMMARY_SCHEMA_VERSION = 1
DESKTOP_LIVE_SUMMARY_FILENAME = "desktop-live-summary.json"
MAX_SUMMARY_BYTES = protocol.MAX_RECEIPT_BYTES
MAX_SUMMARY_STRING_CHARACTERS = 32_767
MAX_SUMMARY_IDENTITIES = 128
MAX_COMMAND_LINE_CHARACTERS = 32_767
MAX_COMMAND_ARGUMENTS = 16
MAX_RUN_CONFIG_BYTES = 1024 * 1024
MAX_ACTOR_READY_BYTES = 64 * 1024
MAX_BASE_SUMMARY_BYTES = 1024 * 1024
MAX_UNITY_LOG_BYTES = 64 * 1024 * 1024
MAX_COORDINATOR_LOG_BYTES = 16 * 1024 * 1024
RUN_CONFIG_TIMEOUT_SECONDS = 90.0
BASE_READINESS_TIMEOUT_SECONDS = 900.0
CONNECTION_TIMEOUT_SECONDS = 120.0
BASE_EXIT_TIMEOUT_SECONDS = 180.0
DESKTOP_CLOSE_GRACE_SECONDS = 10.0
IDENTITY_EXIT_TIMEOUT_SECONDS = 15.0
POLL_SECONDS = 0.1

_SAFE_RUN_ID = re.compile(r"\Aphase184g-[A-Za-z0-9][A-Za-z0-9._-]{7,79}\Z")
_SAFE_TIMESTAMP = re.compile(r"\A[0-9]{8}-[0-9]{6}\Z")
_SAFE_NONCE = re.compile(r"\A[0-9A-Fa-f]{10}\Z")
_UPPER_SHA256 = re.compile(r"\A[0-9A-F]{64}\Z")
_LOWER_GIT_OBJECT = re.compile(r"\A[0-9a-f]{40}(?:[0-9a-f]{24})?\Z")
_DESKTOP_FILE_VERSION = re.compile(
    r"\A(?:0|[1-9][0-9]{0,9})"
    r"(?:\.(?:0|[1-9][0-9]{0,9})){3}\Z"
)
_RAW_TOKEN = re.compile(r"p184g_[A-Za-z0-9]{12,64}")
_CONTEXT_MARKER = re.compile(
    r"\APHASE184G_CONTEXT_READY "
    r"case=(?P<case>[^\s=]+) "
    r"token=(?P<token>[^\s=]+) "
    r"tokenDigest=(?P<digest>[^\s=]+)\Z"
)
_REDACTED_CONTEXT_MARKER = re.compile(
    r"\APHASE184G_CONTEXT_READY "
    r"case=(?P<case>[^\s=]+) "
    r"token=<redacted> "
    r"tokenDigest=(?P<digest>[0-9A-Fa-f]{12})\Z"
)
_REDACTED_TRANSPORT_MARKER = re.compile(
    rf"\A{re.escape(protocol.TRANSPORT_CLIENTS_MARKER)} "
    r"case=(?P<case>[^\s=]+) "
    r"token=<redacted> "
    r"active=(?P<active>(?:0|[1-9][0-9]*)) "
    r"accepted=(?P<accepted>(?:0|[1-9][0-9]*))\Z"
)

COORDINATOR_FAILURE_CODES = frozenset(
    {
        protocol.FAIL_CLI_PROVENANCE,
        protocol.FAIL_DESKTOP_PREFLIGHT,
        protocol.FAIL_DESKTOP_START,
        protocol.FAIL_DESKTOP_IDENTITY,
        protocol.FAIL_DESKTOP_CONNECTION,
        protocol.FAIL_FOXRUN_CHILD,
        protocol.FAIL_EVIDENCE,
        protocol.FAIL_CLEANUP,
    }
)

_SAFE_ENVIRONMENT_NAMES = frozenset(
    {
        "ALLUSERSPROFILE",
        "APPDATA",
        "COMMONPROGRAMFILES",
        "COMMONPROGRAMFILES(X86)",
        "COMMONPROGRAMW6432",
        "COMSPEC",
        "HOMEDRIVE",
        "HOMEPATH",
        "LOCALAPPDATA",
        "NUMBER_OF_PROCESSORS",
        "OS",
        "PATH",
        "PATHEXT",
        "PROCESSOR_ARCHITECTURE",
        "PROCESSOR_IDENTIFIER",
        "PROCESSOR_LEVEL",
        "PROCESSOR_REVISION",
        "PROGRAMDATA",
        "PROGRAMFILES",
        "PROGRAMFILES(X86)",
        "PROGRAMW6432",
        "PSMODULEPATH",
        "PUBLIC",
        "SYSTEMDRIVE",
        "SYSTEMROOT",
        "TEMP",
        "TMP",
        "USERPROFILE",
        "WINDIR",
    }
)

_TOP_KEYS = frozenset(
    {
        "schemaVersion",
        "identity",
        "cli",
        "desktop",
        "connection",
        "foxrun",
        "cleanup",
        "verdict",
    }
)
_IDENTITY_KEYS = frozenset(
    {
        "runId",
        "baseCase",
        "tokenSha256",
        "repositoryHead",
        "windowsVersion",
        "unityVersion",
    }
)
_CLI_KEYS = frozenset(
    {
        "architecture",
        "assetUrl",
        "installedPath",
        "installedSha256",
        "installedVersion",
        "receiptPath",
        "releaseTag",
    }
)
_DESKTOP_KEYS = frozenset(
    {
        "executable",
        "fileVersion",
        "sha256",
        "uriHandler",
        "dataSource",
        "deeplink",
        "rootIdentity",
        "ownedMemberIdentities",
        "externalIdentities",
        "jobOwned",
    }
)
_CONNECTION_KEYS = frozenset(
    {
        "host",
        "port",
        "portPreflight",
        "contextMarker",
        "initialMarker",
        "firstMarker",
        "secondMarker",
        "contextObservedAt",
        "initialObservedAt",
        "desktopIdentityCapturedAt",
        "firstObservedAt",
        "barrierWrittenAt",
        "secondObservedAt",
        "barrierPath",
        "barrierDigest",
        "barrierRemoved",
    }
)
_FOXRUN_KEYS = frozenset(
    {
        "baseSummaryPath",
        "baseVerdict",
        "channelEncodings",
        "deliveryObserved",
        "remoteApplied",
        "sameOriginDropped",
        "laterLocalPublished",
    }
)
_CLEANUP_KEYS = frozenset(
    {
        "jobClosed",
        "processes",
        "port",
        "barrier",
        "files",
        "junctions",
        "subst",
        "gracefulOwnedIdentities",
        "forcedOwnedIdentities",
        "exitedOwnedIdentities",
        "residualOwnedIdentities",
    }
)
_PROCESS_IDENTITY_KEYS = frozenset(
    {"pid", "creationTime100ns", "executable"}
)


def _failure(code: str, message: object) -> protocol.AcceptanceFailure:
    """Handle the failure step."""

    if code not in COORDINATOR_FAILURE_CODES:
        raise ValueError("Unknown Desktop-live coordinator failure code.")
    return protocol.AcceptanceFailure(code, message)


def _raise(code: str, message: object) -> None:
    """Handle the raise step."""

    raise _failure(code, message)


def parse_args(argv: Sequence[str] | None = None) -> argparse.Namespace:
    """Parse only the focused Desktop-live coordinator surface."""

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--unity-editor", required=True, type=pathlib.Path)
    parser.add_argument("--foxglove-cli", required=True, type=pathlib.Path)
    parser.add_argument(
        "--desktop-executable",
        required=True,
        type=pathlib.Path,
    )
    parser.add_argument(
        "--cli-receipt",
        type=pathlib.Path,
        default=DEFAULT_CLI_RECEIPT,
    )
    parser.add_argument("--foxglove-port", type=int)
    parser.add_argument("--run-id")
    return parser.parse_args(argv)


def validate_run_id(value: object) -> str:
    """Validate run id."""

    if not isinstance(value, str) or _SAFE_RUN_ID.fullmatch(value) is None:
        _raise(
            protocol.FAIL_DESKTOP_PREFLIGHT,
            "Desktop-live run id is unsafe or malformed.",
        )
    return value


def generate_run_id(*, timestamp: object, nonce: object) -> str:
    """Create the same safe phase184g-* identity family as the base runner."""

    if (
        not isinstance(timestamp, str)
        or _SAFE_TIMESTAMP.fullmatch(timestamp) is None
        or not isinstance(nonce, str)
        or _SAFE_NONCE.fullmatch(nonce) is None
    ):
        _raise(
            protocol.FAIL_DESKTOP_PREFLIGHT,
            "Desktop-live run identity inputs are malformed.",
        )
    return validate_run_id(f"phase184g-{timestamp}-{nonce.casefold()}")


def _validate_port(value: object) -> int:
    """Validate port."""

    if (
        isinstance(value, bool)
        or not isinstance(value, int)
        or value < 1
        or value > 65535
    ):
        _raise(
            protocol.FAIL_DESKTOP_PREFLIGHT,
            "Foxglove port must be an integer in 1..65535.",
        )
    return value


def _absolute_windows_file(
    value: object,
    label: str,
    *,
    is_file: Callable[[pathlib.Path], bool],
) -> pathlib.Path:
    """Handle the absolute windows file step."""

    try:
        text = os.fspath(value)
        protocol.windows_path_key(text, label=label)
        path = pathlib.Path(text)
    except (TypeError, ValueError, protocol.AcceptanceFailure):
        _raise(
            protocol.FAIL_DESKTOP_PREFLIGHT,
            f"{label} must be one absolute Windows path.",
        )
    try:
        available = is_file(path)
    except Exception:
        available = False
    if available is not True:
        _raise(
            protocol.FAIL_DESKTOP_PREFLIGHT,
            f"{label} must select one existing regular file.",
        )
    return path


def validate_arguments(
    args: argparse.Namespace,
    *,
    platform_name: str | None = None,
    is_file: Callable[[pathlib.Path], bool] | None = None,
) -> argparse.Namespace:
    """Fail closed before any process, network, registry, or output mutation."""

    selected_platform = os.name if platform_name is None else platform_name
    if selected_platform != "nt":
        _raise(
            protocol.FAIL_DESKTOP_PREFLIGHT,
            "Foxglove Desktop-live acceptance requires Windows.",
        )
    file_probe = pathlib.Path.is_file if is_file is None else is_file
    for field, label in (
        ("unity_editor", "Unity Editor"),
        ("foxglove_cli", "Foxglove CLI"),
        ("desktop_executable", "Foxglove Desktop executable"),
        ("cli_receipt", "Foxglove CLI receipt"),
    ):
        setattr(
            args,
            field,
            _absolute_windows_file(
                getattr(args, field, None),
                label,
                is_file=file_probe,
            ),
        )
    if args.run_id is not None:
        args.run_id = validate_run_id(args.run_id)
    if args.foxglove_port is not None:
        args.foxglove_port = _validate_port(args.foxglove_port)
    return args


def build_deeplink(port: object) -> str:
    """Build the fixed local Foxglove websocket deep link without cloud state."""

    selected_port = _validate_port(port)
    query = urlencode(
        (
            ("ds", DATA_SOURCE),
            (
                "ds.url",
                f"ws://{LOOPBACK_HOST}:{selected_port}/",
            ),
        ),
        quote_via=quote,
        safe="",
    )
    return f"foxglove://open?{query}"


def parse_windows_command_line(command: object) -> tuple[str, ...]:
    """Parse a bounded Windows command line using CommandLineToArgvW rules."""

    if (
        not isinstance(command, str)
        or not command
        or len(command) > MAX_COMMAND_LINE_CHARACTERS
        or "\x00" in command
        or "\r" in command
        or "\n" in command
    ):
        _raise(
            protocol.FAIL_DESKTOP_PREFLIGHT,
            "Foxglove URI-handler command is malformed.",
        )

    arguments: list[str] = []
    index = 0
    length = len(command)
    while True:
        while index < length and command[index] in " \t":
            index += 1
        if index >= length:
            break
        if len(arguments) >= MAX_COMMAND_ARGUMENTS:
            _raise(
                protocol.FAIL_DESKTOP_PREFLIGHT,
                "Foxglove URI-handler command has too many arguments.",
            )

        value: list[str] = []
        quoted = False
        while index < length:
            character = command[index]
            if character in " \t" and not quoted:
                break
            if character == "\\":
                start = index
                while index < length and command[index] == "\\":
                    index += 1
                slash_count = index - start
                if index < length and command[index] == '"':
                    value.extend("\\" for _ in range(slash_count // 2))
                    if slash_count % 2:
                        value.append('"')
                        index += 1
                    else:
                        if (
                            quoted
                            and index + 1 < length
                            and command[index + 1] == '"'
                        ):
                            value.append('"')
                            index += 2
                        else:
                            quoted = not quoted
                            index += 1
                else:
                    value.extend("\\" for _ in range(slash_count))
                continue
            if character == '"':
                if (
                    quoted
                    and index + 1 < length
                    and command[index + 1] == '"'
                ):
                    value.append('"')
                    index += 2
                else:
                    quoted = not quoted
                    index += 1
                continue
            value.append(character)
            index += 1
        if quoted:
            _raise(
                protocol.FAIL_DESKTOP_PREFLIGHT,
                "Foxglove URI-handler command has an unterminated quote.",
            )
        arguments.append("".join(value))
        while index < length and command[index] in " \t":
            index += 1
    if not arguments:
        _raise(
            protocol.FAIL_DESKTOP_PREFLIGHT,
            "Foxglove URI-handler command has no executable.",
        )
    return tuple(arguments)


def validate_uri_handler(
    command: object,
    desktop_executable: os.PathLike[str] | str,
    *,
    parser: Callable[[object], tuple[str, ...]] | None = None,
) -> str:
    """Require the merged HKCR handler to be exact-executable plus exact %1."""

    selected_parser = parse_windows_command_line if parser is None else parser
    try:
        arguments = tuple(selected_parser(command))
    except protocol.AcceptanceFailure:
        raise
    except Exception:
        _raise(
            protocol.FAIL_DESKTOP_PREFLIGHT,
            "Foxglove URI-handler command could not be parsed.",
        )
    try:
        same_executable = (
            len(arguments) == 2
            and protocol.windows_paths_equal(
                arguments[0],
                desktop_executable,
            )
        )
    except (TypeError, ValueError, protocol.AcceptanceFailure):
        same_executable = False
    if not same_executable or arguments[1] != "%1":
        _raise(
            protocol.FAIL_DESKTOP_PREFLIGHT,
            "Foxglove URI handler must be the selected executable plus exactly %1.",
        )
    if not isinstance(command, str):
        _raise(
            protocol.FAIL_DESKTOP_PREFLIGHT,
            "Foxglove URI-handler command is malformed.",
        )
    return command


def build_clean_environment(source: Mapping[str, str]) -> dict[str, str]:
    """Copy only bounded host basics; never forward tokens, credentials, or ROS."""

    if not isinstance(source, Mapping):
        _raise(
            protocol.FAIL_DESKTOP_PREFLIGHT,
            "Coordinator environment source is invalid.",
        )
    result: dict[str, str] = {}
    for key, value in source.items():
        if (
            not isinstance(key, str)
            or not isinstance(value, str)
            or key.upper() not in _SAFE_ENVIRONMENT_NAMES
            or not key
            or "=" in key
            or "\x00" in key
            or "\x00" in value
            or "\r" in key
            or "\n" in key
            or "\r" in value
            or "\n" in value
        ):
            continue
        result[key] = value
    return dict(sorted(result.items(), key=lambda item: item[0].casefold()))


def process_identity_document(
    identity: job_owner.ProcessIdentity,
) -> dict[str, object]:
    """Reduce one identity to the only three approved summary fields."""

    if not isinstance(identity, job_owner.ProcessIdentity):
        _raise(
            protocol.FAIL_EVIDENCE,
            "Process identity evidence is invalid.",
        )
    return {
        "pid": identity.pid,
        "creationTime100ns": identity.creation_time_100ns,
        "executable": identity.executable,
    }


def _process_identity_key(
    identity: job_owner.ProcessIdentity,
) -> tuple[int, int, str]:
    """Handle the process identity key step."""

    if not isinstance(identity, job_owner.ProcessIdentity):
        _raise(
            protocol.FAIL_EVIDENCE,
            "Process identity evidence is invalid.",
        )
    return (
        identity.pid,
        identity.creation_time_100ns,
        protocol.windows_path_key(identity.executable),
    )


def _capture_desktop_executable(
    lease: Any,
    *,
    failure_code: str,
    expected: cli_install.ExecutableSnapshot | None = None,
) -> cli_install.ExecutableSnapshot:
    """Capture desktop executable."""

    try:
        snapshot = lease.snapshot()
        path_identity = lease.path_identity()
    except BaseException as exc:
        if isinstance(exc, (KeyboardInterrupt, SystemExit)):
            raise
        _raise(
            failure_code,
            "Foxglove Desktop executable identity could not be verified.",
        )
    if (
        not isinstance(snapshot, cli_install.ExecutableSnapshot)
        or not isinstance(
            path_identity,
            cli_install.ExecutableFileIdentity,
        )
        or snapshot.identity != path_identity
        or (expected is not None and snapshot != expected)
    ):
        _raise(
            failure_code,
            "Foxglove Desktop executable changed during acceptance.",
        )
    return snapshot


def _release_executable_lease(
    manager: Any,
    active_exception: BaseException | None = None,
) -> None:
    """Handle the release executable lease step."""

    if active_exception is None:
        result = manager.__exit__(None, None, None)
    else:
        result = manager.__exit__(
            type(active_exception),
            active_exception,
            active_exception.__traceback__,
        )
    if result not in (None, False):
        _raise(
            protocol.FAIL_DESKTOP_IDENTITY,
            "Foxglove Desktop executable lease suppressed a failure.",
        )


def _exact_mapping(
    value: object,
    expected_keys: frozenset[str],
    label: str,
) -> Mapping[str, Any]:
    """Handle the exact mapping step."""

    if not isinstance(value, Mapping) or frozenset(value) != expected_keys:
        _raise(
            protocol.FAIL_EVIDENCE,
            f"{label} keys do not match the Desktop-live schema.",
        )
    return value


def _bounded_string(
    value: object,
    label: str,
    *,
    allow_none: bool,
) -> str | None:
    """Handle the bounded string step."""

    if value is None and allow_none:
        return None
    if (
        not isinstance(value, str)
        or not value
        or len(value) > MAX_SUMMARY_STRING_CHARACTERS
        or "\x00" in value
        or "\r" in value
        or "\n" in value
    ):
        _raise(protocol.FAIL_EVIDENCE, f"{label} is invalid.")
    return value


def _bool(value: object, label: str) -> bool:
    """Handle the bool step."""

    if not isinstance(value, bool):
        _raise(protocol.FAIL_EVIDENCE, f"{label} must be boolean.")
    return value


def _time_value(value: object, label: str, *, allow_none: bool) -> float | None:
    """Handle the time value step."""

    if value is None and allow_none:
        return None
    if (
        isinstance(value, bool)
        or not isinstance(value, (int, float))
        or not math.isfinite(float(value))
        or float(value) < 0
    ):
        _raise(protocol.FAIL_EVIDENCE, f"{label} is invalid.")
    return float(value)


def _identity_document(
    value: object,
    label: str,
    *,
    allow_none: bool,
) -> Mapping[str, Any] | None:
    """Handle the identity document step."""

    if value is None and allow_none:
        return None
    document = _exact_mapping(
        value,
        _PROCESS_IDENTITY_KEYS,
        label,
    )
    pid = document["pid"]
    creation = document["creationTime100ns"]
    if (
        isinstance(pid, bool)
        or not isinstance(pid, int)
        or pid <= 0
        or isinstance(creation, bool)
        or not isinstance(creation, int)
        or creation <= 0
    ):
        _raise(protocol.FAIL_EVIDENCE, f"{label} numeric identity is invalid.")
    executable = _bounded_string(
        document["executable"],
        f"{label}.executable",
        allow_none=False,
    )
    try:
        protocol.windows_path_key(executable)
    except protocol.AcceptanceFailure:
        _raise(protocol.FAIL_EVIDENCE, f"{label}.executable is invalid.")
    return document


def _identity_list(value: object, label: str) -> list[Mapping[str, Any]]:
    """Handle the identity list step."""

    if (
        not isinstance(value, list)
        or len(value) > MAX_SUMMARY_IDENTITIES
    ):
        _raise(protocol.FAIL_EVIDENCE, f"{label} is invalid.")
    result: list[Mapping[str, Any]] = []
    seen: set[tuple[object, object, object]] = set()
    for index, item in enumerate(value):
        document = _identity_document(
            item,
            f"{label}[{index}]",
            allow_none=False,
        )
        assert document is not None
        key = (
            document["pid"],
            document["creationTime100ns"],
            protocol.windows_path_key(str(document["executable"])),
        )
        if key in seen:
            _raise(protocol.FAIL_EVIDENCE, f"{label} contains a duplicate.")
        seen.add(key)
        result.append(document)
    return result


def _identity_evidence_key(
    document: Mapping[str, Any],
) -> tuple[int, int, str]:
    """Handle the identity evidence key step."""

    return (
        int(document["pid"]),
        int(document["creationTime100ns"]),
        protocol.windows_path_key(str(document["executable"])),
    )


def _expected_barrier_digest(run_id: str, token_digest: str) -> str:
    """Handle the expected barrier digest step."""

    serialized = (
        json.dumps(
            {
                "acceptedClients": 1,
                "runId": run_id,
                "schemaVersion": (
                    protocol.DESKTOP_CLIENT_BARRIER_SCHEMA_VERSION
                ),
                "state": protocol.DESKTOP_CLIENT_BARRIER_STATE,
                "tokenDigest": token_digest,
            },
            allow_nan=False,
            ensure_ascii=True,
            separators=(",", ":"),
            sort_keys=True,
        )
        + "\n"
    ).encode("utf-8")
    return hashlib.sha256(serialized).hexdigest().upper()


def _validate_marker_excerpt(
    value: object,
    label: str,
    *,
    allow_none: bool,
) -> str | None:
    """Validate marker excerpt."""

    text = _bounded_string(value, label, allow_none=allow_none)
    if text is not None and "token=<redacted>" not in text:
        _raise(protocol.FAIL_EVIDENCE, f"{label} is not token-redacted.")
    return text



__all__ = [name for name in globals() if not name.startswith("__")]
