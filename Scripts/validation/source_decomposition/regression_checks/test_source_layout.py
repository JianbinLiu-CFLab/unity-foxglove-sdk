"""Regression tests for the shared source-layout helpers."""
from __future__ import annotations

import importlib
import sys
import tempfile
import types
import unittest
from pathlib import Path
from unittest import mock

from Scripts.validation.source_decomposition import source_layout


class SourceLayoutTests(unittest.TestCase):
    """Exercise source-layout loading and section-discovery behavior."""

    def test_load_fresh_module_removes_failed_module_registration(self) -> None:
        """A failed fresh load must not poison a later test through sys.modules."""
        import sys
        with tempfile.TemporaryDirectory() as temp:
            facade = Path(temp) / "fixture.py"
            facade.write_text("raise RuntimeError('boom')\n", encoding="utf-8")
            sys.modules.pop("fixture", None)
            with self.assertRaisesRegex(RuntimeError, "boom"):
                source_layout.load_fresh_module("fixture", facade)
            self.assertNotIn("fixture", sys.modules)

    def test_load_fresh_module_removes_new_local_modules_on_failure(self) -> None:
        """A failed load must remove newly imported modules from the facade directory."""
        import sys

        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            facade = root / "fixture.py"
            helper = root / "helper.py"
            helper.write_text("VALUE = 1\n", encoding="utf-8")
            facade.write_text("import helper\nraise RuntimeError('boom')\n", encoding="utf-8")
            sys.modules.pop("helper", None)
            try:
                with self.assertRaisesRegex(RuntimeError, "boom"):
                    source_layout.load_fresh_module("fixture", facade)
                self.assertNotIn("helper", sys.modules)
            finally:
                sys.modules.pop("helper", None)

    def test_load_fresh_module_removes_new_nested_local_modules_on_failure(self) -> None:
        """A failed load must remove newly imported nested packages from the facade directory."""
        import sys

        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            helper_dir = root / "helper"
            helper_dir.mkdir()
            (helper_dir / "__init__.py").write_text("\n", encoding="utf-8")
            (helper_dir / "sub.py").write_text("VALUE = 1\n", encoding="utf-8")
            facade = root / "fixture.py"
            facade.write_text(
                "import helper.sub\nraise RuntimeError('boom')\n",
                encoding="utf-8",
            )
            for name in ("helper", "helper.sub"):
                sys.modules.pop(name, None)
            try:
                with self.assertRaisesRegex(RuntimeError, "boom"):
                    source_layout.load_fresh_module("fixture", facade)
                self.assertNotIn("helper", sys.modules)
                self.assertNotIn("helper.sub", sys.modules)
            finally:
                for name in ("helper.sub", "helper"):
                    sys.modules.pop(name, None)

    def test_load_fresh_module_clears_preloaded_nested_local_modules(self) -> None:
        """A fresh load must not reuse preloaded nested packages from the facade directory."""
        import sys

        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            helper_dir = root / "helper"
            helper_dir.mkdir()
            (helper_dir / "__init__.py").write_text("\n", encoding="utf-8")
            sub_path = helper_dir / "sub.py"
            sub_path.write_text("VALUE = 'old'\n", encoding="utf-8")
            facade = root / "fixture.py"
            facade.write_text(
                "import helper.sub\nVALUE = helper.sub.VALUE\n",
                encoding="utf-8",
            )
            original_path = list(sys.path)
            sys.path.insert(0, str(root))
            try:
                import helper.sub

                sub_path.write_text("VALUE = 'new-value'\n", encoding="utf-8")
                module = source_layout.load_fresh_module("fixture", facade)
                self.assertEqual("new-value", module.VALUE)
            finally:
                sys.path[:] = original_path
                for name in ("fixture", "helper.sub", "helper"):
                    sys.modules.pop(name, None)

    def test_load_fresh_module_removes_orphaned_namespace_descendants(self) -> None:
        """A fresh load must remove namespace descendants with cleared parents."""
        import importlib
        import sys

        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            facade = root / "Scripts" / "facade.py"
            module_path = root / "Scripts" / "fixture_ns" / "child" / "mod.py"
            facade.parent.mkdir(parents=True)
            module_path.parent.mkdir(parents=True)
            facade.write_text("VALUE = 1\n", encoding="utf-8")
            module_path.write_text("VALUE = 2\n", encoding="utf-8")
            original_path = list(sys.path)
            saved_modules = {
                name: module
                for name, module in sys.modules.items()
                if name == "Scripts" or name.startswith("Scripts.")
            }
            missing = object()
            previous_fixture = sys.modules.get("phase_namespace_fixture", missing)
            for name in tuple(saved_modules):
                sys.modules.pop(name, None)
            sys.path.insert(0, str(root))
            sys.path.insert(1, str(root / "."))
            try:
                importlib.invalidate_caches()
                importlib.import_module("Scripts.fixture_ns.child")
                source_layout.load_fresh_module("phase_namespace_fixture", facade)
                orphaned = [
                    name
                    for name in sys.modules
                    if name.startswith("Scripts.fixture_ns.")
                    and "Scripts.fixture_ns" not in sys.modules
                ]
                self.assertEqual([], orphaned)
                module = importlib.import_module("Scripts.fixture_ns.child.mod")
                self.assertEqual(2, module.VALUE)
            finally:
                sys.path[:] = original_path
                for name in tuple(sys.modules):
                    if name == "Scripts" or name.startswith("Scripts."):
                        sys.modules.pop(name, None)
                sys.modules.pop("phase_namespace_fixture", None)
                if previous_fixture is not missing:
                    sys.modules["phase_namespace_fixture"] = previous_fixture
                sys.modules.update(saved_modules)

    def test_load_fresh_module_restores_path_for_import_setup_failures(self) -> None:
        """Import setup failures must restore the caller's sys.path exactly."""
        import sys

        with tempfile.TemporaryDirectory() as temp:
            facade = Path(temp) / "fixture.py"
            facade.write_text("VALUE = 1\n", encoding="utf-8")
            original_path = list(sys.path)
            cases = (
                (None, None),
                (types.SimpleNamespace(loader=None), None),
                (types.SimpleNamespace(loader=object()), RuntimeError("module init failed")),
            )
            for spec, module_error in cases:
                with self.subTest(spec=spec, module_error=module_error):
                    with mock.patch.object(
                        source_layout.importlib.util,
                        "spec_from_file_location",
                        return_value=spec,
                    ):
                        module_from_spec = mock.patch.object(
                            source_layout.importlib.util,
                            "module_from_spec",
                            side_effect=module_error,
                        )
                        with module_from_spec:
                            with self.assertRaises(RuntimeError):
                                source_layout.load_fresh_module("fixture", facade)
                    self.assertEqual(original_path, sys.path)

    def test_load_fresh_module_restores_cleared_modules_on_failure(self) -> None:
        """A failed fresh load must restore modules removed during isolation."""
        import sys

        with tempfile.TemporaryDirectory() as temp:
            facade = Path(temp) / "fixture.py"
            facade.write_text("raise RuntimeError('boom')\n", encoding="utf-8")
            sentinel = types.ModuleType("fixture")
            sys.modules["fixture"] = sentinel
            original_path = list(sys.path)
            try:
                with self.assertRaisesRegex(RuntimeError, "boom"):
                    source_layout.load_fresh_module("fixture", facade)
                self.assertIs(sentinel, sys.modules["fixture"])
                self.assertEqual(original_path, sys.path)
            finally:
                if sys.modules.get("fixture") is sentinel:
                    sys.modules.pop("fixture", None)

    def test_source_layout_rejects_duplicate_declared_sections(self) -> None:
        """The shared AST authority must reject repeated section imports."""
        with tempfile.TemporaryDirectory() as temp:
            package = Path(temp) / "fixture"
            package.mkdir()
            (package / "__init__.py").write_text(
                "from . import live\nfrom . import live\n", encoding="utf-8"
            )
            (package / "live.py").write_text("LIVE = 1\n__all__ = [name for name in globals() if not name.startswith('__')]\n", encoding="utf-8")
            from Scripts.validation.source_decomposition.source_layout import section_modules
            with self.assertRaises(ValueError):
                section_modules(package)

    def test_load_fresh_module_preserves_real_scripts_package_graph_across_checkouts(self) -> None:
        """Fresh loads keep every Scripts parent package valid for real facade paths."""
        import sys

        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            package_names = (
                "package",
                "smoke",
                "smoke/foxglove",
                "smoke/foxrun",
                "unity_build",
                "ros2forunity",
                "validation",
                "validation/source_decomposition",
            )
            checkouts = []
            for marker in ("A", "B"):
                checkout = root / marker
                scripts = checkout / "Scripts"
                scripts.mkdir(parents=True)
                (scripts / "__init__.py").write_text(
                    f"MARKER = {marker!r}\n", encoding="utf-8"
                )
                for package_name in package_names:
                    package = scripts / package_name
                    package.mkdir(parents=True, exist_ok=True)
                    (package / "__init__.py").write_text(
                        f"MARKER = {marker!r}\n", encoding="utf-8"
                    )
                facade = scripts / "smoke/foxrun/phase184_profile_acceptance.py"
                facade.write_text(
                    "from Scripts import MARKER as SCRIPTS_MARKER\n"
                    "from Scripts.package import MARKER as PACKAGE_MARKER\n"
                    "from Scripts.smoke import MARKER as SMOKE_MARKER\n"
                    "from Scripts.smoke.foxglove import MARKER as FOXGLOVE_MARKER\n"
                    "from Scripts.smoke.foxrun import MARKER as FOXRUN_MARKER\n"
                    "from Scripts.unity_build import MARKER as UNITY_BUILD_MARKER\n"
                    "from Scripts.ros2forunity import MARKER as ROS2FORUNITY_MARKER\n"
                    "from Scripts.validation.source_decomposition import MARKER as PHASE192_MARKER\n"
                    "VALUE = (SCRIPTS_MARKER, PACKAGE_MARKER, SMOKE_MARKER, FOXGLOVE_MARKER, FOXRUN_MARKER, UNITY_BUILD_MARKER, ROS2FORUNITY_MARKER, PHASE192_MARKER)\n",
                    encoding="utf-8",
                )
                checkouts.append((checkout, facade))

            names = [
                "phase184_profile_acceptance",
                "Scripts",
                "Scripts.package",
                "Scripts.smoke",
                "Scripts.smoke.foxglove",
                "Scripts.smoke.foxrun",
                "Scripts.unity_build",
                "Scripts.ros2forunity",
                "Scripts.validation",
                "Scripts.validation.source_decomposition",
            ]
            saved_modules = dict(sys.modules)
            for name in names:
                sys.modules.pop(name, None)
            try:
                first = source_layout.load_fresh_module("phase184_profile_acceptance", checkouts[0][1])
                self.assertEqual(("A",) * 8, first.VALUE)
                second = source_layout.load_fresh_module("phase184_profile_acceptance", checkouts[1][1])
                self.assertEqual(("B",) * 8, second.VALUE)
                for name in names[1:]:
                    module = sys.modules[name]
                    self.assertIn(str(checkouts[1][0]), str(module.__file__))
            finally:
                for name in tuple(sys.modules):
                    if name == "Scripts" or name.startswith("Scripts."):
                        if name not in saved_modules:
                            sys.modules.pop(name, None)
                sys.modules.update(saved_modules)

    def test_load_fresh_module_removes_arbitrary_source_aliases(self) -> None:
        """Fresh loads remove aliases for source modules without a hardcoded package list."""
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            scripts = root / "Scripts"
            tool = scripts / "tool"
            tool.mkdir(parents=True)
            (scripts / "__init__.py").write_text("", encoding="utf-8")
            (tool / "__init__.py").write_text(
                "from Scripts.tool.implementation import VALUE\n",
                encoding="utf-8",
            )
            (tool / "implementation.py").write_text(
                "VALUE = 'fresh'\n",
                encoding="utf-8",
            )
            facade = tool / "facade.py"
            facade.write_text(
                "from Scripts.tool.implementation import VALUE\n",
                encoding="utf-8",
            )
            alias_module = types.ModuleType("Scripts.tool.implementation")
            alias_module.__file__ = str(tool / "implementation.py")
            alias_only_module = types.ModuleType("tool_alias.implementation")
            alias_only_module.__file__ = str(tool / "implementation.py")
            suffix_alias_module = types.ModuleType("implementation")
            suffix_alias_module.__file__ = str(tool / "implementation.py")
            names = (
                "facade",
                "Scripts",
                "Scripts.tool",
                "Scripts.tool.implementation",
                "tool_alias.implementation",
                "implementation",
            )
            saved_modules = dict(sys.modules)
            for name in names:
                sys.modules.pop(name, None)
            sys.modules["tool_alias.implementation"] = alias_only_module
            sys.modules["shared.implementation"] = suffix_alias_module
            try:
                loaded = source_layout.load_fresh_module("facade", facade)
                self.assertEqual("fresh", loaded.VALUE)
                self.assertIsNot(alias_module, sys.modules.get("Scripts.tool.implementation"))
                self.assertNotIn("tool_alias.implementation", sys.modules)
                self.assertNotIn("implementation", sys.modules)
            finally:
                for name in tuple(sys.modules):
                    if name not in saved_modules:
                        sys.modules.pop(name, None)
                sys.modules.update(saved_modules)
