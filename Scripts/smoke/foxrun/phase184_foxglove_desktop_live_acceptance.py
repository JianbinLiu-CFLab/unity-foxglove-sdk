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
MAX_BASE_SUMMARY_BYTES = 1024 * 1024
MAX_UNITY_LOG_BYTES = 64 * 1024 * 1024
MAX_COORDINATOR_LOG_BYTES = 16 * 1024 * 1024
RUN_CONFIG_TIMEOUT_SECONDS = 90.0
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
    if code not in COORDINATOR_FAILURE_CODES:
        raise ValueError("Unknown Desktop-live coordinator failure code.")
    return protocol.AcceptanceFailure(code, message)


def _raise(code: str, message: object) -> None:
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
    if not isinstance(value, bool):
        _raise(protocol.FAIL_EVIDENCE, f"{label} must be boolean.")
    return value


def _time_value(value: object, label: str, *, allow_none: bool) -> float | None:
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
    return (
        int(document["pid"]),
        int(document["creationTime100ns"]),
        protocol.windows_path_key(str(document["executable"])),
    )


def _expected_barrier_digest(run_id: str, token_digest: str) -> str:
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
    text = _bounded_string(value, label, allow_none=allow_none)
    if text is not None and "token=<redacted>" not in text:
        _raise(protocol.FAIL_EVIDENCE, f"{label} is not token-redacted.")
    return text


def validate_desktop_live_summary(
    summary: Mapping[str, Any],
) -> Mapping[str, Any]:
    """Validate the bounded exact wrapper schema and all PASS implications."""

    document = _exact_mapping(summary, _TOP_KEYS, "summary")
    try:
        encoded = json.dumps(
            dict(document),
            allow_nan=False,
            ensure_ascii=True,
            separators=(",", ":"),
            sort_keys=True,
        ).encode("utf-8")
    except (TypeError, ValueError, RecursionError):
        _raise(protocol.FAIL_EVIDENCE, "Desktop-live summary is not JSON-safe.")
    if len(encoded) > MAX_SUMMARY_BYTES:
        _raise(protocol.FAIL_EVIDENCE, "Desktop-live summary exceeds its bound.")
    if _RAW_TOKEN.search(encoded.decode("ascii")) is not None:
        _raise(protocol.FAIL_EVIDENCE, "Desktop-live summary contains a raw token.")
    if document["schemaVersion"] != SUMMARY_SCHEMA_VERSION:
        _raise(protocol.FAIL_EVIDENCE, "Desktop-live summary schemaVersion is unsupported.")

    verdict = document["verdict"]
    if verdict != "PASS" and verdict not in COORDINATOR_FAILURE_CODES:
        _raise(protocol.FAIL_EVIDENCE, "Desktop-live verdict is not a stable terminal code.")
    passing = verdict == "PASS"

    identity = _exact_mapping(document["identity"], _IDENTITY_KEYS, "identity")
    validate_run_id(identity["runId"])
    if identity["baseCase"] != BASE_CASE:
        _raise(protocol.FAIL_EVIDENCE, "identity.baseCase drifted.")
    token_digest = identity["tokenSha256"]
    if token_digest is not None and (
        not isinstance(token_digest, str)
        or _UPPER_SHA256.fullmatch(token_digest) is None
    ):
        _raise(protocol.FAIL_EVIDENCE, "identity.tokenSha256 is invalid.")
    head = identity["repositoryHead"]
    if head is not None and (
        not isinstance(head, str)
        or _LOWER_GIT_OBJECT.fullmatch(head) is None
    ):
        _raise(protocol.FAIL_EVIDENCE, "identity.repositoryHead is invalid.")
    for key in ("windowsVersion", "unityVersion"):
        _bounded_string(
            identity[key],
            f"identity.{key}",
            allow_none=not passing,
        )

    cli = _exact_mapping(document["cli"], _CLI_KEYS, "cli")
    for key in _CLI_KEYS:
        _bounded_string(cli[key], f"cli.{key}", allow_none=not passing)
    if cli["installedSha256"] is not None and (
        not isinstance(cli["installedSha256"], str)
        or _UPPER_SHA256.fullmatch(cli["installedSha256"]) is None
    ):
        _raise(protocol.FAIL_EVIDENCE, "cli.installedSha256 is invalid.")
    if passing:
        try:
            cli_install.VerifiedCliIdentity(
                installed_path=str(cli["installedPath"]),
                installed_version=str(cli["installedVersion"]),
                installed_sha256=str(cli["installedSha256"]),
                release_tag=str(cli["releaseTag"]),
                asset_url=str(cli["assetUrl"]),
                architecture=str(cli["architecture"]),
                receipt_path=str(cli["receiptPath"]),
            )
            distinct_cli_paths = not protocol.windows_paths_equal(
                str(cli["installedPath"]),
                str(cli["receiptPath"]),
            )
        except Exception:
            distinct_cli_paths = False
        if not distinct_cli_paths:
            _raise(
                protocol.FAIL_EVIDENCE,
                "CLI identity fields are not one coherent verified install.",
            )

    desktop = _exact_mapping(document["desktop"], _DESKTOP_KEYS, "desktop")
    for key in (
        "executable",
        "fileVersion",
        "sha256",
        "uriHandler",
        "dataSource",
        "deeplink",
    ):
        _bounded_string(
            desktop[key],
            f"desktop.{key}",
            allow_none=not passing,
        )
    if desktop["sha256"] is not None and (
        not isinstance(desktop["sha256"], str)
        or _UPPER_SHA256.fullmatch(desktop["sha256"]) is None
    ):
        _raise(protocol.FAIL_EVIDENCE, "desktop.sha256 is invalid.")
    if desktop["fileVersion"] is not None and (
        not isinstance(desktop["fileVersion"], str)
        or _DESKTOP_FILE_VERSION.fullmatch(desktop["fileVersion"]) is None
    ):
        _raise(protocol.FAIL_EVIDENCE, "desktop.fileVersion is invalid.")
    if desktop["dataSource"] is not None and desktop["dataSource"] != DATA_SOURCE:
        _raise(protocol.FAIL_EVIDENCE, "desktop.dataSource drifted.")
    root_identity = _identity_document(
        desktop["rootIdentity"],
        "desktop.rootIdentity",
        allow_none=not passing,
    )
    owned = _identity_list(
        desktop["ownedMemberIdentities"],
        "desktop.ownedMemberIdentities",
    )
    external = _identity_list(
        desktop["externalIdentities"],
        "desktop.externalIdentities",
    )
    job_owned = _bool(desktop["jobOwned"], "desktop.jobOwned")

    connection = _exact_mapping(
        document["connection"],
        _CONNECTION_KEYS,
        "connection",
    )
    if connection["host"] != LOOPBACK_HOST:
        _raise(protocol.FAIL_EVIDENCE, "connection.host must be exact loopback.")
    if connection["port"] is None:
        if passing:
            _raise(protocol.FAIL_EVIDENCE, "connection.port is unavailable.")
    else:
        _validate_port(connection["port"])
    port_preflight = _bool(
        connection["portPreflight"],
        "connection.portPreflight",
    )
    marker_excerpts: dict[str, str | None] = {}
    for key in (
        "contextMarker",
        "initialMarker",
        "firstMarker",
        "secondMarker",
    ):
        marker_excerpts[key] = _validate_marker_excerpt(
            connection[key],
            f"connection.{key}",
            allow_none=not passing,
        )
    times = {
        key: _time_value(
            connection[key],
            f"connection.{key}",
            allow_none=not passing,
        )
        for key in (
            "contextObservedAt",
            "initialObservedAt",
            "desktopIdentityCapturedAt",
            "firstObservedAt",
            "barrierWrittenAt",
            "secondObservedAt",
        )
    }
    barrier_path = _bounded_string(
        connection["barrierPath"],
        "connection.barrierPath",
        allow_none=False,
    )
    barrier_digest = connection["barrierDigest"]
    if barrier_digest is not None and (
        not isinstance(barrier_digest, str)
        or _UPPER_SHA256.fullmatch(barrier_digest) is None
    ):
        _raise(protocol.FAIL_EVIDENCE, "connection.barrierDigest is invalid.")
    barrier_removed = _bool(
        connection["barrierRemoved"],
        "connection.barrierRemoved",
    )

    foxrun = _exact_mapping(document["foxrun"], _FOXRUN_KEYS, "foxrun")
    _bounded_string(
        foxrun["baseSummaryPath"],
        "foxrun.baseSummaryPath",
        allow_none=False,
    )
    if foxrun["baseVerdict"] not in (None, "PASS") and not (
        isinstance(foxrun["baseVerdict"], str)
        and re.fullmatch(r"(?:FAIL|BLOCKED)_[A-Z0-9_]+", foxrun["baseVerdict"])
    ):
        _raise(protocol.FAIL_EVIDENCE, "foxrun.baseVerdict is invalid.")
    encodings = foxrun["channelEncodings"]
    if (
        not isinstance(encodings, list)
        or len(encodings) > 16
        or any(not isinstance(value, str) for value in encodings)
        or len(set(encodings)) != len(encodings)
    ):
        _raise(protocol.FAIL_EVIDENCE, "foxrun.channelEncodings is invalid.")
    foxrun_flags = {
        key: _bool(foxrun[key], f"foxrun.{key}")
        for key in (
            "deliveryObserved",
            "remoteApplied",
            "sameOriginDropped",
            "laterLocalPublished",
        )
    }

    cleanup = _exact_mapping(document["cleanup"], _CLEANUP_KEYS, "cleanup")
    cleanup_flags = {
        key: _bool(cleanup[key], f"cleanup.{key}")
        for key in (
            "jobClosed",
            "processes",
            "port",
            "barrier",
            "files",
            "junctions",
            "subst",
        )
    }
    graceful = _identity_list(
        cleanup["gracefulOwnedIdentities"],
        "cleanup.gracefulOwnedIdentities",
    )
    forced = _identity_list(
        cleanup["forcedOwnedIdentities"],
        "cleanup.forcedOwnedIdentities",
    )
    exited = _identity_list(
        cleanup["exitedOwnedIdentities"],
        "cleanup.exitedOwnedIdentities",
    )
    residual = _identity_list(
        cleanup["residualOwnedIdentities"],
        "cleanup.residualOwnedIdentities",
    )
    owned_keys = {_identity_evidence_key(item) for item in owned}
    graceful_keys = {
        _identity_evidence_key(item)
        for item in graceful
    }
    forced_keys = {
        _identity_evidence_key(item)
        for item in forced
    }
    exited_keys = {
        _identity_evidence_key(item)
        for item in exited
    }
    residual_keys = {
        _identity_evidence_key(item)
        for item in residual
    }
    if graceful_keys & forced_keys:
        _raise(protocol.FAIL_EVIDENCE, "Cleanup identity outcomes overlap.")
    if exited_keys & residual_keys:
        _raise(
            protocol.FAIL_EVIDENCE,
            "Cleanup exit and residual identity proofs overlap.",
        )

    if passing:
        required_times = [
            times[key]
            for key in (
                "contextObservedAt",
                "initialObservedAt",
                "desktopIdentityCapturedAt",
                "firstObservedAt",
                "barrierWrittenAt",
                "secondObservedAt",
            )
        ]
        if (
            any(value is None for value in required_times)
            or not all(
                float(left) < float(right)
                for left, right in zip(
                    required_times,
                    required_times[1:],
                )
            )
        ):
            _raise(
                protocol.FAIL_EVIDENCE,
                "Connection evidence is not in strict monotonic order.",
            )
        if not all(cleanup_flags.values()):
            _raise(
                protocol.FAIL_CLEANUP,
                "Desktop-live cleanup evidence is incomplete.",
            )

        context_text = marker_excerpts["contextMarker"]
        initial_text = marker_excerpts["initialMarker"]
        first_text = marker_excerpts["firstMarker"]
        second_text = marker_excerpts["secondMarker"]
        assert isinstance(context_text, str)
        assert isinstance(initial_text, str)
        assert isinstance(first_text, str)
        assert isinstance(second_text, str)
        context_match = _REDACTED_CONTEXT_MARKER.fullmatch(context_text)
        initial_match = _REDACTED_TRANSPORT_MARKER.fullmatch(initial_text)
        first_match = _REDACTED_TRANSPORT_MARKER.fullmatch(first_text)
        second_match = _REDACTED_TRANSPORT_MARKER.fullmatch(second_text)
        if (
            context_match is None
            or initial_match is None
            or first_match is None
            or second_match is None
            or any(
                match.group("case") != BASE_CASE
                for match in (
                    context_match,
                    initial_match,
                    first_match,
                    second_match,
                )
            )
            or context_match.group("digest").upper()
            != str(token_digest)[:12]
            or (
                int(initial_match.group("active")),
                int(initial_match.group("accepted")),
            )
            != (0, 0)
            or (
                int(first_match.group("active")),
                int(first_match.group("accepted")),
            )
            != (1, 1)
            or int(second_match.group("active")) != 2
            or int(second_match.group("accepted")) < 2
        ):
            _raise(
                protocol.FAIL_EVIDENCE,
                "Redacted transport marker envelopes are inconsistent.",
            )

        if root_identity is None:
            _raise(
                protocol.FAIL_EVIDENCE,
                "Desktop root identity is unavailable.",
            )
        root_key = _identity_evidence_key(root_identity)
        try:
            desktop_root_consistent = (
                root_key in owned_keys
                and protocol.windows_paths_equal(
                    str(root_identity["executable"]),
                    str(desktop["executable"]),
                )
            )
            validate_uri_handler(
                desktop["uriHandler"],
                str(desktop["executable"]),
            )
            deeplink_consistent = desktop["deeplink"] == build_deeplink(
                connection["port"]
            )
        except Exception:
            desktop_root_consistent = False
            deeplink_consistent = False
        if not desktop_root_consistent or not deeplink_consistent:
            _raise(
                protocol.FAIL_EVIDENCE,
                "Desktop executable, ownership, handler, or deep link drifted.",
            )

        try:
            barrier_directory = ntpath.dirname(str(barrier_path))
            base_summary_path = str(foxrun["baseSummaryPath"])
            path_consistent = (
                protocol.windows_path_key(str(barrier_path))
                and ntpath.basename(str(barrier_path))
                == protocol.DESKTOP_CLIENT_BARRIER_FILENAME
                and ntpath.basename(barrier_directory) == identity["runId"]
                and ntpath.basename(
                    ntpath.dirname(barrier_directory)
                ).casefold()
                == "acceptance"
                and ntpath.basename(base_summary_path) == "summary.json"
                and protocol.windows_paths_equal(
                    ntpath.dirname(base_summary_path),
                    barrier_directory,
                )
            )
        except Exception:
            path_consistent = False
        if not path_consistent:
            _raise(
                protocol.FAIL_EVIDENCE,
                "Barrier and base-summary paths are not one owned run.",
            )

        if (
            token_digest is None
            or barrier_digest
            != _expected_barrier_digest(
                str(identity["runId"]),
                token_digest,
            )
        ):
            _raise(
                protocol.FAIL_EVIDENCE,
                "Barrier digest is not associated with the run token digest.",
            )
        if (
            not graceful_keys.issubset(owned_keys)
            or not forced_keys.issubset(owned_keys)
            or exited_keys != owned_keys
            or residual_keys
        ):
            _raise(
                protocol.FAIL_EVIDENCE,
                "Owned cleanup identities are incomplete or contradictory.",
            )
        if (
            head is None
            or not job_owned
            or not owned
            or external
            or not port_preflight
            or barrier_digest is None
            or not barrier_removed
            or foxrun["baseVerdict"] != "PASS"
            or set(encodings) != {"json", "protobuf"}
            or not all(foxrun_flags.values())
        ):
            _raise(
                protocol.FAIL_EVIDENCE,
                "Desktop-live PASS evidence is incomplete or contradictory.",
            )
    return summary


@dataclasses.dataclass(frozen=True)
class IdentityExitVerification:
    """Independent post-Job proof for every captured PID-safe identity."""

    exited: tuple[job_owner.ProcessIdentity, ...]
    residual: tuple[job_owner.ProcessIdentity, ...]

    def __post_init__(self) -> None:
        if not isinstance(self.exited, tuple) or not isinstance(
            self.residual,
            tuple,
        ):
            raise TypeError("Identity exit verification must use tuples.")
        exited_keys = {
            _process_identity_key(identity)
            for identity in self.exited
        }
        residual_keys = {
            _process_identity_key(identity)
            for identity in self.residual
        }
        if (
            len(exited_keys) != len(self.exited)
            or len(residual_keys) != len(self.residual)
            or exited_keys & residual_keys
            or len(exited_keys | residual_keys) > MAX_SUMMARY_IDENTITIES
        ):
            raise ValueError("Identity exit verification is contradictory.")


@dataclasses.dataclass(frozen=True)
class CoordinatorDependencies:
    """Injected external-state boundary for pure coordinator regression tests."""

    repository_root: pathlib.Path
    platform_name: str
    environment: Mapping[str, str]
    clock: Callable[[], float]
    sleep: Callable[[float], None]
    utc_now: Callable[[], dt.datetime]
    nonce: Callable[[], str]
    is_file: Callable[[pathlib.Path], bool]
    path_exists: Callable[[pathlib.Path], bool]
    make_directory: Callable[[pathlib.Path], None]
    verify_cli: Callable[
        [os.PathLike[str] | str, os.PathLike[str] | str],
        cli_install.VerifiedCliIdentity,
    ]
    sha256_file: Callable[[os.PathLike[str] | str], str]
    desktop_executable_lease_factory: Callable[[pathlib.Path], Any]
    read_desktop_file_version: Callable[[pathlib.Path], str]
    read_uri_handler_command: Callable[[], str]
    parse_windows_command_line: Callable[[object], tuple[str, ...]]
    read_repository_head: Callable[[pathlib.Path], str]
    read_windows_version: Callable[[], str]
    reserve_port: Callable[[int | None], Any]
    port_is_bindable: Callable[[str, int], bool]
    job_owner_factory: Callable[[pathlib.Path], Any]
    verify_identities_exited: Callable[
        [Sequence[job_owner.ProcessIdentity], float],
        IdentityExitVerification,
    ]
    read_log_lines: Callable[[pathlib.Path, int], Sequence[str]]
    coordinator_logs_within_bound: Callable[
        [Sequence[pathlib.Path], int],
        bool,
    ]
    load_json_snapshot: Callable[[pathlib.Path, int], object]
    write_json_atomic: Callable[..., None]
    remove_owned_file: Callable[[pathlib.Path], bool]


class _LoopbackPortReservation:
    """One exclusive 127.0.0.1 bind held until the owned child launch."""

    def __init__(self, requested_port: int | None):
        if requested_port is not None:
            requested_port = _validate_port(requested_port)
        selected = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        try:
            if hasattr(socket, "SO_EXCLUSIVEADDRUSE"):
                selected.setsockopt(
                    socket.SOL_SOCKET,
                    socket.SO_EXCLUSIVEADDRUSE,
                    1,
                )
            selected.bind((LOOPBACK_HOST, requested_port or 0))
            host, port = selected.getsockname()[:2]
            if host != LOOPBACK_HOST:
                raise OSError("Loopback reservation drifted.")
            self.port = _validate_port(int(port))
            self._socket: socket.socket | None = selected
        except BaseException:
            selected.close()
            raise

    def release(self) -> None:
        selected = self._socket
        self._socket = None
        if selected is not None:
            selected.close()


def _reserve_port_production(requested_port: int | None) -> _LoopbackPortReservation:
    try:
        return _LoopbackPortReservation(requested_port)
    except protocol.AcceptanceFailure:
        raise
    except Exception:
        _raise(
            protocol.FAIL_DESKTOP_PREFLIGHT,
            "Exclusive loopback Foxglove port reservation failed.",
        )


def _port_is_bindable_production(host: str, port: int) -> bool:
    if host != LOOPBACK_HOST:
        return False
    selected: socket.socket | None = None
    try:
        selected = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        if hasattr(socket, "SO_EXCLUSIVEADDRUSE"):
            selected.setsockopt(
                socket.SOL_SOCKET,
                socket.SO_EXCLUSIVEADDRUSE,
                1,
            )
        selected.bind((host, _validate_port(port)))
        return True
    except Exception:
        return False
    finally:
        if selected is not None:
            with contextlib.suppress(Exception):
                selected.close()


class _FILETIME(ctypes.Structure):
    _fields_ = (
        ("dwLowDateTime", wintypes.DWORD),
        ("dwHighDateTime", wintypes.DWORD),
    )


def _filetime_value(value: _FILETIME) -> int:
    return (
        int(value.dwHighDateTime) << 32
    ) | int(value.dwLowDateTime)


def _identity_still_live_production(
    identity: job_owner.ProcessIdentity,
) -> bool | None:
    """Return live, exited/reused, or unproven without consulting a Job."""

    if os.name != "nt":
        return None
    process_query_limited_information = 0x1000
    synchronize = 0x00100000
    wait_object_0 = 0x00000000
    wait_timeout = 0x00000102
    error_invalid_parameter = 87

    try:
        kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
        open_process = kernel32.OpenProcess
        open_process.argtypes = [
            wintypes.DWORD,
            wintypes.BOOL,
            wintypes.DWORD,
        ]
        open_process.restype = wintypes.HANDLE
        get_process_times = kernel32.GetProcessTimes
        get_process_times.argtypes = [
            wintypes.HANDLE,
            ctypes.POINTER(_FILETIME),
            ctypes.POINTER(_FILETIME),
            ctypes.POINTER(_FILETIME),
            ctypes.POINTER(_FILETIME),
        ]
        get_process_times.restype = wintypes.BOOL
        query_image = kernel32.QueryFullProcessImageNameW
        query_image.argtypes = [
            wintypes.HANDLE,
            wintypes.DWORD,
            wintypes.LPWSTR,
            ctypes.POINTER(wintypes.DWORD),
        ]
        query_image.restype = wintypes.BOOL
        wait_for_single = kernel32.WaitForSingleObject
        wait_for_single.argtypes = [wintypes.HANDLE, wintypes.DWORD]
        wait_for_single.restype = wintypes.DWORD
        close_handle = kernel32.CloseHandle
        close_handle.argtypes = [wintypes.HANDLE]
        close_handle.restype = wintypes.BOOL
    except Exception:
        return None

    handle = open_process(
        process_query_limited_information | synchronize,
        False,
        identity.pid,
    )
    if not handle:
        return (
            False
            if ctypes.get_last_error() == error_invalid_parameter
            else None
        )

    observation: bool | None = None
    try:
        creation = _FILETIME()
        exit_time = _FILETIME()
        kernel_time = _FILETIME()
        user_time = _FILETIME()
        if not get_process_times(
            handle,
            ctypes.byref(creation),
            ctypes.byref(exit_time),
            ctypes.byref(kernel_time),
            ctypes.byref(user_time),
        ):
            return None
        image = ctypes.create_unicode_buffer(32_768)
        image_length = wintypes.DWORD(len(image))
        if not query_image(
            handle,
            0,
            image,
            ctypes.byref(image_length),
        ):
            return None
        current_creation = _filetime_value(creation)
        current_executable = image.value
        if (
            current_creation != identity.creation_time_100ns
            or not protocol.windows_paths_equal(
                current_executable,
                identity.executable,
            )
        ):
            observation = False
        else:
            wait_result = int(wait_for_single(handle, 0))
            if wait_result == wait_object_0:
                observation = False
            elif wait_result == wait_timeout:
                observation = True
    except Exception:
        observation = None
    finally:
        if not close_handle(handle):
            observation = None
    return observation


def _verify_identities_exited_production(
    identities: Sequence[job_owner.ProcessIdentity],
    timeout_seconds: float,
) -> IdentityExitVerification:
    """Boundedly prove captured identities exited after the Job was closed."""

    if (
        isinstance(identities, (str, bytes))
        or len(identities) > MAX_SUMMARY_IDENTITIES
        or isinstance(timeout_seconds, bool)
        or not isinstance(timeout_seconds, (int, float))
        or not math.isfinite(float(timeout_seconds))
        or float(timeout_seconds) < 0
        or float(timeout_seconds) > 60.0
    ):
        raise ValueError("Post-Job identity verification inputs are invalid.")
    frozen = tuple(identities)
    keys = [_process_identity_key(identity) for identity in frozen]
    if len(set(keys)) != len(keys):
        raise ValueError("Post-Job identity verification contains duplicates.")

    pending = dict(zip(keys, frozen, strict=True))
    exited_keys: set[tuple[int, int, str]] = set()
    deadline = time.monotonic() + float(timeout_seconds)
    while pending:
        for key, identity in tuple(pending.items()):
            observation = _identity_still_live_production(identity)
            if observation is False:
                exited_keys.add(key)
                del pending[key]
        if not pending or time.monotonic() >= deadline:
            break
        time.sleep(
            min(
                0.05,
                max(0.0, deadline - time.monotonic()),
            )
        )
    return IdentityExitVerification(
        exited=tuple(
            identity
            for key, identity in zip(keys, frozen, strict=True)
            if key in exited_keys
        ),
        residual=tuple(
            identity
            for key, identity in zip(keys, frozen, strict=True)
            if key in pending
        ),
    )


def _read_desktop_file_version_production(path: pathlib.Path) -> str:
    """Read the selected executable's fixed Windows version resource."""

    class VS_FIXEDFILEINFO(ctypes.Structure):
        _fields_ = [
            ("dwSignature", ctypes.c_uint32),
            ("dwStrucVersion", ctypes.c_uint32),
            ("dwFileVersionMS", ctypes.c_uint32),
            ("dwFileVersionLS", ctypes.c_uint32),
            ("dwProductVersionMS", ctypes.c_uint32),
            ("dwProductVersionLS", ctypes.c_uint32),
            ("dwFileFlagsMask", ctypes.c_uint32),
            ("dwFileFlags", ctypes.c_uint32),
            ("dwFileOS", ctypes.c_uint32),
            ("dwFileType", ctypes.c_uint32),
            ("dwFileSubtype", ctypes.c_uint32),
            ("dwFileDateMS", ctypes.c_uint32),
            ("dwFileDateLS", ctypes.c_uint32),
        ]

    try:
        version = ctypes.WinDLL("version", use_last_error=True)
        get_size = version.GetFileVersionInfoSizeW
        get_size.argtypes = [ctypes.c_wchar_p, ctypes.POINTER(ctypes.c_uint32)]
        get_size.restype = ctypes.c_uint32
        get_info = version.GetFileVersionInfoW
        get_info.argtypes = [
            ctypes.c_wchar_p,
            ctypes.c_uint32,
            ctypes.c_uint32,
            ctypes.c_void_p,
        ]
        get_info.restype = ctypes.c_int
        query = version.VerQueryValueW
        query.argtypes = [
            ctypes.c_void_p,
            ctypes.c_wchar_p,
            ctypes.POINTER(ctypes.c_void_p),
            ctypes.POINTER(ctypes.c_uint32),
        ]
        query.restype = ctypes.c_int

        ignored = ctypes.c_uint32()
        size = int(get_size(str(path), ctypes.byref(ignored)))
        if size <= 0 or size > 16 * 1024 * 1024:
            raise OSError("Invalid version-resource size.")
        buffer = ctypes.create_string_buffer(size)
        if not get_info(str(path), 0, size, buffer):
            raise OSError("GetFileVersionInfoW failed.")
        pointer = ctypes.c_void_p()
        length = ctypes.c_uint32()
        if not query(
            buffer,
            "\\",
            ctypes.byref(pointer),
            ctypes.byref(length),
        ):
            raise OSError("VerQueryValueW failed.")
        if length.value < ctypes.sizeof(VS_FIXEDFILEINFO):
            raise OSError("Fixed version resource is truncated.")
        fixed = ctypes.cast(
            pointer,
            ctypes.POINTER(VS_FIXEDFILEINFO),
        ).contents
        if fixed.dwSignature != 0xFEEF04BD:
            raise OSError("Fixed version signature is invalid.")
        return ".".join(
            str(value)
            for value in (
                fixed.dwFileVersionMS >> 16,
                fixed.dwFileVersionMS & 0xFFFF,
                fixed.dwFileVersionLS >> 16,
                fixed.dwFileVersionLS & 0xFFFF,
            )
        )
    except Exception:
        _raise(
            protocol.FAIL_DESKTOP_PREFLIGHT,
            "Foxglove Desktop file version could not be read.",
        )


def _read_uri_handler_command_production() -> str:
    try:
        import winreg

        with winreg.OpenKey(
            winreg.HKEY_CLASSES_ROOT,
            r"foxglove\shell\open\command",
            0,
            winreg.KEY_READ,
        ) as key:
            value, value_type = winreg.QueryValueEx(key, None)
        if value_type not in (winreg.REG_SZ, winreg.REG_EXPAND_SZ):
            raise OSError("URI-handler registry value has another type.")
        if value_type == winreg.REG_EXPAND_SZ:
            value = os.path.expandvars(value)
        if not isinstance(value, str):
            raise OSError("URI-handler registry value is not text.")
        return value
    except Exception:
        _raise(
            protocol.FAIL_DESKTOP_PREFLIGHT,
            "Merged HKCR Foxglove URI handler could not be read.",
        )


def _read_repository_head_production(repository: pathlib.Path) -> str:
    try:
        completed = subprocess.run(
            ["git", "rev-parse", "HEAD"],
            cwd=repository,
            stdin=subprocess.DEVNULL,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            timeout=30,
            check=False,
            shell=False,
        )
        if (
            completed.returncode != 0
            or len(completed.stdout) > 256
            or len(completed.stderr) > 4096
        ):
            raise OSError("git rev-parse failed.")
        head = completed.stdout.decode("ascii").strip().casefold()
        if _LOWER_GIT_OBJECT.fullmatch(head) is None:
            raise OSError("git object identity is malformed.")
        return head
    except Exception:
        _raise(
            protocol.FAIL_DESKTOP_PREFLIGHT,
            "Repository HEAD could not be captured.",
        )


def _read_windows_version_production() -> str:
    value = platform.platform(aliased=False, terse=False)
    if not value:
        _raise(
            protocol.FAIL_DESKTOP_PREFLIGHT,
            "Windows version identity could not be captured.",
        )
    return value


class _DuplicateJsonKey(ValueError):
    pass


def _unique_json_object(
    pairs: list[tuple[str, object]],
) -> dict[str, object]:
    result: dict[str, object] = {}
    for key, value in pairs:
        if key in result:
            raise _DuplicateJsonKey("Duplicate JSON key.")
        result[key] = value
    return result


def _same_file_snapshot(left: os.stat_result, right: os.stat_result) -> bool:
    return (
        left.st_dev,
        left.st_ino,
        left.st_size,
        left.st_mtime_ns,
    ) == (
        right.st_dev,
        right.st_ino,
        right.st_size,
        right.st_mtime_ns,
    )


def _read_json_snapshot_production(
    path: pathlib.Path,
    max_bytes: int,
) -> object:
    if (
        isinstance(max_bytes, bool)
        or not isinstance(max_bytes, int)
        or max_bytes < 1
        or max_bytes > MAX_RUN_CONFIG_BYTES
    ):
        raise ValueError("JSON snapshot bound is invalid.")
    target = pathlib.Path(path)
    before = target.lstat()
    if (
        not stat.S_ISREG(before.st_mode)
        or stat.S_ISLNK(before.st_mode)
        or bool(
            getattr(before, "st_file_attributes", 0)
            & getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 0x400)
        )
    ):
        raise OSError("JSON snapshot is not a plain regular file.")
    with target.open("rb") as stream:
        opened = os.fstat(stream.fileno())
        raw = stream.read(max_bytes + 1)
    after = target.lstat()
    if (
        not _same_file_snapshot(before, opened)
        or not _same_file_snapshot(opened, after)
        or not raw
        or len(raw) > max_bytes
    ):
        raise OSError("JSON snapshot changed or exceeded its bound.")
    return json.loads(
        raw.decode("utf-8"),
        object_pairs_hook=_unique_json_object,
    )


def _read_log_lines_production(
    path: pathlib.Path,
    max_bytes: int,
) -> tuple[str, ...]:
    if max_bytes != MAX_UNITY_LOG_BYTES:
        raise ValueError("Unity log bound drifted.")
    target = pathlib.Path(path)
    try:
        with target.open("rb") as stream:
            raw = stream.read(max_bytes + 1)
    except FileNotFoundError:
        return ()
    if len(raw) > max_bytes:
        _raise(
            protocol.FAIL_DESKTOP_CONNECTION,
            "Unity log exceeded the Desktop-live bound.",
        )
    return tuple(
        raw.decode("utf-8", errors="replace").splitlines()
    )


def _coordinator_logs_within_bound_production(
    paths: Sequence[pathlib.Path],
    max_bytes: int,
) -> bool:
    if (
        isinstance(paths, (str, bytes))
        or max_bytes != MAX_COORDINATOR_LOG_BYTES
    ):
        return False
    for value in paths:
        target = pathlib.Path(value)
        try:
            info = target.lstat()
        except FileNotFoundError:
            continue
        except OSError:
            return False
        if (
            not stat.S_ISREG(info.st_mode)
            or stat.S_ISLNK(info.st_mode)
            or bool(
                getattr(info, "st_file_attributes", 0)
                & getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 0x400)
            )
            or info.st_size > max_bytes
        ):
            return False
    return True


def _write_json_atomic_production(
    path: pathlib.Path,
    payload: Mapping[str, Any],
    *,
    max_bytes: int,
) -> None:
    protocol.write_json_atomic(
        path,
        payload,
        max_bytes=max_bytes,
    )


def _remove_owned_file_production(path: pathlib.Path) -> bool:
    target = pathlib.Path(path)
    try:
        target.unlink()
    except FileNotFoundError:
        pass
    return not target.exists()


def _default_dependencies() -> CoordinatorDependencies:
    return CoordinatorDependencies(
        repository_root=REPOSITORY_ROOT,
        platform_name=os.name,
        environment=dict(os.environ),
        clock=time.monotonic,
        sleep=time.sleep,
        utc_now=lambda: dt.datetime.now(dt.timezone.utc),
        nonce=lambda: uuid.uuid4().hex[:10],
        is_file=lambda path: pathlib.Path(path).is_file(),
        path_exists=lambda path: pathlib.Path(path).exists(),
        make_directory=lambda path: pathlib.Path(path).mkdir(
            parents=True,
            exist_ok=False,
        ),
        verify_cli=cli_install.verify_installed_cli_provenance,
        sha256_file=protocol.sha256_file,
        desktop_executable_lease_factory=(
            cli_install.WindowsExecutableLease
        ),
        read_desktop_file_version=_read_desktop_file_version_production,
        read_uri_handler_command=_read_uri_handler_command_production,
        parse_windows_command_line=parse_windows_command_line,
        read_repository_head=_read_repository_head_production,
        read_windows_version=_read_windows_version_production,
        reserve_port=_reserve_port_production,
        port_is_bindable=_port_is_bindable_production,
        job_owner_factory=lambda executable: job_owner.WindowsJobOwner(
            executable
        ),
        verify_identities_exited=_verify_identities_exited_production,
        read_log_lines=_read_log_lines_production,
        coordinator_logs_within_bound=(
            _coordinator_logs_within_bound_production
        ),
        load_json_snapshot=_read_json_snapshot_production,
        write_json_atomic=_write_json_atomic_production,
        remove_owned_file=_remove_owned_file_production,
    )


def _empty_summary(
    *,
    run_id: str,
    base_output: pathlib.Path,
    barrier_path: pathlib.Path,
    desktop_executable: object,
    receipt_path: object,
) -> dict[str, object]:
    desktop_text = (
        os.fspath(desktop_executable)
        if isinstance(desktop_executable, (str, os.PathLike))
        else None
    )
    receipt_text = (
        os.fspath(receipt_path)
        if isinstance(receipt_path, (str, os.PathLike))
        else None
    )
    return {
        "schemaVersion": SUMMARY_SCHEMA_VERSION,
        "identity": {
            "runId": run_id,
            "baseCase": BASE_CASE,
            "tokenSha256": None,
            "repositoryHead": None,
            "windowsVersion": None,
            "unityVersion": None,
        },
        "cli": {
            "architecture": None,
            "assetUrl": None,
            "installedPath": None,
            "installedSha256": None,
            "installedVersion": None,
            "receiptPath": receipt_text,
            "releaseTag": None,
        },
        "desktop": {
            "executable": desktop_text,
            "fileVersion": None,
            "sha256": None,
            "uriHandler": None,
            "dataSource": DATA_SOURCE,
            "deeplink": None,
            "rootIdentity": None,
            "ownedMemberIdentities": [],
            "externalIdentities": [],
            "jobOwned": False,
        },
        "connection": {
            "host": LOOPBACK_HOST,
            "port": None,
            "portPreflight": False,
            "contextMarker": None,
            "initialMarker": None,
            "firstMarker": None,
            "secondMarker": None,
            "contextObservedAt": None,
            "initialObservedAt": None,
            "desktopIdentityCapturedAt": None,
            "firstObservedAt": None,
            "barrierWrittenAt": None,
            "secondObservedAt": None,
            "barrierPath": str(barrier_path),
            "barrierDigest": None,
            "barrierRemoved": False,
        },
        "foxrun": {
            "baseSummaryPath": str(base_output / "summary.json"),
            "baseVerdict": None,
            "channelEncodings": [],
            "deliveryObserved": False,
            "remoteApplied": False,
            "sameOriginDropped": False,
            "laterLocalPublished": False,
        },
        "cleanup": {
            "jobClosed": False,
            "processes": False,
            "port": False,
            "barrier": False,
            "files": False,
            "junctions": False,
            "subst": False,
            "gracefulOwnedIdentities": [],
            "forcedOwnedIdentities": [],
            "exitedOwnedIdentities": [],
            "residualOwnedIdentities": [],
        },
        "verdict": protocol.FAIL_DESKTOP_PREFLIGHT,
    }


def _clock_value(dependencies: CoordinatorDependencies) -> float:
    try:
        value = dependencies.clock()
    except Exception:
        _raise(
            protocol.FAIL_DESKTOP_CONNECTION,
            "Coordinator monotonic clock failed.",
        )
    if (
        isinstance(value, bool)
        or not isinstance(value, (int, float))
        or not math.isfinite(float(value))
        or float(value) < 0
    ):
        _raise(
            protocol.FAIL_DESKTOP_CONNECTION,
            "Coordinator monotonic clock is invalid.",
        )
    return float(value)


def _sleep_poll(dependencies: CoordinatorDependencies) -> None:
    try:
        dependencies.sleep(POLL_SECONDS)
    except Exception:
        _raise(
            protocol.FAIL_DESKTOP_CONNECTION,
            "Coordinator bounded polling failed.",
        )


def _require_coordinator_log_bounds(
    dependencies: CoordinatorDependencies,
    paths: Sequence[pathlib.Path],
) -> None:
    try:
        within_bound = dependencies.coordinator_logs_within_bound(
            paths,
            MAX_COORDINATOR_LOG_BYTES,
        )
    except Exception:
        within_bound = False
    if within_bound is not True:
        _raise(
            protocol.FAIL_DESKTOP_CONNECTION,
            "Coordinator-owned process log exceeded its fixed bound.",
        )


def _marker_tail(line: object, marker: str) -> str | None:
    if not isinstance(line, str):
        _raise(
            protocol.FAIL_DESKTOP_CONNECTION,
            "Unity log returned a non-text line.",
        )
    if marker not in line:
        return None
    if line.count(marker) != 1:
        _raise(
            protocol.FAIL_DESKTOP_CONNECTION,
            "Unity log marker envelope is ambiguous.",
        )
    tail = line[line.index(marker) :].strip()
    if (
        not tail
        or len(tail.encode("utf-8")) > protocol.MAX_TRANSPORT_CLIENT_MARKER_BYTES
    ):
        _raise(
            protocol.FAIL_DESKTOP_CONNECTION,
            "Unity log marker exceeds its fixed bound.",
        )
    return tail


def _redact_excerpt(line: str, token: str) -> str:
    redacted = line.replace(token, "<redacted>")
    if token in redacted or _RAW_TOKEN.search(redacted):
        _raise(
            protocol.FAIL_EVIDENCE,
            "Marker excerpt token redaction failed.",
        )
    if len(redacted) > protocol.MAX_TRANSPORT_CLIENT_MARKER_BYTES:
        _raise(
            protocol.FAIL_EVIDENCE,
            "Marker excerpt exceeds its fixed bound.",
        )
    return redacted


@dataclasses.dataclass(frozen=True)
class _LogEvidence:
    context: str | None
    context_index: int | None
    transport_lines: tuple[str, ...]
    transport_markers: tuple[protocol.TransportClientMarker, ...]
    transport_indices: tuple[int, ...]


def _scan_unity_log(
    lines: Sequence[str],
    *,
    case: str,
    token: str,
) -> _LogEvidence:
    if isinstance(lines, (str, bytes)) or len(lines) > 1_000_000:
        _raise(
            protocol.FAIL_DESKTOP_CONNECTION,
            "Unity log line collection exceeded its bound.",
        )
    context_line: str | None = None
    context_index: int | None = None
    transport_lines: list[str] = []
    transport_markers: list[protocol.TransportClientMarker] = []
    transport_indices: list[int] = []
    for raw_index, line in enumerate(lines):
        context = _marker_tail(line, "PHASE184G_CONTEXT_READY")
        if context is not None:
            match = _CONTEXT_MARKER.fullmatch(context)
            expected_digest = hashlib.sha256(
                token.encode("utf-8")
            ).hexdigest()[:12]
            if (
                match is None
                or match.group("case") != case
                or match.group("token") != token
                or match.group("digest").casefold() != expected_digest
            ):
                _raise(
                    protocol.FAIL_DESKTOP_CONNECTION,
                    "Unity context marker identity is mismatched.",
                )
            if context_line is None:
                context_line = context
                context_index = raw_index

        transport = _marker_tail(
            line,
            protocol.TRANSPORT_CLIENTS_OVERFLOW_MARKER,
        )
        if transport is None:
            transport = _marker_tail(
                line,
                protocol.TRANSPORT_CLIENTS_MARKER,
            )
        if transport is None:
            continue
        try:
            marker = protocol.parse_transport_client_marker(
                transport,
                case=case,
                token=token,
            )
        except protocol.AcceptanceFailure:
            _raise(
                protocol.FAIL_DESKTOP_CONNECTION,
                "Unity transport marker identity or envelope is invalid.",
            )
        if marker.overflow:
            _raise(
                protocol.FAIL_DESKTOP_CONNECTION,
                "Unity transport client marker overflow was observed.",
            )
        transport_lines.append(transport)
        transport_markers.append(marker)
        transport_indices.append(raw_index)
    return _LogEvidence(
        context=context_line,
        context_index=context_index,
        transport_lines=tuple(transport_lines),
        transport_markers=tuple(transport_markers),
        transport_indices=tuple(transport_indices),
    )


def _unique_transport_sequence(
    evidence: _LogEvidence,
) -> tuple[
    tuple[protocol.TransportClientMarker, str],
    ...,
]:
    result: list[tuple[protocol.TransportClientMarker, str]] = []
    previous: tuple[protocol.TransportClientMarker, str] | None = None
    for marker, line in zip(
        evidence.transport_markers,
        evidence.transport_lines,
        strict=True,
    ):
        current = (marker, line)
        if current == previous:
            continue
        previous = current
        result.append(current)
        if len(result) > protocol.MAX_TRANSPORT_CLIENT_MARKERS:
            _raise(
                protocol.FAIL_DESKTOP_CONNECTION,
                "Transport marker sequence exceeds its fixed bound.",
            )
    return tuple(result)


def _validate_transport_prefix(
    sequence: tuple[
        tuple[protocol.TransportClientMarker, str],
        ...,
    ],
    *,
    maximum_stage: int,
) -> None:
    if not sequence:
        return
    for index, (marker, _line) in enumerate(sequence):
        if index == 0:
            valid = (marker.active, marker.accepted) == (0, 0)
        elif index == 1:
            valid = (marker.active, marker.accepted) == (1, 1)
        elif index == 2:
            valid = marker.active == 2 and marker.accepted >= 2
        else:
            valid = False
        if not valid or index > maximum_stage:
            _raise(
                protocol.FAIL_DESKTOP_CONNECTION,
                "Transport clients did not follow exact 0/0 -> 1/1 -> 2/2+ order.",
            )


def _poll_base_running(owner: Any, identity: Any) -> None:
    try:
        exit_code = owner.poll(identity)
    except job_owner.OwnershipFailure:
        _raise(
            protocol.FAIL_FOXRUN_CHILD,
            "Owned FoxRun child identity or Job membership changed.",
        )
    except Exception:
        _raise(
            protocol.FAIL_FOXRUN_CHILD,
            "Owned FoxRun child state could not be polled.",
        )
    if exit_code is not None:
        _raise(
            protocol.FAIL_FOXRUN_CHILD,
            "Owned FoxRun child exited before Desktop-live evidence completed.",
        )


def _wait_for_run_config(
    *,
    dependencies: CoordinatorDependencies,
    owner: Any,
    base_identity: Any,
    path: pathlib.Path,
    repository: pathlib.Path,
    run_id: str,
    port: int,
) -> Mapping[str, Any]:
    deadline = _clock_value(dependencies) + RUN_CONFIG_TIMEOUT_SECONDS
    while _clock_value(dependencies) < deadline:
        if dependencies.path_exists(path):
            try:
                document = dependencies.load_json_snapshot(
                    path,
                    MAX_RUN_CONFIG_BYTES,
                )
                if not isinstance(document, Mapping):
                    raise TypeError("Run config is not an object.")
                base_protocol.validate_run_config(document, repository)
            except Exception:
                _raise(
                    protocol.FAIL_FOXRUN_CHILD,
                    "Owned FoxRun child emitted an invalid run-config.",
                )
            if (
                document.get("runId") != run_id
                or document.get("case") != BASE_CASE
                or document.get("foxgloveHost") != LOOPBACK_HOST
                or document.get("foxglovePort") != port
                or pathlib.Path(str(document.get("outputRoot"))) != path.parent
            ):
                _raise(
                    protocol.FAIL_FOXRUN_CHILD,
                    "Owned FoxRun run-config identity drifted.",
                )
            return document
        _poll_base_running(owner, base_identity)
        _sleep_poll(dependencies)
    _raise(
        protocol.FAIL_FOXRUN_CHILD,
        "Owned FoxRun run-config did not appear before its deadline.",
    )


def _wait_for_context_and_initial(
    *,
    dependencies: CoordinatorDependencies,
    owner: Any,
    base_identity: Any,
    unity_log: pathlib.Path,
    coordinator_logs: Sequence[pathlib.Path],
    case: str,
    token: str,
) -> tuple[str, float, str, float]:
    deadline = _clock_value(dependencies) + CONNECTION_TIMEOUT_SECONDS
    context_line: str | None = None
    context_time: float | None = None
    initial_line: str | None = None
    initial_time: float | None = None
    while _clock_value(dependencies) < deadline:
        _require_coordinator_log_bounds(
            dependencies,
            coordinator_logs,
        )
        _poll_base_running(owner, base_identity)
        try:
            lines = dependencies.read_log_lines(
                unity_log,
                MAX_UNITY_LOG_BYTES,
            )
            evidence = _scan_unity_log(lines, case=case, token=token)
        except protocol.AcceptanceFailure:
            raise
        except Exception:
            _raise(
                protocol.FAIL_DESKTOP_CONNECTION,
                "Unity log could not be read for initial transport evidence.",
            )
        sequence = _unique_transport_sequence(evidence)
        _validate_transport_prefix(sequence, maximum_stage=0)
        if sequence and (
            evidence.context is None
            or evidence.context_index is None
            or not evidence.transport_indices
            or evidence.context_index >= evidence.transport_indices[0]
        ):
            _raise(
                protocol.FAIL_DESKTOP_CONNECTION,
                "Initial transport marker preceded the context marker.",
            )
        if evidence.context is not None and context_line is None:
            context_line = evidence.context
            context_time = _clock_value(dependencies)
        if sequence and initial_line is None:
            initial_line = sequence[0][1]
            initial_time = _clock_value(dependencies)
        if (
            context_line is not None
            and initial_line is not None
            and context_time is not None
            and initial_time is not None
        ):
            _sleep_poll(dependencies)
            _require_coordinator_log_bounds(
                dependencies,
                coordinator_logs,
            )
            _poll_base_running(owner, base_identity)
            stable = _scan_unity_log(
                dependencies.read_log_lines(
                    unity_log,
                    MAX_UNITY_LOG_BYTES,
                ),
                case=case,
                token=token,
            )
            stable_sequence = _unique_transport_sequence(stable)
            _validate_transport_prefix(
                stable_sequence,
                maximum_stage=0,
            )
            if (
                not stable_sequence
                or stable.context is None
                or stable.context_index is None
                or not stable.transport_indices
                or stable.context_index >= stable.transport_indices[0]
            ):
                _raise(
                    protocol.FAIL_DESKTOP_CONNECTION,
                    "Initial 0/0 transport chronology was not stable.",
                )
            return (
                context_line,
                context_time,
                initial_line,
                initial_time,
            )
        _sleep_poll(dependencies)
    _raise(
        protocol.FAIL_DESKTOP_CONNECTION,
        "Context and stable initial 0/0 markers did not arrive.",
    )


def _record_job_member_snapshot(
    owner: Any,
    captured: dict[
        tuple[int, int, str],
        job_owner.ProcessIdentity,
    ],
) -> tuple[job_owner.ProcessIdentity, ...]:
    try:
        members = owner.members()
    except job_owner.OwnershipFailure:
        raise
    except Exception:
        _raise(
            protocol.FAIL_DESKTOP_IDENTITY,
            "Current Job membership could not be captured.",
        )
    if (
        isinstance(members, (str, bytes))
        or not isinstance(members, Sequence)
        or len(members) > MAX_SUMMARY_IDENTITIES
    ):
        _raise(
            protocol.FAIL_DESKTOP_IDENTITY,
            "Current Job membership exceeded the evidence bound.",
        )
    frozen = tuple(members)
    for identity in frozen:
        try:
            key = _process_identity_key(identity)
        except (ValueError, TypeError, protocol.AcceptanceFailure):
            _raise(
                protocol.FAIL_DESKTOP_IDENTITY,
                "Current Job membership contains an invalid identity.",
            )
        captured.setdefault(key, identity)
    if len(captured) > MAX_SUMMARY_IDENTITIES:
        _raise(
            protocol.FAIL_DESKTOP_IDENTITY,
            "Captured Job membership exceeded the evidence bound.",
        )
    return frozen


def _refresh_owned_members(
    *,
    owner: Any,
    desktop_identity: job_owner.ProcessIdentity,
    desktop_executable: pathlib.Path,
    captured: dict[
        tuple[int, int, str],
        job_owner.ProcessIdentity,
    ],
) -> tuple[job_owner.ProcessIdentity, ...]:
    try:
        desktop_key = _process_identity_key(desktop_identity)
        captured.setdefault(desktop_key, desktop_identity)
        owner.require_owned_identity(desktop_identity)
        externals = owner.external_processes(desktop_executable)
    except job_owner.OwnershipFailure:
        raise
    except Exception:
        _raise(
            protocol.FAIL_DESKTOP_IDENTITY,
            "Desktop ownership could not be revalidated.",
        )
    if externals:
        _raise(
            protocol.FAIL_DESKTOP_IDENTITY,
            "An exact-path external Desktop process appeared.",
        )
    members = _record_job_member_snapshot(owner, captured)
    if desktop_key not in {
        _process_identity_key(identity)
        for identity in members
    }:
        _raise(
            protocol.FAIL_DESKTOP_IDENTITY,
            "Desktop root is absent from exact Job membership.",
        )
    return members


def _wait_for_transport_stage(
    *,
    dependencies: CoordinatorDependencies,
    owner: Any,
    base_identity: Any,
    desktop_identity: Any,
    desktop_executable: pathlib.Path,
    captured_identities: dict[
        tuple[int, int, str],
        job_owner.ProcessIdentity,
    ],
    unity_log: pathlib.Path,
    coordinator_logs: Sequence[pathlib.Path],
    case: str,
    token: str,
    stage: int,
) -> tuple[str, float, tuple[str, ...]]:
    if stage not in (1, 2):
        raise ValueError("Transport stage must be one or two.")
    deadline = _clock_value(dependencies) + CONNECTION_TIMEOUT_SECONDS
    while _clock_value(dependencies) < deadline:
        _require_coordinator_log_bounds(
            dependencies,
            coordinator_logs,
        )
        _poll_base_running(owner, base_identity)
        _refresh_owned_members(
            owner=owner,
            desktop_identity=desktop_identity,
            desktop_executable=desktop_executable,
            captured=captured_identities,
        )
        try:
            evidence = _scan_unity_log(
                dependencies.read_log_lines(
                    unity_log,
                    MAX_UNITY_LOG_BYTES,
                ),
                case=case,
                token=token,
            )
        except protocol.AcceptanceFailure:
            raise
        except Exception:
            _raise(
                protocol.FAIL_DESKTOP_CONNECTION,
                "Unity log could not be read for Desktop transport evidence.",
            )
        sequence = _unique_transport_sequence(evidence)
        _validate_transport_prefix(sequence, maximum_stage=stage)
        if len(sequence) > stage:
            return (
                sequence[stage][1],
                _clock_value(dependencies),
                evidence.transport_lines,
            )
        _sleep_poll(dependencies)
    _raise(
        protocol.FAIL_DESKTOP_CONNECTION,
        (
            "First Desktop transport marker did not arrive."
            if stage == 1
            else "Second Desktop transport marker did not arrive."
        ),
    )


def _wait_for_base_exit(
    *,
    dependencies: CoordinatorDependencies,
    owner: Any,
    base_identity: Any,
    desktop_identity: Any,
    coordinator_logs: Sequence[pathlib.Path],
) -> int:
    deadline = _clock_value(dependencies) + BASE_EXIT_TIMEOUT_SECONDS
    while _clock_value(dependencies) < deadline:
        _require_coordinator_log_bounds(
            dependencies,
            coordinator_logs,
        )
        try:
            owner.require_owned_identity(desktop_identity)
        except job_owner.OwnershipFailure:
            _raise(
                protocol.FAIL_DESKTOP_IDENTITY,
                "Desktop identity changed while the FoxRun child completed.",
            )
        except Exception:
            _raise(
                protocol.FAIL_DESKTOP_IDENTITY,
                "Desktop identity could not be revalidated during completion.",
            )
        try:
            code = owner.poll(base_identity)
        except Exception:
            _raise(
                protocol.FAIL_FOXRUN_CHILD,
                "Owned FoxRun child exit could not be observed.",
            )
        if code is not None:
            if isinstance(code, bool) or not isinstance(code, int) or code != 0:
                _raise(
                    protocol.FAIL_FOXRUN_CHILD,
                    "Owned FoxRun child did not exit with exact code zero.",
                )
            return code
        _sleep_poll(dependencies)
    _raise(
        protocol.FAIL_FOXRUN_CHILD,
        "Owned FoxRun child did not exit within its acceptance timeout.",
    )


def _mapped_failure(
    exc: BaseException,
    *,
    stage: str,
) -> protocol.AcceptanceFailure:
    stage_code = {
        "cli": protocol.FAIL_CLI_PROVENANCE,
        "desktop-preflight": protocol.FAIL_DESKTOP_PREFLIGHT,
        "job-create": protocol.FAIL_DESKTOP_PREFLIGHT,
        "base-start": protocol.FAIL_FOXRUN_CHILD,
        "base-config": protocol.FAIL_FOXRUN_CHILD,
        "connection": protocol.FAIL_DESKTOP_CONNECTION,
        "desktop-start": protocol.FAIL_DESKTOP_START,
        "desktop-identity": protocol.FAIL_DESKTOP_IDENTITY,
        "base-exit": protocol.FAIL_FOXRUN_CHILD,
        "evidence": protocol.FAIL_EVIDENCE,
        "cleanup": protocol.FAIL_CLEANUP,
    }.get(stage, protocol.FAIL_EVIDENCE)
    if isinstance(exc, protocol.AcceptanceFailure):
        if exc.code in COORDINATOR_FAILURE_CODES:
            if stage == "desktop-preflight" and exc.code == protocol.FAIL_CLI_PROVENANCE:
                return _failure(protocol.FAIL_DESKTOP_PREFLIGHT, exc.message)
            return exc
        return _failure(stage_code, exc.message)
    if isinstance(exc, job_owner.OwnershipFailure):
        if exc.code in {
            job_owner.FAIL_CLEANUP,
            job_owner.FAIL_DESKTOP_CLOSE,
        }:
            code = protocol.FAIL_CLEANUP
        elif stage in {"desktop-preflight", "job-create"}:
            code = protocol.FAIL_DESKTOP_PREFLIGHT
        elif stage in {"desktop-identity", "connection"} and exc.code in {
            job_owner.FAIL_PROCESS_IDENTITY,
            job_owner.FAIL_PROCESS_OWNERSHIP,
            job_owner.FAIL_DESKTOP_HANDOFF,
            job_owner.FAIL_DESKTOP_PREFLIGHT,
        }:
            code = protocol.FAIL_DESKTOP_IDENTITY
        elif stage == "cleanup":
            code = protocol.FAIL_CLEANUP
        else:
            code = stage_code
        return _failure(code, exc.message)
    return _failure(
        stage_code,
        f"Unexpected {stage} failure: {type(exc).__name__}.",
    )


def _base_command(
    *,
    repository: pathlib.Path,
    unity_editor: pathlib.Path,
    port: int,
    run_id: str,
) -> tuple[str, ...]:
    return (
        str(
            repository
            / "Scripts"
            / "smoke"
            / "foxrun"
            / "phase184_profile_acceptance.py"
        ),
        "--case",
        BASE_CASE,
        "--unity-editor",
        str(unity_editor),
        "--foxglove-port",
        str(port),
        "--run-id",
        run_id,
        "--wait-for-desktop-client",
        "--retain-success-workspace",
    )


def _allocated_run_id(
    args: argparse.Namespace,
    dependencies: CoordinatorDependencies,
) -> str:
    if getattr(args, "run_id", None) is not None:
        return validate_run_id(args.run_id)
    try:
        now = dependencies.utc_now()
        nonce = dependencies.nonce()
    except Exception:
        _raise(
            protocol.FAIL_DESKTOP_PREFLIGHT,
            "Desktop-live run identity allocation failed.",
        )
    if (
        not isinstance(now, dt.datetime)
        or now.utcoffset() != dt.timedelta(0)
    ):
        _raise(
            protocol.FAIL_DESKTOP_PREFLIGHT,
            "Desktop-live UTC clock is invalid.",
        )
    return generate_run_id(
        timestamp=now.strftime("%Y%m%d-%H%M%S"),
        nonce=nonce,
    )


def _identity_documents(
    identities: Sequence[job_owner.ProcessIdentity],
) -> list[dict[str, object]]:
    if (
        isinstance(identities, (str, bytes))
        or len(identities) > MAX_SUMMARY_IDENTITIES
    ):
        _raise(
            protocol.FAIL_EVIDENCE,
            "Owned identity collection exceeded its bound.",
        )
    return [process_identity_document(identity) for identity in identities]


def run_acceptance(
    args: argparse.Namespace,
    *,
    dependencies: CoordinatorDependencies | None = None,
) -> dict[str, object]:
    """Run one injected fail-closed Desktop-live coordination transaction."""

    active = _default_dependencies() if dependencies is None else dependencies
    repository = pathlib.Path(active.repository_root).resolve()
    run_id = _allocated_run_id(args, active)
    base_output = (
        repository
        / "build"
        / "phase184"
        / "acceptance"
        / run_id
    ).resolve()
    coordinator_output = (
        repository
        / "build"
        / "phase184"
        / "desktop-live"
        / run_id
    ).resolve()
    barrier_path = (
        base_output / protocol.DESKTOP_CLIENT_BARRIER_FILENAME
    )
    coordinator_logs = (
        coordinator_output / "foxrun.stdout.log",
        coordinator_output / "foxrun.stderr.log",
        coordinator_output / "desktop.stdout.log",
        coordinator_output / "desktop.stderr.log",
    )
    wrapper_summary_path = (
        coordinator_output / DESKTOP_LIVE_SUMMARY_FILENAME
    )
    summary = _empty_summary(
        run_id=run_id,
        base_output=base_output,
        barrier_path=barrier_path,
        desktop_executable=getattr(args, "desktop_executable", None),
        receipt_path=getattr(args, "cli_receipt", None),
    )

    stage = "desktop-preflight"
    failure: protocol.AcceptanceFailure | None = None
    owner: Any | None = None
    reservation: Any | None = None
    reservation_released = False
    output_created = False
    barrier_owned = False
    job_closed = False
    close_attempted = False
    port: int | None = None
    base_identity: Any | None = None
    desktop_identity: Any | None = None
    captured_identities: dict[
        tuple[int, int, str],
        job_owner.ProcessIdentity,
    ] = {}
    base_cleanup: dict[str, bool] = {}
    success_ready = False
    close_summary: job_owner.CloseSummary | None = None
    raw_token: str | None = None
    desktop_lease_manager: Any | None = None
    desktop_lease: Any | None = None
    desktop_snapshot: cli_install.ExecutableSnapshot | None = None
    propagating_exception: BaseException | None = None

    try:
        validate_arguments(
            args,
            platform_name=active.platform_name,
            is_file=active.is_file,
        )
        base_script = (
            repository
            / "Scripts"
            / "smoke"
            / "foxrun"
            / "phase184_profile_acceptance.py"
        )
        if active.is_file(base_script) is not True:
            _raise(
                protocol.FAIL_DESKTOP_PREFLIGHT,
                "Exact Phase184-G base acceptance script is unavailable.",
            )
        if active.path_exists(base_output):
            _raise(
                protocol.FAIL_DESKTOP_PREFLIGHT,
                "Selected Phase184-G base output already exists.",
            )
        if active.path_exists(coordinator_output):
            _raise(
                protocol.FAIL_DESKTOP_PREFLIGHT,
                "Selected Desktop-live coordinator output already exists.",
            )
        active.make_directory(coordinator_output)
        output_created = True

        stage = "cli"
        verified_cli = active.verify_cli(
            args.foxglove_cli,
            args.cli_receipt,
        )
        if not isinstance(
            verified_cli,
            cli_install.VerifiedCliIdentity,
        ):
            _raise(
                protocol.FAIL_CLI_PROVENANCE,
                "CLI verifier did not return a public verified identity.",
            )
        summary["cli"] = verified_cli.to_document()

        stage = "desktop-preflight"
        try:
            lease_manager = active.desktop_executable_lease_factory(
                args.desktop_executable
            )
            entered_lease = lease_manager.__enter__()
        except protocol.AcceptanceFailure:
            raise
        except Exception:
            _raise(
                protocol.FAIL_DESKTOP_PREFLIGHT,
                "Foxglove Desktop executable lease could not be acquired.",
            )
        desktop_lease_manager = lease_manager
        desktop_lease = entered_lease
        desktop_snapshot = _capture_desktop_executable(
            desktop_lease,
            failure_code=protocol.FAIL_DESKTOP_PREFLIGHT,
        )
        desktop_version = active.read_desktop_file_version(
            args.desktop_executable
        )
        desktop_version = _bounded_string(
            desktop_version,
            "Desktop file version",
            allow_none=False,
        )
        _capture_desktop_executable(
            desktop_lease,
            failure_code=protocol.FAIL_DESKTOP_PREFLIGHT,
            expected=desktop_snapshot,
        )
        try:
            path_sha256 = protocol.validate_sha256(
                active.sha256_file(args.desktop_executable)
            )
        except Exception:
            _raise(
                protocol.FAIL_DESKTOP_PREFLIGHT,
                "Foxglove Desktop SHA-256 could not be verified.",
            )
        if path_sha256 != desktop_snapshot.sha256:
            _raise(
                protocol.FAIL_DESKTOP_PREFLIGHT,
                "Foxglove Desktop path hash does not match its lease.",
            )
        handler_command = active.read_uri_handler_command()
        handler_command = validate_uri_handler(
            handler_command,
            args.desktop_executable,
            parser=active.parse_windows_command_line,
        )
        _capture_desktop_executable(
            desktop_lease,
            failure_code=protocol.FAIL_DESKTOP_PREFLIGHT,
            expected=desktop_snapshot,
        )
        head = active.read_repository_head(repository)
        if (
            not isinstance(head, str)
            or _LOWER_GIT_OBJECT.fullmatch(head) is None
        ):
            _raise(
                protocol.FAIL_DESKTOP_PREFLIGHT,
                "Repository HEAD identity is malformed.",
            )
        windows_version = _bounded_string(
            active.read_windows_version(),
            "Windows version identity",
            allow_none=False,
        )
        summary["identity"]["repositoryHead"] = head
        summary["identity"]["windowsVersion"] = windows_version
        summary["desktop"].update(
            {
                "executable": str(args.desktop_executable),
                "fileVersion": desktop_version,
                "sha256": desktop_snapshot.sha256,
                "uriHandler": handler_command,
            }
        )

        reservation = active.reserve_port(args.foxglove_port)
        try:
            port = _validate_port(getattr(reservation, "port"))
        except Exception:
            _raise(
                protocol.FAIL_DESKTOP_PREFLIGHT,
                "Exclusive loopback reservation returned an invalid port.",
            )
        if args.foxglove_port is not None and port != args.foxglove_port:
            _raise(
                protocol.FAIL_DESKTOP_PREFLIGHT,
                "Explicit Foxglove port reservation drifted.",
            )
        summary["connection"]["port"] = port
        summary["connection"]["portPreflight"] = True
        deeplink = build_deeplink(port)
        summary["desktop"]["deeplink"] = deeplink

        stage = "job-create"
        owner = active.job_owner_factory(args.desktop_executable)
        stage = "desktop-preflight"
        owner.require_no_external_processes(args.desktop_executable)

        reservation.release()
        reservation_released = True
        stage = "base-start"
        cleaned_environment = build_clean_environment(active.environment)
        base_identity = owner.launch_suspended_owned(
            pathlib.Path(sys.executable),
            _base_command(
                repository=repository,
                unity_editor=args.unity_editor,
                port=port,
                run_id=run_id,
            ),
            cwd=repository,
            environment=cleaned_environment,
            stdout_log=coordinator_output / "foxrun.stdout.log",
            stderr_log=coordinator_output / "foxrun.stderr.log",
            handoff_policy=job_owner.RootHandoffPolicy.OWNED_PROCESS,
        )
        captured_identities.setdefault(
            _process_identity_key(base_identity),
            base_identity,
        )

        stage = "base-config"
        config = _wait_for_run_config(
            dependencies=active,
            owner=owner,
            base_identity=base_identity,
            path=base_output / "run-config.json",
            repository=repository,
            run_id=run_id,
            port=port,
        )
        raw_token = str(config["token"])
        token_digest = hashlib.sha256(
            raw_token.encode("utf-8")
        ).hexdigest().upper()
        summary["identity"]["tokenSha256"] = token_digest
        unity_log = pathlib.Path(str(config["unityLog"]))

        stage = "connection"
        (
            context_line,
            context_time,
            initial_line,
            initial_time,
        ) = _wait_for_context_and_initial(
            dependencies=active,
            owner=owner,
            base_identity=base_identity,
            unity_log=unity_log,
            coordinator_logs=coordinator_logs,
            case=BASE_CASE,
            token=raw_token,
        )
        summary["connection"].update(
            {
                "contextMarker": _redact_excerpt(
                    context_line,
                    raw_token,
                ),
                "initialMarker": _redact_excerpt(
                    initial_line,
                    raw_token,
                ),
                "contextObservedAt": context_time,
                "initialObservedAt": initial_time,
            }
        )

        stage = "desktop-identity"
        _capture_desktop_executable(
            desktop_lease,
            failure_code=protocol.FAIL_DESKTOP_IDENTITY,
            expected=desktop_snapshot,
        )
        stage = "desktop-start"
        desktop_identity = owner.launch_suspended_owned(
            args.desktop_executable,
            (deeplink,),
            cwd=repository,
            environment=cleaned_environment,
            stdout_log=coordinator_output / "desktop.stdout.log",
            stderr_log=coordinator_output / "desktop.stderr.log",
            handoff_policy=(
                job_owner.RootHandoffPolicy.DESKTOP_SINGLE_INSTANCE
            ),
        )
        stage = "desktop-identity"
        try:
            launched_selected_executable = (
                protocol.windows_paths_equal(
                    desktop_identity.executable,
                    args.desktop_executable,
                )
            )
        except Exception:
            launched_selected_executable = False
        if not launched_selected_executable:
            _raise(
                protocol.FAIL_DESKTOP_IDENTITY,
                "Desktop process image does not match the leased executable.",
            )
        _capture_desktop_executable(
            desktop_lease,
            failure_code=protocol.FAIL_DESKTOP_IDENTITY,
            expected=desktop_snapshot,
        )
        captured_identities.setdefault(
            _process_identity_key(desktop_identity),
            desktop_identity,
        )
        summary["desktop"]["rootIdentity"] = process_identity_document(
            desktop_identity
        )
        summary["desktop"]["jobOwned"] = True
        summary["connection"]["desktopIdentityCapturedAt"] = _clock_value(
            active
        )

        _refresh_owned_members(
            owner=owner,
            desktop_identity=desktop_identity,
            desktop_executable=args.desktop_executable,
            captured=captured_identities,
        )
        summary["desktop"]["ownedMemberIdentities"] = _identity_documents(
            tuple(captured_identities.values())
        )
        _release_executable_lease(desktop_lease_manager)
        desktop_lease_manager = None
        desktop_lease = None

        stage = "connection"
        first_line, first_time, _first_transport_lines = (
            _wait_for_transport_stage(
                dependencies=active,
                owner=owner,
                base_identity=base_identity,
                desktop_identity=desktop_identity,
                desktop_executable=args.desktop_executable,
                captured_identities=captured_identities,
                unity_log=unity_log,
                coordinator_logs=coordinator_logs,
                case=BASE_CASE,
                token=raw_token,
                stage=1,
            )
        )
        summary["connection"]["firstMarker"] = _redact_excerpt(
            first_line,
            raw_token,
        )
        summary["connection"]["firstObservedAt"] = first_time

        _poll_base_running(owner, base_identity)
        barrier_payload = {
            "schemaVersion": protocol.DESKTOP_CLIENT_BARRIER_SCHEMA_VERSION,
            "runId": run_id,
            "tokenDigest": token_digest,
            "state": protocol.DESKTOP_CLIENT_BARRIER_STATE,
            "acceptedClients": 1,
        }
        active.write_json_atomic(
            barrier_path,
            barrier_payload,
            max_bytes=protocol.MAX_DESKTOP_CLIENT_BARRIER_BYTES,
        )
        barrier_owned = True
        try:
            barrier_digest = protocol.validate_sha256(
                active.sha256_file(barrier_path)
            )
        except Exception:
            _raise(
                protocol.FAIL_EVIDENCE,
                "Owned Desktop barrier SHA-256 could not be captured.",
            )
        summary["connection"]["barrierDigest"] = barrier_digest
        summary["connection"]["barrierWrittenAt"] = _clock_value(active)

        (
            second_line,
            second_time,
            transport_lines,
        ) = _wait_for_transport_stage(
            dependencies=active,
            owner=owner,
            base_identity=base_identity,
            desktop_identity=desktop_identity,
            desktop_executable=args.desktop_executable,
            captured_identities=captured_identities,
            unity_log=unity_log,
            coordinator_logs=coordinator_logs,
            case=BASE_CASE,
            token=raw_token,
            stage=2,
        )
        try:
            protocol.validate_transport_client_transition_order(
                transport_lines,
                case=BASE_CASE,
                token=raw_token,
            )
        except protocol.AcceptanceFailure:
            _raise(
                protocol.FAIL_DESKTOP_CONNECTION,
                "Full transport client transition evidence is invalid.",
            )
        summary["connection"]["secondMarker"] = _redact_excerpt(
            second_line,
            raw_token,
        )
        summary["connection"]["secondObservedAt"] = second_time

        stage = "base-exit"
        _wait_for_base_exit(
            dependencies=active,
            owner=owner,
            base_identity=base_identity,
            desktop_identity=desktop_identity,
            coordinator_logs=coordinator_logs,
        )

        stage = "evidence"
        base_summary_path = base_output / "summary.json"
        try:
            base_summary = active.load_json_snapshot(
                base_summary_path,
                MAX_BASE_SUMMARY_BYTES,
            )
            if not isinstance(base_summary, Mapping):
                raise TypeError("Base summary is not an object.")
            base_protocol.validate_summary(
                base_summary,
                expected_case=BASE_CASE,
                expected_token=raw_token,
            )
        except Exception:
            _raise(
                protocol.FAIL_EVIDENCE,
                "Phase184-G base summary is invalid or stale.",
            )
        summary["foxrun"]["baseVerdict"] = base_summary.get("verdict")
        summary["identity"]["unityVersion"] = base_summary.get(
            "identity",
            {},
        ).get("unityVersion")
        cleanup_document = base_summary.get("cleanup")
        if isinstance(cleanup_document, Mapping):
            base_cleanup = {
                key: cleanup_document.get(key) is True
                for key in (
                    "processes",
                    "files",
                    "junctions",
                    "subst",
                )
            }
        foxglove = base_summary.get("foxglove")
        origin = base_summary.get("origin")
        if isinstance(foxglove, Mapping):
            summary["foxrun"]["channelEncodings"] = list(
                foxglove.get("channelEncodings", [])
            )
            summary["foxrun"]["deliveryObserved"] = (
                foxglove.get("deliveryObserved") is True
            )
        if isinstance(origin, Mapping):
            for key in (
                "remoteApplied",
                "sameOriginDropped",
                "laterLocalPublished",
            ):
                summary["foxrun"][key] = origin.get(key) is True
        if base_summary.get("verdict") != "PASS":
            _raise(
                protocol.FAIL_EVIDENCE,
                "Phase184-G base summary did not report PASS.",
            )

        stage = "desktop-identity"
        _refresh_owned_members(
            owner=owner,
            desktop_identity=desktop_identity,
            desktop_executable=args.desktop_executable,
            captured=captured_identities,
        )
        summary["desktop"]["ownedMemberIdentities"] = _identity_documents(
            tuple(captured_identities.values())
        )
        close_attempted = True
        close_summary = owner.request_owned_desktop_close(
            grace_seconds=DESKTOP_CLOSE_GRACE_SECONDS,
            reject_external=True,
        )
        if not isinstance(close_summary, job_owner.CloseSummary):
            _raise(
                protocol.FAIL_CLEANUP,
                "Desktop close did not return owned identity evidence.",
            )
        for identity in (
            close_summary.requested
            + close_summary.graceful
            + close_summary.forced
        ):
            captured_identities.setdefault(
                _process_identity_key(identity),
                identity,
            )
        if len(captured_identities) > MAX_SUMMARY_IDENTITIES:
            _raise(
                protocol.FAIL_CLEANUP,
                "Desktop close identity evidence exceeded its bound.",
            )
        job_closed = True
        summary["cleanup"]["gracefulOwnedIdentities"] = (
            _identity_documents(close_summary.graceful)
        )
        summary["cleanup"]["forcedOwnedIdentities"] = (
            _identity_documents(close_summary.forced)
        )
        success_ready = True
    except BaseException as exc:
        if isinstance(exc, (KeyboardInterrupt, SystemExit)):
            propagating_exception = exc
            raise
        failure = _mapped_failure(exc, stage=stage)
    finally:
        if desktop_lease_manager is not None:
            try:
                _release_executable_lease(
                    desktop_lease_manager,
                    propagating_exception,
                )
                desktop_lease_manager = None
                desktop_lease = None
            except BaseException as exc:
                if (
                    propagating_exception is None
                    and failure is None
                ):
                    failure = _mapped_failure(
                        exc,
                        stage="desktop-identity",
                    )

        if reservation is not None and not reservation_released:
            try:
                reservation.release()
                reservation_released = True
            except Exception:
                if failure is None:
                    failure = _failure(
                        protocol.FAIL_CLEANUP,
                        "Loopback port reservation cleanup failed.",
                    )

        if owner is not None:
            if not job_closed and not close_attempted:
                try:
                    _record_job_member_snapshot(
                        owner,
                        captured_identities,
                    )
                except Exception as exc:
                    if failure is None:
                        failure = _mapped_failure(
                            exc,
                            stage="desktop-identity",
                        )
            if captured_identities:
                summary["desktop"]["ownedMemberIdentities"] = (
                    _identity_documents(
                        tuple(captured_identities.values())
                    )
                )
            if not job_closed:
                try:
                    owner.close()
                    job_closed = True
                except Exception as exc:
                    if failure is None:
                        failure = _mapped_failure(exc, stage="cleanup")
            try:
                externals = owner.recorded_external_processes
                summary["desktop"]["externalIdentities"] = (
                    _identity_documents(externals)
                )
            except Exception as exc:
                if failure is None:
                    failure = _mapped_failure(
                        exc,
                        stage="desktop-identity",
                    )

        exited_identities: tuple[job_owner.ProcessIdentity, ...] = ()
        residual_identities: tuple[job_owner.ProcessIdentity, ...] = ()
        identity_cleanup_proved = not captured_identities
        if job_closed and captured_identities:
            frozen_captured = tuple(captured_identities.values())
            captured_keys = set(captured_identities)
            try:
                verification = active.verify_identities_exited(
                    frozen_captured,
                    IDENTITY_EXIT_TIMEOUT_SECONDS,
                )
                if not isinstance(
                    verification,
                    IdentityExitVerification,
                ):
                    raise TypeError(
                        "Identity verifier returned another type."
                    )
                exited_keys = {
                    _process_identity_key(identity)
                    for identity in verification.exited
                }
                residual_keys = {
                    _process_identity_key(identity)
                    for identity in verification.residual
                }
                if (
                    exited_keys | residual_keys != captured_keys
                    or exited_keys & residual_keys
                ):
                    raise ValueError(
                        "Identity verifier did not cover the capture."
                    )
                exited_identities = verification.exited
                residual_identities = verification.residual
                identity_cleanup_proved = (
                    exited_keys == captured_keys
                    and not residual_keys
                )
            except Exception:
                residual_identities = frozen_captured
                identity_cleanup_proved = False
        summary["cleanup"]["exitedOwnedIdentities"] = (
            _identity_documents(exited_identities)
        )
        summary["cleanup"]["residualOwnedIdentities"] = (
            _identity_documents(residual_identities)
        )

        barrier_removed = False
        if barrier_owned:
            try:
                barrier_removed = (
                    active.remove_owned_file(barrier_path) is True
                )
            except Exception:
                barrier_removed = False
        else:
            try:
                barrier_removed = not active.path_exists(barrier_path)
            except Exception:
                barrier_removed = False
        summary["connection"]["barrierRemoved"] = barrier_removed

        port_bindable = False
        if (
            port is not None
            and reservation_released
            and job_closed
        ):
            try:
                port_bindable = (
                    active.port_is_bindable(LOOPBACK_HOST, port) is True
                )
            except Exception:
                port_bindable = False

        process_cleanup = (
            job_closed
            and identity_cleanup_proved
            and (
                base_cleanup.get("processes", False)
                if base_cleanup
                else True
            )
        )
        summary["cleanup"].update(
            {
                "jobClosed": job_closed,
                "processes": process_cleanup,
                "port": port_bindable,
                "barrier": barrier_removed,
                "files": base_cleanup.get("files", False),
                "junctions": base_cleanup.get("junctions", False),
                "subst": base_cleanup.get("subst", False),
            }
        )

        if job_closed and captured_identities and not identity_cleanup_proved:
            failure = _failure(
                protocol.FAIL_CLEANUP,
                "A captured owned process identity remained after Job close.",
            )

        if success_ready and summary["desktop"]["externalIdentities"]:
            failure = _failure(
                protocol.FAIL_DESKTOP_IDENTITY,
                "External Desktop identity invalidated acceptance.",
            )
        required_cleanup = (
            summary["cleanup"]["jobClosed"],
            summary["cleanup"]["processes"],
            summary["cleanup"]["port"],
            summary["cleanup"]["barrier"],
            summary["cleanup"]["files"],
            summary["cleanup"]["junctions"],
            summary["cleanup"]["subst"],
        )
        if success_ready and not all(required_cleanup):
            failure = _failure(
                protocol.FAIL_CLEANUP,
                "Desktop-live cleanup proof is incomplete.",
            )
        summary["verdict"] = "PASS" if failure is None else failure.code

        try:
            validate_desktop_live_summary(summary)
        except protocol.AcceptanceFailure as validation_failure:
            if summary["verdict"] == "PASS":
                failure = validation_failure
                summary["verdict"] = validation_failure.code
                validate_desktop_live_summary(summary)
            elif validation_failure.code == protocol.FAIL_CLEANUP:
                summary["verdict"] = protocol.FAIL_CLEANUP
                validate_desktop_live_summary(summary)

        if output_created:
            try:
                active.write_json_atomic(
                    wrapper_summary_path,
                    summary,
                    max_bytes=MAX_SUMMARY_BYTES,
                )
            except Exception:
                summary["verdict"] = protocol.FAIL_EVIDENCE
    return summary


def main(
    argv: Sequence[str] | None = None,
    *,
    dependencies: CoordinatorDependencies | None = None,
) -> int:
    """Return zero only for one validated durable PASS summary."""

    try:
        args = parse_args(argv)
        summary = run_acceptance(args, dependencies=dependencies)
        validate_desktop_live_summary(summary)
    except (protocol.AcceptanceFailure, SystemExit):
        return 1
    return 0 if summary["verdict"] == "PASS" else 1


def _entrypoint() -> int:
    return main()


if __name__ == "__main__":
    raise SystemExit(_entrypoint())
