from .foundation import *

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
        timeout_calls = []
        source_paths = [MCAP_CONFORMANCE_PATH]
        package = MCAP_CONFORMANCE_PATH.with_suffix("")
        source_paths.extend(sorted(package.glob("*.py")))
        for source_path in source_paths:
            tree = ast.parse(source_path.read_text(encoding="utf-8"))
            for node in ast.walk(tree):
                if not isinstance(node, ast.Call):
                    continue
                if not any(keyword.arg == "timeout_seconds" for keyword in node.keywords):
                    continue
                function_name = node.func.id if isinstance(node.func, ast.Name) else ""
                if function_name in {"invoke_command_capture", "run_package_manager"}:
                    timeout_calls.append((source_path.name, node.lineno))

        self.assertEqual([], timeout_calls)

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
