"""Regression coverage for the decomposed source-generator validator facade."""

from __future__ import annotations

import importlib.util
import subprocess
import sys
import types
import unittest
import builtins
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

    def test_canonical_spec_loader_exports_validator_entrypoints(self) -> None:
        """A canonical file-spec load keeps the legacy facade exports available."""
        module_name = "Scripts.package.validate_source_generator_dll"
        path = ROOT / "Scripts" / "package" / "validate_source_generator_dll.py"
        spec = importlib.util.spec_from_file_location(module_name, path)
        self.assertIsNotNone(spec)
        self.assertIsNotNone(spec.loader)
        module = importlib.util.module_from_spec(spec)
        previous = sys.modules.get(module_name)
        previous_top_level = sys.modules.get("validate_source_generator_dll")
        previous_path = list(sys.path)
        sys.modules[module_name] = module
        try:
            spec.loader.exec_module(module)
            self.assertTrue(callable(module.main))
            self.assertTrue(callable(module.validate_or_update))
            self.assertTrue(callable(module.validate_analyzer_contracts))
        finally:
            if previous is None:
                sys.modules.pop(module_name, None)
            else:
                sys.modules[module_name] = previous
            if previous_top_level is None:
                sys.modules.pop("validate_source_generator_dll", None)
            else:
                sys.modules["validate_source_generator_dll"] = previous_top_level
            sys.path[:] = previous_path

    def test_canonical_spec_loader_preserves_existing_top_level_module(self) -> None:
        """A canonical file-spec load preserves an existing top-level alias and metadata."""
        module_name = "Scripts.package.validate_source_generator_dll"
        path = ROOT / "Scripts" / "package" / "validate_source_generator_dll.py"
        spec = importlib.util.spec_from_file_location(module_name, path)
        self.assertIsNotNone(spec)
        self.assertIsNotNone(spec.loader)
        module = importlib.util.module_from_spec(spec)
        previous = sys.modules.get(module_name)
        previous_top_level = sys.modules.get("validate_source_generator_dll")
        previous_path = list(sys.path)
        old_module = types.ModuleType("validate_source_generator_dll")
        sys.modules[module_name] = module
        sys.modules["validate_source_generator_dll"] = old_module
        try:
            spec.loader.exec_module(module)
            self.assertIs(sys.modules.get("validate_source_generator_dll"), old_module)
            self.assertEqual(module.__name__, module_name)
            self.assertEqual(module.__package__, "Scripts.package")
        finally:
            if previous is None:
                sys.modules.pop(module_name, None)
            else:
                sys.modules[module_name] = previous
            if previous_top_level is None:
                sys.modules.pop("validate_source_generator_dll", None)
            else:
                sys.modules["validate_source_generator_dll"] = previous_top_level
            sys.path[:] = previous_path

    def test_canonical_spec_loader_removes_new_top_level_alias_and_restores_path(self) -> None:
        """A canonical file-spec load leaves no new top-level alias or path entry."""
        module_name = "Scripts.package.validate_source_generator_dll"
        path = ROOT / "Scripts" / "package" / "validate_source_generator_dll.py"
        spec = importlib.util.spec_from_file_location(module_name, path)
        self.assertIsNotNone(spec)
        self.assertIsNotNone(spec.loader)
        module = importlib.util.module_from_spec(spec)
        previous = sys.modules.get(module_name)
        previous_top_level = sys.modules.get("validate_source_generator_dll")
        previous_path = list(sys.path)
        sys.modules.pop("validate_source_generator_dll", None)
        sys.modules[module_name] = module
        try:
            spec.loader.exec_module(module)
            self.assertNotIn("validate_source_generator_dll", sys.modules)
            self.assertEqual(sys.path, previous_path)
        finally:
            if previous is None:
                sys.modules.pop(module_name, None)
            else:
                sys.modules[module_name] = previous
            if previous_top_level is None:
                sys.modules.pop("validate_source_generator_dll", None)
            else:
                sys.modules["validate_source_generator_dll"] = previous_top_level
            sys.path[:] = previous_path

    def test_canonical_spec_loader_restores_state_after_import_failure(self) -> None:
        """A failed canonical load restores the alias and path transactionally."""
        module_name = "Scripts.package.validate_source_generator_dll"
        path = ROOT / "Scripts" / "package" / "validate_source_generator_dll.py"
        spec = importlib.util.spec_from_file_location(module_name, path)
        self.assertIsNotNone(spec)
        self.assertIsNotNone(spec.loader)
        module = importlib.util.module_from_spec(spec)
        previous = sys.modules.get(module_name)
        previous_top_level = sys.modules.get("validate_source_generator_dll")
        previous_path = list(sys.path)
        old_module = types.ModuleType("validate_source_generator_dll")
        sys.modules[module_name] = module
        sys.modules["validate_source_generator_dll"] = old_module
        original_import = builtins.__import__

        def fail_legacy_import(name, globals=None, locals=None, fromlist=(), level=0):
            if name == "validate_source_generator_dll" and level == 0:
                raise ImportError("forced facade import failure")
            return original_import(name, globals, locals, fromlist, level)

        builtins.__import__ = fail_legacy_import
        try:
            with self.assertRaises(ImportError):
                spec.loader.exec_module(module)
            self.assertIs(sys.modules.get("validate_source_generator_dll"), old_module)
            self.assertEqual(sys.path, previous_path)
        finally:
            builtins.__import__ = original_import
            if previous is None:
                sys.modules.pop(module_name, None)
            else:
                sys.modules[module_name] = previous
            if previous_top_level is None:
                sys.modules.pop("validate_source_generator_dll", None)
            else:
                sys.modules["validate_source_generator_dll"] = previous_top_level
            sys.path[:] = previous_path

    def test_package_facade_exports_validator_entrypoints(self) -> None:
        """The package facade exposes the public validator callables."""
        import Scripts.package.validate_source_generator_dll as validator

        self.assertTrue(callable(validator.main))
        self.assertTrue(callable(validator.validate_or_update))
        self.assertTrue(callable(validator.validate_analyzer_contracts))


if __name__ == "__main__":
    unittest.main()
