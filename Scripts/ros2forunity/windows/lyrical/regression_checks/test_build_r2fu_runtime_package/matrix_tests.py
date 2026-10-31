"""Regression checks for the Lyrical runtime-package builder."""

from __future__ import annotations

import os
import subprocess
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
        source = BUILDER_PATH.read_text(encoding="utf-8")
        self.assertIn("_shared_load_section_names = _PHASE192_SECTION_MANIFEST_MODULE.load_section_names", source)
        self.assertIn("return _shared_load_section_names(section_dir)", source)
        self.assertIn("section_names = load_section_names(section_dir)", source)

    def test_builder_loads_by_file_path_outside_repository_cwd(self) -> None:
        """The builder resolves the shared loader from its own source path."""
        code = (
            "import importlib.util, sys\n"
            f"path = {str(BUILDER_PATH)!r}\n"
            "spec = importlib.util.spec_from_file_location('builder_external_load', path)\n"
            "module = importlib.util.module_from_spec(spec)\n"
            "sys.modules[spec.name] = module\n"
            "spec.loader.exec_module(module)\n"
            "print('BUILDER_EXTERNAL_FILE_LOAD_OK')\n"
        )
        environment = dict(os.environ)
        environment.pop("PYTHONPATH", None)
        with tempfile.TemporaryDirectory() as temp:
            result = subprocess.run(
                [sys.executable, "-B", "-c", code],
                cwd=temp,
                env=environment,
                capture_output=True,
                text=True,
            )
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("BUILDER_EXTERNAL_FILE_LOAD_OK", result.stdout)


__all__ = ["RuntimePackageExtractionTests"]
