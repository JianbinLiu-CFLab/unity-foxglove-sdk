"""Compare split Python identity surfaces between two Git revisions."""

from __future__ import annotations

_REVIEWED_IDENTITY_TOOLING_SECTIONS = frozenset(
    {
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
    }
)

def _reviewed_identity_tooling_test_split(relative: str, sections: tuple[Path, ...]) -> bool:
    """Recognize only the exact reviewed identity-tooling decomposition."""
    return (
        _canonical_source_decomposition_relative(relative)
        == "Scripts/validation/source_decomposition/regression_checks/test_identity_tooling.py"
        and {section.stem for section in sections} == _REVIEWED_IDENTITY_TOOLING_SECTIONS
        and len(sections) == len(_REVIEWED_IDENTITY_TOOLING_SECTIONS)
    )

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

REPOSITORY_ROOT = Path(__file__).resolve().parents[4]

try:
    _TRY_TYPES = (ast.Try, ast.TryStar)
except AttributeError:
    _TRY_TYPES = (ast.Try,)
try:
    _TYPE_ALIAS_TYPE = ast.TypeAlias
except AttributeError:
    _TYPE_ALIAS_TYPE = None
_SECTION_NAME_RE = re.compile(r"[A-Za-z_][A-Za-z0-9_]*\Z")


SPLIT_ROOTS = (Path("Scripts"),)
_LEGACY_SOURCE_DECOMPOSITION_PREFIX = "Scripts/phase192/"
SOURCE_DECOMPOSITION_PREFIX = "Scripts/validation/source_decomposition/"
IDENTITY_WAIVER_PATH = Path(
    SOURCE_DECOMPOSITION_PREFIX + "identity_waivers.json"
)


def _legacy_relative_path(relative: str) -> str | None:
    """Return the pre-validation path for a migrated source-decomposition file."""
    if relative.startswith(SOURCE_DECOMPOSITION_PREFIX):
        return _LEGACY_SOURCE_DECOMPOSITION_PREFIX + relative[len(SOURCE_DECOMPOSITION_PREFIX):]
    return None


def _canonical_source_decomposition_relative(relative: str) -> str:
    """Normalize a source-decomposition path to the validation layout."""
    if relative.startswith(_LEGACY_SOURCE_DECOMPOSITION_PREFIX):
        return SOURCE_DECOMPOSITION_PREFIX + relative[len(_LEGACY_SOURCE_DECOMPOSITION_PREFIX):]
    return relative


def _current_source_decomposition_relative(relative: str) -> str:
    """Return the current validation path for a legacy relative path."""
    if relative.startswith(_LEGACY_SOURCE_DECOMPOSITION_PREFIX):
        return SOURCE_DECOMPOSITION_PREFIX + relative[len(_LEGACY_SOURCE_DECOMPOSITION_PREFIX):]
    return relative


def _resolve_source_decomposition_path(root: Path, relative: str) -> Path:
    """Resolve a source-decomposition path in current or legacy layouts."""
    path = root / relative
    if path.is_file() or path.is_dir():
        return path
    legacy = _legacy_relative_path(relative)
    return root / legacy if legacy is not None else path

def _load_identity_waivers(root: Path) -> dict[str, frozenset[str]]:
    """Load the reviewed public-symbol removal waivers for one revision."""
    path = _resolve_source_decomposition_path(root, IDENTITY_WAIVER_PATH.as_posix())
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
    scratch_root = Path(repository).resolve() / "build" / "identity-gate"
    scratch_root.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="phase192-identity-", dir=scratch_root) as temporary:
        checkout = Path(temporary) / "tree"
        checkout.mkdir()
        archive_path = Path(temporary) / "revision.tar"
        try:
            with archive_path.open("wb") as archive:
                archive_paths = [str(path) for path in SPLIT_ROOTS]
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

_FUNCTION_BODY_RISK_NAMES = frozenset({"__import__", "compile", "eval", "exec", "globals", "locals", "vars"})
_ENVIRONMENT_MUTATORS = frozenset({"clear", "pop", "popitem", "setdefault", "update"})
_ENVIRONMENT_FUNCTIONS = frozenset({"putenv", "unsetenv"})

def _without_function_bodies(source: str) -> str:
    """Return source whose function bodies are replaced by pass, keeping import-time statements."""
    tree = ast.parse(source)
    for node in ast.walk(tree):
        if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef)):
            node.body = [ast.Pass()]
    return ast.unparse(ast.fix_missing_locations(tree)) + "\n"

def _function_body_and_environment_risk_signatures(tree: ast.AST) -> frozenset[str]:
    """Return signatures for code-execution builtins in function bodies and import-time environment writes."""
    os_names = {"os"}
    environ_names: set[str] = set()
    environment_function_names: set[str] = set()
    for node in ast.walk(tree):
        if isinstance(node, ast.Import):
            for alias in node.names:
                if alias.name == "os":
                    os_names.add(alias.asname or "os")
        elif isinstance(node, ast.ImportFrom) and node.module == "os" and not node.level:
            for alias in node.names:
                if alias.name == "environ":
                    environ_names.add(alias.asname or "environ")
                elif alias.name in _ENVIRONMENT_FUNCTIONS:
                    environment_function_names.add(alias.asname or alias.name)

    def is_environ(node: ast.AST) -> bool:
        """Return whether an expression names os.environ."""
        if isinstance(node, ast.Name):
            return node.id in environ_names
        return (
            isinstance(node, ast.Attribute)
            and node.attr == "environ"
            and isinstance(node.value, ast.Name)
            and node.value.id in os_names
        )

    future_annotations = any(
        isinstance(node, ast.ImportFrom)
        and node.module == "__future__"
        and any(alias.name == "annotations" for alias in node.names)
        for node in getattr(tree, "body", ())
    )

    class EnvironmentWriteVisitor(ast.NodeVisitor):
        def __init__(self) -> None:
            self.found = False

        def _check_targets(self, targets: list[ast.AST]) -> None:
            if any(
                is_environ(target)
                or (isinstance(target, ast.Subscript) and is_environ(target.value))
                for target in targets
            ):
                self.found = True

        def visit_Assign(self, node: ast.Assign) -> None:
            self._check_targets(node.targets)
            self.generic_visit(node)

        def visit_AugAssign(self, node: ast.AugAssign) -> None:
            self._check_targets([node.target])
            self.generic_visit(node)

        def visit_AnnAssign(self, node: ast.AnnAssign) -> None:
            self._check_targets([node.target])
            if node.value is not None:
                self.visit(node.value)
            if node.annotation is not None and not future_annotations:
                self.visit(node.annotation)

        def visit_Delete(self, node: ast.Delete) -> None:
            self._check_targets(node.targets)
            self.generic_visit(node)

        def visit_Call(self, node: ast.Call) -> None:
            if isinstance(node.func, ast.Attribute):
                if node.func.attr in _ENVIRONMENT_MUTATORS and is_environ(node.func.value):
                    self.found = True
                if (
                    node.func.attr in _ENVIRONMENT_FUNCTIONS
                    and isinstance(node.func.value, ast.Name)
                    and node.func.value.id in os_names
                ):
                    self.found = True
            elif isinstance(node.func, ast.Name) and node.func.id in environment_function_names:
                self.found = True
            self.generic_visit(node)

        def visit_FunctionDef(self, node: ast.FunctionDef) -> None:
            for decorator in node.decorator_list:
                self.visit(decorator)
            for expression in (*node.args.defaults, *node.args.kw_defaults):
                if expression is not None:
                    self.visit(expression)
            if not future_annotations:
                for argument in (
                    *getattr(node.args, "posonlyargs", ()),
                    *node.args.args,
                    *node.args.kwonlyargs,
                ):
                    if argument.annotation is not None:
                        self.visit(argument.annotation)
                if node.args.vararg is not None and node.args.vararg.annotation is not None:
                    self.visit(node.args.vararg.annotation)
                if node.args.kwarg is not None and node.args.kwarg.annotation is not None:
                    self.visit(node.args.kwarg.annotation)
                if node.returns is not None:
                    self.visit(node.returns)

        visit_AsyncFunctionDef = visit_FunctionDef

        def visit_Lambda(self, node: ast.Lambda) -> None:
            for expression in (*node.args.defaults, *node.args.kw_defaults):
                if expression is not None:
                    self.visit(expression)

        def visit_ClassDef(self, node: ast.ClassDef) -> None:
            for decorator in node.decorator_list:
                self.visit(decorator)
            for base in node.bases:
                self.visit(base)
            for keyword in node.keywords:
                self.visit(keyword.value)
            for statement in node.body:
                self.visit(statement)

        def visit_GeneratorExp(self, node: ast.GeneratorExp) -> None:
            if node.generators:
                self.visit(node.generators[0].iter)

    def writes_environment(statement: ast.AST) -> bool:
        """Return whether one import-time statement writes the process environment."""
        visitor = EnvironmentWriteVisitor()
        visitor.visit(statement)
        return visitor.found

    signatures: set[str] = set()

    def record_import_time_environment(
        node: ast.AST, scope: tuple[str, ...], in_function: bool
    ) -> None:
        """Record an environment write that executes during a definition."""
        if not in_function and writes_environment(node):
            qualified = ".".join(scope) or "<module>"
            signatures.add(
                f"import-time-environment:{qualified}:{ast.dump(node, include_attributes=False)}"
            )

    def visit(node: ast.AST, scope: tuple[str, ...], in_function: bool) -> None:
        """Collect signatures while tracking the enclosing definition scope."""
        if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef)):
            for decorator in node.decorator_list:
                record_import_time_environment(decorator, scope, in_function)
                visit(decorator, scope, in_function)
            arguments = node.args
            definition_expressions = [
                expression
                for expression in (*arguments.defaults, *arguments.kw_defaults)
                if expression is not None
            ]
            if not future_annotations:
                definition_expressions.extend(
                    argument.annotation
                    for argument in (
                        *getattr(arguments, "posonlyargs", ()),
                        *arguments.args,
                        *arguments.kwonlyargs,
                    )
                    if argument.annotation is not None
                )
                if arguments.vararg is not None and arguments.vararg.annotation is not None:
                    definition_expressions.append(arguments.vararg.annotation)
                if arguments.kwarg is not None and arguments.kwarg.annotation is not None:
                    definition_expressions.append(arguments.kwarg.annotation)
                if node.returns is not None:
                    definition_expressions.append(node.returns)
            for expression in definition_expressions:
                record_import_time_environment(expression, scope, in_function)
                visit(expression, scope, in_function)
            function_scope = scope + (node.name,)
            for statement in node.body:
                visit(statement, function_scope, True)
            return
        if isinstance(node, ast.Lambda):
            for expression in (*node.args.defaults, *node.args.kw_defaults):
                if expression is not None:
                    record_import_time_environment(expression, scope, in_function)
                    visit(expression, scope, in_function)
            visit(node.body, scope + ("<lambda>",), True)
            return
        if isinstance(node, ast.ClassDef):
            for decorator in node.decorator_list:
                record_import_time_environment(decorator, scope, in_function)
                visit(decorator, scope, in_function)
            for base in node.bases:
                record_import_time_environment(base, scope, in_function)
                visit(base, scope, in_function)
            for keyword in node.keywords:
                record_import_time_environment(keyword.value, scope, in_function)
                visit(keyword.value, scope, in_function)
            class_scope = scope + (node.name,)
            for statement in node.body:
                if not in_function and not isinstance(
                    statement, (ast.FunctionDef, ast.AsyncFunctionDef, ast.ClassDef)
                ):
                    record_import_time_environment(statement, class_scope, in_function)
                visit(statement, class_scope, in_function)
            return
        for child in ast.iter_child_nodes(node):
            if isinstance(child, (ast.FunctionDef, ast.AsyncFunctionDef)):
                visit(child, scope, in_function)
                continue
            if isinstance(child, ast.Lambda):
                visit(child, scope, in_function)
                continue
            if isinstance(child, ast.ClassDef):
                visit(child, scope, in_function)
                continue
            qualified = ".".join(scope) or "<module>"
            if (
                in_function
                and isinstance(child, ast.Call)
                and isinstance(child.func, ast.Name)
                and child.func.id in _FUNCTION_BODY_RISK_NAMES
            ):
                signatures.add(f"function-body-risk:{qualified}:{child.func.id}")
            if not in_function and isinstance(child, ast.stmt) and writes_environment(child):
                signatures.add(
                    f"import-time-environment:{qualified}:{ast.dump(child, include_attributes=False)}"
                )
            visit(child, scope, in_function)

    visit(tree, (), False)
    return frozenset(signatures)

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

__all__ = ['_REVIEWED_IDENTITY_TOOLING_SECTIONS', '_reviewed_identity_tooling_test_split', 'argparse', 'ast', 'copy', 'hashlib', 'importlib', 'json', 'keyword', 're', 'contextmanager', 'lru_cache', 'Path', 'subprocess', 'tarfile', 'tempfile', 'Iterator', 'Mapping', 'REPOSITORY_ROOT', '_TRY_TYPES', '_TYPE_ALIAS_TYPE', '_SECTION_NAME_RE', 'SPLIT_ROOTS', 'IDENTITY_WAIVER_PATH', '_legacy_relative_path', '_canonical_source_decomposition_relative', '_current_source_decomposition_relative', '_resolve_source_decomposition_path', '_load_identity_waivers', '_git', '_extract_archive_safely', '_revision_checkout', '_FUNCTION_BODY_RISK_NAMES', '_ENVIRONMENT_MUTATORS', '_without_function_bodies', '_function_body_and_environment_risk_signatures', '_parse_source', '_section_modules_for_root', '_section_package_for_facade', '_split_facades', '_top_level_names', '_direct_import_bound_names', '_declared_public_names', '_PHASE192_TEMPLATE_IMPORTS', '_PHASE192_TEMPLATE_CALLS', '_PHASE192_TEMPLATE_METHODS']
