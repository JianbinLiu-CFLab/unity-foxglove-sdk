from .foundation import *


UNITY_TEST_RUNNER_PATH = ROOT / "Scripts" / "release" / "run_unity_tests.py"

class UnityBatchCompileGateTests(unittest.TestCase):
    """Regression coverage for the non-interactive Unity gate wrapper."""

    def setUp(self) -> None:
        """Load the Unity gate module from the checkout under test."""
        self.gate = load_module("unity_batch_compile_under_test", UNITY_BATCH_COMPILE_PATH)

    def test_compile_verdict_rejects_compiler_errors_and_warnings(self) -> None:
        """Reject a batch log containing compiler errors or warnings."""
        verdict, errors, warnings = self.gate.compile_verdict(
            1,
            "Assets/Foo.cs(4,2): error CS0103: missing\n"
            "Assets/Bar.cs(8,1): warning CS0219: unused\n",
        )
        self.assertEqual("FAIL", verdict)
        self.assertEqual(1, len(errors))
        self.assertEqual(1, len(warnings))

    def test_default_timeout_allows_cold_unity_import(self) -> None:
        """Allow a cold self-hosted workspace enough time for Unity package import."""
        self.assertEqual(60 * 60, self.gate.DEFAULT_TIMEOUT_SECONDS)

    def test_compile_verdict_marks_invalid_package_environment_not_run(self) -> None:
        """Classify Unity package-manager environment failures as not run."""
        verdict, diagnostics, _ = self.gate.compile_verdict(
            1,
            '[Package Manager] The "path" argument must be of type string\n',
        )
        self.assertEqual("NOT_RUN", verdict)
        self.assertEqual(
            ['[Package Manager] The "path" argument must be of type string'],
            diagnostics,
        )

    def test_run_preserves_compiler_diagnostics_when_tree_is_not_quiet(self) -> None:
        """Keep compiler errors and observed residual PIDs when cleanup also fails."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            project = root / "Unity2Foxglove"
            project.mkdir()
            unity = root / "Unity.exe"
            unity.write_text("stub", encoding="utf-8")
            log_path = root / "unity.log"
            process = mock.Mock()

            def finish_process(*_args, **_kwargs):
                """Write the compiler failure before the controlled process exits."""
                log_path.write_text(
                    "Assets/Foo.cs(4,2): error CS0103: missing\n",
                    encoding="utf-8",
                )
                return 3

            process.wait.side_effect = finish_process
            tree = mock.Mock(process=process)
            tree.terminate.return_value = []
            environment = {name: "present" for name in self.gate.REQUIRED_ENVIRONMENT}
            with mock.patch.dict(os.environ, environment, clear=False):
                with mock.patch.object(self.gate, "_active_unity_project", return_value=False):
                    with mock.patch(
                        "Scripts.unity_build.unity_il2cpp.start_owned_process",
                        return_value=tree,
                    ):
                        with mock.patch(
                            "Scripts.unity_build.unity_il2cpp.await_tree_quiescence",
                            return_value=[424242],
                        ):
                            with mock.patch("sys.stdout", new_callable=io.StringIO) as output:
                                result = self.gate.run(unity, project, log_path)

            payload = json.loads(output.getvalue())
        self.assertEqual(1, result)
        self.assertEqual("FAIL", payload["verdict"])
        self.assertEqual(3, payload["exit_code"])
        self.assertTrue(any("error CS0103" in line for line in payload["errors"]))
        self.assertIn("Unity process tree did not quiesce after exit", payload["errors"])
        self.assertEqual([424242], payload["residual_pids"])

    def test_run_refuses_incomplete_environment_before_launch(self) -> None:
        """Refuse to launch Unity when required user environment is absent."""
        environment = {name: os.environ.pop(name, None) for name in self.gate.REQUIRED_ENVIRONMENT}
        try:
            with tempfile.TemporaryDirectory() as temp:
                log_path = Path(temp) / "missing.log"
                log_path.write_text("stale compiler output", encoding="utf-8")
                with mock.patch("sys.stdout", new_callable=io.StringIO) as output:
                    result = self.gate.run(Path("missing-unity"), Path("missing-project"), log_path)
                self.assertEqual("", log_path.read_text(encoding="utf-8"))
        finally:
            for name, value in environment.items():
                if value is not None:
                    os.environ[name] = value
        self.assertEqual(7, result)
        result_payload = json.loads(output.getvalue())
        self.assertEqual("NOT_RUN", result_payload["verdict"])
        self.assertEqual(7, result_payload["exit_code"])

    def test_main_passes_timeout_to_batch_runner(self) -> None:
        """Forward the command-line timeout to the batch runner."""
        with mock.patch.object(self.gate, "run", return_value=0) as run:
            with mock.patch.object(
                sys,
                "argv",
                [
                    "run_unity_batch_compile.py",
                    "--unity",
                    "unity.exe",
                    "--project-path",
                    "project",
                    "--log-file",
                    "unity.log",
                    "--timeout-seconds",
                    "17",
                ],
            ):
                self.assertEqual(0, self.gate.main())
        run.assert_called_once_with(
            Path("unity.exe"),
            Path("project"),
            Path("unity.log"),
            17,
        )

    def test_script_entrypoint_resolves_repository_imports(self) -> None:
        """Running the gate as a file must reach its JSON verdict before launch."""
        with tempfile.TemporaryDirectory(dir=ROOT) as temp:
            root = Path(temp)
            project = root / "Unity2Foxglove"
            project.mkdir()
            log_path = root / "build" / "unity.log"
            environment = os.environ.copy()
            environment["PYTHONPATH"] = ""
            environment.update({name: "present" for name in self.gate.REQUIRED_ENVIRONMENT})
            completed = subprocess.run(
                [
                    sys.executable,
                    "-B",
                    str(UNITY_BATCH_COMPILE_PATH),
                    "--unity",
                    sys.executable,
                    "--project-path",
                    str(project),
                    "--log-file",
                    str(log_path),
                    "--timeout-seconds",
                    "2",
                ],
                cwd=ROOT,
                env=environment,
                capture_output=True,
                text=True,
                check=False,
            )
            self.assertNotIn("ModuleNotFoundError", completed.stderr)
            self.assertIn('"verdict":', completed.stdout)
            self.assertTrue(log_path.with_suffix(".json").is_file())

    def test_run_resolves_relative_unity_and_project_paths_before_launch(self) -> None:
        """The Unity gate must compare and launch using absolute paths."""
        with tempfile.TemporaryDirectory(dir=ROOT) as temp:
            root = Path(temp)
            project = root / "Unity2Foxglove"
            project.mkdir()
            unity = root / "Unity.exe"
            unity.write_text("", encoding="utf-8")
            log_path = root / "build" / "unity.log"
            process = mock.Mock(returncode=0)
            process.wait.side_effect = lambda timeout=None: (
                log_path.parent.mkdir(parents=True, exist_ok=True),
                log_path.write_text("Unity compile completed\n", encoding="utf-8"),
                0,
            )[-1]
            tree = mock.Mock(process=process)
            tree.terminate.return_value = []
            with mock.patch.dict(
                os.environ,
                {name: "present" for name in self.gate.REQUIRED_ENVIRONMENT},
                clear=False,
            ):
                with mock.patch.object(self.gate, "_active_unity_project", return_value=False):
                    with mock.patch(
                        "Scripts.unity_build.unity_il2cpp.start_owned_process",
                        return_value=tree,
                    ) as start:
                        with mock.patch(
                            "Scripts.unity_build.unity_il2cpp.await_tree_quiescence",
                            return_value=[],
                        ):
                            result = self.gate.run(
                                Path(os.path.relpath(unity)),
                                Path(os.path.relpath(project)),
                                log_path,
                                timeout_seconds=1,
                            )
            self.assertEqual(0, result)
            command = start.call_args.args[0]
            self.assertEqual(str(unity.resolve()), command[0])
            self.assertEqual(str(project.resolve()), command[command.index("-projectPath") + 1])

UnityBatchCompileGateTests.__module__ = 'Scripts.release.regression_checks.test_release_tooling'


class UnityTestRunnerTests(unittest.TestCase):
    """Regression coverage for the Unity Test Runner wrapper."""

    def setUp(self) -> None:
        """Load the Unity Test Runner module from the checkout under test."""
        self.runner = load_module("unity_test_runner_under_test", UNITY_TEST_RUNNER_PATH)

    def test_parse_nunit_results_passes_and_counts(self) -> None:
        """Parse a passing NUnit result with stable summary counts."""
        with tempfile.TemporaryDirectory() as temp:
            results = Path(temp) / "results.xml"
            results.write_text(
                '<test-run total="1" passed="1" failed="0" inconclusive="0" skipped="0">'
                '<test-suite total="1" passed="1" failed="0" inconclusive="0" skipped="0" />'
                '</test-run>',
                encoding="utf-8",
            )
            verdict, failed, counts = self.runner.parse_nunit_results(results)
        self.assertEqual("PASS", verdict)
        self.assertEqual([], failed)
        self.assertEqual(1, counts["passed"])

    def test_parse_nunit_results_reports_failed_test_name(self) -> None:
        """Expose the failing NUnit test name in the verdict."""
        with tempfile.TemporaryDirectory() as temp:
            results = Path(temp) / "results.xml"
            results.write_text(
                '<test-run total="1" passed="0" failed="1" inconclusive="0" skipped="0">'
                '<test-case fullname="Smoke.Fails" result="Failed" />'
                '</test-run>',
                encoding="utf-8",
            )
            verdict, failed, _ = self.runner.parse_nunit_results(results)
        self.assertEqual("FAIL", verdict)
        self.assertEqual(["Smoke.Fails"], failed)

    def test_parse_nunit_results_rejects_empty_run(self) -> None:
        """Reject a Unity run that discovered no tests."""
        with tempfile.TemporaryDirectory() as temp:
            results = Path(temp) / "results.xml"
            results.write_text(
                '<test-run total="0" passed="0" failed="0" inconclusive="0" skipped="0" />',
                encoding="utf-8",
            )
            verdict, failed, counts = self.runner.parse_nunit_results(results)
        self.assertEqual("FAIL", verdict)
        self.assertEqual([], failed)
        self.assertEqual(0, counts["total"])

    def test_parse_nunit_results_rejects_all_skipped(self) -> None:
        """Reject a Unity run where every discovered test was skipped."""
        with tempfile.TemporaryDirectory() as temp:
            results = Path(temp) / "results.xml"
            results.write_text('<test-run total="3" passed="0" failed="0" inconclusive="0" skipped="3" />', encoding="utf-8")
            verdict, failed, counts = self.runner.parse_nunit_results(results)
        self.assertEqual("FAIL", verdict)
        self.assertEqual([], failed)
        self.assertEqual(3, counts["skipped"])

    def test_parse_nunit_results_rejects_all_inconclusive(self) -> None:
        """Reject a Unity run where every discovered test was inconclusive."""
        with tempfile.TemporaryDirectory() as temp:
            results = Path(temp) / "results.xml"
            results.write_text('<test-run total="2" passed="0" failed="0" inconclusive="2" skipped="0" />', encoding="utf-8")
            verdict, failed, counts = self.runner.parse_nunit_results(results)
        self.assertEqual("FAIL", verdict)
        self.assertEqual([], failed)
        self.assertEqual(2, counts["inconclusive"])

    def test_parse_nunit_results_rejects_mixed_passed_and_skipped(self) -> None:
        """Reject a Unity run that contains skipped tests despite passing cases."""
        with tempfile.TemporaryDirectory() as temp:
            results = Path(temp) / "results.xml"
            results.write_text('<test-run total="3" passed="1" failed="0" inconclusive="0" skipped="2" />', encoding="utf-8")
            verdict, failed, counts = self.runner.parse_nunit_results(results)
        self.assertEqual("FAIL", verdict)
        self.assertEqual([], failed)
        self.assertEqual(1, counts["passed"])

    def test_parse_nunit_results_rejects_malformed_counts(self) -> None:
        """Convert malformed NUnit summary counts into a controlled failure."""
        with tempfile.TemporaryDirectory() as temp:
            results = Path(temp) / "results.xml"
            results.write_text(
                '<test-run total="one" passed="1" failed="0" inconclusive="0" skipped="0" />',
                encoding="utf-8",
            )
            verdict, failed, counts = self.runner.parse_nunit_results(results)
        self.assertEqual("FAIL", verdict)
        self.assertTrue(failed)
        self.assertEqual({}, counts)

    def test_parse_nunit_results_rejects_negative_counts(self) -> None:
        """Reject negative NUnit summary counts."""
        with tempfile.TemporaryDirectory() as temp:
            results = Path(temp) / "results.xml"
            results.write_text(
                '<test-run total="-1" passed="1" failed="0" inconclusive="0" skipped="0" />',
                encoding="utf-8",
            )
            verdict, failed, counts = self.runner.parse_nunit_results(results)
        self.assertEqual("FAIL", verdict)
        self.assertTrue(failed)
        self.assertEqual(-1, counts["total"])

    def test_parse_nunit_results_rejects_inconsistent_counts(self) -> None:
        """Reject NUnit summaries whose component counts do not total the run."""
        with tempfile.TemporaryDirectory() as temp:
            results = Path(temp) / "results.xml"
            results.write_text(
                '<test-run total="1" passed="2" failed="0" inconclusive="0" skipped="0" />',
                encoding="utf-8",
            )
            verdict, failed, counts = self.runner.parse_nunit_results(results)
        self.assertEqual("FAIL", verdict)
        self.assertTrue(failed)
        self.assertEqual(2, counts["passed"])

    def test_parse_nunit_results_rejects_missing_summary(self) -> None:
        """Reject malformed NUnit XML without a summary element."""
        with tempfile.TemporaryDirectory() as temp:
            results = Path(temp) / "results.xml"
            results.write_text("<test-suite />", encoding="utf-8")
            verdict, failed, counts = self.runner.parse_nunit_results(results)
        self.assertEqual("FAIL", verdict)
        self.assertTrue(failed)
        self.assertEqual({}, counts)

    def test_run_marks_missing_environment_not_run(self) -> None:
        """Classify missing Unity environment variables as NOT_RUN."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            environment = {name: os.environ.pop(name, None) for name in self.runner.REQUIRED_ENVIRONMENT}
            try:
                result = self.runner.run(
                    root / "Unity.exe",
                    root / "Project",
                    "EditMode",
                    root / "results.xml",
                    root / "unity.log",
                )
            finally:
                for name, value in environment.items():
                    if value is not None:
                        os.environ[name] = value
            payload = json.loads((root / "results.json").read_text(encoding="utf-8"))
        self.assertEqual(7, result)
        self.assertEqual("NOT_RUN", payload["verdict"])

    def test_run_marks_timeout_and_failed_test(self) -> None:
        """Classify a timed-out process and preserve the failed test name."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            project = root / "Project"
            project.mkdir()
            unity = root / "Unity.exe"
            unity.write_text("stub", encoding="utf-8")
            results = root / "results.xml"
            process = mock.Mock()

            def timeout_with_results(*_args, **_kwargs):
                """Leave a failed NUnit result before the controlled timeout."""
                results.write_text(
                    '<test-run total="1" passed="0" failed="1" inconclusive="0" skipped="0">'
                    '<test-case fullname="Smoke.Fails" result="Failed" />'
                    '</test-run>',
                    encoding="utf-8",
                )
                raise subprocess.TimeoutExpired(["unity"], 1)

            process.wait.side_effect = timeout_with_results
            tree = mock.Mock(process=process)
            tree.terminate.return_value = []
            environment = {name: "present" for name in self.runner.REQUIRED_ENVIRONMENT}
            with mock.patch.dict(os.environ, environment, clear=False):
                with mock.patch.object(self.runner, "_active_unity_project", return_value=False):
                    with mock.patch(
                        "Scripts.unity_build.unity_il2cpp.start_owned_process",
                        return_value=tree,
                    ):
                        with mock.patch(
                            "Scripts.unity_build.unity_il2cpp.await_tree_quiescence",
                            return_value=[],
                        ):
                            result = self.runner.run(
                                unity,
                                project,
                                "PlayMode",
                                results,
                                root / "unity.log",
                                timeout_seconds=1,
                            )
            payload = json.loads(results.with_suffix(".json").read_text(encoding="utf-8"))
        self.assertEqual(1, result)
        self.assertEqual("FAIL", payload["verdict"])
        self.assertIn("Smoke.Fails", payload["failed_tests"])
        self.assertTrue(payload["timed_out"])

    def test_main_forwards_platform_and_paths(self) -> None:
        """Forward command-line test platform and output paths to run."""
        with mock.patch.object(self.runner, "run", return_value=0) as run:
            self.assertEqual(
                0,
                self.runner.main(
                    [
                        "--test-platform",
                        "EditMode",
                        "--test-results",
                        "results.xml",
                        "--log-file",
                        "unity.log",
                        "--nographics",
                    ]
                ),
            )
        self.assertEqual("EditMode", run.call_args.args[2])
        self.assertTrue(run.call_args.args[6])
        self.assertIsNone(run.call_args.args[7])

    def test_main_forwards_test_category(self) -> None:
        """Forward a Unity test category filter to the runner."""
        with mock.patch.object(self.runner, "run", return_value=0) as run:
            self.assertEqual(
                0,
                self.runner.main(
                    [
                        "--test-platform",
                        "PlayMode",
                        "--test-results",
                        "results.xml",
                        "--log-file",
                        "unity.log",
                        "--test-category",
                        "GraphicsAcceptance",
                    ]
                ),
            )
        self.assertEqual("GraphicsAcceptance", run.call_args.args[7])


UnityTestRunnerTests.__module__ = "Scripts.release.regression_checks.test_release_tooling"

__all__ = ["UnityBatchCompileGateTests", "UnityTestRunnerTests"]
