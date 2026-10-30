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

    if facade.suffix.casefold() == ".cpp":
        return read_included_source(facade)

    package_dir = facade.with_suffix("")
    parts = [facade.read_text(encoding="utf-8")]
    declared = section_modules(package_dir)
    parts.extend(path.read_text(encoding="utf-8") for path in declared)
    companion_dir = package_dir / "sections"
    if companion_dir.is_dir():
        parts.extend(
            path.read_text(encoding="utf-8")
            for path in sorted(companion_dir.glob("*.py"))
            if path.name != "__init__.py"
        )
    return "\n".join(parts)


def read_included_source(facade: Path) -> str:
    """Read a source facade and inline its relative `.inl` includes."""

    parts: list[str] = []
    lines = facade.read_text(encoding="utf-8").splitlines(keepends=True)
    fragment_lines = [
        line
        for line in lines
        if line.strip().startswith('#include "') and line.strip().endswith('.inl"')
    ]
    fragment_index = 0
    for line in lines:
        stripped = line.strip()
        if stripped.startswith('#include "') and stripped.endswith('.inl"'):
            include = stripped[10:-1]
            fragment = facade.parent.joinpath(*include.split('/'))
            if not fragment.is_file():
                raise FileNotFoundError(f"Included source fragment is missing: {fragment}")
            parts.append(fragment.read_text(encoding="utf-8"))
            fragment_index += 1
            if fragment_index < len(fragment_lines):
                parts.append("\n")
        else:
            parts.append(line)
    return "".join(parts)


def read_split_csharp_source(facade: Path) -> str:
    """Read a C# facade and its declared Phase192 partial-class sections."""
    source = facade.read_text(encoding="utf-8")
    marker = re.compile(
        r"^\s*///\s*<remarks>Phase192 source sections:\s*(?P<names>[^<]+)</remarks>\s*$",
        re.MULTILINE,
    ).search(source)
    if marker is None:
        return source

    section_directory = facade.parent / "Decomposed" / facade.stem
    names = [name.strip() for name in marker.group("names").split(",") if name.strip()]
    if not section_directory.is_dir():
        raise FileNotFoundError(f"Missing C# source section directory: {section_directory}")
    actual = sorted(
        path.name
        for path in section_directory.glob("*.cs")
    )
    if sorted(names) != actual or len(set(name.casefold() for name in names)) != len(names):
        raise ValueError(
            f"C# source section manifest mismatch in {section_directory}: "
            f"expected={names!r} actual={actual!r}"
        )
    return source + "\n" + "\n".join(
        (section_directory / name).read_text(encoding="utf-8")
        for name in names
    )


def load_fresh_module(module_name: str, facade: Path) -> ModuleType:
    """Load a split facade after clearing its package and section modules."""

    facade = facade.resolve()
    original_path = list(sys.path)
    facade_parent = str(facade.parent)
    scripts_directories = [ancestor for ancestor in facade.parents if ancestor.name == "Scripts"]
    source_root = scripts_directories[0].parent if scripts_directories else facade.parent
    source_paths = {str(source_root), facade_parent}
    prefixes = {module_name, facade.stem}
    package_dir = facade.with_suffix("")
    saved_modules: dict[str, ModuleType] = {}
    initial_module_names = set(sys.modules)
    initial_module_aliases: dict[int, tuple[str, ...]] = {}
    for name, module in tuple(sys.modules.items()):
        initial_module_aliases.setdefault(id(module), tuple())
        initial_module_aliases[id(module)] += (name,)

    def is_local_module_path(path: Path) -> bool:
        """Return whether a module path belongs to the facade directory."""
        return path == facade or facade.parent in path.parents

    def module_paths(module: ModuleType) -> tuple[Path, ...]:
        """Return source paths recorded by a module or package."""
        candidates: list[Path] = []
        origin = getattr(getattr(module, "__spec__", None), "origin", None)
        module_file = getattr(module, "__file__", None) or origin
        if module_file:
            candidates.append(Path(module_file))
        try:
            module_paths_value = getattr(module, "__path__", ())
            module_path_entries = tuple(module_paths_value or ())
        except Exception:
            module_path_entries = ()
        for entry in module_path_entries:
            candidates.append(Path(entry))
        resolved: list[Path] = []
        for candidate in candidates:
            try:
                resolved.append(candidate.resolve())
            except OSError:
                continue
        return tuple(resolved)

    def is_stale_phase192_module(name: str, module: ModuleType) -> bool:
        """Return whether an absolute Scripts package belongs to another checkout."""
        if name != "Scripts" and not name.startswith("Scripts."):
            return False
        paths = module_paths(module)
        if not paths:
            return False
        return not any(path == source_root or source_root in path.parents for path in paths)

    def canonical_module_names(paths: tuple[Path, ...]) -> frozenset[str]:
        """Return canonical Scripts names represented by source paths."""
        names: set[str] = set()
        for path in paths:
            try:
                relative = path.relative_to(source_root)
            except ValueError:
                continue
            parts = list(relative.parts)
            if not parts or parts[0] != "Scripts":
                continue
            if path.name == "__init__.py":
                parts.pop()
            elif path.suffix == ".py":
                parts[-1] = path.stem
            else:
                continue
            if parts:
                names.add(".".join(parts))
        return frozenset(names)

    def is_source_alias(loaded_name: str, loaded_module: ModuleType) -> bool:
        """Return whether a source module is registered under a non-canonical alias."""
        paths = module_paths(loaded_module)
        if not paths:
            return False
        if not any(path == source_root or source_root in path.parents for path in paths):
            return False
        aliases = initial_module_aliases.get(id(loaded_module))
        if aliases is None:
            aliases = tuple(
                name
                for name, module in sys.modules.items()
                if module is loaded_module
            )
        canonical = canonical_module_names(paths)
        if not canonical or len(aliases) <= 1 or not any(name in canonical for name in aliases):
            return False
        if any(
            alias not in canonical
            and (canonical_name == alias or canonical_name.endswith("." + alias))
            for canonical_name in canonical
            for alias in aliases
        ):
            return False
        try:
            is_package = bool(tuple(getattr(loaded_module, "__path__", ()) or ()))
        except Exception:
            is_package = False
        if is_package:
            return False
        if loaded_name in canonical:
            return True
        parent_name = loaded_name.rsplit(".", 1)[0] if "." in loaded_name else None
        parent_module = sys.modules.get(parent_name) if parent_name else None
        if parent_module is not None:
            parent_paths = module_paths(parent_module)
            parent_canonical = canonical_module_names(parent_paths)
            parent_aliases = initial_module_aliases.get(id(parent_module), ())
            if parent_canonical and any(
                name in parent_canonical for name in parent_aliases
            ) and parent_name not in parent_canonical:
                return False
        return True

    def has_duplicate_namespace_paths(loaded_name: str, loaded_module: ModuleType) -> bool:
        """Return whether a non-root namespace package carries duplicate source paths."""
        if loaded_name == "Scripts" or not loaded_name.startswith("Scripts."):
            return False
        module_spec = getattr(loaded_module, "__spec__", None)
        if getattr(module_spec, "origin", None) not in {None, "built-in", "frozen"}:
            return False
        paths = module_paths(loaded_module)
        return len(paths) > 1 and len(set(paths)) != len(paths)

    def should_remove(loaded_name: str, loaded_module: ModuleType) -> bool:
        """Return whether one cached module would contaminate the fresh checkout."""
        if any(
            loaded_name == prefix or loaded_name.startswith(prefix + ".")
            for prefix in prefixes
        ):
            return True
        for path in module_paths(loaded_module):
            if is_local_module_path(path):
                return True
        if is_source_alias(loaded_name, loaded_module):
            return True
        if has_duplicate_namespace_paths(loaded_name, loaded_module):
            return True
        if loaded_name == "Scripts" or loaded_name.startswith("Scripts."):
            return is_stale_phase192_module(loaded_name, loaded_module)
        if is_stale_phase192_module(loaded_name, loaded_module):
            return True
        return False

    try:
        sys.path[:] = [entry for entry in sys.path if entry not in source_paths]
        sys.path.insert(0, facade_parent)
        if str(source_root) != facade_parent:
            sys.path.insert(1, str(source_root))
        for loaded_name, loaded_module in tuple(sys.modules.items()):
            remove = should_remove(loaded_name, loaded_module)
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
                remove = should_remove(loaded_name, sys.modules[loaded_name])
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
