from __future__ import annotations

import unittest

from Scripts.phase192 import freeze_baseline


class FreezeBaselineTests(unittest.TestCase):
    def test_python_test_paths_excludes_package_initializers(self) -> None:
        paths = [
            "Scripts/smoke/foxrun/regression_checks/test_feature.py",
            "Scripts/smoke/foxrun/regression_checks/test_feature/__init__.py",
            "Scripts/smoke/foxrun/regression_checks/test_feature/support.py",
        ]
        self.assertEqual(
            freeze_baseline.python_test_paths(paths),
            [
                "Scripts/smoke/foxrun/regression_checks/test_feature.py",
                "Scripts/smoke/foxrun/regression_checks/test_feature/support.py",
            ],
        )


if __name__ == "__main__":
    unittest.main()
