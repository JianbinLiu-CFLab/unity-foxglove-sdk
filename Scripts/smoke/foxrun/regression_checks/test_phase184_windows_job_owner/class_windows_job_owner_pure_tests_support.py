from __future__ import annotations
from .foundation import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "test_phase184_windows_job_owner.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
class _WindowsJobOwnerPureTests_support:
    """Decomposed Phase192 implementation component."""
    def make_owner(self, api: FakeWindowsApi | None = None, **owner_options):
        """Build owner."""

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
        """Launch the configured owned process."""

        return owner.launch_suspended_owned(
            DESKTOP_PATH,
            ("--open", r"D:\Evidence Folder\result.mcap"),
            cwd=r"D:\Phase184H",
            environment={"PATH": r"C:\Windows", "PHASE184": "H"},
            stdout_log=r"D:\Phase184H\desktop.stdout.log",
            stderr_log=r"D:\Phase184H\desktop.stderr.log",
        )
    def assert_ownership_failure(self, action, expected_code: str):
        """Handle the assert ownership failure step."""

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
        """Build toolhelp enumerator."""

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
        """Verify process identity is immutable and requires exact absolute windows path."""

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
        """Verify production operation fails closed off windows but fake windows is lazy."""

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
        """Verify job configuration base exceptions close empty job once and propagate."""

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
        """Verify raw close handle failure preserves Win32 error."""

        ctypes_api = _LastErrorCtypes()

        def fail_close(_handle: int) -> bool:
            """Handle the fail close step."""

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
        """Verify launch orders create assign identity membership before resume."""

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
        """Verify launch thread handle close failure uses cleanup code."""

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
        """Verify assignment failure never resumes and terminates only created root."""

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
        """Verify invalid created root result is closed without resume."""

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
        """Verify identity or membership failure never resumes created root."""

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
        """Verify base exceptions at launch boundaries cleanup then propagate."""

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
        """Verify low level created process transfer interrupt cleans every handle."""

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
                    """Handle the interrupt result step."""

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
        """Verify selected path mismatch before resume fails closed."""

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


__all__ = [name for name in globals() if not name.startswith("__")]
