"""Regression tests for the Phase197 lifecycle audit evidence boundaries."""

from __future__ import annotations

import json
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
            {"Audit base": ["a" * 40], "Entry base": ["a" * 40]},
            lifecycle_ownership._plan_bases(text),
        )
        self.assertEqual({}, lifecycle_ownership._plan_bases("Entry base: old\n"))

        duplicate = (
            "Audit base: `origin/main == " + "a" * 40 + "`\n"
            "Audit base: `origin/main == " + "b" * 40 + "`\n"
            "Entry base: `origin/main == " + "a" * 40 + "`\n"
        )
        self.assertEqual(
            {"Audit base": ["a" * 40, "b" * 40], "Entry base": ["a" * 40]},
            lifecycle_ownership._plan_bases(duplicate),
        )

    def test_owner_evidence_requires_fields_writers_locks_and_lifetime(self) -> None:
        """Owner evidence rejects file-only and field-only payloads."""
        incomplete = {"files": ["Owner.cs"], "fields": {}}
        self.assertFalse(lifecycle_ownership._owner_evidence_valid(incomplete))
        missing_locks = {
            "files": ["Packages/pkg/Runtime/Owner.cs"],
            "fields": {
                "_state": {
                    "assignments": 1,
                    "files": ["Packages/pkg/Runtime/Owner.cs"],
                    "writers": ["Packages/pkg/Runtime/Owner.cs:10"],
                }
            },
            "lifetime": ["Packages/pkg/Runtime/Owner.cs:20"],
        }
        self.assertFalse(lifecycle_ownership._owner_evidence_valid(missing_locks))
        missing_writers = {
            "files": ["Packages/pkg/Runtime/Owner.cs"],
            "fields": {
                "_state": {
                    "assignments": 1,
                    "files": ["Packages/pkg/Runtime/Owner.cs"],
                    "writers": [],
                }
            },
            "locks": ["Packages/pkg/Runtime/Owner.cs:10"],
            "lifetime": ["Packages/pkg/Runtime/Owner.cs:20"],
        }
        self.assertFalse(lifecycle_ownership._owner_evidence_valid(missing_writers))
        malformed_reference = {
            "files": ["Packages/pkg/Runtime/Owner.cs"],
            "fields": {
                "_state": {
                    "assignments": 1,
                    "files": ["Packages/pkg/Runtime/Owner.cs"],
                    "writers": ["Packages/pkg/Runtime/Owner.cs"],
                }
            },
            "locks": ["Packages/pkg/Runtime/Owner.cs:10"],
            "lifetime": ["Packages/pkg/Runtime/Owner.cs:20"],
        }
        self.assertFalse(lifecycle_ownership._owner_evidence_valid(malformed_reference))
        test_only = {
            "files": ["Packages/pkg/Tests/OwnerTests.cs"],
            "fields": {
                "_state": {
                    "assignments": 1,
                    "files": ["Packages/pkg/Tests/OwnerTests.cs"],
                    "writers": ["Packages/pkg/Tests/OwnerTests.cs:10"],
                }
            },
            "locks": ["Packages/pkg/Tests/OwnerTests.cs:10"],
            "lifetime": ["Packages/pkg/Tests/OwnerTests.cs:20"],
        }
        self.assertFalse(lifecycle_ownership._owner_evidence_valid(test_only))
        complete = {
            "files": ["Packages/pkg/Runtime/Owner.cs"],
            "fields": {
                "_state": {
                    "assignments": 1,
                    "files": ["Packages/pkg/Runtime/Owner.cs"],
                    "declarations": ["Packages/pkg/Runtime/Owner.cs:5"],
                    "writers": ["Packages/pkg/Runtime/Owner.cs:10"],
                    "classification": "mutable",
                }
            },
            "missing_writers": [],
            "owner": "FoxgloveSession",
            "locks": ["Packages/pkg/Runtime/Owner.cs:10"],
            "lifetime": ["Packages/pkg/Runtime/Owner.cs:20"],
            "synchronization": {
                "mode": lifecycle_ownership.OWNER_SYNC_MODES["FoxgloveSession"],
                "refs": ["Packages/pkg/Runtime/Owner.cs:10"],
            },
        }
        self.assertTrue(lifecycle_ownership._owner_evidence_valid(complete))

    def test_test_name_normalisation_preserves_method_with_dotted_arguments(self) -> None:
        """Theory arguments cannot change the extracted method name."""
        self.assertEqual(
            "StopServerRunsForActiveOrPendingRuntimeOwnership",
            lifecycle_ownership._normalise_test_name(
                "Tests.FoxgloveManager.StopServerRunsForActiveOrPendingRuntimeOwnership(System.Double.NaN)"
            ),
        )

    def test_production_source_scope_excludes_editor_and_tests(self) -> None:
        """Only Runtime source paths can provide owner evidence."""
        self.assertTrue(lifecycle_ownership._is_production_source("Packages/pkg/Runtime/Owner.cs"))
        self.assertFalse(lifecycle_ownership._is_production_source("Packages/pkg/Editor/OwnerEditor.cs"))
        self.assertFalse(lifecycle_ownership._is_production_source("Packages/pkg/Tests/OwnerTests.cs"))

    def test_plan_check_rejects_duplicate_base_fields(self) -> None:
        """Duplicate current Plan base fields cannot satisfy the audit gate."""
        with tempfile.TemporaryDirectory() as temp:
            plan = Path(temp) / "Plan197.md"
            sha = "a" * 40
            plan.write_text(
                "Status: W1 pending manual review\n"
                f"Audit base: `origin/main == {sha}`\n"
                f"Audit base: `origin/main == {'b' * 40}`\n"
                f"Entry base: `origin/main == {sha}`\n"
                "| #731 | fix lifecycle | Packages | local | tooling |\n",
                encoding="utf-8",
            )
            with mock.patch.object(lifecycle_ownership, "PLAN", plan), mock.patch.object(
                lifecycle_ownership, "_origin_main_sha", return_value=sha
            ):
                check = lifecycle_ownership._plan_check(sha)
        self.assertEqual("FAIL", check.status)

    def test_trx_coverage_requires_every_expected_test(self) -> None:
        """A single passing test cannot satisfy the lifecycle coverage gate."""
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / "results.trx"
            root = ET.Element("TestRun")
            results = ET.SubElement(root, "Results")
            for name in lifecycle_ownership.EXPECTED_TEST_NAMES:
                for case_index in range(lifecycle_ownership.EXPECTED_TEST_COUNTS[name]):
                    test_name = name if case_index == 0 else f"{name}(case-{case_index})"
                    ET.SubElement(results, "UnitTestResult", testName=test_name, outcome="Passed")
            ET.ElementTree(root).write(path, encoding="utf-8", xml_declaration=True)
            self.assertEqual([], lifecycle_ownership._missing_expected_tests(path))
            ET.SubElement(
                results,
                "UnitTestResult",
                testName=lifecycle_ownership.EXPECTED_TEST_NAMES[-1] + "(false, false)",
                outcome="Failed",
            )
            ET.ElementTree(root).write(path, encoding="utf-8", xml_declaration=True)
            self.assertIn(lifecycle_ownership.EXPECTED_TEST_NAMES[-1], lifecycle_ownership._missing_expected_tests(path))
            results.remove(results[-1])
            for _ in range(lifecycle_ownership.EXPECTED_TEST_COUNTS[lifecycle_ownership.EXPECTED_TEST_NAMES[-1]] - 1):
                results.remove(results[-1])
            ET.ElementTree(root).write(path, encoding="utf-8", xml_declaration=True)
            self.assertTrue(lifecycle_ownership._missing_expected_tests(path))

    def test_writer_evidence_excludes_comparisons(self) -> None:
        """Comparison operators do not create writer evidence."""
        text = (
            "if (_state == expected) return;\n"
            "if (_state != expected) return;\n"
            "if (_count <= limit || _count >= minimum) return;\n"
            "_state = next;\n"
        )
        refs = lifecycle_ownership._writer_refs(text, Path("Packages/pkg/Runtime/Owner.cs"), "_state")
        self.assertEqual(["Packages/pkg/Runtime/Owner.cs:4"], refs)

    def test_writer_evidence_requires_known_ref_mutators(self) -> None:
        """Unknown ref helpers do not count as field writes."""
        text = (
            "ReadOnlyOrUnknown(ref _state);\n"
            "TryCleanup(\n"
            "    ref _state,\n"
            "    ref failure);\n"
            "WarningDebouncer.TryUpdateCooldown(\n"
            "    ref _state,\n"
            "    now,\n"
            "    interval);\n"
        )
        refs = lifecycle_ownership._writer_refs(text, Path("Packages/pkg/Runtime/Owner.cs"), "_state")
        self.assertEqual(
            [
                "Packages/pkg/Runtime/Owner.cs:3",
                "Packages/pkg/Runtime/Owner.cs:6",
            ],
            refs,
        )

    def test_writer_evidence_excludes_atomic_reads(self) -> None:
        """Atomic reads do not create writer evidence."""
        text = (
            "Volatile.Read(ref _state);\n"
            "Interlocked.Read(ref _state);\n"
            "Volatile.Write(ref _state, next);\n"
            "Interlocked.Exchange(ref _state, next);\n"
        )
        refs = lifecycle_ownership._writer_refs(text, Path("Packages/pkg/Runtime/Owner.cs"), "_state")
        self.assertEqual(
            [
                "Packages/pkg/Runtime/Owner.cs:3",
                "Packages/pkg/Runtime/Owner.cs:4",
            ],
            refs,
        )

    def test_line_refs_ignore_csharp_comments_and_strings(self) -> None:
        """Lifecycle words in comments and strings do not create evidence."""
        text = (
            "// Dispose StopServer\n"
            'var message = "Dispose StopServer";\n'
            "/* StartServer */\n"
            "Dispose();\n"
            "StopServer();\n"
        )
        refs = lifecycle_ownership._line_refs(
            text,
            Path("Packages/pkg/Runtime/Owner.cs"),
            lifecycle_ownership.LIFETIME_WORDS,
        )
        self.assertEqual(
            [
                "Packages/pkg/Runtime/Owner.cs:4",
                "Packages/pkg/Runtime/Owner.cs:5",
            ],
            refs,
        )

    def test_real_owner_map_uses_declared_production_synchronization(self) -> None:
        """The tracked production owner map must close without test-only evidence."""
        check = lifecycle_ownership._owner_map()
        self.assertEqual("PASS", check.status)
        payload = json.loads((lifecycle_ownership.OUTPUT / "owner-map.json").read_text(encoding="utf-8"))
        manager = payload["FoxgloveManager"]
        self.assertIn("FoxgloveManager::_clientConnectedForwarder", manager["fields"])
        self.assertEqual([], manager["missing_writers"])

    def test_owner_fields_key_same_names_by_declaring_type(self) -> None:
        """Same-named fields in nested owner types retain separate evidence."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            source = Path("Packages/pkg/Runtime/Owner.cs")
            source_path = root / source
            source_path.parent.mkdir(parents=True)
            source_path.write_text(
                "class Owner {\n"
                "    private sealed class First {\n"
                "        private int _state;\n"
                "        internal void Set() { _state = 1; }\n"
                "    }\n"
                "    private sealed class Second {\n"
                "        private int _state;\n"
                "        internal void Set() { _state = 2; }\n"
                "    }\n"
                "}\n",
                encoding="utf-8",
            )
            with mock.patch.object(lifecycle_ownership.audit_lib, "ROOT", root):
                fields, _ = lifecycle_ownership._collect_owner_fields([source])
        self.assertEqual({"Owner::First::_state", "Owner::Second::_state"}, set(fields))
        self.assertEqual(["Packages/pkg/Runtime/Owner.cs:4"], fields["Owner::First::_state"]["writers"])
        self.assertEqual(["Packages/pkg/Runtime/Owner.cs:8"], fields["Owner::Second::_state"]["writers"])

    def test_nested_field_does_not_inherit_parent_writer(self) -> None:
        """A parent field write cannot satisfy an unwritten nested field."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            source = Path("Packages/pkg/Runtime/Owner.cs")
            source_path = root / source
            source_path.parent.mkdir(parents=True)
            source_path.write_text(
                "class Owner {\n"
                "    private int _state;\n"
                "    internal void Set() { _state = 1; }\n"
                "    private sealed class Child {\n"
                "        private int _state;\n"
                "    }\n"
                "}\n",
                encoding="utf-8",
            )
            with mock.patch.object(lifecycle_ownership.audit_lib, "ROOT", root):
                fields, missing = lifecycle_ownership._collect_owner_fields([source])
        self.assertEqual(["Owner::Child::_state"], missing)
        self.assertEqual([], fields["Owner::Child::_state"]["writers"])

    def test_owner_fields_aggregate_writers_across_partial_files(self) -> None:
        """A writer in another partial file satisfies the declared field."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            declaration = Path("Packages/pkg/Runtime/Owner.cs")
            writer = Path("Packages/pkg/Runtime/Owner.Lifecycle.cs")
            (root / declaration).parent.mkdir(parents=True)
            (root / declaration).write_text("private int _state;\n", encoding="utf-8")
            (root / writer).write_text("_state = 1;\n", encoding="utf-8")
            with mock.patch.object(lifecycle_ownership.audit_lib, "ROOT", root):
                fields, missing = lifecycle_ownership._collect_owner_fields([declaration, writer])
        self.assertEqual([], missing)
        self.assertEqual(["Packages/pkg/Runtime/Owner.Lifecycle.cs:1"], fields["Owner::_state"]["writers"])

    def test_owner_fields_keep_missing_writer_evidence(self) -> None:
        """A mutable declaration without a writer remains a blocking finding."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            declaration = Path("Packages/pkg/Runtime/Owner.cs")
            (root / declaration).parent.mkdir(parents=True)
            (root / declaration).write_text("private int _state;\n", encoding="utf-8")
            with mock.patch.object(lifecycle_ownership.audit_lib, "ROOT", root):
                fields, missing = lifecycle_ownership._collect_owner_fields([declaration])
        self.assertEqual(["Owner::_state"], missing)
        self.assertEqual([], fields["Owner::_state"]["writers"])

    def test_owner_fields_classify_serialized_configuration_without_writer(self) -> None:
        """Serialized configuration fields are retained with an explicit classification."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            declaration = Path("Packages/pkg/Runtime/Owner.cs")
            (root / declaration).parent.mkdir(parents=True)
            (root / declaration).write_text("[SerializeField] private bool _enabled;\n", encoding="utf-8")
            with mock.patch.object(lifecycle_ownership.audit_lib, "ROOT", root):
                fields, missing = lifecycle_ownership._collect_owner_fields([declaration])
        self.assertEqual([], missing)
        self.assertEqual("configuration", fields["Owner::_enabled"]["classification"])
        self.assertEqual([], fields["Owner::_enabled"]["writers"])

    def test_field_classification_does_not_leak_attributes_or_static_state(self) -> None:
        """Only an adjacent SerializeField marks a non-static configuration field."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            declaration = Path("Packages/pkg/Runtime/Owner.cs")
            (root / declaration).parent.mkdir(parents=True)
            (root / declaration).write_text(
                "[SerializeField] private bool _enabled;\n"
                "private static double s_lastRefresh;\n"
                "s_lastRefresh = 1d;\n",
                encoding="utf-8",
            )
            with mock.patch.object(lifecycle_ownership.audit_lib, "ROOT", root):
                fields, missing = lifecycle_ownership._collect_owner_fields([declaration])
        self.assertEqual("configuration", fields["Owner::_enabled"]["classification"])
        self.assertEqual("mutable", fields["Owner::s_lastRefresh"]["classification"])
        self.assertEqual([], missing)

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
