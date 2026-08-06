#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Regression tests for Humble runtime artifact inventory caching.

from __future__ import annotations

import importlib.util
import json
import sys
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[5]
INSPECTOR_PATH = ROOT / "Scripts" / "ros2forunity" / "windows" / "humble" / "inspect_r2fu_runtime_artifact.py"


def load_inspector_module():
    """Load the Humble artifact inspector as a Python module."""
    spec = importlib.util.spec_from_file_location("inspect_r2fu_runtime_artifact", INSPECTOR_PATH)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


class InspectR2fuRuntimeArtifactTests(unittest.TestCase):
    """Regression coverage for cache provenance."""

    def test_cache_requires_current_inspector_hash(self) -> None:
        """An unchanged artifact must be reclassified after inspector logic changes."""
        inspector = load_inspector_module()
        with tempfile.TemporaryDirectory() as temp:
            cache = Path(temp) / "inventory.json"
            cache.write_text(
                json.dumps({"sha256": "artifact-hash", "inspectorSha256": "old-inspector"}),
                encoding="utf-8",
            )

            self.assertIsNone(
                inspector.read_cached_inventory(cache, "artifact-hash", "new-inspector")
            )
            self.assertIsNotNone(
                inspector.read_cached_inventory(cache, "artifact-hash", "old-inspector")
            )


if __name__ == "__main__":
    unittest.main()
