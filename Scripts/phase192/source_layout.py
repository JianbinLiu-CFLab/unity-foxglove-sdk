"""Shared source-layout helpers for Phase192 package identity checks."""

from __future__ import annotations

import ast
from pathlib import Path


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
            if candidate.is_file():
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
