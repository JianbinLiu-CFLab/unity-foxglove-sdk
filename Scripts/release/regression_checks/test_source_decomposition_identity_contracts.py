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
        repository_parent_index = len(Path(relative).parts) - 1
        facade.parent.mkdir(parents=True, exist_ok=True)
        facade.write_text(
            facade_body
            or (
                "from pathlib import Path as _Phase192Path\n"
                "import sys as _phase192_sys\n"
                f"_PHASE192_REPOSITORY_ROOT = _Phase192Path(__file__).resolve().parents[{repository_parent_index}]\n"
                "if str(_PHASE192_REPOSITORY_ROOT) not in _phase192_sys.path:\n"
                "    _phase192_sys.path.insert(0, str(_PHASE192_REPOSITORY_ROOT))\n"
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

    def test_forwarding_entrypoint_contract_rejects_missing_direct_execution_bootstrap(self) -> None:
        """A facade without the repository-root bootstrap cannot be executed directly."""
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            facade = self._write_package(
                root,
                facade_body=(
                    "from Scripts.example_tool.entrypoint import run_entrypoint\n"
                    "if __name__ == \"__main__\":\n"
                    "    raise SystemExit(run_entrypoint())\n"
                ),
            )
            environment = os.environ.copy()
            environment.pop("PYTHONPATH", None)
            direct = subprocess.run(
                [sys.executable, "-B", str(facade)],
                env=environment,
                capture_output=True,
                text=True,
                check=False,
            )
            self.assertNotEqual(0, direct.returncode)
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

    def test_entrypoint_section_rejects_decorated_helpers(self) -> None:
        """Top-level helper decorators cannot execute during section import."""
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            facade = self._write_package(
                root,
                entrypoint_body=(
                    "def register():\n"
                    "    return lambda function: function\n"
                    "@register()\n"
                    "def helper():\n"
                    "    return 0\n"
                    "def run_entrypoint():\n"
                    "    return helper()\n"
                    "__all__ = ['run_entrypoint']\n"
                ),
            )
            self.assertFalse(
                surface_compare._approved_entrypoint_section(
                    facade.with_suffix("") / "entrypoint.py"
                )
            )

    def test_entrypoint_section_rejects_duplicate_run_entrypoint_bindings(self) -> None:
        """Only one synchronous function may bind the exported entrypoint name."""
        sources = (
            (
                "def run_entrypoint():\n"
                "    return 0\n"
                "async def run_entrypoint():\n"
                "    return 1\n"
                "__all__ = ['run_entrypoint']\n"
            ),
            (
                "def run_entrypoint():\n"
                "    return 0\n"
                "from os import getpid as run_entrypoint\n"
                "__all__ = ['run_entrypoint']\n"
            ),
            (
                "def run_entrypoint():\n"
                "    return 0\n"
                "from .foundation import *\n"
                "__all__ = ['run_entrypoint']\n"
            ),
            (
                "import attacker\n"
                "def run_entrypoint():\n"
                "    return 0\n"
                "__all__ = ['run_entrypoint']\n"
            ),
        )
        for source in sources:
            with self.subTest(source=source), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                facade = self._write_package(root, entrypoint_body=source)
                self.assertFalse(
                    surface_compare._approved_entrypoint_section(
                        facade.with_suffix("") / "entrypoint.py"
                    )
                )

    def test_entrypoint_section_rejects_executable_return_annotation(self) -> None:
        """Import-time return annotations cannot execute arbitrary calls."""
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            facade = self._write_package(
                root,
                entrypoint_body=(
                    "import urllib.request\n"
                    "def run_entrypoint() -> urllib.request.urlopen('https://attacker.invalid'):\n"
                    "    return 0\n"
                    "__all__ = ['run_entrypoint']\n"
                ),
            )
            self.assertFalse(
                surface_compare._approved_entrypoint_section(
                    facade.with_suffix("") / "entrypoint.py"
                )
            )

    def test_entrypoint_section_rejects_executable_helper_signatures(self) -> None:
        """Helper defaults and annotations cannot execute during section import."""
        sources = (
            (
                "def helper(value=side_effect()):\n"
                "    return value\n"
                "def run_entrypoint():\n"
                "    return helper()\n"
                "__all__ = ['run_entrypoint']\n"
            ),
            (
                "def helper(value: side_effect()):\n"
                "    return value\n"
                "def run_entrypoint():\n"
                "    return helper(0)\n"
                "__all__ = ['run_entrypoint']\n"
            ),
        )
        for source in sources:
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
            "from .foundation import load_tests\n",
            "from .foundation import load_tests as load_tests\n",
        ):
            with self.subTest(source=source):
                self.assertFalse(
                    surface_compare._initializer_has_reviewed_discovery_shape(
                        ast.parse(source)
                    )
                )

    def test_section_discovery_contract_rejects_load_tests_exports(self) -> None:
        """Section modules cannot reintroduce a unittest discovery hook by export."""
        sources = (
            "def run_entrypoint():\n"
            "    return 0\n"
            "def load_tests(loader, tests, pattern):\n"
            "    return tests\n"
            "__all__ = ['run_entrypoint', 'load_tests']\n",
            "def run_entrypoint():\n"
            "    return 0\n"
            "__all__ = ['run_entrypoint', 'load_tests']\n",
            "def run_entrypoint():\n"
            "    return 0\n"
            "__all__ = ['run_entrypoint']\n"
            "__all__.append('load_tests')\n",
            "def run_entrypoint():\n"
            "    return 0\n"
            "_hook = lambda loader, tests, pattern: tests\n"
            "globals()['load_tests'] = _hook\n"
            "globals()['__all__'] = list(__all__) + ['load_tests']\n"
            "__all__ = ['run_entrypoint']\n",
            "import sys\n"
            "def run_entrypoint():\n"
            "    return 0\n"
            "_hook = lambda loader, tests, pattern: tests\n"
            "setattr(sys.modules[__name__], 'load_tests', _hook)\n"
            "__all__ = ['run_entrypoint']\n",
        )
        for source in sources:
            with self.subTest(source=source), tempfile.TemporaryDirectory() as temporary:
                root = Path(temporary)
                facade = self._write_package(root)
                (facade.with_suffix("") / "entrypoint.py").write_text(source, encoding="utf-8")
                self.assertFalse(
                    surface_compare._sections_have_no_discovery_hook(
                        root,
                        "Scripts/example_tool.py",
                    )
                )

    def test_initializer_path_contract_rejects_shadow_module_path(self) -> None:
        """A generated initializer must derive its facade path from the package location."""
        safe = ast.parse(
            "from pathlib import Path as _Phase192Path\n"
            "_PHASE192_FACADE_FILE = _Phase192Path(__file__).resolve().parents[1] / 'test_tool.py'\n"
            "_PHASE192_PARENT = _PHASE192_FACADE_FILE.parent\n"
        )
        shadow = ast.parse(
            "from pathlib import Path as _Phase192Path\n"
            "_PHASE192_FACADE_FILE = _Phase192Path('/tmp/attacker') / 'test_tool.py'\n"
            "_PHASE192_PARENT = _PHASE192_FACADE_FILE.parent\n"
        )
        nested = ast.parse(
            "from pathlib import Path as _Phase192Path\n"
            "_PHASE192_FACADE_FILE = _Phase192Path(__file__).resolve().parents[1] / 'test_tool.py'\n"
            "_PHASE192_PARENT = _PHASE192_FACADE_FILE.parent\n"
            "if True:\n"
            "    _PHASE192_FACADE_FILE = _Phase192Path('/tmp/attacker') / 'test_tool.py'\n"
        )
        self.assertTrue(
            surface_compare._initializer_path_is_repository_rooted(
                safe,
                "Scripts/regression_checks/test_tool.py",
            )
        )
        self.assertFalse(
            surface_compare._initializer_path_is_repository_rooted(
                shadow,
                "Scripts/regression_checks/test_tool.py",
            )
        )
        self.assertFalse(
            surface_compare._initializer_path_is_repository_rooted(
                nested,
                "Scripts/regression_checks/test_tool.py",
            )
        )

    def test_initializer_import_shape_rejects_nested_import_and_path_mutation(self) -> None:
        """Generated initializers cannot add nested imports or alternate path mutations."""
        nested_path = ast.parse(
            "if True:\n"
            "    _phase192_sys.path.insert(0, str(_PHASE192_PARENT))\n"
        )
        nested_import = ast.parse(
            "if str(_PHASE192_PARENT) not in _phase192_sys.path:\n"
            "    import unittest\n"
        )
        shadow_import = ast.parse(
            "if str(_PHASE192_PARENT) not in _phase192_sys.path:\n"
            "    _phase192_sys.path.insert(0, str(_PHASE192_PARENT))\n"
            "import unittest\n"
        )
        self.assertFalse(surface_compare._initializer_has_generated_import_shape(nested_path))
        self.assertFalse(surface_compare._initializer_has_generated_import_shape(nested_import))
        self.assertFalse(surface_compare._initializer_has_generated_import_shape(shadow_import))

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
            (base / relative.removesuffix(".py") / "data.json").write_text("base\n", encoding="utf-8")
            (head / relative.removesuffix(".py") / "data.json").write_text("head\n", encoding="utf-8")
            with mock.patch(
                "Scripts.validation.source_decomposition.compare_identity_surfaces.entrypoint_contracts._approved_initializer_contract",
                return_value=True,
            ):
                self.assertFalse(surface_compare._approved_test_facade_removal(relative, base, head))
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

    def test_discovery_contract_removal_preserves_module_entrypoint(self) -> None:
        """Removing a test facade cannot alter the retained module entrypoint."""
        with tempfile.TemporaryDirectory() as base_temporary, tempfile.TemporaryDirectory() as head_temporary:
            base = Path(base_temporary)
            head = Path(head_temporary)
            relative = "Scripts/regression_checks/test_tool.py"
            self._write_package(base, relative=relative)
            self._write_package(head, relative=relative)
            (head / relative.removesuffix(".py") / "__main__.py").write_text(
                "from .entrypoint import run_entrypoint\n"
                "if __name__ == '__main__':\n"
                "    raise SystemExit(9)\n",
                encoding="utf-8",
            )
            (head / relative).unlink()
            with mock.patch(
                "Scripts.validation.source_decomposition.compare_identity_surfaces.entrypoint_contracts._approved_initializer_contract",
                return_value=True,
            ):
                self.assertFalse(surface_compare._approved_test_facade_removal(relative, base, head))

    def test_discovery_contract_removal_rejects_added_section(self) -> None:
        """Removing a test facade cannot add a new package section."""
        with tempfile.TemporaryDirectory() as base_temporary, tempfile.TemporaryDirectory() as head_temporary:
            base = Path(base_temporary)
            head = Path(head_temporary)
            relative = "Scripts/regression_checks/test_tool.py"
            self._write_package(base, relative=relative)
            self._write_package(head, relative=relative)
            (head / relative.removesuffix(".py") / "test_new.py").write_text(
                "VALUE = 1\n__all__ = ['VALUE']\n",
                encoding="utf-8",
            )
            (head / relative).unlink()
            with mock.patch(
                "Scripts.validation.source_decomposition.compare_identity_surfaces.entrypoint_contracts._approved_initializer_contract",
                return_value=True,
            ):
                self.assertFalse(surface_compare._approved_test_facade_removal(relative, base, head))

    def test_discovery_contract_removal_rejects_nested_module_change(self) -> None:
        """Removing a test facade cannot hide changed nested Python modules."""
        with tempfile.TemporaryDirectory() as base_temporary, tempfile.TemporaryDirectory() as head_temporary:
            base = Path(base_temporary)
            head = Path(head_temporary)
            relative = "Scripts/regression_checks/test_tool.py"
            self._write_package(base, relative=relative)
            self._write_package(head, relative=relative)
            for root in (base, head):
                nested = root / relative.removesuffix(".py") / "subpkg"
                nested.mkdir()
                (nested / "__init__.py").write_text("VALUE = 1\n", encoding="utf-8")
                (nested / "nested.py").write_text("VALUE = 1\n", encoding="utf-8")
            (head / relative.removesuffix(".py") / "subpkg/nested.py").write_text(
                "VALUE = 2\n",
                encoding="utf-8",
            )
            (head / relative).unlink()
            with mock.patch(
                "Scripts.validation.source_decomposition.compare_identity_surfaces.entrypoint_contracts._approved_initializer_contract",
                return_value=True,
            ):
                self.assertFalse(surface_compare._approved_test_facade_removal(relative, base, head))

    def test_discovery_contract_removal_accepts_testfoo_pattern(self) -> None:
        """Removing any unittest-discoverable test facade uses the same contract."""
        with tempfile.TemporaryDirectory() as base_temporary, tempfile.TemporaryDirectory() as head_temporary:
            base = Path(base_temporary)
            head = Path(head_temporary)
            relative = "Scripts/regression_checks/testfoo.py"
            self._write_package(base, relative=relative)
            self._write_package(head, relative=relative)
            (head / relative).unlink()
            with mock.patch(
                "Scripts.validation.source_decomposition.compare_identity_surfaces.entrypoint_contracts._approved_initializer_contract",
                return_value=True,
            ):
                self.assertTrue(surface_compare._approved_test_facade_removal(relative, base, head))

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

    def test_discovery_contract_normalizes_removed_directory_migration(self) -> None:
        """A removed legacy facade can validate its retained migrated package."""
        with tempfile.TemporaryDirectory() as base_temporary, tempfile.TemporaryDirectory() as head_temporary:
            base = Path(base_temporary)
            head = Path(head_temporary)
            relative = "Scripts/phase192/regression_checks/test_tool.py"
            self._write_package(base, relative=relative)
            self._write_package(
                head,
                relative="Scripts/validation/source_decomposition/regression_checks/test_tool.py",
            )
            (head / "Scripts/validation/source_decomposition/regression_checks/test_tool.py").unlink()
            with mock.patch(
                "Scripts.validation.source_decomposition.compare_identity_surfaces.entrypoint_contracts._approved_initializer_contract",
                return_value=True,
            ):
                self.assertEqual(
                    [],
                    surface_compare._discovery_contract_errors(
                        base,
                        head,
                        allow_directory_migration=True,
                    ),
                )

    def test_discovery_contract_preserves_legacy_paths_without_migration(self) -> None:
        """Legacy paths stay raw when directory migration is disabled."""
        with tempfile.TemporaryDirectory() as base_temporary, tempfile.TemporaryDirectory() as head_temporary:
            base = Path(base_temporary)
            head = Path(head_temporary)
            relative = "Scripts/phase192/regression_checks/test_tool.py"
            self._write_package(base, relative=relative)
            self._write_package(head, relative=relative)
            (head / relative).unlink()
            with mock.patch(
                "Scripts.validation.source_decomposition.compare_identity_surfaces.entrypoint_contracts._approved_initializer_contract",
                return_value=True,
            ):
                self.assertEqual([], surface_compare._discovery_contract_errors(base, head))

    def test_discovery_contract_rejects_migration_path_collision(self) -> None:
        """A migration cannot retain both raw paths for one canonical facade."""
        with tempfile.TemporaryDirectory() as base_temporary, tempfile.TemporaryDirectory() as head_temporary:
            base = Path(base_temporary)
            head = Path(head_temporary)
            self._write_package(
                base,
                relative="Scripts/phase192/regression_checks/test_tool.py",
            )
            self._write_package(
                head,
                relative="Scripts/phase192/regression_checks/test_tool.py",
            )
            self._write_package(
                head,
                relative="Scripts/validation/source_decomposition/regression_checks/test_tool.py",
            )
            errors = surface_compare._discovery_contract_errors(
                base,
                head,
                allow_directory_migration=True,
            )
            self.assertTrue(any(error.startswith("DUPLICATE_DISCOVERY_FACADE") for error in errors))

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
            )
            base = self._commit(repository, "base")
            self._write_package(repository)
            head = self._commit(repository, "head")
            with mock.patch(
                "Scripts.validation.source_decomposition.compare_identity_surfaces.entrypoint_contracts._approved_initializer_contract",
                return_value=True,
            ):
                self.assertEqual(0, surface_compare.compare_revisions(repository, base, head, strict=False))

    def test_migrated_module_entrypoint_changes_are_not_waived(self) -> None:
        """Directory migration does not waive a changed retained module entrypoint."""
        def surface(digest: str) -> tuple:
            """Build a minimal surface with one module entrypoint digest."""
            return (
                frozenset(), (), (), frozenset(), None, frozenset(), True, (),
                frozenset(), (), digest, ("entrypoint",), (), (), frozenset(), (),
            )

        relative = "Scripts/phase192/regression_checks/test_tool.py"
        errors = surface_compare._compare_surfaces(
            {relative: surface("base")},
            {relative: surface("head")},
            strict=False,
            allow_directory_migration=True,
            base_root=Path("base"),
            head_root=Path("head"),
        )
        self.assertEqual([f"MODULE_ENTRYPOINT_CHANGED {relative}"], errors)

    def test_namespace_approval_resolves_migrated_head_path(self) -> None:
        """Namespace approval receives the current path during a supported directory migration."""
        def surface(risks: frozenset[str]) -> tuple:
            """Build the minimal identity surface needed by the comparison."""
            return (
                frozenset(), (), (), frozenset(), None, frozenset(), None, (),
                risks, (), None, (), (), (), frozenset(), (),
            )

        relative = "Scripts/phase192/regression_checks/test_tool.py"
        with tempfile.TemporaryDirectory() as base_temporary, tempfile.TemporaryDirectory() as head_temporary:
            base = Path(base_temporary)
            head = Path(head_temporary)
            with mock.patch.object(surface_compare, "_approved_namespace_change", return_value=True) as approve:
                errors = surface_compare._compare_surfaces(
                    {relative: surface(frozenset({"base-risk"}))},
                    {relative: surface(frozenset())},
                    strict=False,
                    allow_directory_migration=True,
                    base_root=base,
                    head_root=head,
                )
            self.assertEqual([], errors)
            approve.assert_called_once_with(
                relative,
                base,
                head,
                head_relative="Scripts/validation/source_decomposition/regression_checks/test_tool.py",
            )

    def test_entrypoint_parity_probe_matches_direct_and_module_launch(self) -> None:
        """Direct and module launches return the same injected failure code and stderr."""
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            relative = "Scripts/example/tool.py"
            self._write_package(
                root,
                relative=relative,
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
            environment.pop("PYTHONPATH", None)
            environment["PHASE192_PROBE"] = "probe"
            direct = subprocess.run(
                [sys.executable, "-B", str(root / relative)],
                env=environment,
                capture_output=True,
                text=True,
                check=False,
            )
            module = subprocess.run(
                [sys.executable, "-B", "-m", "Scripts.example.tool"],
                cwd=root,
                env=environment,
                capture_output=True,
                text=True,
                check=False,
            )
            self.assertEqual((direct.returncode, direct.stderr), (module.returncode, module.stderr))


if __name__ == "__main__":
    unittest.main()
