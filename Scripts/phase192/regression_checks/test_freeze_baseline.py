from __future__ import annotations

import unittest
import tempfile
from pathlib import Path

from Scripts.phase192 import freeze_baseline, task0_entry


class FreezeBaselineTests(unittest.TestCase):
    """Verify Task 0 baseline inventory helpers."""
    def test_measured_lane_result_is_fail_closed(self) -> None:
        """Measured lane results fail closed for missing and failed evidence."""
        with tempfile.TemporaryDirectory() as temp:
            lane_runs = Path(temp)
            self.assertEqual(task0_entry.measured_lane_result(lane_runs, "missing"), ("NOT_RUN", "meta=missing"))
            (lane_runs / "running.meta").write_text("PID=123\n", encoding="utf-8")
            self.assertEqual(task0_entry.measured_lane_result(lane_runs, "running")[0], "RUNNING")
            (lane_runs / "pass.meta").write_text("PID=123\nEXIT_STATUS=0\n", encoding="utf-8")
            (lane_runs / "pass.stdout.log").write_text("All CI checks passed.\n", encoding="utf-8")
            self.assertEqual(task0_entry.measured_lane_result(lane_runs, "pass"), ("PASS", "exit_status=0; marker=true"))
            (lane_runs / "fail.meta").write_text("PID=123\nEXIT_STATUS=1\n", encoding="utf-8")
            self.assertEqual(task0_entry.measured_lane_result(lane_runs, "fail"), ("FAIL", "exit_status=1; marker=false"))

    def test_python_test_paths_excludes_package_initializers(self) -> None:
        """Python test inventory excludes package initializer files."""
        paths = [
            "Scripts/smoke/foxrun/regression_checks/test_feature.py",
            "Scripts/smoke/foxrun/regression_checks/test_feature/__init__.py",
            "Scripts/smoke/foxrun/regression_checks/test_feature/support.py",
        ]
        self.assertEqual(
            freeze_baseline.python_test_paths(paths),
            [
                "Scripts/smoke/foxrun/regression_checks/test_feature.py",
                "Scripts/smoke/foxrun/regression_checks/test_feature/support.py",
            ],
        )


if __name__ == "__main__":
    unittest.main()
