#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Phase184 Win32 ownership implementation section."""

from __future__ import annotations
from pathlib import Path as _Phase192Path
_PHASE192_FACADE_FILE = _Phase192Path(__file__).resolve().parents[1] / "phase184_windows_job_owner.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
from .foundation import *

class _Win32ApiWait:
    """Compatibility implementation section."""

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

del _Phase192Path

__all__ = ['_Win32ApiWait']
