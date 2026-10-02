from __future__ import annotations
from .class_windows_job_owner_pure_tests_fixtures import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "test_phase184_windows_job_owner.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
class _WindowsJobOwnerPureTests_validation:
    """Decomposed Phase192 implementation component."""
    def test_breakaway_or_single_instance_handoff_fails_and_records_external(self):
        """Verify breakaway or single instance handoff fails and records external."""

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
        """Verify graceful close posts only owned desktop windows then closes job."""

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
        """Verify PID reused or external window is revalidated before wm close."""

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
        """Verify poll wait and repeated close validate the original identity."""

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
        """Verify owner close failure is observable and never retries handles."""

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
        """Verify context manager exposes cleanup failure on every exit path."""

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
        """Verify poll and wait observe normal root exit from retained handle."""

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
        """Verify owned process policy ignores unrelated same path process after exit."""

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
        """Verify desktop policy still rejects external handoff after root exit."""

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
        """Verify Win32 errors are stable bounded and do not leak raw diagnostics."""

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
        """Verify toolhelp first unexpected error fails closed."""

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
        """Verify toolhelp next unexpected error fails closed."""

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
        """Verify toolhelp no more files is normal for empty first snapshot."""

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
        """Verify toolhelp no more files is normal at snapshot end."""

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
class WindowsJobOwnerPureTests(_WindowsJobOwnerPureTests_support, _WindowsJobOwnerPureTests_fixtures, _WindowsJobOwnerPureTests_validation, unittest.TestCase):
    """Exercise the windows job owner pure tests behavior."""
    pass


__all__ = [name for name in globals() if not name.startswith("__")]
