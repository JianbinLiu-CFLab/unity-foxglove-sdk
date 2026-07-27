#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Regressions for assign-before-resume Windows Desktop process ownership."""

from __future__ import annotations

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
    def __init__(self, win32_error: int):
        self.win32_error = int(win32_error)
        super().__init__(self.win32_error, "injected process-open failure")


try:
    from Scripts.smoke.foxrun import phase184_windows_job_owner as job_owner
except ImportError:
    job_owner = None


class WindowsJobOwnerModuleRedTests(unittest.TestCase):
    def test_windows_job_owner_module_exists(self):
        self.assertIsNotNone(
            job_owner,
            "Phase184-H Windows Job ownership module is not implemented.",
        )


@unittest.skipIf(job_owner is None, "Windows Job ownership module is not implemented.")
class FakeWindowsApi:
    """Deterministic API seam; only the ownership-critical calls enter trace."""

    def __init__(self):
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
        if self.fail_operation == operation:
            raise OSError(5, "raw secret\r\n" + ("x" * 2048))

    def _interrupt_if_requested(self, operation: str) -> None:
        if self.interrupt_operation == operation:
            raise self.interrupt_type(f"injected {operation}")

    def create_kill_on_close_job(self) -> int:
        self.trace.append("CreateJob")
        self._fail_if_requested("create_job")
        return 101

    def create_process_suspended(self, **kwargs):
        self.trace.append("CreateProcess suspended")
        self._fail_if_requested("create_process")
        self.last_create = dict(kwargs)
        return self.created

    def assign_process_to_job(self, job_handle: int, process_handle: int) -> bool:
        self.trace.append("Assign")
        self._fail_if_requested("assign")
        self._interrupt_if_requested("assign")
        return self.fail_operation != "assign_false"

    def capture_process_identity(
        self,
        process_handle: int,
        pid: int,
    ):
        self.trace.append("capture identity")
        self._fail_if_requested("capture_identity")
        self._interrupt_if_requested("capture_identity")
        if process_handle == self.created.process_handle:
            return self.launch_identity
        return self.identities_by_pid.get(pid)

    def job_member_pids(self, job_handle: int) -> tuple[int, ...]:
        self.trace.append("membership")
        self._fail_if_requested("membership")
        return tuple(self.member_pids)

    def resume_thread(self, thread_handle: int) -> bool:
        self.trace.append("Resume")
        self._fail_if_requested("resume")
        self._interrupt_if_requested("resume_before")
        self.resumed = True
        self._interrupt_if_requested("resume_after")
        return True

    def query_process_identity(self, pid: int):
        return self.identities_by_pid.get(pid)

    def open_process_for_query(self, pid: int) -> int:
        failure = self.open_failures_by_pid.get(pid)
        if failure is not None:
            raise failure
        handle = self.query_handles_by_pid.setdefault(pid, 100_000 + pid)
        self.query_pid_by_handle[handle] = pid
        return handle

    def process_id_exists(self, pid: int) -> bool:
        self.pid_existence_queries.append(pid)
        return self.pid_exists_by_pid.get(
            pid,
            pid in self.identities_by_pid,
        )

    def is_process_in_job(self, process_handle: int, job_handle: int) -> bool:
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
        return tuple(self.enumerated_identities)

    def post_close_to_top_level_windows(
        self,
        expected_identities,
        job_handle: int | None = None,
    ) -> int:
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
        self.waited_handles.append(process_handle)
        return self.wait_results.get(process_handle, False)

    def poll_process(self, process_handle: int) -> int | None:
        failure = self.poll_failures_by_handle.get(process_handle)
        if failure is not None:
            raise failure
        return self.poll_results.get(process_handle)

    def terminate_process(self, process_handle: int) -> None:
        self.terminated_handles.append(process_handle)

    def close_handle(self, handle: int) -> None:
        self.closed_handles.append(handle)
        failure = self.close_failures_by_handle.get(handle)
        if failure is not None:
            raise failure


class _ToolhelpEntry(ctypes.Structure):
    _fields_ = [
        ("dwSize", ctypes.c_uint32),
        ("th32ProcessID", ctypes.c_uint32),
    ]


class _LastErrorCtypes:
    sizeof = staticmethod(ctypes.sizeof)
    byref = staticmethod(ctypes.byref)

    def __init__(self):
        self.last_error = 999
        self.set_calls: list[int] = []

    def set_last_error(self, value: int) -> None:
        self.last_error = int(value)
        self.set_calls.append(int(value))

    def get_last_error(self) -> int:
        return self.last_error


class _ToolhelpKernel:
    def __init__(
        self,
        ctypes_api: _LastErrorCtypes,
        *,
        first_result: bool,
        first_error: int,
        next_result: bool = False,
        next_error: int = 18,
    ):
        self.ctypes_api = ctypes_api
        self.first_result = first_result
        self.first_error = first_error
        self.next_result = next_result
        self.next_error = next_error
        self.closed_handles: list[int] = []

    def CreateToolhelp32Snapshot(self, _flags: int, _pid: int) -> int:
        return 606

    def Process32FirstW(self, _snapshot: int, entry_pointer) -> bool:
        self.ctypes_api.set_last_error(self.first_error)
        entry_pointer._obj.th32ProcessID = 0
        return self.first_result

    def Process32NextW(self, _snapshot: int, entry_pointer) -> bool:
        self.ctypes_api.set_last_error(self.next_error)
        entry_pointer._obj.th32ProcessID = 0
        return self.next_result

    def CloseHandle(self, handle: int) -> bool:
        self.closed_handles.append(int(handle))
        return True


class _JobConfigurationKernel:
    def __init__(
        self,
        interruption_operation: str,
        interruption: BaseException,
    ):
        self.interruption_operation = interruption_operation
        self.interruption = interruption
        self.closed_handles: list[int] = []

    def CreateJobObjectW(self, _security, _name) -> int:
        return 707

    def SetHandleInformation(
        self,
        _handle: int,
        _mask: int,
        _flags: int,
    ) -> bool:
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
        if self.interruption_operation == "SetInformationJobObject":
            raise self.interruption
        return True

    def CloseHandle(self, handle: int) -> bool:
        self.closed_handles.append(int(handle))
        return True


class _BasicLimitInformation(ctypes.Structure):
    _fields_ = [("LimitFlags", ctypes.c_uint32)]


class _ExtendedLimitInformation(ctypes.Structure):
    _fields_ = [("BasicLimitInformation", _BasicLimitInformation)]


@unittest.skipIf(job_owner is None, "Windows Job ownership module is not implemented.")
class WindowsJobOwnerPureTests(unittest.TestCase):
    def make_owner(self, api: FakeWindowsApi | None = None, **owner_options):
        selected_api = api or FakeWindowsApi()
        return (
            job_owner.WindowsJobOwner(
                DESKTOP_PATH,
                api=selected_api,
                platform_name="nt",
                **owner_options,
            ),
            selected_api,
        )

    def launch(self, owner):
        return owner.launch_suspended_owned(
            DESKTOP_PATH,
            ("--open", r"D:\Evidence Folder\result.mcap"),
            cwd=r"D:\Phase184H",
            environment={"PATH": r"C:\Windows", "PHASE184": "H"},
            stdout_log=r"D:\Phase184H\desktop.stdout.log",
            stderr_log=r"D:\Phase184H\desktop.stderr.log",
        )

    def assert_ownership_failure(self, action, expected_code: str):
        with self.assertRaises(job_owner.OwnershipFailure) as caught:
            action()
        self.assertEqual(expected_code, caught.exception.code)
        self.assertLessEqual(
            len(caught.exception.message),
            job_owner.MAX_DIAGNOSTIC_CHARACTERS,
        )
        self.assertNotIn("\r", caught.exception.message)
        self.assertNotIn("\n", caught.exception.message)
        return caught.exception

    def make_toolhelp_enumerator(
        self,
        *,
        first_result: bool,
        first_error: int,
        next_result: bool = False,
        next_error: int = 18,
    ):
        ctypes_api = _LastErrorCtypes()
        kernel = _ToolhelpKernel(
            ctypes_api,
            first_result=first_result,
            first_error=first_error,
            next_result=next_result,
            next_error=next_error,
        )
        api = object.__new__(job_owner._Win32Api)
        api.ctypes = ctypes_api
        api.kernel32 = kernel
        api.PROCESSENTRY32W = _ToolhelpEntry
        api._invalid_handle = -1
        api.query_process_identity = lambda _pid: None
        return api, ctypes_api, kernel

    def test_process_identity_is_immutable_and_requires_exact_absolute_windows_path(self):
        identity = job_owner.ProcessIdentity(7, 9001, DESKTOP_PATH)
        self.assertEqual(7, identity.pid)
        with self.assertRaises(dataclasses.FrozenInstanceError):
            identity.pid = 8

        for values in (
            (0, 1, DESKTOP_PATH),
            (1, 0, DESKTOP_PATH),
            (1, 1, "foxglove.exe"),
            (1, 1, "/usr/bin/foxglove"),
        ):
            with self.subTest(values=values):
                self.assert_ownership_failure(
                    lambda values=values: job_owner.ProcessIdentity(*values),
                    job_owner.FAIL_PROCESS_IDENTITY,
                )

    def test_production_operation_fails_closed_off_windows_but_fake_windows_is_lazy(self):
        self.assert_ownership_failure(
            lambda: job_owner.WindowsJobOwner(
                DESKTOP_PATH,
                platform_name="posix",
            ),
            job_owner.FAIL_WINDOWS_REQUIRED,
        )
        owner, api = self.make_owner()
        self.assertEqual(["CreateJob"], api.trace)
        owner.close()

    def test_job_configuration_base_exceptions_close_empty_job_once_and_propagate(self):
        for operation in (
            "SetHandleInformation",
            "SetInformationJobObject",
        ):
            for interruption_type in (KeyboardInterrupt, SystemExit):
                with self.subTest(
                    operation=operation,
                    interruption=interruption_type.__name__,
                ):
                    interruption = interruption_type(
                        f"injected {operation}"
                    )
                    ctypes_api = _LastErrorCtypes()
                    kernel = _JobConfigurationKernel(
                        operation,
                        interruption,
                    )
                    api = object.__new__(job_owner._Win32Api)
                    api.ctypes = ctypes_api
                    api.kernel32 = kernel
                    api.EXTENDED_LIMIT_INFORMATION = (
                        _ExtendedLimitInformation
                    )
                    api._invalid_handle = -1

                    with self.assertRaises(interruption_type) as caught:
                        api.create_kill_on_close_job()

                    self.assertIs(interruption, caught.exception)
                    self.assertEqual([707], kernel.closed_handles)

    def test_raw_close_handle_failure_preserves_win32_error(self):
        ctypes_api = _LastErrorCtypes()

        def fail_close(_handle: int) -> bool:
            ctypes_api.set_last_error(6)
            return False

        api = object.__new__(job_owner._Win32Api)
        api.ctypes = ctypes_api
        api.kernel32 = SimpleNamespace(CloseHandle=fail_close)

        with self.assertRaises(OSError) as caught:
            api.close_handle(707)

        self.assertEqual(6, caught.exception.win32_error)
        self.assertLessEqual(
            len(str(caught.exception)),
            job_owner.MAX_DIAGNOSTIC_CHARACTERS,
        )

    def test_launch_orders_create_assign_identity_membership_before_resume(self):
        owner, api = self.make_owner()
        identity = self.launch(owner)

        self.assertEqual(api.launch_identity, identity)
        self.assertEqual(
            [
                "CreateJob",
                "CreateProcess suspended",
                "Assign",
                "exact membership",
                "capture identity",
                "membership",
                "Resume",
            ],
            api.trace,
        )
        self.assertTrue(api.resumed)
        self.assertEqual(DESKTOP_PATH, api.last_create["application_path"])
        self.assertEqual(
            subprocess.list2cmdline(
                (
                    DESKTOP_PATH,
                    "--open",
                    r"D:\Evidence Folder\result.mcap",
                )
            ),
            api.last_create["command_line"],
        )
        self.assertEqual(
            {"PATH": r"C:\Windows", "PHASE184": "H"},
            api.last_create["environment"],
        )
        owner.close()

    def test_launch_thread_handle_close_failure_uses_cleanup_code(self):
        owner, api = self.make_owner()
        api.close_failures_by_handle[api.created.thread_handle] = OSError(
            6,
            "injected thread-handle close failure",
        )

        with self.assertRaises(job_owner.OwnershipFailure) as caught:
            self.launch(owner)

        self.assertEqual(job_owner.FAIL_CLEANUP, caught.exception.code)
        self.assertTrue(api.resumed)
        self.assertEqual(
            [api.created.process_handle],
            api.terminated_handles,
        )
        owner.close()

    def test_assignment_failure_never_resumes_and_terminates_only_created_root(self):
        api = FakeWindowsApi()
        api.fail_operation = "assign_false"
        owner, _ = self.make_owner(api)

        self.assert_ownership_failure(
            lambda: self.launch(owner),
            job_owner.FAIL_PROCESS_ASSIGN,
        )

        self.assertFalse(api.resumed)
        self.assertNotIn("capture identity", api.trace)
        self.assertNotIn("Resume", api.trace)
        self.assertEqual([api.created.process_handle], api.terminated_handles)
        self.assertIn(api.created.process_handle, api.closed_handles)
        self.assertIn(api.created.thread_handle, api.closed_handles)
        owner.close()

    def test_invalid_created_root_result_is_closed_without_resume(self):
        api = FakeWindowsApi()
        api.created = SimpleNamespace(
            pid=0,
            process_handle=202,
            thread_handle=303,
        )
        owner, _ = self.make_owner(api)

        self.assert_ownership_failure(
            lambda: self.launch(owner),
            job_owner.FAIL_PROCESS_CREATE,
        )

        self.assertFalse(api.resumed)
        self.assertEqual([api.created.process_handle], api.terminated_handles)
        self.assertIn(api.created.process_handle, api.closed_handles)
        self.assertIn(api.created.thread_handle, api.closed_handles)
        owner.close()

    def test_identity_or_membership_failure_never_resumes_created_root(self):
        for operation, code in (
            ("capture_identity", job_owner.FAIL_PROCESS_IDENTITY),
            ("membership", job_owner.FAIL_PROCESS_OWNERSHIP),
        ):
            with self.subTest(operation=operation):
                api = FakeWindowsApi()
                api.fail_operation = operation
                owner, _ = self.make_owner(api)
                self.assert_ownership_failure(lambda: self.launch(owner), code)
                self.assertFalse(api.resumed)
                self.assertEqual([api.created.process_handle], api.terminated_handles)
                owner.close()

    def test_base_exceptions_at_launch_boundaries_cleanup_then_propagate(self):
        operations = (
            "assign",
            "exact_membership",
            "capture_identity",
            "resume_before",
            "resume_after",
        )
        for operation in operations:
            for interruption in (KeyboardInterrupt, SystemExit):
                with self.subTest(
                    operation=operation,
                    interruption=interruption.__name__,
                ):
                    api = FakeWindowsApi()
                    api.interrupt_operation = operation
                    api.interrupt_type = interruption
                    owner, _ = self.make_owner(api)

                    with self.assertRaises(interruption):
                        self.launch(owner)

                    self.assertEqual(
                        [api.created.process_handle],
                        api.terminated_handles,
                    )
                    self.assertIn(
                        api.created.process_handle,
                        api.waited_handles,
                    )
                    self.assertIn(
                        api.created.process_handle,
                        api.closed_handles,
                    )
                    self.assertIn(
                        api.created.thread_handle,
                        api.closed_handles,
                    )
                    if operation != "resume_after":
                        self.assertFalse(api.resumed)
                    owner.close()

    def test_low_level_created_process_transfer_interrupt_cleans_every_handle(self):
        for interruption in (KeyboardInterrupt, SystemExit):
            with self.subTest(interruption=interruption.__name__):
                api = object.__new__(job_owner._Win32Api)
                terminated: list[int] = []
                waited: list[int] = []
                closed: list[int] = []
                process_info = SimpleNamespace(
                    hProcess=202,
                    hThread=303,
                    dwProcessId=4242,
                )

                def interrupt_result(_process_info):
                    raise interruption("injected low-level transfer")

                api._created_process_result = interrupt_result
                api.terminate_process = terminated.append
                api.wait_process = (
                    lambda handle, _timeout: waited.append(handle) or True
                )
                api.close_handle = closed.append
                transfer = getattr(
                    api,
                    "_transfer_created_process_or_cleanup",
                    None,
                )
                self.assertTrue(
                    callable(transfer),
                    "Low-level CreateProcess transfer must be BaseException-safe.",
                )

                with self.assertRaises(interruption):
                    transfer(process_info)

                self.assertEqual([202], terminated)
                self.assertEqual([202], waited)
                self.assertCountEqual([202, 303], closed)

    def test_selected_path_mismatch_before_resume_fails_closed(self):
        api = FakeWindowsApi()
        api.launch_identity = job_owner.ProcessIdentity(
            4242,
            123_456_789,
            OTHER_PATH,
        )
        owner, _ = self.make_owner(api)

        self.assert_ownership_failure(
            lambda: self.launch(owner),
            job_owner.FAIL_PROCESS_IDENTITY,
        )
        self.assertFalse(api.resumed)
        self.assertEqual([api.created.process_handle], api.terminated_handles)
        owner.close()

    def test_require_owned_identity_rejects_path_time_membership_and_pid_reuse(self):
        mutations = (
            (
                "missing-membership",
                (),
                job_owner.ProcessIdentity(4242, 123_456_789, DESKTOP_PATH),
                job_owner.FAIL_DESKTOP_HANDOFF,
            ),
            (
                "path",
                (4242,),
                job_owner.ProcessIdentity(4242, 123_456_789, OTHER_PATH),
                job_owner.FAIL_PROCESS_IDENTITY,
            ),
            (
                "creation-time",
                (4242,),
                job_owner.ProcessIdentity(4242, 123_456_790, DESKTOP_PATH),
                job_owner.FAIL_PROCESS_IDENTITY,
            ),
            (
                "pid-reuse",
                (4242,),
                job_owner.ProcessIdentity(4242, 999_999_999, DESKTOP_PATH),
                job_owner.FAIL_PROCESS_IDENTITY,
            ),
        )
        for label, member_pids, current, code in mutations:
            with self.subTest(label=label):
                owner, api = self.make_owner()
                launched = self.launch(owner)
                api.member_pids = member_pids
                api.identities_by_pid[launched.pid] = current
                self.assert_ownership_failure(
                    lambda launched=launched: owner.require_owned_identity(launched),
                    code,
                )
                owner.close()

    def test_untracked_descendant_snapshot_pid_reuse_requires_exact_handle_job_membership(self):
        owner, api = self.make_owner()
        replacement = job_owner.ProcessIdentity(
            7000,
            987_654_321,
            DESKTOP_PATH,
        )
        api.member_pids = (replacement.pid,)
        api.identities_by_pid[replacement.pid] = replacement
        api.membership_by_pid[replacement.pid] = False

        self.assert_ownership_failure(
            owner.members,
            job_owner.FAIL_PROCESS_OWNERSHIP,
        )

        self.assertEqual([], api.close_requests)
        self.assertEqual([], api.window_posted_pids)
        owner.close()

    def test_member_snapshot_skips_only_a_pid_proven_gone_before_open(self):
        owner, api = self.make_owner()
        launched = self.launch(owner)
        vanished = job_owner.ProcessIdentity(
            7001,
            987_654_322,
            OTHER_PATH,
        )
        api.member_pids = (launched.pid, vanished.pid)
        api.identities_by_pid[vanished.pid] = vanished
        api.open_failures_by_pid[vanished.pid] = (
            _InjectedProcessOpenFailure(87)
        )
        api.pid_exists_by_pid[vanished.pid] = False

        self.assertEqual((launched,), owner.members())
        self.assertEqual([vanished.pid], api.pid_existence_queries)
        owner.close()

    def test_member_snapshot_open_failure_still_fails_if_pid_exists(self):
        owner, api = self.make_owner()
        launched = self.launch(owner)
        inaccessible = job_owner.ProcessIdentity(
            7002,
            987_654_323,
            OTHER_PATH,
        )
        api.member_pids = (launched.pid, inaccessible.pid)
        api.identities_by_pid[inaccessible.pid] = inaccessible
        api.open_failures_by_pid[inaccessible.pid] = (
            _InjectedProcessOpenFailure(5)
        )
        api.pid_exists_by_pid[inaccessible.pid] = True

        self.assert_ownership_failure(
            owner.members,
            job_owner.FAIL_PROCESS_OWNERSHIP,
        )
        self.assertEqual([inaccessible.pid], api.pid_existence_queries)
        owner.close()

    def test_member_snapshot_skips_identity_capture_only_after_handle_exit(self):
        owner, api = self.make_owner()
        launched = self.launch(owner)
        vanished_pid = 7003
        vanished_handle = 100_000 + vanished_pid
        api.member_pids = (launched.pid, vanished_pid)
        api.membership_by_pid[vanished_pid] = True
        api.poll_results[vanished_handle] = 0

        self.assertEqual((launched,), owner.members())
        self.assertIn(vanished_handle, api.closed_handles)
        owner.close()

    def test_member_snapshot_skips_membership_race_only_after_handle_exit(self):
        owner, api = self.make_owner()
        launched = self.launch(owner)
        vanished = job_owner.ProcessIdentity(
            7004,
            987_654_324,
            OTHER_PATH,
        )
        vanished_handle = 100_000 + vanished.pid
        api.member_pids = (launched.pid, vanished.pid)
        api.identities_by_pid[vanished.pid] = vanished
        api.membership_by_pid[vanished.pid] = False
        api.poll_results[vanished_handle] = 0

        self.assertEqual((launched,), owner.members())
        self.assertIn(vanished_handle, api.closed_handles)
        owner.close()

    def test_member_snapshot_missing_live_identity_remains_fail_closed(self):
        owner, api = self.make_owner()
        launched = self.launch(owner)
        inaccessible_pid = 7005
        api.member_pids = (launched.pid, inaccessible_pid)
        api.membership_by_pid[inaccessible_pid] = True

        self.assert_ownership_failure(
            owner.members,
            job_owner.FAIL_PROCESS_IDENTITY,
        )
        owner.close()

    def test_member_snapshot_successful_identity_is_skipped_after_final_exit_poll(self):
        owner, api = self.make_owner()
        launched = self.launch(owner)
        query_handle = 100_000 + launched.pid
        api.poll_results[query_handle] = 0

        self.assertEqual((), owner.members())
        self.assert_ownership_failure(
            lambda: owner.require_owned_identity(launched),
            job_owner.FAIL_DESKTOP_HANDOFF,
        )
        self.assertIn(query_handle, api.closed_handles)
        owner.close()

    def test_member_snapshot_final_poll_error_or_invalid_result_fails_closed(self):
        for mode in ("error", "invalid"):
            with self.subTest(mode=mode):
                owner, api = self.make_owner()
                launched = self.launch(owner)
                query_handle = 100_000 + launched.pid
                if mode == "error":
                    api.poll_failures_by_handle[query_handle] = OSError(
                        5,
                        "injected final poll failure",
                    )
                else:
                    api.poll_results[query_handle] = "running"

                self.assert_ownership_failure(
                    owner.members,
                    job_owner.FAIL_PROCESS_OWNERSHIP,
                )
                self.assertIn(query_handle, api.closed_handles)
                owner.close()

    def test_ordinary_and_extended_drive_or_unc_path_aliases_match_selected_executable(self):
        aliases = (
            (
                r"\\?\C:\Program Files\Foxglove\foxglove.exe",
                DESKTOP_PATH,
            ),
            (
                r"\\?\UNC\server\share\foxglove.exe",
                r"\\server\share\FOXGLOVE.exe",
            ),
        )
        for selected, captured in aliases:
            with self.subTest(selected=selected):
                api = FakeWindowsApi()
                api.launch_identity = job_owner.ProcessIdentity(
                    4242,
                    123_456_789,
                    captured,
                )
                api.identities_by_pid[4242] = api.launch_identity
                api.enumerated_identities = (api.launch_identity,)
                owner = job_owner.WindowsJobOwner(
                    selected,
                    api=api,
                    platform_name="nt",
                )
                identity = owner.launch_suspended_owned(
                    selected,
                    (),
                    cwd=r"D:\Phase184H",
                    environment={"PHASE184": "H"},
                    stdout_log=r"D:\Phase184H\stdout.log",
                    stderr_log=r"D:\Phase184H\stderr.log",
                )
                self.assertEqual(api.launch_identity, identity)
                owner.close()

    def test_same_path_external_process_is_recorded_rejected_and_never_targeted(self):
        owner, api = self.make_owner()
        launched = self.launch(owner)
        external = job_owner.ProcessIdentity(
            9001,
            777_777_777,
            r"\\?\C:\Program Files\Foxglove\FOXGLOVE.exe",
        )
        api.identities_by_pid[launched.pid] = launched
        api.identities_by_pid[external.pid] = external
        api.enumerated_identities = (launched, external)

        self.assertEqual((external,), owner.external_processes())
        self.assertEqual((external,), owner.recorded_external_processes)
        self.assert_ownership_failure(
            owner.require_no_external_processes,
            job_owner.FAIL_DESKTOP_PREFLIGHT,
        )
        owner.close()

        self.assertEqual([], api.close_requests)
        self.assertEqual([], api.terminated_handles)

    def test_same_path_job_child_appearing_during_enumeration_is_not_external(self):
        owner, api = self.make_owner()
        late_child = job_owner.ProcessIdentity(
            9011,
            939_393_939,
            DESKTOP_PATH,
        )
        api.member_pids = ()
        api.identities_by_pid[late_child.pid] = late_child
        api.enumerated_identities = (late_child,)
        api.membership_by_pid[late_child.pid] = True

        self.assertEqual((), owner.external_processes())
        self.assertEqual((), owner.recorded_external_processes)
        owner.close()

    def test_exact_path_candidate_open_errors_fail_closed_while_pid_exists(self):
        for label, error_code in (
            ("access-denied", 5),
            ("unexpected", 123),
        ):
            with self.subTest(label=label):
                owner, api = self.make_owner()
                candidate = job_owner.ProcessIdentity(
                    9012,
                    949_494_949,
                    DESKTOP_PATH,
                )
                api.identities_by_pid[candidate.pid] = candidate
                api.enumerated_identities = (candidate,)
                api.open_failures_by_pid[candidate.pid] = (
                    _InjectedProcessOpenFailure(error_code)
                )
                api.pid_exists_by_pid[candidate.pid] = True

                self.assert_ownership_failure(
                    owner.external_processes,
                    job_owner.FAIL_DESKTOP_PREFLIGHT,
                )

                self.assertEqual(
                    [candidate.pid],
                    api.pid_existence_queries,
                )
                self.assertEqual((), owner.recorded_external_processes)
                owner.close()

    def test_open_process_failure_preserves_the_exact_win32_error(self):
        for error_code in (5, 87, 123):
            with self.subTest(error_code=error_code):
                ctypes_api = _LastErrorCtypes()

                def fail_open(
                    _access: int,
                    _inherit: bool,
                    _pid: int,
                ) -> int:
                    ctypes_api.set_last_error(error_code)
                    return 0

                api = object.__new__(job_owner._Win32Api)
                api.ctypes = ctypes_api
                api.kernel32 = SimpleNamespace(OpenProcess=fail_open)
                api._invalid_handle = -1

                with self.assertRaises(
                    job_owner.ProcessOpenFailure,
                ) as caught:
                    api.open_process_for_query(9012)

                self.assertEqual(
                    error_code,
                    caught.exception.win32_error,
                )

    def test_open_failure_is_ignored_only_after_separate_pid_absence_proof(self):
        owner, api = self.make_owner()
        vanished = job_owner.ProcessIdentity(
            9013,
            959_595_959,
            DESKTOP_PATH,
        )
        api.identities_by_pid[vanished.pid] = vanished
        api.enumerated_identities = (vanished,)
        api.open_failures_by_pid[vanished.pid] = (
            _InjectedProcessOpenFailure(87)
        )
        api.pid_exists_by_pid[vanished.pid] = False

        self.assertEqual((), owner.external_processes())
        self.assertEqual([vanished.pid], api.pid_existence_queries)
        self.assertEqual((), owner.recorded_external_processes)
        owner.close()

    def test_breakaway_or_single_instance_handoff_fails_and_records_external(self):
        owner, api = self.make_owner()
        launched = self.launch(owner)
        handoff = job_owner.ProcessIdentity(
            9002,
            888_888_888,
            DESKTOP_PATH,
        )
        api.member_pids = ()
        api.identities_by_pid[handoff.pid] = handoff
        api.enumerated_identities = (handoff,)

        self.assert_ownership_failure(
            lambda: owner.require_owned_identity(launched),
            job_owner.FAIL_DESKTOP_HANDOFF,
        )
        self.assertEqual((handoff,), owner.recorded_external_processes)
        owner.close()

    def test_graceful_close_posts_only_owned_desktop_windows_then_closes_job(self):
        owner, api = self.make_owner()
        desktop = self.launch(owner)
        owned_helper = job_owner.ProcessIdentity(5000, 222, OTHER_PATH)
        external_desktop = job_owner.ProcessIdentity(9003, 333, DESKTOP_PATH)
        api.member_pids = (desktop.pid, owned_helper.pid)
        api.identities_by_pid = {
            desktop.pid: desktop,
            owned_helper.pid: owned_helper,
            external_desktop.pid: external_desktop,
        }
        api.enumerated_identities = (desktop, owned_helper, external_desktop)
        api.wait_results[api.created.process_handle] = False

        summary = owner.request_owned_desktop_close(
            grace_seconds=0.01,
            reject_external=False,
        )

        self.assertEqual([(desktop.pid,)], api.close_requests)
        self.assertEqual((desktop,), summary.requested)
        self.assertEqual((desktop,), summary.forced)
        self.assertIn(101, api.closed_handles)
        self.assertEqual([], api.terminated_handles)
        owner.close()
        self.assertEqual(1, api.closed_handles.count(101))

    def test_pid_reused_or_external_window_is_revalidated_before_wm_close(self):
        scenarios = (
            ("external", False, 123_456_789),
            ("pid-reused", True, 123_456_790),
        )
        for label, membership, creation_time in scenarios:
            with self.subTest(label=label):
                owner, api = self.make_owner()
                desktop = self.launch(owner)
                api.identities_by_pid[desktop.pid] = desktop
                candidate = job_owner.ProcessIdentity(
                    desktop.pid,
                    creation_time,
                    r"\\?\C:\Program Files\Foxglove\FOXGLOVE.exe",
                )
                api.window_candidates = (candidate,)
                api.window_membership_by_pid[desktop.pid] = membership

                self.assert_ownership_failure(
                    lambda: owner.request_owned_desktop_close(
                        grace_seconds=0.01,
                        reject_external=False,
                    ),
                    job_owner.FAIL_PROCESS_OWNERSHIP,
                )

                self.assertEqual([], api.window_posted_pids)
                self.assertEqual([], api.close_requests)

    def test_poll_wait_and_repeated_close_validate_the_original_identity(self):
        owner, api = self.make_owner()
        identity = self.launch(owner)
        api.poll_results[api.created.process_handle] = 17
        api.wait_results[api.created.process_handle] = True

        self.assertEqual(17, owner.poll(identity))
        self.assertTrue(owner.wait(identity, timeout_seconds=0.01))
        owner.close()
        owner.close()

        self.assertEqual(1, api.closed_handles.count(101))
        self.assertEqual(1, api.closed_handles.count(api.created.process_handle))
        self.assertEqual(1, api.closed_handles.count(api.created.thread_handle))

    def test_owner_close_failure_is_observable_and_never_retries_handles(self):
        for label, failing_handle in (
            ("job", 101),
            ("process", 202),
        ):
            with self.subTest(label=label):
                owner, api = self.make_owner()
                self.launch(owner)
                api.close_failures_by_handle[failing_handle] = OSError(
                    6,
                    "raw close failure\r\n" + ("x" * 2048),
                )

                with self.assertRaises(
                    job_owner.OwnershipFailure,
                ) as caught:
                    owner.close()

                self.assertEqual("FAIL_CLEANUP", caught.exception.code)
                self.assertNotIn("raw close failure", caught.exception.message)
                self.assertLessEqual(
                    len(caught.exception.message),
                    job_owner.MAX_DIAGNOSTIC_CHARACTERS,
                )
                self.assertEqual(1, api.closed_handles.count(101))
                self.assertEqual(1, api.closed_handles.count(202))
                self.assertEqual(1, api.closed_handles.count(303))
                self.assert_ownership_failure(
                    owner.members,
                    job_owner.FAIL_PROCESS_OWNERSHIP,
                )

                owner.close()
                self.assertEqual(1, api.closed_handles.count(101))
                self.assertEqual(1, api.closed_handles.count(202))
                self.assertEqual(1, api.closed_handles.count(303))

    def test_context_manager_exposes_cleanup_failure_on_every_exit_path(self):
        for body_raises in (False, True):
            with self.subTest(body_raises=body_raises):
                owner, api = self.make_owner()
                api.close_failures_by_handle[101] = OSError(
                    6,
                    "injected close failure",
                )

                with self.assertRaises(
                    job_owner.OwnershipFailure,
                ) as caught:
                    with owner:
                        if body_raises:
                            raise ValueError("injected body failure")

                self.assertEqual("FAIL_CLEANUP", caught.exception.code)
                if body_raises:
                    self.assertIsInstance(
                        caught.exception.__context__,
                        ValueError,
                    )
                self.assertEqual(1, api.closed_handles.count(101))
                owner.close()
                self.assertEqual(1, api.closed_handles.count(101))

    def test_poll_and_wait_observe_normal_root_exit_from_retained_handle(self):
        owner, api = self.make_owner()
        identity = self.launch(owner)
        api.member_pids = ()
        api.enumerated_identities = ()
        api.poll_results[api.created.process_handle] = 23
        api.wait_results[api.created.process_handle] = True

        self.assertEqual(23, owner.poll(identity))
        self.assertTrue(owner.wait(identity, timeout_seconds=0.01))

        self.assertGreaterEqual(
            api.waited_handles.count(api.created.process_handle),
            1,
        )
        owner.close()

    def test_owned_process_policy_ignores_unrelated_same_path_process_after_exit(self):
        policy_type = getattr(job_owner, "RootHandoffPolicy", None)
        self.assertIsNotNone(
            policy_type,
            "Root handoff policy must be explicit.",
        )
        owner, api = self.make_owner(
            handoff_policy=policy_type.OWNED_PROCESS,
        )
        identity = self.launch(owner)
        unrelated = job_owner.ProcessIdentity(
            9009,
            919_191_919,
            DESKTOP_PATH,
        )
        api.member_pids = ()
        api.enumerated_identities = (unrelated,)
        api.poll_results[api.created.process_handle] = 0
        api.wait_results[api.created.process_handle] = True

        self.assertEqual(0, owner.poll(identity))
        self.assertTrue(owner.wait(identity, timeout_seconds=0.01))
        owner.close()

    def test_desktop_policy_still_rejects_external_handoff_after_root_exit(self):
        owner, api = self.make_owner()
        identity = self.launch(owner)
        handoff = job_owner.ProcessIdentity(
            9010,
            929_292_929,
            DESKTOP_PATH,
        )
        api.member_pids = ()
        api.identities_by_pid[handoff.pid] = handoff
        api.enumerated_identities = (handoff,)
        api.poll_results[api.created.process_handle] = 0

        self.assert_ownership_failure(
            lambda: owner.poll(identity),
            job_owner.FAIL_DESKTOP_HANDOFF,
        )
        owner.close()

    def test_win32_errors_are_stable_bounded_and_do_not_leak_raw_diagnostics(self):
        for operation, code in (
            ("create_job", job_owner.FAIL_JOB_CREATE),
            ("create_process", job_owner.FAIL_PROCESS_CREATE),
            ("assign", job_owner.FAIL_PROCESS_ASSIGN),
        ):
            with self.subTest(operation=operation):
                api = FakeWindowsApi()
                api.fail_operation = operation
                failure = self.assert_ownership_failure(
                    lambda api=api: (
                        job_owner.WindowsJobOwner(
                            DESKTOP_PATH,
                            api=api,
                            platform_name="nt",
                        )
                        if operation == "create_job"
                        else self.launch(self.make_owner(api)[0])
                    ),
                    code,
                )
                self.assertNotIn("secret", failure.message)
                self.assertNotIn("x" * 64, failure.message)

    def test_toolhelp_first_unexpected_error_fails_closed(self):
        low_level, ctypes_api, kernel = self.make_toolhelp_enumerator(
            first_result=False,
            first_error=5,
        )
        owner, api = self.make_owner()
        api.enumerate_process_identities = low_level.enumerate_process_identities

        self.assert_ownership_failure(
            owner.enumerate_exact_path_live_processes,
            job_owner.FAIL_DESKTOP_PREFLIGHT,
        )

        self.assertIn(0, ctypes_api.set_calls)
        self.assertEqual([606], kernel.closed_handles)
        owner.close()

    def test_toolhelp_next_unexpected_error_fails_closed(self):
        low_level, ctypes_api, kernel = self.make_toolhelp_enumerator(
            first_result=True,
            first_error=0,
            next_result=False,
            next_error=5,
        )
        owner, api = self.make_owner()
        api.enumerate_process_identities = low_level.enumerate_process_identities

        self.assert_ownership_failure(
            owner.enumerate_exact_path_live_processes,
            job_owner.FAIL_DESKTOP_PREFLIGHT,
        )

        self.assertGreaterEqual(ctypes_api.set_calls.count(0), 2)
        self.assertEqual([606], kernel.closed_handles)
        owner.close()

    def test_toolhelp_no_more_files_is_normal_for_empty_first_snapshot(self):
        low_level, ctypes_api, kernel = self.make_toolhelp_enumerator(
            first_result=False,
            first_error=18,
        )
        owner, api = self.make_owner()
        api.enumerate_process_identities = low_level.enumerate_process_identities

        self.assertEqual((), owner.enumerate_exact_path_live_processes())

        self.assertIn(0, ctypes_api.set_calls)
        self.assertEqual([606], kernel.closed_handles)
        owner.close()

    def test_toolhelp_no_more_files_is_normal_at_snapshot_end(self):
        low_level, ctypes_api, kernel = self.make_toolhelp_enumerator(
            first_result=True,
            first_error=0,
            next_result=False,
            next_error=18,
        )
        owner, api = self.make_owner()
        api.enumerate_process_identities = low_level.enumerate_process_identities

        self.assertEqual((), owner.enumerate_exact_path_live_processes())

        self.assertGreaterEqual(ctypes_api.set_calls.count(0), 2)
        self.assertEqual([606], kernel.closed_handles)
        owner.close()


@unittest.skipUnless(
    os.name == "nt" and job_owner is not None,
    "Windows-only disposable Job Object integration.",
)
class WindowsJobOwnerIntegrationTests(unittest.TestCase):
    def test_disposable_python_root_can_exit_normally_before_poll_and_wait(self):
        TEST_ROOT.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix="normal-", dir=TEST_ROOT) as raw:
            root = pathlib.Path(raw)
            with job_owner.WindowsJobOwner(
                sys.executable,
                handoff_policy=job_owner.RootHandoffPolicy.OWNED_PROCESS,
            ) as owner:
                identity = owner.launch_suspended_owned(
                    sys.executable,
                    ("-c", "raise SystemExit(17)"),
                    cwd=str(root),
                    environment=dict(os.environ),
                    stdout_log=str(root / "normal.stdout.log"),
                    stderr_log=str(root / "normal.stderr.log"),
                )
                self.assertTrue(owner.wait(identity, timeout_seconds=10.0))
                self.assertEqual(17, owner.poll(identity))

    def test_helper_death_closes_job_and_terminates_its_disposable_python_child(self):
        TEST_ROOT.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix="real-", dir=TEST_ROOT) as raw:
            root = pathlib.Path(raw)
            identity_path = root / "child-identity.json"
            helper_code = "\n".join(
                (
                    "import json, os, pathlib, sys, time",
                    "from Scripts.smoke.foxrun import phase184_windows_job_owner as owner",
                    "root = pathlib.Path(sys.argv[1])",
                    "identity_path = pathlib.Path(sys.argv[2])",
                    "job = owner.WindowsJobOwner(",
                    "    sys.executable,",
                    "    handoff_policy=owner.RootHandoffPolicy.OWNED_PROCESS,",
                    ")",
                    "identity = job.launch_suspended_owned(",
                    "    sys.executable,",
                    "    ('-c', 'import time; time.sleep(120)'),",
                    "    cwd=str(root),",
                    "    environment=dict(os.environ),",
                    "    stdout_log=str(root / 'child.stdout.log'),",
                    "    stderr_log=str(root / 'child.stderr.log'),",
                    ")",
                    "if identity not in job.members():",
                    "    raise RuntimeError('Disposable child is not an exact Job member.')",
                    "identity_path.write_text(json.dumps({",
                    "    'pid': identity.pid,",
                    "    'creation': identity.creation_time_100ns,",
                    "    'path': identity.executable,",
                    "}), encoding='utf-8')",
                    "time.sleep(120)",
                )
            )
            helper = subprocess.Popen(
                (
                    sys.executable,
                    "-c",
                    helper_code,
                    str(root),
                    str(identity_path),
                ),
                cwd=ROOT,
                stdout=subprocess.DEVNULL,
                stderr=subprocess.PIPE,
                text=True,
            )
            child_handle = None
            try:
                deadline = time.monotonic() + 15.0
                while time.monotonic() < deadline and not identity_path.exists():
                    if helper.poll() is not None:
                        break
                    time.sleep(0.05)
                if not identity_path.exists():
                    if helper.poll() is None:
                        helper.kill()
                        helper.wait(timeout=10)
                    stderr = (helper.stderr.read(512) if helper.stderr else "")
                    self.fail(f"Disposable helper did not create child identity: {stderr}")

                identity = json.loads(identity_path.read_text(encoding="utf-8"))
                import ctypes

                kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
                kernel32.OpenProcess.argtypes = [
                    ctypes.c_uint32,
                    ctypes.c_int,
                    ctypes.c_uint32,
                ]
                kernel32.OpenProcess.restype = ctypes.c_void_p
                kernel32.WaitForSingleObject.argtypes = [
                    ctypes.c_void_p,
                    ctypes.c_uint32,
                ]
                kernel32.WaitForSingleObject.restype = ctypes.c_uint32
                kernel32.TerminateProcess.argtypes = [ctypes.c_void_p, ctypes.c_uint32]
                kernel32.TerminateProcess.restype = ctypes.c_int
                kernel32.CloseHandle.argtypes = [ctypes.c_void_p]
                kernel32.CloseHandle.restype = ctypes.c_int

                child_handle = kernel32.OpenProcess(
                    0x00100000 | 0x0001,
                    False,
                    int(identity["pid"]),
                )
                self.assertTrue(child_handle, "Disposable child process is not live.")
                helper.kill()
                helper.wait(timeout=10)
                wait_result = kernel32.WaitForSingleObject(child_handle, 10_000)
                if wait_result != 0:
                    kernel32.TerminateProcess(child_handle, 184)
                self.assertEqual(
                    0,
                    wait_result,
                    "Kill-on-close Job did not terminate the disposable Python child.",
                )
            finally:
                if helper.poll() is None:
                    helper.kill()
                    helper.wait(timeout=10)
                if helper.stderr is not None:
                    helper.stderr.close()
                if child_handle:
                    kernel32.CloseHandle(child_handle)


if __name__ == "__main__":
    unittest.main()
