#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Regression tests for the Humble R2FU artifact sync workflow.

from __future__ import annotations

import importlib.util
import sys
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[5]
SYNC_PATH = ROOT / "Scripts" / "ros2forunity" / "windows" / "humble" / "sync_r2fu_artifact_to_unity2foxglove.py"


def load_sync_module():
    """Load the Humble sync workflow module under test."""
    spec = importlib.util.spec_from_file_location("sync_r2fu_artifact_to_unity2foxglove", SYNC_PATH)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


class SyncR2fuArtifactTests(unittest.TestCase):
    """Regression coverage for Humble artifact identity enforcement."""

    def test_missing_manifest_does_not_bypass_pinned_artifact_hash(self) -> None:
        """The pinned digest remains mandatory when no sidecar manifest exists."""
        sync = load_sync_module()
        with tempfile.TemporaryDirectory() as temp:
            artifact = Path(temp) / "artifact.zip"
            artifact.write_bytes(b"not the pinned Humble artifact")

            with self.assertRaisesRegex(ValueError, "pinned Humble handoff"):
                sync.assert_artifact_matches_manifest(artifact, None)


if __name__ == "__main__":
    unittest.main()
