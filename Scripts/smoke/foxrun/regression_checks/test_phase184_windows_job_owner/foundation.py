#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Regressions for assign-before-resume Windows Desktop process ownership."""
from __future__ import annotations
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "test_phase184_windows_job_owner.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
import ctypes
import dataclasses
import json
import os
import pathlib
import subprocess
import sys
import tempfile
import time
import unittest
from types import SimpleNamespace
ROOT = pathlib.Path(__file__).resolve().parents[4]
TEST_ROOT = ROOT / "build" / "Tests" / "Phase184HWindowsJobOwner"
DESKTOP_PATH = r"C:\Program Files\Foxglove\foxglove.exe"
OTHER_PATH = r"C:\Windows\System32\cmd.exe"
class _InjectedProcessOpenFailure(OSError):
    """Represent the injected process open failure contract."""

    def __init__(self, win32_error: int):
        """Initialize the injected process open failure."""

        self.win32_error = int(win32_error)
        super().__init__(self.win32_error, "injected process-open failure")
try:
    from Scripts.smoke.foxrun import phase184_windows_job_owner as job_owner
except ImportError:
    job_owner = None
class WindowsJobOwnerModuleRedTests(unittest.TestCase):
    """Exercise the windows job owner module red tests behavior."""

    def test_windows_job_owner_module_exists(self):
        """Verify windows job owner module exists."""

        self.assertIsNotNone(
            job_owner,
            "Phase184-H Windows Job ownership module is not implemented.",
        )
@unittest.skipIf(job_owner is None, "Windows Job ownership module is not implemented.")
class FakeWindowsApi:
    """Deterministic API seam; only the ownership-critical calls enter trace."""

    def __init__(self):
        """Initialize the fake windows API."""

        self.trace: list[str] = []
        self.created = SimpleNamespace(
            pid=4242,
            process_handle=202,
            thread_handle=303,
        )
        self.launch_identity = job_owner.ProcessIdentity(
            4242,
            123_456_789,
            DESKTOP_PATH,
        )
        self.member_pids = (4242,)
        self.identities_by_pid = {4242: self.launch_identity}
        self.enumerated_identities = (self.launch_identity,)
        self.resumed = False
        self.terminated_handles: list[int] = []
        self.closed_handles: list[int] = []
        self.close_failures_by_handle: dict[int, BaseException] = {}
        self.close_requests: list[tuple[int, ...]] = []
        self.window_candidates: tuple[object, ...] = ()
        self.window_membership_by_pid: dict[int, bool] = {}
        self.window_posted_pids: list[int] = []
        self.query_handles_by_pid: dict[int, int] = {}
        self.query_pid_by_handle: dict[int, int] = {}
        self.open_failures_by_pid: dict[int, BaseException] = {}
        self.pid_exists_by_pid: dict[int, bool] = {}
        self.pid_existence_queries: list[int] = []
        self.membership_by_pid: dict[int, bool] = {}
        self.wait_results: dict[int, bool] = {202: True}
        self.poll_results: dict[int, int | None] = {202: None}
        self.poll_failures_by_handle: dict[int, BaseException] = {}
        self.waited_handles: list[int] = []
        self.last_create: dict[str, object] | None = None
        self.fail_operation: str | None = None
        self.interrupt_operation: str | None = None
        self.interrupt_type: type[BaseException] = KeyboardInterrupt

    def _fail_if_requested(self, operation: str) -> None:
        """Handle the fail if requested step."""

        if self.fail_operation == operation:
            raise OSError(5, "raw secret\r\n" + ("x" * 2048))

    def _interrupt_if_requested(self, operation: str) -> None:
        """Handle the interrupt if requested step."""

        if self.interrupt_operation == operation:
            raise self.interrupt_type(f"injected {operation}")

    def create_kill_on_close_job(self) -> int:
        """Handle the create kill on close job step."""

        self.trace.append("CreateJob")
        self._fail_if_requested("create_job")
        return 101

    def create_process_suspended(self, **kwargs):
        """Handle the create process suspended step."""

        self.trace.append("CreateProcess suspended")
        self._fail_if_requested("create_process")
        self.last_create = dict(kwargs)
        return self.created

    def assign_process_to_job(self, job_handle: int, process_handle: int) -> bool:
        """Handle the assign process to job step."""

        self.trace.append("Assign")
        self._fail_if_requested("assign")
        self._interrupt_if_requested("assign")
        return self.fail_operation != "assign_false"

    def capture_process_identity(
        self,
        process_handle: int,
        pid: int,
    ):
        """Capture process identity."""

        self.trace.append("capture identity")
        self._fail_if_requested("capture_identity")
        self._interrupt_if_requested("capture_identity")
        if process_handle == self.created.process_handle:
            return self.launch_identity
        return self.identities_by_pid.get(pid)

    def job_member_pids(self, job_handle: int) -> tuple[int, ...]:
        """Handle the job member pids step."""

        self.trace.append("membership")
        self._fail_if_requested("membership")
        return tuple(self.member_pids)

    def resume_thread(self, thread_handle: int) -> bool:
        """Handle the resume thread step."""

        self.trace.append("Resume")
        self._fail_if_requested("resume")
        self._interrupt_if_requested("resume_before")
        self.resumed = True
        self._interrupt_if_requested("resume_after")
        return True

    def query_process_identity(self, pid: int):
        """Handle the query process identity step."""

        return self.identities_by_pid.get(pid)

    def open_process_for_query(self, pid: int) -> int:
        """Handle the open process for query step."""

        failure = self.open_failures_by_pid.get(pid)
        if failure is not None:
            raise failure
        handle = self.query_handles_by_pid.setdefault(pid, 100_000 + pid)
        self.query_pid_by_handle[handle] = pid
        return handle

    def process_id_exists(self, pid: int) -> bool:
        """Handle the process id exists step."""

        self.pid_existence_queries.append(pid)
        return self.pid_exists_by_pid.get(
            pid,
            pid in self.identities_by_pid,
        )

    def is_process_in_job(self, process_handle: int, job_handle: int) -> bool:
        """Return whether process in job."""

        del job_handle
        pid = self.query_pid_by_handle.get(
            process_handle,
            self.created.pid if process_handle == self.created.process_handle else 0,
        )
        if process_handle == self.created.process_handle:
            self.trace.append("exact membership")
            self._interrupt_if_requested("exact_membership")
        return self.membership_by_pid.get(pid, pid in self.member_pids)

    def enumerate_process_identities(self):
        """Handle the enumerate process identities step."""

        return tuple(self.enumerated_identities)

    def post_close_to_top_level_windows(
        self,
        expected_identities,
        job_handle: int | None = None,
    ) -> int:
        """Handle the post close to top level windows step."""

        if job_handle is None:
            pids = tuple(expected_identities)
            self.close_requests.append(pids)
            self.window_posted_pids.extend(pids)
            return len(pids)

        expected_by_pid = {
            identity.pid: identity for identity in expected_identities
        }
        candidates = self.window_candidates or tuple(expected_identities)
        posted: list[int] = []
        for candidate in candidates:
            expected = expected_by_pid.get(candidate.pid)
            if expected is None:
                continue
            if not self.window_membership_by_pid.get(candidate.pid, True):
                raise OSError(5, "window process is external")
            if (
                candidate.pid != expected.pid
                or candidate.creation_time_100ns != expected.creation_time_100ns
                or not job_owner.protocol.windows_paths_equal(
                    candidate.executable,
                    expected.executable,
                )
            ):
                raise OSError(5, "window process identity changed")
            posted.append(candidate.pid)
        self.window_posted_pids.extend(posted)
        self.close_requests.append(tuple(posted))
        return len(posted)

    def wait_process(self, process_handle: int, timeout_seconds: float) -> bool:
        """Handle the wait process step."""

        self.waited_handles.append(process_handle)
        return self.wait_results.get(process_handle, False)

    def poll_process(self, process_handle: int) -> int | None:
        """Handle the poll process step."""

        failure = self.poll_failures_by_handle.get(process_handle)
        if failure is not None:
            raise failure
        return self.poll_results.get(process_handle)

    def terminate_process(self, process_handle: int) -> None:
        """Handle the terminate process step."""

        self.terminated_handles.append(process_handle)

    def close_handle(self, handle: int) -> None:
        """Handle the close handle step."""

        self.closed_handles.append(handle)
        failure = self.close_failures_by_handle.get(handle)
        if failure is not None:
            raise failure
class _ToolhelpEntry(ctypes.Structure):
    """Represent the toolhelp entry contract."""

    _fields_ = [
        ("dwSize", ctypes.c_uint32),
        ("th32ProcessID", ctypes.c_uint32),
    ]
class _LastErrorCtypes:
    """Represent the last error ctypes contract."""

    sizeof = staticmethod(ctypes.sizeof)
    byref = staticmethod(ctypes.byref)

    def __init__(self):
        """Initialize the last error ctypes."""

        self.last_error = 999
        self.set_calls: list[int] = []

    def set_last_error(self, value: int) -> None:
        """Handle the set last error step."""

        self.last_error = int(value)
        self.set_calls.append(int(value))

    def get_last_error(self) -> int:
        """Handle the get last error step."""

        return self.last_error
class _ToolhelpKernel:
    """Represent the toolhelp kernel contract."""

    def __init__(
        self,
        ctypes_api: _LastErrorCtypes,
        *,
        first_result: bool,
        first_error: int,
        next_result: bool = False,
        next_error: int = 18,
    ):
        """Initialize the toolhelp kernel."""

        self.ctypes_api = ctypes_api
        self.first_result = first_result
        self.first_error = first_error
        self.next_result = next_result
        self.next_error = next_error
        self.closed_handles: list[int] = []

    def CreateToolhelp32Snapshot(self, _flags: int, _pid: int) -> int:
        """Emulate the Win32 CreateToolhelp32Snapshot call."""

        return 606

    def Process32FirstW(self, _snapshot: int, entry_pointer) -> bool:
        """Emulate the Win32 Process32FirstW call."""

        self.ctypes_api.set_last_error(self.first_error)
        entry_pointer._obj.th32ProcessID = 0
        return self.first_result

    def Process32NextW(self, _snapshot: int, entry_pointer) -> bool:
        """Emulate the Win32 Process32NextW call."""

        self.ctypes_api.set_last_error(self.next_error)
        entry_pointer._obj.th32ProcessID = 0
        return self.next_result

    def CloseHandle(self, handle: int) -> bool:
        """Emulate the Win32 CloseHandle call."""

        self.closed_handles.append(int(handle))
        return True
class _JobConfigurationKernel:
    """Represent the job configuration kernel contract."""

    def __init__(
        self,
        interruption_operation: str,
        interruption: BaseException,
    ):
        """Initialize the job configuration kernel."""

        self.interruption_operation = interruption_operation
        self.interruption = interruption
        self.closed_handles: list[int] = []

    def CreateJobObjectW(self, _security, _name) -> int:
        """Emulate the Win32 CreateJobObjectW call."""

        return 707

    def SetHandleInformation(
        self,
        _handle: int,
        _mask: int,
        _flags: int,
    ) -> bool:
        """Emulate the Win32 SetHandleInformation call."""

        if self.interruption_operation == "SetHandleInformation":
            raise self.interruption
        return True

    def SetInformationJobObject(
        self,
        _handle: int,
        _information_class: int,
        _information,
        _information_size: int,
    ) -> bool:
        """Emulate the Win32 SetInformationJobObject call."""

        if self.interruption_operation == "SetInformationJobObject":
            raise self.interruption
        return True

    def CloseHandle(self, handle: int) -> bool:
        """Emulate the Win32 CloseHandle call."""

        self.closed_handles.append(int(handle))
        return True
class _BasicLimitInformation(ctypes.Structure):
    """Represent the basic limit information contract."""

    _fields_ = [("LimitFlags", ctypes.c_uint32)]
class _ExtendedLimitInformation(ctypes.Structure):
    """Represent the extended limit information contract."""

    _fields_ = [("BasicLimitInformation", _BasicLimitInformation)]


__all__ = [name for name in globals() if not name.startswith("__")]
