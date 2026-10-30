from .foundation import *
from .run_ci_tests_part_1 import _RunCiTestsPart1
from .run_ci_tests_part_2 import _RunCiTestsPart2
from .run_ci_tests_part_3 import _RunCiTestsPart3
from .run_ci_tests_part_4 import _RunCiTestsPart4

class _RunCiTestsPart5:
    """Decomposed RunCiTests method group 5."""

    def test_flat_dotnet_selectors_restore_and_run_isolated_lanes(self) -> None:
        """Each flat selector should restore its isolated lane then run it once without restore."""
        cases = self._flat_dotnet_lane_cases()
        xunit_artifacts: list[tuple[str, str]] = []
        lane_msbuild_roots: list[tuple[str, ...]] = []

        for (
            selector,
            project,
            props,
            restore_label,
            command,
            run_label,
            trx_name,
            results_dir,
        ) in cases:
            with self.subTest(selector=selector):
                with mock.patch.object(
                    self.run_ci,
                    "restore_with_ignoring_failed_sources",
                    return_value=True,
                ) as restore:
                    with mock.patch.object(self.run_ci, "run", return_value=True) as run:
                        with mock.patch.object(self.run_ci, "run_with_restore_fallback") as fallback:
                            with mock.patch.object(sys, "argv", ["run_ci.py", "--only", selector]):
                                self.assertEqual(0, self.run_ci.main())

                restore.assert_called_once_with(project, restore_label, props, fatal=False)
                run.assert_called_once_with(command, run_label)
                fallback.assert_not_called()
                self.assertIn("--no-restore", command)
                isolated_roots = [
                    prop.split("=", 1)[1]
                    for prop in props
                    if prop.startswith(
                        (
                            "-p:BaseOutputPath=",
                            "-p:BaseIntermediateOutputPath=",
                            "-p:MSBuildProjectExtensionsPath=",
                            "-p:RestoreOutputPath=",
                        )
                    )
                ]
                self.assertEqual(4, len(isolated_roots))
                isolated_root = str(self.run_ci.ISOLATED_DOTNET_ROOT.resolve()).replace("\\", "/") + "/"
                self.assertTrue(all(root.startswith(isolated_root) for root in isolated_roots))
                lane_msbuild_roots.append(
                    tuple(root.replace("\\", "/").rstrip("/") for root in isolated_roots)
                )

                if selector == "xunit-adapter":
                    self.assertIn("-p:IncludeRos2ForUnityAdapter=true", props)
                if selector == "xunit-native":
                    self.assertIn("-p:IncludeRos2ForUnityNative=true", props)
                if selector == "xunit-manager-boundary":
                    self.assertIn("-p:IncludeManagerProductionBoundary=true", props)
                if trx_name is not None:
                    self.assertIsNotNone(results_dir)
                    self.assertIn(f"trx;LogFileName={trx_name}", command)
                    self.assertIn(str(results_dir), command)
                    xunit_artifacts.append((trx_name, str(results_dir)))

        self.assertEqual(4, len({trx_name for trx_name, _ in xunit_artifacts}))
        self.assertEqual(4, len({results_dir for _, results_dir in xunit_artifacts}))
        self.assertEqual(5, len(lane_msbuild_roots))
        self.assertEqual(5, len(set(lane_msbuild_roots)))

    def test_flat_dotnet_lane_failure_does_not_retry_with_restore(self) -> None:
        """Every failed lane should report failure after one no-restore command."""
        for (
            selector,
            project,
            props,
            restore_label,
            command,
            run_label,
            _trx_name,
            _results_dir,
        ) in self._flat_dotnet_lane_cases():
            with self.subTest(selector=selector):
                with mock.patch.object(
                    self.run_ci,
                    "restore_with_ignoring_failed_sources",
                    return_value=True,
                ) as restore:
                    with mock.patch.object(self.run_ci, "run", return_value=False) as run:
                        with mock.patch.object(self.run_ci, "run_with_restore_fallback") as fallback:
                            with mock.patch.object(sys, "argv", ["run_ci.py", "--only", selector]):
                                self.assertEqual(1, self.run_ci.main())

                restore.assert_called_once_with(project, restore_label, props, fatal=False)
                run.assert_called_once_with(command, run_label)
                self.assertIn("--no-restore", command)
                fallback.assert_not_called()

    def test_flat_dotnet_lane_restore_failure_skips_the_no_restore_command(self) -> None:
        """Every failed explicit restore should fail its lane without running its test command."""
        for (
            selector,
            project,
            props,
            restore_label,
            _command,
            _run_label,
            _trx_name,
            _results_dir,
        ) in self._flat_dotnet_lane_cases():
            with self.subTest(selector=selector):
                with mock.patch.object(
                    self.run_ci,
                    "restore_with_ignoring_failed_sources",
                    return_value=False,
                ) as restore:
                    with mock.patch.object(self.run_ci, "run") as run:
                        with mock.patch.object(self.run_ci, "run_with_restore_fallback") as fallback:
                            with mock.patch.object(sys, "argv", ["run_ci.py", "--only", selector]):
                                self.assertEqual(1, self.run_ci.main())

                restore.assert_called_once_with(project, restore_label, props, fatal=False)
                run.assert_not_called()
                fallback.assert_not_called()

    def test_run_ci_jobs_limits_active_jobs_until_release(self) -> None:
        """The real top-level scheduler should not start a third job before release."""
        jobs = [
            self.run_ci.CiJob("first", ["first"]),
            self.run_ci.CiJob("second", ["second"]),
            self.run_ci.CiJob("third", ["third"]),
        ]
        release = threading.Event()
        two_jobs_started = threading.Event()
        third_job_started = threading.Event()
        state_lock = threading.Lock()
        state = {"active": 0, "max_active": 0, "started_before_release": []}
        outcome: dict[str, object] = {}
        worker_errors: list[BaseException] = []

        def fake_run_ci_job(job, log_dir):
            """Hold worker slots until the test releases the synthetic jobs."""
            with state_lock:
                state["active"] += 1
                state["max_active"] = max(state["max_active"], state["active"])
                if not release.is_set():
                    state["started_before_release"].append(job.name)
                    if len(state["started_before_release"]) == 2:
                        two_jobs_started.set()
                    elif len(state["started_before_release"]) == 3:
                        third_job_started.set()
            try:
                release.wait()
                return self.run_ci.CiJobResult(job.name, True, 0, 0.0, log_dir / f"{job.name}.log")
            finally:
                with state_lock:
                    state["active"] -= 1

        def run_scheduler() -> None:
            """Run the real scheduler without blocking the test thread."""
            try:
                outcome["results"] = self.run_ci.run_ci_jobs(jobs, max_workers=2)
            except BaseException as exc:  # pragma: no cover - asserted by the test thread.
                worker_errors.append(exc)

        with tempfile.TemporaryDirectory() as temp:
            with mock.patch.object(self.run_ci, "CI_ROOT", Path(temp)):
                with mock.patch.object(self.run_ci, "_run_ci_job", side_effect=fake_run_ci_job):
                    scheduler_thread = threading.Thread(target=run_scheduler)
                    scheduler_thread.start()
                    try:
                        self.assertTrue(two_jobs_started.wait(timeout=2), "two workers did not start")
                        self.assertFalse(
                            third_job_started.wait(timeout=0.5),
                            "a third job started before a worker slot was released",
                        )
                        with state_lock:
                            self.assertEqual(2, len(state["started_before_release"]))
                            self.assertLessEqual(state["max_active"], 2)
                    finally:
                        release.set()
                        scheduler_thread.join(timeout=5)

        self.assertFalse(scheduler_thread.is_alive(), "scheduler did not finish after release")
        self.assertEqual([], worker_errors)
        self.assertEqual({job.name: True for job in jobs}, outcome["results"])

    def test_run_ci_jobs_admits_compatible_work_while_dotnet_group_is_active(self) -> None:
        """A blocked dotnet job should leave a worker free for an unrelated pending job."""
        jobs = [
            types.SimpleNamespace(
                name="dotnet-a",
                command=["dotnet-a"],
                disable_timeout=False,
                exclusive_group="dotnet",
            ),
            types.SimpleNamespace(
                name="dotnet-b",
                command=["dotnet-b"],
                disable_timeout=False,
                exclusive_group="dotnet",
            ),
            types.SimpleNamespace(
                name="other",
                command=["other"],
                disable_timeout=False,
                exclusive_group=None,
            ),
        ]
        dotnet_a_started = threading.Event()
        dotnet_a_release = threading.Event()
        dotnet_b_started = threading.Event()
        other_finished = threading.Event()
        started_names: list[str] = []
        state_lock = threading.Lock()
        outcome: dict[str, object] = {}
        worker_errors: list[BaseException] = []

        def fake_run_ci_job(job, log_dir):
            """Control only worker completion while retaining the real admission scheduler."""
            with state_lock:
                started_names.append(job.name)
            if job.name == "dotnet-a":
                dotnet_a_started.set()
                dotnet_a_release.wait()
            elif job.name == "dotnet-b":
                dotnet_b_started.set()
            elif job.name == "other":
                other_finished.set()
            return self.run_ci.CiJobResult(job.name, True, 0, 0.0, log_dir / f"{job.name}.log")

        def run_scheduler() -> None:
            """Run the real scheduler off the test thread so it can await controlled workers."""
            try:
                outcome["results"] = self.run_ci.run_ci_jobs(jobs, max_workers=2)
            except BaseException as exc:  # pragma: no cover - asserted by the test thread.
                worker_errors.append(exc)

        with tempfile.TemporaryDirectory() as temp:
            with mock.patch.object(self.run_ci, "CI_ROOT", Path(temp)):
                with mock.patch.object(self.run_ci, "_run_ci_job", side_effect=fake_run_ci_job):
                    scheduler_thread = threading.Thread(target=run_scheduler)
                    scheduler_thread.start()
                    try:
                        self.assertTrue(dotnet_a_started.wait(timeout=2), "dotnet-a did not start")
                        self.assertTrue(other_finished.wait(timeout=2), "compatible job did not start")
                        self.assertFalse(
                            dotnet_b_started.wait(timeout=0.5),
                            "second dotnet job started before the active group released",
                        )
                        with state_lock:
                            self.assertEqual({"dotnet-a", "other"}, set(started_names))
                        dotnet_a_release.set()
                        self.assertTrue(dotnet_b_started.wait(timeout=2), "dotnet-b did not start after release")
                    finally:
                        dotnet_a_release.set()
                        scheduler_thread.join(timeout=5)

        self.assertFalse(scheduler_thread.is_alive(), "scheduler did not finish after release")
        self.assertEqual([], worker_errors)
        self.assertEqual(
            [("dotnet-a", True), ("dotnet-b", True), ("other", True)],
            list(outcome["results"].items()),
        )

    def test_run_ci_jobs_releases_dotnet_group_after_normal_failure(self) -> None:
        """A normal failed grouped job should release its group for the next grouped job."""
        jobs = [
            types.SimpleNamespace(
                name="dotnet-a",
                command=["dotnet-a"],
                disable_timeout=False,
                exclusive_group="dotnet",
            ),
            types.SimpleNamespace(
                name="dotnet-b",
                command=["dotnet-b"],
                disable_timeout=False,
                exclusive_group="dotnet",
            ),
        ]
        dotnet_a_started = threading.Event()
        dotnet_a_release = threading.Event()
        dotnet_b_started = threading.Event()
        outcome: dict[str, object] = {}
        worker_errors: list[BaseException] = []

        def fake_run_ci_job(job, log_dir):
            """Return an ordinary failed result for the group owner after controlled release."""
            if job.name == "dotnet-a":
                dotnet_a_started.set()
                dotnet_a_release.wait()
                log_path = log_dir / f"{job.name}.log"
                log_path.write_text("synthetic normal failure\n", encoding="utf-8")
                return self.run_ci.CiJobResult(
                    job.name,
                    False,
                    1,
                    0.0,
                    log_path,
                )
            dotnet_b_started.set()
            return self.run_ci.CiJobResult(job.name, True, 0, 0.0, log_dir / f"{job.name}.log")

        def run_scheduler() -> None:
            """Run the real scheduler off the test thread so it can await controlled workers."""
            try:
                outcome["results"] = self.run_ci.run_ci_jobs(jobs, max_workers=2)
            except BaseException as exc:  # pragma: no cover - asserted by the test thread.
                worker_errors.append(exc)

        with tempfile.TemporaryDirectory() as temp:
            with mock.patch.object(self.run_ci, "CI_ROOT", Path(temp)):
                with mock.patch.object(self.run_ci, "_run_ci_job", side_effect=fake_run_ci_job):
                    scheduler_thread = threading.Thread(target=run_scheduler)
                    scheduler_thread.start()
                    try:
                        self.assertTrue(dotnet_a_started.wait(timeout=2), "dotnet-a did not start")
                        self.assertFalse(
                            dotnet_b_started.wait(timeout=0.5),
                            "dotnet-b started before the failed owner released its group",
                        )
                        dotnet_a_release.set()
                        self.assertTrue(dotnet_b_started.wait(timeout=2), "dotnet-b did not start after failure")
                    finally:
                        dotnet_a_release.set()
                        scheduler_thread.join(timeout=5)

        self.assertFalse(scheduler_thread.is_alive(), "scheduler did not finish after failure")
        self.assertEqual([], worker_errors)
        self.assertEqual(
            [("dotnet-a", False), ("dotnet-b", True)],
            list(outcome["results"].items()),
        )

    def test_mcap_conformance_disables_wall_clock_timeout(self) -> None:
        """The external differential gate should not assume how fast the host machine is."""
        observed: dict[str, object] = {}

        def fake_run(cmd, label, **kwargs):
            """Capture the dedicated conformance command without executing it."""
            observed["cmd"] = cmd
            observed["label"] = label
            observed["timeout_seconds"] = kwargs.get("timeout_seconds")
            return True

        with mock.patch.object(self.run_ci, "run", side_effect=fake_run):
            with mock.patch.object(sys, "argv", ["run_ci.py", "--only", "mcap-conformance"]):
                self.assertEqual(0, self.run_ci.main())

        self.assertIn("run_phase121_conformance.py", " ".join(observed["cmd"]))
        self.assertEqual("Official MCAP differential conformance", observed["label"])
        self.assertEqual(self.run_ci.job_timeout_seconds(), observed["timeout_seconds"])

    def test_parallel_mcap_job_disables_wall_clock_timeout(self) -> None:
        """The parent CI process must not reintroduce a deadline around the MCAP child."""
        command = [
            sys.executable,
            "-c",
            "import sys; sys.stdout.write('x' * 1048576); sys.stdout.flush()",
        ]
        with tempfile.TemporaryDirectory() as temp:
            job = self.run_ci.CiJob("mcap-conformance", command, disable_timeout=True)
            result = self.run_ci._run_ci_job(job, Path(temp))

        self.assertTrue(result.ok)
        self.assertEqual(0, result.returncode)

    def test_parallel_job_drains_noisy_child_before_wait_deadlock(self) -> None:
        """A noisy child must complete instead of filling the redirected pipe."""
        command = [
            sys.executable,
            "-c",
            "import sys; sys.stdout.write('x' * 1048576); sys.stdout.flush()",
        ]
        with tempfile.TemporaryDirectory() as temp:
            job = self.run_ci.CiJob("pipe-drain", command)
            with mock.patch.object(self.run_ci, "job_timeout_seconds", return_value=20):
                result = self.run_ci._run_ci_job(job, Path(temp))

        self.assertTrue(result.ok)
        self.assertEqual(0, result.returncode)
        self.assertLess(result.elapsed_seconds, 10)

    def test_main_dispatches_default_ci_through_parallel_jobs(self) -> None:
        """Without --only, CI should use the parallel job runner and aggregate job results."""
        observed: dict[str, object] = {}

        def fake_run_ci_jobs(jobs, max_workers):
            """Capture default CI jobs without executing subprocesses."""
            observed["names"] = [job.name for job in jobs]
            observed["max_workers"] = max_workers
            return {job.name: True for job in jobs}

        with mock.patch.object(self.run_ci, "run_ci_jobs", side_effect=fake_run_ci_jobs):
            with mock.patch.object(sys, "argv", ["run_ci.py", "--jobs", "2"]):
                self.assertEqual(0, self.run_ci.main())

        self.assertEqual(
            [
                "analyzer",
                "dotnet-runtime",
                "mediafoundation-native-smoke",
                "xunit",
                "xunit-adapter",
                "xunit-native",
                "xunit-manager-boundary",
                "ros2bridge-xunit",
                "performance-regression",
                "foxrun-publish-panel",
                "phase179-ros2-regression",
                "phase181-ros2-regression",
                "phase184-acceptance-tooling",
                "phase186-bridge-tooling",
                "mcap-conformance",
                "packages",
                "boundary",
            ],
            observed["names"],
        )
        self.assertEqual(2, observed["max_workers"])

class RunCiTests(_RunCiTestsPart1, _RunCiTestsPart2, _RunCiTestsPart3, _RunCiTestsPart4, _RunCiTestsPart5, unittest.TestCase):
    """Regression coverage for local CI runner reliability."""
    pass

RunCiTests.__module__ = 'Scripts.release.regression_checks.test_release_tooling'

__all__ = ["_RunCiTestsPart5", "RunCiTests"]
