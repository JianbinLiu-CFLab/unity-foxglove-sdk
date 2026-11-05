from .foundation import *


UNITY_TEST_RUNNER_PATH = ROOT / "Scripts" / "release" / "run_unity_tests.py"


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
        self.assertTrue(run.call_args.args[-1])


UnityTestRunnerTests.__module__ = "Scripts.release.regression_checks.test_release_tooling"

__all__ = ["UnityTestRunnerTests"]
