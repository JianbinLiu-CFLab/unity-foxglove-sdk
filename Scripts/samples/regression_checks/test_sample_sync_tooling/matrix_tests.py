"""Regression checks for sample synchronization tooling."""

from __future__ import annotations

from pathlib import Path

from .bridge_sample_tests import BridgeSampleSyncTests
from .foundation import *
from .full_demo_sync_tests import FullDemoSyncTests
from .ros2_sample_sync_tests import Ros2SampleSyncTests


class SampleSyncToolingTests(
    BridgeSampleSyncTests,
    FullDemoSyncTests,
    Ros2SampleSyncTests,
    unittest.TestCase,
):
    """Regression coverage for sample sync tooling."""

    def test_source_uses_named_split_package(self) -> None:
        """Require this suite to keep one named package with a runnable entrypoint."""
        module = __import__(
            "Scripts.samples.regression_checks.test_sample_sync_tooling",
            fromlist=["__file__"],
        )
        package = Path(module.__file__).with_suffix("")
        self.assertTrue(package.is_dir())
        self.assertTrue((package / "__main__.py").is_file())


__all__ = ["SampleSyncToolingTests"]
