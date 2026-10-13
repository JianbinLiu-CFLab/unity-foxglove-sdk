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

    def test_measured_lane_result_rejects_stale_head(self) -> None:
        """A lane result from another checkout cannot be reused as PASS."""
        with tempfile.TemporaryDirectory() as temp:
            lane_runs = Path(temp)
            (lane_runs / "stale.meta").write_text(
                "HEAD_SHA=" + "b" * 40 + "\nEXIT_STATUS=0\n",
                encoding="utf-8",
            )
            (lane_runs / "stale.stdout.log").write_text(
                "All CI checks passed.\n",
                encoding="utf-8",
            )
            status, measurement = task0_entry.measured_lane_result(
                lane_runs,
                "stale",
                "a" * 40,
            )
            self.assertEqual(status, "STALE")
            self.assertIn("expected_head=" + "a" * 40, measurement)

    def test_measured_lane_result_rejects_stale_worktree(self) -> None:
        """A lane result from another working tree cannot be reused as PASS."""
        with tempfile.TemporaryDirectory() as temp:
            lane_runs = Path(temp)
            (lane_runs / "stale.meta").write_text(
                "HEAD_SHA=" + "a" * 40 + "\nWORKTREE_SHA=" + "b" * 64 + "\nEXIT_STATUS=0\n",
                encoding="utf-8",
            )
            (lane_runs / "stale.stdout.log").write_text(
                "All CI checks passed.\n",
                encoding="utf-8",
            )
            status, measurement = task0_entry.measured_lane_result(
                lane_runs,
                "stale",
                "a" * 40,
                "c" * 64,
            )
            self.assertEqual(status, "STALE")
            self.assertIn("expected_worktree=" + "c" * 64, measurement)

    def test_measured_lane_result_requires_bound_evidence(self) -> None:
        """Bound lane evidence must contain both checkout identity fields."""
        with tempfile.TemporaryDirectory() as temp:
            lane_runs = Path(temp)
            (lane_runs / "unbound.meta").write_text(
                "EXIT_STATUS=0\n",
                encoding="utf-8",
            )
            (lane_runs / "unbound.stdout.log").write_text(
                "All CI checks passed.\n",
                encoding="utf-8",
            )
            status, measurement = task0_entry.measured_lane_result(
                lane_runs,
                "unbound",
                "a" * 40,
                "b" * 64,
            )
            self.assertEqual(status, "STALE")
            self.assertEqual(measurement, "head_sha=missing")

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

    def test_missing_generated_source_is_explicitly_classified(self) -> None:
        """Allow-dirty classification accepts a tracked but missing generated source."""
        with tempfile.TemporaryDirectory() as temp:
            output = Path(temp) / "baseline"
            freeze_baseline.write_tsv(
                output / "generated-manifest.tsv",
                ["path", "generator_command", "owner", "sha256", "generated_excluded"],
                [],
            )
            freeze_baseline.write_tsv(
                output / "vendor-manifest.tsv",
                ["path", "owner", "source_ref", "sha256", "vendor_excluded"],
                [],
            )
            freeze_baseline.validate_classification(
                output,
                ["Generated.g.cs"],
                frozenset({".cs"}),
                lambda path: path == "Generated.g.cs",
                lambda path: False,
                {"Generated.g.cs"},
            )

    def test_lane_status_counts_preserve_independent_failures(self) -> None:
        """Two failing or timed-out lanes remain two failures or timeouts."""
        counts = freeze_baseline.lane_status_counts(
            ["FAIL", "FAIL", "TIMEOUT", "TIMEOUT", "RUNNING", "STALE", "NOT_RUN"]
        )
        self.assertEqual(counts["baseline_lane_failures"], 2)
        self.assertEqual(counts["baseline_lane_timeouts"], 2)
        self.assertEqual(counts["baseline_lane_running"], 1)
        self.assertEqual(counts["baseline_lane_stale"], 1)
        self.assertEqual(counts["baseline_lane_not_run"], 1)

    def test_parse_status_preserves_non_utf8_filename_bytes(self) -> None:
        """Git NUL-delimited paths retain undecodable bytes through surrogateescape."""
        payload = b"M  bad-\xff.cs\0"
        completed = subprocess.CompletedProcess(["git"], 0, stdout=payload, stderr=b"")
        with mock.patch.object(freeze_baseline, "_git", return_value=completed):
            records = freeze_baseline.parse_status(Path("."))
        self.assertEqual(records[0][0], "M ")
        self.assertIn("\udcff", records[0][1])

    def test_tracked_paths_preserves_non_utf8_filename_bytes(self) -> None:
        """Tracked path bytes retain undecodable filename bytes through surrogateescape."""
        completed = subprocess.CompletedProcess(["git"], 0, stdout=b"bad-\xff.cs\0", stderr=b"")
        with mock.patch.object(freeze_baseline, "_git", return_value=completed):
            paths = freeze_baseline.tracked_paths(Path("."))
        self.assertIn("\udcff", paths[0])

    def test_write_tsv_preserves_non_utf8_filename_bytes(self) -> None:
        """TSV output retains undecodable filename bytes through surrogateescape."""
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / "paths.tsv"
            freeze_baseline.write_tsv(path, ["path"], [["bad-\udcff.cs"]])
            self.assertIn(b"bad-\xff.cs", path.read_bytes())

    def test_incomplete_baseline_requires_explicit_opt_in(self) -> None:
        """Incomplete evidence fails unless the caller explicitly allows it."""
        self.assertEqual(freeze_baseline.baseline_exit_code(True, False, False), 2)
        self.assertEqual(freeze_baseline.baseline_exit_code(True, False, True), 0)
        self.assertEqual(freeze_baseline.baseline_exit_code(False, False, False), 0)

    def test_lane_manifest_binds_ci_evidence_to_current_checkout(self) -> None:
        """The lane manifest passes both checkout identities to the evidence reader."""
        with tempfile.TemporaryDirectory() as temp:
            with mock.patch.object(
                freeze_baseline,
                "measured_lane_result",
                return_value=("PASS", "bound"),
            ) as measured:
                rows = freeze_baseline.measured_run_ci_lanes(
                    Path(temp),
                    ["dotnet"],
                    "a" * 40,
                    "b" * 64,
                )
            self.assertEqual(rows, [("dotnet", "PASS", "bound")])
            measured.assert_called_once_with(Path(temp), "dotnet", "a" * 40, "b" * 64)

    def test_main_classification_passes_missing_source_manifest(self) -> None:
        """The main classification path includes missing tracked sources."""
        with tempfile.TemporaryDirectory() as temp:
            output = Path(temp)
            freeze_baseline.write_tsv(
                output / "missing-tracked-sources.tsv",
                ["path", "generated_excluded", "vendor_excluded", "reason"],
                [["Generated.g.cs", "true", "false", "missing"]],
            )
            with mock.patch.object(freeze_baseline, "validate_classification") as validate:
                freeze_baseline.validate_inventory_classification(output, ["Generated.g.cs"])
            self.assertEqual(validate.call_args.args[-1], {"Generated.g.cs"})

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
