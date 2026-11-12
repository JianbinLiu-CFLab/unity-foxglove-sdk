"""Exercise trusted CSV parser migration compatibility through the CLI."""

from __future__ import annotations

import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

from .test_source_text_inventory_semantics import _FixtureSupport


class SourceTextInventoryCsvMigrationTests(_FixtureSupport, unittest.TestCase):
    """Keep empty parser versions compatible with legacy trusted reports."""

    def _run_migration_cli(self, root, approval=None, trusted_base=None):
        """Run the checker with optional parser approval and trusted CSV input."""
        args = [
            sys.executable,
            "-B",
            str(root / "Scripts/validation/source_text_inventory.py"),
            "--root",
            str(root),
            "--base-ref",
            "HEAD",
            "--output",
            str(root / "build/inventory"),
        ]
        if approval is not None:
            args.extend(["--parser-migration-approval", str(approval)])
        if trusted_base is not None:
            args.extend(["--trusted-base-inventory", str(trusted_base)])
        return subprocess.run(args, capture_output=True, text=True, check=False)

    def test_csv_legacy_baseline_preserves_parser_migration_status(self):
        """Accept a trusted CSV whose parser version column is empty for every row."""
        from Scripts.validation.source_text_inventory import inventory
        from Scripts.validation.source_text_inventory_report import write_report

        with tempfile.TemporaryDirectory() as directory:
            root = self._git_fixture(directory)
            checker = root / "Scripts/validation/source_text_inventory.py"
            original = checker.read_text(encoding="utf-8")
            legacy = (
                "\n_original_inventory = inventory\n"
                "def inventory(root):\n"
                "    rows = _original_inventory(root)\n"
                "    for row in rows:\n"
                "        row.pop('parser_version', None)\n"
                "    return rows\n"
            )
            checker.write_text(
                original.replace("\nif __name__ == \"__main__\":", legacy + "\nif __name__ == \"__main__\":"),
                encoding="utf-8",
            )
            subprocess.run(["git", "-C", str(root), "add", "-A"], check=True)
            subprocess.run(["git", "-C", str(root), "commit", "-qm", "legacy-base"], check=True)
            checker.write_text(original, encoding="utf-8")

            row = inventory(root)[0]
            row.pop("parser_version", None)
            report = root / "build" / "trusted-inventory"
            write_report([row], report)
            baseline = self._run_migration_cli(root, trusted_base=report)
            self.assertEqual(0, baseline.returncode, baseline.stdout + baseline.stderr)
            self.assertIn("legacy-unversioned->2 additive_schema", baseline.stdout)
            evidence = json.loads(
                (root / "build/inventory/parser_migration_evidence.json").read_text(encoding="utf-8")
            )
            approval = root / "build" / "legacy-parser-approval.json"
            approval.write_text(
                json.dumps(
                    {
                        **evidence["fingerprints"],
                        "scope": "legacy-to-versioned",
                        "reason": "Reviewed trusted CSV legacy parser migration.",
                    }
                ),
                encoding="utf-8",
            )
            result = self._run_migration_cli(root, approval=approval, trusted_base=report)
            self.assertEqual(0, result.returncode, result.stdout + result.stderr)
            self.assertIn("PARSER_MIGRATION_APPROVED", result.stdout)
