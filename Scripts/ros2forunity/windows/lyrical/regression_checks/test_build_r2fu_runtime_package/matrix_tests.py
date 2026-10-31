"""Regression checks for the Lyrical runtime-package builder."""

from __future__ import annotations

from pathlib import Path

from .archive_metadata_tests import LyricalArchiveMetadataTests
from .build_transaction_tests import LyricalBuildTransactionTests
from .dependency_description_tests import LyricalDependencyDescriptionTests
from .foundation import *
from .lifecycle_zenoh_tests import LyricalLifecycleZenohTests
from .runtime_overlay_tests import LyricalRuntimeOverlayTests


class RuntimePackageExtractionTests(
    LyricalArchiveMetadataTests,
    LyricalDependencyDescriptionTests,
    LyricalRuntimeOverlayTests,
    LyricalLifecycleZenohTests,
    LyricalBuildTransactionTests,
    unittest.TestCase,
):
    """Regression coverage for Lyrical runtime package archive extraction."""

    def setUp(self) -> None:
        """Load a fresh reference to the builder module for each test."""
        self.builder = load_builder_module()

    def test_source_uses_named_split_package(self) -> None:
        """Require this suite to keep one named package with a runnable entrypoint."""
        module = __import__(
            "Scripts.ros2forunity.windows.lyrical.regression_checks.test_build_r2fu_runtime_package",
            fromlist=["__file__"],
        )
        package = Path(module.__file__).with_suffix("")
        self.assertTrue(package.is_dir())
        self.assertTrue((package / "__main__.py").is_file())

    def test_builder_uses_shared_section_manifest_loader(self) -> None:
        """The builder and validator must share one manifest contract."""
        shared_manifest = (
            ROOT / "Scripts/ros2forunity/windows/lyrical/section_manifest.py"
        ).resolve()
        self.assertEqual(
            shared_manifest,
            Path(self.builder.load_section_names.__code__.co_filename).resolve(),
        )


__all__ = ["RuntimePackageExtractionTests"]
