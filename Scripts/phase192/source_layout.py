"""Shared source-layout helpers for Phase192 package identity checks."""

from __future__ import annotations

import ast
import importlib
import importlib.util
import keyword
import re
import unittest
from pathlib import Path
import sys
from types import ModuleType


def _parse_source(source: str, path: Path) -> ast.Module:
    """Parse source text and raise syntax errors with the source path."""
    compile(source, str(path), "exec")
    return ast.parse(source, filename=str(path))


def section_modules(package_dir: Path) -> tuple[Path, ...]:
    """Return package section files in the order exported by ``__init__.py``."""

    init_path = package_dir / "__init__.py"
    tree = _parse_source(init_path.read_text(encoding="utf-8"), init_path)
    modules: list[Path] = []
    seen: dict[str, Path] = {}
    for node in tree.body:
        if not isinstance(node, ast.ImportFrom) or node.level != 1:
            continue
        imported = [node.module] if node.module else [alias.name for alias in node.names]
        for module_name in imported:
            if module_name == "__init__":
                continue
            if (
                not isinstance(module_name, str)
                or not re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]*", module_name, re.UNICODE)
                or keyword.iskeyword(module_name)
            ):
                raise ValueError(f"Invalid declared source section: {module_name!r}")
            candidate = package_dir / f"{module_name}.py"
            if not candidate.is_file():
                raise FileNotFoundError(
                    f"Declared source section is missing: {candidate}"
                )
            key = module_name.casefold()
            if key in seen:
                raise ValueError(
                    f"Duplicate declared source section (case-insensitive): {candidate}"
                )
            seen[key] = candidate
            modules.append(candidate)
    if not modules:
        raise ValueError(f"No exported source sections found in {init_path}")
    return tuple(modules)


def read_split_source(facade: Path) -> str:
    """Read a facade and its package sections in the package export order."""

    package_dir = facade.with_suffix("")
    parts = [facade.read_text(encoding="utf-8")]
    declared = section_modules(package_dir)
    parts.extend(path.read_text(encoding="utf-8") for path in declared)
    declared_names = {path.name for path in declared}
    parts.extend(
        path.read_text(encoding="utf-8")
        for path in sorted(package_dir.glob("*.py"))
        if path.name not in declared_names and path.name != "__init__.py"
    )
    return "\n".join(parts)


def load_fresh_module(module_name: str, facade: Path) -> ModuleType:
    """Load a split facade after clearing its package and section modules."""

    facade = facade.resolve()
    original_path = list(sys.path)
    facade_parent = str(facade.parent)
    prefixes = {module_name, facade.stem}
    package_dir = facade.with_suffix("")
    saved_modules: dict[str, ModuleType] = {}
    initial_module_names = set(sys.modules)

    def is_local_module_path(path: Path) -> bool:
        """Return whether a module path belongs to the facade directory."""
        return path == facade or facade.parent in path.parents

    try:
        sys.path[:] = [entry for entry in sys.path if entry != facade_parent]
        sys.path.insert(0, facade_parent)
        for loaded_name, loaded_module in tuple(sys.modules.items()):
            remove = any(
                loaded_name == prefix or loaded_name.startswith(prefix + ".")
                for prefix in prefixes
            )
            if not remove:
                origin = getattr(getattr(loaded_module, "__spec__", None), "origin", None)
                module_file = getattr(loaded_module, "__file__", None) or origin
                if module_file:
                    try:
                        resolved = Path(module_file).resolve()
                    except OSError:
                        resolved = None
                    if resolved is not None:
                        remove = is_local_module_path(resolved)
            if remove:
                saved_modules[loaded_name] = loaded_module
                sys.modules.pop(loaded_name, None)
        importlib.invalidate_caches()
        spec = importlib.util.spec_from_file_location(module_name, facade)
        if spec is None or spec.loader is None:
            raise RuntimeError(f"Could not load module from {facade}")
        module = importlib.util.module_from_spec(spec)
        sys.modules[module_name] = module
        try:
            spec.loader.exec_module(module)
        except BaseException:
            if sys.modules.get(module_name) is module:
                sys.modules.pop(module_name, None)
            raise
        return module
    except BaseException:
        for loaded_name in tuple(sys.modules):
            remove = loaded_name in saved_modules or any(
                loaded_name == prefix or loaded_name.startswith(prefix + ".")
                for prefix in prefixes
            )
            if not remove and loaded_name not in initial_module_names:
                loaded_module = sys.modules[loaded_name]
                origin = getattr(getattr(loaded_module, "__spec__", None), "origin", None)
                module_file = getattr(loaded_module, "__file__", None) or origin
                if module_file:
                    try:
                        resolved = Path(module_file).resolve()
                    except OSError:
                        resolved = None
                    if resolved is not None:
                        remove = is_local_module_path(resolved)
            if remove:
                sys.modules.pop(loaded_name, None)
        sys.modules.update(saved_modules)
        raise
    finally:
        sys.path[:] = original_path


def load_test_suite(module_name: str, source: Path, root: Path) -> unittest.TestSuite:
    """Load an out-of-tree unittest module with its supplied package root."""

    package_name = module_name.split(".", 1)[0]
    saved_modules = {
        name: module
        for name, module in sys.modules.items()
        if name == package_name or name.startswith(package_name + ".")
    }
    for name in tuple(saved_modules):
        sys.modules.pop(name, None)
    original_path = list(sys.path)
    sys.path.insert(0, str(root.resolve()))
    try:
        importlib.invalidate_caches()
        spec = importlib.util.spec_from_file_location(module_name, source.resolve())
        if spec is None or spec.loader is None:
            raise RuntimeError(f"unittest discovery failed for {module_name}")
        module = importlib.util.module_from_spec(spec)
        sys.modules[module_name] = module
        spec.loader.exec_module(module)
        return unittest.defaultTestLoader.loadTestsFromModule(module)
    finally:
        sys.path[:] = original_path
        for name in tuple(sys.modules):
            if name == package_name or name.startswith(package_name + "."):
                sys.modules.pop(name, None)
        sys.modules.update(saved_modules)
