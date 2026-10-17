"""Regression checks for Unity package validator hygiene."""

from __future__ import annotations

from pathlib import Path

from .content_policy_tests import PackageContentPolicyTests
from .foundation import *
from .notice_inventory_tests import PackageNoticeInventoryTests
from .package_structure_tests import PackageStructureTests
from .platform_boundary_tests import PackagePlatformBoundaryTests
from .source_generator_artifact_tests import SourceGeneratorArtifactTests
from .source_generator_contract_tests import SourceGeneratorContractTests
from .source_generator_parity_tests import SourceGeneratorParityTests


class ValidatePackageTests(
    PackagePlatformBoundaryTests,
    PackageStructureTests,
    PackageNoticeInventoryTests,
    PackageContentPolicyTests,
    unittest.TestCase,
):
    """Regression coverage for package validator hygiene checks."""

    def setUp(self) -> None:
        """Load a fresh validate_unity_package module for each test."""
        self.validator = load_module("validate_unity_package_under_test", VALIDATE_PACKAGE_PATH)

    def test_source_uses_named_split_package(self) -> None:
        """Require this suite to keep one named package with a runnable entrypoint."""
        module = __import__(
            "Scripts.package.regression_checks.test_validate_unity_package",
            fromlist=["__file__"],
        )
        package = Path(module.__file__).with_suffix("")
        self.assertTrue(package.is_dir())
        self.assertTrue((package / "__main__.py").is_file())


class ValidateSourceGeneratorDllTests(
    SourceGeneratorContractTests,
    SourceGeneratorParityTests,
    SourceGeneratorArtifactTests,
    unittest.TestCase,
):
    """Regression coverage for source-generator artifact validation."""

    def setUp(self) -> None:
        """Load a fresh source-generator validator for each test."""
        self.validator = load_module("validate_source_generator_dll_under_test", VALIDATE_SOURCE_GENERATOR_PATH)


__all__ = ["ValidatePackageTests", "ValidateSourceGeneratorDllTests"]
