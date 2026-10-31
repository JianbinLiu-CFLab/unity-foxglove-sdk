"""Compare split Python identity surfaces between two Git revisions."""

from __future__ import annotations

import argparse
import ast
import copy
import hashlib
import importlib
import importlib.util
import json
import keyword
import re
from contextlib import contextmanager
from functools import lru_cache
from pathlib import Path
import subprocess
import tarfile
import tempfile
from typing import Iterator, Mapping

REPOSITORY_ROOT = Path(__file__).resolve().parents[3]

_TRY_TYPES = (ast.Try, ast.TryStar)
_TYPE_ALIAS_TYPE = None
_SECTION_NAME_RE = re.compile(r"[A-Za-z_][A-Za-z0-9_]*\Z")


SPLIT_ROOTS = (Path("Scripts"),)
IDENTITY_WAIVER_PATH = Path("Scripts/phase192/identity_waivers.json")

def _load_identity_waivers(root: Path) -> dict[str, frozenset[str]]:
    """Load the reviewed public-symbol removal waivers for one revision."""
    path = root / IDENTITY_WAIVER_PATH
    if not path.is_file():
        return {}
    try:
        payload = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        raise ValueError(f"Invalid identity waiver file: {path}") from exc
    if not isinstance(payload, dict) or payload.get("version") != 1:
        raise ValueError(f"Invalid identity waiver schema: {path}")
    entries = payload.get("waived_symbols", {})
    if not isinstance(entries, dict):
        raise ValueError(f"Invalid identity waiver entries: {path}")
    waivers: dict[str, frozenset[str]] = {}
    for relative, names in entries.items():
        relative_path = Path(relative) if isinstance(relative, str) else None
        if (
            not isinstance(relative, str)
            or not relative.endswith(".py")
            or "\\" in relative
            or ":" in relative
            or relative_path.is_absolute()
            or ".." in relative_path.parts
        ):
            raise ValueError(f"Invalid identity waiver path: {relative!r}")
        if not isinstance(names, list) or any(
            not isinstance(name, str) or not name or name.startswith("_")
            for name in names
        ):
            raise ValueError(f"Invalid identity waiver symbols: {relative}")
        waivers[relative] = frozenset(names)
    return waivers

def _git(repository: Path, *args: str) -> str:
    """Run Git and return standard output."""
    try:
        completed = subprocess.run(
            ["git", *args],
            cwd=repository,
            text=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            check=False,
            timeout=60,
        )
    except subprocess.TimeoutExpired as exc:
        raise RuntimeError(f"git command timed out: {' '.join(args)}") from exc
    if completed.returncode != 0:
        raise RuntimeError(completed.stderr.strip() or "git command failed")
    return completed.stdout

def _extract_archive_safely(archive: tarfile.TarFile, destination: Path) -> None:
    """Internal helper for _extract_archive_safely."""
    root = destination.resolve()
    for member in archive.getmembers():
        target = (destination / member.name).resolve()
        if not target.is_relative_to(root):
            raise RuntimeError(f"git archive member escapes checkout: {member.name}")
        if member.issym() or member.islnk():
            raise RuntimeError(f"git archive member is a link: {member.name}")
        if member.isdir():
            target.mkdir(parents=True, exist_ok=True)
            continue
        if not member.isfile():
            raise RuntimeError(f"unsupported git archive member: {member.name}")
        target.parent.mkdir(parents=True, exist_ok=True)
        source = archive.extractfile(member)
        if source is None:
            raise RuntimeError(f"git archive member cannot be read: {member.name}")
        with source, target.open("wb") as output:
            output.write(source.read())

@contextmanager
def _revision_checkout(repository: Path, revision: str) -> Iterator[Path]:
    """Extract a revision without registering a persistent Git worktree."""
    with tempfile.TemporaryDirectory(prefix="phase192-identity-") as temporary:
        checkout = Path(temporary) / "tree"
        checkout.mkdir()
        archive_path = Path(temporary) / "revision.tar"
        try:
            with archive_path.open("wb") as archive:
                archive_paths = [
                    "Scripts/phase192",
                    *(str(path) for path in SPLIT_ROOTS),
                ]
                completed = subprocess.run(
                    ["git", "archive", "--format=tar", revision, "--", *archive_paths],
                    cwd=repository,
                    stdout=archive,
                    stderr=subprocess.PIPE,
                    text=False,
                    check=False,
                    timeout=60,
                )
        except subprocess.TimeoutExpired as exc:
            raise RuntimeError(f"git archive timed out for {revision}") from exc
        if completed.returncode != 0:
            detail = completed.stderr.decode(errors="replace").strip()
            raise RuntimeError(detail or f"git archive failed for {revision}")
        with tarfile.open(archive_path, mode="r") as archive:
            _extract_archive_safely(archive, checkout)
        yield checkout

def _parse_source(source: str, path: str | Path) -> ast.Module:
    """Compile and parse source so syntax-only identity checks match Python imports."""
    compile(source, str(path), "exec")
    return ast.parse(source, filename=str(path))

def _section_modules_for_root(root: Path):
    """Return a non-executing parser for the revision's package initializers."""
    del root

    def section_modules(package_dir: Path) -> tuple[Path, ...]:
        """Internal helper for section_modules."""
        manifest_path = package_dir / "sections.manifest"
        if manifest_path.is_file():
            names = tuple(
                line.strip()
                for line in manifest_path.read_text(encoding="utf-8").splitlines()
                if line.strip() and not line.lstrip().startswith("#")
            )
            if not names or len(names) != len(set(names)):
                raise ValueError(f"Invalid source section manifest: {manifest_path}")
            if any(
                not name.endswith(".py")
                or "/" in name
                or "\\" in name
                or re.fullmatch(r"[A-Za-z0-9_]+(?:\.[A-Za-z0-9_]+)*", Path(name).stem) is None
                for name in names
            ):
                raise ValueError(f"Invalid source section manifest: {manifest_path}")
            paths = tuple(package_dir / name for name in names)
            if any(not path.is_file() for path in paths):
                missing = [path.name for path in paths if not path.is_file()]
                raise FileNotFoundError(
                    f"Declared source sections are missing from {manifest_path}: {', '.join(missing)}"
                )
            actual = {
                path.name
                for path in package_dir.glob("*.py")
                if path.name not in {"__init__.py", "__main__.py"}
            }
            if actual != set(names):
                raise ValueError(
                    f"Source section manifest differs from package files: {manifest_path}; "
                    f"missing={sorted(set(names) - actual)} extra={sorted(actual - set(names))}"
                )
            return paths
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
                    not module_name
                    or _SECTION_NAME_RE.fullmatch(module_name) is None
                    or keyword.iskeyword(module_name)
                ):
                    raise ValueError(f"Invalid declared source section: {module_name!r} in {init_path}")
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

    return section_modules

def _section_package_for_facade(facade: Path) -> Path | None:
    """Return the standard or manifest-backed section directory for a facade."""
    package = facade.with_suffix("")
    if (package / "__init__.py").is_file():
        return package
    manifest_package = facade.parent / "Decomposed" / facade.stem
    if manifest_package.is_dir():
        manifest_path = manifest_package / "sections.manifest"
        if not manifest_path.is_file():
            raise ValueError(f"Missing source section manifest: {manifest_path}")
        return manifest_package
    return None

def _split_facades(root: Path) -> tuple[Path, ...]:
    """Return split facade paths under supported roots."""
    paths: list[Path] = []
    seen: dict[str, Path] = {}
    for relative_root in SPLIT_ROOTS:
        root_path = root / relative_root
        if not root_path.is_dir():
            continue
        for facade in sorted(root_path.rglob("*.py")):
            package = _section_package_for_facade(facade)
            if package is not None:
                key = facade.relative_to(root).as_posix().casefold()
                if key in seen:
                    raise ValueError(f"Duplicate facade path (case-insensitive): {facade}")
                seen[key] = facade
                paths.append(facade)
    return tuple(paths)

def _top_level_names(tree: ast.Module, *, include_imports: bool = True) -> set[str]:
    """Approximate a section's runtime globals without executing its code."""
    names: set[str] = set()

    def add_target(target: ast.AST) -> None:
        """Internal helper for add_target."""
        if isinstance(target, ast.Name):
            names.add(target.id)
        elif isinstance(target, (ast.Tuple, ast.List)):
            for item in target.elts:
                add_target(item)

    def visit(statements: list[ast.stmt]) -> None:
        """Internal helper for visit."""
        for node in statements:
            if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef, ast.ClassDef)):
                names.add(node.name)
            elif isinstance(node, ast.Import):
                if include_imports:
                    for alias in node.names:
                        names.add(alias.asname or alias.name.split(".", 1)[0])
            elif isinstance(node, ast.ImportFrom):
                if include_imports and node.module != "__future__":
                    for alias in node.names:
                        if alias.name != "*":
                            names.add(alias.asname or alias.name)
            elif isinstance(node, (ast.Assign, ast.AnnAssign, ast.AugAssign)):
                targets = node.targets if isinstance(node, ast.Assign) else [node.target]
                for target in targets:
                    add_target(target)
            elif isinstance(node, (ast.For, ast.AsyncFor)):
                add_target(node.target)
                visit(node.body)
                visit(node.orelse)
            elif isinstance(node, (ast.With, ast.AsyncWith)):
                for item in node.items:
                    if item.optional_vars is not None:
                        add_target(item.optional_vars)
                visit(node.body)
            elif isinstance(node, ast.If):
                visit(node.body)
                visit(node.orelse)
            elif isinstance(node, ast.While):
                visit(node.body)
                visit(node.orelse)
            elif isinstance(node, _TRY_TYPES):
                visit(node.body)
                for handler in node.handlers:
                    # ``except ... as name`` is cleared by Python after the handler.
                    visit(handler.body)
                visit(node.orelse)
                visit(node.finalbody)
            elif isinstance(node, ast.Match):
                for case in node.cases:
                    visit(case.body)
            elif isinstance(node, ast.Delete):
                def discard_target(target: ast.AST) -> None:
                    """Internal helper for discard_target."""
                    if isinstance(target, ast.Name):
                        names.discard(target.id)
                    elif isinstance(target, (ast.Tuple, ast.List)):
                        for item in target.elts:
                            discard_target(item)
                for target in node.targets:
                    discard_target(target)

    visit(tree.body)
    return names

def _direct_import_bound_names(tree: ast.Module) -> frozenset[str]:
    """Return names bound by top-level import statements."""
    names: set[str] = set()
    for node in tree.body:
        if isinstance(node, ast.Import):
            names.update(alias.asname or alias.name.split(".", 1)[0] for alias in node.names)
        elif isinstance(node, ast.ImportFrom):
            names.update(alias.asname or alias.name for alias in node.names if alias.name != "*")
    return frozenset(names)

def _declared_public_names(tree: ast.Module) -> frozenset[str]:
    """Internal helper for _declared_public_names."""
    return frozenset(
        name for name in _top_level_names(tree, include_imports=True)
        if not name.startswith("_")
    )


_PHASE192_TEMPLATE_IMPORTS = frozenset({"pathlib", "sys", "types", "unittest"})
_PHASE192_TEMPLATE_CALLS = frozenset({
    "add", "callable", "delattr", "getattr", "globals", "hasattr", "isinstance", "issubclass", "main", "print", "set",
    "setattr", "sorted", "str", "super", "tuple", "SystemExit",
})
_PHASE192_TEMPLATE_METHODS = frozenset({
    "__class__", "__delattr__", "__dict__", "__setattr__", "add", "get", "insert", "main", "pop", "resolve", "startswith",
})

__all__ = ['argparse', 'ast', 'copy', 'hashlib', 'importlib', 'json', 'keyword', 're', 'contextmanager', 'lru_cache', 'Path', 'subprocess', 'tarfile', 'tempfile', 'Iterator', 'Mapping', 'REPOSITORY_ROOT', '_TRY_TYPES', '_TYPE_ALIAS_TYPE', '_SECTION_NAME_RE', 'SPLIT_ROOTS', 'IDENTITY_WAIVER_PATH', '_load_identity_waivers', '_git', '_extract_archive_safely', '_revision_checkout', '_parse_source', '_section_modules_for_root', '_section_package_for_facade', '_split_facades', '_top_level_names', '_direct_import_bound_names', '_declared_public_names', '_PHASE192_TEMPLATE_IMPORTS', '_PHASE192_TEMPLATE_CALLS', '_PHASE192_TEMPLATE_METHODS']
