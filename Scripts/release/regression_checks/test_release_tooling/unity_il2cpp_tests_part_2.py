from .foundation import *
from .unity_il2cpp_tests_part_1 import _UnityIl2CppBuildTestsPart1

class _UnityIl2CppBuildTestsPart2:
    """Decomposed UnityIl2CppBuildTests method group 2."""

    def test_hub_discovery_rejects_a_non_executable_candidate(self) -> None:
        """The generic Hub fallback must apply the same executable gate."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            _project, unity = self._hub_layout(root)
            unity.parent.mkdir(parents=True)
            unity.write_text("NOT_A_UNITY_EXECUTABLE\n", encoding="utf-8")

            with mock.patch.object(self.unity_il2cpp.os, "access", return_value=False), \
                    mock.patch.object(self.unity_il2cpp.platform, "system", return_value="Windows"):
                with mock.patch.dict(self.unity_il2cpp.os.environ,
                                     {"PROGRAMFILES": str(root / "ProgramFiles"),
                                      "PROGRAMFILES(X86)": str(root / "missing")}, clear=False):
                    resolved = self.unity_il2cpp.find_unity_from_hub()

        self.assertIsNone(resolved)

    def test_explicit_unity_path_rejects_a_directory(self) -> None:
        """--unity must not accept a directory that merely occupies the path."""
        with tempfile.TemporaryDirectory() as temp:
            occupied = Path(temp) / "Unity.exe"
            occupied.mkdir()

            with self.assertRaises(FileNotFoundError):
                self.unity_il2cpp.find_unity_explicit(str(occupied))

    def test_environment_unity_path_rejects_a_directory(self) -> None:
        """UNITY_EXE must not accept a directory that merely occupies the path."""
        with tempfile.TemporaryDirectory() as temp:
            occupied = Path(temp) / "Unity.exe"
            occupied.mkdir()

            with mock.patch.dict(self.unity_il2cpp.os.environ,
                                 {"UNITY_EXE": str(occupied)}, clear=False):
                with self.assertRaises(FileNotFoundError):
                    self.unity_il2cpp.find_unity_from_env()

    def test_explicit_relative_unity_path_is_resolved_at_discovery(self) -> None:
        """The identity checked at discovery must be the identity launched after the cwd change."""
        with tempfile.TemporaryDirectory() as temp:
            caller_cwd = Path(temp) / "caller"
            caller_cwd.mkdir()
            self._write_unity_stand_in(caller_cwd / "tools" / "Unity.exe")
            previous = os.getcwd()
            os.chdir(caller_cwd)
            try:
                resolved = self.unity_il2cpp.find_unity_explicit(os.path.join("tools", "Unity.exe"))
            finally:
                os.chdir(previous)

        self.assertTrue(Path(resolved).is_absolute())

    def _drive_main(self, temp: Path, on_run, pre_existing: bytes | None = None):
        """Run main() with a controlled build command and process result.

        Returns (returncode, combined output, requested Player path). The generated
        artifact preflight and the command construction are stubbed so the assertion
        is about the post-run outcome only, not about discovery or preflight.
        """
        output = temp / "requested" / "FoxgloveDemo.exe"
        output.parent.mkdir(parents=True, exist_ok=True)
        if pre_existing is not None:
            output.write_bytes(pre_existing)
        project = temp / "project"
        project.mkdir(exist_ok=True)
        log = temp / "build.log"

        def controlled_run(*_args, **_kwargs):
            """Stand in for the Unity invocation and its process result."""
            return on_run(output)

        with mock.patch.object(self.unity_il2cpp, "build_command",
                               return_value=(["unity", "-batchmode"], project, log, output)):
            with mock.patch.object(self.unity_il2cpp, "validate_generated_artifacts",
                                   return_value=[]):
                with mock.patch.object(self.unity_il2cpp, "run_with_progress",
                                       side_effect=controlled_run):
                    with mock.patch.object(sys, "argv", ["unity_il2cpp.py"]):
                        captured = io.StringIO()
                        with contextlib.redirect_stdout(captured), \
                                contextlib.redirect_stderr(captured):
                            returncode = self.unity_il2cpp.main()
        return returncode, captured.getvalue(), output

    def test_zero_exit_without_requested_player_is_rejected(self) -> None:
        """A zero process code must not be reported as success with no Player produced."""
        def produce_nothing(_output):
            """Exit zero without writing the requested Player."""
            return self.unity_il2cpp.EXIT_SUCCESS

        with tempfile.TemporaryDirectory() as temp:
            returncode, text, output = self._drive_main(Path(temp), produce_nothing)

            self.assertFalse(output.exists())

        self.assertNotEqual(self.unity_il2cpp.EXIT_SUCCESS, returncode)
        self.assertNotIn("Build command completed successfully.", text)

    def test_stale_pre_existing_player_is_rejected(self) -> None:
        """A pre-run output left untouched by the build is not this invocation's Player."""
        def leave_stale_output(_output):
            """Exit zero without touching the pre-existing output."""
            return self.unity_il2cpp.EXIT_SUCCESS

        with tempfile.TemporaryDirectory() as temp:
            returncode, text, output = self._drive_main(
                Path(temp), leave_stale_output, pre_existing=b"stale player bytes")

            # The controller must not delete or rewrite what it did not produce.
            self.assertTrue(output.exists())
            self.assertEqual(b"stale player bytes", output.read_bytes())

        self.assertNotEqual(self.unity_il2cpp.EXIT_SUCCESS, returncode)
        self.assertNotIn("Build command completed successfully.", text)

    def test_fresh_invocation_owned_player_is_accepted(self) -> None:
        """A Player produced by this invocation must still be reported as success."""
        def produce_player(output):
            """Write the requested Player, then exit zero."""
            output.write_bytes(b"freshly built player")
            return self.unity_il2cpp.EXIT_SUCCESS

        with tempfile.TemporaryDirectory() as temp:
            returncode, text, output = self._drive_main(Path(temp), produce_player)

            self.assertTrue(output.exists())

        self.assertEqual(self.unity_il2cpp.EXIT_SUCCESS, returncode)
        self.assertIn("Build command completed successfully.", text)

    def test_rebuilt_player_over_a_previous_output_is_accepted(self) -> None:
        """Replacing an older Player in place is ownership by this invocation."""
        def rebuild_player(output):
            """Overwrite the previous output with new bytes, then exit zero."""
            output.write_bytes(b"rebuilt player bytes, different length")
            return self.unity_il2cpp.EXIT_SUCCESS

        with tempfile.TemporaryDirectory() as temp:
            returncode, text, _output = self._drive_main(
                Path(temp), rebuild_player, pre_existing=b"older player")

        self.assertEqual(self.unity_il2cpp.EXIT_SUCCESS, returncode)
        self.assertIn("Build command completed successfully.", text)

    def test_nonzero_process_exit_reports_failure(self) -> None:
        """A nonzero Unity exit code must propagate unchanged and report failure."""
        def fail_the_build(_output):
            """Exit nonzero without producing anything."""
            return 3

        with tempfile.TemporaryDirectory() as temp:
            returncode, text, _output = self._drive_main(Path(temp), fail_the_build)

        self.assertEqual(3, returncode)
        self.assertIn("Build failed with exit code 3", text)
        self.assertNotIn("Build command completed successfully.", text)

    def test_output_verification_is_deterministic_and_leaves_no_residue(self) -> None:
        """The stale verdict must repeat exactly and must not add or remove files."""
        def leave_stale_output(_output):
            """Exit zero without touching the pre-existing output."""
            return self.unity_il2cpp.EXIT_SUCCESS

        verdicts = []
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            first, _text, output = self._drive_main(
                root, leave_stale_output, pre_existing=b"stale player bytes")
            verdicts.append(first)
            after_first = sorted(p.relative_to(root).as_posix()
                                 for p in root.rglob("*") if p.is_file())

            second, _text2, _output2 = self._drive_main(
                root, leave_stale_output, pre_existing=None)
            verdicts.append(second)
            after_second = sorted(p.relative_to(root).as_posix()
                                  for p in root.rglob("*") if p.is_file())

            self.assertEqual(after_first, after_second)
            self.assertEqual(b"stale player bytes", output.read_bytes())

        self.assertEqual(verdicts[0], verdicts[1])
        self.assertNotEqual(self.unity_il2cpp.EXIT_SUCCESS, verdicts[0])

    def test_unity_start_failure_uses_the_declared_exit_taxonomy(self) -> None:
        """A failed process start must not escape main as a raw traceback."""
        def exploding_run(*_args, **_kwargs):
            """Stand in for a process start that fails at the OS boundary."""
            raise FileNotFoundError("controlled process start failure")

        with mock.patch.object(self.unity_il2cpp, "run_with_progress", side_effect=exploding_run):
            with mock.patch.object(self.unity_il2cpp, "validate_generated_artifacts", return_value=[]):
                with mock.patch.object(self.unity_il2cpp, "build_command", return_value=(
                        ["unity", "-batchmode"], Path(tempfile.gettempdir()) / "project",
                        Path(tempfile.gettempdir()) / "unity.log",
                        Path(tempfile.gettempdir()) / "player.exe")):
                    with mock.patch.object(sys, "argv", ["unity_il2cpp.py"]):
                        captured = io.StringIO()
                        with contextlib.redirect_stdout(captured), contextlib.redirect_stderr(captured):
                            returncode = self.unity_il2cpp.main()

        self.assertEqual(self.unity_il2cpp.EXIT_PREFLIGHT_FAILURE, returncode)
        self.assertIn("controlled process start failure", captured.getvalue())

    def test_negative_build_timeout_is_rejected(self) -> None:
        """Only zero, not an arbitrary negative value, may disable the build timeout."""
        with mock.patch.object(sys, "argv", ["unity_il2cpp.py", "--timeout-minutes", "-1"]):
            with self.assertRaises(SystemExit) as raised:
                self.unity_il2cpp.parse_args()

        self.assertEqual(self.unity_il2cpp.EXIT_USAGE_ERROR, raised.exception.code)

    def test_posix_process_group_enumeration_fails_closed_without_ps(self) -> None:
        """A missing ps binary must not disguise an extant owned process group as empty."""
        with mock.patch.object(self.unity_il2cpp.subprocess, "run", side_effect=FileNotFoundError("ps")):
            with mock.patch.object(self.unity_il2cpp.os, "killpg", return_value=None, create=True):
                pids = self.unity_il2cpp._posix_process_group_pids(4321)

        self.assertEqual([4321], pids)

    def test_posix_process_group_diagnostic_uses_a_bounded_timeout(self) -> None:
        """The residual-PID diagnostic must bound the ps subprocess it launches."""
        completed = types.SimpleNamespace(stdout=" 111 4321\n", returncode=0)
        with mock.patch.object(self.unity_il2cpp.subprocess, "run", return_value=completed) as run:
            with mock.patch.object(self.unity_il2cpp, "_posix_process_group_exists", return_value=False):
                pids = self.unity_il2cpp._posix_process_group_pids(4321)

        self.assertEqual([111], pids)
        self.assertIn("timeout", run.call_args.kwargs)
        self.assertGreater(run.call_args.kwargs["timeout"], 0)

    def test_posix_process_group_diagnostic_timeout_fails_closed(self) -> None:
        """A diagnostic timeout must retain an extant group in the residual report."""
        expired = subprocess.TimeoutExpired(["ps", "-eo", "pid=,pgid="], 1)
        with mock.patch.object(self.unity_il2cpp.subprocess, "run", side_effect=expired):
            with mock.patch.object(self.unity_il2cpp, "_posix_process_group_exists", return_value=True):
                pids = self.unity_il2cpp._posix_process_group_pids(4321)

        self.assertEqual([4321], pids)

    def _controlled_tree(self, poll_result, pid_sequence, call_log):
        """Build a stand-in owned tree with a scripted residual-PID sequence."""
        remaining = list(pid_sequence)

        class ControlledProcess:
            """Stand in for the launched root process."""

            def poll(self):
                """Report the scripted root process result."""
                return poll_result

        class ControlledTree:
            """Stand in for one platform-owned process tree."""

            def __init__(self):
                """Record the scripted process and start with ownership held."""
                self.process = ControlledProcess()

            def active_pids(self):
                """Return the next scripted residual set, repeating the last one."""
                call_log.append("active_pids")
                return remaining.pop(0) if len(remaining) > 1 else remaining[0]

            def close(self):
                """Record that platform ownership was released."""
                call_log.append("close")

        return ControlledTree()

    def _run_with_controlled_tree(self, temp: Path, tree, timeout_minutes: int = 0):
        """Drive run_with_progress against a controlled owned tree."""
        log = temp / "unity.log"
        with mock.patch.object(self.unity_il2cpp, "start_owned_process", return_value=tree):
            # Keep the synthetic deadline comfortably above a loaded-host
            # scheduling slice; the assertion is about polling order, not a
            # sub-second timing guarantee.
            with mock.patch.object(self.unity_il2cpp, "UNITY_TERMINATION_WAIT_SECONDS", 1.0):
                captured = io.StringIO()
                with contextlib.redirect_stdout(captured), contextlib.redirect_stderr(captured):
                    returncode = self.unity_il2cpp.run_with_progress(
                        ["controlled"], temp, log, 1, timeout_minutes)
        return returncode, captured.getvalue()

    def test_success_waits_for_the_owned_tree_to_quiesce(self) -> None:
        """A tree that drains shortly after the root exits is still a success."""
        calls: list[str] = []
        tree = self._controlled_tree(0, [[424242], [424242], []], calls)

        with tempfile.TemporaryDirectory() as temp:
            returncode, text = self._run_with_controlled_tree(Path(temp), tree)

        self.assertEqual(self.unity_il2cpp.EXIT_SUCCESS, returncode)
        self.assertNotIn("did not quiesce", text)

    def test_success_is_withheld_when_the_owned_tree_never_quiesces(self) -> None:
        """A zero root exit must not be success while descendants are still owned."""
        calls: list[str] = []
        tree = self._controlled_tree(0, [[424242]], calls)

        with tempfile.TemporaryDirectory() as temp:
            returncode, text = self._run_with_controlled_tree(Path(temp), tree)

        self.assertNotEqual(self.unity_il2cpp.EXIT_SUCCESS, returncode)
        self.assertIn("did not quiesce", text)
        self.assertIn("424242", text)

    def test_quiescence_is_checked_before_ownership_is_released(self) -> None:
        """Ownership must not be released before quiescence has been decided."""
        calls: list[str] = []
        tree = self._controlled_tree(0, [[424242]], calls)

        with tempfile.TemporaryDirectory() as temp:
            self._run_with_controlled_tree(Path(temp), tree)

        self.assertIn("close", calls)
        self.assertIn("active_pids", calls)
        self.assertLess(calls.index("active_pids"), calls.index("close"))

    def test_nonzero_exit_is_not_masked_by_a_quiescence_failure(self) -> None:
        """A real Unity failure code must survive an unquiet tree."""
        calls: list[str] = []
        tree = self._controlled_tree(3, [[424242]], calls)

        with tempfile.TemporaryDirectory() as temp:
            returncode, text = self._run_with_controlled_tree(Path(temp), tree)

        self.assertEqual(3, returncode)
        self.assertIn("did not quiesce", text)

    def test_posix_termination_poll_avoids_repeated_ps_processes(self) -> None:
        """Quiescence polling should use the process-group primitive, not shell out on every pass."""
        process = mock.Mock()
        process.wait.return_value = 0

        kill_signal = 9

        def inspect_or_kill_group(_process_group_id, signal_value):
            """Model an extant group until the final kill signal is sent."""
            if signal_value == kill_signal:
                return None
            raise ProcessLookupError

        tree = self.unity_il2cpp.OwnedProcessTree(process, posix_process_group_id=4321)
        with mock.patch.object(self.unity_il2cpp.signal, "SIGKILL", kill_signal, create=True):
            with mock.patch.object(
                self.unity_il2cpp.os,
                "killpg",
                side_effect=inspect_or_kill_group,
                create=True,
            ):
                with mock.patch.object(self.unity_il2cpp, "_posix_process_group_pids", return_value=[]) as enumerate_pids:
                    residual = tree.terminate()

        self.assertEqual([], residual)
        enumerate_pids.assert_not_called()

    def test_windows_job_uses_a_dedicated_child_termination_code(self) -> None:
        """A killed child must not inherit the build CLI's own timeout exit code."""
        kernel32 = mock.Mock()
        kernel32.TerminateJobObject.return_value = True
        job = object.__new__(self.unity_il2cpp._WindowsKillOnCloseJob)
        job._handle = 123
        job._kernel32 = kernel32

        job.terminate()

        self.assertNotEqual(
            self.unity_il2cpp.EXIT_TIMEOUT,
            self.unity_il2cpp.WINDOWS_JOB_TERMINATE_EXIT_CODE,
        )
        kernel32.TerminateJobObject.assert_called_once_with(
            123,
            self.unity_il2cpp.WINDOWS_JOB_TERMINATE_EXIT_CODE,
        )

    def test_timeout_terminates_owned_descendant_tree(self) -> None:
        """A timed-out Unity stand-in must not leave its compiler child alive."""
        parent_pid = None
        child_pid = None
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            parent_pid_path = root / "parent.pid"
            child_pid_path = root / "child.pid"
            parent_code = (
                "import os, pathlib, subprocess, sys, time; "
                f"pathlib.Path({str(parent_pid_path)!r}).write_text(str(os.getpid()), encoding='utf-8'); "
                "child = subprocess.Popen([sys.executable, '-c', 'import time; time.sleep(60)']); "
                f"pathlib.Path({str(child_pid_path)!r}).write_text(str(child.pid), encoding='utf-8'); "
                "time.sleep(60)"
            )

            try:
                # Process creation is intentionally real here so the test
                # proves descendant cleanup.  Allow a loaded Windows host
                # enough startup time before the synthetic timeout fires.
                with mock.patch.object(self.unity_il2cpp, "SECONDS_PER_MINUTE", 15):
                    with mock.patch.object(self.unity_il2cpp, "LOG_POLL_SLEEP_SECONDS", 0.01):
                        with mock.patch.object(self.unity_il2cpp, "UNITY_TERMINATION_WAIT_SECONDS", 2):
                            result = self.unity_il2cpp.run_with_progress(
                                [sys.executable, "-c", parent_code],
                                root,
                                root / "unity.log",
                                interval=1,
                                timeout_minutes=1,
                            )

                self.assertEqual(self.unity_il2cpp.EXIT_TIMEOUT, result)
                self.assertTrue(parent_pid_path.is_file(), "fake Unity did not publish its PID")
                self.assertTrue(child_pid_path.is_file(), "fake compiler did not publish its PID")
                parent_pid = int(parent_pid_path.read_text(encoding="utf-8"))
                child_pid = int(child_pid_path.read_text(encoding="utf-8"))
                self.assertTrue(self._wait_for_pid_exit(parent_pid))
                self.assertTrue(
                    self._wait_for_pid_exit(child_pid),
                    f"timed-out compiler child remained alive: pid={child_pid}",
                )
            finally:
                if child_pid is None:
                    child_pid = self._read_pid_if_present(child_pid_path)
                if parent_pid is None:
                    parent_pid = self._read_pid_if_present(parent_pid_path)
                self._kill_if_running(child_pid)
                self._kill_if_running(parent_pid)

    def test_owned_process_tree_preserves_normal_exit(self) -> None:
        """Owned launch plumbing must preserve an ordinary successful exit."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            with mock.patch.object(self.unity_il2cpp, "LOG_POLL_SLEEP_SECONDS", 0.01):
                result = self.unity_il2cpp.run_with_progress(
                    [sys.executable, "-c", "raise SystemExit(0)"],
                    root,
                    root / "unity.log",
                    interval=1,
                    timeout_minutes=0,
                )

        self.assertEqual(self.unity_il2cpp.EXIT_SUCCESS, result)

    @unittest.skipIf(os.name == "nt", "POSIX process-group ownership seam")
    def test_root_exit_still_terminates_late_posix_descendant(self) -> None:
        """Closing ownership after root exit must kill descendants that remain in its group."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            child_pid_path = root / "child.pid"
            parent_code = (
                "import pathlib, subprocess, sys; "
                f"child=subprocess.Popen([sys.executable,'-c','import time; time.sleep(60)']); "
                f"pathlib.Path({str(child_pid_path)!r}).write_text(str(child.pid), encoding='utf-8')"
            )
            with mock.patch.object(self.unity_il2cpp, "UNITY_TERMINATION_WAIT_SECONDS", 0.1):
                with mock.patch.object(self.unity_il2cpp, "PROCESS_TREE_POLL_SECONDS", 0.01):
                    result = self.unity_il2cpp.run_with_progress(
                        [sys.executable, "-c", parent_code],
                        root,
                        root / "unity.log",
                        interval=1,
                        timeout_minutes=0,
                    )
            self.assertEqual(self.unity_il2cpp.EXIT_SUCCESS, result)
            child_pid = int(child_pid_path.read_text(encoding="utf-8"))
            deadline = time.monotonic() + 2.0
            while time.monotonic() < deadline:
                if not self._pid_is_running(child_pid):
                    break
                # A killed orphan can remain as a zombie until WSL reaps it;
                # it is no longer executing and therefore does not violate the
                # owned-tree retirement contract.
                proc_stat = Path(f"/proc/{child_pid}/stat")
                if proc_stat.is_file() and proc_stat.read_text(encoding="utf-8").split()[2] == "Z":
                    break
                time.sleep(0.02)
            else:
                self.fail(f"late descendant remained alive: pid={child_pid}")

    def test_windows_partial_acquisition_cleans_up_on_keyboard_interrupt(self) -> None:
        """An interrupt during suspended-process acquisition must release Job ownership."""
        if not hasattr(subprocess, "CREATE_NEW_PROCESS_GROUP"):
            self.skipTest("Windows creation flags are unavailable on this host")
        process = mock.Mock(pid=4242)
        job = mock.Mock()
        with mock.patch.object(self.unity_il2cpp.os, "name", "nt"):
            with mock.patch.object(self.unity_il2cpp, "_WindowsKillOnCloseJob", return_value=job):
                with mock.patch.object(self.unity_il2cpp.subprocess, "Popen", return_value=process):
                    with mock.patch.object(
                        self.unity_il2cpp, "_resume_suspended_windows_process", side_effect=KeyboardInterrupt
                    ):
                        with self.assertRaises(KeyboardInterrupt):
                            self.unity_il2cpp.start_owned_process(["controlled"], Path("."))
        process.kill.assert_called_once_with()
        process.wait.assert_called_once()
        job.close.assert_called_once_with()

    def test_giant_progress_interval_has_bounded_prelaunch_behavior(self) -> None:
        """Arbitrary-precision CLI intervals must not escape as float overflow."""
        calls: list[str] = []
        tree = self._controlled_tree(0, [[]], calls)
        with tempfile.TemporaryDirectory() as temp:
            with mock.patch.object(self.unity_il2cpp, "start_owned_process", return_value=tree):
                with mock.patch.object(self.unity_il2cpp, "LOG_POLL_SLEEP_SECONDS", 0):
                    result = self.unity_il2cpp.run_with_progress(
                        ["controlled"],
                        Path(temp),
                        Path(temp) / "unity.log",
                        10**10000,
                        timeout_minutes=0,
                    )
        self.assertEqual(self.unity_il2cpp.EXIT_SUCCESS, result)

    @unittest.skipIf(os.name == "nt", "POSIX process-group escape seam")
    def test_posix_escaped_descendant_is_retired_after_root_exit(self) -> None:
        """A descendant that calls setsid must remain owned until cleanup."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            child_pid_path = root / "child.pid"
            parent_code = (
                "import pathlib, subprocess, sys, time; "
                f"child=subprocess.Popen([sys.executable,'-c','import os,time; os.setsid(); time.sleep(60)']); "
                f"pathlib.Path({str(child_pid_path)!r}).write_text(str(child.pid), encoding='utf-8'); "
                "time.sleep(0.3)"
            )
            with mock.patch.object(self.unity_il2cpp, "LOG_POLL_SLEEP_SECONDS", 0.02):
                with mock.patch.object(self.unity_il2cpp, "UNITY_TERMINATION_WAIT_SECONDS", 0.2):
                    with mock.patch.object(self.unity_il2cpp, "PROCESS_TREE_POLL_SECONDS", 0.01):
                        result = self.unity_il2cpp.run_with_progress(
                            [sys.executable, "-c", parent_code],
                            root,
                            root / "unity.log",
                            interval=1,
                            timeout_minutes=0,
                        )
            child_pid = int(child_pid_path.read_text(encoding="utf-8"))
            self.assertTrue(self._wait_for_pid_exit(child_pid), f"escaped descendant remained alive: pid={child_pid}")

    @staticmethod
    def _read_pid_if_present(path: Path) -> int | None:
        """Read a test-owned PID file when startup reached that boundary."""
        if not path.is_file():
            return None
        return int(path.read_text(encoding="utf-8"))

    @staticmethod
    def _wait_for_pid_exit(pid: int, timeout_seconds: float = 2.0) -> bool:
        """Return whether a concrete PID disappears within a bounded wait."""
        deadline = time.monotonic() + timeout_seconds
        while time.monotonic() < deadline:
            if not UnityIl2CppBuildTests._pid_is_running(pid):
                return True
            time.sleep(0.02)
        return not UnityIl2CppBuildTests._pid_is_running(pid)

    @staticmethod
    def _pid_is_running(pid: int) -> bool:
        """Check one PID with only standard-library platform primitives."""
        if os.name != "nt":
            proc_stat = Path(f"/proc/{pid}/stat")
            if proc_stat.is_file():
                try:
                    # A reaped process can remain as a zombie briefly; it no
                    # longer executes and therefore satisfies the exit contract.
                    if proc_stat.read_text(encoding="utf-8").split()[2] == "Z":
                        return False
                except (OSError, IndexError):
                    return False
            try:
                os.kill(pid, 0)
                return True
            except ProcessLookupError:
                return False
            except PermissionError:
                return True

        from ctypes import wintypes

        process_query_limited_information = 0x1000
        still_active = 259
        kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
        kernel32.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
        kernel32.OpenProcess.restype = wintypes.HANDLE
        kernel32.GetExitCodeProcess.argtypes = [wintypes.HANDLE, ctypes.POINTER(wintypes.DWORD)]
        kernel32.GetExitCodeProcess.restype = wintypes.BOOL
        kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
        kernel32.CloseHandle.restype = wintypes.BOOL
        handle = kernel32.OpenProcess(process_query_limited_information, False, pid)
        if not handle:
            return False
        try:
            exit_code = wintypes.DWORD()
            return bool(kernel32.GetExitCodeProcess(handle, ctypes.byref(exit_code))) and exit_code.value == still_active
        finally:
            kernel32.CloseHandle(handle)

    @staticmethod
    def _kill_if_running(pid: int | None) -> None:
        """Best-effort cleanup for a failed process-tree regression."""
        if pid is None:
            return
        try:
            os.kill(pid, signal.SIGTERM if os.name == "nt" else signal.SIGKILL)
        except (OSError, ProcessLookupError):
            pass
        UnityIl2CppBuildTests._wait_for_pid_exit(pid)

class UnityIl2CppBuildTests(_UnityIl2CppBuildTestsPart1, _UnityIl2CppBuildTestsPart2, unittest.TestCase):
    """Regression coverage for Unity IL2CPP build tooling."""
    pass

UnityIl2CppBuildTests.__module__ = 'Scripts.release.regression_checks.test_release_tooling'

__all__ = ["_UnityIl2CppBuildTestsPart2", "UnityIl2CppBuildTests"]
