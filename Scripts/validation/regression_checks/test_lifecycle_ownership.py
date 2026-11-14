"""Regression tests for the Phase197 lifecycle audit evidence boundaries."""

from __future__ import annotations

import tempfile
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path
from unittest import mock

from Scripts.validation.plan_audit import lifecycle_ownership


class LifecycleOwnershipAuditTests(unittest.TestCase):
    """Cover lifecycle audit parsing and evidence decisions."""

    def test_plan_bases_require_current_named_fields(self) -> None:
        """Only explicit Audit base and Entry base fields are accepted."""
        text = (
            "Entry base: `origin/main == " + "a" * 40 + "`\n"
            "Historical entry: `origin/main == " + "b" * 40 + "`\n"
            "Audit base: `origin/main == " + "a" * 40 + "`\n"
        )
        self.assertEqual(
            {"Audit base": "a" * 40, "Entry base": "a" * 40},
            lifecycle_ownership._plan_bases(text),
        )
        self.assertEqual({}, lifecycle_ownership._plan_bases("Entry base: old\n"))

    def test_owner_evidence_requires_fields_writers_and_lifetime(self) -> None:
        """Owner evidence rejects file-only and field-only payloads."""
        incomplete = {"files": ["Owner.cs"], "fields": {}}
        self.assertFalse(lifecycle_ownership._owner_evidence_valid(incomplete))
        complete = {
            "files": ["Owner.cs"],
            "fields": {"_state": {"assignments": 1}},
            "locks": ["Owner.cs:10"],
            "lifetime": ["Owner.cs:20"],
        }
        self.assertTrue(lifecycle_ownership._owner_evidence_valid(complete))

    def test_trx_coverage_requires_every_expected_test(self) -> None:
        """A single passing test cannot satisfy the lifecycle coverage gate."""
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / "results.trx"
            root = ET.Element("TestRun")
            results = ET.SubElement(root, "Results")
            for name in lifecycle_ownership.EXPECTED_TEST_NAMES:
                ET.SubElement(results, "UnitTestResult", testName=name, outcome="Passed")
            ET.ElementTree(root).write(path, encoding="utf-8", xml_declaration=True)
            self.assertEqual([], lifecycle_ownership._missing_expected_tests(path))
            results.remove(results[-1])
            ET.ElementTree(root).write(path, encoding="utf-8", xml_declaration=True)
            self.assertTrue(lifecycle_ownership._missing_expected_tests(path))

    def test_mutation_evidence_requires_expected_test_failure(self) -> None:
        """Mutation evidence rejects compile-only or unrelated failures."""
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / "mutation.trx"
            root = ET.Element("TestRun")
            results = ET.SubElement(root, "Results")
            ET.SubElement(results, "UnitTestResult", testName="Unrelated", outcome="Failed")
            ET.ElementTree(root).write(path, encoding="utf-8", xml_declaration=True)
            self.assertFalse(lifecycle_ownership._expected_test_failure(path))
            results.clear()
            ET.SubElement(
                results,
                "UnitTestResult",
                testName=lifecycle_ownership.EXPECTED_TEST_NAMES[0],
                outcome="Failed",
            )
            ET.ElementTree(root).write(path, encoding="utf-8", xml_declaration=True)
            self.assertTrue(lifecycle_ownership._expected_test_failure(path))

    def test_candidate_identity_requires_reviewable_metadata(self) -> None:
        """Candidate scope rejects unknown or metadata-free rows."""
        candidates = [{"pr": "unknown", "title": "", "merge_sha": "", "surface": "unknown"}]
        self.assertTrue(lifecycle_ownership._candidate_metadata_issues(candidates))
        candidates = [{"pr": "731", "title": "fix lifecycle", "merge_sha": "a" * 40, "surface": "Packages"}]
        self.assertEqual([], lifecycle_ownership._candidate_metadata_issues(candidates))

    def test_gate_uses_reviewed_appendix_labels_for_threshold(self) -> None:
        """Machine suggestions cannot override the reviewed Appendix labels."""
        with tempfile.TemporaryDirectory() as temp:
            plan = Path(temp) / "Plan197.md"
            plan.write_text(
                "Status: W1 audit complete\n"
                "| #731 | fix lifecycle | Packages | local | cross-lifecycle |\n",
                encoding="utf-8",
            )
            candidates = [
                {
                    "pr": "731",
                    "title": "fix lifecycle",
                    "merge_sha": "a" * 40,
                    "surface": "Packages",
                    "suggested_label": "local",
                },
            ]
            with mock.patch.object(lifecycle_ownership, "PLAN", plan):
                check = lifecycle_ownership._gate_check(candidates)
        self.assertEqual("FAIL", check.status)
        self.assertIn("threshold_reached=True", check.detail)


if __name__ == "__main__":
    unittest.main()
