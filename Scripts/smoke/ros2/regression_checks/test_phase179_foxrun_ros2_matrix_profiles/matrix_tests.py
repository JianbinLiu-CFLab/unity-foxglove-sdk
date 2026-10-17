"""Regression checks for named Phase179 ROS2 interoperability matrix profiles."""

from __future__ import annotations

from pathlib import Path

from .correlation_evidence_tests import Phase179CorrelationEvidenceTests
from .foundation import *
from .profile_definition_tests import Phase179ProfileDefinitionTests
from .summary_helpers import Phase179SummaryHelpers
from .windows_local_acceptance_tests import Phase179WindowsLocalAcceptanceTests


class Phase179FoxRunRos2MatrixProfileTests(
    Phase179ProfileDefinitionTests,
    Phase179WindowsLocalAcceptanceTests,
    Phase179CorrelationEvidenceTests,
    Phase179SummaryHelpers,
    unittest.TestCase,
):
    """Lock every certified distro/RMW row to one visible operator-facing profile."""

    def setUp(self) -> None:
        """Load a fresh profile engine for each isolated contract check."""
        self.profiles = load_profile_module()

    def test_source_uses_named_split_package(self) -> None:
        """Require this suite to keep one named package with a runnable entrypoint."""
        module = __import__(
            "Scripts.smoke.ros2.regression_checks.test_phase179_foxrun_ros2_matrix_profiles",
            fromlist=["__file__"],
        )
        package = Path(module.__file__).with_suffix("")
        self.assertTrue(package.is_dir())
        self.assertTrue((package / "__main__.py").is_file())


__all__ = ["Phase179FoxRunRos2MatrixProfileTests"]
