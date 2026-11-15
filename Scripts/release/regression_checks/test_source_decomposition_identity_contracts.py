#!/usr/bin/env python3
"""Regression tests for the positive Phase192 identity contracts."""

from __future__ import annotations

import ast
import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest import mock

from Scripts.validation.source_decomposition.compare_identity_surfaces import surface_compare


class SourceDecompositionIdentityContractTests(unittest.TestCase):
    """Exercise the reviewed entrypoint, initializer, discovery, and wiring contracts."""

    def _write_package(
        self,
        root: Path,
        *,
        facade_body: str | None = None,
        module_body: str | None = None,
        entrypoint_body: str | None = None,
        initializer_body: str | None = None,
        relative: str = "Scripts/example_tool.py",
    ) -> Path:
        """Create a hermetic split facade fixture."""
        facade = root / relative
        package = facade.with_suffix("")
        package.mkdir(parents=True, exist_ok=True)
        module_name = ".".join(Path(relative).with_suffix("").parts)
        facade.parent.mkdir(parents=True, exist_ok=True)
        facade.write_text(
            facade_body
            or (
                f"from {module_name}.entrypoint import run_entrypoint\n"
                "if __name__ == \"__main__\":\n"
                "    raise SystemExit(run_entrypoint())\n"
            ),
            encoding="utf-8",
        )
        (package / "__main__.py").write_text(
            module_body
            or (
                "from .entrypoint import run_entrypoint\n"
                "if __name__ == \"__main__\":\n"
                "    raise SystemExit(run_entrypoint())\n"
            ),
            encoding="utf-8",
        )
        (package / "entrypoint.py").write_text(
            entrypoint_body
            or (
                "def run_entrypoint():\n"
                "    return 0\n"
                "__all__ = [\"run_entrypoint\"]\n"
            ),
            encoding="utf-8",
        )
        (package / "__init__.py").write_text(
            initializer_body or "from .entrypoint import *\n",
            encoding="utf-8",
        )
        return facade

    @staticmethod
    def _commit(repository: Path, message: str) -> str:
        """Commit the fixture tree and return its revision."""
        subprocess.run(["git", "add", "Scripts"], cwd=repository, check=True)
        subprocess.run(
            [
                "git",
                "-c",
                "user.name=fixture",
                "-c",
                "user.email=fixture@example.invalid",
                "commit",
                "-q",
                "-m",
                message,
            ],
            cwd=repository,
            check=True,
        )
        return subprocess.run(
            ["git", "rev-parse", "HEAD"],
            cwd=repository,
            check=True,
            capture_output=True,
            text=True,
        ).stdout.strip()

    def test_forwarding_entrypoint_contract_accepts_standard_template(self) -> None:
        """A facade and module entrypoint that import the section wrapper pass."""
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            self._write_package(root)
            self.assertTrue(
                surface_compare._approved_entrypoint_contract(
                    root,
                    "Scripts/example_tool.py",
                )
            )

    def test_forwarding_entrypoint_contract_rejects_inline_and_shadowed_logic(self) -> None:
        """Inline logic, a second main block, and a local wrapper binding are rejected."""
        variants = (
            (
                "if __name__ == \"__main__\":\n"
                "    try:\n"
                "        raise SystemExit(run_entrypoint())\n"
                "    except Exception:\n"
                "        raise SystemExit(1)\n",
                "inline exception mapping",
            ),
            (
                "if __name__ == \"__main__\":\n"
                "    print('unexpected')\n"
                "if __name__ == \"__main__\":\n"
                "    raise SystemExit(run_entrypoint())\n",
                "multiple main blocks",
            ),
            (
                "def run_entrypoint():\n"
                "    return 9\n"
                "if __name__ == \"__main__\":\n"
                "    raise SystemExit(run_entrypoint())\n",
                "shadowed wrapper",
            ),
        )
        for body, label in variants:
            with self.subTest(variant=label), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                self._write_package(root, facade_body=body, module_body=body)
                self.assertFalse(
                    surface_compare._approved_entrypoint_contract(
                        root,
                        "Scripts/example_tool.py",
                    )
                )

    def test_entrypoint_section_rejects_import_time_side_effect(self) -> None:
        """A newly approved entrypoint section cannot mutate process state on import."""
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            facade = self._write_package(
                root,
                entrypoint_body=(
                    "import os\n"
                    "os.environ['PHASE192_BAD'] = '1'\n"
                    "def run_entrypoint():\n"
                    "    return 0\n"
                    "__all__ = ['run_entrypoint']\n"
                ),
            )
            self.assertFalse(
                surface_compare._approved_entrypoint_section(
                    facade.with_suffix("") / "entrypoint.py"
                )
            )

    def test_entrypoint_section_rejects_async_and_required_arguments(self) -> None:
        """The synchronous zero-argument wrapper contract rejects incompatible callables."""
        for source in (
            "async def run_entrypoint():\n    return 0\n__all__ = ['run_entrypoint']\n",
            "def run_entrypoint(required):\n    return required\n__all__ = ['run_entrypoint']\n",
        ):
            with self.subTest(source=source), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                facade = self._write_package(root, entrypoint_body=source)
                self.assertFalse(
                    surface_compare._approved_entrypoint_section(
                        facade.with_suffix("") / "entrypoint.py"
                    )
                )

    def test_initializer_discovery_contract_rejects_sentinel_variants(self) -> None:
        """A mutable load-tests hook is rejected even when its sentinel is renamed."""
        accepted = ast.parse("from .entrypoint import *\n")
        self.assertTrue(surface_compare._initializer_has_reviewed_discovery_shape(accepted))
        for source in (
            "_phase192_load_tests_used = False\n",
            "def load_tests(loader, tests, pattern):\n    return tests\n",
            "def load_tests_once(loader, tests, pattern):\n    return loader.suiteClass()\n",
            "load_tests = lambda loader, tests, pattern: tests\n",
            "load_tests_once = lambda loader, tests, pattern: tests\n",
        ):
            with self.subTest(source=source):
                self.assertFalse(
                    surface_compare._initializer_has_reviewed_discovery_shape(
                        ast.parse(source)
                    )
                )

    def test_discovery_contract_rejects_new_duplicate_facade(self) -> None:
        """A test facade beside a test package is rejected as duplicate discovery."""
        with tempfile.TemporaryDirectory() as base_temporary, tempfile.TemporaryDirectory() as head_temporary:
            base = Path(base_temporary)
            head = Path(head_temporary)
            base_package = base / "Scripts/regression_checks/test_tool"
            base_package.mkdir(parents=True)
            (base_package / "__init__.py").write_text("from .entrypoint import *\n", encoding="utf-8")
            (base_package / "entrypoint.py").write_text(
                "def run_entrypoint():\n    return 0\n__all__ = ['run_entrypoint']\n",
                encoding="utf-8",
            )
            self._write_package(head, relative="Scripts/regression_checks/test_tool.py")
            self.assertEqual(
                ["DUPLICATE_DISCOVERY_FACADE Scripts/regression_checks/test_tool.py"],
                surface_compare._discovery_contract_errors(base, head),
            )
            base_package = base / "Scripts/regression_checks/testfoo"
            base_package.mkdir(parents=True)
            (base_package / "__init__.py").write_text("from .entrypoint import *\n", encoding="utf-8")
            (base_package / "entrypoint.py").write_text(
                "def run_entrypoint():\n    return 0\n__all__ = ['run_entrypoint']\n",
                encoding="utf-8",
            )
            self._write_package(head, relative="Scripts/regression_checks/testfoo.py")
            self.assertIn(
                "DUPLICATE_DISCOVERY_FACADE Scripts/regression_checks/testfoo.py",
                surface_compare._discovery_contract_errors(base, head),
            )

    def test_discovery_contract_removal_preserves_sections(self) -> None:
        """Removing a test facade cannot remove or alter any package section."""
        with tempfile.TemporaryDirectory() as base_temporary, tempfile.TemporaryDirectory() as head_temporary:
            base = Path(base_temporary)
            head = Path(head_temporary)
            relative = "Scripts/regression_checks/test_tool.py"
            self._write_package(base, relative=relative)
            self._write_package(head, relative=relative)
            (head / relative).unlink()
            with mock.patch(
                "Scripts.validation.source_decomposition.compare_identity_surfaces.entrypoint_contracts._approved_initializer_contract",
                return_value=True,
            ):
                self.assertTrue(surface_compare._approved_test_facade_removal(relative, base, head))
            (head / relative.removesuffix(".py") / "entrypoint.py").write_text(
                "def run_entrypoint():\n    return 1\n__all__ = ['run_entrypoint']\n",
                encoding="utf-8",
            )
            with mock.patch(
                "Scripts.validation.source_decomposition.compare_identity_surfaces.entrypoint_contracts._approved_initializer_contract",
                return_value=True,
            ):
                self.assertFalse(surface_compare._approved_test_facade_removal(relative, base, head))

    def test_discovery_contract_removal_preserves_manifest_order(self) -> None:
        """Removing a test facade cannot reorder manifest-backed sections."""
        with tempfile.TemporaryDirectory() as base_temporary, tempfile.TemporaryDirectory() as head_temporary:
            base = Path(base_temporary)
            head = Path(head_temporary)
            relative = "Scripts/regression_checks/test_tool.py"
            self._write_package(base, relative=relative)
            self._write_package(head, relative=relative)
            for root in (base, head):
                package = root / relative.removesuffix(".py")
                (package / "extra.py").write_text(
                    "VALUE = 1\n__all__ = ['VALUE']\n",
                    encoding="utf-8",
                )
            base_package = base / relative.removesuffix(".py")
            head_package = head / relative.removesuffix(".py")
            (base_package / "sections.manifest").write_text(
                "entrypoint.py\nextra.py\n",
                encoding="utf-8",
            )
            (head_package / "sections.manifest").write_text(
                "extra.py\nentrypoint.py\n",
                encoding="utf-8",
            )
            (head / relative).unlink()
            with mock.patch(
                "Scripts.validation.source_decomposition.compare_identity_surfaces.entrypoint_contracts._approved_initializer_contract",
                return_value=True,
            ):
                self.assertFalse(surface_compare._approved_test_facade_removal(relative, base, head))

    def test_discovery_contract_normalizes_directory_migration(self) -> None:
        """A supported Phase192 directory migration is compared by canonical path."""
        with tempfile.TemporaryDirectory() as base_temporary, tempfile.TemporaryDirectory() as head_temporary:
            base = Path(base_temporary)
            head = Path(head_temporary)
            self._write_package(
                base,
                relative="Scripts/phase192/regression_checks/test_tool.py",
            )
            self._write_package(
                head,
                relative="Scripts/validation/source_decomposition/regression_checks/test_tool.py",
            )
            self.assertEqual(
                [],
                surface_compare._discovery_contract_errors(
                    base,
                    head,
                    allow_directory_migration=True,
                ),
            )

    def test_compare_revisions_uses_checkout_roots_before_cleanup(self) -> None:
        """The compatibility gate can approve a valid entrypoint migration end to end."""
        with tempfile.TemporaryDirectory() as temporary:
            repository = Path(temporary)
            subprocess.run(["git", "init", "-q"], cwd=repository, check=True)
            self._write_package(
                repository,
                facade_body=(
                    "from Scripts.example_tool.entrypoint import run_entrypoint\n"
                    "if __name__ == \"__main__\":\n"
                    "    raise SystemExit(run_entrypoint())\n"
                ),
                module_body=(
                    "from .entrypoint import *\n"
                    "if __name__ == \"__main__\":\n"
                    "    raise SystemExit(run_entrypoint())\n"
                ),
            )
            base = self._commit(repository, "base")
            (repository / "Scripts/example_tool.py").write_text(
                "from Scripts.example_tool.entrypoint import run_entrypoint\n"
                "if __name__ == \"__main__\":\n"
                "    raise SystemExit(run_entrypoint())\n",
                encoding="utf-8",
            )
            (repository / "Scripts/example_tool/__main__.py").write_text(
                "from .entrypoint import run_entrypoint\n"
                "if __name__ == \"__main__\":\n"
                "    raise SystemExit(run_entrypoint())\n",
                encoding="utf-8",
            )
            head = self._commit(repository, "head")
            self.assertEqual(0, surface_compare.compare_revisions(repository, base, head, strict=False))

    def test_entrypoint_parity_probe_matches_direct_and_module_launch(self) -> None:
        """Direct and module launches return the same injected failure code and stderr."""
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            self._write_package(
                root,
                entrypoint_body=(
                    "import os\n"
                    "import sys\n"
                    "def run_entrypoint():\n"
                    "    print(os.environ['PHASE192_PROBE'], file=sys.stderr)\n"
                    "    return 7\n"
                    "__all__ = ['run_entrypoint']\n"
                ),
            )
            environment = os.environ.copy()
            environment["PYTHONPATH"] = str(root)
            environment["PHASE192_PROBE"] = "probe"
            direct = subprocess.run(
                [sys.executable, "-B", str(root / "Scripts/example_tool.py")],
                env=environment,
                capture_output=True,
                text=True,
                check=False,
            )
            module = subprocess.run(
                [sys.executable, "-B", "-m", "Scripts.example_tool"],
                cwd=root,
                env=environment,
                capture_output=True,
                text=True,
                check=False,
            )
            self.assertEqual((direct.returncode, direct.stderr), (module.returncode, module.stderr))


if __name__ == "__main__":
    unittest.main()
