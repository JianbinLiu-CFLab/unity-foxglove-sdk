"""Behavioral controls for the Phase190 subprocess and evidence gate."""
from __future__ import annotations

import contextlib
import io
import tempfile
import unittest
from pathlib import Path
from unittest import mock

from Scripts.phase190 import run_conformance


class RunConformanceTests(unittest.TestCase):
    """An inventory digest must never stand in for executed conformance tests."""

    def test_trx_requires_exact_nonempty_executed_set(self) -> None:
        """Positive, missing, duplicate, skipped and malformed results stay distinct."""
        with tempfile.TemporaryDirectory() as directory:
            result = Path(directory) / "results.trx"
            for xml, expected in [
                ('<TestRun><Results><UnitTestResult testName="Demo.A" outcome="Passed" /></Results></TestRun>', True),
                ('<TestRun><Results><UnitTestResult testName="Demo.A" outcome="NotExecuted" /></Results></TestRun>', False),
                ('<TestRun><Results><UnitTestResult testName="Demo.B" outcome="Passed" /></Results></TestRun>', False),
                ('<TestRun><Results><UnitTestResult testName="Demo.A" outcome="Failed" /></Results></TestRun>', False),
                ('<TestRun><Results><UnitTestResult testName="Demo.A" outcome="Passed" /><UnitTestResult testName="Demo.A" outcome="Passed" /></Results></TestRun>', False),
                ('<TestRun><Results /></TestRun>', False),
                ('<malformed', False),
            ]:
                with self.subTest(xml=xml):
                    result.write_text(xml, encoding="utf-8")
                    self.assertEqual(expected, run_conformance.results_pass(result, {"A"}))
            self.assertFalse(run_conformance.results_pass(result, set()))
            self.assertFalse(run_conformance.results_pass(Path(directory) / "absent.trx", {"A"}))

    def test_nonzero_test_runner_is_propagated_and_never_prints_pass(self) -> None:
        """A real runner failure controls the CLI result even for valid inventory."""
        with tempfile.TemporaryDirectory() as directory:
            inventory = Path(directory) / "inventory.tsv"
            inventory.write_text("claim_id\tinvariant\nFR-DECL-001\tdefaults\n", encoding="utf-8")
            output = io.StringIO()
            with mock.patch("subprocess.run", return_value=mock.Mock(returncode=7)) as runner:
                with contextlib.redirect_stdout(output):
                    code = run_conformance.main(["--inventory", str(inventory)])
            self.assertTrue(runner.called, "The command returned without executing any test")
            self.assertEqual(7, code)
            self.assertNotIn("CONFORMANCE_SMOKE_PASS", output.getvalue())

    def test_zero_exit_without_test_results_is_not_conformance(self) -> None:
        """A missing TRX or zero-test invocation is a failure, not a green gate."""
        with tempfile.TemporaryDirectory() as directory:
            inventory = Path(directory) / "inventory.tsv"
            inventory.write_text("claim_id\tinvariant\nFR-DECL-001\tdefaults\n", encoding="utf-8")
            with mock.patch("subprocess.run", return_value=mock.Mock(returncode=0)):
                self.assertNotEqual(0, run_conformance.main(["--inventory", str(inventory)]))


if __name__ == "__main__":
    unittest.main()
