"""Compare split Python identity surfaces between two Git revisions."""

from __future__ import annotations

import argparse
import ast
import hashlib
from contextlib import contextmanager
from pathlib import Path
import subprocess
import tempfile
from typing import Iterator
import sys

REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
if str(REPOSITORY_ROOT) not in sys.path:
    sys.path.insert(0, str(REPOSITORY_ROOT))

from Scripts.phase192.source_layout import section_modules

SPLIT_ROOTS = (
    Path("Scripts/smoke/foxrun"),
    Path("Scripts/smoke/ros2"),
    Path("Scripts/smoke/websocket"),
)


def _git(repository: Path, *args: str) -> str:
    """Run Git and return standard output."""
    completed = subprocess.run(["git", *args], cwd=repository, text=True,
                               stdout=subprocess.PIPE, stderr=subprocess.PIPE, check=False)
    if completed.returncode != 0:
        raise RuntimeError(completed.stderr.strip() or "git command failed")
    return completed.stdout


@contextmanager
def _revision_checkout(repository: Path, revision: str) -> Iterator[Path]:
    """Check out a revision in a temporary worktree."""
    with tempfile.TemporaryDirectory(prefix="phase192-identity-") as temporary:
        checkout = Path(temporary) / "tree"
        try:
            _git(repository, "worktree", "add", "--detach", str(checkout), revision)
            yield checkout
        finally:
            subprocess.run(
                ["git", "worktree", "remove", "--force", str(checkout)],
                cwd=repository,
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
                text=True,
                check=False,
            )


def _split_facades(root: Path) -> tuple[Path, ...]:
    """Return split facade paths under supported roots."""
    paths: list[Path] = []
    for relative_root in SPLIT_ROOTS:
        root_path = root / relative_root
        if not root_path.is_dir():
            continue
        for facade in sorted(root_path.rglob("*.py")):
            package = facade.with_suffix("")
            if package.is_dir() and (package / "__init__.py").is_file():
                paths.append(facade)
    return tuple(paths)


def _top_level_names(tree: ast.Module) -> set[str]:
    """Approximate a section's runtime globals without executing its code."""

    names: set[str] = set()

    def add_target(target: ast.AST) -> None:
        """Collect names bound by an assignment target."""
        if isinstance(target, ast.Name):
            names.add(target.id)
        elif isinstance(target, (ast.Tuple, ast.List)):
            for item in target.elts:
                add_target(item)

    def visit(statements: list[ast.stmt]) -> None:
        """Collect names from supported statement forms."""
        for node in statements:
            if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef, ast.ClassDef)):
                names.add(node.name)
            elif isinstance(node, ast.Import):
                for alias in node.names:
                    names.add(alias.asname or alias.name.split(".", 1)[0])
            elif isinstance(node, ast.ImportFrom):
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
            elif isinstance(node, (ast.While,)):
                visit(node.body)
                visit(node.orelse)
            elif isinstance(node, ast.Try):
                visit(node.body)
                for handler in node.handlers:
                    if handler.name:
                        names.add(handler.name)
                    visit(handler.body)
                visit(node.orelse)
                visit(node.finalbody)
            elif isinstance(node, ast.Match):
                for case in node.cases:
                    visit(case.body)
            elif isinstance(node, ast.Delete):
                for target in node.targets:
                    if isinstance(target, ast.Name):
                        names.discard(target.id)

    visit(tree.body)
    return names


def _symbols(source: str, path: str) -> frozenset[str]:
    """Return public top-level symbols from one Python source unit."""

    tree = ast.parse(source, filename=path)
    return frozenset(name for name in _top_level_names(tree) if not name.startswith("_"))


def _section_exports(source: str, path: str) -> frozenset[str]:
    """Resolve the generated section ``__all__`` contract statically."""

    tree = ast.parse(source, filename=path)
    names = _top_level_names(tree)
    for node in tree.body:
        targets: list[ast.AST] = []
        if isinstance(node, ast.Assign):
            targets = list(node.targets)
        elif isinstance(node, ast.AnnAssign):
            targets = [node.target]
        if not any(
            isinstance(target, ast.Name) and target.id == "__all__"
            for target in targets
        ):
            continue
        value = node.value
        if isinstance(value, (ast.ListComp, ast.SetComp)):
            generator = value.generators[0] if value.generators else None
            if (
                generator is not None
                and isinstance(value.elt, ast.Name)
                and isinstance(generator.target, ast.Name)
                and value.elt.id == generator.target.id
                and isinstance(generator.iter, ast.Call)
                and isinstance(generator.iter.func, ast.Name)
                and generator.iter.func.id == "globals"
            ):
                return frozenset(name for name in names if not name.startswith("__"))
        try:
            literal = ast.literal_eval(value)
        except (TypeError, ValueError):
            break
        if isinstance(literal, (list, tuple, set)) and all(
            isinstance(item, str) for item in literal
        ):
            return frozenset(literal)
        break
    return frozenset(name for name in names if not name.startswith("_"))


def _surface(root: Path, facade: Path) -> tuple[frozenset[str], tuple[str, ...], str]:
    """Return symbols, section order, and initializer hash for a facade."""
    relative = facade.relative_to(root).as_posix()
    package = facade.with_suffix("")
    sections = section_modules(package)
    exported: set[str] = set()
    for section in sections:
        exported.update(
            _section_exports(
                section.read_text(encoding="utf-8"),
                section.relative_to(root).as_posix(),
            )
        )
    exported.update(_symbols(facade.read_text(encoding="utf-8"), relative))
    initializer = package / "__init__.py"
    initializer_bytes = initializer.read_bytes().replace(b"\r\n", b"\n")
    initializer_digest = hashlib.sha256(initializer_bytes).hexdigest()
    return frozenset(exported), tuple(path.stem for path in sections), initializer_digest


def _surfaces(root: Path) -> dict[str, tuple[frozenset[str], tuple[str, ...], str]]:
    """Build identity surfaces for all split facades."""
    result: dict[str, tuple[frozenset[str], tuple[str, ...], str]] = {}
    for facade in _split_facades(root):
        relative = facade.relative_to(root).as_posix()
        package = facade.with_suffix("")
        listed = {path.name for path in section_modules(package)}
        actual = {path.name for path in package.glob("*.py")
                  if path.name not in {"__init__.py", "__main__.py"}}
        if listed != actual:
            missing = ", ".join(sorted(actual - listed))
            extra = ", ".join(sorted(listed - actual))
            raise ValueError(f"{relative}: exported sections differ from package files; "
                             f"missing={missing or '-'} extra={extra or '-'}")
        result[relative] = _surface(root, facade)
    return result


def _compare_surfaces(
    base_surfaces: dict[str, tuple[frozenset[str], tuple[str, ...], str]],
    head_surfaces: dict[str, tuple[frozenset[str], tuple[str, ...], str]],
) -> list[str]:
    """Return every removed or added facade, section, symbol, or initializer."""
    errors: list[str] = []
    for relative, (base_symbols, base_sections, base_initializer) in sorted(base_surfaces.items()):
        current = head_surfaces.get(relative)
        if current is None:
            errors.append(f"REMOVED_FACADE {relative}")
            continue
        head_symbols, head_sections, head_initializer = current
        if base_initializer != head_initializer:
            errors.append(f"PACKAGE_INITIALIZER_CHANGED {relative}")
        missing_sections = sorted(set(base_sections) - set(head_sections))
        extra_sections = sorted(set(head_sections) - set(base_sections))
        missing_symbols = sorted(set(base_symbols) - set(head_symbols))
        extra_symbols = sorted(set(head_symbols) - set(base_symbols))
        if missing_sections:
            errors.append(f"MISSING_SECTIONS {relative}: {','.join(missing_sections)}")
        if extra_sections:
            errors.append(f"EXTRA_SECTIONS {relative}: {','.join(extra_sections)}")
        if missing_symbols:
            errors.append(f"MISSING_SYMBOLS {relative}: {','.join(missing_symbols)}")
        if extra_symbols:
            errors.append(f"EXTRA_SYMBOLS {relative}: {','.join(extra_symbols)}")
    for relative in sorted(set(head_surfaces) - set(base_surfaces)):
        errors.append(f"ADDED_FACADE {relative}")
    return errors


def compare_revisions(repository: Path, base: str, head: str) -> int:
    """Fail unless split facade identity surfaces are exactly equivalent."""
    base = _git(repository, "rev-parse", "--verify", base + "^{commit}").strip()
    head = _git(repository, "rev-parse", "--verify", head + "^{commit}").strip()
    with _revision_checkout(repository, base) as base_root, _revision_checkout(repository, head) as head_root:
        base_surfaces = _surfaces(base_root)
        head_surfaces = _surfaces(head_root)
    errors = _compare_surfaces(base_surfaces, head_surfaces)
    if errors:
        for error in errors:
            print(error)
        return 1
    print(f"IDENTITY_EQUIVALENT base={base} head={head} facades={len(base_surfaces)}")
    return 0


def main(argv: list[str] | None = None) -> int:
    """Run the identity comparison CLI."""
    parser = argparse.ArgumentParser()
    parser.add_argument("--base", required=True)
    parser.add_argument("--head", required=True)
    parser.add_argument("--repository", type=Path, default=Path("."))
    args = parser.parse_args(argv)
    return compare_revisions(args.repository.resolve(), args.base, args.head)


if __name__ == "__main__":
    raise SystemExit(main())
