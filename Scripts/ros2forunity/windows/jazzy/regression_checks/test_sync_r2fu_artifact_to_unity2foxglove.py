#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Regression tests for Jazzy R2FU artifact runtime selection.

"""Regression coverage for exclusive Jazzy runtime selection."""

from __future__ import annotations

import importlib.util
import json
import sys
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[5]
SYNC_PATH = ROOT / "Scripts" / "ros2forunity" / "windows" / "jazzy" / "sync_r2fu_artifact_to_unity2foxglove.py"


def load_sync_module():
    """Load the sync workflow module under test."""
    spec = importlib.util.spec_from_file_location("jazzy_sync_r2fu_artifact", SYNC_PATH)
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    assert spec.loader is not None
    spec.loader.exec_module(module)
    return module


class JazzyRuntimeSelectionTests(unittest.TestCase):
    """Keep the project manifest and lock on exactly one runtime."""

    def test_switching_to_jazzy_removes_foreign_manifest_and_lock_entries(self) -> None:
        """The official Jazzy entrypoint cannot leave another runtime importable."""
        sync = load_sync_module()
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            packages = root / "Unity2Foxglove" / "Packages"
            packages.mkdir(parents=True)
            foreign = "dev.unity2foxglove.ros2forunity.runtime.lyrical.win64"
            (packages / "manifest.json").write_text(
                json.dumps({"dependencies": {foreign: "file:../../Packages/lyrical"}}),
                encoding="utf-8",
            )
            (packages / "packages-lock.json").write_text(
                json.dumps(
                    {
                        "dependencies": {
                            foreign: {
                                "version": "file:../../Packages/lyrical",
                                "depth": 0,
                                "source": "local",
                                "dependencies": {},
                            }
                        }
                    }
                ),
                encoding="utf-8",
            )
            runtime_package = root / "Packages" / sync.PACKAGE_NAME
            runtime_package.mkdir(parents=True)
            (runtime_package / "package.json").write_text(
                json.dumps({"name": sync.PACKAGE_NAME, "version": "0.1.0-test"}),
                encoding="utf-8",
            )

            result = sync.ensure_project_uses_runtime_package(root, update=True)

            manifest = json.loads((packages / "manifest.json").read_text(encoding="utf-8"))
            lock = json.loads((packages / "packages-lock.json").read_text(encoding="utf-8"))
            self.assertEqual([sync.PACKAGE_NAME], sorted(manifest["dependencies"]))
            self.assertEqual([sync.PACKAGE_NAME], sorted(lock["dependencies"]))
            self.assertEqual([sync.PACKAGE_NAME], result["lockRuntimePackages"])

    def test_explicit_ros2_bin_is_forwarded_to_the_runtime_builder(self) -> None:
        """An isolated worktree can reuse the repository-local ROS2 entrypoint explicitly."""
        sync = load_sync_module()
        ros2_bin = Path(r"D:\repo\ros2-windows\ros2_jazzy\bin")

        args = sync.parse_args(["--ros2-bin", str(ros2_bin)])
        command = sync.build_runtime_command(
            Path("build_r2fu_runtime_package.py"),
            Path("runtime.zip"),
            Path("inventory.json"),
            Path("runtime-package"),
            args.ros2_bin,
        )

        self.assertEqual(ros2_bin, args.ros2_bin)
        self.assertEqual("--ros2-bin", command[-2])
        self.assertEqual(str(ros2_bin), command[-1])


if __name__ == "__main__":
    unittest.main()
