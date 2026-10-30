from .foundation import *

class _RunCiTestsPart2:
    """Decomposed RunCiTests method group 2."""

    def test_run_parallel_replays_captured_timeout_once_in_declaration_order(self) -> None:
        """Captured timeouts should retain partial output and use one timeout-specific replay diagnostic."""
        captured_results = {
            "first validator": self.run_ci.CapturedCommandResult(
                label="first validator",
                ok=True,
                returncode=0,
                elapsed_seconds=1.2,
                stdout="first output\n",
                stderr="",
            ),
            "timeout validator": self.run_ci.CapturedCommandResult(
                label="timeout validator",
                ok=False,
                returncode=124,
                elapsed_seconds=4.6,
                stdout="partial timeout stdout\n",
                stderr="partial timeout stderr\n",
                timeout_seconds=7,
            ),
        }

        def fake_run_captured(_cmd: list[str], label: str):
            """Return captured results without starting a subprocess."""
            return captured_results[label]

        with mock.patch.object(self.run_ci, "run_captured", side_effect=fake_run_captured):
            with mock.patch("sys.stdout", new_callable=io.StringIO) as stdout:
                with mock.patch("sys.stderr", new_callable=io.StringIO) as stderr:
                    results = self.run_ci.run_parallel(
                        [
                            ("first validator", ["first"]),
                            ("timeout validator", ["timeout"]),
                        ]
                    )

        rendered_stdout = stdout.getvalue()
        rendered_stderr = stderr.getvalue()
        rendered = rendered_stdout + rendered_stderr
        self.assertEqual({"first validator": True, "timeout validator": False}, results)
        self.assertLess(rendered_stdout.index("--- first validator ---"), rendered_stdout.index("--- timeout validator ---"))
        self.assertLess(rendered_stdout.index("first output"), rendered_stdout.index("--- timeout validator ---"))
        self.assertIn("partial timeout stdout", rendered_stdout)
        self.assertIn("partial timeout stderr", rendered_stderr)
        self.assertIn(
            f"{self.run_ci.FAIL} timeout validator timed out after 7s (4.6s)",
            rendered_stdout,
        )
        self.assertEqual(1, rendered.count(f"{self.run_ci.FAIL} timeout validator"))
        self.assertNotIn(f"{self.run_ci.FAIL} timeout validator (exit 124)", rendered)

    def test_run_timeout_reports_reason_and_elapsed_seconds(self) -> None:
        """Timed-out direct commands should retain their limit and show elapsed time."""
        with mock.patch.object(self.run_ci.time, "monotonic", side_effect=[30.0, 31.28]):
            with mock.patch.object(
                self.run_ci,
                "_run_owned_command",
                return_value=self.run_ci.OwnedCommandResult(124, timed_out=True),
            ):
                with mock.patch("sys.stdout", new_callable=io.StringIO) as stdout:
                    self.assertFalse(self.run_ci.run(["tool"], "timeout", timeout_seconds=7))

        rendered = stdout.getvalue()
        self.assertIn(f"{self.run_ci.FAIL} timeout timed out after 7s", rendered)
        self.assertIn("(1.3s)", rendered)

    def test_run_ci_reports_timeout_without_hanging(self) -> None:
        """Subprocess timeouts should fail the command instead of hanging local CI."""
        with mock.patch.dict(os.environ, {"UNITY2FOXGLOVE_CI_TIMEOUT": "1"}):
            with mock.patch.object(
                self.run_ci,
                "_run_owned_command",
                return_value=self.run_ci.OwnedCommandResult(124, timed_out=True),
            ):
                self.assertFalse(self.run_ci.run(["tool"], "timeout", fatal=False))
                with self.assertRaises(SystemExit) as context:
                    self.run_ci.run(["tool"], "fatal-timeout", fatal=True)
        self.assertEqual(124, context.exception.code)

    def test_run_can_disable_wall_clock_timeout(self) -> None:
        """Finite gates should be allowed to finish without a machine-specific deadline."""
        completed = subprocess.CompletedProcess(args=["tool"], returncode=0, stdout="", stderr="")

        with mock.patch.object(
            self.run_ci,
            "_run_owned_command",
            return_value=self.run_ci.OwnedCommandResult(0),
        ) as run_process:
            self.assertTrue(self.run_ci.run(["tool"], "finite gate", disable_timeout=True))

        self.assertIsNone(run_process.call_args.kwargs["timeout_seconds"])

    def test_run_timeout_terminates_descendant_process(self) -> None:
        """The uncaptured runner must terminate a child that outlives its parent."""
        self._assert_runner_timeout_terminates_descendant(captured=False)

    def test_run_captured_timeout_terminates_descendant_process(self) -> None:
        """The captured runner must terminate a child that outlives its parent."""
        self._assert_runner_timeout_terminates_descendant(captured=True)

    def test_owned_command_keeps_success_after_residual_tree_is_cleaned(self) -> None:
        """A transient descendant must not turn a successful command into exit 125 after cleanup."""
        process = mock.Mock(returncode=0)
        process.communicate.return_value = ("output\n", "")
        tree = mock.Mock(process=process)
        tree.terminate.return_value = []

        with mock.patch.object(self.run_ci, "start_owned_process", return_value=tree):
            with mock.patch.object(self.run_ci, "await_tree_quiescence", return_value=[424242]):
                result = self.run_ci._run_owned_command(
                    ["tool"], capture_output=True, timeout_seconds=10
                )

        self.assertEqual(0, result.returncode)
        self.assertEqual("output\n", result.stdout)
        self.assertEqual((), result.residual_pids)
        tree.terminate.assert_called_once_with()

    def test_owned_command_does_not_promote_unknown_returncode_to_success(self) -> None:
        """An unavailable process return code must not be rewritten as zero."""
        process = mock.Mock(returncode=None)
        process.communicate.return_value = ("output\n", "")
        tree = mock.Mock(process=process)
        tree.terminate.return_value = []

        with mock.patch.object(self.run_ci, "start_owned_process", return_value=tree):
            with mock.patch.object(self.run_ci, "await_tree_quiescence", return_value=[]):
                result = self.run_ci._run_owned_command(
                    ["tool"], capture_output=True, timeout_seconds=10
                )

        self.assertIsNone(result.returncode)

    def test_owned_command_fails_closed_when_cleanup_leaves_residual_tree(self) -> None:
        """A descendant that survives termination must retain the fail-closed result."""
        process = mock.Mock(returncode=0)
        process.communicate.return_value = ("output\n", "")
        tree = mock.Mock(process=process)
        tree.terminate.return_value = [424242]

        with mock.patch.object(self.run_ci, "start_owned_process", return_value=tree):
            with mock.patch.object(self.run_ci, "await_tree_quiescence", return_value=[424242]):
                result = self.run_ci._run_owned_command(
                    ["tool"], capture_output=True, timeout_seconds=10
                )

        self.assertEqual(125, result.returncode)
        self.assertEqual((424242,), result.residual_pids)

    def _assert_runner_timeout_terminates_descendant(self, *, captured: bool) -> None:
        """Verify timeout cleanup reaches descendants for both runner surfaces."""
        with tempfile.TemporaryDirectory() as temp:
            child_pid_path = Path(temp) / "child.pid"
            parent_code = (
                "import pathlib, subprocess, sys, time; "
                f"child=subprocess.Popen([sys.executable, '-c', 'import time; time.sleep(60)']); "
                f"pathlib.Path({str(child_pid_path)!r}).write_text(str(child.pid), encoding='utf-8'); "
                "time.sleep(60)"
            )
            command = [sys.executable, "-c", parent_code]
            if captured:
                with mock.patch.object(self.run_ci, "command_timeout_seconds", return_value=5):
                    result = self.run_ci.run_captured(command, "captured process tree timeout")
                self.assertFalse(result.ok)
                self.assertEqual(124, result.returncode)
            else:
                result = self.run_ci.run(command, "process tree timeout", timeout_seconds=5)
                self.assertFalse(result)

            deadline = time.monotonic() + 4
            child_pid = None
            while time.monotonic() < deadline:
                if child_pid_path.is_file():
                    child_pid = int(child_pid_path.read_text(encoding="utf-8"))
                    if not self._pid_is_running(child_pid):
                        break
                time.sleep(0.05)
            self.assertIsNotNone(child_pid)
            self.assertFalse(self._pid_is_running(child_pid), f"descendant remained alive: {child_pid}")

    @staticmethod
    def _pid_is_running(pid: int) -> bool:
        """Return whether the operating-system process identifier is alive."""
        try:
            os.kill(pid, 0)
        except (OSError, ProcessLookupError):
            return False
        return True

    def test_default_ci_builds_independent_subcommand_jobs(self) -> None:
        """Default local CI should enqueue every dotnet lane as a self-subcommand."""
        args = types.SimpleNamespace(skip_analyzer=False)

        jobs = self.run_ci.build_default_ci_jobs(args)

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
            [job.name for job in jobs],
        )
        for job in jobs:
            self.assertEqual(
                [sys.executable, str(RUN_CI_PATH.resolve()), "--only", job.name],
                job.command,
        )
        self.assertEqual(
            {
                "phase184-acceptance-tooling",
            },
            {job.name for job in jobs if job.disable_timeout},
        )

    def test_phase184_acceptance_regression_module_constants_are_exact(self) -> None:
        """The tooling lane must name only the six maintained pure unittest modules."""
        expected = {
            name: module
            for name, module, _label in self.PHASE184_ACCEPTANCE_TOOLING_SUITES
        }

        self.assertEqual(
            expected,
            {name: getattr(self.run_ci, name, None) for name in expected},
        )

    def test_maintained_ci_dispatches_phase185_and_phase186_manual_regressions(self) -> None:
        """Maintained CI must execute the standalone Phase185 and Phase186-H suites."""
        self.assertIn(
            "Scripts.smoke.foxrun.regression_checks.test_phase185_foxrun_messagepack_probe",
            self.run_ci.PACKAGE_LANE_REGRESSION_MODULES,
        )
        self.assertIn(
            "Scripts.smoke.foxrun.regression_checks.test_phase186_bridge_manual",
            self.run_ci.PACKAGE_LANE_REGRESSION_MODULES,
        )

    def test_unity_build_requires_succeeded_build_result(self) -> None:
        """Unity build helper must gate success on BuildReport.summary.result."""
        source = (ROOT / "Unity2Foxglove" / "Assets" / "Editor" / "FoxgloveBuild.cs").read_text(encoding="utf-8")
        self.assertIn("report.summary.result == BuildResult.Succeeded", source)
        self.assertIn("report.summary.totalErrors == 0", source)

    def test_mcap_preflight_binds_latest_completion_and_clears_stale_evidence(self) -> None:
        """Replay preflight must not overwrite edits or expose evidence for a failed path."""
        source = (ROOT / "Packages" / "dev.unity2foxglove.sdk" / "Editor" / "Manager" / "McapReplayPreflightDrawer.cs").read_text(encoding="utf-8")
        self.assertIn("_pendingLatestReplayPathSnapshot", source)
        self.assertIn("ClearCurrentEvidence", source)

    def test_latest_recording_scan_is_streaming_and_cancellable(self) -> None:
        """Latest-recording discovery must avoid eager path-array materialization."""
        source = (ROOT / "Packages" / "dev.unity2foxglove.sdk" / "Editor" / "Manager" / "McapReplayPreflightDrawer.cs").read_text(encoding="utf-8")
        self.assertIn("Directory.EnumerateFiles", source)
        self.assertNotIn("Directory.GetFiles(recordingsDir, \"*.mcap\", SearchOption.AllDirectories)", source)

    def test_mcap_preflight_rejects_malformed_manifest_hash_identity(self) -> None:
        """Replay identity must not classify arbitrary text as a manifest hash."""
        source = (ROOT / "Packages" / "dev.unity2foxglove.sdk" / "Editor" / "Manager" / "McapReplayPreflightDrawer.cs").read_text(encoding="utf-8")
        self.assertIn("IsValidFoxRunHash", source)
        self.assertIn("if (!IsValidFoxRunHash(hash))", source)
        self.assertIn("return string.Empty;", source)

    def test_encoding_inspectors_preserve_unsupported_serialized_values(self) -> None:
        """Enum inspectors must not rewrite forward-compatible raw values on repaint."""
        foxrun = (ROOT / "Packages" / "dev.unity2foxglove.sdk" / "Editor" / "Shared" / "FoxRunEncodingEditorLabels.cs").read_text(encoding="utf-8")
        publisher = (ROOT / "Packages" / "dev.unity2foxglove.sdk" / "Editor" / "Shared" / "PublisherEncodingEditorLabels.cs").read_text(encoding="utf-8")
        self.assertIn("EditorGUI.BeginChangeCheck", foxrun)
        self.assertIn("EditorGUI.EndChangeCheck", foxrun)
        self.assertIn("EditorGUI.BeginChangeCheck", publisher)

    def test_phase134_read_repo_text_rejects_missing_source(self) -> None:
        """Provider validation must fail closed when a required source file is absent."""
        source = (ROOT / "Packages" / "dev.unity2foxglove.sdk" / "Tests" / "Runtime" / "Phase134_1Validation.cs").read_text(encoding="utf-8")
        self.assertIn("if (!File.Exists(path))", source)
        self.assertIn("throw new FileNotFoundException", source)

    def test_phase13_try_read_repo_text_fails_closed_on_missing_source(self) -> None:
        """Phase 13 validation must not turn missing source files into silent early returns."""
        source = read_runtime_validation_source(
            ROOT / "Packages" / "dev.unity2foxglove.sdk" / "Tests" / "Runtime" / "Phase13Validation.cs"
        )
        self.assertIn("if (!File.Exists(path))", source)
        self.assertIn("throw new FileNotFoundException", source)

    def test_phase13_recording_fixture_owns_and_cleans_temp_path(self) -> None:
        """Recording validation must retain the temp-path identity through cleanup."""
        source = read_runtime_validation_source(
            ROOT / "Packages" / "dev.unity2foxglove.sdk" / "Tests" / "Runtime" / "Phase13Validation.cs"
        )
        self.assertIn("var recordingPath = Path.GetTempFileName()", source)
        self.assertIn("File.Delete(recordingPath)", source)

    def test_editor_security_uses_canonical_loopback_host_policy(self) -> None:
        """Root-CA exposure warnings must normalize IPv4/IPv6 loopback forms."""
        source = (ROOT / "Packages" / "dev.unity2foxglove.sdk" / "Editor" / "Manager" / "FoxgloveManagerEditor.Security.cs").read_text(encoding="utf-8")
        self.assertIn("FoxgloveManager.IsLoopbackHost(distributorHost)", source)

    def test_editor_path_picker_tolerates_malformed_existing_value(self) -> None:
        """Passive inspector rendering must survive malformed serialized paths."""
        source = (ROOT / "Packages" / "dev.unity2foxglove.sdk" / "Editor" / "Manager" / "FoxgloveManagerEditor.Helpers.cs").read_text(encoding="utf-8")
        self.assertIn("catch (Exception)", source)
        self.assertIn("return GetDefaultDir();", source)

    def test_phase140_queue_stress_bounds_thread_joins(self) -> None:
        """Concurrent queue validation must not hang indefinitely on a stalled worker."""
        source = (ROOT / "Packages" / "dev.unity2foxglove.sdk" / "Tests" / "Runtime" / "Phase140_7Validation.cs").read_text(encoding="utf-8")
        self.assertIn("producer.Join(TimeSpan.FromSeconds", source)
        self.assertIn("consumer.Join(TimeSpan.FromSeconds", source)

    def test_phase162_bounded_runner_does_not_turn_timeout_into_success(self) -> None:
        """A timed-out ROS2 helper must return a nonzero status even if the child lingers."""
        facade = ROOT / "Scripts" / "smoke" / "ros2" / "phase162_lyrical_zenoh_player_smoke.py"
        package = facade.with_suffix("")
        sources = [facade]
        if package.is_dir():
            sources.extend(sorted(package.glob("*.py")))
        source = "\n".join(path.read_text(encoding="utf-8") for path in sources)
        self.assertIn("if process.poll() is None", source)
        self.assertIn("returncode = 124", source)

    def test_phase184_acceptance_regressions_have_a_truthful_dedicated_lane(self) -> None:
        """The selector must execute exactly six pure unittest suites in locked order."""
        with mock.patch.object(self.run_ci, "run", return_value=True) as run:
            with mock.patch.object(
                sys,
                "argv",
                ["run_ci.py", "--only", "phase184-acceptance-tooling"],
            ):
                self.assertEqual(0, self.run_ci.main())

        observed = [(call.args[0], call.args[1]) for call in run.call_args_list]
        self.assertEqual(
            [
                ([sys.executable, "-m", "unittest", module], label)
                for _name, module, label in self.PHASE184_ACCEPTANCE_TOOLING_SUITES
            ],
            observed,
        )
        self.assertTrue(
            all(
                call.kwargs.get("disable_timeout", False) is False
                for call in run.call_args_list
            )
        )

        phase184_modules = [
            module
            for _name, module, _label in self.PHASE184_ACCEPTANCE_TOOLING_SUITES
        ]
        self.assertTrue(
            all(
                command[:3] == [sys.executable, "-m", "unittest"]
                and len(command) == 4
                for command, _label in observed
            )
        )
        self.assertTrue(
            all(
                module.startswith("Scripts.smoke.foxrun.regression_checks.test_")
                for module in phase184_modules
            )
        )
        self.assertTrue(
            {
                "Scripts.smoke.foxrun.phase184_profile_acceptance",
                "Scripts.smoke.foxrun.phase184_foxglove_cli_install",
                "Scripts.smoke.foxrun.phase184_foxglove_desktop_live_acceptance",
            }.isdisjoint(phase184_modules)
        )

        with mock.patch.object(self.run_ci, "run", return_value=True) as phase181_run:
            with mock.patch.object(
                sys,
                "argv",
                ["run_ci.py", "--only", "phase181-ros2-regression"],
            ):
                self.assertEqual(0, self.run_ci.main())

        phase181_commands = {
            tuple(call.args[0])
            for call in phase181_run.call_args_list
        }
        self.assertTrue(
            all(
                (sys.executable, "-m", "unittest", module)
                not in phase181_commands
                for module in phase184_modules
            )
        )

    def test_phase184_acceptance_tooling_lane_propagates_failure(self) -> None:
        """A failing Phase184 tooling suite must fail its dedicated selector."""

        with mock.patch.object(
            self.run_ci,
            "run",
            side_effect=(False, True, True, True, True, True),
        ):
            with mock.patch.object(
                sys,
                "argv",
                ["run_ci.py", "--only", "phase184-acceptance-tooling"],
            ):
                self.assertEqual(1, self.run_ci.main())

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

__all__ = ["_RunCiTestsPart2"]
