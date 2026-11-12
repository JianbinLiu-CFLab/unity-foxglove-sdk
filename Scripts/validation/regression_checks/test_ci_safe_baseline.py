"""Regression tests for CI-safe known-failure baseline governance."""

import unittest

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
                "baseline replacement missing explicit approval for removed entry: --old",
                "baseline replacement missing explicit approval for added entry: --new",
            ],
            baseline_problems("--old\n", "--new\n"),
        )


if __name__ == "__main__":
    unittest.main()
