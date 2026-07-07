#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Regression tests for sample synchronization helpers.

from __future__ import annotations

import importlib.util
import json
import sys
import tempfile
import unittest
from io import StringIO
from pathlib import Path
from unittest import mock


ROOT = Path(__file__).resolve().parents[3]


def load_module(name: str, relative: str):
    """Load one repository helper script as an isolated module."""
    path = ROOT / relative
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    sys.modules[spec.name] = module
    original_path = list(sys.path)
    sys.path.insert(0, str(path.parent))
    try:
        spec.loader.exec_module(module)
    finally:
        sys.path[:] = original_path
    return module


class SampleSyncToolingTests(unittest.TestCase):
    """Regression coverage for sample sync tooling."""

    def test_full_demo_scene_sanitizes_portable_fields_with_variable_indentation(self) -> None:
        """Sample sync should sanitize local-only fields even if Unity changes indentation."""
        module = load_module("sync_full_demo_under_test", "Scripts/samples/sync_full_demo.py")

        with tempfile.TemporaryDirectory() as temp:
            scene = Path(temp) / "scene.unity"
            scene.write_text(
                "FoxgloveManager:\n"
                "    _sharedToken: secret-token\n"
                "    _replayFilePath: C:/Users/Alice/private.mcap\n",
                encoding="utf-8",
            )

            payload = module.portable_full_demo_scene_payload(scene).decode("utf-8")

        self.assertIn("    _sharedToken:", payload)
        self.assertIn("    _replayFilePath:", payload)
        self.assertNotIn("secret-token", payload)
        self.assertNotIn("C:/Users/Alice", payload)

    def test_full_demo_scene_validation_rejects_local_paths_and_tokens(self) -> None:
        """Portable scene payload validation should fail loudly on local-only data."""
        module = load_module("sync_full_demo_validate_under_test", "Scripts/samples/sync_full_demo.py")

        with self.assertRaises(ValueError):
            module.validate_portable_full_demo_scene_payload(
                "  _sharedToken: secret\n  _certificatePfxPath: C:/Users/Alice/cert.pfx\n"
            )

    def test_validate_file_maps_reports_invalid_portable_scene_source(self) -> None:
        """Validate mode should collect portable-scene errors instead of throwing."""
        module = load_module("sync_full_demo_validate_collect_under_test", "Scripts/samples/sync_full_demo.py")

        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            src = root / "Unity2Foxglove" / "Assets" / "Scenes" / "SampleScene.unity"
            dst = root / "Packages" / "dev.unity2foxglove.sdk" / "Samples~" / "FullDemoVisualization" / "Scenes" / "FullDemoVisualization.unity"
            src.parent.mkdir(parents=True)
            dst.parent.mkdir(parents=True)
            src.write_text("  _sharedToken: secret\n", encoding="utf-8")
            dst.write_text("placeholder\n", encoding="utf-8")
            with mock.patch.object(module, "DEMO_ASSETS", root / "Unity2Foxglove" / "Assets"):
                with mock.patch.object(
                    module,
                    "portable_full_demo_scene_payload",
                    side_effect=ValueError("portable scene still has local value"),
                ):
                    errors = module.validate_file_maps([(src, dst)])

        self.assertEqual(1, len(errors))
        self.assertIn("invalid source", errors[0])

    def test_validate_mode_prints_neutral_error_label(self) -> None:
        """Validate mode should not classify stale content as missing files."""
        module = load_module("sync_full_demo_validate_label_under_test", "Scripts/samples/sync_full_demo.py")

        with mock.patch.object(module, "parse_args", return_value=type("Args", (), {"mode": "validate"})()):
            with mock.patch.object(module, "build_pairs", return_value=[]):
                with mock.patch.object(module, "validate_file_maps", return_value=["stale destination: sample"]):
                    stderr = StringIO()
                    with mock.patch("sys.stderr", stderr):
                        result = module.main()

        self.assertEqual(module.EXIT_FAILURE, result)
        self.assertIn("[error] stale destination: sample", stderr.getvalue())
        self.assertNotIn("[missing]", stderr.getvalue())

    def test_ros2_sample_default_imported_root_uses_package_manifest_version(self) -> None:
        """Sample sync should not hardcode the imported sample package version."""
        module = load_module("sync_ros2_samples_under_test", "Scripts/samples/sync_ros2_samples.py")

        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            manifest = root / "Packages" / "dev.unity2foxglove.ros2forunity" / "package.json"
            manifest.parent.mkdir(parents=True)
            manifest.write_text(json.dumps({"version": "9.8.7-preview.6"}), encoding="utf-8")

            imported_root = module.default_imported_root(root)

        self.assertTrue(imported_root.as_posix().endswith("Unity2Foxglove ROS2 For Unity/9.8.7-preview.6"))


if __name__ == "__main__":
    unittest.main()
