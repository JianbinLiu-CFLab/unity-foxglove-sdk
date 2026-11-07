"""Regression tests for plan audit utilities."""

from __future__ import annotations

import hashlib
import json
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest import mock

from Scripts.validation.plan_audit import audit_lib
from Scripts.validation.plan_audit import bridge_history
from Scripts.validation.plan_audit import source_text_rules
from Scripts.validation.plan_audit import unity_coverage
from Scripts.validation.plan_audit import unity_acceptance
from Scripts.validation.plan_audit import unity_lane
from Scripts.validation.plan_audit import performance_evidence


class PlanAuditLibraryTests(unittest.TestCase):
    """Verify audit helpers preserve source state and evidence boundaries."""

    def test_mutate_restores_bytes_after_success_and_failure(self) -> None:
        """Mutation restores the original bytes on both exit paths."""
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / "source.py"
            path.write_text("VALUE = 1\n", encoding="utf-8")
            original = path.read_bytes()
            with audit_lib.mutate(path, "VALUE = 1", "VALUE = 2"):
                self.assertEqual("VALUE = 2\n", path.read_text(encoding="utf-8"))
            self.assertEqual(original, path.read_bytes())
            with self.assertRaises(RuntimeError):
                with audit_lib.mutate(path, "VALUE = 1", "VALUE = 3"):
                    raise RuntimeError("probe")
            self.assertEqual(original, path.read_bytes())

    def test_mutate_rejects_zero_or_multiple_matches(self) -> None:
        """Mutation refuses ambiguous replacement targets."""
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / "source.py"
            path.write_text("VALUE = 1\nVALUE = 1\n", encoding="utf-8")
            with self.assertRaises(ValueError):
                with audit_lib.mutate(path, "VALUE = 2", "VALUE = 3"):
                    pass
            with self.assertRaises(ValueError):
                with audit_lib.mutate(path, "VALUE = 1", "VALUE = 3"):
                    pass

    def test_worktree_snapshot_applies_exclusions(self) -> None:
        """Snapshots return a stable digest and honor excluded prefixes."""
        text, digest = audit_lib.worktree_snapshot(exclude=("build/",))
        self.assertEqual(hashlib.sha256(text.encode("utf-8")).hexdigest(), digest)
        self.assertNotIn("build/", text.replace("\\", "/"))

    def test_write_verdict_rejects_invalid_status(self) -> None:
        """Verdict output rejects statuses outside the audit contract."""
        check = audit_lib.Check("x", "Plan/x", "auto", "BROKEN", "true", "build/x", "bad")
        with tempfile.TemporaryDirectory() as temp:
            with self.assertRaises(ValueError):
                audit_lib.write_verdict("topic", "195", [check], "sha", Path(temp), True)

    def test_temp_git_tree_is_independent(self) -> None:
        """Exported audit trees resolve their own repository root."""
        audit_lib.BUILD_ROOT.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(dir=audit_lib.BUILD_ROOT) as temp:
            destination = Path(temp) / "tree"
            audit_lib.temp_git_tree("HEAD", destination, ("Scripts/README.md",))
            result = audit_lib.run(
                ["git", "rev-parse", "--show-toplevel"],
                Path(temp) / "root.log",
                30,
                cwd=destination,
            )
            self.assertEqual(0, result["exit_code"])
            self.assertEqual(destination.resolve().as_posix().lower(), str(result["output"]).strip().replace("\\", "/").lower())

    def test_history_marks_log_retrieval_failure_as_not_run(self) -> None:
        """A failed GitHub log fetch cannot be reported as clean history."""
        audit_lib.BUILD_ROOT.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(dir=audit_lib.BUILD_ROOT) as temp:
            output = Path(temp)

            def fake_run(command, log_path, timeout, cwd=audit_lib.ROOT):
                """Return one failed history listing followed by an inaccessible log."""
                del timeout, cwd
                log_path.parent.mkdir(parents=True, exist_ok=True)
                if command[1:3] == ["run", "list"]:
                    log_path.write_text('[{"databaseId": 123, "conclusion": "failure"}]\n', encoding="utf-8")
                    return {"exit_code": 0, "output": '[{"databaseId": 123, "conclusion": "failure"}]'}
                log_path.write_text("permission denied\n", encoding="utf-8")
                return {"exit_code": 1, "output": "permission denied"}

            with mock.patch.object(bridge_history.audit_lib, "run", side_effect=fake_run):
                check = bridge_history._history(output)
            self.assertEqual("NOT_RUN", check.status)
            self.assertIn("123", check.detail)

    def test_plan_check_reports_missing_plan_file(self) -> None:
        """A missing plan artifact produces a controlled audit result."""
        with mock.patch.object(unity_acceptance, "PLAN", Path("build/plan-audit/missing-plan.md")):
            check = unity_acceptance._plan_check("base")
        self.assertEqual("MISSING", check.status)

    def test_source_text_rules_missing_inputs_are_controlled(self) -> None:
        """Missing local audit inputs produce explicit MISSING checks."""
        audit_lib.BUILD_ROOT.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(dir=audit_lib.BUILD_ROOT) as temp:
            root = Path(temp)
            output = root / "output"
            (output / "logs").mkdir(parents=True)
            with mock.patch.object(audit_lib, "ROOT", root), mock.patch.object(source_text_rules, "OUTPUT", output), mock.patch.object(source_text_rules, "PLAN", root / "Plan198.md"):
                self.assertEqual("MISSING", source_text_rules._plan_check("base").status)
                self.assertEqual("MISSING", source_text_rules._rules_check().status)

    def test_source_text_registry_blocks_span_multiline_entries(self) -> None:
        """Registry parsing keeps multiline entries scoped to their own flag."""
        blocks = source_text_rules._registry_blocks(
            'Ci("--phase-one",\n    "one", One.Validate, evidence: ValidationEvidence.Structural),\n'
            'Ci("--phase-two", "two", Two.Validate, evidence: ValidationEvidence.Behavior),\n'
        )
        self.assertEqual(["--phase-one", "--phase-two"], [flag for flag, _ in blocks])
        self.assertIn("One.Validate", blocks[0][1])
        self.assertNotIn("Two.Validate", blocks[0][1])

    def test_source_text_transfer_counts_are_exact(self) -> None:
        """Transfer checks reject missing or incomplete source assertion sets."""
        audit_lib.BUILD_ROOT.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(dir=audit_lib.BUILD_ROOT) as temp:
            root = Path(temp)
            package = root / "Packages" / "fixture" / "Tests" / "Unit"
            package.mkdir(parents=True)
            runtime = package / "RuntimeReviewFindingTests.cs"
            camera = package / "CameraDiagnosticsAttributionTests.cs"
            runtime.write_text("\n".join('value.Contains("x");' for _ in range(18)), encoding="utf-8")
            camera.write_text("\n".join('value.Contains("x");' for _ in range(71)), encoding="utf-8")
            output = root / "output"
            (output / "logs").mkdir(parents=True)
            with mock.patch.object(audit_lib, "ROOT", root), mock.patch.object(source_text_rules, "OUTPUT", output):
                checks = source_text_rules._transfer_checks([])
                self.assertEqual(["PASS", "PASS", "PASS"], [check.status for check in checks])
                camera.write_text("\n".join('value.Contains("x");' for _ in range(70)), encoding="utf-8")
                checks = source_text_rules._transfer_checks([])
                self.assertEqual("FAIL", checks[1].status)

    def test_plan_check_rejects_missing_or_unrecognized_status(self) -> None:
        """A plan must declare a recognized delivered status before passing."""
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / "plan.md"
            with mock.patch.object(unity_acceptance, "PLAN", path):
                path.write_text("# Plan\n", encoding="utf-8")
                self.assertEqual("FAIL", unity_acceptance._plan_check("base").status)
                path.write_text("Status: Blocked\n", encoding="utf-8")
                self.assertEqual("FAIL", unity_acceptance._plan_check("base").status)
                path.write_text("Status: implemented\n", encoding="utf-8")
                self.assertEqual("PASS", unity_acceptance._plan_check("base").status)
                path.write_text("Status: Not implemented\n", encoding="utf-8")
                self.assertEqual("FAIL", unity_acceptance._plan_check("base").status)
                path.write_text("Status: Not completed\n", encoding="utf-8")
                self.assertEqual("FAIL", unity_acceptance._plan_check("base").status)

    def test_resolve_revision_falls_back_when_origin_main_is_missing(self) -> None:
        """Revision resolution remains usable without a fetched origin/main ref."""
        def fake_run(command, **kwargs):
            """Return only the fallback HEAD revision as available."""
            del kwargs
            if command[-1] == "HEAD":
                return SimpleNamespace(returncode=0, stdout="head-sha\n")
            return SimpleNamespace(returncode=128, stdout="")

        with mock.patch.object(audit_lib.subprocess, "run", side_effect=fake_run):
            self.assertEqual("head-sha", audit_lib.resolve_revision())

    def test_definition_of_done_rejects_not_run_and_manual(self) -> None:
        """Required checks must have PASS evidence before DoD can pass."""
        checks = [
            audit_lib.Check("195-PLAN", "Plan", "history", "PASS", "true", "build/a", ""),
            audit_lib.Check("195-W0-local100", "Plan", "auto", "PASS", "true", "build/b", ""),
            audit_lib.Check("195-W1-assets", "Plan", "auto", "PASS", "true", "build/c", ""),
            audit_lib.Check("195-W1-script", "Plan", "auto", "PASS", "true", "build/d", ""),
            audit_lib.Check("195-W1-workflow", "Plan", "auto", "PASS", "true", "build/e", ""),
            audit_lib.Check("195-W2", "Plan", "probe", "NOT_RUN", "true", "build/f", ""),
            audit_lib.Check("195-W3", "Plan", "probe", "PASS", "true", "build/g", ""),
            audit_lib.Check("195-W4", "Plan", "history", "MANUAL", "true", "build/h", ""),
        ]
        check = unity_acceptance._dod_check(checks)
        self.assertEqual("FAIL", check.status)
        self.assertIn("195-W2", check.detail)
        self.assertIn("195-W4", check.detail)

    def test_w4_workflow_accepts_escaped_package_paths(self) -> None:
        """W4 recognizes the escaped package path used by the shell workflow."""
        with tempfile.TemporaryDirectory(dir=audit_lib.BUILD_ROOT) as temp:
            with mock.patch.object(
                unity_coverage.subprocess,
                "run",
                side_effect=[SimpleNamespace(stdout="feature/test\n"), SimpleNamespace(stdout="head\n")],
            ), mock.patch.object(
                unity_coverage.audit_lib,
                "run",
                return_value={"exit_code": 0, "output": "[]"},
            ):
                checks: list[audit_lib.Check] = []
                unity_coverage._workflow_w4(checks, Path(temp))
            self.assertEqual("195-W4", checks[0].id)
            self.assertNotEqual("MISSING", checks[0].status)

    def test_local100_does_not_trust_cached_summary(self) -> None:
        """A cached 100-run summary never bypasses a fresh acceptance loop."""
        audit_lib.BUILD_ROOT.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(dir=audit_lib.BUILD_ROOT) as temp:
            output = Path(temp)
            summary = output / "logs" / "w0-local100-summary.json"
            summary.parent.mkdir(parents=True, exist_ok=True)
            summary.write_text(json.dumps({"base_sha": "current", "source_tree_sha": "current", "runs": 100, "passes": 100}), encoding="utf-8")
            with mock.patch.object(
                bridge_history, "_local100_impl", return_value=audit_lib.Check("x", "Plan", "auto", "PASS", "true", "build/x", "rerun")
            ) as implementation:
                check = bridge_history._local100(output)
            implementation.assert_called_once_with(output)
            self.assertEqual("PASS", check.status)

    def test_worktree_tree_hash_changes_when_source_bytes_change(self) -> None:
        """The cache identity includes current worktree bytes rather than only a ref."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            source = root / "source.cs"
            source.write_text("VALUE = 1\n", encoding="utf-8")
            first = bridge_history._worktree_tree_sha(root)
            source.write_text("VALUE = 2\n", encoding="utf-8")
            second = bridge_history._worktree_tree_sha(root)
        self.assertNotEqual(first, second)

    def test_bridge_test_identity_requires_exact_fully_qualified_name(self) -> None:
        """Bridge acceptance rejects lookalike and duplicate test identities."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            exact = bridge_history.FULL_TEST_NAME
            passed = root / "passed.trx"
            passed.write_text(
                f'<TestRun><Results><UnitTestResult testName="{exact}" outcome="Passed" /></Results></TestRun>',
                encoding="utf-8",
            )
            self.assertEqual((True, "one matching UnitTestResult with outcome Passed"), bridge_history._one_result(passed))

            lookalike = root / "lookalike.trx"
            lookalike.write_text(
                f'<TestRun><Results><UnitTestResult testName="{exact}Backdoor" outcome="Passed" /></Results></TestRun>',
                encoding="utf-8",
            )
            self.assertEqual((False, "TRX result count=1, matching=0"), bridge_history._one_result(lookalike))

            duplicate = root / "duplicate.trx"
            duplicate.write_text(
                f'<TestRun><Results><UnitTestResult testName="{exact}" outcome="Passed" />'
                f'<UnitTestResult testName="{exact}" outcome="Passed" /></Results></TestRun>',
                encoding="utf-8",
            )
            self.assertEqual((False, "TRX result count=2, matching=2"), bridge_history._one_result(duplicate))

        self.assertTrue(bridge_history._discovery_has_exact_test(f"{bridge_history.FULL_TEST_NAME}\n"))
        self.assertFalse(bridge_history._discovery_has_exact_test(f"{bridge_history.FULL_TEST_NAME}Backdoor\n"))

    def test_dispatch_evidence_requires_audited_revision(self) -> None:
        """Dispatch evidence from another revision cannot satisfy the audit."""
        audit_lib.BUILD_ROOT.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(dir=audit_lib.BUILD_ROOT) as temp:
            output = Path(temp)

            def fake_run(command, log_path, timeout, cwd=audit_lib.ROOT):
                """Return one stale and one audited successful dispatch run."""
                del timeout, cwd
                log_path.parent.mkdir(parents=True, exist_ok=True)
                if command[1:3] == ["run", "list"]:
                    return {
                        "exit_code": 0,
                        "output": json.dumps(
                            [
                                {"databaseId": 1, "conclusion": "success", "headSha": "stale"},
                                {"databaseId": 2, "conclusion": "success", "headSha": "audited"},
                            ]
                        ),
                    }
                return {"exit_code": 0, "output": '{"jobs":[{"name":"unity-tests","conclusion":"success"}]}' }

            checks: list[audit_lib.Check] = []
            with mock.patch.object(unity_lane.audit_lib, "run", side_effect=fake_run):
                unity_lane._dispatch(checks, output, "audited")
            self.assertEqual("PASS", checks[0].status)
            self.assertIn("verified runs=[2]", checks[0].detail)

    def test_preflight_allows_unheld_stale_lockfile(self) -> None:
        """A stale lockfile that opens exclusively does not block Unity."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            unity = root / "Unity.exe"
            unity.write_bytes(b"")
            lockfile = root / "Unity2Foxglove" / "Temp" / "UnityLockfile"
            lockfile.parent.mkdir(parents=True)
            lockfile.write_bytes(b"")
            with mock.patch.object(unity_lane.audit_lib, "ROOT", root), mock.patch.dict(
                unity_lane.os.environ, {"UNITY_EXECUTABLE": str(unity)}, clear=False
            ), mock.patch.object(unity_lane.subprocess, "run", return_value=SimpleNamespace(stdout="", returncode=0)):
                self.assertFalse(unity_lane._lockfile_is_held(lockfile))
                self.assertEqual((True, "preflight passed"), unity_lane._preflight())

    def test_preflight_ignores_unrelated_unity_project(self) -> None:
        """A Unity process for another project does not block the audit."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            unity = root / "Unity.exe"
            unity.write_bytes(b"")
            with mock.patch.object(unity_lane.audit_lib, "ROOT", root), mock.patch.dict(
                unity_lane.os.environ, {"UNITY_EXECUTABLE": str(unity)}, clear=False
            ), mock.patch.object(
                unity_lane.subprocess, "run", return_value=SimpleNamespace(stdout='-projectPath "C:\\other-project"', returncode=0)
            ):
                self.assertEqual((True, "preflight passed"), unity_lane._preflight())

    def test_preflight_fails_closed_when_process_query_fails(self) -> None:
        """A failed ownership query must not launch Unity without verification."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            unity = root / "Unity.exe"
            unity.write_bytes(b"")
            with mock.patch.object(unity_lane.audit_lib, "ROOT", root), mock.patch.dict(
                unity_lane.os.environ, {"UNITY_EXECUTABLE": str(unity)}, clear=False
            ), mock.patch.object(
                unity_lane.subprocess, "run", return_value=SimpleNamespace(stdout="", returncode=1)
            ):
                safe, reason = unity_lane._preflight()
            self.assertFalse(safe)
            self.assertIn("query failed", reason)

    def test_preflight_rejects_held_lockfile(self) -> None:
        """A lockfile held by another owner blocks the Unity red run."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            unity = root / "Unity.exe"
            unity.write_bytes(b"")
            with mock.patch.object(unity_lane.audit_lib, "ROOT", root), mock.patch.dict(
                unity_lane.os.environ, {"UNITY_EXECUTABLE": str(unity)}, clear=False
            ), mock.patch.object(unity_lane, "_lockfile_is_held", return_value=True), mock.patch.object(
                unity_lane.subprocess, "run", return_value=SimpleNamespace(stdout="")
            ):
                self.assertEqual((False, "Unity2Foxglove/Temp/UnityLockfile is held"), unity_lane._preflight())

    def test_unity_acceptance_requires_exact_behavior_case_set(self) -> None:
        """Acceptance evidence must contain every named behavior case exactly once."""
        with tempfile.TemporaryDirectory() as temp:
            results = Path(temp) / "playmode.xml"
            results.write_text(
                """<test-run><test-case name=\"GraphicsAcceptance_CameraInfoUsesImageSourceCamera\" result=\"Passed\"><properties><property name=\"Category\" value=\"GraphicsAcceptance\" /></properties></test-case></test-run>""",
                encoding="utf-8",
            )
            status, detail = unity_coverage._unity_acceptance_result(
                results,
                "GraphicsAcceptance",
                unity_coverage.EXPECTED_CASES["W2"],
            )
        self.assertEqual("FAIL", status)
        self.assertIn("duplicate-or-missing", detail)

    def test_mutation_evidence_requires_named_failure(self) -> None:
        """A mutation verdict must identify the expected failing behavior case."""
        with tempfile.TemporaryDirectory() as temp:
            results = Path(temp) / "playmode.xml"
            results.write_text(
                """<test-run><test-case name=\"Unrelated\" result=\"Failed\"><properties><property name=\"Category\" value=\"GraphicsAcceptance\" /></properties></test-case></test-run>""",
                encoding="utf-8",
            )
            self.assertFalse(
                unity_coverage._mutation_failure_observed(
                    results,
                    "GraphicsAcceptance",
                    "GraphicsAcceptance_ResizeDuringReadbackProducesNoErrors",
                )
            )


__all__ = ["PlanAuditLibraryTests"]
