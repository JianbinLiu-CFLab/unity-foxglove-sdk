from __future__ import annotations

import os
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

from Scripts.validation.source_decomposition import compare_identity_surfaces

_REPOSITORY_ROOT = Path(__file__).resolve().parents[4]
_PHASE192_TRUSTED_BASE_SHA = "d83d9a897041b432c8eea220e48ba29c0be62258"
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

    def test_workflow_fails_closed_on_trusted_identity_errors(self) -> None:
        """The workflow must not interpret trusted checker failures itself."""
        workflow = (_REPOSITORY_ROOT / ".github/workflows/dotnet-tests.yml").read_text(encoding="utf-8")
        self.assertNotIn("reviewed_current_layout_paths", workflow)
        self.assertNotIn("IDENTITY_GATE_REVIEWED_CURRENT_LAYOUT_TRANSITION", workflow)
        self.assertNotIn("expected_paths = (", workflow)
        self.assertNotIn("cleanup_lines = [", workflow)
        self.assertNotIn("IDENTITY_GATE_REVIEWED_VALIDATOR_ISOLATION_TRANSITION", workflow)
        self.assertNotIn('python3 - "$trusted_log"', workflow)
        self.assertIn('if [ "$trusted_status" -ne 0 ]; then', workflow)
        self.assertIn('cat "$trusted_log"', workflow)
        self.assertIn('exit "$trusted_status"', workflow)

    def test_workflow_propagates_a_trusted_checker_failure(self) -> None:
        """A non-zero trusted checker result exits before any head-side gate runs."""
        if sys.platform == "win32":
            self.skipTest("the workflow shell is executed in the hosted Linux lane")
        bash = shutil.which("bash")
        if bash is None:
            self.skipTest("bash is required to execute the workflow shell")
        workflow = (_REPOSITORY_ROOT / ".github/workflows/dotnet-tests.yml").read_text(encoding="utf-8")
        lines = workflow.splitlines()
        start = next(
            index
            for index, line in enumerate(lines)
            if line.strip() == 'trusted_log="$RUNNER_TEMP/phase192-trusted-base.log"'
        )
        end = next(
            index
            for index in range(start + 1, len(lines))
            if lines[index].strip() == "# Run the checked-in head checker as a second gate so newly supported"
        )
        script = "\n".join(
            line[10:] if line.startswith("          ") else line
            for line in lines[start:end]
        )
        command_start = script.index("set +e\n")
        command_end = script.index("trusted_status=$?\n", command_start)
        replacement = (
            "set +e\n"
            "python3 -c 'import sys; print(\"TRUSTED_FAILURE\"); sys.exit(17)' "
            ">\"$trusted_log\" 2>&1\n"
        )
        script = script[:command_start] + replacement + script[command_end:]
        with tempfile.TemporaryDirectory() as temporary:
            environment = os.environ.copy()
            environment["RUNNER_TEMP"] = temporary
            result = subprocess.run(
                [bash, "-c", script],
                capture_output=True,
                text=True,
                env=environment,
                check=False,
            )
        self.assertEqual(17, result.returncode, result.stdout + result.stderr)
        self.assertIn("TRUSTED_FAILURE", result.stdout)

    def test_phase146b_migration_is_checker_first_and_current_baseline_is_split(self) -> None:
        """The completed Phase146B split is documented as fail-closed for future migrations."""
        documentation = (
            _REPOSITORY_ROOT / "Scripts/validation/source_decomposition/README.md"
        ).read_text(encoding="utf-8")
        self.assertIn("Phase146B", documentation)
        self.assertIn("trusted base", documentation)
        self.assertIn("fails closed", documentation)
        self.assertTrue(
            (
                _REPOSITORY_ROOT
                / "Scripts/ros2forunity/windows/lyrical/phase146b_r2fu_lyrical_windows_build.py"
            ).is_file()
        )
        self.assertTrue(
            (
                _REPOSITORY_ROOT
                / "Scripts/ros2forunity/windows/lyrical/phase146b_r2fu_lyrical_windows_build/implementation.py"
            ).is_file()
        )

    def test_phase146b_baseline_identity_contract_covers_facade_and_section(self) -> None:
        """The Phase146B facade and implementation section have a checked identity contract."""
        relative = (
            "Scripts/ros2forunity/windows/lyrical/phase146b_r2fu_lyrical_windows_build.py"
        )
        facade = _REPOSITORY_ROOT / relative
        surface = compare_identity_surfaces._surface(_REPOSITORY_ROOT, facade, reject_conditional=False)
        with compare_identity_surfaces._revision_checkout(
            _REPOSITORY_ROOT,
            _PHASE192_TRUSTED_BASE_SHA,
        ) as trusted_root:
            trusted_surface = compare_identity_surfaces._surface(
                trusted_root,
                trusted_root / relative,
                reject_conditional=False,
            )
        self.assertEqual(
            ("safety_and_environment", "build_pipeline", "evidence"),
            trusted_surface[1],
        )
        self.assertEqual(trusted_surface[1], surface[1])
        self.assertEqual(trusted_surface[0], surface[0])
        self.assertEqual(trusted_surface[4], surface[4])
        self.assertIn("run_upstream_build", trusted_surface[0])
        self.assertIn("write_evidence", trusted_surface[0])
        self.assertTrue(trusted_surface[4])
        self.assertEqual(
            [],
            compare_identity_surfaces._compare_surfaces(
                {relative: trusted_surface},
                {relative: surface},
                strict=True,
            ),
        )
        mutated = list(surface)
        mutated[0] = frozenset(set(surface[0]) | {"UNREVIEWED_EXPORT"})
        mutation_errors = compare_identity_surfaces._compare_surfaces(
            {relative: trusted_surface},
            {relative: tuple(mutated)},
            strict=True,
        )
        self.assertTrue(
            any(error.startswith(f"EXTRA_SYMBOLS {relative}") for error in mutation_errors)
        )

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
