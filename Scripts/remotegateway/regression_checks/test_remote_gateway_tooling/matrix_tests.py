"""Regression checks for remote-gateway build and acceptance helpers."""

from __future__ import annotations

from pathlib import Path

from .artifact_copy_tests import RemoteGatewayArtifactCopyTests
from .build_provenance_tests import RemoteGatewayBuildProvenanceTests
from .environment_inheritance_tests import RemoteGatewayEnvironmentTests
from .foundation import *
from .manifest_validation_tests import RemoteGatewayManifestValidationTests
from .skip_build_identity_tests import RemoteGatewaySkipBuildTests


class RemoteGatewayToolingTests(
    RemoteGatewayArtifactCopyTests,
    RemoteGatewayBuildProvenanceTests,
    RemoteGatewayEnvironmentTests,
    RemoteGatewaySkipBuildTests,
    RemoteGatewayManifestValidationTests,
    unittest.TestCase,
):
    """Lock artifact allow-lists, provenance, and acceptance identity."""

    def setUp(self) -> None:
        """Load fresh build and acceptance modules."""
        self.build = load_module("remote_gateway_build_under_test", BUILD_PATH)
        self.acceptance = load_module(
            "remote_gateway_acceptance_under_test",
            ACCEPTANCE_PATH,
        )

    def _create_source_fixture(self, root: Path) -> tuple[Path, Path]:
        """Create the minimal source files required by manifest generation tests."""
        source = root / "foxglove-sdk"
        header = source / "include" / "foxglove-c" / "foxglove-c.h"
        header.parent.mkdir(parents=True)
        header.write_bytes(b"header fixture")
        (source / "Cargo.lock").write_bytes(b"cargo lock fixture")
        return source, header

    def test_source_uses_named_split_package(self) -> None:
        """Require this suite to keep one named package with a runnable entrypoint."""
        package = Path(__file__).resolve().parent
        self.assertTrue((package / "__init__.py").is_file())
        self.assertTrue((package / "__main__.py").is_file())
        self.assertTrue(package.with_suffix(".py").is_file())


__all__ = ["RemoteGatewayToolingTests"]
