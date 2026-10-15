"""Regression checks for the Phase186 package-matrix validator."""

from __future__ import annotations

from pathlib import Path

from .foundation import *
from .analyzer_metadata_tests import Phase186AnalyzerMetadataTests
from .cross_package_boundary_tests import Phase186CrossPackageBoundaryTests
from .package_manifest_tests import Phase186PackageManifestTests
from .assembly_reference_tests import Phase186AssemblyReferenceTests


class Phase186PackageMatrixTests(
    Phase186AssemblyReferenceTests,
    Phase186CrossPackageBoundaryTests,
    Phase186PackageManifestTests,
    Phase186AnalyzerMetadataTests,
    unittest.TestCase,
):
    """Lock failure evidence and assembly-reference boundary behavior."""

    def setUp(self) -> None:
        """Load a fresh validator for every test."""
        self.validator = load_module(
            "phase186_package_matrix_under_test",
            VALIDATOR_PATH,
        )

    def test_source_uses_named_split_package(self) -> None:
        """Require this suite to keep one named package with a runnable entrypoint."""
        package = Path(__file__).resolve().parent
        self.assertTrue((package / "__init__.py").is_file())
        self.assertTrue((package / "__main__.py").is_file())
        self.assertTrue(package.with_suffix(".py").is_file())

    def test_nu1101_is_not_transient(self) -> None:
        """NU1101 package-not-found errors must not trigger an automatic retry."""
        self.assertFalse(
            self.validator._is_transient_build_failure(
                "error NU1101: Unable to find package Missing.Package"
            )
        )


__all__ = ["Phase186PackageMatrixTests"]
