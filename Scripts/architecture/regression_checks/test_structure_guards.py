"""Regression tests for structural source guards."""

from __future__ import annotations

import importlib.util
import json
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[3]
MODULE_PATH = ROOT / "Scripts" / "architecture" / "structure_guards.py"


def load_module():
    """Load the guard implementation from the checkout under test."""
    spec = importlib.util.spec_from_file_location("structure_guards_under_test", MODULE_PATH)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    spec.loader.exec_module(module)
    return module


def write_lines(path: Path, count: int, ending: bytes = b"\n") -> None:
    """Write a file with a deterministic physical line count."""
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(ending.join([b"x"] * count) + (ending if ending else b""))


class StructureGuardTests(unittest.TestCase):
    """Cover file, partial-type, phase-path, and baseline rules."""

    def setUp(self) -> None:
        """Load the implementation under test."""
        self.guards = load_module()

    def test_file_limit_and_baseline_growth(self) -> None:
        """Allow 1000 lines, reject 1001, and enforce a frozen ceiling."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            path = root / "Source.cs"
            write_lines(path, 1000)
            self.assertEqual([], self.guards.violations(root, ["Source.cs"], {"files": {}, "partial_types": {}, "phase_paths": []}))
            write_lines(path, 1001)
            self.assertTrue(self.guards.violations(root, ["Source.cs"], {"files": {}, "partial_types": {}, "phase_paths": []}))
            baseline = {"files": {"Source.cs": 1000}, "partial_types": {}, "phase_paths": []}
            self.assertIn("limit=1000", self.guards.violations(root, ["Source.cs"], baseline)[0])
            write_lines(path, 999)
            self.assertEqual([], self.guards.violations(root, ["Source.cs"], baseline))

    def test_line_endings_and_missing_final_newline(self) -> None:
        """Count CRLF and LF equally and count an unterminated final line."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            lf = root / "lf.py"
            crlf = root / "crlf.py"
            write_lines(lf, 1001)
            write_lines(crlf, 1001, b"\r\n")
            self.assertEqual(self.guards.line_count(lf), self.guards.line_count(crlf))
            unterminated = root / "unterminated.py"
            unterminated.write_bytes(b"x\nx")
            self.assertEqual(2, self.guards.line_count(unterminated))

    def test_excluded_sources_and_golden_file_scope(self) -> None:
        """Skip generated and third-party sources but keep a golden C# file in scope."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            paths = [
                "third-party/vendor.cs",
                "Packages/pkg/Runtime/Generated/auto.cs",
                "Packages/pkg/Runtime/auto.g.cs",
                "Packages/pkg/Runtime/fixture.golden.cs",
            ]
            for path in paths:
                write_lines(root / path, 1001)
            errors = self.guards.violations(root, paths, {"files": {}, "partial_types": {}, "phase_paths": []})
            self.assertEqual(["G1 file=Packages/pkg/Runtime/fixture.golden.cs lines=1001 limit=1000"], errors)

    def test_partial_baseline_growth_and_new_type_limit(self) -> None:
        """Reject a baseline partial growth and a new type above 2000 lines."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            first = root / "Packages/pkg/Runtime/Thing.A.cs"
            second = root / "Packages/pkg/Runtime/Thing.B.cs"
            source = "namespace Demo;\npublic partial class Thing { }\n"
            first.parent.mkdir(parents=True)
            first.write_text(source + "x\n" * 999, encoding="utf-8")
            second.write_text(source + "x\n" * 999, encoding="utf-8")
            paths = ["Packages/pkg/Runtime/Thing.A.cs", "Packages/pkg/Runtime/Thing.B.cs"]
            key = "Packages/pkg|Demo|Thing"
            baseline = {"files": {}, "partial_types": {key: 2000}, "phase_paths": []}
            self.assertTrue(any("G2 partial_type=Packages/pkg|Demo|Thing" in error for error in self.guards.violations(root, paths, baseline)))
            new_key = "Packages/pkg|Demo|NewThing"
            new_a = root / "Packages/pkg/Runtime/New.A.cs"
            new_b = root / "Packages/pkg/Runtime/New.B.cs"
            new_a.write_text("namespace Demo;\npublic partial class NewThing { }\n" + "x\n" * 999, encoding="utf-8")
            new_b.write_text("namespace Demo;\npublic partial class NewThing { }\n" + "x\n" * 999, encoding="utf-8")
            paths.extend(["Packages/pkg/Runtime/New.A.cs", "Packages/pkg/Runtime/New.B.cs"])
            errors = self.guards.violations(root, paths, {"files": {}, "partial_types": {}, "phase_paths": []})
            self.assertTrue(any(new_key in error and "lines=" in error for error in errors))

    def test_partial_names_are_package_scoped_and_file_namespaces_parse(self) -> None:
        """Keep equal type names in separate packages and parse file-scoped namespaces."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            paths = []
            for package in ("one", "two"):
                path = f"Packages/{package}/Runtime/Thing.cs"
                file_path = root / path
                file_path.parent.mkdir(parents=True, exist_ok=True)
                file_path.write_text("namespace Demo.FileScoped;\npublic partial class Thing { }\n" + "x\n" * 700, encoding="utf-8")
                paths.append(path)
            totals = self.guards._partial_totals(root, paths)
            self.assertEqual(2, len(totals))
            self.assertEqual(702, totals["Packages/one|Demo.FileScoped|Thing"])

    def test_phase_paths_and_non_source_files(self) -> None:
        """Reject new phase prefixes, allow a frozen prefix, and ignore DLLs."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            paths = ["Scripts/phase197_tool.py", "Scripts/x/Phase197Thing/A.cs", "Scripts/phase185/neutral.py", "Scripts/phase197.dll"]
            for path in paths[:3]:
                write_lines(root / path, 1)
            baseline = {"files": {}, "partial_types": {}, "phase_paths": ["Scripts/phase185"]}
            errors = self.guards.violations(root, paths, baseline)
            self.assertIn("G3 phase_path=Scripts/phase197_tool.py", errors)
            self.assertIn("G3 phase_path=Scripts/x/Phase197Thing", errors)
            self.assertNotIn("Scripts/phase185", "\n".join(errors))

    def test_write_baseline_only_lowers_and_removes(self) -> None:
        """Lower an existing ceiling, remove a fixed entry, and never add a new one."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            old = root / "old.cs"
            new = root / "new.cs"
            write_lines(old, 1002)
            write_lines(new, 1001)
            baseline_path = root / "baseline.json"
            baseline_path.write_text(json.dumps({"files": {"old.cs": 1005, "gone.cs": 1001}, "partial_types": {}, "phase_paths": []}), encoding="utf-8")
            self.guards.write_baseline(root, ["old.cs", "new.cs"], baseline_path)
            result = json.loads(baseline_path.read_text(encoding="utf-8"))
            self.assertEqual({"old.cs": 1002}, result["files"])


if __name__ == "__main__":
    unittest.main()
