"""Regression coverage for the delegate-target manifest generator and checker."""

from __future__ import annotations

import io
import json
import subprocess
import tempfile
import unittest
from contextlib import redirect_stdout
from pathlib import Path

from Scripts.validation import source_text_delegate_targets as targets

FILE = "Packages/fixture/Tests/Unit/Fixture.cs"


def _sarif(results: list[dict]) -> str:
    """Return a minimal SARIF 2.1 log with the given results."""
    return json.dumps({"version": "2.1.0", "runs": [{"results": results}]})


def _entry(variant: str, target: str = "M:Fixture.FixtureValidation.Used") -> dict:
    """Return one FOXDT900 SARIF result for a manifest line."""
    line = f"{variant}\t{FILE}\tM:Fixture.FixtureValidation.Validate\t{target}\t1"
    return {"ruleId": "FOXDT900", "level": "note", "message": {"text": line}}


class _FakeBuilds:
    """Simulate emit builds by writing a prepared SARIF log per variant."""

    def __init__(self, logs: dict[str, str], returncodes: dict[str, int] | None = None) -> None:
        """Store the SARIF text and exit code to produce for each variant."""
        self.logs = logs
        self.returncodes = returncodes or {}
        self.commands: list[list[str]] = []

    def __call__(self, command, cwd):
        """Write the variant's SARIF log where the command asks for it."""
        self.commands.append(list(command))
        sarif = next(arg.split("=", 1)[1] for arg in command if arg.startswith("-p:FoxDelegateTargetsErrorLog="))
        variant = Path(sarif).stem
        if variant in self.logs:
            Path(sarif).write_text(self.logs[variant], encoding="utf-8")
        return subprocess.CompletedProcess(command, self.returncodes.get(variant, 0), "build output", "")


class DelegateTargetsGeneratorTests(unittest.TestCase):
    """Cover SARIF parsing, abort rules, rendering, and --write/--check behavior."""

    def _run(self, root: Path, argv: list[str], builds: _FakeBuilds) -> tuple[int, str]:
        """Run the CLI against a temporary root with simulated builds."""
        output = io.StringIO()
        with redirect_stdout(output):
            code = targets.main(["--root", str(root), *argv], runner=builds)
        return code, output.getvalue()

    def test_write_then_check_round_trips_every_variant(self):
        """--write renders the sorted union and --check accepts it unchanged."""
        logs = {name: _sarif([_entry(name)]) for name, _, _ in targets.VARIANTS}
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            code, output = self._run(root, ["--write"], _FakeBuilds(logs))
            self.assertEqual(0, code, output)
            text = (root / targets.MANIFEST).read_text(encoding="utf-8")
            lines = text.splitlines()
            self.assertEqual(sorted(lines), lines)
            self.assertTrue(text.endswith("\n"))
            self.assertNotIn("\r", text)
            self.assertEqual(len(targets.VARIANTS), len(lines))
            code, output = self._run(root, ["--check"], _FakeBuilds(logs))
            self.assertEqual(0, code, output)
            self.assertIn("DELEGATE_TARGETS_OK", output)

    def test_check_reports_missing_and_stale_lines(self):
        """--check fails when the committed manifest differs from the builds."""
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / targets.MANIFEST).parent.mkdir(parents=True)
            (root / targets.MANIFEST).write_text(
                f"unit\t{FILE}\tM:Fixture.FixtureValidation.Validate\tM:Fixture.FixtureValidation.Noop\t1\n", encoding="utf-8"
            )
            code, output = self._run(root, ["--check", "--variants", "unit"], _FakeBuilds({"unit": _sarif([_entry("unit")])}))
            self.assertEqual(1, code, output)
            self.assertIn("DELEGATE_TARGETS_MISSING unit", output)
            self.assertIn("DELEGATE_TARGETS_STALE unit", output)

    def test_check_fails_without_a_committed_manifest(self):
        """--check never treats a missing manifest as empty."""
        with tempfile.TemporaryDirectory() as directory:
            code, output = self._run(Path(directory), ["--check", "--variants", "unit"], _FakeBuilds({"unit": _sarif([])}))
            self.assertEqual(1, code, output)
            self.assertIn("manifest missing", output)

    def test_builds_with_other_errors_abort(self):
        """Any non-FOXDT900 error, a missing log, or a failed build aborts the run."""
        error = {"ruleId": "CS0103", "level": "error", "message": {"text": "The name 'x' does not exist"}}
        cases = {
            "compiler error": (_FakeBuilds({"unit": _sarif([_entry("unit"), error])}), "CS0103"),
            "missing sarif": (_FakeBuilds({}), "no SARIF log"),
            "failed build": (_FakeBuilds({"unit": _sarif([_entry("unit")])}, {"unit": 1}), "build failed"),
            "foreign variant": (_FakeBuilds({"unit": _sarif([_entry("unit-native")])}), "another variant"),
        }
        for name, (builds, marker) in cases.items():
            with self.subTest(name), tempfile.TemporaryDirectory() as directory:
                code, output = self._run(Path(directory), ["--write", "--variants", "unit"], builds)
                self.assertEqual(1, code, output)
                self.assertIn(marker, output)
                self.assertFalse((Path(directory) / targets.MANIFEST).exists())

    def test_partial_write_keeps_other_variants(self):
        """--write for a subset replaces only the selected variants' lines."""
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / targets.MANIFEST).parent.mkdir(parents=True)
            keep = f"bridge-unit\t{FILE}\tM:Fixture.FixtureValidation.Validate\tM:Fixture.FixtureValidation.Used\t1"
            drop = f"unit\t{FILE}\tM:Fixture.FixtureValidation.Validate\tM:Fixture.FixtureValidation.Noop\t1"
            (root / targets.MANIFEST).write_text(keep + "\n" + drop + "\n", encoding="utf-8")
            code, output = self._run(root, ["--write", "--variants", "unit"], _FakeBuilds({"unit": _sarif([_entry("unit")])}))
            self.assertEqual(0, code, output)
            lines = (root / targets.MANIFEST).read_text(encoding="utf-8").splitlines()
            self.assertIn(keep, lines)
            self.assertNotIn(drop, lines)
            self.assertIn(_entry("unit")["message"]["text"], lines)

    def test_emit_commands_cover_the_ci_variant_matrix(self):
        """Every CI variant is built with emit mode and its own error log."""
        logs = {name: _sarif([]) for name, _, _ in targets.VARIANTS}
        builds = _FakeBuilds(logs)
        with tempfile.TemporaryDirectory() as directory:
            code, output = self._run(Path(directory), ["--write"], builds)
        self.assertEqual(0, code, output)
        self.assertEqual(len(targets.VARIANTS), len(builds.commands))
        for command in builds.commands:
            self.assertIn("-p:FoxDelegateTargetsEmit=true", command)
        logs_used = [next(a for a in c if a.startswith("-p:FoxDelegateTargetsErrorLog=")) for c in builds.commands]
        self.assertEqual(len(set(logs_used)), len(logs_used))

    def test_unknown_variant_is_rejected(self):
        """Selecting a variant outside the CI matrix is an error."""
        with tempfile.TemporaryDirectory() as directory:
            code, output = self._run(Path(directory), ["--check", "--variants", "runtime-native"], _FakeBuilds({}))
        self.assertEqual(2, code, output)


if __name__ == "__main__":
    unittest.main()
