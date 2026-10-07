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

class _Win32ApiProcess:
    """Compatibility implementation section."""

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

del _Phase192Path

__all__ = [name for name in globals() if not name.startswith("__")]
