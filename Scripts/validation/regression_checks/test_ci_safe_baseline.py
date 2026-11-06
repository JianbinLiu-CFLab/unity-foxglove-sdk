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

    def test_baseline_problems_rejects_additions_and_duplicates(self):
        """Reject a new known failure and a duplicated head entry."""
        problems = baseline_problems("--a\n", "--a\n--b\n--b\n")
        self.assertEqual(
            [
                "duplicate baseline entry: --b",
                "baseline entry added without reviewed base change: --b",
            ],
            problems,
        )

    def test_baseline_problems_allows_removals(self):
        """Allow deleting a known failure after it is fixed."""
        self.assertEqual([], baseline_problems("--a\n--b\n", "--a\n"))


if __name__ == "__main__":
    unittest.main()
