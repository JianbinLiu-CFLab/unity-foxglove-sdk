#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Regression tests for architecture analysis helpers.

from __future__ import annotations

import contextlib
import importlib.util
import io
import json
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock


ROOT = Path(__file__).resolve().parents[2]


def load_module(name: str, relative: str):
    """Load one repository helper script as an isolated module."""
    path = ROOT / relative
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    sys.modules[spec.name] = module
    original_path = list(sys.path)
    sys.path.insert(0, str(path.parent))
    try:
        spec.loader.exec_module(module)
    except Exception:
        sys.modules.pop(spec.name, None)
        raise
    finally:
        sys.path[:] = original_path
    return module


class ArchitectureToolingTests(unittest.TestCase):
    """Regression coverage for architecture helper tooling."""

    def test_asmdef_cycle_detection_handles_deep_graphs_without_recursion_error(self) -> None:
        """Architecture coupling analysis should tolerate deep acyclic graphs."""
        module = load_module("analyze_coupling_under_test", "Scripts/architecture/analyze_coupling.py")
        metrics = [
            module.AsmdefMetric(path=f"{index}.asmdef", name=f"A{index}", references=[f"A{index + 1}"])
            for index in range(1100)
        ]
        metrics.append(module.AsmdefMetric(path="1100.asmdef", name="A1100", references=[]))

        cycles = module.find_asmdef_cycles(metrics)

        self.assertEqual([], cycles)

    def test_registry_default_test_parse_warns_when_registry_shape_is_unrecognized(self) -> None:
        """A registry parse miss should be visible rather than disabling boundary checks."""
        module = load_module("analyze_coupling_registry_under_test", "Scripts/architecture/analyze_coupling.py")

        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            registry = root / "Packages/dev.unity2foxglove.sdk/Tests/Runtime/PhaseValidationRegistry.cs"
            registry.parent.mkdir(parents=True)
            registry.write_text("DefaultValidation(typeof(Phase1Validation));\n", encoding="utf-8")
            stderr = io.StringIO()
            with contextlib.redirect_stderr(stderr):
                files = module.find_registry_default_test_files(root)

        self.assertEqual(set(), files)
        self.assertIn("warning", stderr.getvalue().lower())

    def test_asmdef_collection_reports_non_object_json_without_crashing(self) -> None:
        """Syntactically valid non-object JSON is still an invalid asmdef input."""
        module = load_module("analyze_coupling_asmdef_under_test", "Scripts/architecture/analyze_coupling.py")

        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            asmdef = root / "Packages" / "Broken.asmdef"
            asmdef.parent.mkdir(parents=True)
            asmdef.write_text("[]\n", encoding="utf-8")

            metrics = module.collect_asmdef_metrics(root, ["Packages/Broken.asmdef"])

        self.assertEqual(1, len(metrics))
        self.assertEqual("<invalid-json-object>", metrics[0].name)
        self.assertEqual([], metrics[0].references)

    def test_read_text_rejects_lossy_utf8(self) -> None:
        """Corrupt tracked bytes must fail instead of being replaced."""
        module = load_module("analyze_coupling_strict_text_under_test", "Scripts/architecture/analyze_coupling.py")

        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / "broken.cs"
            path.write_bytes(b"namespace Valid { \xff }\n")
            with self.assertRaises(UnicodeDecodeError):
                module.read_text(Path(temp), "broken.cs")

    def test_asmdef_collection_rejects_invalid_schema_shapes(self) -> None:
        """Asmdef names/references must retain their declared JSON types."""
        module = load_module("analyze_coupling_asmdef_schema_under_test", "Scripts/architecture/analyze_coupling.py")

        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            paths = []
            cases = {
                "missing-name.asmdef": {"references": []},
                "numeric-name.asmdef": {"name": 7, "references": []},
                "string-refs.asmdef": {"name": "A", "references": "B"},
                "nonstring-ref.asmdef": {"name": "A", "references": [3]},
            }
            for filename, payload in cases.items():
                path = root / filename
                path.write_text(json.dumps(payload), encoding="utf-8")
                paths.append(filename)

            metrics = module.collect_asmdef_metrics(root, paths)

        self.assertEqual(4, len(metrics))
        self.assertTrue(all(item.name == "<invalid-schema>" for item in metrics))

    def test_write_output_publishes_complete_report_atomically(self) -> None:
        """Output publication keeps the destination complete across replacement."""
        module = load_module("analyze_coupling_atomic_output_under_test", "Scripts/architecture/analyze_coupling.py")

        with tempfile.TemporaryDirectory() as temp:
            output = Path(temp) / "report.json"
            output.write_text("OLD-COMPLETE\n", encoding="utf-8")
            module.write_output("NEW-COMPLETE\n", str(output))
            self.assertEqual("NEW-COMPLETE\n", output.read_text(encoding="utf-8"))
            self.assertFalse(any(output.parent.glob(".*.tmp")))

    def test_git_ls_files_preserves_newline_and_quote_paths(self) -> None:
        """NUL-delimited git output keeps literal path identities intact."""
        module = load_module("analyze_coupling_git_paths_under_test", "Scripts/architecture/analyze_coupling.py")

        class Result:
            returncode = 0
            stdout = b"dir/new\nline.cs\0dir/quote\"name.cs\0"
            stderr = b""

        with mock.patch.object(module.subprocess, "run", return_value=Result()) as run:
            paths = module.run_git_ls_files(Path("."))

        self.assertEqual(["dir/new\nline.cs", 'dir/quote"name.cs'], paths)
        self.assertIn("-z", run.call_args.args[0])

    def test_read_text_rejects_source_changed_during_read(self) -> None:
        """A report must not mix bytes when a source mutates mid-read."""
        module = load_module("analyze_coupling_mutating_source_under_test", "Scripts/architecture/analyze_coupling.py")

        class FakePath:
            def __init__(self):
                self._stats = iter(((1, 3, 9), (2, 3, 9)))

            def stat(self):
                class S:
                    pass
                s = S()
                s.st_mtime_ns, s.st_size, s.st_ino = next(self._stats)
                return s

            def read_bytes(self):
                return b"abc"

        class FakeRoot:
            def __truediv__(self, _relative):
                return FakePath()

        with self.assertRaises(RuntimeError):
            module.read_text(FakeRoot(), "changed.cs")

    def test_report_flags_root_developer_meta_as_a_private_boundary(self) -> None:
        """Architecture reporting must include a tracked root Developer.meta."""
        module = load_module(
            "analyze_coupling_root_meta_under_test",
            "Scripts/architecture/analyze_coupling.py",
        )

        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            (root / "Developer.meta").write_text(
                "fileFormatVersion: 2\n",
                encoding="utf-8",
            )

            def fake_ls_files(_root, *pathspecs):
                """Return the root companion only for the complete tracked universe."""
                return [] if pathspecs else ["Developer.meta"]

            with mock.patch.object(
                module,
                "run_git_ls_files",
                side_effect=fake_ls_files,
            ):
                report = module.build_report(
                    root,
                    module.argparse.Namespace(
                        include_generated=False,
                        hotspot_limit=20,
                    ),
                )

        self.assertEqual(
            ["Developer.meta"],
            report["tracked_nested_developer_paths"],
        )


if __name__ == "__main__":
    unittest.main()
