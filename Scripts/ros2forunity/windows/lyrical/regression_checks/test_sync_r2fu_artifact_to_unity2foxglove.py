#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Regression tests for the Lyrical R2FU artifact sync workflow.

from __future__ import annotations

import importlib.util
import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[5]
SYNC_PATH = ROOT / "Scripts" / "ros2forunity" / "windows" / "lyrical" / "sync_r2fu_artifact_to_unity2foxglove.py"


def load_sync_module():
    """Load the sync workflow module under test."""
    spec = importlib.util.spec_from_file_location("sync_r2fu_artifact_to_unity2foxglove", SYNC_PATH)
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    assert spec.loader is not None
    spec.loader.exec_module(module)
    return module


class SyncR2fuArtifactTests(unittest.TestCase):
    """Regression coverage for syncing a generated Lyrical R2FU artifact into the Unity package."""

    def setUp(self) -> None:
        """Load a fresh copy of the sync module for each test."""
        self.sync = load_sync_module()

    def test_adapter_compliance_tracks_generated_runtime_manifest(self) -> None:
        """Artifact sync updates adapter manifest and notices from generated runtime package data."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            compliance = root / "Packages" / "dev.unity2foxglove.ros2forunity" / "Compliance"
            package = root / "Packages" / self.sync.PACKAGE_NAME
            runtime_support = package / "RuntimeSupport"
            compliance.mkdir(parents=True)
            runtime_support.mkdir(parents=True)

            adoption = {
                "supportedRuntimePackages": [
                    {
                        "packageName": self.sync.PACKAGE_NAME,
                        "artifactSha256": "old",
                        "artifactSize": 1,
                        "inventoryFileCount": 1,
                        "criticalRuntimeFiles": [],
                    }
                ]
            }
            (compliance / "ros2-for-unity-adoption-manifest.json").write_text(
                json.dumps(adoption),
                encoding="utf-8",
            )
            runtime_manifest = {
                "artifactSha256": "new-sha",
                "artifactSize": 123,
                "inventoryFileCount": 456,
                "criticalRuntimeFiles": ["rcl.dll", "zenohc.dll"],
                "defaultRmwImplementation": "rmw_fastrtps_cpp",
                "supportedRmwImplementations": ["rmw_fastrtps_cpp", "rmw_zenoh_cpp"],
                "communicationModes": [{"id": "zenoh"}],
            }
            (runtime_support / "runtime-manifest.json").write_text(
                json.dumps(runtime_manifest),
                encoding="utf-8",
            )
            (package / "THIRD_PARTY_NOTICES.md").write_text("new notices", encoding="utf-8")

            self.sync.sync_adapter_compliance(root, package)

            updated = json.loads((compliance / "ros2-for-unity-adoption-manifest.json").read_text(encoding="utf-8"))
            runtime = updated["supportedRuntimePackages"][0]
            self.assertEqual("new-sha", runtime["artifactSha256"])
            self.assertEqual(123, runtime["artifactSize"])
            self.assertEqual(456, runtime["inventoryFileCount"])
            self.assertEqual(["rcl.dll", "zenohc.dll"], runtime["criticalRuntimeFiles"])
            self.assertEqual(["rmw_fastrtps_cpp", "rmw_zenoh_cpp"], runtime["supportedRmwImplementations"])
            self.assertEqual("new notices", (compliance / "r2fu-lyrical-win64-runtime-notices.md").read_text(encoding="utf-8"))

    def test_manifest_sha256_comparison_is_case_insensitive(self) -> None:
        """Artifact manifest sha256 may use uppercase hex without failing verification."""
        with tempfile.TemporaryDirectory() as temp:
            artifact = Path(temp) / "artifact.zip"
            artifact.write_bytes(b"lyrical artifact payload")
            digest = self.sync.sha256_file(artifact)
            manifest = Path(temp) / "artifact.manifest.json"
            manifest.write_text(json.dumps({"sha256": digest.upper()}), encoding="utf-8")

            previous = self.sync.EXPECTED_ARTIFACT_SHA256
            try:
                self.sync.EXPECTED_ARTIFACT_SHA256 = digest
                info = self.sync.assert_artifact_matches_manifest(artifact, manifest)
            finally:
                self.sync.EXPECTED_ARTIFACT_SHA256 = previous

            self.assertEqual(digest, info["sha256"])

    def test_runtime_selection_removes_foreign_manifest_and_lock_entries(self) -> None:
        """Selecting Lyrical leaves Unity with exactly one runtime package in both files."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            packages = root / "Unity2Foxglove" / "Packages"
            packages.mkdir(parents=True)
            runtime_package = root / "Packages" / self.sync.PACKAGE_NAME
            runtime_package.mkdir(parents=True)
            (runtime_package / "package.json").write_text(
                json.dumps({"name": self.sync.PACKAGE_NAME, "version": "0.1.0-test"}),
                encoding="utf-8",
            )
            foreign = "dev.unity2foxglove.ros2forunity.runtime.jazzy.win64"
            (packages / "manifest.json").write_text(
                json.dumps({"dependencies": {foreign: "file:../../Packages/jazzy"}}),
                encoding="utf-8",
            )
            (packages / "packages-lock.json").write_text(
                json.dumps(
                    {
                        "dependencies": {
                            foreign: {
                                "version": "file:../../Packages/jazzy",
                                "depth": 0,
                                "source": "local",
                                "dependencies": {},
                            }
                        }
                    }
                ),
                encoding="utf-8",
            )

            result = self.sync.ensure_project_uses_runtime_package(root, update=True)

            manifest = json.loads((packages / "manifest.json").read_text(encoding="utf-8"))
            lock = json.loads((packages / "packages-lock.json").read_text(encoding="utf-8"))
            self.assertEqual([self.sync.PACKAGE_NAME], sorted(manifest["dependencies"]))
            self.assertEqual([self.sync.PACKAGE_NAME], sorted(lock["dependencies"]))
            self.assertEqual([self.sync.PACKAGE_NAME], result["lockRuntimePackages"])

    def test_logged_subprocess_failure_reports_log_path(self) -> None:
        """Failed logged subprocesses surface the captured evidence log path."""
        with tempfile.TemporaryDirectory() as temp:
            log = Path(temp) / "failure.log"
            with self.assertRaises(subprocess.CalledProcessError) as raised:
                self.sync.run([sys.executable, "-c", "raise SystemExit(7)"], log=log)

            self.assertIn(str(log), raised.exception.stderr)


if __name__ == "__main__":
    unittest.main()
