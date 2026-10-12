from __future__ import annotations

import unittest
import tempfile
import subprocess
from pathlib import Path
from unittest import mock

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

    def test_generated_catalogs_are_excluded_from_hand_maintained_inventory(self) -> None:
        """Generated CDR and FoxRun catalog sources stay out of split inventory."""
        self.assertTrue(
            freeze_baseline.is_generated(
                "Packages/dev.unity2foxglove.foxrun.ros2.interfaces.typesupport.humble.win64/Runtime/FoxRun/Generated/FoxRunCustomTypesupportCatalog.g.cs"
            )
        )
        self.assertTrue(
            freeze_baseline.is_generated(
                "Packages/dev.unity2foxglove.ros2bridge/Runtime/Schemas/Ros2Msg/Generated/Ros2CdrGeneratedSerializers.g.cs.meta"
            )
        )

    def test_parse_status_keeps_rename_pair_together(self) -> None:
        """Porcelain NUL records preserve both paths of a rename."""
        payload = b"R  new_name.cs\0old_name.cs\0M  changed.py\0"
        completed = subprocess.CompletedProcess(["git"], 0, stdout=payload, stderr=b"")
        with mock.patch.object(freeze_baseline, "_git", return_value=completed):
            self.assertEqual(
                freeze_baseline.parse_status(Path(".")),
                [("R ", "new_name.cs", "old_name.cs"), ("M ", "changed.py", "")],
            )

    def test_parse_status_keeps_worktree_rename_pair_together(self) -> None:
        """Porcelain NUL records preserve renames in the worktree status column."""
        payload = b" R new_name.cs\0old_name.cs\0"
        completed = subprocess.CompletedProcess(["git"], 0, stdout=payload, stderr=b"")
        with mock.patch.object(freeze_baseline, "_git", return_value=completed):
            self.assertEqual(
                freeze_baseline.parse_status(Path(".")),
                [(" R", "new_name.cs", "old_name.cs")],
            )

    def test_lane_result_reads_unittest_count_from_stderr(self) -> None:
        """Unittest counts are measured from either output stream."""
        self.assertEqual(
            freeze_baseline.lane_result(0, "ok\n", "Ran 17 tests in 0.1s\n"),
            ("PASS", "exit_status=0; tests=17", 17),
        )
        self.assertEqual(
            freeze_baseline.lane_result(0, "inventory complete\n", ""),
            ("PASS", "exit_status=0; tests=not-reported", None),
        )

    def test_source_inventory_reports_missing_tracked_sources(self) -> None:
        """Tracked source paths missing from the worktree are explicit evidence."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            output = root / "baseline"
            counts = freeze_baseline.source_inventory(root, output, ["Missing.cs"])
            self.assertEqual(counts["missing_tracked_sources"], 1)
            self.assertIn("Missing.cs", (output / "missing-tracked-sources.tsv").read_text(encoding="utf-8"))

    def test_plan_hashes_report_missing_paths(self) -> None:
        """Plan hashing exposes missing governance inputs to the caller."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            missing = task0_entry.record_plan_hashes(
                root,
                root / "baseline",
                [Path("Plan/192_missing.md")],
            )
            self.assertEqual(missing, ["Plan/192_missing.md"])

    def test_generated_sources_are_absent_from_split_candidates(self) -> None:
        """Generated files do not inflate the hand-maintained split inventory."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            generated = root / "Generated.g.cs"
            generated.write_text("class Generated {}\n" * 501, encoding="utf-8")
            hand = root / "Hand.cs"
            hand.write_text("class Hand {}\n" * 501, encoding="utf-8")
            output = root / "baseline"
            counts = freeze_baseline.source_inventory(root, output, ["Generated.g.cs", "Hand.cs"])
            self.assertEqual(counts["over500_total"], 1)
            rows = (output / "over-500-lines.tsv").read_text(encoding="utf-8")
            self.assertNotIn("Generated.g.cs", rows)
            self.assertIn("Hand.cs", rows)


if __name__ == "__main__":
    unittest.main()
