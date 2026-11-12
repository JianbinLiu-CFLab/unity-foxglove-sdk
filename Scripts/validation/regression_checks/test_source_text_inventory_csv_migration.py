"""Exercise trusted CSV parser migration compatibility through the CLI."""

from __future__ import annotations

import json
import os
import shutil
import subprocess
import sys
import tempfile
import textwrap
import unittest
from pathlib import Path

from Scripts.release.regression_checks.test_release_tooling.foundation import (
    DOTNET_WORKFLOW_PATH,
    ROOT,
    active_workflow_line_index,
)

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

    def test_normal_base_checker_changes_fail_closed(self) -> None:
        """Reject a changed head checker when the base is outside migration approvals."""
        git_path = shutil.which("git")
        if os.name == "nt":
            candidates = []
            if git_path:
                candidates.append(Path(git_path).resolve().parents[1] / "bin" / "bash.exe")
            candidates.append(Path("C:/Program Files/Git/bin/bash.exe"))
            bash_path = next((path for path in candidates if path.is_file()), None)
        else:
            bash_path = Path(shutil.which("bash") or "")
        if not git_path or bash_path is None or not bash_path.is_file():
            self.skipTest("Git and Bash are required for the workflow fixture")

        scratch = ROOT / "build" / "tmp"
        scratch.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(dir=scratch) as directory:
            fixture = Path(directory)
            validation = fixture / "Scripts" / "validation"
            validation.mkdir(parents=True)
            (validation / "source_text_inventory.py").write_text(
                "import argparse\n"
                "parser = argparse.ArgumentParser()\n"
                "parser.add_argument('--base-ref')\n"
                "parser.parse_args()\n",
                encoding="utf-8",
            )
            (validation / "helper.py").write_text("VALUE = 1\n", encoding="utf-8")
            for relative in (
                "Packages/fixture/Runtime/Sub/Product.cs",
                "Packages/fixture/Editor/Sub/Editor.cs",
                "Packages/fixture/Tests/Runtime/Sub/Runtime.cs",
                "Packages/fixture/Tests/Unit/Sub/Unit.cs",
            ):
                path = fixture / relative
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text("public sealed class FixtureType {}\n", encoding="utf-8")

            subprocess.run(["git", "init", "-q", str(fixture)], check=True)
            subprocess.run(["git", "-C", str(fixture), "config", "user.name", "fixture"], check=True)
            subprocess.run(
                ["git", "-C", str(fixture), "config", "user.email", "fixture@example.invalid"],
                check=True,
            )
            subprocess.run(["git", "-C", str(fixture), "add", "-A"], check=True)
            subprocess.run(["git", "-C", str(fixture), "commit", "-qm", "minimum"], check=True)
            minimum = subprocess.check_output(
                ["git", "-C", str(fixture), "rev-parse", "HEAD"], text=True
            ).strip()
            (fixture / "base-marker.txt").write_text("base\n", encoding="utf-8")
            subprocess.run(["git", "-C", str(fixture), "add", "-A"], check=True)
            subprocess.run(["git", "-C", str(fixture), "commit", "-qm", "base"], check=True)
            base = subprocess.check_output(
                ["git", "-C", str(fixture), "rev-parse", "HEAD"], text=True
            ).strip()

            workflow = DOTNET_WORKFLOW_PATH.read_text(encoding="utf-8")
            start = active_workflow_line_index(workflow, "- name: Run live source-text inventory")
            end = active_workflow_line_index(workflow, "- name: Upload live source-text inventory")
            lines = workflow.splitlines()[start:end]
            run_index = next(index for index, line in enumerate(lines) if line.strip() == "run: |")
            run_indent = len(lines[run_index]) - len(lines[run_index].lstrip())
            script = textwrap.dedent(
                "\n".join(
                    line[run_indent + 2 :] if line.strip() else ""
                    for line in lines[run_index + 1 :]
                )
            )
            self.assertIn("migration_args=()", script)
            guard = script.split("migration_args=()", 1)[0]
            guard = guard.replace(
                "fe195f288a3df0c9754056ef2d84c095c2e98cf7", minimum
            )
            if os.name == "nt":
                guard = guard.replace("python3", "python")

            runner_temp = fixture / "runner-temp"
            runner_temp.mkdir()
            if os.name == "nt":
                converted = subprocess.run(
                    [str(bash_path), "-lc", 'cygpath -u "$1"', "bash", str(runner_temp)],
                    check=True,
                    capture_output=True,
                    text=True,
                ).stdout.strip()
            else:
                converted = str(runner_temp)
            environment = os.environ.copy()
            environment["RUNNER_TEMP"] = converted
            environment["SOURCE_TEXT_INVENTORY_BASE_SHA"] = base

            def run_guard():
                return subprocess.run(
                    [str(bash_path), "-lc", guard],
                    cwd=fixture,
                    env=environment,
                    capture_output=True,
                    text=True,
                    check=False,
                )

            self.assertEqual(0, run_guard().returncode)
            checker = validation / "source_text_inventory.py"
            checker.write_text(checker.read_text(encoding="utf-8") + "# bypass\n", encoding="utf-8")
            rejected = run_guard()
            self.assertNotEqual(0, rejected.returncode)
            self.assertIn("immutable base checker", rejected.stderr)
