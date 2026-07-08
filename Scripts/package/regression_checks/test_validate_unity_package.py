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


if __name__ == "__main__":
    unittest.main()
