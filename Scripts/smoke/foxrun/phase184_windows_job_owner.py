#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Assign-before-resume Windows process ownership for Foxglove Desktop."""

from __future__ import annotations

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


class WindowsJobOwner:
    """Own one unnamed kill-on-close Job and launch roots before first resume."""

    def __init__(
        self,
        desktop_executable: os.PathLike[str] | str,
        *,
        api=None,
        platform_name: str | None = None,
        handoff_policy: RootHandoffPolicy = (
            RootHandoffPolicy.DESKTOP_SINGLE_INSTANCE
        ),
    ) -> None:
        """Initialize the windows job owner."""

        self._platform_name = os.name if platform_name is None else platform_name
        if self._platform_name != "nt":
            raise _fail(
                FAIL_WINDOWS_REQUIRED,
                "Windows Job ownership is unavailable on this platform.",
            )
        self._desktop_executable = _path_text(
            desktop_executable,
            label="Desktop executable",
        )
        if not isinstance(handoff_policy, RootHandoffPolicy):
            raise _fail(
                FAIL_PROCESS_OWNERSHIP,
                "Root handoff policy is invalid.",
            )
        self._default_handoff_policy = handoff_policy
        self._api = api
        self._job_handle: int | None = None
        self._roots: dict[ProcessIdentity, int] = {}
        self._root_handoff_policies: dict[
            ProcessIdentity,
            RootHandoffPolicy,
        ] = {}
        self._root_exit_codes: dict[ProcessIdentity, int] = {}
        self._recorded_externals: dict[
            tuple[int, int, str],
            ProcessIdentity,
        ] = {}
        self._closed = False
        try:
            self._api = api if api is not None else _Win32Api()
            handle = int(self._api.create_kill_on_close_job())
            if handle <= 0:
                raise OSError("Invalid Job handle.")
            self._job_handle = handle
        except Exception:
            self._api = api
            raise _fail(
                FAIL_JOB_CREATE,
                "The Windows kill-on-close Job Object could not be created.",
            ) from None

    @property
    def recorded_external_processes(self) -> tuple[ProcessIdentity, ...]:
        """Handle the recorded external processes step."""

        return tuple(
            sorted(
                self._recorded_externals.values(),
                key=lambda item: (item.pid, item.creation_time_100ns),
            )
        )

    def _require_active(self) -> tuple[object, int]:
        """Require active."""

        if self._closed or self._api is None or self._job_handle is None:
            raise _fail(FAIL_PROCESS_OWNERSHIP, "Windows Job ownership is closed.")
        return self._api, self._job_handle

    def _abort_created(self, created: object) -> bool:
        """Handle the abort created step."""

        api = self._api
        if api is None:
            return False
        cleanup_now = getattr(created, "cleanup_now", None)
        if callable(cleanup_now):
            return bool(cleanup_now())
        process_handle = int(getattr(created, "process_handle", 0) or 0)
        thread_handle = int(getattr(created, "thread_handle", 0) or 0)
        exited = process_handle <= 0
        handles_closed = True
        if process_handle:
            try:
                api.terminate_process(process_handle)
            except BaseException:
                pass
            try:
                exited = bool(api.wait_process(process_handle, 5.0))
            except BaseException:
                exited = False
        if thread_handle:
            try:
                api.close_handle(thread_handle)
            except BaseException:
                handles_closed = False
        if process_handle:
            try:
                api.close_handle(process_handle)
            except BaseException:
                handles_closed = False
        return exited and handles_closed

    def _raise_launch_stage_failure(
        self,
        created: object,
        failure: BaseException,
        *,
        code: str,
        message: str,
    ) -> None:
        """Handle the raise launch stage failure step."""

        cleanup_succeeded = self._abort_created(created)
        if not cleanup_succeeded:
            raise _fail(
                (
                    FAIL_CLEANUP
                    if code == FAIL_CLEANUP
                    else FAIL_PROCESS_OWNERSHIP
                ),
                "Created-process interruption cleanup did not complete.",
            ) from None
        if not isinstance(failure, Exception):
            raise failure.with_traceback(failure.__traceback__)
        raise _fail(code, message) from None

    def launch_suspended_owned(
        self,
        application_path: os.PathLike[str] | str,
        arguments: Sequence[str],
        *,
        cwd: os.PathLike[str] | str,
        environment: Mapping[str, str],
        stdout_log: os.PathLike[str] | str,
        stderr_log: os.PathLike[str] | str,
        handoff_policy: RootHandoffPolicy | None = None,
    ) -> ProcessIdentity:
        """Create suspended, assign, prove identity/membership, then resume."""

        api, job_handle = self._require_active()
        selected_policy = (
            self._default_handoff_policy
            if handoff_policy is None
            else handoff_policy
        )
        if not isinstance(selected_policy, RootHandoffPolicy):
            raise _fail(
                FAIL_PROCESS_OWNERSHIP,
                "Root handoff policy is invalid.",
            )
        (
            application,
            frozen_arguments,
            working_directory,
            frozen_environment,
            standard_output,
            standard_error,
            command_line,
        ) = _validate_launch(
            application_path,
            arguments,
            cwd,
            environment,
            stdout_log,
            stderr_log,
        )
        created = None
        try:
            created = api.create_process_suspended(
                application_path=application,
                arguments=frozen_arguments,
                command_line=command_line,
                cwd=working_directory,
                environment=frozen_environment,
                stdout_log=standard_output,
                stderr_log=standard_error,
            )
            pid = int(created.pid)
            process_handle = int(created.process_handle)
            thread_handle = int(created.thread_handle)
            if pid <= 0 or process_handle <= 0 or thread_handle <= 0:
                raise OSError("Invalid created-process identity.")
        except BaseException as exc:
            if created is not None:
                self._raise_launch_stage_failure(
                    created,
                    exc,
                    code=FAIL_PROCESS_CREATE,
                    message="The suspended Windows process could not be created.",
                )
            if not isinstance(exc, Exception):
                raise
            raise _fail(
                FAIL_PROCESS_CREATE,
                "The suspended Windows process could not be created.",
            ) from None

        ownership_transferred = False
        try:
            assigned = bool(
                api.assign_process_to_job(job_handle, process_handle)
            )
        except BaseException as exc:
            self._raise_launch_stage_failure(
                created,
                exc,
                code=FAIL_PROCESS_ASSIGN,
                message="The suspended process could not be assigned to its Job Object.",
            )
        if not assigned:
            self._raise_launch_stage_failure(
                created,
                OSError("AssignProcessToJobObject returned false."),
                code=FAIL_PROCESS_ASSIGN,
                message="The suspended process could not be assigned to its Job Object.",
            )

        try:
            if not bool(api.is_process_in_job(process_handle, job_handle)):
                raise OSError("Created process is not in the exact Job.")
            ownership_transferred = True
        except BaseException as exc:
            self._raise_launch_stage_failure(
                created,
                exc,
                code=FAIL_PROCESS_OWNERSHIP,
                message="The suspended process was not confirmed in the exact Job.",
            )

        try:
            identity = api.capture_process_identity(process_handle, pid)
            if not isinstance(identity, ProcessIdentity):
                raise TypeError("Invalid process identity.")
            if identity.pid != pid or not _same_path(identity.executable, application):
                raise ValueError("Process identity does not match selected executable.")
        except BaseException as exc:
            self._raise_launch_stage_failure(
                created,
                exc,
                code=FAIL_PROCESS_IDENTITY,
                message=(
                    "The suspended process identity did not match "
                    "the selected executable."
                ),
            )

        try:
            member_pids = tuple(int(value) for value in api.job_member_pids(job_handle))
            if pid not in member_pids:
                raise ValueError("Created process is not a Job member.")
        except BaseException as exc:
            self._raise_launch_stage_failure(
                created,
                exc,
                code=FAIL_PROCESS_OWNERSHIP,
                message="The suspended process was not confirmed as a Job member.",
            )

        try:
            if not bool(api.resume_thread(thread_handle)):
                raise OSError("ResumeThread failed.")
        except BaseException as exc:
            self._raise_launch_stage_failure(
                created,
                exc,
                code=FAIL_PROCESS_RESUME,
                message="The owned suspended process could not be resumed.",
            )

        if not ownership_transferred:
            self._raise_launch_stage_failure(
                created,
                OSError("Created-process ownership transfer was not recorded."),
                code=FAIL_PROCESS_OWNERSHIP,
                message="Created-process ownership transfer was not recorded.",
            )
        try:
            api.close_handle(thread_handle)
        except BaseException as exc:
            self._raise_launch_stage_failure(
                created,
                exc,
                code=FAIL_CLEANUP,
                message="Owned process thread-handle cleanup failed.",
            )
        self._roots[identity] = process_handle
        self._root_handoff_policies[identity] = selected_policy
        disarm_cleanup = getattr(created, "disarm_cleanup", None)
        if callable(disarm_cleanup):
            disarm_cleanup()
        return identity

    def members(self) -> tuple[ProcessIdentity, ...]:
        """Return current Job members with exact live PID/time/path identities."""

        api, job_handle = self._require_active()
        try:
            pids = tuple(int(value) for value in api.job_member_pids(job_handle))
        except Exception:
            raise _fail(
                FAIL_PROCESS_OWNERSHIP,
                "Windows Job membership could not be queried.",
            ) from None
        if (
            len(pids) > MAX_JOB_MEMBERS
            or any(pid <= 0 or pid > 0xFFFFFFFF for pid in pids)
            or len(set(pids)) != len(pids)
        ):
            raise _fail(
                FAIL_PROCESS_OWNERSHIP,
                "Windows Job membership exceeded its fixed identity bound.",
            )

        expected_by_pid = {identity.pid: identity for identity in self._roots}
        identities: list[ProcessIdentity] = []
        for pid in pids:
            process_handle = 0
            try:
                try:
                    process_handle = int(api.open_process_for_query(pid))
                except Exception:
                    process_handle = 0
                if process_handle <= 0:
                    try:
                        exists = api.process_id_exists(pid)
                    except Exception:
                        exists = None
                    if exists is False:
                        continue
                    raise _fail(
                        FAIL_PROCESS_OWNERSHIP,
                        "A Job snapshot PID was not revalidated against the exact Job.",
                    )

                try:
                    is_member = bool(
                        api.is_process_in_job(
                            process_handle,
                            job_handle,
                        )
                    )
                except Exception:
                    is_member = False
                if not is_member:
                    try:
                        exit_code = api.poll_process(process_handle)
                    except Exception:
                        exit_code = None
                    if type(exit_code) is int:
                        continue
                    raise _fail(
                        FAIL_PROCESS_OWNERSHIP,
                        "A Job snapshot PID was not revalidated against the exact Job.",
                    )

                try:
                    identity = api.capture_process_identity(
                        process_handle,
                        pid,
                    )
                except Exception:
                    identity = None
                if (
                    not isinstance(identity, ProcessIdentity)
                    or identity.pid != pid
                ):
                    try:
                        exit_code = api.poll_process(process_handle)
                    except Exception:
                        exit_code = None
                    if type(exit_code) is int:
                        continue
                    raise _fail(
                        FAIL_PROCESS_IDENTITY,
                        "A Job member identity could not be captured.",
                    )

                expected = expected_by_pid.get(pid)
                if (
                    expected is not None
                    and not _identities_match(identity, expected)
                ):
                    raise _fail(
                        FAIL_PROCESS_IDENTITY,
                        "A Job member PID no longer has its owned process identity.",
                    )

                try:
                    exit_code = api.poll_process(process_handle)
                except Exception:
                    raise _fail(
                        FAIL_PROCESS_OWNERSHIP,
                        "A Job member live state could not be observed.",
                    ) from None
                if exit_code is not None:
                    if type(exit_code) is int:
                        continue
                    raise _fail(
                        FAIL_PROCESS_OWNERSHIP,
                        "A Job member live state was invalid.",
                    )
                identities.append(identity)
            finally:
                if process_handle:
                    with contextlib.suppress(Exception):
                        api.close_handle(process_handle)
        return tuple(
            sorted(
                identities,
                key=lambda item: (item.pid, item.creation_time_100ns),
            )
        )

    def enumerate_exact_path_live_processes(
        self,
        executable: os.PathLike[str] | str | None = None,
    ) -> tuple[ProcessIdentity, ...]:
        """Read-only enumeration of live processes matching one exact path."""

        api, _ = self._require_active()
        target = (
            self._desktop_executable
            if executable is None
            else _path_text(executable, label="Enumerated executable")
        )
        try:
            identities = tuple(api.enumerate_process_identities())
        except Exception:
            raise _fail(
                FAIL_DESKTOP_PREFLIGHT,
                "Live process identity enumeration failed.",
            ) from None
        if len(identities) > MAX_ENUMERATED_PROCESSES:
            raise _fail(
                FAIL_DESKTOP_PREFLIGHT,
                "Live process enumeration exceeded its fixed bound.",
            )
        matches: dict[tuple[int, int, str], ProcessIdentity] = {}
        for identity in identities:
            if not isinstance(identity, ProcessIdentity):
                raise _fail(
                    FAIL_DESKTOP_PREFLIGHT,
                    "Live process enumeration returned an invalid identity.",
                )
            if _same_path(identity.executable, target):
                matches[_identity_key(identity)] = identity
        return tuple(
            sorted(
                matches.values(),
                key=lambda item: (item.pid, item.creation_time_100ns),
            )
        )

    def external_processes(
        self,
        executable: os.PathLike[str] | str | None = None,
    ) -> tuple[ProcessIdentity, ...]:
        """Return and boundedly record exact-path processes outside this Job."""

        api, job_handle = self._require_active()
        target = (
            self._desktop_executable
            if executable is None
            else _path_text(executable, label="External executable")
        )
        external_by_key: dict[
            tuple[int, int, str],
            ProcessIdentity,
        ] = {}
        for observed in self.enumerate_exact_path_live_processes(target):
            process_handle = 0
            try:
                process_handle = int(api.open_process_for_query(observed.pid))
            except OSError:
                try:
                    process_exists = api.process_id_exists(observed.pid)
                except Exception:
                    raise _fail(
                        FAIL_DESKTOP_PREFLIGHT,
                        "Exact-path process existence could not be proven.",
                    ) from None
                if not isinstance(process_exists, bool):
                    raise _fail(
                        FAIL_DESKTOP_PREFLIGHT,
                        "Exact-path process existence result was invalid.",
                    )
                if not process_exists:
                    continue
                raise _fail(
                    FAIL_DESKTOP_PREFLIGHT,
                    "Exact-path process Job membership could not be proven.",
                ) from None
            except Exception:
                raise _fail(
                    FAIL_DESKTOP_PREFLIGHT,
                    "Exact-path process query handle could not be opened.",
                ) from None

            if process_handle <= 0:
                try:
                    process_exists = api.process_id_exists(observed.pid)
                except Exception:
                    raise _fail(
                        FAIL_DESKTOP_PREFLIGHT,
                        "Exact-path process existence could not be proven.",
                    ) from None
                if not isinstance(process_exists, bool):
                    raise _fail(
                        FAIL_DESKTOP_PREFLIGHT,
                        "Exact-path process existence result was invalid.",
                    )
                if not process_exists:
                    continue
                raise _fail(
                    FAIL_DESKTOP_PREFLIGHT,
                    "Exact-path process query handle could not be opened.",
                )

            try:
                is_member = bool(
                    api.is_process_in_job(process_handle, job_handle)
                )
                current = api.capture_process_identity(
                    process_handle,
                    observed.pid,
                )
            except Exception:
                vanished = False
                if process_handle:
                    try:
                        vanished = api.poll_process(process_handle) is not None
                    except Exception:
                        vanished = False
                if vanished:
                    continue
                raise _fail(
                    FAIL_DESKTOP_PREFLIGHT,
                    "Exact-path process Job membership could not be proven.",
                ) from None
            finally:
                if process_handle:
                    with contextlib.suppress(Exception):
                        api.close_handle(process_handle)

            if (
                not isinstance(current, ProcessIdentity)
                or current.pid != observed.pid
            ):
                raise _fail(
                    FAIL_DESKTOP_PREFLIGHT,
                    "Exact-path process identity could not be revalidated.",
                )
            if not _same_path(current.executable, target):
                continue
            if not is_member:
                external_by_key[_identity_key(current)] = current

        external = tuple(
            sorted(
                external_by_key.values(),
                key=lambda item: (item.pid, item.creation_time_100ns),
            )
        )
        for identity in external:
            key = _identity_key(identity)
            if (
                key not in self._recorded_externals
                and len(self._recorded_externals) < MAX_RECORDED_EXTERNALS
            ):
                self._recorded_externals[key] = identity
        return external

    def require_no_external_processes(
        self,
        executable: os.PathLike[str] | str | None = None,
    ) -> None:
        """Require no external processes."""

        if self.external_processes(executable):
            raise _fail(
                FAIL_DESKTOP_PREFLIGHT,
                "An exact-path Desktop process exists outside the owned Job.",
            )

    def require_owned_identity(self, identity: ProcessIdentity) -> ProcessIdentity:
        """Reject missing membership, PID reuse, path drift, and handoff."""

        if not isinstance(identity, ProcessIdentity):
            raise _fail(FAIL_PROCESS_IDENTITY, "Owned process identity is invalid.")
        policy = self._root_policy(identity)
        external = (
            self.external_processes(identity.executable)
            if policy is RootHandoffPolicy.DESKTOP_SINGLE_INSTANCE
            else ()
        )
        current_by_pid = {item.pid: item for item in self.members()}
        current = current_by_pid.get(identity.pid)
        if current is None:
            if policy is RootHandoffPolicy.DESKTOP_SINGLE_INSTANCE:
                raise _fail(
                    FAIL_DESKTOP_HANDOFF,
                    "The owned Desktop root left its Job or handed off externally.",
                )
            raise _fail(
                FAIL_PROCESS_OWNERSHIP,
                "The process is not a current member of the owned Job.",
            )
        if not _identities_match(current, identity):
            raise _fail(
                FAIL_PROCESS_IDENTITY,
                "The live PID creation time or executable path changed.",
            )
        if external and policy is RootHandoffPolicy.DESKTOP_SINGLE_INSTANCE:
            raise _fail(
                FAIL_DESKTOP_HANDOFF,
                "A same-path Desktop process is running outside the owned Job.",
            )
        return current

    def _root_handle(self, identity: ProcessIdentity) -> int | None:
        """Handle the root handle step."""

        for expected, handle in self._roots.items():
            if _identities_match(expected, identity):
                return handle
        return None

    def _root_policy(
        self,
        identity: ProcessIdentity,
    ) -> RootHandoffPolicy | None:
        """Handle the root policy step."""

        for expected, policy in self._root_handoff_policies.items():
            if _identities_match(expected, identity):
                return policy
        return None

    def _require_retained_root(
        self,
        identity: ProcessIdentity,
    ) -> tuple[object, int, int, RootHandoffPolicy]:
        """Require retained root."""

        if not isinstance(identity, ProcessIdentity):
            raise _fail(FAIL_PROCESS_IDENTITY, "Owned process identity is invalid.")
        api, job_handle = self._require_active()
        handle = self._root_handle(identity)
        policy = self._root_policy(identity)
        if handle is None or policy is None:
            raise _fail(
                FAIL_PROCESS_WAIT,
                "Only a directly launched retained root can be observed.",
            )
        return api, job_handle, handle, policy

    def _validate_live_retained_root(
        self,
        identity: ProcessIdentity,
        *,
        api,
        job_handle: int,
        process_handle: int,
    ) -> None:
        """Validate live retained root."""

        try:
            if not bool(api.is_process_in_job(process_handle, job_handle)):
                raise OSError("Retained root left the exact Job.")
        except Exception:
            raise _fail(
                FAIL_PROCESS_OWNERSHIP,
                "The active retained root is not in the exact Job.",
            ) from None
        try:
            current = api.capture_process_identity(
                process_handle,
                identity.pid,
            )
        except Exception:
            current = None
        if (
            not isinstance(current, ProcessIdentity)
            or not _identities_match(current, identity)
        ):
            raise _fail(
                FAIL_PROCESS_IDENTITY,
                "The active retained root identity changed.",
            )

    def _check_root_handoff(
        self,
        identity: ProcessIdentity,
        policy: RootHandoffPolicy,
    ) -> None:
        """Handle the check root handoff step."""

        if (
            policy is RootHandoffPolicy.DESKTOP_SINGLE_INSTANCE
            and self.external_processes(identity.executable)
        ):
            raise _fail(
                FAIL_DESKTOP_HANDOFF,
                "The Desktop root exited or handed off to an external process.",
            )

    def poll(self, identity: ProcessIdentity) -> int | None:
        """Handle the poll step."""

        api, job_handle, handle, policy = self._require_retained_root(identity)
        try:
            exit_code = api.poll_process(handle)
        except Exception:
            raise _fail(FAIL_PROCESS_WAIT, "Owned process polling failed.") from None
        if exit_code is not None:
            exact_exit_code = int(exit_code)
            self._root_exit_codes[identity] = exact_exit_code
            self._check_root_handoff(identity, policy)
            return exact_exit_code
        self._validate_live_retained_root(
            identity,
            api=api,
            job_handle=job_handle,
            process_handle=handle,
        )
        self._check_root_handoff(identity, policy)
        return None

    def wait(
        self,
        identity: ProcessIdentity,
        *,
        timeout_seconds: float,
    ) -> bool:
        """Wait for the configured owned process."""

        timeout = _validate_timeout(timeout_seconds)
        api, job_handle, handle, policy = self._require_retained_root(identity)
        try:
            exited = bool(api.wait_process(handle, timeout))
        except Exception:
            raise _fail(FAIL_PROCESS_WAIT, "Owned process wait failed.") from None
        if exited:
            try:
                exit_code = api.poll_process(handle)
            except Exception:
                raise _fail(
                    FAIL_PROCESS_WAIT,
                    "Owned process exit code could not be recorded.",
                ) from None
            if exit_code is None:
                raise _fail(
                    FAIL_PROCESS_WAIT,
                    "Owned process wait signaled without an exit code.",
                )
            self._root_exit_codes[identity] = int(exit_code)
            self._check_root_handoff(identity, policy)
            return True
        self._validate_live_retained_root(
            identity,
            api=api,
            job_handle=job_handle,
            process_handle=handle,
        )
        self._check_root_handoff(identity, policy)
        return False

    def request_owned_desktop_close(
        self,
        *,
        grace_seconds: float = 10.0,
        reject_external: bool = True,
    ) -> CloseSummary:
        """Post WM_CLOSE to owned Desktop windows, then hard-close the Job."""

        grace = _validate_timeout(grace_seconds)
        requested: tuple[ProcessIdentity, ...] = ()
        graceful: list[ProcessIdentity] = []
        forced: list[ProcessIdentity] = []
        try:
            external = self.external_processes(self._desktop_executable)
            if external and reject_external:
                raise _fail(
                    FAIL_DESKTOP_HANDOFF,
                    "Desktop close rejected an exact-path process outside the Job.",
                )
            requested = tuple(
                identity
                for identity in self.members()
                if _same_path(identity.executable, self._desktop_executable)
            )
            if requested:
                try:
                    api, job_handle = self._require_active()
                    self._api.post_close_to_top_level_windows(
                        requested,
                        job_handle,
                    )
                except Exception:
                    raise _fail(
                        FAIL_PROCESS_OWNERSHIP,
                        "A Desktop window process failed exact Job identity validation.",
                    ) from None

            deadline = time.monotonic() + grace
            for identity in requested:
                remaining = max(0.0, deadline - time.monotonic())
                handle = self._root_handle(identity)
                try:
                    exited = (
                        bool(self._api.wait_process(handle, remaining))
                        if handle is not None
                        else bool(self._api.wait_identity(identity, remaining))
                    )
                except Exception:
                    exited = False
                (graceful if exited else forced).append(identity)
            return CloseSummary(
                requested=requested,
                graceful=tuple(graceful),
                forced=tuple(forced),
            )
        finally:
            self.close()

    def close(self) -> None:
        """Close the sole Job handle once, then release retained root handles."""

        if self._closed:
            return
        self._closed = True
        api = self._api
        job_handle = self._job_handle
        self._job_handle = None
        root_handles = tuple(dict.fromkeys(self._roots.values()))
        self._roots.clear()
        self._root_handoff_policies.clear()
        self._root_exit_codes.clear()
        owned_handles = (
            ((job_handle,) if job_handle is not None else ())
            + root_handles
        )
        handles = (
            tuple(dict.fromkeys(owned_handles))
            if api is not None
            else ()
        )
        cleanup_failed = False
        for handle in handles:
            try:
                api.close_handle(handle)
            except Exception:
                cleanup_failed = True
        if cleanup_failed:
            raise _fail(
                FAIL_CLEANUP,
                "Windows Job ownership handle cleanup did not complete.",
            ) from None

    def __enter__(self) -> "WindowsJobOwner":
        """Enter the windows job owner context."""

        self._require_active()
        return self

    def __exit__(self, _type, _value, _traceback) -> None:
        """Exit the windows job owner context without suppressing failures."""

        self.close()


class _Win32Api:
    """Lazy ctypes bindings for the bounded Win32 ownership operations."""

    JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000
    JobObjectBasicProcessIdList = 3
    JobObjectExtendedLimitInformation = 9

    CREATE_SUSPENDED = 0x00000004
    CREATE_NEW_PROCESS_GROUP = 0x00000200
    CREATE_UNICODE_ENVIRONMENT = 0x00000400
    EXTENDED_STARTUPINFO_PRESENT = 0x00080000
    PROC_THREAD_ATTRIBUTE_HANDLE_LIST = 0x00020002
    STARTF_USESTDHANDLES = 0x00000100

    HANDLE_FLAG_INHERIT = 0x00000001
    GENERIC_READ = 0x80000000
    GENERIC_WRITE = 0x40000000
    FILE_SHARE_READ = 0x00000001
    FILE_SHARE_WRITE = 0x00000002
    FILE_SHARE_DELETE = 0x00000004
    CREATE_ALWAYS = 2
    OPEN_EXISTING = 3
    FILE_ATTRIBUTE_NORMAL = 0x00000080

    PROCESS_TERMINATE = 0x0001
    PROCESS_QUERY_LIMITED_INFORMATION = 0x1000
    SYNCHRONIZE = 0x00100000
    TH32CS_SNAPPROCESS = 0x00000002
    WM_CLOSE = 0x0010
    WAIT_OBJECT_0 = 0
    WAIT_TIMEOUT = 258
    WAIT_FAILED = 0xFFFFFFFF
    STILL_ACTIVE = 259
    ERROR_INVALID_PARAMETER = 87
    ERROR_NO_MORE_FILES = 18

    def __init__(self) -> None:
        """Initialize the Win32 API."""

        if os.name != "nt":
            raise OSError("Win32 API is unavailable.")
        import ctypes
        from ctypes import wintypes

        self.ctypes = ctypes
        self.wintypes = wintypes
        self.kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
        self.user32 = ctypes.WinDLL("user32", use_last_error=True)
        self._invalid_handle = ctypes.c_void_p(-1).value
        self._define_structures()
        self._bind_functions()

    def _define_structures(self) -> None:
        """Handle the define structures step."""

        ctypes = self.ctypes
        wintypes = self.wintypes

        class SECURITY_ATTRIBUTES(ctypes.Structure):
            """Represent the security attributes contract."""

            _fields_ = [
                ("nLength", wintypes.DWORD),
                ("lpSecurityDescriptor", wintypes.LPVOID),
                ("bInheritHandle", wintypes.BOOL),
            ]

        class FILETIME(ctypes.Structure):
            """Represent the filetime contract."""

            _fields_ = [
                ("dwLowDateTime", wintypes.DWORD),
                ("dwHighDateTime", wintypes.DWORD),
            ]

        class STARTUPINFOW(ctypes.Structure):
            """Represent the startupinfow contract."""

            _fields_ = [
                ("cb", wintypes.DWORD),
                ("lpReserved", wintypes.LPWSTR),
                ("lpDesktop", wintypes.LPWSTR),
                ("lpTitle", wintypes.LPWSTR),
                ("dwX", wintypes.DWORD),
                ("dwY", wintypes.DWORD),
                ("dwXSize", wintypes.DWORD),
                ("dwYSize", wintypes.DWORD),
                ("dwXCountChars", wintypes.DWORD),
                ("dwYCountChars", wintypes.DWORD),
                ("dwFillAttribute", wintypes.DWORD),
                ("dwFlags", wintypes.DWORD),
                ("wShowWindow", wintypes.WORD),
                ("cbReserved2", wintypes.WORD),
                ("lpReserved2", ctypes.POINTER(wintypes.BYTE)),
                ("hStdInput", wintypes.HANDLE),
                ("hStdOutput", wintypes.HANDLE),
                ("hStdError", wintypes.HANDLE),
            ]

        class STARTUPINFOEXW(ctypes.Structure):
            """Represent the startupinfoexw contract."""

            _fields_ = [
                ("StartupInfo", STARTUPINFOW),
                ("lpAttributeList", wintypes.LPVOID),
            ]

        class PROCESS_INFORMATION(ctypes.Structure):
            """Represent the process information contract."""

            _fields_ = [
                ("hProcess", wintypes.HANDLE),
                ("hThread", wintypes.HANDLE),
                ("dwProcessId", wintypes.DWORD),
                ("dwThreadId", wintypes.DWORD),
            ]

        class IO_COUNTERS(ctypes.Structure):
            """Represent the I/O counters contract."""

            _fields_ = [
                ("ReadOperationCount", ctypes.c_uint64),
                ("WriteOperationCount", ctypes.c_uint64),
                ("OtherOperationCount", ctypes.c_uint64),
                ("ReadTransferCount", ctypes.c_uint64),
                ("WriteTransferCount", ctypes.c_uint64),
                ("OtherTransferCount", ctypes.c_uint64),
            ]

        class BASIC_LIMIT_INFORMATION(ctypes.Structure):
            """Represent the basic limit information contract."""

            _fields_ = [
                ("PerProcessUserTimeLimit", ctypes.c_int64),
                ("PerJobUserTimeLimit", ctypes.c_int64),
                ("LimitFlags", wintypes.DWORD),
                ("MinimumWorkingSetSize", ctypes.c_size_t),
                ("MaximumWorkingSetSize", ctypes.c_size_t),
                ("ActiveProcessLimit", wintypes.DWORD),
                ("Affinity", ctypes.c_size_t),
                ("PriorityClass", wintypes.DWORD),
                ("SchedulingClass", wintypes.DWORD),
            ]

        class EXTENDED_LIMIT_INFORMATION(ctypes.Structure):
            """Represent the extended limit information contract."""

            _fields_ = [
                ("BasicLimitInformation", BASIC_LIMIT_INFORMATION),
                ("IoInfo", IO_COUNTERS),
                ("ProcessMemoryLimit", ctypes.c_size_t),
                ("JobMemoryLimit", ctypes.c_size_t),
                ("PeakProcessMemoryUsed", ctypes.c_size_t),
                ("PeakJobMemoryUsed", ctypes.c_size_t),
            ]

        class PROCESSENTRY32W(ctypes.Structure):
            """Represent the processentry32 w contract."""

            _fields_ = [
                ("dwSize", wintypes.DWORD),
                ("cntUsage", wintypes.DWORD),
                ("th32ProcessID", wintypes.DWORD),
                ("th32DefaultHeapID", ctypes.c_size_t),
                ("th32ModuleID", wintypes.DWORD),
                ("cntThreads", wintypes.DWORD),
                ("th32ParentProcessID", wintypes.DWORD),
                ("pcPriClassBase", wintypes.LONG),
                ("dwFlags", wintypes.DWORD),
                ("szExeFile", wintypes.WCHAR * 260),
            ]

        self.SECURITY_ATTRIBUTES = SECURITY_ATTRIBUTES
        self.FILETIME = FILETIME
        self.STARTUPINFOW = STARTUPINFOW
        self.STARTUPINFOEXW = STARTUPINFOEXW
        self.PROCESS_INFORMATION = PROCESS_INFORMATION
        self.EXTENDED_LIMIT_INFORMATION = EXTENDED_LIMIT_INFORMATION
        self.PROCESSENTRY32W = PROCESSENTRY32W
        self.WNDENUMPROC = ctypes.WINFUNCTYPE(
            wintypes.BOOL,
            wintypes.HWND,
            wintypes.LPARAM,
        )

    def _bind_functions(self) -> None:
        """Handle the bind functions step."""

        ctypes = self.ctypes
        wintypes = self.wintypes
        kernel32 = self.kernel32
        user32 = self.user32

        kernel32.CreateJobObjectW.argtypes = [
            ctypes.POINTER(self.SECURITY_ATTRIBUTES),
            wintypes.LPCWSTR,
        ]
        kernel32.CreateJobObjectW.restype = wintypes.HANDLE
        kernel32.SetInformationJobObject.argtypes = [
            wintypes.HANDLE,
            ctypes.c_int,
            wintypes.LPVOID,
            wintypes.DWORD,
        ]
        kernel32.SetInformationJobObject.restype = wintypes.BOOL
        kernel32.QueryInformationJobObject.argtypes = [
            wintypes.HANDLE,
            ctypes.c_int,
            wintypes.LPVOID,
            wintypes.DWORD,
            ctypes.POINTER(wintypes.DWORD),
        ]
        kernel32.QueryInformationJobObject.restype = wintypes.BOOL
        kernel32.SetHandleInformation.argtypes = [
            wintypes.HANDLE,
            wintypes.DWORD,
            wintypes.DWORD,
        ]
        kernel32.SetHandleInformation.restype = wintypes.BOOL
        kernel32.AssignProcessToJobObject.argtypes = [
            wintypes.HANDLE,
            wintypes.HANDLE,
        ]
        kernel32.AssignProcessToJobObject.restype = wintypes.BOOL
        kernel32.IsProcessInJob.argtypes = [
            wintypes.HANDLE,
            wintypes.HANDLE,
            ctypes.POINTER(wintypes.BOOL),
        ]
        kernel32.IsProcessInJob.restype = wintypes.BOOL
        kernel32.CreateFileW.argtypes = [
            wintypes.LPCWSTR,
            wintypes.DWORD,
            wintypes.DWORD,
            ctypes.POINTER(self.SECURITY_ATTRIBUTES),
            wintypes.DWORD,
            wintypes.DWORD,
            wintypes.HANDLE,
        ]
        kernel32.CreateFileW.restype = wintypes.HANDLE
        kernel32.InitializeProcThreadAttributeList.argtypes = [
            wintypes.LPVOID,
            wintypes.DWORD,
            wintypes.DWORD,
            ctypes.POINTER(ctypes.c_size_t),
        ]
        kernel32.InitializeProcThreadAttributeList.restype = wintypes.BOOL
        kernel32.UpdateProcThreadAttribute.argtypes = [
            wintypes.LPVOID,
            wintypes.DWORD,
            ctypes.c_size_t,
            wintypes.LPVOID,
            ctypes.c_size_t,
            wintypes.LPVOID,
            ctypes.POINTER(ctypes.c_size_t),
        ]
        kernel32.UpdateProcThreadAttribute.restype = wintypes.BOOL
        kernel32.DeleteProcThreadAttributeList.argtypes = [wintypes.LPVOID]
        kernel32.DeleteProcThreadAttributeList.restype = None
        kernel32.CreateProcessW.argtypes = [
            wintypes.LPCWSTR,
            wintypes.LPWSTR,
            ctypes.POINTER(self.SECURITY_ATTRIBUTES),
            ctypes.POINTER(self.SECURITY_ATTRIBUTES),
            wintypes.BOOL,
            wintypes.DWORD,
            wintypes.LPVOID,
            wintypes.LPCWSTR,
            ctypes.POINTER(self.STARTUPINFOW),
            ctypes.POINTER(self.PROCESS_INFORMATION),
        ]
        kernel32.CreateProcessW.restype = wintypes.BOOL
        kernel32.ResumeThread.argtypes = [wintypes.HANDLE]
        kernel32.ResumeThread.restype = wintypes.DWORD
        kernel32.GetProcessId.argtypes = [wintypes.HANDLE]
        kernel32.GetProcessId.restype = wintypes.DWORD
        kernel32.GetProcessTimes.argtypes = [
            wintypes.HANDLE,
            ctypes.POINTER(self.FILETIME),
            ctypes.POINTER(self.FILETIME),
            ctypes.POINTER(self.FILETIME),
            ctypes.POINTER(self.FILETIME),
        ]
        kernel32.GetProcessTimes.restype = wintypes.BOOL
        kernel32.QueryFullProcessImageNameW.argtypes = [
            wintypes.HANDLE,
            wintypes.DWORD,
            wintypes.LPWSTR,
            ctypes.POINTER(wintypes.DWORD),
        ]
        kernel32.QueryFullProcessImageNameW.restype = wintypes.BOOL
        kernel32.OpenProcess.argtypes = [
            wintypes.DWORD,
            wintypes.BOOL,
            wintypes.DWORD,
        ]
        kernel32.OpenProcess.restype = wintypes.HANDLE
        kernel32.GetExitCodeProcess.argtypes = [
            wintypes.HANDLE,
            ctypes.POINTER(wintypes.DWORD),
        ]
        kernel32.GetExitCodeProcess.restype = wintypes.BOOL
        kernel32.WaitForSingleObject.argtypes = [wintypes.HANDLE, wintypes.DWORD]
        kernel32.WaitForSingleObject.restype = wintypes.DWORD
        kernel32.TerminateProcess.argtypes = [wintypes.HANDLE, wintypes.UINT]
        kernel32.TerminateProcess.restype = wintypes.BOOL
        kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
        kernel32.CloseHandle.restype = wintypes.BOOL
        kernel32.CreateToolhelp32Snapshot.argtypes = [
            wintypes.DWORD,
            wintypes.DWORD,
        ]
        kernel32.CreateToolhelp32Snapshot.restype = wintypes.HANDLE
        kernel32.Process32FirstW.argtypes = [
            wintypes.HANDLE,
            ctypes.POINTER(self.PROCESSENTRY32W),
        ]
        kernel32.Process32FirstW.restype = wintypes.BOOL
        kernel32.Process32NextW.argtypes = [
            wintypes.HANDLE,
            ctypes.POINTER(self.PROCESSENTRY32W),
        ]
        kernel32.Process32NextW.restype = wintypes.BOOL

        user32.EnumWindows.argtypes = [self.WNDENUMPROC, wintypes.LPARAM]
        user32.EnumWindows.restype = wintypes.BOOL
        user32.GetWindowThreadProcessId.argtypes = [
            wintypes.HWND,
            ctypes.POINTER(wintypes.DWORD),
        ]
        user32.GetWindowThreadProcessId.restype = wintypes.DWORD
        user32.PostMessageW.argtypes = [
            wintypes.HWND,
            wintypes.UINT,
            wintypes.WPARAM,
            wintypes.LPARAM,
        ]
        user32.PostMessageW.restype = wintypes.BOOL

    def _error(self) -> OSError:
        """Handle the error step."""

        return OSError(int(self.ctypes.get_last_error()))

    def _is_valid_handle(self, handle: object) -> bool:
        """Return whether valid handle."""

        value = int(handle or 0)
        return value not in (0, self._invalid_handle)

    def close_handle(self, handle: int) -> None:
        """Handle the close handle step."""

        if handle:
            self.ctypes.set_last_error(0)
            if not self.kernel32.CloseHandle(handle):
                raise HandleCloseFailure(
                    int(self.ctypes.get_last_error())
                )

    def create_kill_on_close_job(self) -> int:
        """Handle the create kill on close job step."""

        handle = self.kernel32.CreateJobObjectW(None, None)
        if not self._is_valid_handle(handle):
            raise self._error()
        try:
            if not self.kernel32.SetHandleInformation(
                handle,
                self.HANDLE_FLAG_INHERIT,
                0,
            ):
                raise self._error()
            info = self.EXTENDED_LIMIT_INFORMATION()
            info.BasicLimitInformation.LimitFlags = (
                self.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
            )
            if not self.kernel32.SetInformationJobObject(
                handle,
                self.JobObjectExtendedLimitInformation,
                self.ctypes.byref(info),
                self.ctypes.sizeof(info),
            ):
                raise self._error()
            return int(handle)
        except BaseException:
            try:
                self.close_handle(int(handle))
            except BaseException:
                pass
            raise

    def _open_inheritable_file(
        self,
        path: str,
        *,
        access: int,
        disposition: int,
    ) -> int:
        """Handle the open inheritable file step."""

        security = self.SECURITY_ATTRIBUTES()
        security.nLength = self.ctypes.sizeof(security)
        security.bInheritHandle = True
        handle = self.kernel32.CreateFileW(
            path,
            access,
            self.FILE_SHARE_READ | self.FILE_SHARE_WRITE | self.FILE_SHARE_DELETE,
            self.ctypes.byref(security),
            disposition,
            self.FILE_ATTRIBUTE_NORMAL,
            None,
        )
        if not self._is_valid_handle(handle):
            raise self._error()
        return int(handle)

    def _created_process_result(self, process_info) -> _CreatedProcess:
        """Handle the created process result step."""

        return _CreatedProcess(
            pid=int(process_info.dwProcessId),
            process_handle=int(process_info.hProcess),
            thread_handle=int(process_info.hThread),
            _cleanup=lambda: self._cleanup_created_process_info(process_info),
        )

    def _cleanup_created_process_info(self, process_info) -> bool:
        """Handle the cleanup created process info step."""

        process_handle = int(getattr(process_info, "hProcess", 0) or 0)
        thread_handle = int(getattr(process_info, "hThread", 0) or 0)
        if not process_handle and not thread_handle:
            return True

        exited = process_handle <= 0
        handles_closed = True
        if process_handle:
            try:
                self.terminate_process(process_handle)
            except BaseException:
                pass
            try:
                exited = bool(self.wait_process(process_handle, 5.0))
            except BaseException:
                exited = False
        if thread_handle:
            try:
                self.close_handle(thread_handle)
            except BaseException:
                handles_closed = False
        if process_handle:
            try:
                self.close_handle(process_handle)
            except BaseException:
                handles_closed = False
        with contextlib.suppress(BaseException):
            process_info.hThread = 0
        with contextlib.suppress(BaseException):
            process_info.hProcess = 0
        return exited and handles_closed

    def _transfer_created_process_or_cleanup(
        self,
        process_info,
    ) -> _CreatedProcess:
        """Handle the transfer created process or cleanup step."""

        try:
            return self._created_process_result(process_info)
        except BaseException as exc:
            if self._cleanup_created_process_info(process_info):
                raise exc.with_traceback(exc.__traceback__)
            raise OSError("Created-process transfer cleanup failed.") from None

    def create_process_suspended(
        self,
        *,
        application_path: str,
        arguments: tuple[str, ...],
        command_line: str,
        cwd: str,
        environment: Mapping[str, str],
        stdout_log: str,
        stderr_log: str,
    ) -> _CreatedProcess:
        """Handle the create process suspended step."""

        del arguments
        ctypes = self.ctypes
        log_handles: list[int] = []
        attribute_list = None
        attribute_initialized = False
        process_info = self.PROCESS_INFORMATION()
        try:
            pathlib.Path(stdout_log).parent.mkdir(parents=True, exist_ok=True)
            pathlib.Path(stderr_log).parent.mkdir(parents=True, exist_ok=True)
            stdin_handle = self._open_inheritable_file(
                "NUL",
                access=self.GENERIC_READ,
                disposition=self.OPEN_EXISTING,
            )
            log_handles.append(stdin_handle)
            stdout_handle = self._open_inheritable_file(
                stdout_log,
                access=self.GENERIC_WRITE,
                disposition=self.CREATE_ALWAYS,
            )
            log_handles.append(stdout_handle)
            if _same_path(stdout_log, stderr_log):
                stderr_handle = stdout_handle
            else:
                stderr_handle = self._open_inheritable_file(
                    stderr_log,
                    access=self.GENERIC_WRITE,
                    disposition=self.CREATE_ALWAYS,
                )
                log_handles.append(stderr_handle)

            size = ctypes.c_size_t()
            self.kernel32.InitializeProcThreadAttributeList(None, 1, 0, ctypes.byref(size))
            if size.value <= 0:
                raise self._error()
            attribute_list = ctypes.create_string_buffer(size.value)
            if not self.kernel32.InitializeProcThreadAttributeList(
                ctypes.cast(attribute_list, self.wintypes.LPVOID),
                1,
                0,
                ctypes.byref(size),
            ):
                raise self._error()
            attribute_initialized = True

            inherited_values = tuple(dict.fromkeys(log_handles))
            inherited_array_type = self.wintypes.HANDLE * len(inherited_values)
            inherited_array = inherited_array_type(*inherited_values)
            if not self.kernel32.UpdateProcThreadAttribute(
                ctypes.cast(attribute_list, self.wintypes.LPVOID),
                0,
                self.PROC_THREAD_ATTRIBUTE_HANDLE_LIST,
                ctypes.cast(inherited_array, self.wintypes.LPVOID),
                ctypes.sizeof(inherited_array),
                None,
                None,
            ):
                raise self._error()

            startup = self.STARTUPINFOEXW()
            startup.StartupInfo.cb = ctypes.sizeof(startup)
            startup.StartupInfo.dwFlags = self.STARTF_USESTDHANDLES
            startup.StartupInfo.hStdInput = stdin_handle
            startup.StartupInfo.hStdOutput = stdout_handle
            startup.StartupInfo.hStdError = stderr_handle
            startup.lpAttributeList = ctypes.cast(
                attribute_list,
                self.wintypes.LPVOID,
            )

            command_buffer = ctypes.create_unicode_buffer(command_line)
            environment_text = "".join(
                f"{key}={value}\0"
                for key, value in sorted(
                    environment.items(),
                    key=lambda item: item[0].casefold(),
                )
            ) + "\0"
            environment_buffer = ctypes.create_unicode_buffer(environment_text)
            flags = (
                self.CREATE_SUSPENDED
                | self.CREATE_NEW_PROCESS_GROUP
                | self.CREATE_UNICODE_ENVIRONMENT
                | self.EXTENDED_STARTUPINFO_PRESENT
            )
            if not self.kernel32.CreateProcessW(
                application_path,
                command_buffer,
                None,
                None,
                True,
                flags,
                ctypes.cast(environment_buffer, self.wintypes.LPVOID),
                cwd,
                ctypes.cast(ctypes.byref(startup), ctypes.POINTER(self.STARTUPINFOW)),
                ctypes.byref(process_info),
            ):
                raise self._error()
            try:
                return self._transfer_created_process_or_cleanup(process_info)
            except BaseException:
                if int(getattr(process_info, "hProcess", 0) or 0):
                    self._cleanup_created_process_info(process_info)
                raise
        except BaseException as exc:
            if int(getattr(process_info, "hProcess", 0) or 0):
                if not self._cleanup_created_process_info(process_info):
                    raise OSError(
                        "Created-process interruption cleanup failed."
                    ) from None
            raise exc.with_traceback(exc.__traceback__)
        finally:
            if attribute_initialized and attribute_list is not None:
                with contextlib.suppress(BaseException):
                    self.kernel32.DeleteProcThreadAttributeList(
                        self.ctypes.cast(attribute_list, self.wintypes.LPVOID)
                    )
            for handle in tuple(dict.fromkeys(log_handles)):
                with contextlib.suppress(BaseException):
                    self.close_handle(handle)

    def assign_process_to_job(self, job_handle: int, process_handle: int) -> bool:
        """Handle the assign process to job step."""

        return bool(
            self.kernel32.AssignProcessToJobObject(job_handle, process_handle)
        )

    def is_process_in_job(self, process_handle: int, job_handle: int) -> bool:
        """Return whether process in job."""

        result = self.wintypes.BOOL()
        if not self.kernel32.IsProcessInJob(
            process_handle,
            job_handle,
            self.ctypes.byref(result),
        ):
            raise self._error()
        return bool(result.value)

    def resume_thread(self, thread_handle: int) -> bool:
        """Handle the resume thread step."""

        return int(self.kernel32.ResumeThread(thread_handle)) != 0xFFFFFFFF

    def terminate_process(self, process_handle: int) -> None:
        """Handle the terminate process step."""

        if not self.kernel32.TerminateProcess(process_handle, 184):
            raise self._error()

    def capture_process_identity(
        self,
        process_handle: int,
        pid: int,
    ) -> ProcessIdentity:
        """Capture process identity."""

        actual_pid = int(self.kernel32.GetProcessId(process_handle))
        if actual_pid <= 0 or actual_pid != int(pid):
            raise self._error()
        creation = self.FILETIME()
        exit_time = self.FILETIME()
        kernel_time = self.FILETIME()
        user_time = self.FILETIME()
        if not self.kernel32.GetProcessTimes(
            process_handle,
            self.ctypes.byref(creation),
            self.ctypes.byref(exit_time),
            self.ctypes.byref(kernel_time),
            self.ctypes.byref(user_time),
        ):
            raise self._error()
        capacity = 32_768
        path_buffer = self.ctypes.create_unicode_buffer(capacity)
        path_length = self.wintypes.DWORD(capacity)
        if not self.kernel32.QueryFullProcessImageNameW(
            process_handle,
            0,
            path_buffer,
            self.ctypes.byref(path_length),
        ):
            raise self._error()
        creation_ticks = (
            int(creation.dwHighDateTime) << 32
        ) | int(creation.dwLowDateTime)
        return ProcessIdentity(
            actual_pid,
            creation_ticks,
            path_buffer.value[: int(path_length.value)],
        )

    def job_member_pids(self, job_handle: int) -> tuple[int, ...]:
        """Handle the job member pids step."""

        ctypes = self.ctypes
        capacity = 16
        header_size = ctypes.sizeof(self.wintypes.DWORD) * 2
        while capacity <= MAX_JOB_MEMBERS:
            buffer_size = header_size + (capacity * ctypes.sizeof(ctypes.c_size_t))
            buffer = ctypes.create_string_buffer(buffer_size)
            return_length = self.wintypes.DWORD()
            ok = self.kernel32.QueryInformationJobObject(
                job_handle,
                self.JobObjectBasicProcessIdList,
                ctypes.cast(buffer, self.wintypes.LPVOID),
                buffer_size,
                ctypes.byref(return_length),
            )
            assigned = int.from_bytes(buffer.raw[0:4], "little")
            returned = int.from_bytes(buffer.raw[4:8], "little")
            if ok and returned <= capacity and returned >= assigned:
                array_type = ctypes.c_size_t * returned
                values = array_type.from_buffer(buffer, header_size)
                return tuple(int(value) for value in values)
            if assigned > MAX_JOB_MEMBERS:
                raise OSError("Job member bound exceeded.")
            if not ok and int(ctypes.get_last_error()) not in (0, 24, 122, 234):
                raise self._error()
            capacity = max(capacity * 2, assigned, returned, 1)
        raise OSError("Job member bound exceeded.")

    def open_process_for_query(self, pid: int) -> int:
        """Handle the open process for query step."""

        self.ctypes.set_last_error(0)
        handle = self.kernel32.OpenProcess(
            self.PROCESS_QUERY_LIMITED_INFORMATION,
            False,
            int(pid),
        )
        if not self._is_valid_handle(handle):
            error = int(self.ctypes.get_last_error())
            raise ProcessOpenFailure(error)
        return int(handle)

    def process_id_exists(self, pid: int) -> bool:
        """Prove PID presence or absence through a separate read-only snapshot."""

        target_pid = int(pid)
        if target_pid <= 0 or target_pid > 0xFFFFFFFF:
            raise OSError("Process PID is invalid.")
        self.ctypes.set_last_error(0)
        snapshot = self.kernel32.CreateToolhelp32Snapshot(
            self.TH32CS_SNAPPROCESS,
            0,
        )
        if not self._is_valid_handle(snapshot):
            raise self._error()
        scanned = 0
        try:
            entry = self.PROCESSENTRY32W()
            entry.dwSize = self.ctypes.sizeof(entry)
            self.ctypes.set_last_error(0)
            if not self.kernel32.Process32FirstW(
                snapshot,
                self.ctypes.byref(entry),
            ):
                error = int(self.ctypes.get_last_error())
                if error == self.ERROR_NO_MORE_FILES:
                    return False
                raise OSError(error)
            while True:
                scanned += 1
                if scanned > MAX_ENUMERATED_PROCESSES:
                    raise OSError("Process enumeration bound exceeded.")
                if int(entry.th32ProcessID) == target_pid:
                    return True
                entry.dwSize = self.ctypes.sizeof(entry)
                self.ctypes.set_last_error(0)
                if not self.kernel32.Process32NextW(
                    snapshot,
                    self.ctypes.byref(entry),
                ):
                    error = int(self.ctypes.get_last_error())
                    if error == self.ERROR_NO_MORE_FILES:
                        return False
                    raise OSError(error)
        finally:
            self.close_handle(int(snapshot))

    def query_process_identity(self, pid: int) -> ProcessIdentity | None:
        """Handle the query process identity step."""

        try:
            handle = self.open_process_for_query(pid)
        except OSError:
            return None
        if not handle:
            return None
        try:
            return self.capture_process_identity(handle, int(pid))
        except (OSError, OwnershipFailure):
            return None
        finally:
            self.close_handle(handle)

    def enumerate_process_identities(self) -> tuple[ProcessIdentity, ...]:
        """Handle the enumerate process identities step."""

        snapshot = self.kernel32.CreateToolhelp32Snapshot(
            self.TH32CS_SNAPPROCESS,
            0,
        )
        if not self._is_valid_handle(snapshot):
            raise self._error()
        identities: list[ProcessIdentity] = []
        try:
            entry = self.PROCESSENTRY32W()
            entry.dwSize = self.ctypes.sizeof(entry)
            self.ctypes.set_last_error(0)
            if not self.kernel32.Process32FirstW(snapshot, self.ctypes.byref(entry)):
                error = int(self.ctypes.get_last_error())
                if error == self.ERROR_NO_MORE_FILES:
                    return ()
                raise OSError(error)
            while True:
                if len(identities) >= MAX_ENUMERATED_PROCESSES:
                    raise OSError("Process enumeration bound exceeded.")
                pid = int(entry.th32ProcessID)
                if pid > 0:
                    identity = self.query_process_identity(pid)
                    if identity is not None:
                        identities.append(identity)
                entry.dwSize = self.ctypes.sizeof(entry)
                self.ctypes.set_last_error(0)
                if not self.kernel32.Process32NextW(
                    snapshot,
                    self.ctypes.byref(entry),
                ):
                    error = int(self.ctypes.get_last_error())
                    if error == self.ERROR_NO_MORE_FILES:
                        break
                    raise OSError(error)
            return tuple(identities)
        finally:
            self.close_handle(int(snapshot))

    def post_close_to_top_level_windows(
        self,
        expected_identities: tuple[ProcessIdentity, ...],
        job_handle: int,
    ) -> int:
        """Handle the post close to top level windows step."""

        expected_by_pid = {
            identity.pid: identity for identity in expected_identities
        }
        if (
            len(expected_by_pid) != len(expected_identities)
            or len(expected_by_pid) > MAX_JOB_MEMBERS
        ):
            raise OSError("Window target bound exceeded.")
        posted = 0
        callback_error: Exception | None = None

        @self.WNDENUMPROC
        def visit(window, _parameter):
            """Handle the visit step."""

            nonlocal callback_error, posted
            pid = self.wintypes.DWORD()
            self.user32.GetWindowThreadProcessId(window, self.ctypes.byref(pid))
            expected = expected_by_pid.get(int(pid.value))
            if expected is None:
                return True

            process_handle = 0
            try:
                process_handle = self.open_process_for_query(expected.pid)
                if not process_handle:
                    raise OSError("Window process could not be opened.")
                if not self.is_process_in_job(process_handle, job_handle):
                    raise OSError("Window process is outside the exact Job.")
                current = self.capture_process_identity(
                    process_handle,
                    expected.pid,
                )
                if not _identities_match(current, expected):
                    raise OSError("Window process identity changed.")
                if not self.user32.PostMessageW(window, self.WM_CLOSE, 0, 0):
                    raise self._error()
                posted += 1
            except Exception as exc:
                callback_error = exc
                return False
            finally:
                if process_handle:
                    with contextlib.suppress(Exception):
                        self.close_handle(process_handle)
            return True

        enum_ok = bool(self.user32.EnumWindows(visit, 0))
        if callback_error is not None:
            raise callback_error
        if not enum_ok:
            raise self._error()
        return posted

    def poll_process(self, process_handle: int) -> int | None:
        """Handle the poll process step."""

        exit_code = self.wintypes.DWORD()
        if not self.kernel32.GetExitCodeProcess(
            process_handle,
            self.ctypes.byref(exit_code),
        ):
            raise self._error()
        return None if int(exit_code.value) == self.STILL_ACTIVE else int(exit_code.value)

    def wait_process(self, process_handle: int, timeout_seconds: float) -> bool:
        """Handle the wait process step."""

        timeout = _validate_timeout(timeout_seconds)
        milliseconds = min(
            0xFFFFFFFE,
            int(math.ceil(timeout * 1000.0)),
        )
        result = int(
            self.kernel32.WaitForSingleObject(process_handle, milliseconds)
        )
        if result == self.WAIT_OBJECT_0:
            return True
        if result == self.WAIT_TIMEOUT:
            return False
        raise self._error()

    def wait_identity(
        self,
        identity: ProcessIdentity,
        timeout_seconds: float,
    ) -> bool:
        """Handle the wait identity step."""

        self.ctypes.set_last_error(0)
        handle = self.kernel32.OpenProcess(
            self.SYNCHRONIZE | self.PROCESS_QUERY_LIMITED_INFORMATION,
            False,
            identity.pid,
        )
        if not self._is_valid_handle(handle):
            error = int(self.ctypes.get_last_error())
            if error == self.ERROR_INVALID_PARAMETER:
                return True
            raise ProcessOpenFailure(error)
        try:
            current = self.capture_process_identity(int(handle), identity.pid)
            if current != identity:
                return True
            return self.wait_process(int(handle), timeout_seconds)
        finally:
            self.close_handle(int(handle))
