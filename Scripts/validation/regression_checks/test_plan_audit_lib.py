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
from Scripts.validation.plan_audit import unity_acceptance


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

    def test_local100_cache_rejects_mismatched_source_identity(self) -> None:
        """A cached 100-run result is rerun when the audited source identity changes."""
        audit_lib.BUILD_ROOT.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(dir=audit_lib.BUILD_ROOT) as temp:
            output = Path(temp)
            summary = output / "logs" / "w0-local100-summary.json"
            summary.parent.mkdir(parents=True, exist_ok=True)
            summary.write_text(json.dumps({"base_sha": "old", "source_tree_sha": "old", "runs": 100, "passes": 100}), encoding="utf-8")
            with mock.patch.object(bridge_history, "_cache_identity", return_value=("new", "new")), mock.patch.object(
                bridge_history, "_local100_impl", return_value=audit_lib.Check("x", "Plan", "auto", "PASS", "true", "build/x", "rerun")
            ) as implementation:
                check = bridge_history._local100(output)
            implementation.assert_called_once_with(output)
            self.assertEqual("PASS", check.status)


__all__ = ["PlanAuditLibraryTests"]
