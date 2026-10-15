#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Phase184 WindowsJobOwner implementation section."""

from __future__ import annotations
from pathlib import Path as _Phase192Path
_PHASE192_FACADE_FILE = _Phase192Path(__file__).resolve().parents[1] / "phase184_windows_job_owner.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
from .foundation import *

class _WindowsJobOwnerObservation:
    """Compatibility implementation section."""

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

del _Phase192Path

__all__ = ['_WindowsJobOwnerObservation']
