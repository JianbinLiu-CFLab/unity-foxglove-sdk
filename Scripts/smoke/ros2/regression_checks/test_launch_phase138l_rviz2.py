#!/usr/bin/env python3
"""Regression tests for Phase138L RViz2 process ownership transfer."""

from __future__ import annotations

import importlib.util
import sys
import unittest
from pathlib import Path
from unittest import mock


ROOT = Path(__file__).resolve().parents[4]
SCRIPT = ROOT / "Scripts" / "smoke" / "ros2" / "launch_phase138l_rviz2.py"


def load_module():
    """Load the launcher module with its ROS2 helper import path configured."""
    smoke_dir = str(SCRIPT.parent)
    if smoke_dir not in sys.path:
        sys.path.insert(0, smoke_dir)
    spec = importlib.util.spec_from_file_location("phase138l_rviz2", SCRIPT)
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    assert spec.loader is not None
    spec.loader.exec_module(module)
    return module


class Phase138LRvizOwnershipTests(unittest.TestCase):
    """Verify RViz2 process handles transfer into cleanup ownership."""
    def test_launch_transfers_rviz_process_to_owner_list(self) -> None:
        """The returned RViz process must be retained for cleanup ownership."""
        module = load_module()
        fake_process = object()
        owned: list[object] = []
        with mock.patch.object(module.ros2env, "launch_rviz", return_value=fake_process) as launch:
            result = module.launch_owned_rviz(
                Path("ros2"),
                Path("config.rviz"),
                {},
                "phase138l-rviz",
                owned,
                startup_check_seconds=0.0,
                window_wait_seconds=0.0,
            )
        self.assertIs(result, fake_process)
        self.assertEqual([fake_process], owned)
        launch.assert_called_once()


if __name__ == "__main__":
    unittest.main()
