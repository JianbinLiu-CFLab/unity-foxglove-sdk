# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

import unittest

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


if __name__ == "__main__":
    unittest.main()
