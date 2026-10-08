"""Regression checks for the Humble runtime-package builder."""

from __future__ import annotations

from pathlib import Path

from .archive_extraction_tests import RuntimeArchiveExtractionTests
from .build_transaction_tests import RuntimeBuildTransactionTests
from .foundation import *
from .overlay_inventory_tests import RuntimeOverlayInventoryTests
from .runtime_patch_tests import RuntimePatchOverlayTests


class RuntimePackageExtractionTests(
    RuntimeArchiveExtractionTests,
    RuntimePatchOverlayTests,
    RuntimeOverlayInventoryTests,
    RuntimeBuildTransactionTests,
    unittest.TestCase,
):
    """Regression coverage for runtime package archive extraction."""

    def setUp(self) -> None:
        """Load a fresh reference to the builder module for each test."""
        self.builder = load_builder_module()

    def test_source_uses_named_split_package(self) -> None:
        """Require this suite to keep one named package with a runnable entrypoint."""
        module = __import__(
            "Scripts.ros2forunity.windows.humble.regression_checks.test_build_r2fu_runtime_package",
            fromlist=["__file__"],
        )
        package = Path(module.__file__).with_suffix("")
        self.assertTrue(package.is_dir())
        self.assertTrue((package / "__main__.py").is_file())


__all__ = ["RuntimePackageExtractionTests"]
