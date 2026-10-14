# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

import importlib
import unittest
from pathlib import Path
from tempfile import TemporaryDirectory

from Scripts.release import regression_inventory


class RegressionInventoryTests(unittest.TestCase):
    """Verify the repository-wide regression discovery authority."""

    def test_repository_inventory_is_complete(self):
        """The live repository has no unassigned regression module."""
        regression_inventory.validate_inventory()

    def test_decomposed_python_regression_surfaces_keep_expected_counts(self):
        """Explicit package loading keeps the four migrated suites complete."""
        expected = {
            "Scripts.package.regression_checks.test_validate_phase186_package_matrix": 11,
            "Scripts.remotegateway.regression_checks.test_remote_gateway_tooling": 23,
            "Scripts.ros2forunity.windows.humble.regression_checks.test_build_r2fu_runtime_package": 23,
            "Scripts.samples.regression_checks.test_sample_sync_tooling": 25,
        }
        loader = unittest.TestLoader()
        observed = {
            name: loader.loadTestsFromModule(importlib.import_module(name)).countTestCases()
            for name in expected
        }
        self.assertEqual(expected, observed)

    def test_unassigned_module_is_rejected(self):
        """A synthetic orphan module must fail completeness validation."""
        errors = regression_inventory.inventory_errors(
            discovered={"Scripts.example.regression_checks.test_orphan"},
            ci_modules=set(),
            exclusions={},
        )
        self.assertTrue(any(error.startswith("UNASSIGNED:") for error in errors))

    def test_windows_tooling_docs_use_complete_inventory_lane(self):
        root = Path(__file__).resolve().parents[3]
        lane = regression_inventory._WORKFLOW_RUNNER_MODULES["windows-parity-regression-tooling"]
        self.assertEqual(len(lane), len(set(lane)))
        command = "python -B Scripts/release/regression_inventory.py --run-lane windows-parity-regression-tooling"
        for relative in (
            "Scripts/ros2forunity/README.md",
            "Scripts/ros2forunity/windows/humble/README.md",
            "Scripts/ros2forunity/windows/jazzy/README.md",
        ):
            with self.subTest(path=relative):
                self.assertIn(command, (root / relative).read_text(encoding="utf-8"))
        for distro in ("humble", "jazzy", "lyrical"):
            prefix = f"Scripts.ros2forunity.windows.{distro}.regression_checks."
            directory = root / "Scripts/ros2forunity/windows" / distro / "regression_checks"
            expected = {prefix + path.stem for path in directory.glob("test_*.py")}
            self.assertTrue(expected)
            self.assertEqual(expected, {name for name in lane if name.startswith(prefix)})

    def test_split_cli_exit_contracts_are_documented(self):
        root = Path(__file__).resolve().parents[3]
        readme = (root / "Scripts/ros2forunity/README.md").read_text(encoding="utf-8")
        for marker in (
            "build_foxrun_custom_typesupport_addon.py",
            "verify_foxrun_custom_typesupport_toolchain.py",
            "characterize_foxrun_custom_interface.py",
            "run_phase121_conformance.py",
            "phase179_foxrun_ros2_player_host.py",
            "KeyboardInterrupt",
            "130",
        ):
            with self.subTest(marker=marker):
                self.assertIn(marker, readme)

    def test_runner_modules_require_a_workflow_lane(self):
        """A runner-only module counts only when a workflow invokes its lane."""
        with TemporaryDirectory() as temporary:
            root = Path(temporary)
            workflow = root / ".github" / "workflows" / "ci.yml"
            workflow.parent.mkdir(parents=True)
            workflow.write_text(
                "run: python3 Scripts/release/run_ci.py --only phase186-bridge-tooling\n",
                encoding="utf-8",
            )
            runner = root / "Scripts" / "release" / "run_ci.py"
            runner.parent.mkdir(parents=True)
            runner.write_text(
                "FAKE = 'Scripts.fake.regression_checks.test_runner_only'\n",
                encoding="utf-8",
            )

            modules = regression_inventory.discover_ci_modules(root)
            self.assertIn(
                "Scripts.smoke.foxrun.regression_checks.test_phase186_bridge_live",
                modules,
            )
            self.assertNotIn(
                "Scripts.fake.regression_checks.test_runner_only",
                modules,
            )

    def test_runner_modules_are_not_counted_without_a_workflow_lane(self):
        """A module present only in run_ci.py is not CI coverage."""
        with TemporaryDirectory() as temporary:
            root = Path(temporary)
            workflow = root / ".github" / "workflows" / "ci.yml"
            workflow.parent.mkdir(parents=True)
            workflow.write_text("run: echo no runner lane\n", encoding="utf-8")
            runner = root / "Scripts" / "release" / "run_ci.py"
            runner.parent.mkdir(parents=True)
            runner.write_text(
                "MODULE = 'Scripts.fake.regression_checks.test_runner_only'\n",
                encoding="utf-8",
            )

            modules = regression_inventory.discover_ci_modules(root)
            self.assertNotIn(
                "Scripts.fake.regression_checks.test_runner_only",
                modules,
            )

    def test_workflow_inventory_ignores_comments_and_disabled_steps(self):
        """Only commands in active run blocks count as CI coverage."""
        with TemporaryDirectory() as temporary:
            root = Path(temporary)
            workflow = root / ".github" / "workflows" / "ci.yml"
            workflow.parent.mkdir(parents=True)
            workflow.write_text(
                """
jobs:
  test:
    steps:
      - name: disabled
        if: false
        run: python -m unittest Scripts.fake.regression_checks.test_disabled
      - name: active
        run: |
          # python -m unittest Scripts.fake.regression_checks.test_comment
          python -m unittest Scripts.fake.regression_checks.test_active
""",
                encoding="utf-8",
            )
            modules = regression_inventory.discover_ci_modules(root)
            self.assertIn("Scripts.fake.regression_checks.test_active", modules)
            self.assertNotIn("Scripts.fake.regression_checks.test_disabled", modules)
            self.assertNotIn("Scripts.fake.regression_checks.test_comment", modules)

    def test_workflow_inventory_recognizes_reviewed_inventory_lane(self):
        """A reviewed inventory lane expands to its declared modules."""
        with TemporaryDirectory() as temporary:
            root = Path(temporary)
            workflow = root / ".github" / "workflows" / "ci.yml"
            workflow.parent.mkdir(parents=True)
            workflow.write_text(
                "run: python3 -B Scripts/release/regression_inventory.py --run-lane phase181-interface-tooling\n",
                encoding="utf-8",
            )
            modules = regression_inventory.discover_ci_modules(root)
            self.assertIn(
                "Scripts.ros2forunity.interfaces.regression_checks.test_h01_delivery_enum_contract",
                modules,
            )

    def test_portable_modules_stay_in_the_portable_ci_lane(self):
        """Fixture-free regression checks execute in the required portable lane."""
        linux_lane = regression_inventory._WORKFLOW_RUNNER_MODULES[
            "phase181-interface-tooling"
        ]
        windows_lane = regression_inventory._WORKFLOW_RUNNER_MODULES[
            "windows-parity-regression-tooling"
        ]
        portable_lane = regression_inventory._WORKFLOW_RUNNER_MODULES[
            "portable-regression-tooling"
        ]
        expected = {
            "Scripts.mcap.regression_checks.test_mcap_time_sync",
            "Scripts.smoke.foxrun.regression_checks.test_atomic_publication",
            "Scripts.smoke.foxrun.regression_checks.test_phase189_component_messagepack_manual",
            "Scripts.smoke.foxrun.regression_checks.test_phase189_component_messagepack_probe",
            "Scripts.smoke.ros2.regression_checks.test_i10_native_cleanup",
            "Scripts.smoke.ros2.regression_checks.test_i10_phase106_cleanup",
            "Scripts.smoke.ros2.regression_checks.test_i10_phase109_cleanup",
            "Scripts.smoke.ros2.regression_checks.test_i10_remaining_cleanup",
            "Scripts.smoke.ros2.regression_checks.test_launch_phase138l_rviz2",
            "Scripts.smoke.ros2.regression_checks.test_phase138u_followup",
        }
        self.assertEqual(expected, set(portable_lane))
        self.assertEqual({}, regression_inventory.EXPLICIT_EXCLUSIONS)
        self.assertNotIn(
            "Scripts.ros2forunity.interfaces.regression_checks.test_h04_tool_identity_contract",
            linux_lane,
        )
        self.assertIn(
            "Scripts.ros2forunity.interfaces.regression_checks.test_h04_tool_identity_contract",
            windows_lane,
        )


if __name__ == "__main__":
    unittest.main()
