"""Regression tests for CI-safe known-failure baseline governance."""

import unittest
from pathlib import Path

from Scripts.validation.ci_safe_baseline import baseline_problems, parse_entries


class CiSafeBaselineTests(unittest.TestCase):
    """Verify baseline additions and duplicates are rejected."""

    def test_parse_entries_reports_duplicates_without_comments(self):
        """Ignore comments and retain one duplicate diagnostic per key."""
        entries, duplicates = parse_entries("--a # first\n--b\n--a\n--a\n")
        self.assertEqual(["--a", "--b", "--a", "--a"], entries)
        self.assertEqual(["--a"], duplicates)

    def test_baseline_problems_rejects_net_additions_and_duplicates(self):
        """Reject a new known failure and a duplicated head entry."""
        problems = baseline_problems("--a\n", "--a\n--b\n--b\n")
        self.assertEqual(
            [
                "duplicate baseline entry: --b",
                "baseline entry added without reviewed replacement: --b",
            ],
            problems,
        )

    def test_baseline_problems_allows_removals(self):
        """Allow deleting a known failure after it is fixed."""
        self.assertEqual([], baseline_problems("--a\n--b\n", "--a\n"))

    def test_baseline_problems_allows_reviewed_renames(self):
        """Allow a one-for-one replacement only with an explicit approval marker."""
        self.assertEqual(
            [],
            baseline_problems(
                "--old\n",
                "# reviewed replacement: --old -> --new\n--new\n",
            ),
        )

    def test_baseline_problems_rejects_unmarked_renames(self):
        """Reject a one-for-one replacement without an explicit approval marker."""
        self.assertEqual(
            [
                "baseline replacement requires exactly one approval for added entry: --new",
                "baseline replacement requires one-to-one approval for removed entry: --old",
            ],
            baseline_problems("--old\n", "--new\n"),
        )

    def test_baseline_problems_rejects_unapproved_net_removal_addition(self):
        """Reject an added failure hidden beside removals."""
        self.assertEqual(
            ["baseline replacement requires exactly one approval for added entry: --new"],
            baseline_problems("--old\n--fixed\n", "--new\n"),
        )

    def test_baseline_problems_requires_bijective_replacement_pairs(self):
        """Reject approvals that reuse one old or new entry across multiple pairs."""
        head = "\n".join(
            [
                "# reviewed replacement: --old1 -> --new1",
                "# reviewed replacement: --old2 -> --new1",
                "# reviewed replacement: --old2 -> --new2",
                "--new1",
                "--new2",
            ]
        ) + "\n"
        self.assertEqual(
            [
                "baseline replacement requires exactly one approval for added entry: --new1",
                "baseline replacement requires one-to-one approval for removed entry: --old2",
            ],
            baseline_problems("--old1\n--old2\n", head),
        )

    def test_phase163_health_reload_guard_keeps_cancellation(self):
        """Keep assembly reload cleanup coupled to the health-check cancellation path."""
        root = Path(__file__).resolve().parents[3]
        phase = (root / "Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase163_25Validation.cs").read_text(
            encoding="utf-8"
        )
        drawer = (root / "Packages/dev.unity2foxglove.ros2bridge/Editor/Ros2Bridge/Ros2BridgeHealthDrawer.cs").read_text(
            encoding="utf-8"
        )
        self.assertIn("AssemblyReloadEvents.beforeAssemblyReload += BeforeAssemblyReload", phase)
        self.assertIn("private void BeforeAssemblyReload()", drawer)
        self.assertIn("CancelHealthCheck();", drawer)


if __name__ == "__main__":
    unittest.main()
