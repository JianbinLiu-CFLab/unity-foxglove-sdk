"""Compare split Python identity surfaces between two Git revisions."""

from __future__ import annotations

import argparse
import ast
import hashlib
import keyword
import re
from contextlib import contextmanager
from pathlib import Path
import subprocess
import tarfile
import tempfile
from typing import Iterator
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
        if isinstance(target, ast.Name):
            names.add(target.id)
        elif isinstance(target, (ast.Tuple, ast.List)):
            for item in target.elts:
                add_target(item)

    def visit(statements: list[ast.stmt]) -> None:
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
                    if handler.name:
                        names.add(handler.name)
                    visit(handler.body)
                visit(node.orelse)
                visit(node.finalbody)
            elif isinstance(node, ast.Match):
                for case in node.cases:
                    visit(case.body)
            elif isinstance(node, ast.Delete):
                def discard_target(target: ast.AST) -> None:
                    if isinstance(target, ast.Name):
                        names.discard(target.id)
                    elif isinstance(target, (ast.Tuple, ast.List)):
                        for item in target.elts:
                            discard_target(item)
                for target in node.targets:
                    discard_target(target)

    visit(tree.body)
    return names

def _declared_public_names(tree: ast.Module) -> frozenset[str]:
    return frozenset(
        name for name in _top_level_names(tree, include_imports=True)
        if not name.startswith("_")
    )

def _is_public_globals_comprehension(value: ast.AST) -> bool:
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
    names: set[str] = set()
    def add_target(target: ast.AST) -> None:
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
    control_types = (ast.If, ast.For, ast.AsyncFor, ast.While, *_TRY_TYPES, ast.With, ast.AsyncWith, ast.Match)

    def block_sets(statements: list[ast.stmt]) -> tuple[set[str], set[str]]:
        maybe: set[str] = set()
        guaranteed: set[str] = set()
        for statement in statements:
            statement_maybe, statement_guaranteed = statement_sets(statement)
            maybe.update(statement_maybe)
            guaranteed.update(statement_guaranteed)
        return maybe, guaranteed

    def statement_sets(statement: ast.stmt) -> tuple[set[str], set[str]]:
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
    if _TYPE_ALIAS_TYPE is not None and any(isinstance(node, _TYPE_ALIAS_TYPE) for node in ast.walk(tree)):
        raise ValueError(f"Unsupported type alias binding in {path}")

    class Visitor(ast.NodeVisitor):
        def __init__(self) -> None:
            self._class_depth = 0

        def _visit_definition_header(self, node: ast.FunctionDef | ast.AsyncFunctionDef) -> None:
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
            self._visit_definition_header(node)
        def visit_AsyncFunctionDef(self, node: ast.AsyncFunctionDef) -> None:
            self._visit_definition_header(node)
        def visit_ClassDef(self, node: ast.ClassDef) -> None:
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
            if any(not name.startswith("_") for name in node.names):
                raise ValueError(f"Unsupported public global binding in {path}")
        def visit_Import(self, node: ast.Import) -> None:
            if any(alias.name.split(".", 1)[0] in {"builtins", "__builtins__", "__builtin__"} for alias in node.names):
                raise ValueError(f"Unsupported builtins import in {path}")
        def visit_ImportFrom(self, node: ast.ImportFrom) -> None:
            if node.module in {"builtins", "__builtins__", "__builtin__"}:
                raise ValueError(f"Unsupported builtins import in {path}")
            self.generic_visit(node)
        def visit_NamedExpr(self, node: ast.NamedExpr) -> None:
            raise ValueError(f"Unsupported module binding expression in {path}")
        def visit_Match(self, node: ast.Match) -> None:
            raise ValueError(f"Unsupported module pattern binding in {path}")
        def visit_Delete(self, node: ast.Delete) -> None:
            if self._class_depth:
                raise ValueError(f"Unsupported class namespace deletion in {path}")
            def unsafe_target(target: ast.AST) -> bool:
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
    parents: dict[int, ast.AST] = {}
    for parent in ast.walk(tree):
        for child in ast.iter_child_nodes(parent):
            parents[id(child)] = parent
    namespace_names = {"globals", "vars", "locals"}
    dynamic_names = {"exec", "eval", "compile"}
    builtin_names = {"builtins", "__builtins__", "__builtin__"}
    mutation_attrs = {"clear", "pop", "popitem", "setdefault", "update", "__setitem__", "__delitem__"}
    read_attrs = {"get", "__getitem__", "__contains__", "keys", "items", "values", "copy"}
    dangerous_keys = dynamic_names | {"__builtins__", "__import__", "import_module", "getattr", "setattr", "delattr", "__dict__", "f_globals", "f_locals"}

    def literal_string(node: ast.AST | None) -> str | None:
        return node.value if isinstance(node, ast.Constant) and isinstance(node.value, str) else None
    def namespace_call(node: ast.AST | None) -> bool:
        return isinstance(node, ast.Call) and isinstance(node.func, ast.Name) and node.func.id in namespace_names and not node.args and not node.keywords
    def key_node(node: ast.Call | ast.Subscript) -> ast.AST | None:
        return node.slice if isinstance(node, ast.Subscript) else (node.args[0] if node.args else None)
    def check_key(node: ast.Call | ast.Subscript) -> None:
        key = literal_string(key_node(node))
        if key is None:
            raise ValueError(f"Unsupported dynamic namespace key in {path}")
        if key in dangerous_keys:
            raise ValueError(f"Unsupported dynamic execution namespace key in {path}")
    def literal_modules_key(node: ast.AST | None) -> bool:
        return isinstance(node, ast.Constant) and node.value == "modules"

    def namespace_object(node: ast.AST) -> bool:
        return (
            isinstance(node, ast.Call)
            and isinstance(node.func, ast.Name)
            and node.func.id in namespace_names
            and bool(node.args)
        )

    module_registry_aliases: set[str] = set()

    def contains_module_registry(node: ast.AST) -> bool:
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
            if literal_modules_key(node.slice) and isinstance(node.value, ast.Call):
                if (
                    isinstance(node.value.func, ast.Attribute)
                    and node.value.func.attr in {"getattr", "__getattribute__"}
                ):
                    return True
        return any(contains_module_registry(child) for child in ast.iter_child_nodes(node))

    def target_names(target: ast.AST) -> set[str]:
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
        if isinstance(node, ast.Global) and any(not name.startswith("_") for name in node.names):
            raise ValueError(f"Unsupported public global binding in {path}")
        if isinstance(node, ast.Import):
            if any(alias.name.split(".", 1)[0] in builtin_names or alias.name.rsplit(".", 1)[-1] in dynamic_names for alias in node.names):
                raise ValueError(f"Unsupported dynamic execution import in {path}")
        if isinstance(node, ast.ImportFrom):
            if (node.module or "").split(".", 1)[0] in builtin_names or any(alias.name in dynamic_names or alias.name == "import_module" for alias in node.names):
                raise ValueError(f"Unsupported dynamic execution import in {path}")
        if isinstance(node, ast.Name):
            if node.id in builtin_names or node.id in dynamic_names:
                raise ValueError(f"Unsupported dynamic execution in {path}")
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
            if isinstance(node.func, ast.Name) and node.func.id == "__import__" and node.args and literal_string(node.args[0]) in builtin_names:
                raise ValueError(f"Unsupported namespace access in {path}")
            if isinstance(node.func, ast.Attribute) and node.func.attr in dynamic_names:
                value = node.func.value
                if isinstance(value, ast.Name) and value.id in builtin_names:
                    raise ValueError(f"Unsupported dynamic execution in {path}")
                if isinstance(value, ast.Call) and (
                    (isinstance(value.func, ast.Name) and value.func.id == "__import__")
                    or (isinstance(value.func, ast.Attribute) and value.func.attr == "import_module")
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
            if any(isinstance(argument, ast.Call) and isinstance(argument.func, ast.Name) and argument.func.id in namespace_names for argument in node.args):
                raise ValueError(f"Unsupported namespace escape in {path}")
            if isinstance(node.func, ast.Name) and node.func.id in {"getattr", "setattr", "delattr"} and any(isinstance(argument, ast.Constant) and argument.value in dangerous_keys for argument in node.args[1:]):
                raise ValueError(f"Unsupported namespace access in {path}")
            if reject_module_registry and isinstance(node.func, ast.Name) and node.func.id in {"setattr", "delattr"} and node.args and contains_module_registry(node.args[0]):
                raise ValueError(f"Unsupported module namespace mutation in {path}")
            if reject_module_registry and isinstance(node.func, ast.Attribute) and node.func.attr in mutation_attrs and contains_module_registry(node.func.value):
                raise ValueError(f"Unsupported module namespace mutation in {path}")
            if reject_module_registry and isinstance(node.func, ast.Attribute) and node.func.attr in {"__setattr__", "__delattr__"} and node.args and any(contains_module_registry(argument) for argument in node.args):
                raise ValueError(f"Unsupported module namespace mutation in {path}")
            if isinstance(node.func, ast.Attribute) and node.func.attr == "__getattribute__":
                value = node.func.value
                if isinstance(value, ast.Name) and value.id in builtin_names:
                    raise ValueError(f"Unsupported dynamic execution in {path}")
                if isinstance(value, ast.Call) and isinstance(value.func, ast.Name) and value.func.id == "__import__":
                    raise ValueError(f"Unsupported dynamic execution in {path}")
                if contains_module_registry(value):
                    raise ValueError(f"Unsupported dynamic execution in {path}")
    if any(isinstance(node, ast.Call) and isinstance(node.func, ast.Attribute) and node.func.attr == "import_module" and node.args and literal_string(node.args[0]) in builtin_names for node in ast.walk(tree)):
        raise ValueError(f"Unsupported builtins import in {path}")

def _symbols(
    source: str,
    path: str,
    *,
    reject_conditional: bool = True,
) -> frozenset[str]:
    tree = _parse_source(source, path)
    _reject_unsupported_module_bindings(tree, path)
    _validate_namespace_access(tree, set(), path)
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
    tree = _parse_source(source, path)
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
    tree = _parse_source(initializer.read_text(encoding="utf-8"), initializer)
    return tuple(ast.unparse(statement).strip() for statement in tree.body)

def _initializer_digest(initializer: Path) -> str:
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

def _entrypoint_contract(path: Path) -> tuple[bool, str | None, tuple[str, ...], frozenset[str]]:
    if not path.is_file():
        return False, None, (), frozenset()
    source = path.read_text(encoding="utf-8")
    compile(source, str(path), "exec")
    tree = _parse_source(source, path)
    relative_imports = [
        node
        for node in tree.body
        if isinstance(node, ast.ImportFrom) and node.level == 1
    ]
    if not relative_imports:
        raise ValueError(f"module entrypoint does not import its package: {path}")
    package_dir = path.parent
    package_init = package_dir / "__init__.py"
    package_exports: set[str] = set()
    if package_init.is_file():
        package_tree = _parse_source(package_init.read_text(encoding="utf-8"), package_init)
        package_exports.update(_top_level_names(package_tree, include_imports=True))
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
                    package_exports.update(
                        _section_exports(
                            section_path.read_text(encoding="utf-8"),
                            str(section_path),
                            reject_conditional=False,
                        )
                    )
    for node in relative_imports:
        if node.module is None:
            if any(alias.name != "*" and alias.name not in package_exports for alias in node.names):
                missing = sorted(alias.name for alias in node.names if alias.name != "*" and alias.name not in package_exports)
                raise ValueError(f"relative entrypoint export is missing: {path}: {','.join(missing)}")
            continue
        parts = node.module.split(".")
        if any(_SECTION_NAME_RE.fullmatch(part) is None or keyword.iskeyword(part) for part in parts):
            raise ValueError(f"invalid relative module import in entrypoint: {path}")
        module_file = package_dir.joinpath(*parts).with_suffix(".py")
        module_package = package_dir.joinpath(*parts) / "__init__.py"
        target = module_file if module_file.is_file() else module_package if module_package.is_file() else None
        if target is None:
            raise ValueError(f"relative entrypoint module is missing: {path}: {node.module}")
        if target.name == "__init__.py":
            target_exports = _top_level_names(_parse_source(target.read_text(encoding="utf-8"), target), include_imports=True)
        else:
            target_exports = _top_level_names(_parse_source(target.read_text(encoding="utf-8"), target), include_imports=True)
        if any(alias.name != "*" and alias.name not in target_exports for alias in node.names):
            missing = sorted(alias.name for alias in node.names if alias.name != "*" and alias.name not in target_exports)
            raise ValueError(f"relative entrypoint symbol is missing: {path}: {node.module}:{','.join(missing)}")
    imported_names = {
        alias.asname or alias.name
        for node in relative_imports
        for alias in node.names
        if alias.name != "*"
    }
    has_star_import = any(
        alias.name == "*"
        for node in relative_imports
        for alias in node.names
    )
    local_names = {
        node.name
        for node in tree.body
        if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef, ast.ClassDef))
    }
    dynamic_names: set[str] = set()

    def is_main_guard(node: ast.If) -> bool:
        test = node.test
        return (
            isinstance(test, ast.Compare)
            and len(test.ops) == 1
            and isinstance(test.ops[0], (ast.Eq, ast.Is))
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

    def is_safe_call(node: ast.Call) -> bool:
        function = node.func
        if isinstance(function, ast.Name):
            return function.id in {
                "SystemExit",
                "callable",
                "globals",
            } or function.id in imported_names or function.id in local_names or function.id in dynamic_names
        if isinstance(function, ast.Attribute):
            if function.attr == "get" and isinstance(function.value, ast.Call):
                return (
                    isinstance(function.value.func, ast.Name)
                    and function.value.func.id == "globals"
                )
            if isinstance(function.value, ast.Name):
                return function.value.id in imported_names
        return False

    def validate_guard(node: ast.AST) -> bool:
        delegated = False
        for child in ast.walk(node):
            if isinstance(child, ast.Assign):
                targets = [target for target in child.targets if isinstance(target, ast.Name)]
                if not targets or any(not target.id.startswith("_phase192_") for target in targets):
                    raise ValueError(f"unsafe module entrypoint statement: {path}")
                dynamic_names.update(target.id for target in targets)
            elif isinstance(child, ast.Call):
                if not is_safe_call(child):
                    raise ValueError(f"unsafe module entrypoint call: {path}")
                if (
                    isinstance(child.func, ast.Name)
                    and child.func.id in imported_names | local_names | dynamic_names
                ):
                    delegated = True
        return delegated

    body = list(tree.body)
    if body and isinstance(body[0], ast.Expr) and isinstance(body[0].value, ast.Constant) and isinstance(body[0].value.value, str):
        body = body[1:]
    delegated = False
    for statement in body:
        if isinstance(statement, ast.Import):
            if any(alias.name.split(".", 1)[0] not in _ENTRYPOINT_IMPORT_ROOTS for alias in statement.names):
                raise ValueError(f"unsafe module entrypoint import: {path}")
            continue
        if isinstance(statement, ast.ImportFrom):
            if statement.module == "__future__":
                if statement.level != 0 or any(
                    alias.name != "annotations" or alias.asname is not None
                    for alias in statement.names
                ):
                    raise ValueError(f"unsupported future import in module entrypoint: {path}")
                continue
            if statement.level == 0:
                root = (statement.module or "").split(".", 1)[0]
                if root not in _ENTRYPOINT_IMPORT_ROOTS:
                    raise ValueError(f"unsafe module entrypoint import: {path}")
            continue
        if isinstance(statement, (ast.FunctionDef, ast.AsyncFunctionDef, ast.ClassDef)):
            if statement.decorator_list:
                raise ValueError(f"unsafe module entrypoint decorator: {path}")
            available = frozenset(_SAFE_BUILTIN_NAMES)
            if isinstance(statement, (ast.FunctionDef, ast.AsyncFunctionDef)):
                if not _safe_function_definition(statement, available, postponed_annotations=True):
                    raise ValueError(f"unsafe module entrypoint definition: {path}")
            elif not _safe_class_definition(statement, available, postponed_annotations=True):
                raise ValueError(f"unsafe module entrypoint definition: {path}")
            if has_star_import:
                raise ValueError(f"wildcard module entrypoint has executable definitions: {path}")
            continue
        if isinstance(statement, ast.If) and is_main_guard(statement):
            delegated = validate_guard(statement) or delegated
            continue
        raise ValueError(f"unsafe module entrypoint statement: {path}")
    if not has_star_import and not delegated:
        raise ValueError(f"module entrypoint has no delegated callable: {path}")
    normalized = ast.Module(body=body, type_ignores=[])
    digest = hashlib.sha256(
        ast.dump(normalized, include_attributes=False).encode("utf-8")
    ).hexdigest()
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
    targets: list[ast.AST] = []
    if isinstance(node, ast.Assign):
        targets.extend(node.targets)
    elif isinstance(node, ast.AnnAssign):
        targets.append(node.target)
    names: list[str] = []
    def collect(target: ast.AST) -> None:
        if isinstance(target, ast.Name):
            names.append(target.id)
        elif isinstance(target, (ast.Tuple, ast.List)):
            for item in target.elts:
                collect(item)
    for target in targets:
        collect(target)
    return tuple(names)

def _literal_initializer_value(node: ast.AST) -> bool:
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

def _is_docstring_statement(statement: str) -> bool:
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
    try:
        body = ast.parse(statement).body
    except SyntaxError:
        return frozenset()
    if len(body) != 1:
        return frozenset()

    def collect(node: ast.stmt) -> set[str]:
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
    try:
        tree = ast.parse(statement)
    except SyntaxError:
        return frozenset()
    return frozenset(
        node.id for node in ast.walk(tree)
        if isinstance(node, ast.Name) and isinstance(node.ctx, ast.Load)
    )

def _safe_definition_expression(node: ast.AST | None) -> bool:
    if node is None:
        return True
    return not any(
        isinstance(item, (ast.Call, ast.Lambda, ast.NamedExpr, ast.Await, ast.Yield, ast.YieldFrom))
        for item in ast.walk(node)
    )

def _loaded_names(node: ast.AST | None) -> frozenset[str]:
    if node is None:
        return frozenset()
    return frozenset(
        item.id for item in ast.walk(node)
        if isinstance(item, ast.Name) and isinstance(item.ctx, ast.Load)
    )

def _safe_function_definition(
    node: ast.FunctionDef | ast.AsyncFunctionDef,
    available_names: frozenset[str] = frozenset(),
    *,
    postponed_annotations: bool = False,
) -> bool:
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
    if any(not _safe_definition_expression(value) for value in [*annotations, *defaults]):
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
    if node.decorator_list or node.keywords:
        return False
    if any(not _safe_definition_expression(base) for base in node.bases):
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
            if (
                isinstance(statement, ast.AnnAssign)
                and not postponed_annotations
                and not _loaded_names(statement.annotation) <= available_names
            ):
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

def _safe_initializer_addition(
    statement: str,
    allowed_section_imports: frozenset[str] = frozenset(),
    forbidden_names: frozenset[str] = frozenset(),
    available_names: frozenset[str] = _SAFE_BUILTIN_NAMES,
    *,
    postponed_annotations: bool = False,
) -> bool:
    try:
        parsed = ast.parse(statement)
    except SyntaxError:
        return False
    if len(parsed.body) != 1:
        return False
    node = parsed.body[0]
    bound = _statement_bound_names(statement)
    if bound & forbidden_names:
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
        return bool(bound) and all(
            not name.startswith("__") and not name.startswith("_PHASE192_") for name in bound
        ) and _safe_class_definition(
        node, available_names, postponed_annotations=postponed_annotations
    )
    if isinstance(node, ast.Import):
        return bool(node.names) and all(
            alias.name.split(".", 1)[0] not in {"builtins", "__builtins__", "__builtin__"}
            and alias.name.split(".", 1)[0] not in {"exec", "eval", "compile"}
            and not alias.name.startswith("__")
            and not (alias.asname or "").startswith("__")
            and alias.name.split(".", 1)[0] in _SAFE_IMPORT_ROOTS
            for alias in node.names
        )
    if isinstance(node, ast.ImportFrom):
        if node.module == "__future__":
            return False
        if not node.level:
            root = (node.module or "").split(".", 1)[0]
            return bool(node.names) and root not in {"builtins", "__builtins__", "__builtin__", "exec", "eval", "compile"} and all(
                alias.name != "*"
                and not alias.name.startswith("__")
                and not (alias.asname or "").startswith("__")
                and root in _SAFE_IMPORT_ROOTS
                for alias in node.names
            )
        if node.module is None:
            return bool(node.names) and all(
                alias.name in allowed_section_imports
                and alias.name != "*"
                and (alias.asname is None or not alias.asname.startswith("__"))
                for alias in node.names
            )
        return bool(node.names) and all(
            node.module.split(".", 1)[0] in allowed_section_imports
            and alias.name != "*"
            and not alias.name.startswith("__")
            and not (alias.asname or "").startswith("__")
            for alias in node.names
        )
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
            )
            for item in node.body
        )
    if isinstance(node, ast.Assert):
        return (
            _literal_initializer_value(node.test)
            and (node.msg is None or _literal_initializer_value(node.msg))
        )
    if isinstance(node, ast.Assign):
        return bool(bound) and all(
            not name.startswith("__") and not name.startswith("_PHASE192_") for name in bound
        ) and _literal_initializer_value(node.value)
    if isinstance(node, ast.AnnAssign):
        annotation_names = _loaded_names(node.annotation)
        return bool(bound) and all(
            not name.startswith("__") and not name.startswith("_PHASE192_") for name in bound
        ) and _safe_definition_expression(node.annotation) and (
            postponed_annotations or annotation_names <= available_names
        ) and node.value is not None and _literal_initializer_value(node.value)
    return False

def _initializer_is_additive(
    base_initializer: tuple[str, ...],
    head_initializer: tuple[str, ...],
    allowed_section_imports: frozenset[str] = frozenset(),
    forbidden_external_names: frozenset[str] = frozenset(),
) -> bool:
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
        try:
            index = head.index(statement, cursor)
        except ValueError:
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
            available_names.update(bound)
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
        ):
            return False
        seen_additions.update(bound)
        available_names.update(bound)
    return True

def _sections_preserve_order(base: tuple[str, ...], head: tuple[str, ...]) -> bool:
    if len(set(base)) != len(base) or len(set(head)) != len(head):
        return False
    index = 0
    for section in head:
        if index < len(base) and section == base[index]:
            index += 1
    return index == len(base)

def _surface(
    root: Path,
    facade: Path,
    section_modules=None,
    *,
    reject_conditional: bool = True,
) -> tuple:
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

    def add_conditional_contract(signature: str) -> None:
        conditional_contract.append(signature)

    for section in sections:
        section_source = section.read_text(encoding="utf-8")
        section_tree = _parse_source(section_source, section)
        exports = _section_exports(
            section_source,
            section.relative_to(root).as_posix(),
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
    facade_symbols = _symbols(
        facade_source,
        relative,
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
    entrypoint_present, entrypoint_digest, entrypoint_contract, entrypoint_risks = _entrypoint_contract(package / "__main__.py")
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
    )

def _surfaces(
    root: Path,
    *,
    strict: bool = True,
) -> dict[str, tuple]:
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

def _compare_surfaces(base_surfaces, head_surfaces, *, strict: bool = True) -> list[str]:
    errors: list[str] = []

    def unpack(surface):
        if len(surface) > 13:
            raise ValueError(f"unsupported identity surface shape: {len(surface)} fields")
        defaults = (
            frozenset(), (), (), frozenset(), None, frozenset(), None, (),
            frozenset(), (), None, (), (),
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
        ) = unpack(current)
        missing_sections = sorted(set(base_sections) - set(head_sections))
        extra_sections = sorted(set(head_sections) - set(base_sections))
        missing_symbols = sorted(set(base_symbols) - set(head_symbols))
        extra_symbols = sorted(set(head_symbols) - set(base_symbols))
        if strict and base_risks != head_risks:
            errors.append(f"NAMESPACE_CONTRACT_CHANGED {relative}")
        if strict and tuple(base_conditional_contract or ()) != tuple(head_conditional_contract or ()):
            errors.append(f"CONDITIONAL_CONTRACT_CHANGED {relative}")
        if strict and tuple(base_section_map or ()) != tuple(head_section_map or ()):
            errors.append(f"SECTION_EXPORT_OWNER_CHANGED {relative}")
        if strict and tuple(base_owner_map or ()) != tuple(head_owner_map or ()):
            errors.append(f"SYMBOL_OWNER_CHANGED {relative}")
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
    base = _git(repository, "rev-parse", "--verify", base + "^{commit}").strip()
    head = _git(repository, "rev-parse", "--verify", head + "^{commit}").strip()
    with _revision_checkout(repository, base) as base_root, _revision_checkout(repository, head) as head_root:
        base_surfaces = _surfaces(base_root, strict=True)
        head_surfaces = _surfaces(head_root, strict=strict)
    errors = _compare_surfaces(base_surfaces, head_surfaces, strict=strict)
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