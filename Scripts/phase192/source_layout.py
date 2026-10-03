"""Shared source-layout helpers for Phase192 package identity checks."""

from __future__ import annotations

import ast
import importlib
import importlib.util
from pathlib import Path
import sys
from types import ModuleType


def section_modules(package_dir: Path) -> tuple[Path, ...]:
    """Return package section files in the order exported by ``__init__.py``."""

    init_path = package_dir / "__init__.py"
    tree = ast.parse(init_path.read_text(encoding="utf-8"), filename=str(init_path))
    modules: list[Path] = []
    for node in tree.body:
        if not isinstance(node, ast.ImportFrom) or node.level != 1:
            continue
        imported = [node.module] if node.module else [alias.name for alias in node.names]
        for module_name in imported:
            if module_name == "__init__":
                continue
            candidate = package_dir / f"{module_name}.py"
            if not candidate.is_file():
                raise FileNotFoundError(
                    f"Declared source section is missing: {candidate}"
                )
            modules.append(candidate)
    if not modules:
        raise ValueError(f"No exported source sections found in {init_path}")
    return tuple(modules)


def read_split_source(facade: Path) -> str:
    """Read a facade and its package sections in the package export order."""

    package_dir = facade.with_suffix("")
    parts = [facade.read_text(encoding="utf-8")]
    parts.extend(path.read_text(encoding="utf-8") for path in section_modules(package_dir))
    return "\n".join(parts)


def load_fresh_module(module_name: str, facade: Path) -> ModuleType:
    """Load a split facade after clearing its package and section modules."""

    facade = facade.resolve()
    prefixes = {module_name, facade.stem}
    package_dir = facade.with_suffix("")
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
                    remove = resolved == facade or package_dir in resolved.parents
        if remove:
            sys.modules.pop(loaded_name, None)
    importlib.invalidate_caches()
    spec = importlib.util.spec_from_file_location(module_name, facade)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Could not load module from {facade}")
    module = importlib.util.module_from_spec(spec)
    sys.modules[module_name] = module
    spec.loader.exec_module(module)
    return module
