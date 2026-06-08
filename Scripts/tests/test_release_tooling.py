#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Regression tests for release helper correctness and diagnostics.

from __future__ import annotations

import importlib.util
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock


ROOT = Path(__file__).resolve().parents[2]
BUMP_VERSION_PATH = ROOT / "Scripts" / "release" / "bump_version.py"
RUN_CI_PATH = ROOT / "Scripts" / "release" / "run_ci.py"
VALIDATE_PACKAGE_PATH = ROOT / "Scripts" / "release" / "validate_package.py"


def load_module(name: str, path: Path):
    """Load a Python script as an isolated module."""
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


class VersionBumpTests(unittest.TestCase):
    """Regression coverage for release version synchronization."""

    def setUp(self) -> None:
        """Load a fresh bump_version module for each test."""
        self.bump_module = load_module("bump_version_under_test", BUMP_VERSION_PATH)

    def test_update_readme_keeps_two_newest_release_notes_without_corruption(self) -> None:
        """Trim old release notes without stale-offset deletion."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            readme = root / "README.md"
            readme.write_text(
                "\n".join(
                    [
                        "header",
                        "- [v1.9.4 release notes](docs/releases/RELEASE_NOTES_v1.9.4.md)",
                        "- [v1.9.3 release notes](docs/releases/RELEASE_NOTES_v1.9.3.md)",
                        "- [v1.9.2 release notes](docs/releases/RELEASE_NOTES_v1.9.2.md)",
                        "- [v1.9.1 release notes](docs/releases/RELEASE_NOTES_v1.9.1.md)",
                        "- [Release notes archive](docs/releases/)",
                        "footer",
                    ]
                )
                + "\n",
                encoding="utf-8",
            )

            bump = self.bump_module.VersionBump(root, "2.0.0", "2026-06-08", False)
            bump.update_readme("1.9.4")

            text = readme.read_text(encoding="utf-8")
            self.assertIn("- [v2.0.0 release notes](docs/releases/RELEASE_NOTES_v2.0.0.md)", text)
            self.assertIn("- [v1.9.4 release notes](docs/releases/RELEASE_NOTES_v1.9.4.md)", text)
            self.assertNotIn("RELEASE_NOTES_v1.9.3", text)
            self.assertNotIn("RELEASE_NOTES_v1.9.2", text)
            self.assertNotIn("RELEASE_NOTES_v1.9.1", text)
            self.assertIn("- [Release notes archive](docs/releases/)", text)
            self.assertIn("footer", text)

    def test_phase16_assertion_update_replaces_exactly_one_occurrence(self) -> None:
        """Multiple Phase16 version assertion hits should fail loudly."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            path = root / "Packages/dev.unity2foxglove.sdk/Tests/Runtime/Phase16Validation.cs"
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(
                '"\\"version\\": \\"1.2.3\\"" and duplicate "\\"version\\": \\"0.0.1\\""\n'
                "package.json version is 1.2.3\n"
                "comment package.json version is 0.0.1\n",
                encoding="utf-8",
            )

            bump = self.bump_module.VersionBump(root, "9.9.9", "2026-06-08", False)

            with self.assertRaises(ValueError):
                bump.update_phase16_assertion()


class RunCiTests(unittest.TestCase):
    """Regression coverage for local CI runner reliability."""

    def setUp(self) -> None:
        """Load a fresh run_ci module for each test."""
        self.run_ci = load_module("run_ci_under_test", RUN_CI_PATH)

    def test_run_ci_imports_under_current_python(self) -> None:
        """The CI runner module loads without f-string syntax failures."""
        self.assertTrue(hasattr(self.run_ci, "main"))

    def test_boundary_check_fails_when_git_command_fails(self) -> None:
        """Boundary checks must not pass when git itself fails."""
        failed = subprocess.CompletedProcess(args=["git"], returncode=128, stdout="", stderr="fatal")
        with mock.patch.object(self.run_ci.subprocess, "run", return_value=failed):
            self.assertFalse(self.run_ci._check_boundary())


class ValidatePackageTests(unittest.TestCase):
    """Regression coverage for package release validator hygiene checks."""

    def setUp(self) -> None:
        """Load a fresh validate_package module for each test."""
        self.validator = load_module("validate_package_under_test", VALIDATE_PACKAGE_PATH)

    def test_sample_meta_checks_asmdef_files(self) -> None:
        """Sample asmdef files need stable Unity .meta sidecars."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            sample = root / "Samples~" / "Demo"
            sample.mkdir(parents=True)
            (sample / "Demo.asmdef").write_text("{}", encoding="utf-8")

            self.validator.SAMPLES = root / "Samples~"
            results = []
            self.validator.check_sample_meta(results)

        self.assertFalse(results[-1].ok)
        self.assertIn("Demo.asmdef", results[-1].detail)

    def test_forbidden_sample_artifacts_reports_root_directory_once(self) -> None:
        """A forbidden directory should not flood diagnostics with descendants."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            library = root / "Samples~" / "BasicVisualization" / "Library"
            nested = library / "Sub"
            nested.mkdir(parents=True)
            (nested / "cache.bin").write_bytes(b"cache")

            self.validator.SAMPLES = root / "Samples~"
            results = []
            self.validator.check_forbidden_sample_artifacts(results)

        self.assertFalse(results[-1].ok)
        offenders = [item.strip() for item in results[-1].detail.split(";")]
        self.assertEqual(1, len(offenders))
        self.assertTrue(offenders[0].endswith("Samples~/BasicVisualization/Library"))


if __name__ == "__main__":
    unittest.main()
