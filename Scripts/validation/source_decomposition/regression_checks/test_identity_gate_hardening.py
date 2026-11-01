from __future__ import annotations

import subprocess
import sys
import hashlib
import tempfile
import unittest
from pathlib import Path

from Scripts.validation.source_decomposition import compare_identity_surfaces

_REPOSITORY_ROOT = Path(__file__).resolve().parents[4]
_REVIEWED_SECTIONS = (
    "aggregate",
    "foundation",
    "surface_rows_tests",
    "split_surface_tests",
    "split_contract_tests",
    "compatibility_surface_tests",
    "compatibility_initializer_tests",
    "loader_isolation_tests",
    "loader_entrypoint_tests",
    "loader_mutation_tests",
    "namespace_risk_tests",
    "namespace_execution_core_tests",
    "namespace_execution_edges_tests",
)


class IdentityGateHardeningTests(unittest.TestCase):
    """Regression coverage for identity-gate waiver and discovery boundaries."""

    def _write_identity_package(self, root: Path, sections: tuple[str, ...]) -> Path:
        """Create a minimal identity-tooling package fixture."""
        package = root / "Scripts/validation/source_decomposition/regression_checks/test_identity_tooling"
        package.mkdir(parents=True)
        imports = "\n".join(f"from . import {name}" for name in sections)
        (package / "__init__.py").write_text(imports + "\n", encoding="utf-8")
        (package / "__main__.py").write_text("from . import *\n", encoding="utf-8")
        for name in sections:
            source = "PUBLIC = 1\n__all__ = ['PUBLIC']\n"
            if name == "unreviewed_section":
                source = "globals()['FORBIDDEN'] = 1\n__all__ = ['PUBLIC']\n"
            (package / f"{name}.py").write_text(source, encoding="utf-8")
        return package / "__main__.py"

    def test_entrypoint_waiver_rejects_unreviewed_section(self) -> None:
        """An extra section must disable the identity-tooling namespace waiver."""
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            path = self._write_identity_package(root, _REVIEWED_SECTIONS + ("unreviewed_section",))
            with self.assertRaises(ValueError):
                compare_identity_surfaces._entrypoint_contract(path, repository_root=root)

    def test_entrypoint_waiver_accepts_exact_reviewed_sections(self) -> None:
        """The exact reviewed section set remains a valid transition."""
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            path = self._write_identity_package(root, _REVIEWED_SECTIONS)
            self.assertTrue(compare_identity_surfaces._entrypoint_contract(path, repository_root=root)[0])

    def test_identity_discovery_can_repeat_in_one_process(self) -> None:
        """Repeated discovery in one interpreter must not return an empty suite."""
        script = "\n".join(
            (
                "import unittest",
                "loader = unittest.TestLoader()",
                "first = loader.discover('Scripts/validation/source_decomposition/regression_checks', pattern='test_identity_tooling.py', top_level_dir='.')",
                "second = loader.discover('Scripts/validation/source_decomposition/regression_checks', pattern='test_identity_tooling.py', top_level_dir='.')",
                "print(first.countTestCases(), second.countTestCases())",
            )
        )
        result = subprocess.run(
            [sys.executable, "-B", "-c", script],
            cwd=_REPOSITORY_ROOT,
            capture_output=True,
            text=True,
            check=False,
        )
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual("334 334", result.stdout.strip())

    def test_template_registry_fallback_is_not_workflow_mutable(self) -> None:
        """The template-registry identity waiver must not be defined by the PR workflow."""
        workflow = (_REPOSITORY_ROOT / ".github/workflows/dotnet-tests.yml").read_text(encoding="utf-8")
        self.assertNotIn('expected_template_registry_split_paths', workflow)
        self.assertNotIn("IDENTITY_GATE_REVIEWED_TEMPLATE_REGISTRY", workflow)

    def test_source_decomposition_directory_migration_preserves_surface_key(self) -> None:
        """The compatibility checker maps the validation path back to its legacy key."""
        current = {
            "Scripts/validation/source_decomposition/source_layout.py": ("surface",)
        }
        normalized = compare_identity_surfaces._normalize_migrated_source_decomposition_surfaces(
            current
        )
        self.assertEqual(
            {"Scripts/phase192/source_layout.py": ("surface",)},
            normalized,
        )

    def test_current_layout_does_not_trigger_directory_migration_normalization(self) -> None:
        """A post-migration base must keep its current surface keys."""
        current = {
            "Scripts/validation/source_decomposition/source_layout.py": ("surface",)
        }
        self.assertFalse(
            compare_identity_surfaces._directory_migration_is_active(
                current,
                current,
                strict=False,
            )
        )
        legacy = {
            "Scripts/phase192/source_layout.py": ("surface",),
            "Scripts/phase192/identity_contracts.py": ("surface",),
        }
        current_pair = {
            "Scripts/validation/source_decomposition/source_layout.py": ("surface",),
            "Scripts/validation/source_decomposition/identity_contracts.py": ("surface",),
        }
        self.assertTrue(
            compare_identity_surfaces._directory_migration_is_active(
                legacy,
                current_pair,
                strict=False,
            )
        )
        self.assertFalse(
            compare_identity_surfaces._directory_migration_is_active(
                legacy,
                current,
                strict=False,
            )
        )

    def test_template_registry_facade_transition_requires_reviewed_digest(self) -> None:
        """The facade compatibility transition is bound to its reviewed source digest."""
        relative = compare_identity_surfaces._REVIEWED_TEMPLATE_REGISTRY_FACADE_RELATIVE
        digest = compare_identity_surfaces._REVIEWED_TEMPLATE_REGISTRY_FACADE_DIGEST
        self.assertTrue(
            compare_identity_surfaces._reviewed_template_registry_facade_transition(
                relative,
                {relative: digest},
            )
        )
        self.assertFalse(
            compare_identity_surfaces._reviewed_template_registry_facade_transition(
                relative,
                {relative: "0" * 64},
            )
        )
        compare_relative = (
            compare_identity_surfaces._REVIEWED_COMPARE_IDENTITY_SURFACES_FACADE_RELATIVE
        )
        compare_digest = (
            compare_identity_surfaces._REVIEWED_COMPARE_IDENTITY_SURFACES_FACADE_DIGEST
        )
        self.assertTrue(
            compare_identity_surfaces._reviewed_template_registry_facade_transition(
                compare_relative,
                {compare_relative: compare_digest},
            )
        )
        self.assertFalse(
            compare_identity_surfaces._reviewed_template_registry_facade_transition(
                compare_relative,
                {relative: digest},
            )
        )

    def test_workflow_uses_exact_current_layout_transition(self) -> None:
        """The current-layout fallback must not be a broad migration waiver."""
        workflow = (_REPOSITORY_ROOT / ".github/workflows/dotnet-tests.yml").read_text(encoding="utf-8")
        self.assertNotIn("migration_paths_changed", workflow)
        self.assertNotIn("IDENTITY_GATE_REVIEWED_VALIDATION_DIRECTORY_MIGRATION", workflow)
        self.assertIn("reviewed_current_layout_paths = {", workflow)
        self.assertIn("IDENTITY_GATE_REVIEWED_CURRENT_LAYOUT_TRANSITION", workflow)
        expected_workflow_digest = "__WORKFLOW_DIGEST__"
        actual_workflow_digest = hashlib.sha256(
            (_REPOSITORY_ROOT / ".github/workflows/dotnet-tests.yml").read_bytes()
        ).hexdigest()
        self.assertEqual(expected_workflow_digest, actual_workflow_digest)

    def test_legacy_python_module_entrypoints_remain_executable(self) -> None:
        """Legacy Phase192 module paths must execute without loader mismatch."""
        commands = (
            [sys.executable, "-B", "-m", "Scripts.phase192.source_layout"],
            [sys.executable, "-B", "-m", "Scripts.phase192.identity_contracts", "--help"],
        )
        for command in commands:
            with self.subTest(command=command):
                result = subprocess.run(
                    command,
                    cwd=_REPOSITORY_ROOT,
                    capture_output=True,
                    text=True,
                    check=False,
                )
                self.assertEqual(0, result.returncode, result.stderr)

    def test_namespace_split_uses_supplied_base_revision(self) -> None:
        """The namespace split regression must not read mutable origin/main."""
        source = (
            _REPOSITORY_ROOT
            / "Scripts/validation/source_decomposition/regression_checks/test_identity_tooling/split_surface_tests.py"
        ).read_text(encoding="utf-8")
        self.assertIn("PHASE192_BASE_SHA", source)
        self.assertNotIn("origin/main:Scripts/validation/source_decomposition/identity_namespace_analysis.py", source)


if __name__ == "__main__":
    raise SystemExit(unittest.main(module=__name__))
