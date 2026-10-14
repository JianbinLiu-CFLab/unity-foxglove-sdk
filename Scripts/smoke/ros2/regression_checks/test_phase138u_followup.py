#!/usr/bin/env python3
"""Regression coverage for Phase138U decomposed helper contracts."""

from __future__ import annotations

import contextlib
import io
import inspect
import runpy
import unittest
from unittest import mock

from Scripts.smoke.ros2.phase138u_lidar_deskew_rviz2_acceptance import acceptance_cli
from Scripts.smoke.ros2.phase138u_lidar_deskew_rviz2_acceptance import pointcloud_metrics


class Phase138UFollowupTests(unittest.TestCase):
    """Verify self-test source consistency and module exit semantics."""

    @staticmethod
    def _run_entrypoint(side_effect: BaseException) -> BaseException:
        """Execute the module entrypoint with a controlled CLI failure."""
        with mock.patch.object(acceptance_cli, "main", side_effect=side_effect):
            try:
                runpy.run_module(
                    "Scripts.smoke.ros2.phase138u_lidar_deskew_rviz2_acceptance.__main__",
                    run_name="__main__",
                )
            except BaseException as exc:
                return exc
        raise AssertionError("module entrypoint returned without raising SystemExit")

    def test_self_test_executes_the_subscriber_metric_source(self) -> None:
        """The no-ROS self-test must exercise the helper sent to the ROS2 child."""
        namespace = {}
        exec(acceptance_cli.POINTCLOUD2_METRIC_HELPERS, namespace)
        for name in (
            "read_point_field", "extract_pointcloud2_points", "distance3",
            "ring_seam_metrics", "coordinate_summary", "plane_from_points",
            "fit_plane_metrics", "compute_pointcloud2_metrics",
        ):
            self.assertIn(inspect.getsource(getattr(pointcloud_metrics, name)), acceptance_cli.POINTCLOUD2_METRIC_HELPERS)
        self.assertEqual(pointcloud_metrics.POINT_FIELD_FORMATS, namespace["POINT_FIELD_FORMATS"])
        original = pointcloud_metrics.compute_pointcloud2_metrics

        def compare_metrics(*args):
            """Compare the importable metric implementation with child source."""
            result = original(*args)
            self.assertEqual(result, namespace["compute_pointcloud2_metrics"](*args))
            return result

        with mock.patch.object(pointcloud_metrics, "compute_pointcloud2_metrics", side_effect=compare_metrics) as compute:
            with contextlib.redirect_stdout(io.StringIO()):
                self.assertEqual(0, acceptance_cli.run_self_test())
        compute.assert_called_once()

    def test_module_entrypoint_maps_inconclusive_to_exit_two(self) -> None:
        """Inconclusive captures must retain their distinct process exit code."""
        stderr = io.StringIO()
        with contextlib.redirect_stderr(stderr):
            raised = self._run_entrypoint(acceptance_cli.InconclusiveError("static capture"))
        self.assertIsInstance(raised, SystemExit)
        self.assertEqual(2, raised.code)
        self.assertEqual("[phase138u-lidar-deskew] INCONCLUSIVE: static capture\n", stderr.getvalue())

    def test_module_entrypoint_maps_unexpected_failure_to_exit_one(self) -> None:
        """Unexpected acceptance failures must return the hard-failure code."""
        stderr = io.StringIO()
        with contextlib.redirect_stderr(stderr):
            raised = self._run_entrypoint(RuntimeError("boom"))
        self.assertIsInstance(raised, SystemExit)
        self.assertEqual(1, raised.code)
        self.assertEqual("[phase138u-lidar-deskew] FAIL: boom\n", stderr.getvalue())

    def test_module_entrypoint_maps_keyboard_interrupt_to_exit_130(self) -> None:
        """User interruption must return the conventional 130 status."""
        raised = self._run_entrypoint(KeyboardInterrupt())
        self.assertIsInstance(raised, SystemExit)
        self.assertEqual(130, raised.code)


if __name__ == "__main__":
    unittest.main()
