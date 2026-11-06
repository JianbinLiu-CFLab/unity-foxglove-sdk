"""Regression tests for plan audit utilities."""

from __future__ import annotations

import hashlib
import tempfile
import unittest
from pathlib import Path

from Scripts.validation.plan_audit import audit_lib


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


__all__ = ["PlanAuditLibraryTests"]
