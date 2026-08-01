#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Regression tests for Unity package validator hygiene checks.

from __future__ import annotations

import importlib.util
import subprocess
import sys
import tempfile
import unittest
import xml.etree.ElementTree as ET
from pathlib import Path
from unittest import mock


ROOT = Path(__file__).resolve().parents[3]
VALIDATE_PACKAGE_PATH = ROOT / "Scripts" / "package" / "validate_unity_package.py"
VALIDATE_SOURCE_GENERATOR_PATH = ROOT / "Scripts" / "package" / "validate_source_generator_dll.py"


def load_module(name: str, path: Path):
    """Load a Python script as an isolated module."""
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


class ValidatePackageTests(unittest.TestCase):
    """Regression coverage for package validator hygiene checks."""

    def setUp(self) -> None:
        """Load a fresh validate_unity_package module for each test."""
        self.validator = load_module("validate_unity_package_under_test", VALIDATE_PACKAGE_PATH)

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

    def test_sample_meta_checks_prefab_files(self) -> None:
        """Common Unity assets such as prefabs need stable .meta sidecars."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            sample = root / "Samples~" / "Demo"
            sample.mkdir(parents=True)
            (sample / "Robot.prefab").write_text("%YAML 1.1", encoding="utf-8")

            self.validator.SAMPLES = root / "Samples~"
            results = []
            self.validator.check_sample_meta(results)

        self.assertFalse(results[-1].ok)
        self.assertIn("Robot.prefab", results[-1].detail)

    def test_package_version_must_be_semver(self) -> None:
        """Unity package versions should be MAJOR.MINOR.PATCH."""
        results = []
        self.validator.check_package_identity(
            results,
            {
                "name": "dev.unity2foxglove.sdk",
                "displayName": "Unity2Foxglove SDK",
                "license": "Apache-2.0",
                "version": "1.0",
                "samples": [],
            },
        )

        version_result = next(item for item in results if item.name == "package version")
        self.assertFalse(version_result.ok)

    def test_dependent_package_version_pin_must_match_sdk(self) -> None:
        """Optional packages in the repo should depend on the current SDK version."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            gateway = root / "Packages" / "dev.unity2foxglove.remotegateway.win64"
            gateway.mkdir(parents=True)
            (gateway / "package.json").write_text(
                '{"dependencies":{"dev.unity2foxglove.sdk":"1.9.5"}}',
                encoding="utf-8",
            )

            self.validator.REMOTE_GATEWAY_PACKAGE = gateway
            results = []
            self.validator.check_dependent_package_versions(results, {"version": "1.9.6"})

        self.assertFalse(results[-1].ok)
        self.assertIn("1.9.5", results[-1].detail)
        self.assertIn("1.9.6", results[-1].detail)

    def test_dependent_package_version_pin_accepts_current_sdk(self) -> None:
        """The remote gateway package can be validated independently of install order."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            gateway = root / "Packages" / "dev.unity2foxglove.remotegateway.win64"
            gateway.mkdir(parents=True)
            (gateway / "package.json").write_text(
                '{"dependencies":{"dev.unity2foxglove.sdk":"1.9.6"}}',
                encoding="utf-8",
            )

            self.validator.REMOTE_GATEWAY_PACKAGE = gateway
            results = []
            self.validator.check_dependent_package_versions(results, {"version": "1.9.6"})

        self.assertTrue(results[-1].ok)

    def test_ros2_bridge_package_requires_the_duplex_sample_surface(self) -> None:
        """A distributable Bridge sample includes its behavior, builder, and guide."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp) / "dev.unity2foxglove.ros2bridge"
            package.mkdir()
            (package / "package.json").write_text(
                '{"name":"dev.unity2foxglove.ros2bridge",'
                '"version":"0.1.0-preview.1",'
                '"dependencies":{"dev.unity2foxglove.sdk":"1.9.6"},'
                '"samples":[{"displayName":"ROS2 Bridge Sample",'
                '"path":"Samples~/Ros2BridgeSample"}]}',
                encoding="utf-8",
            )
            old_required = (
                "Runtime/Unity2Foxglove.Ros2Bridge.asmdef",
                "Editor/Unity2Foxglove.Ros2Bridge.Editor.asmdef",
                "Tests/Unity2Foxglove.Ros2Bridge.Tests.asmdef",
                "Samples~/Ros2BridgeSample/Scenes/Ros2BridgeSample.unity",
                "Samples~/Ros2BridgeSample/Scripts/Unity2Foxglove.Ros2Bridge.Sample.asmdef",
                "Editor/SourceGenerators/analyzers/dotnet/cs/Unity2Foxglove.Ros2Bridge.FoxRunSourceGenerator.dll",
            )
            for relative in old_required:
                asset = package / relative
                asset.parent.mkdir(parents=True, exist_ok=True)
                asset.write_bytes(b"{}")
                Path(str(asset) + ".meta").write_text("guid: test\n", encoding="utf-8")

            self.validator.ROS2_BRIDGE_PACKAGE = package
            results = []
            self.validator.check_ros2_bridge_package(results)

        required = next(
            item for item in results
            if item.name == "ROS2 Bridge required assets and metas"
        )
        self.assertFalse(required.ok)
        self.assertIn("Ros2BridgeSampleDuplex.cs", required.detail)
        self.assertIn("Ros2BridgeSampleSceneBuilder.cs", required.detail)
        self.assertIn("PHASE186_BREAKING_UPGRADE.md", required.detail)

    def test_optional_package_boundaries_reject_remote_gateway_publish_sentinel(self) -> None:
        """Preview native packages should not carry stale publish-blocker sentinel text."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            gateway = root / "Packages" / "dev.unity2foxglove.remotegateway.win64"
            gateway.mkdir(parents=True)
            (gateway / "THIRD_PARTY_NOTICES.md").write_text(
                "Before publishing this package, regenerate notices.\n",
                encoding="utf-8",
            )

            self.validator.REMOTE_GATEWAY_PACKAGE = gateway
            self.validator.ROS2_RUNTIME_PACKAGES = ()
            self.validator.UNITY_DEMO_ASSETS = root / "Assets"
            results = []
            self.validator.check_optional_package_boundaries(results)

        self.assertFalse(results[0].ok)

    def test_optional_package_boundaries_require_ros2_runtime_conflicts(self) -> None:
        """Sibling ROS2 runtime packages share one assembly name and must declare conflicts."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            humble = root / "Packages" / "humble"
            jazzy = root / "Packages" / "jazzy"
            humble.mkdir(parents=True)
            jazzy.mkdir(parents=True)
            (humble / "package.json").write_text(
                '{"name":"humble","unity2foxgloveConflicts":["jazzy"]}',
                encoding="utf-8",
            )
            (jazzy / "package.json").write_text(
                '{"name":"jazzy"}',
                encoding="utf-8",
            )

            self.validator.REMOTE_GATEWAY_PACKAGE = root / "missing"
            self.validator.ROS2_RUNTIME_PACKAGES = (humble, jazzy)
            self.validator.UNITY_DEMO_ASSETS = root / "Assets"
            results = []
            self.validator.check_optional_package_boundaries(results)

        conflict_result = next(item for item in results if item.name == "ROS2 runtime package conflict metadata")
        self.assertFalse(conflict_result.ok)

    def test_optional_package_boundaries_reject_duplicate_demo_link_xml(self) -> None:
        """The demo project should rely on the package link.xml instead of a copied asset."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            assets = root / "Assets"
            assets.mkdir()
            (assets / "link.xml").write_text("<linker />", encoding="utf-8")

            self.validator.REMOTE_GATEWAY_PACKAGE = root / "missing"
            self.validator.ROS2_RUNTIME_PACKAGES = ()
            self.validator.UNITY_DEMO_ASSETS = assets
            results = []
            self.validator.check_optional_package_boundaries(results)

        link_result = next(item for item in results if item.name == "demo project avoids duplicate package link.xml")
        self.assertFalse(link_result.ok)

    def test_third_party_notice_requirements_cover_runtime_plugin_dlls(self) -> None:
        """Runtime plugin DLLs should be gated by explicit third-party notice tokens."""
        requirement_paths = {
            requirement[0].as_posix()
            for requirement in self.validator.THIRD_PARTY_NOTICE_REQUIREMENTS
        }

        expected = [
            "Runtime/Plugins/compression/K4os.Compression.LZ4.dll",
            "Runtime/Plugins/compression/K4os.Compression.LZ4.Streams.dll",
            "Runtime/Plugins/compression/K4os.Hash.xxHash.dll",
            "Runtime/Plugins/compression/System.IO.Pipelines.dll",
            "Runtime/Plugins/compression/ZstdSharp.dll",
            "Runtime/Plugins/StbImageWriteSharp.dll",
            "Runtime/Plugins/Windows/x86_64/Unity2FoxgloveDracoNative.dll",
        ]
        for suffix in expected:
            self.assertTrue(any(path.endswith(suffix) for path in requirement_paths), suffix)

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

    def test_forbidden_public_content_reports_all_labels_per_file(self) -> None:
        """One public file can violate multiple public-boundary patterns."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            samples = root / "Samples~"
            docs = root / "Documentation~"
            samples.mkdir()
            docs.mkdir()
            offender = samples / "README.md"
            offender.write_text("C:/Users/Alice/private\nTODO\n", encoding="utf-8")

            self.validator.SAMPLES = samples
            self.validator.DOCS = docs
            self.validator.PACKAGE = root
            results = []
            self.validator.check_forbidden_public_content(results)

        self.assertFalse(results[-1].ok)
        self.assertIn("local Windows path", results[-1].detail)
        self.assertIn("to-do marker", results[-1].detail)

    def test_manual_phase_service_guard_ignores_commented_demo(self) -> None:
        """Commented manual smoke services remain inert and allowed."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            demo = root / "FoxRun"
            demo.mkdir()
            source = demo / "FoxService141DManualSmoke.cs"
            source.write_text('//[FoxService("/phase141d/manual_dto")]\n', encoding="utf-8")

            results = []
            self.validator.check_manual_phase_service_guards(results, list(demo.rglob("*.cs")))

        self.assertTrue(results[-1].ok)

    def test_manual_phase_service_guard_rejects_active_demo(self) -> None:
        """Phase-only manual services should not be accidentally committed active."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            demo = root / "FoxRun"
            demo.mkdir()
            source = demo / "FoxService141DManualSmoke.cs"
            source.write_text('[FoxService("/phase141d/manual_dto")]\n', encoding="utf-8")

            results = []
            self.validator.check_manual_phase_service_guards(results, list(demo.rglob("*.cs")))

        self.assertFalse(results[-1].ok)
        self.assertIn("FoxService141DManualSmoke.cs:1", results[-1].detail)

    def test_validation_naming_allows_legacy_phase_files(self) -> None:
        """Existing Phase-prefixed validation files remain grandfathered."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            runtime = root / "Tests" / "Runtime"
            runtime.mkdir(parents=True)
            legacy = runtime / "Phase164_57Validation.cs"
            legacy.write_text("// legacy validation\n", encoding="utf-8")
            legacy_variant = runtime / "Phase164_57FooValidation.cs"
            legacy_variant.write_text("// legacy validation variant\n", encoding="utf-8")

            self.validator.PACKAGE = root
            results = []
            self.validator.check_validation_naming(results)

        self.assertTrue(results[-1].ok)

    def test_validation_naming_rejects_exact_cutoff_phase_files(self) -> None:
        """The exact cutoff phase/index should not slip through an off-by-one gap."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            runtime = root / "Tests" / "Runtime"
            runtime.mkdir(parents=True)
            cutoff = runtime / "Phase164_58Validation.cs"
            cutoff.write_text("// cutoff validation\n", encoding="utf-8")
            cutoff_variant = runtime / "Phase164_58FooValidation.cs"
            cutoff_variant.write_text("// cutoff validation variant\n", encoding="utf-8")

            self.validator.PACKAGE = root
            results = []
            self.validator.check_validation_naming(results)

        self.assertFalse(results[-1].ok)
        self.assertIn("Phase164_58Validation.cs", results[-1].detail)
        self.assertIn("Phase164_58FooValidation.cs", results[-1].detail)

    def test_validation_naming_rejects_new_phase_files(self) -> None:
        """New validations should use descriptive filenames instead of Phase numbers."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            runtime = root / "Tests" / "Runtime"
            runtime.mkdir(parents=True)
            offender = runtime / "Phase164_59Validation.cs"
            offender.write_text("// new validation\n", encoding="utf-8")
            variant = runtime / "Phase164_59FooValidation.cs"
            variant.write_text("// new validation variant\n", encoding="utf-8")

            self.validator.PACKAGE = root
            results = []
            self.validator.check_validation_naming(results)

        self.assertFalse(results[-1].ok)
        self.assertIn("Phase164_59Validation.cs", results[-1].detail)
        self.assertIn("Phase164_59FooValidation.cs", results[-1].detail)


class ValidateSourceGeneratorDllTests(unittest.TestCase):
    """Regression coverage for source generator DLL validator diagnostics."""

    def setUp(self) -> None:
        """Load a fresh validate_source_generator_dll module for each test."""
        self.validator = load_module("validate_source_generator_dll_under_test", VALIDATE_SOURCE_GENERATOR_PATH)

    def test_build_failure_returns_structured_failure(self) -> None:
        """A failed dotnet build should not surface as a Python traceback."""
        failed = subprocess.CalledProcessError(returncode=9, cmd=["dotnet", "build"])
        with mock.patch.object(self.validator.subprocess, "run", side_effect=failed):
            with mock.patch("sys.stderr") as stderr:
                self.assertFalse(self.validator.run_build())

        written = "".join(call.args[0] for call in stderr.write.call_args_list if call.args)
        self.assertIn("[FAIL] Source generator Release build failed", written)

    def test_provider_analyzers_use_owned_explicit_sources_and_roslyn_only_dependencies(self) -> None:
        """Provider analyzers must not compile core trees or carry runtime codec dependencies."""
        projects = (
            ROOT
            / "Packages/dev.unity2foxglove.ros2forunity/Editor/SourceGenerators/FoxRunR2fuSourceGenerator.csproj",
            ROOT
            / "Packages/dev.unity2foxglove.ros2bridge/Editor/SourceGenerators/FoxRunBridgeSourceGenerator.csproj",
        )
        for project in projects:
            package_root = project.parents[2].resolve()
            root = ET.parse(project).getroot()
            dependencies = {
                node.attrib["Include"]
                for node in root.findall(".//PackageReference")
            }
            self.assertEqual(
                {
                    "Microsoft.CodeAnalysis.Analyzers",
                    "Microsoft.CodeAnalysis.CSharp",
                },
                dependencies,
                project,
            )
            self.assertFalse(root.findall(".//ProjectReference"), project)
            self.assertFalse(root.findall(".//Reference"), project)
            for node in root.findall(".//Compile"):
                for include in node.attrib.get("Include", "").split(";"):
                    include = include.strip()
                    if not include:
                        continue
                    self.assertNotIn("*", include, project)
                    source = (project.parent / include).resolve()
                    self.assertTrue(
                        source.is_relative_to(package_root),
                        f"{project}: non-owned source {include}",
                    )

    def test_validator_exposes_the_locked_provider_contract_gate(self) -> None:
        """Freshness must include dependencies, ledgers, IDs, hint parity, and analyzer sets."""
        self.assertTrue(self.validator.validate_analyzer_contracts(("core", "r2fu", "ros2bridge")))

    def test_missing_analyzer_dependency_is_reported_before_hash_comparison(self) -> None:
        """A source generator dependency must ship beside the analyzer DLL for Unity to load it."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            build = root / "build"
            build.mkdir()
            (build / "FoxgloveLogSourceGenerator.dll").write_bytes(b"generator")

            with mock.patch.object(self.validator, "run_build", return_value=True):
                with mock.patch.object(
                    self.validator,
                    "CHECKED_IN_ARTIFACTS",
                    {
                        "FoxgloveLogSourceGenerator.dll": root / "checked" / "FoxgloveLogSourceGenerator.dll",
                        "Google.Protobuf.dll": root / "checked" / "Google.Protobuf.dll",
                    },
                ):
                    with mock.patch("sys.stderr") as stderr:
                        result = self.validator.validate_or_update(False, build, [])

        self.assertEqual(1, result)
        written = "".join(call.args[0] for call in stderr.write.call_args_list if call.args)
        self.assertIn("Google.Protobuf.dll", written)
        self.assertIn("did not produce", written)

    def test_runtime_protobuf_plugin_must_match_checked_in_analyzer_dependency(self) -> None:
        """The Unity runtime plug-in must stay in lockstep with analyzer Google.Protobuf."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            build = root / "build"
            checked = root / "checked"
            build.mkdir()
            checked.mkdir()
            (build / "FoxgloveLogSourceGenerator.dll").write_bytes(b"generator")
            (build / "Google.Protobuf.dll").write_bytes(b"protobuf-3.29.3")
            (checked / "FoxgloveLogSourceGenerator.dll").write_bytes(b"generator")
            (checked / "Google.Protobuf.dll").write_bytes(b"protobuf-3.29.3")
            runtime_plugin = root / "Google.Protobuf.runtime.dll"
            runtime_plugin.write_bytes(b"protobuf-mismatch")

            with mock.patch.object(self.validator, "run_build", return_value=True):
                with mock.patch.object(
                    self.validator,
                    "CHECKED_IN_ARTIFACTS",
                    {
                        "FoxgloveLogSourceGenerator.dll": checked / "FoxgloveLogSourceGenerator.dll",
                        "Google.Protobuf.dll": checked / "Google.Protobuf.dll",
                    },
                ):
                    with mock.patch.object(
                        self.validator,
                        "UNITY_PLUGIN_GOOGLE_PROTOBUF",
                        runtime_plugin,
                        create=True,
                    ):
                        with mock.patch("sys.stderr") as stderr:
                            result = self.validator.validate_or_update(False, build, [])

        self.assertEqual(1, result)
        written = "".join(call.args[0] for call in stderr.write.call_args_list if call.args)
        self.assertIn("Unity runtime Google.Protobuf plug-in", written)

    def test_runtime_protobuf_mismatch_blocks_analyzer_update_before_copy(self) -> None:
        """A mismatched runtime plug-in must not leave an --update half-applied."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            build = root / "build"
            checked = root / "checked"
            build.mkdir()
            checked.mkdir()
            (build / "FoxgloveLogSourceGenerator.dll").write_bytes(b"fresh-generator")
            (build / "Google.Protobuf.dll").write_bytes(b"fresh-protobuf")
            checked_generator = checked / "FoxgloveLogSourceGenerator.dll"
            checked_generator.write_bytes(b"old-generator")
            (checked / "Google.Protobuf.dll").write_bytes(b"old-protobuf")
            runtime_plugin = root / "Google.Protobuf.runtime.dll"
            runtime_plugin.write_bytes(b"protobuf-mismatch")

            with mock.patch.object(self.validator, "run_build", return_value=True):
                with mock.patch.object(
                    self.validator,
                    "CHECKED_IN_ARTIFACTS",
                    {
                        "FoxgloveLogSourceGenerator.dll": checked_generator,
                        "Google.Protobuf.dll": checked / "Google.Protobuf.dll",
                    },
                ):
                    with mock.patch.object(
                        self.validator,
                        "UNITY_PLUGIN_GOOGLE_PROTOBUF",
                        runtime_plugin,
                    ):
                        with mock.patch("sys.stderr"):
                            result = self.validator.validate_or_update(True, build, [])
            persisted_generator = checked_generator.read_bytes()

        self.assertEqual(1, result)
        self.assertEqual(b"old-generator", persisted_generator)


if __name__ == "__main__":
    unittest.main()
