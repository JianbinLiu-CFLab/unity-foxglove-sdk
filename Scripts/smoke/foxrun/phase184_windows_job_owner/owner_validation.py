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

class _WindowsJobOwnerValidation:
    """Compatibility implementation section."""

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

del _Phase192Path

__all__ = [name for name in globals() if not name.startswith("__")]
