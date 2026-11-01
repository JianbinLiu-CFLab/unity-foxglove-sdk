from __future__ import annotations

import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

from Scripts.phase192 import compare_identity_surfaces

_REPOSITORY_ROOT = Path(__file__).resolve().parents[3]
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
        package = root / "Scripts/phase192/regression_checks/test_identity_tooling"
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
                "first = loader.discover('Scripts/phase192/regression_checks', pattern='test_identity_tooling.py', top_level_dir='.')",
                "second = loader.discover('Scripts/phase192/regression_checks', pattern='test_identity_tooling.py', top_level_dir='.')",
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


if __name__ == "__main__":
    raise SystemExit(unittest.main(module=__name__))
