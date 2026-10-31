from .foundation import *

class _RunCiTestsPart4:
    """Decomposed RunCiTests method group 4."""

    def test_only_help_lists_phase184_acceptance_tooling_selector(self) -> None:
        """CLI help must name the dedicated Phase184 tooling lane honestly."""

        with mock.patch.object(sys, "argv", ["run_ci.py", "--help"]):
            with mock.patch("sys.stdout", new_callable=io.StringIO) as stdout:
                with self.assertRaises(SystemExit) as context:
                    self.run_ci.main()

        self.assertEqual(0, context.exception.code)
        self.assertIn("phase184-acceptance-tooling", stdout.getvalue())

    def test_phase184_acceptance_tooling_command_is_exact(self) -> None:
        """The dedicated lane must remain a tooling test, not claim runtime execution."""

        jobs = self.run_ci.build_default_ci_jobs(
            types.SimpleNamespace(skip_analyzer=False)
        )
        job = next(
            candidate
            for candidate in jobs
            if candidate.name == "phase184-acceptance-tooling"
        )
        self.assertEqual(
            (
                sys.executable,
                str(RUN_CI_PATH.resolve()),
                "--only",
                "phase184-acceptance-tooling",
            ),
            tuple(job.command),
        )

    def test_phase186_bridge_tooling_selector_is_default_but_live_is_not(self) -> None:
        """Ordinary CI runs static tooling; provisioned live work stays explicit."""

        jobs = self.run_ci.build_default_ci_jobs(
            types.SimpleNamespace(skip_analyzer=False)
        )
        names = [job.name for job in jobs]
        self.assertIn("phase186-bridge-tooling", names)
        self.assertNotIn("phase186-bridge-windows-live", names)
        tooling = next(job for job in jobs if job.name == "phase186-bridge-tooling")
        self.assertFalse(tooling.disable_timeout)

    def test_phase186_bridge_tooling_selector_runs_exact_static_suites_and_matrix(self) -> None:
        """The tooling selector must never launch Unity, a sidecar, or a ROS peer."""

        with mock.patch.object(self.run_ci, "run", return_value=True) as run:
            with mock.patch.object(
                sys,
                "argv",
                ["run_ci.py", "--only", "phase186-bridge-tooling"],
            ):
                self.assertEqual(0, self.run_ci.main())

        commands = [call.args[0] for call in run.call_args_list]
        self.assertEqual(
            [
                [sys.executable, "-m", "unittest", module]
                for module in self.PHASE186_BRIDGE_TOOLING_SUITES
            ]
            + [
                [sys.executable, "Scripts/package/validate_phase186_package_matrix.py"],
                [sys.executable, "-m", "Scripts.smoke.foxrun.phase186_provenance"],
            ],
            commands,
        )
        flattened = " ".join(part for command in commands for part in command)
        self.assertNotIn(
            [
                sys.executable,
                "-m",
                "Scripts.smoke.foxrun.phase186_bridge_certification",
            ],
            commands,
        )
        self.assertNotIn("phase186_bridge_acceptance --case", flattened)

        provenance_call = next(
            call
            for call in run.call_args_list
            if call.args[0][-1] == "Scripts.smoke.foxrun.regression_checks.test_phase186_provenance"
        )
        self.assertEqual(self.run_ci.job_timeout_seconds(), provenance_call.kwargs["timeout_seconds"])
        for call in run.call_args_list:
            command = call.args[0]
            if command[-1] not in self.PHASE186_BRIDGE_TOOLING_SUITES or command[-1] == "Scripts.smoke.foxrun.regression_checks.test_phase186_provenance":
                continue
            self.assertIsNot(True, call.kwargs.get("disable_timeout", False))

    def test_phase186_bridge_windows_live_selector_runs_exact_certification(self) -> None:
        """The live selector must bind certification identity to the current SHA."""

        head = "a" * 40
        with mock.patch.object(self.run_ci, "current_git_head", return_value=head):
            with mock.patch.object(self.run_ci, "run", return_value=True) as run:
                with mock.patch.object(
                    sys,
                    "argv",
                    ["run_ci.py", "--only", "phase186-bridge-windows-live"],
                ):
                    self.assertEqual(0, self.run_ci.main())

        self.assertEqual(1, run.call_count)
        command = run.call_args.args[0]
        self.assertEqual(
            [
                sys.executable,
                "-m",
                "Scripts.smoke.foxrun.phase186_bridge_certification",
                "--expected-head",
                head,
                "--output-root",
                "build/phase186/windows-live",
                "--run-id",
                self.run_ci.phase186_certification_run_id(head),
            ],
            command,
        )
        self.assertEqual(self.run_ci.job_timeout_seconds(), run.call_args.kwargs["timeout_seconds"])

    def test_phase186_bridge_windows_live_not_run_is_not_promoted_to_pass(self) -> None:
        """Any nonzero certification result, including NOT RUN, fails the selector."""

        with mock.patch.object(self.run_ci, "current_git_head", return_value="b" * 40):
            with mock.patch.object(self.run_ci, "run", return_value=False):
                with mock.patch.object(
                    sys,
                    "argv",
                    ["run_ci.py", "--only", "phase186-bridge-windows-live"],
                ):
                    self.assertEqual(1, self.run_ci.main())

    def test_phase186_selectors_are_named_in_help(self) -> None:
        """Both honest Phase186 lanes must be discoverable without reading the plan."""

        with mock.patch.object(sys, "argv", ["run_ci.py", "--help"]):
            with mock.patch("sys.stdout", new_callable=io.StringIO) as stdout:
                with self.assertRaises(SystemExit) as context:
                    self.run_ci.main()

        self.assertEqual(0, context.exception.code)
        rendered = stdout.getvalue()
        self.assertIn("phase186-bridge-tooling", rendered)
        self.assertIn("phase186-bridge-windows-live", rendered)

    def test_phase186_workflows_keep_tooling_and_provisioned_live_separate(self) -> None:
        """Hosted CI runs tooling while live certification requires a labeled owner."""

        dotnet_workflow = DOTNET_WORKFLOW_PATH.read_text(encoding="utf-8")
        self.assertIn(
            "run_ci.py --only phase186-bridge-tooling",
            dotnet_workflow,
        )
        live_workflow = PHASE186_WINDOWS_LIVE_WORKFLOW_PATH.read_text(encoding="utf-8")
        self.assertIn("workflow_dispatch:", live_workflow)
        self.assertIn("unity2foxglove-phase186-live", live_workflow)
        self.assertIn("run_ci.py --only phase186-bridge-windows-live", live_workflow)
        self.assertIn("build/phase186/windows-live", live_workflow)
        self.assertNotRegex(live_workflow, r"(?m)^\s+push:")
        self.assertNotRegex(live_workflow, r"(?m)^\s+pull_request:")

    def test_only_help_lists_dotnet_lane_selectors(self) -> None:
        """CLI help should expose each direct dotnet lane selector."""
        with mock.patch.object(sys, "argv", ["run_ci.py", "--help"]):
            with mock.patch("sys.stdout", new_callable=io.StringIO) as stdout:
                with self.assertRaises(SystemExit) as context:
                    self.run_ci.main()

        self.assertEqual(0, context.exception.code)
        for selector in ("dotnet-runtime", "xunit", "xunit-adapter", "xunit-native", "xunit-manager-boundary"):
            help_pattern = re.escape(selector).replace(r"\-", r"-\s*")
            self.assertRegex(stdout.getvalue(), help_pattern)

    def test_unknown_only_selector_is_a_usage_error(self) -> None:
        """A misspelled lane must never become a zero-work success."""
        with mock.patch.object(
            sys,
            "argv",
            ["run_ci.py", "--only", "pacakges"],
        ), mock.patch("sys.stderr", new_callable=io.StringIO) as stderr:
            with self.assertRaises(SystemExit) as context:
                self.run_ci.main()

        self.assertEqual(2, context.exception.code)
        self.assertIn("invalid choice", stderr.getvalue())

    def test_direct_analyzer_skip_is_a_usage_error(self) -> None:
        """The analyzer-only selector cannot claim success when its only lane is skipped."""
        with mock.patch.object(
            sys,
            "argv",
            ["run_ci.py", "--only", "analyzer", "--skip-analyzer"],
        ), mock.patch("sys.stderr", new_callable=io.StringIO) as stderr:
            with self.assertRaises(SystemExit) as context:
                self.run_ci.main()

        self.assertEqual(2, context.exception.code)
        self.assertIn("--skip-analyzer cannot be combined with --only analyzer", stderr.getvalue())

    def test_foxrun_command_rejects_duplicate_next_revision_options(self) -> None:
        """The FoxRun command must reject every duplicate --next-revision occurrence."""
        source = (
            ROOT
            / "Packages"
            / "dev.unity2foxglove.ros2forunity"
            / "Editor"
            / "Native"
            / "FoxRun"
            / "FoxRunRos2InterfacePackageCommand.cs"
        ).read_text(encoding="utf-8")
        self.assertIn("nextRevisionOptionCount", source)
        self.assertIn("nextRevisionOptionCount > 1", source)

    def test_foxrun_command_has_no_exit_code_local_shadowing(self) -> None:
        """The batch argument error path must compile without local-name shadowing."""
        source = (
            ROOT
            / "Packages"
            / "dev.unity2foxglove.ros2forunity"
            / "Editor"
            / "Native"
            / "FoxRun"
            / "FoxRunRos2InterfacePackageCommand.cs"
        ).read_text(encoding="utf-8")
        self.assertIn("var argumentErrorCode = ReportArgumentError", source)
        self.assertNotIn("var exitCode = ReportArgumentError", source)

    def test_all_ros2forunity_inspectors_handle_external_output_paths(self) -> None:
        """Every maintained distro inspector must report external outputs without raising."""
        for distro in ("humble", "jazzy", "lyrical"):
            source = (
                ROOT
                / "Scripts"
                / "ros2forunity"
                / "windows"
                / distro
                / "inspect_r2fu_runtime_artifact.py"
            ).read_text(encoding="utf-8")
            self.assertIn("display_output", source)
            self.assertIn("except ValueError", source)

    def test_empty_ci_result_summary_is_non_pass(self) -> None:
        """An aggregate with no executed lanes must fail instead of vacuously passing."""
        with mock.patch("sys.stdout", new_callable=io.StringIO) as stdout:
            result = self.run_ci.report_ci_job_results({})

        self.assertEqual(1, result)
        self.assertIn("No CI checks were executed.", stdout.getvalue())

    def test_default_analyzer_skip_exposes_machine_readable_lane(self) -> None:
        """A permitted partial default run must identify its skipped analyzer lane explicitly."""
        with mock.patch.object(self.run_ci, "run_ci_jobs", return_value={"other": True}):
            with mock.patch.object(
                sys,
                "argv",
                ["run_ci.py", "--skip-analyzer"],
            ), mock.patch("sys.stdout", new_callable=io.StringIO) as stdout:
                result = self.run_ci.main()

        rendered = stdout.getvalue()
        self.assertEqual(0, result)
        self.assertIn("[SKIP] analyzer", rendered)
        self.assertIn("SKIPPED_LANES=analyzer", rendered)
        self.assertIn("All executed CI checks passed.", rendered)
        self.assertNotIn("All CI checks passed.", rendered)

    def test_default_ci_marks_resource_heavy_lanes_exclusive(self) -> None:
        """Resource-heavy lanes serialize so concurrent jobs cannot exhaust the host."""
        jobs = self.run_ci.build_default_ci_jobs(types.SimpleNamespace(skip_analyzer=False))

        self.assertEqual("dotnet", self.run_ci.DOTNET_CI_EXCLUSIVE_GROUP)
        self.assertEqual(
            {
                "analyzer": self.run_ci.DOTNET_CI_EXCLUSIVE_GROUP,
                "dotnet-runtime": self.run_ci.DOTNET_CI_EXCLUSIVE_GROUP,
                "mediafoundation-native-smoke": self.run_ci.DOTNET_CI_EXCLUSIVE_GROUP,
                "xunit": self.run_ci.DOTNET_CI_EXCLUSIVE_GROUP,
                "xunit-adapter": self.run_ci.DOTNET_CI_EXCLUSIVE_GROUP,
                "xunit-native": self.run_ci.DOTNET_CI_EXCLUSIVE_GROUP,
                "xunit-manager-boundary": self.run_ci.DOTNET_CI_EXCLUSIVE_GROUP,
                "ros2bridge-xunit": self.run_ci.DOTNET_CI_EXCLUSIVE_GROUP,
                "performance-regression": self.run_ci.DOTNET_CI_EXCLUSIVE_GROUP,
                "foxrun-publish-panel": None,
                "phase179-ros2-regression": None,
                "phase181-ros2-regression": None,
                 "phase184-acceptance-tooling": self.run_ci.DOTNET_CI_EXCLUSIVE_GROUP,
                 "phase186-bridge-tooling": self.run_ci.DOTNET_CI_EXCLUSIVE_GROUP,
                 "mcap-conformance": self.run_ci.DOTNET_CI_EXCLUSIVE_GROUP,
                 "packages": self.run_ci.DOTNET_CI_EXCLUSIVE_GROUP,
                "boundary": None,
            },
            {job.name: job.exclusive_group for job in jobs},
        )

    def test_main_dispatches_dotnet_parent_through_flat_parallel_jobs(self) -> None:
        """The dotnet parent selection should use the top-level lane scheduler."""
        observed: dict[str, object] = {}

        def fake_run_ci_jobs(jobs, max_workers):
            """Capture the flattened lane jobs without executing subprocesses."""
            observed["names"] = [job.name for job in jobs]
            observed["max_workers"] = max_workers
            return {job.name: True for job in jobs}

        with mock.patch.object(self.run_ci, "run_ci_jobs", side_effect=fake_run_ci_jobs):
            with mock.patch.object(self.run_ci, "restore_with_ignoring_failed_sources", return_value=True):
                with mock.patch.object(self.run_ci, "run_with_restore_fallback", return_value=True):
                    with mock.patch.object(sys, "argv", ["run_ci.py", "--only", "dotnet", "--jobs", "2"]):
                        self.assertEqual(0, self.run_ci.main())

        self.assertEqual(
            [
                "dotnet-runtime",
                "mediafoundation-native-smoke",
                "xunit",
                "xunit-adapter",
                "xunit-native",
                "xunit-manager-boundary",
                "ros2bridge-xunit",
                "performance-regression",
            ],
            observed.get("names"),
        )
        self.assertEqual(2, observed.get("max_workers"))

    def _flat_dotnet_lane_cases(self):
        """Return the exact restore and command contracts for every flat dotnet lane."""
        return [
            (
                "dotnet-runtime",
                self.run_ci.RUNTIME_TESTS_PROJ,
                self.run_ci.RUNTIME_TEST_PROPS,
                "Restore runtime test project",
                [
                    "dotnet",
                    "run",
                    "--no-restore",
                    "--project",
                    self.run_ci.RUNTIME_TESTS_PROJ,
                    *self.run_ci.RUNTIME_TEST_PROPS,
                ],
                "Dotnet validation suite (default CI)",
                None,
                None,
            ),
            (
                "xunit",
                self.run_ci.UNIT_TESTS_PROJ,
                self.run_ci.UNIT_TEST_PROPS,
                "Restore xUnit unit test project",
                [
                    "dotnet",
                    "test",
                    "--no-restore",
                    self.run_ci.UNIT_TESTS_PROJ,
                    *self.run_ci.UNIT_TEST_PROPS,
                    "--logger",
                    "trx;LogFileName=unit-tests.trx",
                    "--results-directory",
                    str(self.run_ci.UNIT_TEST_RESULTS_DIR),
                ],
                "xUnit unit tests",
                "unit-tests.trx",
                self.run_ci.CI_ROOT / "test-results" / "unit",
            ),
            (
                "xunit-adapter",
                self.run_ci.UNIT_TESTS_PROJ,
                self.run_ci.UNIT_ADAPTER_TEST_PROPS,
                "Restore xUnit optional ROS2 adapter lane",
                [
                    "dotnet",
                    "test",
                    "--no-restore",
                    self.run_ci.UNIT_TESTS_PROJ,
                    *self.run_ci.UNIT_ADAPTER_TEST_PROPS,
                    "--logger",
                    "trx;LogFileName=unit-tests-adapter.trx",
                    "--results-directory",
                    str(self.run_ci.UNIT_ADAPTER_TEST_RESULTS_DIR),
                ],
                "xUnit optional ROS2 adapter unit tests",
                "unit-tests-adapter.trx",
                self.run_ci.CI_ROOT / "test-results" / "unit-adapter",
            ),
            (
                "xunit-native",
                self.run_ci.UNIT_TESTS_PROJ,
                self.run_ci.UNIT_NATIVE_TEST_PROPS,
                "Restore xUnit Native ROS2 compilation lane",
                [
                    "dotnet",
                    "test",
                    "--no-restore",
                    self.run_ci.UNIT_TESTS_PROJ,
                    *self.run_ci.UNIT_NATIVE_TEST_PROPS,
                    "--logger",
                    "trx;LogFileName=unit-tests-native.trx",
                    "--results-directory",
                    str(self.run_ci.UNIT_NATIVE_TEST_RESULTS_DIR),
                ],
                "xUnit Native ROS2 compilation unit tests",
                "unit-tests-native.trx",
                self.run_ci.CI_ROOT / "test-results" / "unit-native",
            ),
            (
                "xunit-manager-boundary",
                self.run_ci.UNIT_TESTS_PROJ,
                self.run_ci.UNIT_MANAGER_BOUNDARY_TEST_PROPS,
                "Restore Manager production-boundary xUnit lane",
                [
                    "dotnet",
                    "test",
                    "--no-restore",
                    self.run_ci.UNIT_TESTS_PROJ,
                    *self.run_ci.UNIT_MANAGER_BOUNDARY_TEST_PROPS,
                    "--logger",
                    "trx;LogFileName=unit-tests-manager-boundary.trx",
                    "--results-directory",
                    str(self.run_ci.UNIT_MANAGER_BOUNDARY_TEST_RESULTS_DIR),
                ],
                "xUnit Manager production-boundary tests",
                "unit-tests-manager-boundary.trx",
                self.run_ci.UNIT_MANAGER_BOUNDARY_TEST_RESULTS_DIR,
            ),
        ]

__all__ = ["_RunCiTestsPart4"]
