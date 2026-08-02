#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Regression tests for the optional ROS2 For Unity package validator.

from __future__ import annotations

import importlib.util
import json
import sys
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[5]
VALIDATOR_PATH = ROOT / "Scripts" / "ros2forunity" / "windows" / "humble" / "validate_ros2forunity_package.py"


def load_validator_module():
    """Load the optional package validator module under test."""
    spec = importlib.util.spec_from_file_location("validate_ros2forunity_package", VALIDATOR_PATH)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


class Ros2ForUnityPackageValidatorTests(unittest.TestCase):
    """Regression coverage for optional package validator edge cases."""

    def setUp(self) -> None:
        """Load a fresh validator module for each test."""
        self.validator = load_validator_module()

    def configure_package_paths(self, root: Path) -> None:
        """Redirect validator globals to a temporary package layout."""
        package = root / "Packages" / "dev.unity2foxglove.ros2forunity"
        runtime = root / "Packages" / self.validator.RUNTIME_PACKAGE_NAME
        self.validator.ROOT = root
        self.validator.PACKAGE = package
        self.validator.MANIFEST = package / "Compliance" / "ros2-for-unity-adoption-manifest.json"
        self.validator.RUNTIME_INVENTORY = package / "Compliance" / "r2fu-humble-win64-runtime-inventory.json"
        self.validator.RUNTIME_PACKAGE = runtime
        self.validator.RUNTIME_NOTICES = runtime / "THIRD_PARTY_NOTICES.md"
        self.validator.ADAPTER_SAMPLE = package / "Samples~" / "ROS2 For Unity External Adapter"
        self.validator.RVIZ_SAMPLE = package / "Samples~" / "RViz2 Standard Visualization Acceptance"
        self.validator.RVIZ_POINTCLOUD2_SAMPLE = package / "Samples~" / "RViz2 PointCloud2 Acceptance"
        self.validator.RVIZ_MARKERARRAY_SAMPLE = package / "Samples~" / "RViz2 MarkerArray Acceptance"
        self.validator.RVIZ_V1_SAMPLE = package / "Samples~" / "RViz2 Standard Visualization v1"

    def test_main_clears_file_caches_before_running_checks(self) -> None:
        """The validator entrypoint should reset cached file reads before checks."""
        self.validator.JSON_CACHE[Path("stale.json")] = {"stale": True}
        self.validator.TEXT_CACHE[Path("stale.md")] = "stale"

        def cache_check(results):
            """Observe that caches were cleared before the first check runs."""
            self.assertEqual({}, self.validator.JSON_CACHE)
            self.assertEqual({}, self.validator.TEXT_CACHE)
            results.append(self.validator.CheckResult("cache clear observed", True, ""))

        self.validator.check_package_metadata = cache_check
        self.validator.check_required_files = lambda results: None
        self.validator.check_manifest = lambda results: None
        self.validator.check_runtime_inventory = lambda results: None
        self.validator.check_text_boundaries = lambda results: None
        self.validator.check_no_runtime_artifacts = lambda results: None
        self.validator.check_sample_source_boundary = lambda results: None
        self.validator.check_runtime_source_boundary = lambda results: None
        self.validator.check_core_boundary = lambda results: None

        self.assertEqual(self.validator.EXIT_SUCCESS, self.validator.main())

    def test_editor_asmdef_allows_implicit_auto_referenced_default(self) -> None:
        """Missing autoReferenced should keep Unity's implicit true default."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            self.configure_package_paths(root)
            editor = self.validator.PACKAGE / "Editor"
            editor.mkdir(parents=True)
            (editor / "Unity2Foxglove.Ros2ForUnity.Editor.asmdef").write_text(
                '{"includePlatforms":["Editor"]}',
                encoding="utf-8",
            )
            (editor / "Ros2ForUnityRuntimeDefineInstaller.cs").write_text(
                "UNITY2FOXGLOVE_ROS2_FOR_UNITY Ros2ForUnityRuntimeSelection.GetStatus() "
                "NamedBuildTarget.Standalone",
                encoding="utf-8",
            )
            (editor / "Ros2ForUnityRuntimeSelection.cs").write_text(
                "RuntimePackagePrefix DiscoverCandidateRuntimes UNITY2FOXGLOVE_ROS2_FOR_UNITY",
                encoding="utf-8",
            )
            results = []

            self.validator.check_no_runtime_artifacts(results)

        surface = next(result for result in results if result.name == "optional package editor surface only enables runtime compile symbol")
        self.assertTrue(surface.ok)

    def test_editor_asmdef_rejects_explicit_auto_referenced_false(self) -> None:
        """Explicit autoReferenced=false should fail the editor asmdef surface check."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            self.configure_package_paths(root)
            editor = self.validator.PACKAGE / "Editor"
            editor.mkdir(parents=True)
            (editor / "Unity2Foxglove.Ros2ForUnity.Editor.asmdef").write_text(
                '{"includePlatforms":["Editor"],"autoReferenced":false}',
                encoding="utf-8",
            )
            (editor / "Ros2ForUnityRuntimeDefineInstaller.cs").write_text(
                "UNITY2FOXGLOVE_ROS2_FOR_UNITY Ros2ForUnityRuntimeSelection.GetStatus() "
                "NamedBuildTarget.Standalone",
                encoding="utf-8",
            )
            (editor / "Ros2ForUnityRuntimeSelection.cs").write_text(
                "RuntimePackagePrefix DiscoverCandidateRuntimes UNITY2FOXGLOVE_ROS2_FOR_UNITY",
                encoding="utf-8",
            )
            results = []

            self.validator.check_no_runtime_artifacts(results)

        surface = next(result for result in results if result.name == "optional package editor surface only enables runtime compile symbol")
        self.assertFalse(surface.ok)

    def test_exact_editor_source_generator_artifacts_are_allowed(self) -> None:
        """The package may ship its exact Editor-only source generator toolchain."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            self.configure_package_paths(root)
            editor = self.validator.PACKAGE / "Editor"
            source_generators = editor / "SourceGenerators"
            analyzer = (
                source_generators
                / "analyzers"
                / "dotnet"
                / "cs"
                / "Unity2Foxglove.Ros2ForUnity.FoxRunSourceGenerator.dll"
            )
            analyzer.parent.mkdir(parents=True)
            analyzer.write_bytes(b"analyzer")
            (source_generators / "AnalyzerReleases.Shipped.md").write_text(
                "# Shipped", encoding="utf-8"
            )
            (source_generators / "Directory.Build.props").write_text(
                "<Project />", encoding="utf-8"
            )
            (source_generators / "FoxRunR2fuSourceGenerator.csproj").write_text(
                "<Project />", encoding="utf-8"
            )
            (editor / "Unity2Foxglove.Ros2ForUnity.Editor.asmdef").write_text(
                '{"includePlatforms":["Editor"]}', encoding="utf-8"
            )
            (editor / "Ros2ForUnityRuntimeDefineInstaller.cs").write_text(
                "UNITY2FOXGLOVE_ROS2_FOR_UNITY Ros2ForUnityRuntimeSelection.GetStatus() "
                "NamedBuildTarget.Standalone",
                encoding="utf-8",
            )
            (editor / "Ros2ForUnityRuntimeSelection.cs").write_text(
                "RuntimePackagePrefix DiscoverCandidateRuntimes UNITY2FOXGLOVE_ROS2_FOR_UNITY",
                encoding="utf-8",
            )
            results = []

            self.validator.check_no_runtime_artifacts(results)

        no_binaries = next(
            result
            for result in results
            if result.name == "optional package has no runtime binaries"
        )
        surface = next(
            result
            for result in results
            if result.name
            == "optional package editor surface only enables runtime compile symbol"
        )
        self.assertTrue(no_binaries.ok)
        self.assertTrue(surface.ok)

    def test_binary_outside_exact_editor_analyzer_path_remains_rejected(self) -> None:
        """A similarly named binary elsewhere must not bypass the source-generator exception."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            self.configure_package_paths(root)
            misplaced = self.validator.PACKAGE / "Editor" / "CopiedGenerator.dll"
            misplaced.parent.mkdir(parents=True)
            misplaced.write_bytes(b"not approved")
            results = []

            self.validator.check_no_runtime_artifacts(results)

        no_binaries = next(
            result
            for result in results
            if result.name == "optional package has no runtime binaries"
        )
        self.assertFalse(no_binaries.ok)
        self.assertIn("CopiedGenerator.dll", no_binaries.detail)

    def test_public_phase_scan_covers_rviz_sample_readmes_and_deduplicates_hits(self) -> None:
        """Public docs scanning should include RViz samples and report unique phase tokens."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            self.configure_package_paths(root)
            for path in (
                self.validator.PACKAGE / "README.md",
                self.validator.PACKAGE / "THIRD_PARTY_NOTICES.md",
                self.validator.ADAPTER_SAMPLE / "README.md",
                self.validator.RVIZ_POINTCLOUD2_SAMPLE / "README.md",
                self.validator.RVIZ_MARKERARRAY_SAMPLE / "README.md",
                self.validator.RVIZ_V1_SAMPLE / "README.md",
                self.validator.RUNTIME_NOTICES,
            ):
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text("", encoding="utf-8")
            (self.validator.RVIZ_SAMPLE / "README.md").parent.mkdir(parents=True, exist_ok=True)
            (self.validator.RVIZ_SAMPLE / "README.md").write_text("Leaked Phase110 token", encoding="utf-8")
            results = []

            self.validator.check_text_boundaries(results)

        phase_result = next(result for result in results if result.name == "public R2FU docs avoid internal phase names")
        self.assertFalse(phase_result.ok)
        self.assertEqual("Phase110", phase_result.detail)

    def test_runtime_inventory_file_count_must_be_present(self) -> None:
        """Runtime inventory file entries should require a declared fileCount."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            self.configure_package_paths(root)
            self.validator.RUNTIME_INVENTORY.parent.mkdir(parents=True)
            self.validator.RUNTIME_INVENTORY.write_text(
                '{"files":[{"path":"rcl.dll","sha256":"abc"}]}',
                encoding="utf-8",
            )
            results = []

            self.validator.check_runtime_inventory(results)

        entries = next(result for result in results if result.name == "runtime inventory file entries")
        self.assertFalse(entries.ok)

    def test_runtime_inventory_uses_the_current_default_rmw_schema_field(self) -> None:
        """A refreshed runtime inventory records its default RMW as `defaultRmwImplementation`."""

        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            self.configure_package_paths(root)
            self.validator.RUNTIME_INVENTORY.parent.mkdir(parents=True, exist_ok=True)
            self.validator.MANIFEST.parent.mkdir(parents=True, exist_ok=True)
            self.validator.RUNTIME_INVENTORY.write_text(
                json.dumps(
                    {
                        "schemaVersion": 1,
                        "runtimeId": self.validator.RUNTIME_ID,
                        "artifactName": self.validator.RUNTIME_ARTIFACT,
                        "rosDistro": "humble",
                        "defaultRmwImplementation": "rmw_fastrtps_cpp",
                        "platform": "win64",
                        "buildType": "standalone",
                        "redistributionStatus": "candidate_not_published",
                        "artifactSize": 1,
                        "sha256": self.validator.RUNTIME_SHA256,
                        "fileCount": 1,
                        "categoryCounts": {
                            "native_libraries": 901,
                            "managed_assemblies": 2,
                            "generated_message_assemblies": 40,
                        },
                        "knownCriticalFiles": [
                            {"name": "rcl.dll", "present": True},
                            {"name": "yaml.dll", "present": True},
                            {"name": "spdlog.dll", "present": True},
                            {"name": "fmt.dll", "present": False},
                        ],
                        "detectedComponents": [
                            {"name": "ros2cs"},
                            {"name": "Fast DDS"},
                            {"name": "RMW FastRTPS"},
                            {"name": "Pixi runtime closure DLLs"},
                        ],
                        "files": [{"path": "rcl.dll", "sha256": "abc"}],
                    }
                ),
                encoding="utf-8",
            )
            self.validator.MANIFEST.write_text(
                json.dumps(
                    {
                        "supportedRuntimePackages": [
                            {
                                "packageName": self.validator.RUNTIME_PACKAGE_NAME,
                                "artifactSize": 1,
                                "artifactSha256": self.validator.RUNTIME_SHA256,
                                "inventoryFileCount": 1,
                            }
                        ]
                    }
                ),
                encoding="utf-8",
            )
            results = []

            self.validator.check_runtime_inventory(results)

        rmw = next(result for result in results if result.name == "runtime inventory defaultRmwImplementation")
        self.assertTrue(rmw.ok)


if __name__ == "__main__":
    unittest.main()
