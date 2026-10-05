"""Compare split Python identity surfaces between two Git revisions."""

from __future__ import annotations

import argparse
import ast
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
import sys

REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
if str(REPOSITORY_ROOT) not in sys.path:
    sys.path.insert(0, str(REPOSITORY_ROOT))

_TRY_TYPES = (ast.Try, getattr(ast, "TryStar", ast.Try))
_TYPE_ALIAS_TYPE = getattr(ast, "TypeAlias", None)
_SECTION_NAME_RE = re.compile(r"[A-Za-z_][A-Za-z0-9_]*\Z")


SPLIT_ROOTS = (
    Path("Scripts/smoke/foxrun"),
    Path("Scripts/smoke/ros2"),
    Path("Scripts/smoke/websocket"),
)
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

def _split_facades(root: Path) -> tuple[Path, ...]:
    """Return split facade paths under supported roots."""
    paths: list[Path] = []
    seen: dict[str, Path] = {}
    for relative_root in SPLIT_ROOTS:
        root_path = root / relative_root
        if not root_path.is_dir():
            continue
        for facade in sorted(root_path.rglob("*.py")):
            package = facade.with_suffix("")
            if package.is_dir() and (package / "__init__.py").is_file():
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

def _is_public_globals_comprehension(value: ast.AST) -> bool:
    """Internal helper for _is_public_globals_comprehension."""
    if not isinstance(value, (ast.ListComp, ast.SetComp)) or len(value.generators) != 1:
        return False
    generator = value.generators[0]
    if (
        generator.is_async
        or not isinstance(value.elt, ast.Name)
        or not isinstance(generator.target, ast.Name)
        or value.elt.id != generator.target.id
        or not isinstance(generator.iter, ast.Call)
        or not isinstance(generator.iter.func, ast.Name)
        or generator.iter.func.id != "globals"
        or generator.iter.args
        or generator.iter.keywords
        or len(generator.ifs) != 1
    ):
        return False
    condition = generator.ifs[0]
    return (
        isinstance(condition, ast.UnaryOp)
        and isinstance(condition.op, ast.Not)
        and isinstance(condition.operand, ast.Call)
        and isinstance(condition.operand.func, ast.Attribute)
        and isinstance(condition.operand.func.value, ast.Name)
        and condition.operand.func.value.id == generator.target.id
        and condition.operand.func.attr == "startswith"
        and len(condition.operand.args) == 1
        and not condition.operand.keywords
        and isinstance(condition.operand.args[0], ast.Constant)
        and condition.operand.args[0].value == "__"
    )

def _bound_names_in_statement(statement: ast.stmt) -> set[str]:
    """Internal helper for _bound_names_in_statement."""
    names: set[str] = set()
    def add_target(target: ast.AST) -> None:
        """Internal helper for add_target."""
        if isinstance(target, ast.Name):
            names.add(target.id)
        elif isinstance(target, (ast.Tuple, ast.List)):
            for item in target.elts:
                add_target(item)
    if isinstance(statement, (ast.FunctionDef, ast.AsyncFunctionDef, ast.ClassDef)):
        names.add(statement.name)
    elif isinstance(statement, ast.Import):
        for alias in statement.names:
            names.add(alias.asname or alias.name.split(".", 1)[0])
    elif isinstance(statement, ast.ImportFrom):
        for alias in statement.names:
            if alias.name != "*":
                names.add(alias.asname or alias.name)
    elif isinstance(statement, (ast.Assign, ast.AnnAssign, ast.AugAssign)):
        targets = statement.targets if isinstance(statement, (ast.Assign, ast.AugAssign)) else [statement.target]
        for target in targets:
            add_target(target)
    elif isinstance(statement, (ast.For, ast.AsyncFor)):
        add_target(statement.target)
    elif isinstance(statement, (ast.With, ast.AsyncWith)):
        for item in statement.items:
            if item.optional_vars is not None:
                add_target(item.optional_vars)
    elif isinstance(statement, _TRY_TYPES):
        for handler in statement.handlers:
            if handler.name:
                names.add(handler.name)
    return names

def _conditional_only_public_names(
    tree: ast.Module,
    *,
    include_private: bool = False,
) -> frozenset[str]:
    """Internal helper for _conditional_only_public_names."""
    control_types = (ast.If, ast.For, ast.AsyncFor, ast.While, *_TRY_TYPES, ast.With, ast.AsyncWith, ast.Match)

    def block_sets(statements: list[ast.stmt]) -> tuple[set[str], set[str]]:
        """Internal helper for block_sets."""
        maybe: set[str] = set()
        guaranteed: set[str] = set()
        for statement in statements:
            statement_maybe, statement_guaranteed = statement_sets(statement)
            maybe.update(statement_maybe)
            guaranteed.update(statement_guaranteed)
        return maybe, guaranteed

    def statement_sets(statement: ast.stmt) -> tuple[set[str], set[str]]:
        """Internal helper for statement_sets."""
        if not isinstance(statement, control_types):
            names = _bound_names_in_statement(statement)
            return set(names), set(names)
        if isinstance(statement, ast.If):
            body_maybe, body_guaranteed = block_sets(statement.body)
            else_maybe, else_guaranteed = block_sets(statement.orelse)
            return body_maybe | else_maybe, body_guaranteed & else_guaranteed if statement.orelse else set()
        if isinstance(statement, _TRY_TYPES):
            body_maybe, body_guaranteed = block_sets(statement.body)
            handlers = [block_sets(handler.body) for handler in statement.handlers]
            maybe = set(body_maybe)
            for handler_maybe, _ in handlers:
                maybe.update(handler_maybe)
            else_maybe, _ = block_sets(statement.orelse)
            maybe.update(else_maybe)
            if handlers:
                guaranteed = set(body_guaranteed)
                for _, handler_guaranteed in handlers:
                    guaranteed.intersection_update(handler_guaranteed)
            else:
                guaranteed = set()
            return maybe, guaranteed
        if isinstance(statement, (ast.For, ast.AsyncFor, ast.While)):
            body_maybe, _ = block_sets(statement.body)
            else_maybe, _ = block_sets(statement.orelse)
            return _bound_names_in_statement(statement) | body_maybe | else_maybe, set()
        if isinstance(statement, (ast.With, ast.AsyncWith)):
            body_maybe, _ = block_sets(statement.body)
            return _bound_names_in_statement(statement) | body_maybe, set()
        if isinstance(statement, ast.Match):
            maybe: set[str] = set()
            for case in statement.cases:
                case_maybe, _ = block_sets(case.body)
                maybe.update(case_maybe)
            return maybe, set()
        return set(), set()

    unconditional: set[str] = set()
    conditional: set[str] = set()
    guaranteed: set[str] = set()
    for statement in tree.body:
        maybe, sure = statement_sets(statement)
        if isinstance(statement, control_types):
            conditional.update(maybe)
            guaranteed.update(sure)
        else:
            unconditional.update(maybe)
    prefix = "__" if include_private else "_"
    return frozenset(
        name for name in conditional - unconditional - guaranteed
        if not name.startswith(prefix)
    )

def _conditional_contract_signatures(tree: ast.AST) -> tuple[str, ...]:
    """Return stable signatures for import-time conditional bindings."""
    signatures: list[str] = []

    def bound_in_block(statements: list[ast.stmt]) -> set[str]:
        """Internal helper for bound_in_block."""
        names: set[str] = set()
        for statement in statements:
            names.update(_bound_names_in_statement(statement))
            if isinstance(statement, (ast.FunctionDef, ast.AsyncFunctionDef)):
                continue
            if isinstance(statement, ast.ClassDef):
                names.update(bound_in_block(statement.body))
                continue
            if isinstance(statement, ast.If):
                names.update(bound_in_block(statement.body))
                names.update(bound_in_block(statement.orelse))
            elif isinstance(statement, (ast.For, ast.AsyncFor, ast.While)):
                names.update(bound_in_block(statement.body))
                names.update(bound_in_block(statement.orelse))
            elif isinstance(statement, (ast.With, ast.AsyncWith)):
                names.update(bound_in_block(statement.body))
            elif isinstance(statement, _TRY_TYPES):
                names.update(bound_in_block(statement.body))
                for handler in statement.handlers:
                    names.update(bound_in_block(handler.body))
                names.update(bound_in_block(statement.orelse))
                names.update(bound_in_block(statement.finalbody))
            elif isinstance(statement, ast.Match):
                for case in statement.cases:
                    names.update(bound_in_block(case.body))
        return names

    def digest(
        kind: str,
        header: ast.AST,
        names: set[str],
        body: list[ast.stmt],
        *,
        structure: ast.AST | None = None,
    ) -> None:
        """Internal helper for digest."""
        name_text = ",".join(sorted(names))
        body_shape = structure if structure is not None else ast.Module(body=body, type_ignores=[])
        payload = (
            f"{kind}|{name_text}|"
            f"{ast.dump(header, include_attributes=False)}|"
            f"{ast.dump(body_shape, include_attributes=False)}"
        )
        signature = hashlib.sha256(payload.encode("utf-8")).hexdigest()
        signatures.append(f"{name_text}:{signature}")

    def visit_statements(statements: list[ast.stmt]) -> None:
        """Internal helper for visit_statements."""
        for statement in statements:
            if isinstance(statement, (ast.FunctionDef, ast.AsyncFunctionDef)):
                continue
            if isinstance(statement, ast.ClassDef):
                visit_statements(statement.body)
                continue
            if isinstance(statement, ast.If):
                digest("If", statement.test, bound_in_block(statement.body + statement.orelse), statement.body + statement.orelse, structure=statement)
                visit_statements(statement.body)
                visit_statements(statement.orelse)
                continue
            if isinstance(statement, (ast.For, ast.AsyncFor)):
                digest(
                    type(statement).__name__,
                    ast.Tuple(elts=[statement.target, statement.iter], ctx=ast.Load()),
                    bound_in_block(statement.body + statement.orelse) | _bound_names_in_statement(statement),
                    statement.body + statement.orelse,
                    structure=statement,
                )
                visit_statements(statement.body)
                visit_statements(statement.orelse)
                continue
            if isinstance(statement, ast.While):
                digest("While", statement.test, bound_in_block(statement.body + statement.orelse), statement.body + statement.orelse, structure=statement)
                visit_statements(statement.body)
                visit_statements(statement.orelse)
                continue
            if isinstance(statement, (ast.With, ast.AsyncWith)):
                digest(
                    type(statement).__name__,
                    ast.Tuple(
                        elts=[
                            ast.Tuple(
                                elts=[item.context_expr, item.optional_vars or ast.Constant(None)],
                                ctx=ast.Load(),
                            )
                            for item in statement.items
                        ],
                        ctx=ast.Load(),
                    ),
                    bound_in_block(statement.body) | _bound_names_in_statement(statement),
                    statement.body,
                    structure=statement,
                )
                visit_statements(statement.body)
                continue
            if isinstance(statement, _TRY_TYPES):
                all_blocks = list(statement.body) + list(statement.orelse) + list(statement.finalbody)
                for handler in statement.handlers:
                    all_blocks.extend(handler.body)
                digest(
                    type(statement).__name__,
                    ast.Tuple(
                        elts=[
                            ast.Tuple(
                                elts=[handler.type or ast.Constant(None), ast.Constant(handler.name)],
                                ctx=ast.Load(),
                            )
                            for handler in statement.handlers
                        ],
                        ctx=ast.Load(),
                    ),
                    bound_in_block(all_blocks),
                    all_blocks,
                    structure=statement,
                )
                visit_statements(statement.body)
                for handler in statement.handlers:
                    visit_statements(handler.body)
                visit_statements(statement.orelse)
                visit_statements(statement.finalbody)
                continue
            if isinstance(statement, ast.Match):
                digest(
                    "Match",
                    statement.subject,
                    set().union(*(bound_in_block(case.body) for case in statement.cases)),
                    [item for case in statement.cases for item in case.body],
                    structure=statement,
                )
                for case in statement.cases:
                    visit_statements(case.body)

    visit_statements(getattr(tree, "body", []))
    return tuple(signatures)

def _reject_unsupported_module_bindings(tree: ast.Module, path: str) -> None:
    """Internal helper for _reject_unsupported_module_bindings."""
    if _TYPE_ALIAS_TYPE is not None and any(isinstance(node, _TYPE_ALIAS_TYPE) for node in ast.walk(tree)):
        raise ValueError(f"Unsupported type alias binding in {path}")

    class Visitor(ast.NodeVisitor):
        """Internal helper for Visitor."""
        def __init__(self) -> None:
            """Internal helper for __init__."""
            self._class_depth = 0

        def _visit_definition_header(self, node: ast.FunctionDef | ast.AsyncFunctionDef) -> None:
            """Internal helper for _visit_definition_header."""
            for decorator in node.decorator_list:
                self.visit(decorator)
            arguments = [
                *node.args.posonlyargs, *node.args.args, *node.args.kwonlyargs,
                *([node.args.vararg] if node.args.vararg else []),
                *([node.args.kwarg] if node.args.kwarg else []),
            ]
            for argument in arguments:
                if argument.annotation is not None:
                    self.visit(argument.annotation)
            for default in [*node.args.defaults, *[value for value in node.args.kw_defaults if value is not None]]:
                self.visit(default)
            if node.returns is not None:
                self.visit(node.returns)
        def visit_FunctionDef(self, node: ast.FunctionDef) -> None:
            """Internal helper for visit_FunctionDef."""
            self._visit_definition_header(node)
        def visit_AsyncFunctionDef(self, node: ast.AsyncFunctionDef) -> None:
            """Internal helper for visit_AsyncFunctionDef."""
            self._visit_definition_header(node)
        def visit_ClassDef(self, node: ast.ClassDef) -> None:
            """Internal helper for visit_ClassDef."""
            for decorator in node.decorator_list:
                self.visit(decorator)
            for base in node.bases:
                self.visit(base)
            for keyword in node.keywords:
                self.visit(keyword.value)
            self._class_depth += 1
            try:
                for statement in node.body:
                    self.visit(statement)
            finally:
                self._class_depth -= 1
        def visit_Global(self, node: ast.Global) -> None:
            """Internal helper for visit_Global."""
            if any(not name.startswith("_") for name in node.names):
                raise ValueError(f"Unsupported public global binding in {path}")
        def visit_Import(self, node: ast.Import) -> None:
            """Internal helper for visit_Import."""
            if any(alias.name.split(".", 1)[0] in {"builtins", "__builtins__", "__builtin__"} for alias in node.names):
                raise ValueError(f"Unsupported builtins import in {path}")
        def visit_ImportFrom(self, node: ast.ImportFrom) -> None:
            """Internal helper for visit_ImportFrom."""
            if node.module in {"builtins", "__builtins__", "__builtin__"}:
                raise ValueError(f"Unsupported builtins import in {path}")
            self.generic_visit(node)
        def visit_NamedExpr(self, node: ast.NamedExpr) -> None:
            """Internal helper for visit_NamedExpr."""
            raise ValueError(f"Unsupported module binding expression in {path}")
        def visit_Match(self, node: ast.Match) -> None:
            """Internal helper for visit_Match."""
            raise ValueError(f"Unsupported module pattern binding in {path}")
        def visit_Delete(self, node: ast.Delete) -> None:
            """Internal helper for visit_Delete."""
            if self._class_depth:
                raise ValueError(f"Unsupported class namespace deletion in {path}")
            def unsafe_target(target: ast.AST) -> bool:
                """Internal helper for unsafe_target."""
                if isinstance(target, ast.Name):
                    return not target.id.startswith(("_phase192_", "_Phase192", "_PHASE192_"))
                if isinstance(target, (ast.Tuple, ast.List)):
                    return any(unsafe_target(item) for item in target.elts)
                return True
            if any(unsafe_target(target) for target in node.targets):
                raise ValueError(f"Unsupported module deletion in {path}")
            self.generic_visit(node)
    Visitor().visit(tree)

def _validate_namespace_access(
    tree: ast.Module,
    allowed_nodes: set[int],
    path: str,
    *,
    reject_module_registry: bool = False,
) -> None:
    """Internal helper for _validate_namespace_access."""
    parents: dict[int, ast.AST] = {}
    for parent in ast.walk(tree):
        for child in ast.iter_child_nodes(parent):
            parents[id(child)] = parent

    # Track objects that can expose the module registry while preserving
    # ordinary dynamic attribute access on runtime objects.
    indirect_namespace_names = {
        "getattr", "setattr", "delattr", "__getattribute__",
    }
    sys_aliases: set[str] = {"sys"}
    importlib_aliases: set[str] = set()
    import_module_callable_aliases: set[str] = set()
    dunder_import_aliases: set[str] = set()
    import_module_aliases: set[str] = set()
    dunder_import_denied_roots = {
        "builtins", "__builtins__", "__builtin__", "importlib", "os",
        "subprocess", "sys",
    }
    dunder_import_denied_attrs = {
        "__dict__", "__getattribute__", "__setattr__", "__delattr__",
        "modules", "open", "system", "popen", "run", "Popen", "call",
        "check_call", "check_output", "exec", "eval", "compile",
        "import_module", "remove", "unlink", "write", "write_text",
        "write_bytes", "mkdir", "rmdir", "rename", "replace", "chmod",
        "connect", "send", "recv",
    }

    def fixed_string(node: ast.AST | None) -> str | None:
        """Internal helper for fixed_string."""
        if isinstance(node, ast.Constant) and isinstance(node.value, str):
            return node.value
        if isinstance(node, ast.BinOp) and isinstance(node.op, ast.Add):
            left = fixed_string(node.left)
            right = fixed_string(node.right)
            if left is not None and right is not None:
                return left + right
        return None

    def dunder_import_call(node: ast.AST | None) -> ast.Call | None:
        """Internal helper for dunder_import_call."""
        if (
            isinstance(node, ast.Call)
            and isinstance(node.func, ast.Name)
            and node.func.id == "__import__"
        ):
            return node
        return None

    def validate_dunder_import(node: ast.Call) -> None:
        """Internal helper for validate_dunder_import."""
        if not node.args or any(keyword.arg != "fromlist" for keyword in node.keywords):
            raise ValueError(f"Unsupported dynamic import in {path}")
        module_name = fixed_string(node.args[0])
        if module_name is None or module_name.split(".", 1)[0] in dunder_import_denied_roots:
            raise ValueError(f"Unsupported dynamic import in {path}")
        for keyword in node.keywords:
            value = keyword.value
            if not isinstance(value, (ast.List, ast.Tuple)) or not all(
                isinstance(item, ast.Constant) and isinstance(item.value, str)
                for item in value.elts
            ):
                raise ValueError(f"Unsupported dynamic import fromlist in {path}")

    def contains_dunder_import(node: ast.AST) -> bool:
        """Internal helper for contains_dunder_import."""
        return any(dunder_import_call(item) is not None for item in ast.walk(node))

    def importlib_object(node: ast.AST | None) -> bool:
        """Internal helper for importlib_object."""
        return isinstance(node, ast.Name) and node.id in importlib_aliases

    def import_module_callable(node: ast.AST | None) -> bool:
        """Internal helper for import_module_callable."""
        return (
            isinstance(node, ast.Name)
            and node.id in import_module_callable_aliases
        ) or (
            isinstance(node, ast.Attribute)
            and node.attr == "import_module"
            and importlib_object(node.value)
        )

    def imported_module_call(node: ast.AST | None) -> bool:
        """Internal helper for imported_module_call."""
        return isinstance(node, ast.Call) and import_module_callable(node.func)

    static_string_values: dict[str, str] = {}
    static_string_candidates: dict[str, set[str] | None] = {}
    for assignment in ast.walk(tree):
        if not isinstance(assignment, (ast.Assign, ast.AnnAssign)):
            continue
        value = assignment.value
        fixed = fixed_string(value)
        targets = assignment.targets if isinstance(assignment, ast.Assign) else [assignment.target]
        for target in targets:
            if not isinstance(target, ast.Name):
                continue
            if fixed is None:
                static_string_candidates[target.id] = None
            elif target.id not in static_string_candidates:
                static_string_candidates[target.id] = {fixed}
            elif static_string_candidates[target.id] is not None:
                static_string_candidates[target.id].add(fixed)
    for name, candidates in static_string_candidates.items():
        if candidates and len(candidates) == 1:
            static_string_values[name] = next(iter(candidates))

    changed = True
    while changed:
        changed = False
        for node in ast.walk(tree):
            if isinstance(node, ast.Import):
                for alias in node.names:
                    if alias.name == "sys":
                        name = alias.asname or "sys"
                        if name not in sys_aliases:
                            sys_aliases.add(name)
                            changed = True
                    if alias.name == "importlib" or alias.name.startswith("importlib."):
                        name = alias.asname or alias.name.split(".", 1)[0]
                        if name not in importlib_aliases:
                            importlib_aliases.add(name)
                            changed = True
            elif isinstance(node, ast.ImportFrom) and node.module == "sys":
                for alias in node.names:
                    if alias.name == "sys":
                        name = alias.asname or "sys"
                        if name not in sys_aliases:
                            sys_aliases.add(name)
                            changed = True
            elif isinstance(node, (ast.Assign, ast.AnnAssign)):
                value = node.value
                imported = dunder_import_call(value)
                if imported is not None:
                    validate_dunder_import(imported)
                    targets = node.targets if isinstance(node, ast.Assign) else [node.target]
                    for target in targets:
                        if isinstance(target, ast.Name):
                            dunder_import_aliases.add(target.id)
                elif (
                    isinstance(value, ast.Call)
                    and isinstance(value.func, ast.Attribute)
                    and value.func.attr == "import_module"
                ):
                    module_name = fixed_string(value.args[0]) if value.args else None
                    if module_name is None and value.args and isinstance(value.args[0], ast.Name):
                        module_name = static_string_values.get(value.args[0].id)
                    if module_name is not None and module_name.split(".", 1)[0] in dunder_import_denied_roots:
                        targets = node.targets if isinstance(node, ast.Assign) else [node.target]
                        for target in targets:
                            if isinstance(target, ast.Name):
                                import_module_aliases.add(target.id)
                elif isinstance(value, ast.Name) and value.id in dunder_import_aliases:
                    targets = node.targets if isinstance(node, ast.Assign) else [node.target]
                    for target in targets:
                        if isinstance(target, ast.Name):
                            dunder_import_aliases.add(target.id)
                elif isinstance(value, ast.Name) and value.id in import_module_aliases:
                    targets = node.targets if isinstance(node, ast.Assign) else [node.target]
                    for target in targets:
                        if isinstance(target, ast.Name):
                            import_module_aliases.add(target.id)
                if importlib_object(value):
                    targets = node.targets if isinstance(node, ast.Assign) else [node.target]
                    for target in targets:
                        if isinstance(target, ast.Name) and target.id not in importlib_aliases:
                            importlib_aliases.add(target.id)
                            changed = True
                if (
                    isinstance(value, ast.Attribute)
                    and value.attr == "import_module"
                    and importlib_object(value.value)
                ):
                    targets = node.targets if isinstance(node, ast.Assign) else [node.target]
                    for target in targets:
                        if isinstance(target, ast.Name):
                            import_module_callable_aliases.add(target.id)
                if isinstance(value, ast.Name) and value.id in import_module_callable_aliases:
                    targets = node.targets if isinstance(node, ast.Assign) else [node.target]
                    for target in targets:
                        if isinstance(target, ast.Name):
                            import_module_callable_aliases.add(target.id)
                if isinstance(value, ast.Name) and value.id in sys_aliases:
                    targets = node.targets if isinstance(node, ast.Assign) else [node.target]
                    for target in targets:
                        if isinstance(target, ast.Name) and target.id not in sys_aliases:
                            sys_aliases.add(target.id)
                            changed = True
    imported_module_aliases: set[str] = set(import_module_aliases)
    changed = True
    while changed:
        changed = False
        for node in ast.walk(tree):
            if not isinstance(node, (ast.Assign, ast.AnnAssign)):
                continue
            value = node.value
            targets = node.targets if isinstance(node, ast.Assign) else [node.target]
            if imported_module_call(value) or (
                isinstance(value, ast.Name) and value.id in imported_module_aliases
            ):
                for target in targets:
                    if isinstance(target, ast.Name) and target.id not in imported_module_aliases:
                        imported_module_aliases.add(target.id)
                        changed = True
    imported_module_names = dunder_import_aliases | import_module_aliases | imported_module_aliases

    for node in ast.walk(tree):
        if (
            isinstance(node, ast.Call)
            and isinstance(node.func, ast.Name)
            and node.func.id == "vars"
            and node.args
            and isinstance(node.args[0], ast.Name)
            and node.args[0].id in sys_aliases | imported_module_names
        ):
            raise ValueError(f"Unsupported namespace object lookup in {path}")
        if isinstance(node, (ast.Assign, ast.AnnAssign, ast.NamedExpr)):
            value = node.value
            targets = node.targets if isinstance(node, ast.Assign) else [node.target]
            if isinstance(value, ast.Name) and value.id in indirect_namespace_names:
                raise ValueError(f"Unsupported namespace helper alias in {path}")
            if isinstance(value, ast.Attribute) and value.attr in {
                "__getattribute__", "__setattr__", "__delattr__",
                "attrgetter", "methodcaller", "partial", "import_module",
            }:
                raise ValueError(f"Unsupported indirect namespace helper in {path}")
            if isinstance(value, ast.Call) and isinstance(value.func, ast.Attribute) and value.func.attr in {
                "attrgetter", "methodcaller", "partial",
            }:
                raise ValueError(f"Unsupported indirect namespace helper in {path}")
        if isinstance(node, ast.Call):
            if (
                isinstance(node.func, ast.Name)
                and node.func.id == "getattr"
                and node.args
                and importlib_object(node.args[0])
            ):
                raise ValueError(f"Unsupported dynamic import lookup in {path}")
            if (
                isinstance(node.func, ast.Name)
                and node.func.id in {"getattr", "setattr", "delattr"}
                and node.args
                and isinstance(node.args[0], ast.Name)
                and node.args[0].id in sys_aliases | imported_module_names
            ):
                if len(node.args) < 2:
                    raise ValueError(f"Unsupported dynamic namespace helper in {path}")
                key_node = node.args[1]
                key = (
                    key_node.value
                    if isinstance(key_node, ast.Constant) and isinstance(key_node.value, str)
                    else None
                )
                if key is None or key in {
                    "modules", "__builtins__", "__import__", "import_module",
                    "getattr", "setattr", "delattr",
                } | dunder_import_denied_attrs:
                    raise ValueError(f"Unsupported dynamic namespace key in {path}")
            if isinstance(node.func, ast.Attribute):
                base = node.func.value
                if (
                    isinstance(base, ast.Name)
                    and base.id in {"operator", "functools"}
                    and node.func.attr in {"attrgetter", "methodcaller", "partial"}
                ):
                    raise ValueError(f"Unsupported indirect namespace helper in {path}")
                if (
                    node.func.attr in {"__getattribute__", "__setattr__", "__delattr__"}
                    and isinstance(base, ast.Name)
                    and base.id in sys_aliases | imported_module_names
                ):
                    if node.func.attr in {"__setattr__", "__delattr__"}:
                        raise ValueError(f"Unsupported namespace mutation in {path}")
                    key = fixed_string(node.args[-1]) if node.args else None
                    if key is None or key in {
                        "modules", "__dict__", "__builtins__", "__import__",
                        "import_module", "getattr", "setattr", "delattr",
                    } | dunder_import_denied_attrs:
                        raise ValueError(f"Unsupported dynamic namespace key in {path}")
                if (
                    node.func.attr in {"__getattribute__", "__setattr__", "__delattr__"}
                    and node.args
                    and isinstance(node.args[0], ast.Name)
                    and node.args[0].id in sys_aliases | imported_module_names
                ):
                    if node.func.attr in {"__setattr__", "__delattr__"}:
                        raise ValueError(f"Unsupported namespace mutation in {path}")
                    key = fixed_string(node.args[-1])
                    if key is None or key in {
                        "modules", "__dict__", "__builtins__", "__import__",
                        "import_module", "getattr", "setattr", "delattr",
                    } | dunder_import_denied_attrs:
                        raise ValueError(f"Unsupported dynamic namespace key in {path}")
    namespace_names = {"globals", "vars", "locals"}
    dynamic_names = {"exec", "eval", "compile"}
    builtin_names = {"builtins", "__builtins__", "__builtin__"}
    mutation_attrs = {"clear", "pop", "popitem", "setdefault", "update", "__setitem__", "__delitem__"}
    read_attrs = {"get", "__getitem__", "__contains__", "keys", "items", "values", "copy"}
    dangerous_keys = dynamic_names | {"__builtins__", "__import__", "import_module", "getattr", "setattr", "delattr", "__dict__", "f_globals", "f_locals"}

    def literal_string(node: ast.AST | None) -> str | None:
        """Internal helper for literal_string."""
        return node.value if isinstance(node, ast.Constant) and isinstance(node.value, str) else None
    def constant_string(node: ast.AST | None) -> str | None:
        """Fold only literal string concatenations used as namespace keys."""
        literal = literal_string(node)
        if literal is not None:
            return literal
        if isinstance(node, ast.BinOp) and isinstance(node.op, ast.Add):
            left = constant_string(node.left)
            right = constant_string(node.right)
            if left is not None and right is not None:
                return left + right
        return None
    def namespace_call(node: ast.AST | None) -> bool:
        """Internal helper for namespace_call."""
        return isinstance(node, ast.Call) and isinstance(node.func, ast.Name) and node.func.id in namespace_names and not node.args and not node.keywords

    def reject_namespace_helper_aliases() -> None:
        """Reject namespace helper attributes when they are rebound as values."""
        for item in ast.walk(tree):
            if not (
                isinstance(item, ast.Attribute)
                and namespace_call(item.value)
                and item.attr in read_attrs
            ):
                continue
            parent = parents.get(id(item))
            if item.attr == "copy":
                raise ValueError(f"Unsupported namespace helper alias in {path}")
            if not (isinstance(parent, ast.Call) and parent.func is item):
                raise ValueError(f"Unsupported namespace helper alias in {path}")

    if reject_module_registry:
        reject_namespace_helper_aliases()

    def key_node(node: ast.Call | ast.Subscript) -> ast.AST | None:
        """Internal helper for key_node."""
        return node.slice if isinstance(node, ast.Subscript) else (node.args[0] if node.args else None)
    def check_key(node: ast.Call | ast.Subscript) -> None:
        """Internal helper for check_key."""
        key_ast = key_node(node)
        key = literal_string(key_ast)
        if key is None:
            raise ValueError(f"Unsupported dynamic namespace key in {path}")
        if key in dangerous_keys:
            raise ValueError(f"Unsupported dynamic execution namespace key in {path}")
    def literal_modules_key(node: ast.AST | None) -> bool:
        """Internal helper for literal_modules_key."""
        return isinstance(node, ast.Constant) and node.value == "modules"

    def namespace_object(node: ast.AST) -> bool:
        """Internal helper for namespace_object."""
        return (
            isinstance(node, ast.Call)
            and isinstance(node.func, ast.Name)
            and node.func.id in namespace_names
            and bool(node.args)
        )

    module_registry_aliases: set[str] = set()

    def contains_module_registry(node: ast.AST) -> bool:
        """Internal helper for contains_module_registry."""
        if isinstance(node, ast.Name) and node.id in module_registry_aliases:
            return True
        if isinstance(node, ast.Attribute) and node.attr == "modules":
            return True
        if isinstance(node, ast.Call):
            if (
                isinstance(node.func, ast.Name)
                and node.func.id == "getattr"
                and len(node.args) >= 2
                and literal_modules_key(node.args[1])
            ):
                return True
            if (
                isinstance(node.func, ast.Attribute)
                and node.func.attr == "__getattribute__"
                and node.args
                and literal_modules_key(node.args[-1])
            ):
                return True
        if isinstance(node, ast.Subscript):
            if literal_modules_key(node.slice) and namespace_object(node.value):
                return True
            if (
                isinstance(node.value, ast.Call)
                and isinstance(node.value.func, ast.Name)
                and node.value.func.id == "vars"
                and node.value.args
                and isinstance(node.value.args[0], ast.Name)
                and node.value.args[0].id in sys_aliases | dunder_import_aliases | import_module_aliases
            ):
                return True
            if literal_modules_key(node.slice) and isinstance(node.value, ast.Call):
                if (
                    isinstance(node.value.func, ast.Attribute)
                    and node.value.func.attr in {"getattr", "__getattribute__"}
                ):
                    return True
        return any(contains_module_registry(child) for child in ast.iter_child_nodes(node))

    def target_names(target: ast.AST) -> set[str]:
        """Internal helper for target_names."""
        if isinstance(target, ast.Name):
            return {target.id}
        if isinstance(target, (ast.Tuple, ast.List)):
            names: set[str] = set()
            for item in target.elts:
                names.update(target_names(item))
            return names
        return set()

    changed = True
    while changed:
        changed = False
        for statement in ast.walk(tree):
            if isinstance(statement, ast.ImportFrom) and statement.module == "sys":
                for alias in statement.names:
                    if alias.name == "modules":
                        name = alias.asname or alias.name
                        if name not in module_registry_aliases:
                            module_registry_aliases.add(name)
                            changed = True
            if isinstance(statement, ast.Assign):
                targets = statement.targets
                value = statement.value
            elif isinstance(statement, ast.AnnAssign):
                targets = [statement.target]
                value = statement.value
            else:
                continue
            if value is None or not contains_module_registry(value):
                continue
            for target in targets:
                for name in target_names(target):
                    if name not in module_registry_aliases:
                        module_registry_aliases.add(name)
                        changed = True
    for executable in ast.walk(tree):
        if isinstance(executable, (ast.FunctionDef, ast.AsyncFunctionDef, ast.ClassDef, ast.Lambda)):
            for child in ast.walk(executable):
                if isinstance(child, ast.Return) and child.value is not None and contains_module_registry(child.value):
                    raise ValueError(f"Unsupported module registry return in {path}")
        if isinstance(executable, (ast.Assign, ast.AnnAssign)):
            value = executable.value
            targets = executable.targets if isinstance(executable, ast.Assign) else [executable.target]
            if value is not None and contains_module_registry(value):
                def allowed_target(target: ast.AST) -> bool:
                    """Internal helper for allowed_target."""
                    if isinstance(target, ast.Name):
                        return target.id.startswith("_phase192_")
                    return (
                        isinstance(target, ast.Subscript)
                        and isinstance(target.value, ast.Attribute)
                        and target.value.attr == "modules"
                        and isinstance(target.value.value, ast.Name)
                        and target.value.value.id.startswith("_phase192_")
                    )
                if any(not allowed_target(target) for target in targets):
                    raise ValueError(f"Unsupported module registry alias in {path}")

    def contains_registry_mutation(node: ast.AST) -> bool:
        """Internal helper for contains_registry_mutation."""
        if isinstance(node, ast.Attribute) and contains_module_registry(node.value):
            if isinstance(node.ctx, (ast.Store, ast.Del)) or node.attr in {"__setattr__", "__delattr__"}:
                return True
        if isinstance(node, ast.Subscript) and contains_module_registry(node.value):
            if not isinstance(node.ctx, ast.Load):
                return True
        if isinstance(node, ast.Call):
            if isinstance(node.func, ast.Name) and node.func.id in {"setattr", "delattr"}:
                if node.args and contains_module_registry(node.args[0]):
                    return True
            if isinstance(node.func, ast.Attribute) and node.func.attr in mutation_attrs:
                if contains_module_registry(node.func.value):
                    return True
            if isinstance(node.func, ast.Attribute) and node.func.attr in {"__setattr__", "__delattr__"}:
                if any(contains_module_registry(argument) for argument in node.args):
                    return True
        return any(contains_registry_mutation(child) for child in ast.iter_child_nodes(node))

    if reject_module_registry:
        executable_types = (
            ast.FunctionDef, ast.AsyncFunctionDef, ast.Lambda,
            ast.ListComp, ast.SetComp, ast.DictComp, ast.GeneratorExp,
        )
        for executable in ast.walk(tree):
            if isinstance(executable, (ast.FunctionDef, ast.AsyncFunctionDef, ast.Lambda)):
                definition_expressions = [
                    *executable.args.defaults,
                    *[value for value in executable.args.kw_defaults if value is not None],
                    *[
                        annotation
                        for annotation in [
                            getattr(executable, "returns", None),
                            *(
                                argument.annotation
                                for argument in [
                                    *executable.args.posonlyargs,
                                    *executable.args.args,
                                    *executable.args.kwonlyargs,
                                    *([executable.args.vararg] if executable.args.vararg else []),
                                    *([executable.args.kwarg] if executable.args.kwarg else []),
                                ]
                            ),
                        ]
                        if annotation is not None
                    ],
                ]
                if any(contains_module_registry(expression) for expression in definition_expressions):
                    raise ValueError(f"Unsupported module registry alias in {path}")
            if isinstance(executable, ast.ClassDef):
                if any(contains_module_registry(base) for base in executable.bases):
                    raise ValueError(f"Unsupported module registry alias in {path}")
            if isinstance(executable, executable_types) and contains_registry_mutation(executable):
                raise ValueError(f"Unsupported module namespace escape in {path}")
        safe_registry_consumers = {
            "tuple", "list", "set", "frozenset", "dict", "len", "any", "all",
            "sum", "min", "max", "sorted", "enumerate", "zip", "iter",
        }
        for call in ast.walk(tree):
            if not isinstance(call, ast.Call) or not any(
                contains_module_registry(argument) for argument in call.args
            ):
                continue
            if isinstance(call.func, ast.Lambda):
                raise ValueError(f"Unsupported module namespace escape in {path}")
            if isinstance(call.func, ast.Name) and call.func.id not in safe_registry_consumers:
                raise ValueError(f"Unsupported module namespace escape in {path}")
            if isinstance(call.func, ast.Attribute) and not (
                call.func.attr == "dict"
                and isinstance(call.func.value, ast.Attribute)
                and call.func.value.attr == "patch"
            ):
                raise ValueError(f"Unsupported module namespace escape in {path}")
        for comprehension in ast.walk(tree):
            if not isinstance(comprehension, (ast.ListComp, ast.SetComp, ast.DictComp, ast.GeneratorExp)):
                continue
            for generator in comprehension.generators:
                if not contains_module_registry(generator.iter):
                    continue
                bound = target_names(generator.target)
                if any(
                    isinstance(node, ast.Name) and node.id in bound
                    for node in ast.walk(comprehension.elt)
                ):
                    raise ValueError(f"Unsupported module namespace escape in {path}")

    for node in ast.walk(tree):
        if id(node) in allowed_nodes:
            continue
        if isinstance(node, ast.Call) and namespace_call(node):
            parent = parents.get(id(node))
            if isinstance(parent, ast.Attribute) and parent.value is node and parent.attr in read_attrs:
                pass
            elif isinstance(parent, ast.Subscript) and parent.value is node and isinstance(parent.ctx, ast.Load):
                check_key(parent)
            else:
                raise ValueError(f"Unsupported namespace escape in {path}")
        if isinstance(node, ast.Global) and any(not name.startswith("_") for name in node.names):
            raise ValueError(f"Unsupported public global binding in {path}")
        if isinstance(node, ast.Import):
            if any(alias.name.split(".", 1)[0] in builtin_names or alias.name.rsplit(".", 1)[-1] in dynamic_names for alias in node.names):
                raise ValueError(f"Unsupported dynamic execution import in {path}")
        if isinstance(node, ast.ImportFrom):
            module_root = (node.module or "").split(".", 1)[0]
            forbidden_aliases = {
                "__dict__", "__getattribute__", "__setattr__", "__delattr__",
                "attrgetter", "methodcaller", "partial",
            }
            if (
                module_root in builtin_names
                or any(
                    alias.name in dynamic_names
                    or alias.name == "import_module"
                    or (
                        module_root in {"sys", "operator", "functools"}
                        and alias.name in forbidden_aliases
                    )
                    for alias in node.names
                )
            ):
                raise ValueError(f"Unsupported dynamic execution import in {path}")
        if isinstance(node, ast.Name):
            if node.id in builtin_names or node.id in dynamic_names:
                raise ValueError(f"Unsupported dynamic execution in {path}")
            if node.id in indirect_namespace_names:
                parent = parents.get(id(node))
                if not (isinstance(parent, ast.Call) and parent.func is node):
                    raise ValueError(f"Unsupported namespace helper alias in {path}")
            if node.id == "__import__":
                parent = parents.get(id(node))
                if not (isinstance(parent, ast.Call) and parent.func is node):
                    raise ValueError(f"Unsupported dynamic import alias in {path}")
            if node.id in namespace_names:
                parent = parents.get(id(node))
                if not isinstance(parent, ast.Call) or parent.func is not node:
                    raise ValueError(f"Unsupported namespace alias in {path}")
                grandparent = parents.get(id(parent))
                if isinstance(grandparent, ast.Subscript) and not isinstance(grandparent.ctx, ast.Load):
                    raise ValueError(f"Unsupported namespace mutation in {path}")
                if isinstance(grandparent, ast.Attribute) and grandparent.attr in mutation_attrs:
                    raise ValueError(f"Unsupported namespace mutation in {path}")
                if isinstance(grandparent, ast.Call) and isinstance(grandparent.func, ast.Name) and grandparent.func.id in {"setattr", "delattr"}:
                    raise ValueError(f"Unsupported namespace mutation in {path}")
                if isinstance(grandparent, (ast.Assign, ast.AnnAssign, ast.NamedExpr)):
                    raise ValueError(f"Unsupported namespace alias in {path}")
        if isinstance(node, ast.Attribute) and node.attr in {"__dict__", "f_globals", "f_locals", "_getframe"}:
            raise ValueError(f"Unsupported namespace access in {path}")
        if (
            isinstance(node, ast.Attribute)
            and node.attr in dunder_import_denied_attrs
            and (
                contains_dunder_import(node.value)
                or (
                    isinstance(node.value, ast.Name)
                    and node.value.id in importlib_aliases | imported_module_names
                )
                or imported_module_call(node.value)
            )
            and not (
                node.attr == "import_module"
                and isinstance(parents.get(id(node)), ast.Call)
                and parents[id(node)].func is node
            )
        ):
            raise ValueError(f"Unsupported dynamic import attribute in {path}")
        if isinstance(node, ast.Subscript) and namespace_call(node.value):
            check_key(node)
        if reject_module_registry and (
            (isinstance(node, ast.Attribute) and node.attr == "modules" and not isinstance(node.ctx, ast.Load))
            or (isinstance(node, ast.Attribute) and contains_module_registry(node.value) and isinstance(node.ctx, (ast.Store, ast.Del)))
            or (isinstance(node, ast.Subscript) and contains_module_registry(node.value) and not isinstance(node.ctx, ast.Load))
        ):
            raise ValueError(f"Unsupported module namespace mutation in {path}")
        if reject_module_registry and isinstance(node, ast.Attribute) and node.attr in {"__setattr__", "__delattr__"} and contains_module_registry(node.value):
            raise ValueError(f"Unsupported module namespace mutation in {path}")
        if isinstance(node, ast.Attribute) and node.attr == "import_module":
            parent = parents.get(id(node))
            if not (isinstance(parent, ast.Call) and parent.func is node):
                raise ValueError(f"Unsupported dynamic import alias in {path}")
        if isinstance(node, ast.Call):
            if isinstance(node.func, ast.Name) and node.func.id in dynamic_names:
                raise ValueError(f"Unsupported dynamic execution in {path}")
            imported = dunder_import_call(node)
            if imported is not None:
                validate_dunder_import(imported)
            if isinstance(node.func, ast.Attribute) and node.func.attr in dynamic_names:
                value = node.func.value
                if isinstance(value, ast.Name) and value.id in builtin_names:
                    raise ValueError(f"Unsupported dynamic execution in {path}")
                if isinstance(value, ast.Call) and (
                    (isinstance(value.func, ast.Name) and value.func.id == "__import__")
                    or imported_module_call(value)
                ):
                    raise ValueError(f"Unsupported dynamic execution in {path}")
                if contains_module_registry(value):
                    raise ValueError(f"Unsupported dynamic execution in {path}")
            if isinstance(node.func, ast.Attribute):
                base = node.func.value
                if isinstance(base, ast.Call) and isinstance(base.func, ast.Name) and base.func.id in namespace_names and node.func.attr not in read_attrs:
                    raise ValueError(f"Unsupported namespace mutation in {path}")
                if namespace_call(base) and node.func.attr in read_attrs:
                    check_key(node)
            if isinstance(node.func, ast.Subscript) and namespace_call(node.func.value):
                check_key(node.func)
            if any(
                any(namespace_call(item) for item in ast.walk(argument))
                for argument in [*node.args, *(keyword.value for keyword in node.keywords)]
            ):
                raise ValueError(f"Unsupported namespace escape in {path}")
            if isinstance(node.func, ast.Name) and node.func.id in {"getattr", "setattr", "delattr"} and any(constant_string(argument) in dangerous_keys for argument in node.args[1:]):
                raise ValueError(f"Unsupported namespace access in {path}")
            if reject_module_registry and isinstance(node.func, ast.Name) and node.func.id in {"setattr", "delattr"} and node.args and contains_module_registry(node.args[0]):
                raise ValueError(f"Unsupported module namespace mutation in {path}")
            if reject_module_registry and isinstance(node.func, ast.Attribute) and node.func.attr in mutation_attrs and contains_module_registry(node.func.value):
                raise ValueError(f"Unsupported module namespace mutation in {path}")
            if reject_module_registry and isinstance(node.func, ast.Attribute) and node.func.attr in {"__setattr__", "__delattr__"} and node.args and any(contains_module_registry(argument) for argument in node.args):
                raise ValueError(f"Unsupported module namespace mutation in {path}")
            if isinstance(node.func, ast.Attribute) and node.func.attr == "__getattribute__":
                value = node.func.value
                if isinstance(value, ast.Name) and value.id in {"object", "type"}:
                    raise ValueError(f"Unsupported reflection helper in {path}")
                if isinstance(value, ast.Name) and value.id in builtin_names:
                    raise ValueError(f"Unsupported dynamic execution in {path}")
                if isinstance(value, ast.Call) and isinstance(value.func, ast.Name) and value.func.id == "__import__":
                    raise ValueError(f"Unsupported dynamic execution in {path}")
                if contains_module_registry(value):
                    raise ValueError(f"Unsupported dynamic execution in {path}")
    if any(isinstance(node, ast.Call) and isinstance(node.func, ast.Attribute) and node.func.attr == "import_module" and node.args and constant_string(node.args[0]) in builtin_names for node in ast.walk(tree)):
        raise ValueError(f"Unsupported builtins import in {path}")

def _relative_import_contract(
    tree: ast.Module,
    package_dir: Path,
    source_path: str | Path,
    declared_sections: frozenset[str] | None = None,
) -> tuple[str, ...]:
    """Validate relative imports and return the star-import chain contract."""
    star_contract: list[str] = []
    for node in ast.walk(tree):
        if not isinstance(node, ast.ImportFrom) or not node.level:
            continue
        if node.level != 1:
            raise ValueError(f"Unsupported parent-relative import in {source_path}")
        star_contract.append(ast.dump(node, include_attributes=False))
        if node.module:
            parts = node.module.split(".")
            if any(_SECTION_NAME_RE.fullmatch(part) is None or keyword.iskeyword(part) for part in parts):
                raise ValueError(f"Invalid relative import in {source_path}: {node.module}")
            source_stem = Path(source_path).stem
            if (
                declared_sections is not None
                and parts[0] not in declared_sections
                and parts[0] != source_stem
            ):
                raise ValueError(
                    f"Relative import target is not a declared section in {source_path}: {node.module}"
                )
            target_base = package_dir.joinpath(*parts)
            target = target_base.with_suffix(".py") if target_base.with_suffix(".py").is_file() else target_base / "__init__.py"
            if not target.is_file():
                raise ValueError(f"Relative import target is missing in {source_path}: {node.module}")
        else:
            target = package_dir / "__init__.py"
        if any(alias.name == "*" for alias in node.names):
            continue
        for alias in node.names:
            if alias.name == "*":
                continue
            if node.module:
                target_tree = _parse_source(target.read_text(encoding="utf-8"), target)
                exports = _top_level_names(target_tree, include_imports=True)
                exports.difference_update(_conditional_only_public_names(target_tree, include_private=True))
                if alias.name not in exports:
                    raise ValueError(
                        f"Relative import symbol is missing in {source_path}: {node.module}:{alias.name}"
                    )
            else:
                candidate = package_dir / f"{alias.name}.py"
                candidate_package = package_dir / alias.name / "__init__.py"
                if (
                    declared_sections is not None
                    and alias.name not in declared_sections
                    and (candidate.is_file() or candidate_package.is_file())
                ):
                    raise ValueError(
                        f"Relative import target is not a declared section in {source_path}: {alias.name}"
                    )
                if not candidate.is_file() and not candidate_package.is_file():
                    # A package initializer may export a function or constant directly.
                    package_tree = _parse_source(target.read_text(encoding="utf-8"), target)
                    package_exports = _top_level_names(package_tree, include_imports=True)
                    package_exports.difference_update(
                        _conditional_only_public_names(package_tree, include_private=True)
                    )
                    if alias.name not in package_exports:
                        raise ValueError(
                            f"Relative import target is missing in {source_path}: {alias.name}"
                        )
    return tuple(star_contract)

def _symbols(
    source: str,
    path: str,
    *,
    reject_conditional: bool = True,
) -> frozenset[str]:
    """Internal helper for _symbols."""
    tree = _parse_source(source, path)
    source_file = Path(path)
    if any(isinstance(node, ast.ImportFrom) and node.level for node in ast.walk(tree)):
        if not source_file.is_file():
            raise ValueError(f"Relative import validation requires a concrete source path: {path}")
        _relative_import_contract(tree, source_file.parent, path)
    _reject_unsupported_module_bindings(tree, path)
    allowed_nodes: set[int] = set()
    for node in tree.body:
        if isinstance(node, (ast.Assign, ast.AnnAssign)):
            targets = node.targets if isinstance(node, ast.Assign) else [node.target]
            if any(isinstance(target, ast.Name) and target.id == "__all__" for target in targets):
                if _is_public_globals_comprehension(node.value):
                    allowed_nodes.update(id(item) for item in ast.walk(node.value))
    _validate_namespace_access(tree, allowed_nodes, path)
    conditional = _conditional_only_public_names(tree)
    if reject_conditional and conditional:
        raise ValueError(f"Conditional public bindings are not supported in {path}: {','.join(sorted(conditional))}")
    return frozenset(
        name
        for name in _top_level_names(tree, include_imports=True)
        if not name.startswith("__")
    )

def _section_exports(
    source: str,
    path: str,
    *,
    reject_conditional: bool = True,
) -> frozenset[str]:
    """Internal helper for _section_exports."""
    tree = _parse_source(source, path)
    source_file = Path(path)
    if any(isinstance(node, ast.ImportFrom) and node.level for node in ast.walk(tree)):
        if not source_file.is_file():
            raise ValueError(f"Relative import validation requires a concrete source path: {path}")
        _relative_import_contract(tree, source_file.parent, path)
    _reject_unsupported_module_bindings(tree, path)
    declared = _declared_public_names(tree)
    bound = _top_level_names(tree, include_imports=True)
    direct_values: list[ast.AST] = []
    direct_ids: set[int] = set()
    for node in tree.body:
        targets = list(node.targets) if isinstance(node, ast.Assign) else [node.target] if isinstance(node, ast.AnnAssign) else []
        if any(isinstance(target, ast.Name) and target.id == "__all__" for target in targets):
            direct_values.append(node.value)
            direct_ids.add(id(node))
    allowed_nodes: set[int] = set()
    for value in direct_values:
        if _is_public_globals_comprehension(value):
            allowed_nodes.update(id(item) for item in ast.walk(value))
    _validate_namespace_access(tree, allowed_nodes, path, reject_module_registry=True)
    for node in ast.walk(tree):
        if isinstance(node, (ast.Assign, ast.AnnAssign)):
            targets = list(node.targets) if isinstance(node, ast.Assign) else [node.target]
            if any(isinstance(target, ast.Name) and target.id == "__all__" for target in targets) and id(node) not in direct_ids:
                raise ValueError(f"Nested __all__ assignment in {path}")
        if isinstance(node, (ast.AugAssign, ast.Delete)):
            targets = node.targets if isinstance(node, ast.Delete) else [node.target]
            if any(isinstance(target, ast.Name) and target.id == "__all__" for target in targets):
                raise ValueError(f"Unsupported __all__ mutation in {path}")
        if isinstance(node, ast.Name) and node.id == "__all__" and isinstance(node.ctx, ast.Load):
            raise ValueError(f"Unsupported __all__ read in {path}")
    conditional = _conditional_only_public_names(tree)
    if reject_conditional and conditional:
        raise ValueError(f"Conditional public bindings are not supported in {path}: {','.join(sorted(conditional))}")
    if not direct_values:
        raise ValueError(f"Missing direct __all__ assignment in {path}")
    first = direct_values[0]
    if any(ast.dump(value, include_attributes=False) != ast.dump(first, include_attributes=False) for value in direct_values[1:]):
        raise ValueError(f"Conflicting __all__ assignments in {path}")
    if isinstance(first, (ast.ListComp, ast.SetComp)):
        if _is_public_globals_comprehension(first):
            return frozenset(
                name
                for name in _top_level_names(tree, include_imports=True)
                if not name.startswith("__")
            )
        raise ValueError(f"Unsupported dynamic __all__ in {path}")
    try:
        literal = ast.literal_eval(first)
    except (TypeError, ValueError) as exc:
        raise ValueError(f"Unsupported __all__ in {path}") from exc
    if not isinstance(literal, (list, tuple, set)) or not all(isinstance(item, str) for item in literal):
        raise ValueError(f"Invalid __all__ in {path}")
    missing = sorted(set(literal) - bound)
    if missing:
        raise ValueError(f"__all__ names are not bound in {path}: {','.join(missing)}")
    return frozenset(literal)

def _initializer_lines(initializer: Path) -> tuple[str, ...]:
    """Internal helper for _initializer_lines."""
    tree = _parse_source(initializer.read_text(encoding="utf-8"), initializer)
    return tuple(ast.unparse(statement).strip() for statement in tree.body)

def _initializer_digest(initializer: Path) -> str:
    """Internal helper for _initializer_digest."""
    normalized = initializer.read_bytes().replace(b"\r\n", b"\n").replace(b"\r", b"\n")
    return hashlib.sha256(normalized).hexdigest()

_NAMESPACE_RISK_NAMES = frozenset({
    "__builtins__", "__import__", "builtins", "compile", "delattr", "eval",
    "exec", "getattr", "globals", "import_module", "locals", "setattr",
    "vars",
})
_NAMESPACE_RISK_ATTRIBUTES = frozenset({
    "__dict__", "__getattribute__", "__setattr__", "__delattr__",
    "f_globals", "f_locals", "import_module", "modules", "_getframe",
})

def _namespace_risk_signatures(tree: ast.AST) -> frozenset[str]:
    """Return stable AST fingerprints for namespace and import escape surfaces."""
    signatures: set[str] = set()
    for node in ast.walk(tree):
        risky = False
        if isinstance(node, ast.Global):
            risky = True
        elif isinstance(node, ast.ImportFrom):
            risky = (
                (node.module or "").split(".", 1)[0] in {"builtins", "__builtins__", "__builtin__"}
                or any(alias.name in {"modules", "getattr", "setattr", "delattr", "import_module", "__import__"} for alias in node.names)
            )
        elif isinstance(node, ast.Name):
            risky = node.id in _NAMESPACE_RISK_NAMES
        elif isinstance(node, ast.Attribute):
            risky = node.attr in _NAMESPACE_RISK_ATTRIBUTES
        elif isinstance(node, ast.Call):
            risky = (
                isinstance(node.func, ast.Name)
                and node.func.id in _NAMESPACE_RISK_NAMES
            )
        if risky:
            signatures.add(ast.dump(node, include_attributes=False))
    return frozenset(signatures)

_ENTRYPOINT_IMPORT_ROOTS = frozenset({"argparse", "sys"})

def _entrypoint_contract(
    path: Path,
    allowed_relative_modules: frozenset[str] | None = None,
    *,
    reject_conditional: bool = True,
) -> tuple[bool, str | None, tuple[str, ...], frozenset[str]]:
    """Internal helper for _entrypoint_contract."""
    if not path.is_file():
        return False, None, (), frozenset()
    source = path.read_text(encoding="utf-8")
    compile(source, str(path), "exec")
    tree = _parse_source(source, path)
    package_dir = path.parent
    package_init = package_dir / "__init__.py"
    allowed_relative_modules = (
        None if allowed_relative_modules is None else frozenset(allowed_relative_modules)
    )
    top_level_relative_imports = tuple(
        node for node in tree.body if isinstance(node, ast.ImportFrom) and node.level == 1
    )
    relative_ids = {id(node) for node in top_level_relative_imports}
    for node in ast.walk(tree):
        if not isinstance(node, ast.ImportFrom) or not node.level:
            continue
        if node.level != 1 or id(node) not in relative_ids:
            raise ValueError(f"unsupported nested or parent-relative entrypoint import: {path}")

    def stable_exports(target: Path) -> set[str]:
        """Internal helper for stable_exports."""
        target_tree = _parse_source(target.read_text(encoding="utf-8"), target)
        names = _top_level_names(target_tree, include_imports=True)
        names.difference_update(_conditional_only_public_names(target_tree, include_private=True))
        return names

    package_exports: set[str] = set()
    if package_init.is_file():
        package_tree = _parse_source(package_init.read_text(encoding="utf-8"), package_init)
        package_exports.update(stable_exports(package_init))
        for initializer_node in package_tree.body:
            if not isinstance(initializer_node, ast.ImportFrom) or initializer_node.level != 1:
                continue
            section_names = [initializer_node.module] if initializer_node.module else [
                alias.name for alias in initializer_node.names if alias.name != "*"
            ]
            for section_name in section_names:
                if not section_name or _SECTION_NAME_RE.fullmatch(section_name) is None:
                    continue
                section_path = package_dir / f"{section_name}.py"
                if section_path.is_file():
                    section_tree = _parse_source(section_path.read_text(encoding="utf-8"), section_path)
                    package_exports.update(stable_exports(section_path))
                    package_exports.update(
                        _section_exports(
                            section_path.read_text(encoding="utf-8"),
                            str(section_path),
                            reject_conditional=reject_conditional,
                        )
                    )

    relative_imports = top_level_relative_imports
    if not relative_imports:
        raise ValueError(f"module entrypoint does not import its package: {path}")
    for node in relative_imports:
        if node.module is None:
            missing = sorted(
                alias.name
                for alias in node.names
                if alias.name != "*"
                and (
                    (
                        allowed_relative_modules is not None
                        and alias.name not in allowed_relative_modules
                        and alias.name not in package_exports
                    )
                    or (
                        allowed_relative_modules is None
                        and alias.name not in package_exports
                        and not (package_dir / f"{alias.name}.py").is_file()
                        and not (package_dir / alias.name / "__init__.py").is_file()
                    )
                )
            )
            if missing:
                raise ValueError(f"relative entrypoint export is missing: {path}: {','.join(missing)}")
            continue
        parts = node.module.split(".")
        if any(_SECTION_NAME_RE.fullmatch(part) is None or keyword.iskeyword(part) for part in parts):
            raise ValueError(f"invalid relative module import in entrypoint: {path}")
        if allowed_relative_modules is not None and parts[0] not in allowed_relative_modules:
            raise ValueError(f"unlisted relative entrypoint module: {path}: {node.module}")
        module_file = package_dir.joinpath(*parts).with_suffix(".py")
        module_package = package_dir.joinpath(*parts) / "__init__.py"
        target = module_file if module_file.is_file() else module_package if module_package.is_file() else None
        if target is None:
            raise ValueError(f"relative entrypoint module is missing: {path}: {node.module}")
        target_exports = stable_exports(target)
        if any(alias.name != "*" and alias.name not in target_exports for alias in node.names):
            missing = sorted(alias.name for alias in node.names if alias.name != "*" and alias.name not in target_exports)
            raise ValueError(f"relative entrypoint symbol is missing: {path}: {node.module}:{','.join(missing)}")

    for node in tree.body:
        if isinstance(node, ast.Import) or (isinstance(node, ast.ImportFrom) and node.level == 0 and node.module != "__future__"):
            roots = (
                {alias.name.split(".", 1)[0] for alias in node.names}
                if isinstance(node, ast.Import)
                else {(node.module or "").split(".", 1)[0]}
            )
            if not roots <= _ENTRYPOINT_IMPORT_ROOTS:
                raise ValueError(f"unsupported absolute module entrypoint import: {path}")
            if not _absolute_import_resolves(node):
                raise ValueError(f"unresolvable absolute module entrypoint import: {path}")

    imported_names = {
        alias.asname or alias.name
        for node in tree.body
        if isinstance(node, (ast.Import, ast.ImportFrom))
        and (not isinstance(node, ast.ImportFrom) or node.module != "__future__")
        for alias in node.names
        if alias.name != "*"
    }
    has_star_import = any(
        alias.name == "*" for node in relative_imports for alias in node.names
    )
    dynamic_names: set[str] = set()
    local_names: set[str] = set()
    _ENTRYPOINT_DANGEROUS_NAMES = frozenset({
        "__import__", "builtins", "compile", "delattr", "eval", "exec", "getattr",
        "globals", "locals", "setattr", "vars",
    })
    _ENTRYPOINT_DANGEROUS_ATTRS = frozenset({
        "__dict__", "__getattribute__", "__setattr__", "__delattr__", "modules",
        "__class__", "__mro__", "__subclasses__", "__globals__", "__code__", "__closure__", "__func__",
        "system", "popen", "run", "Popen", "call", "check_call", "check_output",
        "import_module", "exec", "eval", "compile", "setattr", "delattr",
        "open", "write", "write_text", "write_bytes", "unlink", "remove", "mkdir",
        "rmdir", "rename", "replace", "chmod", "connect", "send", "recv",
    })
    entrypoint_builtins = frozenset(set(_SAFE_BUILTIN_NAMES) | {"sorted", "all", "any", "print"})

    def local_function_is_safe(node: ast.FunctionDef | ast.AsyncFunctionDef) -> bool:
        """Internal helper for local_function_is_safe."""
        if not _safe_function_definition(
            node,
            frozenset(set(entrypoint_builtins) | imported_names | local_names | {"__doc__"}),
            postponed_annotations=True,
        ):
            return False
        allowed_call_names = set(entrypoint_builtins) | imported_names | {
            statement.name
            for statement in body
            if isinstance(statement, (ast.FunctionDef, ast.AsyncFunctionDef))
        }
        for child in ast.walk(node):
            if isinstance(child, (ast.Import, ast.ImportFrom, ast.While, ast.For, ast.AsyncFor, *_TRY_TYPES, ast.With, ast.AsyncWith, ast.Global, ast.Nonlocal, ast.NamedExpr, ast.Delete, ast.Assert, ast.Lambda, ast.Yield, ast.YieldFrom, ast.Await)):
                return False
            if isinstance(child, ast.Name) and child.id in _ENTRYPOINT_DANGEROUS_NAMES:
                return False
            if isinstance(child, ast.Attribute) and child.attr in _ENTRYPOINT_DANGEROUS_ATTRS:
                return False
            if isinstance(child, ast.Call):
                if isinstance(child.func, ast.Name):
                    if child.func.id in _ENTRYPOINT_DANGEROUS_NAMES:
                        return False
                    if child.func.id not in allowed_call_names:
                        return False
                if isinstance(child.func, ast.Attribute) and child.func.attr in _ENTRYPOINT_DANGEROUS_ATTRS:
                    return False
        return True

    def is_main_guard(node: ast.If) -> bool:
        """Internal helper for is_main_guard."""
        test = node.test
        return (
            isinstance(test, ast.Compare)
            and len(test.ops) == 1
            and isinstance(test.ops[0], ast.Eq)
            and len(test.comparators) == 1
            and (
                (
                    isinstance(test.left, ast.Name)
                    and test.left.id == "__name__"
                    and isinstance(test.comparators[0], ast.Constant)
                    and test.comparators[0].value == "__main__"
                )
                or (
                    isinstance(test.comparators[0], ast.Name)
                    and test.comparators[0].id == "__name__"
                    and isinstance(test.left, ast.Constant)
                    and test.left.value == "__main__"
                )
            )
        )

    def safe_value(node: ast.AST) -> bool:
        """Internal helper for safe_value."""
        if isinstance(node, (ast.Constant, ast.Name)):
            return True
        if isinstance(node, ast.Attribute):
            return node.attr not in _ENTRYPOINT_DANGEROUS_ATTRS and safe_value(node.value)
        if isinstance(node, ast.Subscript):
            return safe_value(node.value) and safe_value(node.slice)
        if isinstance(node, ast.Slice):
            return all(part is None or safe_value(part) for part in (node.lower, node.upper, node.step))
        if isinstance(node, (ast.Tuple, ast.List, ast.Set)):
            return all(safe_value(item) for item in node.elts)
        return False

    def is_safe_dispatch_call(node: ast.Call) -> bool:
        """Internal helper for is_safe_dispatch_call."""
        if not isinstance(node.func, ast.Name):
            return False
        if node.func.id not in imported_names | local_names | dynamic_names:
            return False
        return all(safe_value(argument) for argument in node.args) and all(
            safe_value(keyword_node.value) for keyword_node in node.keywords
        )

    def validate_guard_statements(statements: list[ast.stmt]) -> bool:
        """Internal helper for validate_guard_statements."""
        delegated = False
        for statement in statements:
            if isinstance(statement, ast.Assign):
                if len(statement.targets) != 1 or not isinstance(statement.targets[0], ast.Name):
                    raise ValueError(f"unsafe module entrypoint assignment: {path}")
                target = statement.targets[0].id
                value = statement.value
                if not target.startswith("_phase192_") or not (
                    isinstance(value, ast.Call)
                    and isinstance(value.func, ast.Attribute)
                    and isinstance(value.func.value, ast.Call)
                    and isinstance(value.func.value.func, ast.Name)
                    and value.func.value.func.id == "globals"
                    and not value.func.value.args
                    and not value.func.value.keywords
                    and value.func.attr == "get"
                    and len(value.args) == 1
                    and isinstance(value.args[0], ast.Constant)
                    and isinstance(value.args[0].value, str)
                    and not value.keywords
                ):
                    raise ValueError(f"unsafe module entrypoint assignment: {path}")
                dynamic_names.add(target)
                continue
            if isinstance(statement, ast.If):
                if statement.orelse or not (
                    isinstance(statement.test, ast.Call)
                    and isinstance(statement.test.func, ast.Name)
                    and statement.test.func.id == "callable"
                    and len(statement.test.args) == 1
                    and not statement.test.keywords
                    and isinstance(statement.test.args[0], ast.Name)
                    and statement.test.args[0].id in dynamic_names
                ):
                    raise ValueError(f"unsafe module entrypoint control flow: {path}")
                delegated = validate_guard_statements(statement.body) or delegated
                continue
            if isinstance(statement, ast.Raise):
                exception = statement.exc
                if not (
                    isinstance(exception, ast.Call)
                    and isinstance(exception.func, ast.Name)
                    and exception.func.id == "SystemExit"
                    and len(exception.args) == 1
                    and not exception.keywords
                    and isinstance(exception.args[0], ast.Call)
                    and is_safe_dispatch_call(exception.args[0])
                ):
                    raise ValueError(f"unsafe module entrypoint raise: {path}")
                delegated = True
                continue
            raise ValueError(f"unsafe module entrypoint statement: {path}")
        return delegated

    body = list(tree.body)
    if body and isinstance(body[0], ast.Expr) and isinstance(body[0].value, ast.Constant) and isinstance(body[0].value.value, str):
        body = body[1:]
    delegated = False
    definitions: list[ast.FunctionDef | ast.AsyncFunctionDef] = []
    for statement in body:
        if isinstance(statement, ast.Import):
            continue
        if isinstance(statement, ast.ImportFrom):
            if statement.module == "__future__":
                if statement.level != 0 or any(alias.name != "annotations" or alias.asname is not None for alias in statement.names):
                    raise ValueError(f"unsupported future import in module entrypoint: {path}")
                continue
            if statement.level == 0:
                continue
            if statement.level != 1:
                raise ValueError(f"unsupported relative import in module entrypoint: {path}")
            continue
        if isinstance(statement, (ast.FunctionDef, ast.AsyncFunctionDef)):
            if statement.decorator_list or not local_function_is_safe(statement):
                raise ValueError(f"unsafe module entrypoint definition: {path}")
            definitions.append(statement)
            local_names.add(statement.name)
            continue
        if isinstance(statement, ast.ClassDef):
            raise ValueError(f"unsafe module entrypoint class definition: {path}")
        if isinstance(statement, ast.If) and is_main_guard(statement):
            delegated = validate_guard_statements(statement.body) or delegated
            continue
        raise ValueError(f"unsafe module entrypoint statement: {path}")
    if not has_star_import and not delegated:
        raise ValueError(f"module entrypoint has no delegated callable: {path}")
    normalized = ast.Module(body=body, type_ignores=[])
    digest = hashlib.sha256(ast.dump(normalized, include_attributes=False).encode("utf-8")).hexdigest()
    contract: list[str] = []
    for node in ast.walk(normalized):
        if isinstance(node, ast.ImportFrom) and node.level == 1:
            contract.append(ast.dump(node, include_attributes=False))
        elif isinstance(node, ast.If):
            contract.append(ast.dump(node, include_attributes=False))
        elif isinstance(node, ast.Call):
            contract.append(ast.dump(node, include_attributes=False))
    risks = _namespace_risk_signatures(normalized)
    return True, digest, tuple(sorted(set(contract))), risks

def _initializer_bound_names(node: ast.AST) -> tuple[str, ...]:
    """Internal helper for _initializer_bound_names."""
    targets: list[ast.AST] = []
    if isinstance(node, ast.Assign):
        targets.extend(node.targets)
    elif isinstance(node, ast.AnnAssign):
        targets.append(node.target)
    names: list[str] = []
    def collect(target: ast.AST) -> None:
        """Internal helper for collect."""
        if isinstance(target, ast.Name):
            names.append(target.id)
        elif isinstance(target, (ast.Tuple, ast.List)):
            for item in target.elts:
                collect(item)
    for target in targets:
        collect(target)
    return tuple(names)

def _literal_initializer_value(node: ast.AST) -> bool:
    """Internal helper for _literal_initializer_value."""
    if isinstance(node, ast.Constant):
        return True
    if isinstance(node, (ast.List, ast.Tuple, ast.Set)):
        return all(_literal_initializer_value(item) for item in node.elts)
    if isinstance(node, ast.Dict):
        return all(key is None or _literal_initializer_value(key) for key in node.keys) and all(
            _literal_initializer_value(value) for value in node.values
        )
    if isinstance(node, ast.UnaryOp) and isinstance(node.op, (ast.UAdd, ast.USub)):
        return _literal_initializer_value(node.operand)
    return False

def _literal_assignment_target_matches(target: ast.AST, value: ast.AST) -> bool:
    """Return whether a literal assignment target cannot fail unpacking."""
    if isinstance(target, ast.Name):
        return True
    if not isinstance(target, (ast.Tuple, ast.List)) or not isinstance(value, (ast.Tuple, ast.List)):
        return False
    return len(target.elts) == len(value.elts) and all(
        _literal_assignment_target_matches(target_item, value_item)
        for target_item, value_item in zip(target.elts, value.elts)
    )

def _is_docstring_statement(statement: str) -> bool:
    """Internal helper for _is_docstring_statement."""
    try:
        parsed = ast.parse(statement)
    except SyntaxError:
        return False
    return (
        len(parsed.body) == 1
        and isinstance(parsed.body[0], ast.Expr)
        and isinstance(parsed.body[0].value, ast.Constant)
        and isinstance(parsed.body[0].value.value, str)
    )

def _statement_bound_names(statement: str) -> frozenset[str]:
    """Internal helper for _statement_bound_names."""
    try:
        body = ast.parse(statement).body
    except SyntaxError:
        return frozenset()
    if len(body) != 1:
        return frozenset()

    def collect(node: ast.stmt) -> set[str]:
        """Internal helper for collect."""
        if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef, ast.ClassDef)):
            return {node.name}
        if isinstance(node, ast.Import):
            return {alias.asname or alias.name.split(".", 1)[0] for alias in node.names}
        if isinstance(node, ast.ImportFrom):
            if node.module == "__future__":
                return set()
            return {alias.asname or alias.name for alias in node.names if alias.name != "*"}
        if isinstance(node, (ast.Assign, ast.AnnAssign)):
            return set(_initializer_bound_names(node))
        if isinstance(node, ast.If):
            return set().union(*(collect(item) for item in [*node.body, *node.orelse]))
        return set()

    return frozenset(collect(body[0]))

def _statement_loaded_names(statement: str) -> frozenset[str]:
    """Internal helper for _statement_loaded_names."""
    try:
        tree = ast.parse(statement)
    except SyntaxError:
        return frozenset()
    return frozenset(
        node.id for node in ast.walk(tree)
        if isinstance(node, ast.Name) and isinstance(node.ctx, ast.Load)
    )

def _safe_definition_expression(node: ast.AST | None) -> bool:
    """Internal helper for _safe_definition_expression."""
    if node is None:
        return True
    return not any(
        isinstance(item, (ast.Call, ast.Lambda, ast.NamedExpr, ast.Await, ast.Yield, ast.YieldFrom))
        for item in ast.walk(node)
    )

def _loaded_names(node: ast.AST | None) -> frozenset[str]:
    """Internal helper for _loaded_names."""
    if node is None:
        return frozenset()
    return frozenset(
        item.id for item in ast.walk(node)
        if isinstance(item, ast.Name) and isinstance(item.ctx, ast.Load)
    )

def _safe_runtime_expression(
    node: ast.AST | None,
    available_names: frozenset[str],
) -> bool:
    """Allow only literal or already-bound expressions evaluated at import time."""
    if node is None:
        return True
    if isinstance(node, ast.Constant):
        return True
    if isinstance(node, ast.Name):
        return node.id in available_names
    if isinstance(node, (ast.Tuple, ast.List)):
        return all(_safe_runtime_expression(item, available_names) for item in node.elts)
    if isinstance(node, ast.Set):
        return all(_literal_initializer_value(item) for item in node.elts)
    if isinstance(node, ast.Dict):
        return all(
            key is not None
            and _literal_initializer_value(key)
            and _safe_runtime_expression(value, available_names)
            for key, value in zip(node.keys, node.values)
        )
    if isinstance(node, ast.UnaryOp) and isinstance(node.op, (ast.UAdd, ast.USub)):
        return _literal_initializer_value(node.operand)
    return False

def _safe_annotation_expression(
    node: ast.AST | None,
    available_names: frozenset[str],
    *,
    postponed_annotations: bool = False,
) -> bool:
    """Allow non-executing, statically bound annotation expressions."""
    if node is None:
        return True
    if not _safe_definition_expression(node):
        return False
    if postponed_annotations:
        return True
    if isinstance(node, ast.Constant):
        return True
    if isinstance(node, ast.Name):
        return node.id in available_names
    return False

def _safe_function_definition(
    node: ast.FunctionDef | ast.AsyncFunctionDef,
    available_names: frozenset[str] = frozenset(),
    *,
    postponed_annotations: bool = False,
) -> bool:
    """Internal helper for _safe_function_definition."""
    annotations = [
        node.returns,
        *(
            argument.annotation
            for argument in [
                *node.args.posonlyargs, *node.args.args, *node.args.kwonlyargs,
                *([node.args.vararg] if node.args.vararg else []),
                *([node.args.kwarg] if node.args.kwarg else []),
            ]
        ),
    ]
    defaults = [
        *node.args.defaults,
        *[value for value in node.args.kw_defaults if value is not None],
    ]
    if node.decorator_list:
        return False
    if any(
        not _safe_annotation_expression(
            value,
            available_names,
            postponed_annotations=postponed_annotations,
        )
        for value in annotations
    ) or any(not _safe_runtime_expression(value, available_names) for value in defaults):
        return False
    runtime_names = set().union(*(_loaded_names(value) for value in defaults)) if defaults else set()
    annotation_names = set().union(*(_loaded_names(value) for value in annotations)) if annotations else set()
    return runtime_names <= available_names and (postponed_annotations or annotation_names <= available_names)

def _safe_class_definition(
    node: ast.ClassDef,
    available_names: frozenset[str] = frozenset(),
    *,
    postponed_annotations: bool = False,
) -> bool:
    """Internal helper for _safe_class_definition."""
    if node.decorator_list or node.keywords:
        return False
    if any(
        not isinstance(base, ast.Name) or base.id not in available_names
        for base in node.bases
    ):
        return False
    base_names = set().union(*(_loaded_names(base) for base in node.bases)) if node.bases else set()
    if not base_names <= available_names:
        return False
    for statement in node.body:
        if isinstance(statement, ast.Expr) and isinstance(statement.value, ast.Constant) and isinstance(statement.value.value, str):
            continue
        if isinstance(statement, ast.Pass):
            continue
        if isinstance(statement, (ast.FunctionDef, ast.AsyncFunctionDef)) and _safe_function_definition(
            statement, available_names, postponed_annotations=postponed_annotations
        ):
            continue
        if isinstance(statement, (ast.Assign, ast.AnnAssign)):
            value = statement.value
            if value is None or not _literal_initializer_value(value):
                return False
            targets = list(statement.targets) if isinstance(statement, ast.Assign) else [statement.target]
            if any(not _literal_assignment_target_matches(target, value) for target in targets):
                return False
            if isinstance(statement, ast.AnnAssign):
                if not _safe_annotation_expression(
                    statement.annotation,
                    available_names,
                    postponed_annotations=postponed_annotations,
                ):
                    return False
                if not postponed_annotations and not _loaded_names(statement.annotation) <= available_names:
                    return False
            continue
        return False
    return True

_SAFE_IMPORT_ROOTS = frozenset({
    "abc", "argparse", "array", "ast", "asyncio", "base64", "collections",
    "contextlib", "copy", "csv", "dataclasses", "datetime", "enum", "functools",
    "hashlib", "importlib", "inspect", "io", "itertools", "json", "logging",
    "math", "ntpath", "operator", "os", "pathlib", "platform", "re", "shlex",
    "socket", "ssl", "string", "subprocess", "sys", "tempfile", "textwrap",
    "threading", "time", "types", "typing", "urllib", "uuid", "warnings", "weakref", "xml",
})
_SAFE_BUILTIN_NAMES = frozenset({
    "BaseException", "Exception", "False", "None", "NotImplemented", "True",
    "ValueError", "TypeError", "RuntimeError", "bool", "bytes", "bytearray",
    "classmethod", "complex", "dict", "enumerate", "float", "frozenset",
    "int", "len", "list", "map", "max", "min", "object", "property",
    "range", "set", "staticmethod", "str", "super", "tuple", "type", "zip",
})

def _safe_inert_initializer_condition(node: ast.AST) -> bool:
    """Internal helper for _safe_inert_initializer_condition."""
    return (
        isinstance(node, ast.Constant)
        and node.value is False
    ) or (
        isinstance(node, ast.Name)
        and node.id == "TYPE_CHECKING"
    ) or (
        isinstance(node, ast.Attribute)
        and node.attr == "TYPE_CHECKING"
        and isinstance(node.value, ast.Name)
        and node.value.id == "typing"
    )

@lru_cache(maxsize=256)
def _module_spec_exists(module_name: str) -> bool:
    """Internal helper for _module_spec_exists."""
    try:
        return importlib.util.find_spec(module_name) is not None
    except (ImportError, ModuleNotFoundError, AttributeError, ValueError):
        return False

def _absolute_import_resolves(node: ast.Import | ast.ImportFrom) -> bool:
    """Internal helper for _absolute_import_resolves."""
    if isinstance(node, ast.Import):
        return bool(node.names) and all(
            alias.name.split(".", 1)[0] in _SAFE_IMPORT_ROOTS
            and _module_spec_exists(alias.name)
            for alias in node.names
        )
    if node.level or not node.module or not node.names:
        return False
    root = node.module.split(".", 1)[0]
    if root not in _SAFE_IMPORT_ROOTS or not _module_spec_exists(node.module):
        return False
    try:
        module = importlib.import_module(node.module)
    except (ImportError, ModuleNotFoundError, AttributeError, ValueError):
        return False
    for alias in node.names:
        if alias.name == "*" or alias.name.startswith("__"):
            return False
        if hasattr(module, alias.name):
            continue
        if not _module_spec_exists(f"{node.module}.{alias.name}"):
            return False
    return True

def _section_alias_bindings(
    statements: tuple[str, ...] | list[str],
    allowed_section_imports: frozenset[str],
) -> dict[str, str]:
    """Internal helper for _section_alias_bindings."""
    bindings: dict[str, str] = {}
    for statement in statements:
        try:
            tree = ast.parse(statement)
        except SyntaxError:
            continue
        if len(tree.body) != 1 or not isinstance(tree.body[0], ast.ImportFrom):
            continue
        node = tree.body[0]
        if node.level != 1 or node.module is not None:
            continue
        for alias in node.names:
            if alias.name in allowed_section_imports and alias.asname:
                bindings[alias.asname] = alias.name
    return bindings

def _definitely_bound_names(statement: str) -> frozenset[str]:
    """Internal helper for _definitely_bound_names."""
    try:
        tree = ast.parse(statement)
    except SyntaxError:
        return frozenset()
    if len(tree.body) != 1:
        return frozenset()
    node = tree.body[0]
    if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef, ast.ClassDef, ast.Import, ast.ImportFrom, ast.Assign, ast.AnnAssign, ast.AugAssign)):
        return frozenset(_statement_bound_names(statement))
    if isinstance(node, ast.If):
        if not node.orelse:
            return frozenset()
        def block(statements: list[ast.stmt]) -> set[str]:
            """Internal helper for block."""
            result: set[str] = set()
            for item in statements:
                text = ast.unparse(item)
                result.update(_definitely_bound_names(text))
            return result
        return frozenset(block(node.body) & block(node.orelse))
    return frozenset()

def _literal_truth_value(node: ast.AST) -> bool | None:
    """Internal helper for _literal_truth_value."""
    try:
        value = ast.literal_eval(node)
    except (TypeError, ValueError, SyntaxError):
        return None
    return bool(value)

def _statement_deleted_names(statement: str) -> frozenset[str]:
    """Internal helper for _statement_deleted_names."""
    try:
        tree = ast.parse(statement)
    except SyntaxError:
        return frozenset()
    if len(tree.body) != 1 or not isinstance(tree.body[0], ast.Delete):
        return frozenset()
    names: set[str] = set()
    def collect(target: ast.AST) -> None:
        """Internal helper for collect."""
        if isinstance(target, ast.Name):
            names.add(target.id)
        elif isinstance(target, (ast.Tuple, ast.List)):
            for item in target.elts:
                collect(item)
    for target in tree.body[0].targets:
        collect(target)
    return frozenset(names)

def _safe_initializer_addition(
    statement: str,
    allowed_section_imports: frozenset[str] = frozenset(),
    forbidden_names: frozenset[str] = frozenset(),
    available_names: frozenset[str] = _SAFE_BUILTIN_NAMES,
    *,
    postponed_annotations: bool = False,
    section_exports: Mapping[str, frozenset[str]] | None = None,
) -> bool:
    """Internal helper for _safe_initializer_addition."""
    try:
        parsed = ast.parse(statement)
    except SyntaxError:
        return False
    if len(parsed.body) != 1:
        return False
    node = parsed.body[0]
    bound = _statement_bound_names(statement)
    if not (isinstance(node, ast.ImportFrom) and node.level) and bound & forbidden_names:
        return False
    if isinstance(node, ast.Expr):
        return isinstance(node.value, ast.Constant) and isinstance(node.value.value, str)
    if isinstance(node, ast.Pass):
        return True
    if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef)):
        return bool(bound) and all(
            not name.startswith("__") and not name.startswith("_PHASE192_") for name in bound
        ) and _safe_function_definition(
        node, available_names, postponed_annotations=postponed_annotations
    )
    if isinstance(node, ast.ClassDef):
        return False
    if isinstance(node, ast.Import):
        return bool(node.names) and _absolute_import_resolves(node) and all(
            alias.name.split(".", 1)[0] not in {"builtins", "__builtins__", "__builtin__"}
            and alias.name.split(".", 1)[0] not in {"exec", "eval", "compile"}
            and not alias.name.startswith("__")
            and not (alias.asname or "").startswith("__")
            for alias in node.names
        )
    if isinstance(node, ast.ImportFrom):
        if node.module == "__future__":
            return False
        if not node.level:
            root = (node.module or "").split(".", 1)[0]
            return bool(node.names) and _absolute_import_resolves(node) and all(
                alias.name != "*"
                and not alias.name.startswith("__")
                and not (alias.asname or "").startswith("__")
                and root not in {"builtins", "__builtins__", "__builtin__", "exec", "eval", "compile"}
                for alias in node.names
            )
        section_exports = section_exports or {}
        if node.module is None:
            valid = bool(node.names) and all(
                alias.name in allowed_section_imports
                and alias.name != "*"
                and (alias.asname is None or not alias.asname.startswith("__"))
                for alias in node.names
            )
            if not valid:
                return False
            legitimate = {
                alias.name
                for alias in node.names
                if alias.asname is None or alias.asname == alias.name
            }
            return not ((bound - legitimate) & forbidden_names)
        section_name = node.module.split(".", 1)[0]
        exports = section_exports.get(section_name)
        valid = bool(node.names) and exports is not None and all(
            alias.name in exports
            and alias.name != "*"
            and not alias.name.startswith("__")
            and not (alias.asname or "").startswith("__")
            for alias in node.names
        )
        if not valid:
            return False
        legitimate = {
            alias.name
            for alias in node.names
            if alias.asname is None or alias.asname == alias.name
        }
        return not ((bound - legitimate) & forbidden_names)
    if isinstance(node, ast.If):
        if (
            node.orelse
            or not _safe_inert_initializer_condition(node.test)
            or not _loaded_names(node.test) <= available_names
        ):
            return False
        return all(
            _safe_initializer_addition(
                ast.unparse(item),
                allowed_section_imports,
                forbidden_names,
                available_names,
                postponed_annotations=postponed_annotations,
                section_exports=section_exports,
            )
            for item in node.body
        )
    if isinstance(node, ast.Assert):
        return (
            _literal_truth_value(node.test) is True
            and (node.msg is None or _literal_initializer_value(node.msg))
        )
    if isinstance(node, ast.Assign):
        return bool(bound) and all(
            not name.startswith("__") and not name.startswith("_PHASE192_") for name in bound
        ) and _literal_initializer_value(node.value) and all(
            _literal_assignment_target_matches(target, node.value)
            for target in node.targets
        )
    if isinstance(node, ast.AnnAssign):
        annotation_names = _loaded_names(node.annotation)
        return bool(bound) and all(
            not name.startswith("__") and not name.startswith("_PHASE192_") for name in bound
        ) and _safe_annotation_expression(
            node.annotation,
            available_names,
            postponed_annotations=postponed_annotations,
        ) and (
            postponed_annotations or annotation_names <= available_names
        ) and node.value is not None and _literal_initializer_value(node.value) and isinstance(node.target, ast.Name)
    return False

def _section_tuple_names(statement: str) -> tuple[str, ...] | None:
    """Internal helper for _section_tuple_names."""
    try:
        tree = ast.parse(statement)
    except SyntaxError:
        return None
    if len(tree.body) != 1 or not isinstance(tree.body[0], ast.Assign):
        return None
    assignment = tree.body[0]
    if not any(isinstance(target, ast.Name) and target.id == "_PHASE192_SECTION_MODULES" for target in assignment.targets):
        return None
    value = assignment.value
    if not isinstance(value, (ast.Tuple, ast.List)):
        return None
    names = tuple(item.id for item in value.elts if isinstance(item, ast.Name))
    if len(names) != len(value.elts) or any(not name.startswith("_phase192_section_") for name in names):
        return None
    return names


def _initializer_statements_match(
    base_statement: str,
    head_statement: str,
    preceding_head: tuple[str, ...] = (),
    allowed_section_imports: frozenset[str] = frozenset(),
) -> bool:
    """Internal helper for _initializer_statements_match."""
    if base_statement == head_statement:
        return True
    base_sections = _section_tuple_names(base_statement)
    head_sections = _section_tuple_names(head_statement)
    if base_sections is None or head_sections is None or len(set(head_sections)) != len(head_sections):
        return False
    bindings = _section_alias_bindings(preceding_head, allowed_section_imports)
    if any(name not in bindings or bindings[name] not in allowed_section_imports for name in head_sections):
        return False
    cursor = 0
    for name in base_sections:
        try:
            cursor = head_sections.index(name, cursor) + 1
        except ValueError:
            return False
    return True


def _initializer_is_additive(
    base_initializer: tuple[str, ...],
    head_initializer: tuple[str, ...],
    allowed_section_imports: frozenset[str] = frozenset(),
    forbidden_external_names: frozenset[str] = frozenset(),
    section_exports: Mapping[str, frozenset[str]] | None = None,
) -> bool:
    """Internal helper for _initializer_is_additive."""
    base = list(base_initializer)
    head = list(head_initializer)
    base_has_docstring = bool(base) and _is_docstring_statement(base[0])
    head_has_docstring = bool(head) and _is_docstring_statement(head[0])
    if base_has_docstring != head_has_docstring:
        return False
    if base_has_docstring and head_has_docstring:
        base = base[1:]
        head = head[1:]
    matched_indexes: set[int] = set()
    matched_by_head: dict[int, str] = {}
    cursor = 0
    for statement in base:
        index = next(
            (
                candidate_index
                for candidate_index in range(cursor, len(head))
                if _initializer_statements_match(
                     statement,
                     head[candidate_index],
                     tuple(head[:candidate_index]),
                     allowed_section_imports,
                 )
            ),
            None,
        )
        if index is None:
            return False
        matched_indexes.add(index)
        matched_by_head[index] = statement
        cursor = index + 1
    forbidden_names = frozenset().union(
        forbidden_external_names,
        *(
            set(_statement_bound_names(statement)) | set(_statement_loaded_names(statement))
            for statement in base
        ),
    )
    available_names = set(_SAFE_BUILTIN_NAMES)
    postponed_annotations = "from __future__ import annotations" in base
    seen_additions: set[str] = set()
    for index, statement in enumerate(head):
        bound = _statement_bound_names(statement)
        if index in matched_by_head:
            available_names.difference_update(_statement_deleted_names(statement))
            available_names.update(_definitely_bound_names(statement))
            available_names.update(_statement_loaded_names(statement))
            continue
        if bound & seen_additions:
            return False
        if not _safe_initializer_addition(
            statement,
            allowed_section_imports,
            forbidden_names,
            frozenset(available_names),
            postponed_annotations=postponed_annotations,
            section_exports=section_exports,
        ):
            return False
        seen_additions.update(bound)
        available_names.update(bound)
    return True

def _sections_preserve_order(base: tuple[str, ...], head: tuple[str, ...]) -> bool:
    """Internal helper for _sections_preserve_order."""
    if len(set(base)) != len(base) or len(set(head)) != len(head):
        return False
    index = 0
    for section in head:
        if index < len(base) and section == base[index]:
            index += 1
    return index == len(base)

def _contract_preserves_order(base: tuple[str, ...], head: tuple[str, ...]) -> bool:
    """Return whether every baseline contract entry remains in order."""
    cursor = 0
    for entry in head:
        if cursor < len(base) and entry == base[cursor]:
            cursor += 1
    return cursor == len(base)

def _validate_import_time_section(
    source: str,
    path: Path,
    package_dir: Path,
    new_sections: frozenset[str],
    seen: set[Path] | None = None,
) -> None:
    """Reject import-time control flow and side effects in an additive section."""
    seen = set() if seen is None else seen
    path = path.resolve()
    if path in seen:
        return
    seen.add(path)
    tree = _parse_source(source, path)
    postponed_annotations = any(
        isinstance(node, ast.ImportFrom)
        and node.module == "__future__"
        and not node.level
        and any(alias.name == "annotations" for alias in node.names)
        for node in tree.body
    )
    available_names: set[str] = set(_SAFE_BUILTIN_NAMES) | {
        "__doc__", "__name__", "__package__", "__spec__"
    }

    def bound_names(node: ast.stmt) -> set[str]:
        """Return simple names introduced by an import or literal definition."""
        if isinstance(node, ast.Import):
            return {alias.asname or alias.name.split(".", 1)[0] for alias in node.names}
        if isinstance(node, ast.ImportFrom):
            return {
                alias.asname or alias.name
                for alias in node.names
                if alias.name != "*" and node.module != "__future__"
            }
        if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef, ast.ClassDef)):
            return {node.name}
        if isinstance(node, ast.Assign):
            return set().union(*(target_names(target) for target in node.targets))
        if isinstance(node, ast.AnnAssign):
            return target_names(node.target)
        return set()

    def target_names(target: ast.AST) -> set[str]:
        """Return names for a literal assignment target."""
        if isinstance(target, ast.Name):
            return {target.id}
        if isinstance(target, (ast.Tuple, ast.List)):
            return set().union(*(target_names(item) for item in target.elts))
        return set()

    def relative_target(name: str) -> Path:
        """Resolve a package-relative section target without executing it."""
        parts = name.split(".")
        if not parts or any(_SECTION_NAME_RE.fullmatch(part) is None for part in parts):
            raise ValueError(f"Unsafe relative import in {path}: {name}")
        target = package_dir.joinpath(*parts).with_suffix(".py")
        if not target.is_file():
            target = package_dir.joinpath(*parts) / "__init__.py"
        if not target.is_file():
            raise ValueError(f"Missing relative import target in {path}: {name}")
        return target

    def validate_block(
        statements: list[ast.stmt],
        names: set[str],
        *,
        inert: bool = False,
        module_level: bool = True,
    ) -> None:
        """Validate a restricted import-time statement block."""
        for index, node in enumerate(statements):
            if (
                index == 0
                and isinstance(node, ast.Expr)
                and isinstance(node.value, ast.Constant)
                and isinstance(node.value.value, str)
            ):
                continue
            if isinstance(node, ast.Expr):
                raise ValueError(f"Unsafe import-time expression in {path}")
            if isinstance(node, ast.Pass):
                continue
            if isinstance(node, ast.Import):
                if not node.names or not _absolute_import_resolves(node):
                    raise ValueError(f"Unsafe import-time import in {path}")
                names.update(bound_names(node))
                continue
            if isinstance(node, ast.ImportFrom):
                if node.module == "__future__":
                    if node.level or any(alias.name != "annotations" for alias in node.names):
                        raise ValueError(f"Unsafe future import in {path}")
                    continue
                if not node.level:
                    if not node.names or not _absolute_import_resolves(node):
                        raise ValueError(f"Unsafe import-time import in {path}")
                    names.update(bound_names(node))
                    continue
                if node.level != 1:
                    raise ValueError(f"Unsafe parent-relative import in {path}")
                imported_sections = [node.module] if node.module else [
                    alias.name for alias in node.names if alias.name != "*"
                ]
                for imported in imported_sections:
                    target = relative_target(imported)
                    if imported.split(".", 1)[0] in new_sections:
                        _validate_import_time_section(
                            target.read_text(encoding="utf-8"),
                            target,
                            package_dir,
                            new_sections,
                            seen,
                        )
                names.update(bound_names(node))
                continue
            if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef)):
                if not _safe_function_definition(
                    node,
                    frozenset(names),
                    postponed_annotations=postponed_annotations or inert,
                ):
                    raise ValueError(f"Unsafe import-time function definition in {path}")
                names.add(node.name)
                continue
            if isinstance(node, ast.ClassDef):
                raise ValueError(f"Unsafe import-time class definition in {path}")
            if isinstance(node, ast.If):
                if (
                    node.orelse
                    or not _safe_inert_initializer_condition(node.test)
                    or not _loaded_names(node.test) <= frozenset(names)
                ):
                    raise ValueError(f"Unsafe import-time conditional in {path}")
                validate_block(node.body, set(names), inert=True, module_level=False)
                continue
            if isinstance(node, (ast.Assign, ast.AnnAssign)):
                targets = list(node.targets) if isinstance(node, ast.Assign) else [node.target]
                if any(
                    not _literal_assignment_target_matches(target, node.value)
                    for target in targets
                ):
                    raise ValueError(f"Unsafe import-time assignment target in {path}")
                if any(
                    isinstance(target, ast.Name)
                    and target.id == "__all__"
                    and _is_public_globals_comprehension(node.value)
                    for target in targets
                ):
                    names.update(bound_names(node))
                    continue
                if node.value is None or not _literal_initializer_value(node.value):
                    raise ValueError(f"Non-literal import-time assignment in {path}")
                if isinstance(node, ast.AnnAssign):
                    if not _safe_annotation_expression(
                        node.annotation,
                        frozenset(names),
                        postponed_annotations=postponed_annotations or inert,
                    ):
                        raise ValueError(f"Unsafe import-time annotation in {path}")
                    if not (postponed_annotations or inert) and not _loaded_names(node.annotation) <= frozenset(names):
                        raise ValueError(f"Unbound import-time annotation in {path}")
                names.update(bound_names(node))
                continue
            raise ValueError(f"Unsafe import-time statement in {path}: {type(node).__name__}")

    validate_block(tree.body, available_names)

def _validate_compatibility_new_sections(
    base_root: Path,
    head_root: Path,
    base_surfaces: Mapping[str, tuple],
    head_surfaces: Mapping[str, tuple],
) -> None:
    """Validate every section newly imported by compatibility mode."""
    base_section_modules = _section_modules_for_root(base_root)
    head_section_modules = _section_modules_for_root(head_root)
    for relative in sorted(head_surfaces):
        head_facade = head_root / relative
        head_package = head_facade.with_suffix("")
        head_sections = head_section_modules(head_package)
        base_package = (base_root / relative).with_suffix("")
        base_sections = (
            base_section_modules(base_package)
            if relative in base_surfaces and base_package.is_dir()
            else ()
        )
        old_names = frozenset(path.stem for path in base_sections)
        new_names = frozenset(path.stem for path in head_sections) - old_names
        if relative not in base_surfaces:
            initializer = head_package / "__init__.py"
            _validate_import_time_section(
                initializer.read_text(encoding="utf-8"),
                initializer,
                head_package,
                frozenset(path.stem for path in head_sections),
            )
        for section in head_sections:
            if section.stem not in new_names:
                continue
            _validate_import_time_section(
                section.read_text(encoding="utf-8"),
                section,
                head_package,
                new_names,
            )

def _surface(
    root: Path,
    facade: Path,
    section_modules=None,
    *,
    reject_conditional: bool = True,
) -> tuple:
    """Internal helper for _surface."""
    relative = facade.relative_to(root).as_posix()
    package = facade.with_suffix("")
    section_modules = section_modules or _section_modules_for_root(root)
    sections = section_modules(package)
    section_exports: set[str] = set()
    section_export_map: list[tuple[str, frozenset[str]]] = []
    owner_map: dict[str, set[str]] = {}
    conditional_names: set[str] = set()
    risk_signatures: set[str] = set()
    conditional_contract: list[str] = []
    relative_star_contract: list[str] = []
    module_import_names: set[str] = set()

    def add_conditional_contract(signature: str) -> None:
        """Internal helper for add_conditional_contract."""
        conditional_contract.append(signature)

    for section in sections:
        section_source = section.read_text(encoding="utf-8")
        section_tree = _parse_source(section_source, section)
        module_import_names.update(_direct_import_bound_names(section_tree))
        relative_star_contract.extend(
            _relative_import_contract(
                section_tree,
                package,
                section,
                frozenset(path.stem for path in sections),
            )
        )
        exports = _section_exports(
            section_source,
            str(section),
            reject_conditional=reject_conditional,
        )
        section_exports.update(exports)
        for name in exports:
            owner_map.setdefault(name, set()).add(f"section:{section.stem}")
        section_export_map.append((section.stem, frozenset(exports)))
        risk_signatures.update(_namespace_risk_signatures(section_tree))
        for signature in _conditional_contract_signatures(section_tree):
            add_conditional_contract(f"section:{section.stem}:{signature}")
        conditional_names.update(
            _conditional_only_public_names(
                section_tree,
                include_private=True,
            )
        )
    facade_source = facade.read_text(encoding="utf-8")
    facade_tree = _parse_source(facade_source, facade)
    module_import_names.update(_direct_import_bound_names(facade_tree))
    relative_star_contract.extend(
        _relative_import_contract(
            facade_tree,
            facade.parent,
            facade,
            frozenset(path.stem for path in sections),
        )
    )
    facade_symbols = _symbols(
        facade_source,
        str(facade),
        reject_conditional=reject_conditional,
    )
    exported = set(section_exports)
    exported.update(facade_symbols)
    for name in facade_symbols:
        owner_map.setdefault(name, set()).add("facade")
    risk_signatures.update(_namespace_risk_signatures(facade_tree))
    for signature in _conditional_contract_signatures(facade_tree):
        add_conditional_contract(f"facade:{signature}")
    conditional_names.update(
        _conditional_only_public_names(
            facade_tree,
            include_private=True,
        )
    )
    initializer = package / "__init__.py"
    initializer_tree = _parse_source(initializer.read_text(encoding="utf-8"), initializer)
    module_import_names.update(_direct_import_bound_names(initializer_tree))
    _relative_import_contract(
        initializer_tree,
        package,
        initializer,
        frozenset(path.stem for path in sections),
    )
    entrypoint_present, entrypoint_digest, entrypoint_contract, entrypoint_risks = _entrypoint_contract(
        package / "__main__.py",
        frozenset(path.stem for path in sections),
        reject_conditional=reject_conditional,
    )
    risk_signatures.update(entrypoint_risks)
    _reject_unsupported_module_bindings(initializer_tree, str(initializer))
    risk_signatures.update(_namespace_risk_signatures(initializer_tree))
    for signature in _conditional_contract_signatures(initializer_tree):
        add_conditional_contract(f"initializer:{signature}")
    initializer_names = {
        name for name in _top_level_names(initializer_tree, include_imports=True)
        if not name.startswith("_")
    }
    exported.update(initializer_names)
    for name in initializer_names:
        owner_map.setdefault(name, set()).add("initializer")
    owner_map_tuple = tuple(
        (name, tuple(sorted(owners)))
        for name, owners in sorted(owner_map.items())
    )
    public_symbols = frozenset(
        name
        for name in exported
        if not name.startswith("_") and name not in module_import_names
    )
    return (
        frozenset(exported),
        tuple(path.stem for path in sections),
        _initializer_lines(initializer),
        frozenset(section_exports),
        _initializer_digest(initializer),
        frozenset(conditional_names),
        entrypoint_present,
        tuple(section_export_map),
        frozenset(risk_signatures),
        tuple(conditional_contract),
        entrypoint_digest,
        entrypoint_contract,
        owner_map_tuple,
        tuple(relative_star_contract),
        public_symbols,
    )

def _surfaces(
    root: Path,
    *,
    strict: bool = True,
) -> dict[str, tuple]:
    """Internal helper for _surfaces."""
    result: dict[str, tuple] = {}
    section_modules = _section_modules_for_root(root)
    for facade in _split_facades(root):
        relative = facade.relative_to(root).as_posix()
        package = facade.with_suffix("")
        listed_paths = section_modules(package)
        listed = {path.name for path in listed_paths}
        actual_paths = [
            path for path in package.glob("*.py")
            if path.name not in {"__init__.py", "__main__.py"}
        ]
        actual_by_casefold: dict[str, Path] = {}
        for path in actual_paths:
            key = path.stem.casefold()
            if key in actual_by_casefold:
                raise ValueError(
                    f"Duplicate source section file (case-insensitive): {actual_by_casefold[key]} / {path}"
                )
            actual_by_casefold[key] = path
        actual = {path.name for path in actual_paths}
        missing = sorted(actual - listed)
        extra = sorted(listed - actual)
        if extra or (strict and missing):
            raise ValueError(
                f"{relative}: exported sections differ from package files; "
                f"missing={', '.join(missing) or '-'} extra={', '.join(extra) or '-'}"
            )
        entrypoint = package / "__main__.py"
        if entrypoint.is_file():
            entrypoint_tree = _parse_source(entrypoint.read_text(encoding="utf-8"), entrypoint)
            if not any(
                isinstance(node, ast.ImportFrom) and node.level == 1
                for node in ast.walk(entrypoint_tree)
            ):
                raise ValueError(f"{relative}: module entrypoint does not import its package")
        result[relative] = _surface(
            root,
            facade,
            section_modules,
            reject_conditional=strict,
        )
    return result

def _conditional_signature_names(signature: str) -> set[str]:
    """Extract bound names from a scoped conditional signature."""
    if signature.startswith("section:"):
        payload = signature.split(":", 2)[-1]
    elif signature.startswith(("facade:", "initializer:")):
        payload = signature.split(":", 1)[-1]
    else:
        payload = signature
    name_text = payload.rsplit(":", 1)[0]
    return {name for name in name_text.split(",") if name}

def _compare_surfaces(
    base_surfaces,
    head_surfaces,
    *,
    strict: bool = True,
    waived_symbols: Mapping[str, frozenset[str]] | None = None,
) -> list[str]:
    """Compare identity surfaces using waivers from the trusted base revision."""
    errors: list[str] = []
    waiver_map = waived_symbols or {}
    unknown_waiver_paths = sorted(set(waiver_map) - set(base_surfaces))
    for relative in unknown_waiver_paths:
        errors.append(f"INVALID_IDENTITY_WAIVER {relative}: facade is not in the base surface")

    def unpack(surface):
        """Internal helper for unpack."""
        if len(surface) > 15:
            raise ValueError(f"unsupported identity surface shape: {len(surface)} fields")
        defaults = (
            frozenset(), (), (), frozenset(), None, frozenset(), None, (),
            frozenset(), (), None, (), (), (), frozenset(),
        )
        return tuple(surface) + defaults[len(surface):]

    for relative, base_surface in sorted(base_surfaces.items()):
        current = head_surfaces.get(relative)
        if current is None:
            errors.append(f"REMOVED_FACADE {relative}")
            continue
        (
            base_symbols,
            base_sections,
            base_initializer,
            base_section_exports,
            base_digest,
            base_conditional,
            base_entrypoint,
            base_section_map,
            base_risks,
            base_conditional_contract,
            base_entrypoint_digest,
            base_entrypoint_contract,
            base_owner_map,
            base_relative_star_contract,
            base_public_symbols,
        ) = unpack(base_surface)
        (
            head_symbols,
            head_sections,
            head_initializer,
            head_section_exports,
            head_digest,
            head_conditional,
            head_entrypoint,
            head_section_map,
            head_risks,
            head_conditional_contract,
            head_entrypoint_digest,
            head_entrypoint_contract,
            head_owner_map,
            head_relative_star_contract,
            head_public_symbols,
        ) = unpack(current)
        if not base_public_symbols and base_symbols:
            base_public_symbols = frozenset(
                name for name in base_symbols if not name.startswith("_")
            )
        if not head_public_symbols and head_symbols:
            head_public_symbols = frozenset(
                name for name in head_symbols if not name.startswith("_")
            )
        missing_sections = sorted(set(base_sections) - set(head_sections))
        extra_sections = sorted(set(head_sections) - set(base_sections))
        if strict:
            missing_symbols = sorted(set(base_symbols) - set(head_symbols))
        else:
            public_removals = set(base_public_symbols) - set(head_public_symbols)
            waiver = set(waiver_map.get(relative, frozenset()))
            invalid_waivers = sorted(waiver - set(base_public_symbols))
            if invalid_waivers:
                errors.append(
                    f"INVALID_IDENTITY_WAIVER {relative}: {', '.join(invalid_waivers)}"
                )
            missing_symbols = sorted(public_removals - waiver)
        extra_symbols = sorted(set(head_symbols) - set(base_symbols))
        if strict and base_risks != head_risks:
            errors.append(f"NAMESPACE_CONTRACT_CHANGED {relative}")
        if strict and tuple(base_conditional_contract or ()) != tuple(head_conditional_contract or ()):
            errors.append(f"CONDITIONAL_CONTRACT_CHANGED {relative}")
        if strict and tuple(base_section_map or ()) != tuple(head_section_map or ()):
            errors.append(f"SECTION_EXPORT_OWNER_CHANGED {relative}")
        if strict and tuple(base_owner_map or ()) != tuple(head_owner_map or ()):
            errors.append(f"SYMBOL_OWNER_CHANGED {relative}")
        base_relative = tuple(base_relative_star_contract or ())
        head_relative = tuple(head_relative_star_contract or ())
        if (
            (strict and base_relative != head_relative)
            or (not strict and not _contract_preserves_order(base_relative, head_relative))
        ):
            errors.append(f"RELATIVE_STAR_IMPORT_CHANGED {relative}")
        if not strict and base_risks != head_risks:
            errors.append(f"NAMESPACE_CONTRACT_CHANGED {relative}")
        if not strict:
            base_contract = tuple(base_conditional_contract or ())
            head_contract = tuple(head_conditional_contract or ())
            matched_head_indexes: set[int] = set()
            cursor = 0
            preserves_order = True
            for signature in base_contract:
                try:
                    index = head_contract.index(signature, cursor)
                except ValueError:
                    preserves_order = False
                    break
                matched_head_indexes.add(index)
                cursor = index + 1
            added_contract = [
                signature for index, signature in enumerate(head_contract)
                if index not in matched_head_indexes
            ]
            existing_conditional_names = set(base_symbols) | set(base_conditional)
            added_existing_bindings = {
                signature for signature in added_contract
                if any(
                    name in existing_conditional_names
                    for name in _conditional_signature_names(signature)
                )
            }
            if not preserves_order or added_existing_bindings:
                errors.append(f"CONDITIONAL_CONTRACT_CHANGED {relative}")
        if base_entrypoint and head_entrypoint:
            if strict and base_entrypoint_digest != head_entrypoint_digest:
                errors.append(f"MODULE_ENTRYPOINT_CHANGED {relative}")
            elif (
                not strict

                and (
                    (
                        base_entrypoint_digest is not None
                        and head_entrypoint_digest is not None
                        and base_entrypoint_digest != head_entrypoint_digest
                    )
                    or (
                        (base_entrypoint_digest is None or head_entrypoint_digest is None)
                        and tuple(base_entrypoint_contract or ()) != tuple(head_entrypoint_contract or ())
                    )
                )
            ):
                errors.append(f"MODULE_ENTRYPOINT_CHANGED {relative}")
        if not strict and base_owner_map and head_owner_map:
            base_owners = dict(base_owner_map)
            head_owners = dict(head_owner_map)
            owner_changes = sorted(
                name for name in set(base_owners) & set(head_owners)
                if tuple(base_owners[name]) != tuple(head_owners[name])
            )
            new_collisions = sorted(
                name for name, owners in head_owners.items()
                if len(owners) > 1 and name not in base_owners
            )
            if owner_changes:
                errors.append(f"SYMBOL_OWNER_CHANGED {relative}: {','.join(owner_changes)}")
            if new_collisions:
                errors.append(f"SYMBOL_OWNER_COLLISION {relative}: {','.join(new_collisions)}")
        if not strict and base_section_map and head_section_map:
            base_exports_by_section = dict(base_section_map)
            head_exports_by_section = dict(head_section_map)
            def duplicate_exports(exports_by_section):
                """Internal helper for duplicate_exports."""
                owners: dict[str, set[str]] = {}
                for section_name, exports in exports_by_section.items():
                    for name in exports:
                        owners.setdefault(name, set()).add(section_name)
                return {name for name, sections_for_name in owners.items() if len(sections_for_name) > 1}
            new_collisions = duplicate_exports(head_exports_by_section) - duplicate_exports(base_exports_by_section)
            base_owners: dict[str, set[str]] = {}
            head_owners: dict[str, set[str]] = {}
            for section_name, exports in base_exports_by_section.items():
                for name in exports:
                    base_owners.setdefault(name, set()).add(section_name)
            for section_name, exports in head_exports_by_section.items():
                for name in exports:
                    head_owners.setdefault(name, set()).add(section_name)
            owner_changes = sorted(
                name for name in set(base_owners) & set(head_owners)
                if base_owners[name] != head_owners[name]
            )
            base_section_exports_all = set().union(*base_exports_by_section.values()) if base_exports_by_section else set()
            new_sections = set(head_exports_by_section) - set(base_exports_by_section)
            new_section_collisions = {
                f"{section_name}:{name}"
                for section_name in new_sections
                for name in head_exports_by_section[section_name]
                if name in base_section_exports_all or name in base_symbols
            }
            if new_collisions or new_section_collisions:
                details = sorted(new_collisions) + sorted(new_section_collisions)
                errors.append(f"SECTION_EXPORT_COLLISION {relative}: {','.join(details)}")
            if owner_changes:
                errors.append(f"SECTION_EXPORT_OWNER_CHANGED {relative}: {','.join(owner_changes)}")
        if strict:
            initializer_ok = (
                base_digest == head_digest
                if base_digest is not None and head_digest is not None
                else base_initializer == head_initializer
            )
        else:
            if base_owner_map:
                base_external_names = {
                    name
                    for name, owners in base_owner_map
                    if any(owner != "initializer" for owner in owners)
                }
            else:
                base_external_names = set(base_symbols)
            initializer_ok = _initializer_is_additive(
                base_initializer,
                head_initializer,
                frozenset(head_sections),
                frozenset(base_external_names)
                | (set(head_section_exports) - set(base_section_exports)),
                dict(head_section_map or ()),
            )
        if not initializer_ok:
            errors.append(f"PACKAGE_INITIALIZER_CHANGED {relative}")
        if not _sections_preserve_order(base_sections, head_sections):
            errors.append(f"SECTION_ORDER_CHANGED {relative}")
        if missing_sections:
            errors.append(f"MISSING_SECTIONS {relative}: {','.join(missing_sections)}")
        if strict and extra_sections:
            errors.append(f"EXTRA_SECTIONS {relative}: {','.join(extra_sections)}")
        if missing_symbols:
            errors.append(f"MISSING_SYMBOLS {relative}: {','.join(missing_symbols)}")
        conditional_changed = sorted(
            (set(base_symbols) - set(base_conditional)) & set(head_conditional)
        )
        if conditional_changed:
            errors.append(
                f"CONDITIONAL_SYMBOL_CHANGED {relative}: {','.join(conditional_changed)}"
            )
        if base_entrypoint and not head_entrypoint:
            errors.append(f"MISSING_MODULE_ENTRYPOINT {relative}")
        if strict and not base_entrypoint and head_entrypoint:
            errors.append(f"ADDED_MODULE_ENTRYPOINT {relative}")
        if strict and extra_symbols:
            errors.append(f"EXTRA_SYMBOLS {relative}: {','.join(extra_symbols)}")
    if strict:
        for relative in sorted(set(head_surfaces) - set(base_surfaces)):
            errors.append(f"ADDED_FACADE {relative}")
    return errors

def compare_revisions(repository: Path, base: str, head: str, *, strict: bool = True) -> int:
    """Internal helper for compare_revisions."""
    base = _git(repository, "rev-parse", "--verify", base + "^{commit}").strip()
    head = _git(repository, "rev-parse", "--verify", head + "^{commit}").strip()
    with _revision_checkout(repository, base) as base_root, _revision_checkout(repository, head) as head_root:
        base_surfaces = _surfaces(base_root, strict=strict)
        head_surfaces = _surfaces(head_root, strict=strict)
        base_waivers = _load_identity_waivers(base_root)
        head_waivers = _load_identity_waivers(head_root)
        if strict and base_waivers != head_waivers:
            print("IDENTITY_WAIVER_CHANGED Scripts/phase192/identity_waivers.json")
            return 1
        if not strict:
            _validate_compatibility_new_sections(
                base_root,
                head_root,
                base_surfaces,
                head_surfaces,
            )
    errors = _compare_surfaces(
        base_surfaces,
        head_surfaces,
        strict=strict,
        waived_symbols=base_waivers if not strict else None,
    )
    if errors:
        for error in errors:
            print(error)
        return 1
    verdict = "IDENTITY_EQUIVALENT" if strict else "IDENTITY_COMPATIBLE"
    print(
        f"{verdict} base={base} head={head} "
        f"base_facades={len(base_surfaces)} head_facades={len(head_surfaces)}"
    )
    return 0

def main(argv: list[str] | None = None) -> int:
    """Internal helper for main."""
    parser = argparse.ArgumentParser()
    parser.add_argument("--base", required=True)
    parser.add_argument("--head", required=True)
    parser.add_argument("--repository", type=Path, default=Path("."))
    modes = parser.add_mutually_exclusive_group()
    modes.add_argument(
        "--strict",
        dest="strict",
        action="store_true",
        default=True,
        help="reject additions and initializer changes; use for a decomposition audit (default)",
    )
    modes.add_argument(
        "--compatibility",
        dest="strict",
        action="store_false",
        help="allow additive surface and initializer changes while blocking removals",
    )
    args = parser.parse_args(argv)
    return compare_revisions(args.repository.resolve(), args.base, args.head, strict=args.strict)

if __name__ == "__main__":
    raise SystemExit(main())
