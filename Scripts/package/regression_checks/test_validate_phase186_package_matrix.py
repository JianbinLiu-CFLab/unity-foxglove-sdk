#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Regression tests for the Phase186 package-matrix validator.

from __future__ import annotations

import importlib.util
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock


ROOT = Path(__file__).resolve().parents[3]
VALIDATOR_PATH = ROOT / "Scripts/package/validate_phase186_package_matrix.py"


def load_module(name: str, path: Path):
    """Load one validation script as an isolated module."""
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


class Phase186PackageMatrixTests(unittest.TestCase):
    """Lock failure evidence and assembly-reference boundary behavior."""

    def setUp(self) -> None:
        """Load a fresh validator for every test."""
        self.validator = load_module(
            "phase186_package_matrix_under_test",
            VALIDATOR_PATH,
        )

    def test_compile_failure_still_records_the_complete_matrix(self) -> None:
        """One failed composition must not suppress the remaining evidence."""
        outcomes = [
            subprocess.CompletedProcess([], 0, stdout="sdk ok"),
            subprocess.CompletedProcess([], 7, stdout="r2fu failed"),
            subprocess.CompletedProcess([], 0, stdout="bridge ok"),
            subprocess.CompletedProcess([], 0, stdout="all ok"),
        ]
        with tempfile.TemporaryDirectory() as temp:
            report = Path(temp) / "report.json"
            with mock.patch.object(self.validator, "REPORT", report), mock.patch.object(
                self.validator.subprocess,
                "run",
                side_effect=outcomes,
            ), mock.patch.object(
                self.validator,
                "validate_boundaries",
                return_value=["boundary"],
            ):
                self.assertEqual(1, self.validator.main())
            payload = json.loads(report.read_text(encoding="utf-8"))

        self.assertEqual("FAIL", payload["verdict"])
        self.assertEqual(4, len(payload["compileGates"]))
        self.assertEqual([0, 7, 0, 0], [row["exitCode"] for row in payload["compileGates"]])

    def test_guid_asmdef_reference_resolves_to_forbidden_assembly(self) -> None:
        """GUID-form references must enforce the same package boundary as names."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            forbidden_root = root / "bridge"
            forbidden_root.mkdir()
            forbidden = forbidden_root / "Bridge.asmdef"
            forbidden.write_text(
                '{"name":"Unity2Foxglove.Ros2Bridge"}',
                encoding="utf-8",
            )
            Path(str(forbidden) + ".meta").write_text(
                "fileFormatVersion: 2\nguid: 0123456789abcdef0123456789abcdef\n",
                encoding="utf-8",
            )
            consumer = root / "Consumer.asmdef"
            consumer.write_text(
                '{"name":"Consumer","references":'
                '["GUID:0123456789abcdef0123456789abcdef"]}',
                encoding="utf-8",
            )

            self.assertTrue(
                self.validator._references_forbidden_assembly(
                    consumer,
                    "Unity2Foxglove.Ros2Bridge",
                    forbidden_root,
                )
            )

    def test_named_child_asmdef_reference_resolves_to_forbidden_package(self) -> None:
        """Named references to a forbidden package's child assembly must be rejected."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            forbidden_root = root / "bridge"
            forbidden_root.mkdir()
            child = forbidden_root / "Bridge.Editor.asmdef"
            child.write_text(
                '{"name":"Unity2Foxglove.Ros2Bridge.Editor"}',
                encoding="utf-8",
            )
            consumer = root / "Consumer.asmdef"
            consumer.write_text(
                '{"name":"Consumer","references":'
                '["Unity2Foxglove.Ros2Bridge.Editor"]}',
                encoding="utf-8",
            )

            self.assertTrue(
                self.validator._references_forbidden_assembly(
                    consumer,
                    "Unity2Foxglove.Ros2Bridge",
                    forbidden_root,
                )
            )

    def test_guid_child_asmdef_reference_resolves_to_forbidden_package(self) -> None:
        """GUID references to a forbidden package's child assembly must be rejected."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            forbidden_root = root / "bridge"
            forbidden_root.mkdir()
            for filename, name, guid in (
                (
                    "Bridge.asmdef",
                    "Unity2Foxglove.Ros2Bridge",
                    "0123456789abcdef0123456789abcdef",
                ),
                (
                    "Bridge.Editor.asmdef",
                    "Unity2Foxglove.Ros2Bridge.Editor",
                    "fedcba9876543210fedcba9876543210",
                ),
            ):
                asmdef = forbidden_root / filename
                asmdef.write_text(json.dumps({"name": name}), encoding="utf-8")
                Path(str(asmdef) + ".meta").write_text(
                    f"fileFormatVersion: 2\nguid: {guid}\n",
                    encoding="utf-8",
                )
            consumer = root / "Consumer.asmdef"
            consumer.write_text(
                '{"name":"Consumer","references":'
                '["GUID:fedcba9876543210fedcba9876543210"]}',
                encoding="utf-8",
            )

            self.assertTrue(
                self.validator._references_forbidden_assembly(
                    consumer,
                    "Unity2Foxglove.Ros2Bridge",
                    forbidden_root,
                )
            )


if __name__ == "__main__":
    unittest.main()
