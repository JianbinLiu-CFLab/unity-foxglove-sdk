"""Regression coverage for the decomposed source-generator validator facade."""

from __future__ import annotations

import subprocess
import sys
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[3]


class ValidateSourceGeneratorFacadeTests(unittest.TestCase):
    """Keep the legacy script entrypoint and package import contract aligned."""

    def test_legacy_script_entrypoint_exposes_validator_help(self) -> None:
        """The legacy path remains directly executable after the split."""
        completed = subprocess.run(
            [sys.executable, "Scripts/package/validate_source_generator_dll.py", "--help"],
            cwd=ROOT,
            check=False,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            text=True,
        )
        self.assertEqual(0, completed.returncode)
        self.assertIn("Validate the checked-in source generator DLL.", completed.stdout)

    def test_package_facade_exports_validator_entrypoints(self) -> None:
        """The package facade exposes the public validator callables."""
        import Scripts.package.validate_source_generator_dll as validator

        self.assertTrue(callable(validator.main))
        self.assertTrue(callable(validator.validate_or_update))
        self.assertTrue(callable(validator.validate_analyzer_contracts))


if __name__ == "__main__":
    unittest.main()
