#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Module: Scripts/smoke
# Purpose: Pure, transport-independent protocol helpers for Phase181 custom ROS2 interop.

"""Bounded evidence protocol shared by all Phase181 custom ROS2 peers.

This module deliberately contains no ROS imports.  It owns the facts that must
remain identical between Windows-local Editor helpers, Player helpers, and a
Linux peer: interface identity, marker parsing, evidence state progression,
safe summary persistence, and owned-process cleanup.
"""

from __future__ import annotations

import json
import os
import pathlib
import re
import signal
import subprocess
import tempfile
import time
from dataclasses import dataclass
from enum import Enum
from typing import Any, Callable, Iterable, Mapping, Protocol


INTERFACE_DIGEST_RE = re.compile(r"^[0-9a-f]{64}$")
MARKER_RE = re.compile(
    r"(?<![A-Za-z0-9_])(PHASE181_CUSTOM(?:_ROS2)?_[A-Z_]+)(?:\s+(.*))?$"
)
SUMMARY_SCHEMA_VERSION = 1
SENSITIVE_KEY_PARTS = (
    "token",
    "password",
    "secret",
    "credential",
    "environment",
    "zenohconfig",
    "routerconfig",
)
MARKER_NAMES = frozenset(
    {
        "PHASE181_CUSTOM_ROS2_READY",
        "PHASE181_CUSTOM_INTERFACE_READY",
        "PHASE181_CUSTOM_ROS2_PUBLISHED",
        "PHASE181_CUSTOM_ROS2_APPLIED",
        "PHASE181_CUSTOM_ROS2_SAME_ORIGIN_DROPPED",
        "PHASE181_CUSTOM_ROS2_PASS",
        "PHASE181_CUSTOM_ROS2_FAIL",
        "PHASE181_CUSTOM_ROS2_UNAVAILABLE",
    }
)


class ProtocolFailure(RuntimeError):
    """Stable Phase181 failure with no unbounded host diagnostic text."""

    def __init__(self, code: str, message: str) -> None:
        """Initialize a bounded Phase181 peer-protocol failure."""
        self.code = code
        super().__init__(f"{code}: {message}")


class ProtocolState(str, Enum):
    """Ordered positive-evidence states; any failure remains terminal."""

    PRECHECK = "PRECHECK"
    PEER_SOURCE_READY = "PEER_SOURCE_READY"
    STRING_SUBSCRIBER_WAITING = "STRING_SUBSCRIBER_WAITING"
    UNITY_READY = "UNITY_READY"
    STRING_CORRELATED = "STRING_CORRELATED"
    PROBES_RUNNING = "PROBES_RUNNING"
    UNITY_APPLIED = "UNITY_APPLIED"
    ORIGIN_CHECKED = "ORIGIN_CHECKED"
    CLEAN_STOP = "CLEAN_STOP"
    PASS = "PASS"


_NEXT_STATE: dict[ProtocolState, ProtocolState] = {
    ProtocolState.PRECHECK: ProtocolState.PEER_SOURCE_READY,
    ProtocolState.PEER_SOURCE_READY: ProtocolState.STRING_SUBSCRIBER_WAITING,
    ProtocolState.STRING_SUBSCRIBER_WAITING: ProtocolState.UNITY_READY,
    ProtocolState.UNITY_READY: ProtocolState.STRING_CORRELATED,
    ProtocolState.STRING_CORRELATED: ProtocolState.PROBES_RUNNING,
    ProtocolState.PROBES_RUNNING: ProtocolState.UNITY_APPLIED,
    ProtocolState.UNITY_APPLIED: ProtocolState.ORIGIN_CHECKED,
    ProtocolState.ORIGIN_CHECKED: ProtocolState.CLEAN_STOP,
    ProtocolState.CLEAN_STOP: ProtocolState.PASS,
}


@dataclass(frozen=True)
class StateTransition:
    """Persistable transition with an injected monotonic clock value."""

    state: ProtocolState
    observed_at: float


class EvidenceStateMachine:
    """Rejects a positive verdict unless every evidence stage occurred in order."""

    def __init__(self, now: Callable[[], float] = time.monotonic) -> None:
        """Initialize the Phase181 peer-state machine clock."""
        self._now = now
        self.state = ProtocolState.PRECHECK
        self.transitions: list[StateTransition] = [StateTransition(self.state, self._now())]

    def transition(self, target: ProtocolState) -> None:
        """Advance exactly one positive-evidence state, never skipping a proof."""

        expected = _NEXT_STATE.get(self.state)
        if expected != target:
            raise ProtocolFailure(
                "FAIL_STATE_TRANSITION",
                f"Expected {expected.value if expected else 'terminal'} after {self.state.value}, not {target.value}.",
            )
        self.state = target
        self.transitions.append(StateTransition(target, self._now()))

    def fail(self, code: str) -> None:
        """Record a bounded terminal failure code without changing positive ordering."""

        if not code.startswith("FAIL_"):
            raise ValueError("Phase181 failure codes must begin with FAIL_.")
        self.transitions.append(StateTransition(ProtocolState.PRECHECK, self._now()))


@dataclass(frozen=True)
class UnityMarker:
    """One bounded, machine-readable Unity marker from an append-only log."""

    name: str
    fields: Mapping[str, str]
    raw: str


class ProcessLike(Protocol):
    """Minimal process surface used by deterministic cleanup tests."""

    pid: int

    def poll(self) -> int | None:
        """Return the current owned-process exit status, if any."""
        ...

    def terminate(self) -> Any:
        """Request owned-process termination."""
        ...

    def kill(self) -> Any:
        """Force-stop the owned process after graceful termination expires."""
        ...

    def wait(self, timeout: float | None = None) -> int:
        """Wait for the owned process with the supplied bounded timeout."""
        ...


def require_interface_digest(expected: str, actual: str) -> str:
    """Return the exact full digest or fail closed before any ROS endpoint starts."""

    if not isinstance(expected, str) or not INTERFACE_DIGEST_RE.fullmatch(expected):
        raise ProtocolFailure("FAIL_INTERFACE_DIGEST", "The expected static interface digest is malformed.")
    if not isinstance(actual, str) or not INTERFACE_DIGEST_RE.fullmatch(actual):
        raise ProtocolFailure("FAIL_INTERFACE_DIGEST", "The observed static interface digest is malformed.")
    if actual != expected:
        raise ProtocolFailure("FAIL_INTERFACE_DIGEST", "The peer and Unity static interface digests differ.")
    return actual


def digest_prefix(digest: str, length: int = 12) -> str:
    """Return a bounded display prefix only after validating the full digest."""

    require_interface_digest(digest, digest)
    if length <= 0 or length > len(digest):
        raise ValueError("Digest prefix length must be within the full digest.")
    return digest[:length]


def log_offset(path: pathlib.Path) -> int:
    """Return the current append-only log offset; a missing log starts at zero."""

    try:
        return path.stat().st_size
    except FileNotFoundError:
        return 0


def parse_marker_line(line: str) -> UnityMarker | None:
    """Parse one recognized bounded marker; unrelated Unity output is ignored."""

    match = MARKER_RE.search(line.strip())
    if match is None or match.group(1) not in MARKER_NAMES:
        return None
    fields: dict[str, str] = {}
    tail = match.group(2) or ""
    for item in tail.split():
        key, separator, value = item.partition("=")
        if not separator or not key or len(key) > 48 or len(value) > 128:
            continue
        fields[key] = value
    return UnityMarker(match.group(1), fields, match.group(0).strip()[:512])


def read_new_markers(path: pathlib.Path, offset: int) -> tuple[list[UnityMarker], int]:
    """Read new marker bytes, restarting at zero when Unity replaces its Batch log."""

    if offset < 0:
        raise ValueError("Log offsets cannot be negative.")
    try:
        with path.open("r", encoding="utf-8", errors="replace") as stream:
            stream.seek(0, 2)
            if stream.tell() < offset:
                # ``-logFile`` truncates a previous Batch log after the worker
                # has captured its old cursor.  The new run is authoritative;
                # retaining the stale cursor would hide every new readiness
                # marker until the replacement file grew past its old size.
                offset = 0
            stream.seek(offset)
            appended = stream.read()
            end_offset = stream.tell()
    except FileNotFoundError:
        return [], 0

    seen: set[str] = set()
    markers: list[UnityMarker] = []
    for line in appended.splitlines():
        marker = parse_marker_line(line)
        if marker is None or marker.raw in seen:
            continue
        seen.add(marker.raw)
        markers.append(marker)
    return markers, end_offset


def require_nullable_empty_payload(payload: Mapping[str, object]) -> None:
    """Require the generated null/empty encoding, not an equivalent look-alike DTO."""

    checks = (
        payload.get("message") == "",
        payload.get("has_message") is True,
        payload.get("bytes") == [],
        payload.get("has_bytes") is True,
        payload.get("values") == [],
        payload.get("has_values") is True,
        payload.get("has_optional_count") is False,
        payload.get("has_optional_text") is False,
        payload.get("has_nested") is False,
    )
    if not all(checks):
        raise ProtocolFailure(
            "FAIL_PAYLOAD_SHAPE",
            "The custom envelope did not preserve the required nullable and empty sequence cases.",
        )


def require_envelope_metadata(envelope: Mapping[str, object], previous_sequence: int | None) -> int:
    """Validate one runtime envelope stamp and strictly increasing origin sequence."""

    sequence = envelope.get("foxrun_sequence")
    stamp = envelope.get("foxrun_stamp")
    if not isinstance(sequence, int) or sequence < 0 or sequence >= 2**64:
        raise ProtocolFailure("FAIL_ENVELOPE_METADATA", "Envelope sequence is not an unsigned 64-bit integer.")
    if previous_sequence is not None and sequence <= previous_sequence:
        raise ProtocolFailure("FAIL_ENVELOPE_METADATA", "Envelope sequence did not strictly increase for the run.")
    if not isinstance(stamp, Mapping):
        raise ProtocolFailure("FAIL_ENVELOPE_METADATA", "Envelope timestamp is missing.")
    seconds = stamp.get("sec")
    nanoseconds = stamp.get("nanosec")
    if not isinstance(seconds, int) or not isinstance(nanoseconds, int) or not 0 <= nanoseconds < 1_000_000_000:
        raise ProtocolFailure("FAIL_ENVELOPE_METADATA", "Envelope timestamp is not normalized.")
    return sequence


def sanitize_summary(value: object, key: str | None = None) -> object:
    """Retain only bounded non-secret evidence suitable for a local summary JSON."""

    normalized_key = (key or "").replace("_", "").lower()
    if normalized_key == "error" or any(part in normalized_key for part in SENSITIVE_KEY_PARTS):
        if normalized_key == "environment":
            return None
        return "redacted"
    if isinstance(value, Mapping):
        result: dict[str, object] = {}
        for child_key, child_value in value.items():
            if not isinstance(child_key, str):
                continue
            cleaned = sanitize_summary(child_value, child_key)
            if cleaned is not None:
                result[child_key] = cleaned
        return result
    if isinstance(value, (list, tuple)):
        return [sanitize_summary(item) for item in value]
    if isinstance(value, str):
        return value[:256]
    if isinstance(value, (int, float, bool)) or value is None:
        return value
    return str(value)[:256]


def write_summary_atomic(path: pathlib.Path, summary: Mapping[str, object]) -> None:
    """Persist sanitized evidence atomically beside the intended summary path."""

    path.parent.mkdir(parents=True, exist_ok=True)
    versioned_summary = dict(summary)
    versioned_summary.setdefault("summarySchemaVersion", SUMMARY_SCHEMA_VERSION)
    sanitized = sanitize_summary(versioned_summary)
    if not isinstance(sanitized, Mapping):
        raise TypeError("Phase181 summary root must remain a mapping.")
    temporary_path: pathlib.Path | None = None
    try:
        with tempfile.NamedTemporaryFile(
            mode="w",
            encoding="utf-8",
            dir=path.parent,
            prefix=path.name + ".",
            suffix=".tmp",
            delete=False,
        ) as stream:
            temporary_path = pathlib.Path(stream.name)
            json.dump(sanitized, stream, indent=2, sort_keys=True)
            stream.write("\n")
        os.replace(temporary_path, path)
    finally:
        if temporary_path is not None:
            try:
                temporary_path.unlink()
            except FileNotFoundError:
                pass


def terminate_owned_process(
    process: ProcessLike,
    *,
    platform_name: str | None = None,
    killpg: Callable[[int, int], object] | None = None,
    timeout_seconds: float = 5.0,
    windows_tree_terminator: Callable[[int], object] | None = None,
) -> None:
    """Stop only a helper-owned process tree, never a system-wide process class."""

    if process.poll() is not None:
        return
    platform = platform_name or os.name
    if platform == "nt":
        process.terminate()
        try:
            process.wait(timeout=timeout_seconds)
            return
        except subprocess.TimeoutExpired:
            if windows_tree_terminator is not None:
                windows_tree_terminator(process.pid)
            else:
                process.kill()
            process.wait(timeout=timeout_seconds)
        return

    posix_killpg = killpg or getattr(os, "killpg", None)
    if posix_killpg is None:
        raise ProtocolFailure("FAIL_PROCESS_CLEANUP", "POSIX process-group cleanup is unavailable on this host.")
    posix_killpg(process.pid, signal.SIGTERM)
    try:
        process.wait(timeout=timeout_seconds)
    except subprocess.TimeoutExpired:
        posix_killpg(process.pid, signal.SIGKILL)
        process.wait(timeout=timeout_seconds)


def bounded_command_label(command: Iterable[str]) -> list[str]:
    """Return command labels only; paths and values do not leak into summaries."""

    labels: list[str] = []
    for argument in command:
        if not argument:
            continue
        stem = pathlib.PurePath(argument).name
        if stem.startswith("-"):
            labels.append(stem[:64])
        elif not labels:
            labels.append(stem[:64])
    return labels[:8]
