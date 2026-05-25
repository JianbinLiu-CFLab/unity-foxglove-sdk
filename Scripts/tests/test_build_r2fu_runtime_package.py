#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Regression tests for runtime package builder path safety.

from __future__ import annotations

import importlib.util
import sys
import tempfile
import unittest
import zipfile
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
BUILDER_PATH = ROOT / "Scripts" / "release" / "build_r2fu_runtime_package.py"


def load_builder_module():
    spec = importlib.util.spec_from_file_location("build_r2fu_runtime_package", BUILDER_PATH)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


class RuntimePackageExtractionTests(unittest.TestCase):
    def setUp(self) -> None:
        self.builder = load_builder_module()

    def test_extract_runtime_rejects_zip_slip_entries(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            archive = root / "runtime.zip"
            package = root / "package"
            with zipfile.ZipFile(archive, "w") as zip_file:
                zip_file.writestr("Ros2ForUnity/../escape.txt", "nope")

            paths = self.builder.BuildPaths(archive, root / "inventory.json", package)

            with self.assertRaises(ValueError):
                self.builder.extract_runtime(paths)

            self.assertFalse((root / "escape.txt").exists())

    def test_extract_runtime_keeps_valid_entries_under_runtime_root(self) -> None:
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            archive = root / "runtime.zip"
            package = root / "package"
            with zipfile.ZipFile(archive, "w") as zip_file:
                zip_file.writestr("Ros2ForUnity/Scripts/ROS2ForUnity.cs", "ok")

            paths = self.builder.BuildPaths(archive, root / "inventory.json", package)

            self.builder.extract_runtime(paths)

            target = package / "Runtime" / "Ros2ForUnity" / "Scripts" / "ROS2ForUnity.cs"
            self.assertEqual("ok", target.read_text(encoding="utf-8"))


if __name__ == "__main__":
    unittest.main()
