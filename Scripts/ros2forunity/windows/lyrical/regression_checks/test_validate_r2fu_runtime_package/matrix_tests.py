"""Regression checks for Lyrical runtime package validation gates."""

from __future__ import annotations

from pathlib import Path

from .dependency_safety_tests import RuntimeDependencySafetyTests
from .foundation import *
from .layout_metadata_tests import RuntimeLayoutMetadataTests
from .public_boundary_tests import RuntimePublicBoundaryTests
from .release_gate_tests import RuntimeReleaseGateTests


class RuntimePackageValidatorTests(
    RuntimeReleaseGateTests,
    RuntimeLayoutMetadataTests,
    RuntimeDependencySafetyTests,
    RuntimePublicBoundaryTests,
    unittest.TestCase,
):
    """Regression coverage for release and public-doc validation."""

    def setUp(self) -> None:
        """Load a fresh validator module for each test."""
        self.validator = load_validator_module()

    def test_source_uses_named_split_package(self) -> None:
        """Require this suite to keep one named package with a runnable entrypoint."""
        module = __import__(
            "Scripts.ros2forunity.windows.lyrical.regression_checks.test_validate_r2fu_runtime_package",
            fromlist=["__file__"],
        )
        package = Path(module.__file__).with_suffix("")
        self.assertTrue(package.is_dir())
        self.assertTrue((package / "__main__.py").is_file())


__all__ = ["RuntimePackageValidatorTests"]
