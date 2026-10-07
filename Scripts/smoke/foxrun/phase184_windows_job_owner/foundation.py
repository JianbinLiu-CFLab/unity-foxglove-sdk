#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Assign-before-resume Windows process ownership for Foxglove Desktop."""

from __future__ import annotations
from __future__ import annotations
from pathlib import Path as _Phase192Path
_PHASE192_FACADE_FILE = _Phase192Path(__file__).resolve().parents[1] / "phase184_windows_job_owner.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
import sys as _phase192_sys
_PHASE192_REPOSITORY_ROOT = __import__("pathlib").Path(__file__).resolve().parents[3]
if str(_PHASE192_REPOSITORY_ROOT) not in _phase192_sys.path:
    _phase192_sys.path.insert(0, str(_PHASE192_REPOSITORY_ROOT))
del _PHASE192_REPOSITORY_ROOT, _phase192_sys

import contextlib
import dataclasses
import enum
import math
import os
import pathlib
import re
import subprocess
import time
from collections.abc import Mapping, Sequence

from Scripts.smoke.foxrun import phase184_foxglove_desktop_live_protocol as protocol


MAX_DIAGNOSTIC_CHARACTERS = 512
MAX_ARGUMENTS = 256
MAX_COMMAND_LINE_CHARACTERS = 32_767
MAX_ENVIRONMENT_ENTRIES = 4_096
MAX_ENVIRONMENT_CHARACTERS = 32_767
MAX_JOB_MEMBERS = 4_096
MAX_ENUMERATED_PROCESSES = 65_536
MAX_RECORDED_EXTERNALS = 64
MAX_WAIT_SECONDS = 300.0

FAIL_WINDOWS_REQUIRED = "FAIL_WINDOWS_REQUIRED"
FAIL_JOB_CREATE = "FAIL_JOB_CREATE"
FAIL_PROCESS_CREATE = "FAIL_PROCESS_CREATE"
FAIL_PROCESS_ASSIGN = "FAIL_PROCESS_ASSIGN"
FAIL_PROCESS_IDENTITY = "FAIL_PROCESS_IDENTITY"
FAIL_PROCESS_OWNERSHIP = "FAIL_PROCESS_OWNERSHIP"
FAIL_PROCESS_RESUME = "FAIL_PROCESS_RESUME"
FAIL_DESKTOP_PREFLIGHT = "FAIL_DESKTOP_PREFLIGHT"
FAIL_DESKTOP_HANDOFF = "FAIL_DESKTOP_HANDOFF"
FAIL_DESKTOP_CLOSE = "FAIL_DESKTOP_CLOSE"
FAIL_PROCESS_WAIT = "FAIL_PROCESS_WAIT"
FAIL_CLEANUP = "FAIL_CLEANUP"

OWNERSHIP_FAILURE_CODES = frozenset(
    {
        FAIL_WINDOWS_REQUIRED,
        FAIL_JOB_CREATE,
        FAIL_PROCESS_CREATE,
        FAIL_PROCESS_ASSIGN,
        FAIL_PROCESS_IDENTITY,
        FAIL_PROCESS_OWNERSHIP,
        FAIL_PROCESS_RESUME,
        FAIL_DESKTOP_PREFLIGHT,
        FAIL_DESKTOP_HANDOFF,
        FAIL_DESKTOP_CLOSE,
        FAIL_PROCESS_WAIT,
        FAIL_CLEANUP,
    }
)


def _bounded_message(value: object) -> str:
    """Handle the bounded message step."""

    text = value if isinstance(value, str) else type(value).__name__
    text = re.sub(r"[ \t]+", " ", text.replace("\r", " ").replace("\n", " ")).strip()
    if not text:
        text = "Windows process ownership failed."
    if len(text) > MAX_DIAGNOSTIC_CHARACTERS:
        text = text[: MAX_DIAGNOSTIC_CHARACTERS - 1] + "\N{HORIZONTAL ELLIPSIS}"
    return text


class OwnershipFailure(RuntimeError):
    """Stable machine code plus a bounded one-line ownership diagnostic."""

    def __init__(self, code: str, message: object):
        """Initialize the ownership failure."""

        if code not in OWNERSHIP_FAILURE_CODES:
            raise ValueError("Unknown Windows ownership failure code.")
        self.code = code
        self.message = _bounded_message(message)
        super().__init__(f"{self.code}: {self.message}")


class ProcessOpenFailure(OSError):
    """OpenProcess failure that retains the exact Win32 error code."""

    def __init__(self, win32_error: int):
        """Initialize the process open failure."""

        self.win32_error = int(win32_error)
        super().__init__(
            self.win32_error,
            "Windows process query handle could not be opened.",
        )


class HandleCloseFailure(OSError):
    """CloseHandle failure that retains the exact Win32 error code."""

    def __init__(self, win32_error: int):
        """Initialize the handle close failure."""

        self.win32_error = int(win32_error)
        super().__init__(
            self.win32_error,
            "Windows handle cleanup failed.",
        )


def _fail(code: str, message: str) -> OwnershipFailure:
    """Handle the fail step."""

    return OwnershipFailure(code, message)


def _path_text(value: os.PathLike[str] | str, *, label: str) -> str:
    """Handle the path text step."""

    try:
        text = os.fspath(value)
        protocol.windows_path_key(text, label=label)
    except (TypeError, ValueError, protocol.AcceptanceFailure):
        raise _fail(FAIL_PROCESS_IDENTITY, f"{label} must be an absolute Windows path.") from None
    if not isinstance(text, str):
        raise _fail(FAIL_PROCESS_IDENTITY, f"{label} must be an absolute Windows path.")
    return text


def _same_path(left: str, right: str) -> bool:
    """Handle the same path step."""

    try:
        return protocol.windows_paths_equal(left, right)
    except (TypeError, ValueError, protocol.AcceptanceFailure):
        return False


@dataclasses.dataclass(frozen=True, slots=True)
class ProcessIdentity:
    """PID-reuse-safe identity captured from a live Windows process handle."""

    pid: int
    creation_time_100ns: int
    executable: str

    def __post_init__(self) -> None:
        """Validate the process identity invariants."""

        if (
            isinstance(self.pid, bool)
            or not isinstance(self.pid, int)
            or self.pid <= 0
            or self.pid > 0xFFFFFFFF
        ):
            raise _fail(FAIL_PROCESS_IDENTITY, "Process PID is invalid.")
        if (
            isinstance(self.creation_time_100ns, bool)
            or not isinstance(self.creation_time_100ns, int)
            or self.creation_time_100ns <= 0
        ):
            raise _fail(FAIL_PROCESS_IDENTITY, "Process creation time is invalid.")
        _path_text(self.executable, label="Process executable")


class RootHandoffPolicy(enum.Enum):
    """Explicit root behavior after exit or same-path process appearance."""

    DESKTOP_SINGLE_INSTANCE = "desktop-single-instance"
    OWNED_PROCESS = "owned-process"


@dataclasses.dataclass(frozen=True, slots=True)
class CloseSummary:
    """Bounded close evidence containing identities, never environment data."""

    requested: tuple[ProcessIdentity, ...]
    graceful: tuple[ProcessIdentity, ...]
    forced: tuple[ProcessIdentity, ...]


@dataclasses.dataclass(slots=True)
class _CreatedProcess:
    """Represent the created process contract."""

    pid: int
    process_handle: int
    thread_handle: int
    _cleanup: object | None = dataclasses.field(
        default=None,
        repr=False,
        compare=False,
    )

    def cleanup_now(self) -> bool:
        """Handle the cleanup now step."""

        cleanup = self._cleanup
        self._cleanup = None
        if not callable(cleanup):
            return True
        try:
            return bool(cleanup())
        except BaseException:
            return False

    def disarm_cleanup(self) -> None:
        """Handle the disarm cleanup step."""

        self._cleanup = None

    def __del__(self) -> None:
        """Handle the del step."""

        self.cleanup_now()


def _identity_key(identity: ProcessIdentity) -> tuple[int, int, str]:
    """Handle the identity key step."""

    return (
        identity.pid,
        identity.creation_time_100ns,
        protocol.windows_path_key(identity.executable),
    )


def _identities_match(left: ProcessIdentity, right: ProcessIdentity) -> bool:
    """Handle the identities match step."""

    return (
        left.pid == right.pid
        and left.creation_time_100ns == right.creation_time_100ns
        and _same_path(left.executable, right.executable)
    )


def _validate_timeout(value: object, *, allow_zero: bool = True) -> float:
    """Validate timeout."""

    if (
        isinstance(value, bool)
        or not isinstance(value, (int, float))
        or not math.isfinite(float(value))
        or float(value) < (0.0 if allow_zero else 0.001)
        or float(value) > MAX_WAIT_SECONDS
    ):
        raise _fail(FAIL_PROCESS_WAIT, "Process wait timeout is outside the fixed bound.")
    return float(value)


def _validate_launch(
    application_path: os.PathLike[str] | str,
    arguments: Sequence[str],
    cwd: os.PathLike[str] | str,
    environment: Mapping[str, str],
    stdout_log: os.PathLike[str] | str,
    stderr_log: os.PathLike[str] | str,
) -> tuple[str, tuple[str, ...], str, dict[str, str], str, str, str]:
    """Validate launch."""

    application = _path_text(application_path, label="Application path")
    working_directory = _path_text(cwd, label="Working directory")
    standard_output = _path_text(stdout_log, label="Standard-output log")
    standard_error = _path_text(stderr_log, label="Standard-error log")

    if isinstance(arguments, (str, bytes)) or not isinstance(arguments, Sequence):
        raise _fail(FAIL_PROCESS_CREATE, "Process arguments must be one explicit sequence.")
    if len(arguments) > MAX_ARGUMENTS:
        raise _fail(FAIL_PROCESS_CREATE, "Process argument count exceeds the fixed bound.")
    frozen_arguments: list[str] = []
    for argument in arguments:
        if (
            not isinstance(argument, str)
            or "\x00" in argument
            or "\r" in argument
            or "\n" in argument
        ):
            raise _fail(FAIL_PROCESS_CREATE, "Process argument is invalid.")
        frozen_arguments.append(argument)

    if not isinstance(environment, Mapping):
        raise _fail(FAIL_PROCESS_CREATE, "Process environment must be explicit.")
    if len(environment) > MAX_ENVIRONMENT_ENTRIES:
        raise _fail(FAIL_PROCESS_CREATE, "Process environment exceeds the fixed entry bound.")
    frozen_environment: dict[str, str] = {}
    folded_keys: set[str] = set()
    environment_characters = 1
    for key, value in environment.items():
        if (
            not isinstance(key, str)
            or not key
            or "=" in key
            or "\x00" in key
            or "\r" in key
            or "\n" in key
            or not isinstance(value, str)
            or "\x00" in value
        ):
            raise _fail(FAIL_PROCESS_CREATE, "Process environment contains an invalid entry.")
        folded = key.casefold()
        if folded in folded_keys:
            raise _fail(
                FAIL_PROCESS_CREATE,
                "Process environment contains case-insensitive duplicate keys.",
            )
        folded_keys.add(folded)
        frozen_environment[key] = value
        environment_characters += len(key) + len(value) + 2
    if environment_characters > MAX_ENVIRONMENT_CHARACTERS:
        raise _fail(
            FAIL_PROCESS_CREATE,
            "Process environment exceeds the fixed character bound.",
        )

    command_line = subprocess.list2cmdline((application, *frozen_arguments))
    if len(command_line) + 1 > MAX_COMMAND_LINE_CHARACTERS:
        raise _fail(FAIL_PROCESS_CREATE, "Process command line exceeds the fixed bound.")
    return (
        application,
        tuple(frozen_arguments),
        working_directory,
        frozen_environment,
        standard_output,
        standard_error,
        command_line,
    )

del _Phase192Path

__all__ = [name for name in globals() if not name.startswith("__")]
