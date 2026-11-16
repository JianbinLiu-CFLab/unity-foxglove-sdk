#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Regression tests for the Humble R2FU artifact sync workflow.

from __future__ import annotations

import importlib.util
import json
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

    def test_project_update_uses_local_reference_in_lockfile(self) -> None:
        """Updating a missing local runtime lock entry must preserve the file reference."""
        sync = load_sync_module()
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            packages = root / "Unity2Foxglove" / "Packages"
            packages.mkdir(parents=True)
            (packages / "manifest.json").write_text(
                json.dumps({"dependencies": {}}),
                encoding="utf-8",
            )
            (packages / "packages-lock.json").write_text(
                json.dumps({"dependencies": {}}),
                encoding="utf-8",
            )

            sync.ensure_project_uses_runtime_package(root, update=True)

            lock = json.loads((packages / "packages-lock.json").read_text(encoding="utf-8"))
            entry = lock["dependencies"][sync.PACKAGE_NAME]
            self.assertEqual("file:../../Packages/" + sync.PACKAGE_NAME, entry["version"])


if __name__ == "__main__":
    unittest.main()
