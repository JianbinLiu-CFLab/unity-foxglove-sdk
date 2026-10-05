# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

import unittest
from pathlib import Path
from tempfile import TemporaryDirectory

from Scripts.release import regression_inventory


class RegressionInventoryTests(unittest.TestCase):
    """Verify the repository-wide regression discovery authority."""

    def test_repository_inventory_is_complete(self):
        """The live repository has no unassigned regression module."""
        regression_inventory.validate_inventory()

    def test_unassigned_module_is_rejected(self):
        """A synthetic orphan module must fail completeness validation."""
        errors = regression_inventory.inventory_errors(
            discovered={"Scripts.example.regression_checks.test_orphan"},
            ci_modules=set(),
            exclusions={},
        )
        self.assertTrue(any(error.startswith("UNASSIGNED:") for error in errors))

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


if __name__ == "__main__":
    unittest.main()
