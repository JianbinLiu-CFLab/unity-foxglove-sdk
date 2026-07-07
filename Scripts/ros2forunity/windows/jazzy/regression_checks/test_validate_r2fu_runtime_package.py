#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Regression tests for R2FU runtime package validation gates.

from __future__ import annotations

import importlib.util
import sys
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[5]
VALIDATOR_PATH = ROOT / "Scripts" / "ros2forunity" / "windows" / "jazzy" / "validate_r2fu_runtime_package.py"


def load_validator_module():
    """Load the runtime package validator module under test."""
    spec = importlib.util.spec_from_file_location("validate_r2fu_runtime_package", VALIDATOR_PATH)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


class RuntimePackageValidatorTests(unittest.TestCase):
    """Regression coverage for release and public-doc validation."""

    def setUp(self) -> None:
        """Load a fresh validator module for each test."""
        self.validator = load_validator_module()

    def test_release_gate_blocks_candidate_runtime_inventory(self) -> None:
        """Release gate fails while redistributionStatus is candidate_not_published."""
        exit_code = self.validator.main(["--release-gate"])

        self.assertEqual(self.validator.EXIT_FAILURE, exit_code)

    def test_public_docs_must_include_artifact_hash(self) -> None:
        """Public docs validation rejects README/notices that omit the artifact hash."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp)
            readme = package / "README.md"
            notices = package / "THIRD_PARTY_NOTICES.md"
            package_json = package / "package.json"
            manifest = package / "runtime-manifest.json"
            artifact_sha = "a" * 64

            readme.write_text(
                "runtime.jazzy.win64 adapter combined Unity2Foxglove workflow\n"
                "Install only one runtime.* package\n",
                encoding="utf-8",
            )
            notices.write_text(artifact_sha, encoding="utf-8")
            package_json.write_text("{}", encoding="utf-8")
            manifest.write_text(f'{{"artifactSha256":"{artifact_sha}"}}', encoding="utf-8")

            self.validator.PACKAGE = package
            self.validator.PUBLIC_DOCS = (readme, notices, package_json, manifest)
            results = []

            self.validator.check_public_docs(results)

        failed = [result.name for result in results if not result.ok]
        self.assertIn("README documents artifact SHA-256", failed)

    def test_public_docs_missing_manifest_does_not_pass_empty_hash(self) -> None:
        """Missing manifest cannot make artifact hash checks pass through empty-string containment."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp)
            readme = package / "README.md"
            notices = package / "THIRD_PARTY_NOTICES.md"
            package_json = package / "package.json"
            missing_manifest = package / "missing-runtime-manifest.json"

            readme.write_text(
                "runtime.jazzy.win64 adapter combined Unity2Foxglove workflow\n"
                "Install only one ros2forunity.runtime. package\n",
                encoding="utf-8",
            )
            notices.write_text("notices without artifact hash", encoding="utf-8")
            package_json.write_text("{}", encoding="utf-8")

            self.validator.PACKAGE = package
            self.validator.PUBLIC_DOCS = (readme, notices, package_json, missing_manifest)
            self.validator.MANIFEST = missing_manifest
            results = []

            self.validator.check_public_docs(results)

        failed = [result.name for result in results if not result.ok]
        self.assertIn("README documents artifact SHA-256", failed)
        self.assertIn("THIRD_PARTY_NOTICES documents artifact SHA-256", failed)

    def test_public_docs_one_runtime_policy_accepts_plain_runtime_package_text(self) -> None:
        """The one-runtime policy check accepts stable wording, not only a glob literal."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp)
            readme = package / "README.md"
            notices = package / "THIRD_PARTY_NOTICES.md"
            package_json = package / "package.json"
            manifest = package / "runtime-manifest.json"
            artifact_sha = "b" * 64

            readme.write_text(
                "runtime.jazzy.win64 adapter combined Unity2Foxglove workflow\n"
                f"Install only one runtime packages entry. {artifact_sha}\n",
                encoding="utf-8",
            )
            notices.write_text(artifact_sha, encoding="utf-8")
            package_json.write_text("{}", encoding="utf-8")
            manifest.write_text(f'{{"artifactSha256":"{artifact_sha}"}}', encoding="utf-8")

            self.validator.PACKAGE = package
            self.validator.PUBLIC_DOCS = (readme, notices, package_json, manifest)
            self.validator.MANIFEST = manifest
            results = []

            self.validator.check_public_docs(results)

        one_runtime = [result for result in results if result.name == "README documents one-runtime policy"]
        self.assertTrue(one_runtime)
        self.assertTrue(one_runtime[0].ok)

    def test_unity_editor_using_guard_allows_blank_lines_and_comments(self) -> None:
        """The UnityEditor using guard accepts normal formatting inside the UNITY_EDITOR block."""
        with tempfile.TemporaryDirectory() as temp:
            runtime_root = Path(temp) / "Runtime" / "Ros2ForUnity"
            scripts = runtime_root / "Scripts"
            scripts.mkdir(parents=True)
            (scripts / "ROS2ForUnity.cs").write_text(
                "#if UNITY_EDITOR\n"
                "// editor-only package lookup\n"
                "using UnityEditor;\n"
                "UnityEditor.PackageManager.PackageInfo packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssetPath(\"Packages/demo\");\n"
                "#endif\n"
                "// Unity2Foxglove package path support\n"
                "const string packageName = \"dev.unity2foxglove.ros2forunity.runtime.jazzy.win64\";\n"
                "var resolvedPath = unity2FoxgloveRuntimePackageAssetPath;\n"
                "SetProcessEnvironmentVariable(GetEnvPathVariableName(), resolvedPath);\n"
                "Path.Combine(\"Packages\", \"Runtime\");\n"
                "Directory.Exists(packagePath);\n"
                "return assetPath;\n",
                encoding="utf-8",
            )

            self.validator.RUNTIME_ROOT = runtime_root
            results = []

            self.validator.check_package_path_patch(results)

        guard = [result for result in results if result.name == "UnityEditor using guarded"]
        self.assertTrue(guard)
        self.assertTrue(guard[0].ok)

    def test_runtime_source_declares_rmw_guard(self) -> None:
        """ROS2ForUnity startup path declares and enforces the expected RMW."""
        source = (
            self.validator.RUNTIME_ROOT
            / "Scripts"
            / "ROS2ForUnity.cs"
        ).read_text(encoding="utf-8", errors="replace")

        self.assertIn("expectedRmwImplementation", source)
        self.assertIn("ValidateRmwImplementation", source)
        self.assertIn("rmw_fastrtps_cpp", source)

    def test_generator_alignment_reports_missing_generator_as_failed_check(self) -> None:
        """Missing generator source should produce a structured failed result."""
        with tempfile.TemporaryDirectory() as temp:
            self.validator.ROOT = Path(temp)
            results = []

            self.validator.check_generator_alignment(results)

        self.assertFalse(results[0].ok)
        self.assertIn("generator script readable", results[0].name)


if __name__ == "__main__":
    unittest.main()
