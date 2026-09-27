from __future__ import annotations
from .class_windows_job_owner_pure_tests_support import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "test_phase184_windows_job_owner.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
class _WindowsJobOwnerPureTests_fixtures:
    def test_require_owned_identity_rejects_path_time_membership_and_pid_reuse(self):
        """Verify require owned identity rejects path time membership and PID reuse."""

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
        """Verify untracked descendant snapshot PID reuse requires exact handle job membership."""

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
        """Verify member snapshot skips only a PID proven gone before open."""

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
        """Verify member snapshot open failure still fails if PID exists."""

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
        """Verify member snapshot skips identity capture only after handle exit."""

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
        """Verify member snapshot skips membership race only after handle exit."""

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
        """Verify member snapshot missing live identity remains fail closed."""

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
        """Verify member snapshot successful identity is skipped after final exit poll."""

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
        """Verify member snapshot final poll error or invalid result fails closed."""

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
        """Verify ordinary and extended drive or unc path aliases match selected executable."""

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
        """Verify same path external process is recorded rejected and never targeted."""

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
        """Verify same path job child appearing during enumeration is not external."""

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
        """Verify exact path candidate open errors fail closed while PID exists."""

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
        """Verify open process failure preserves the exact Win32 error."""

        for error_code in (5, 87, 123):
            with self.subTest(error_code=error_code):
                ctypes_api = _LastErrorCtypes()

                def fail_open(
                    _access: int,
                    _inherit: bool,
                    _pid: int,
                ) -> int:
                    """Handle the fail open step."""

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
    def test_wait_identity_only_treats_a_missing_pid_as_already_exited(self):
        """Distinguish a vanished PID from an unqueryable live process."""

        identity = job_owner.ProcessIdentity(9014, 969_696_969, DESKTOP_PATH)
        for error_code, expected_missing in ((87, True), (5, False)):
            with self.subTest(error_code=error_code):
                ctypes_api = _LastErrorCtypes()

                def fail_open(
                    _access: int,
                    _inherit: bool,
                    _pid: int,
                    *,
                    selected_error: int = error_code,
                ) -> int:
                    """Return an invalid handle with one selected Win32 error."""

                    ctypes_api.set_last_error(selected_error)
                    return 0

                api = object.__new__(job_owner._Win32Api)
                api.ctypes = ctypes_api
                api.kernel32 = SimpleNamespace(OpenProcess=fail_open)
                api._invalid_handle = -1

                if expected_missing:
                    self.assertTrue(api.wait_identity(identity, 0.01))
                else:
                    with self.assertRaises(job_owner.ProcessOpenFailure) as caught:
                        api.wait_identity(identity, 0.01)
                    self.assertEqual(error_code, caught.exception.win32_error)
    def test_open_failure_is_ignored_only_after_separate_pid_absence_proof(self):
        """Verify open failure is ignored only after separate PID absence proof."""

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


__all__ = [name for name in globals() if not name.startswith("__")]
