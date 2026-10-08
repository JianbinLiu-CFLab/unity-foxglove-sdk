"""Regression checks for smoke helper failure handling."""

from __future__ import annotations

from pathlib import Path

from .bridge_phase_tests import BridgePhaseTests
from .foundation import *
from .phase110_fetch_tests import Phase110FetchTests
from .phase138_rviz_tests import Phase138RvizTests
from .phase139_acceptance_tests import Phase139AcceptanceTests
from .tls_mcap_tests import TlsMcapProbeTests
from .transport_core_tests import CoreTransportTests


class CoreSmokeScriptTests(
    CoreTransportTests,
    Phase139AcceptanceTests,
    TlsMcapProbeTests,
    Phase110FetchTests,
    Phase138RvizTests,
    BridgePhaseTests,
    unittest.TestCase,
):
    """Regression coverage for smoke helper failure handling."""

    def test_source_uses_named_split_package(self) -> None:
        """Require this suite to keep one named package with a runnable entrypoint."""
        module = __import__(
            "Scripts.smoke.test_core_smoke_scripts",
            fromlist=["__file__"],
        )
        package = Path(module.__file__).with_suffix("")
        self.assertTrue(package.is_dir())
        self.assertTrue((package / "__main__.py").is_file())


__all__ = ["CoreSmokeScriptTests"]
