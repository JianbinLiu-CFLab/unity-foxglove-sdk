from .foundation import *


_MCAP_TIMEOUT_HELPERS = frozenset({"invoke_command_capture", "run_package_manager"})
_MCAP_HELPER_RECEIVERS = frozenset({"_configuration", "configuration", "_measurement", "measurement"})

def _is_mcap_timeout_helper(function: ast.expr) -> bool:
    """Return whether a call targets one of the conformance helper APIs."""
    if isinstance(function, ast.Name):
        return function.id in _MCAP_TIMEOUT_HELPERS
    return (
        isinstance(function, ast.Attribute)
        and function.attr in _MCAP_TIMEOUT_HELPERS
        and isinstance(function.value, ast.Name)
        and function.value.id in _MCAP_HELPER_RECEIVERS
    )

def _mcap_timeout_calls(source_paths: list[Path]) -> list[tuple[str, int]]:
    """Return known MCAP helper calls that introduce a fixed timeout."""

    timeout_calls: list[tuple[str, int]] = []
    for source_path in source_paths:
        tree = ast.parse(source_path.read_text(encoding="utf-8"))
        for node in ast.walk(tree):
            if not isinstance(node, ast.Call):
                continue
            if not any(keyword.arg == "timeout_seconds" for keyword in node.keywords):
                continue
            if _is_mcap_timeout_helper(node.func):
                timeout_calls.append((source_path.name, node.lineno))
    return timeout_calls


class McapConformanceToolTests(unittest.TestCase):
    """Regression coverage for the release-blocking official MCAP differential."""

    def setUp(self) -> None:
        """Load a fresh conformance wrapper for each test."""
        self.conformance = load_module("mcap_conformance_under_test", MCAP_CONFORMANCE_PATH)

    def test_conformance_wrapper_uses_named_split_package(self) -> None:
        """The Phase121 wrapper keeps its execution sections in one named package."""
        package = MCAP_CONFORMANCE_PATH.with_suffix("")
        self.assertTrue(package.is_dir())
        self.assertTrue((package / "__main__.py").is_file())

    def test_official_conformance_stages_have_no_hardcoded_deadlines(self) -> None:
        """Official finite stages should run to completion independent of host speed."""
        source_paths = [MCAP_CONFORMANCE_PATH]
        package = MCAP_CONFORMANCE_PATH.with_suffix("")
        source_paths.extend(sorted(package.glob("*.py")))
        self.assertEqual([], _mcap_timeout_calls(source_paths))

    def test_timeout_guard_detects_attribute_calls(self) -> None:
        """The timeout guard must inspect qualified helper calls, not only bare names."""
        with tempfile.TemporaryDirectory() as temporary:
            fixture = Path(temporary) / "fixture.py"
            fixture.write_text(
                "_configuration.invoke_command_capture(['tool'], timeout_seconds=60)\n",
                encoding="utf-8",
            )
            self.assertEqual([("fixture.py", 1)], _mcap_timeout_calls([fixture]))

    def test_timeout_guard_ignores_unrelated_attribute_calls(self) -> None:
        """The timeout guard must not flag unrelated objects with the same method name."""
        with tempfile.TemporaryDirectory() as temporary:
            fixture = Path(temporary) / "fixture.py"
            fixture.write_text(
                "third_party.invoke_command_capture(['tool'], timeout_seconds=60)\n",
                encoding="utf-8",
            )
            self.assertEqual([], _mcap_timeout_calls([fixture]))

    def test_public_configuration_state_tracks_configuration_module(self) -> None:
        """Facade configuration values stay synchronized after runtime updates."""
        from Scripts.mcap.conformance import run_phase121_conformance as facade
        from Scripts.mcap.conformance.run_phase121_conformance import configuration

        chosen = ROOT / "build" / "review-official-root"
        facade.set_official_root(chosen)
        self.assertEqual(chosen.resolve(), facade.OFFICIAL_ROOT)
        self.assertEqual(chosen.resolve(), configuration.OFFICIAL_ROOT)
        report = ROOT / "build" / "review-report.json"
        with mock.patch.object(facade._checkout, "write_ci_smoke_report"):
            self.assertEqual(0, facade.main(["--report-path", str(report), "--ci-smoke"]))
        self.assertEqual(report.resolve(), facade.REPORT_PATH)
        self.assertEqual(report.resolve(), configuration.REPORT_PATH)

    def test_timeout_failure_details_preserve_the_timeout_reason(self) -> None:
        """Large runner output must not truncate away the actionable timeout diagnosis."""
        stdout = "running csharp-streamed-reader\n" + "\n".join(
            f"  testing fixture-{index}.mcap" for index in range(600)
        )
        result = self.conformance.CommandResult(
            -1,
            stdout,
            "Timed out after 180 second(s).",
            "runner command",
            timed_out=True,
        )

        report = self.conformance.measure_runner_output(
            "csharp-streamed-reader",
            "streamed-reader",
            result,
        )

        failure = report["failures"][0]
        self.assertTrue(failure["timedOut"])
        self.assertIn("Timed out after 180 second(s).", failure["details"])

McapConformanceToolTests.__module__ = 'Scripts.release.regression_checks.test_release_tooling'

__all__ = ["McapConformanceToolTests"]
