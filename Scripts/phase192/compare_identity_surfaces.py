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


_FIXED_IMPORT_ROOTS = frozenset({"pathlib", "json", "Scripts"})
_NAMESPACE_ROOTS = frozenset({"globals", "locals", "vars"})
_DANGEROUS_ROOTS = frozenset({"builtins", "__builtins__", "__builtin__"})
_DANGEROUS_ATTRIBUTES = frozenset(
    {"__dict__", "f_globals", "f_locals", "_getframe"}
)
_NAMESPACE_MUTATIONS = frozenset(
    {"clear", "pop", "popitem", "setdefault", "update", "__setitem__", "__delitem__"}
)
_NAMESPACE_READS = frozenset(
    {"get", "keys", "items", "values", "copy", "__getitem__", "__contains__"}
)


def _literal_string(node: ast.AST | None) -> str | None:
    return node.value if isinstance(node, ast.Constant) and isinstance(node.value, str) else None


def _target_names(target: ast.AST) -> set[str]:
    if isinstance(target, ast.Name):
        return {target.id}
    if isinstance(target, (ast.Tuple, ast.List)):
        names: set[str] = set()
        for item in target.elts:
            names.update(_target_names(item))
        return names
    return set()


def _validate_namespace_access(tree: ast.Module, path: str) -> None:
    parents: dict[int, ast.AST] = {}
    for parent in ast.walk(tree):
        for child in ast.iter_child_nodes(parent):
            parents[id(child)] = parent

    aliases: dict[str, str] = {}

    def origin(node: ast.AST | None, seen: set[str] | None = None) -> str | None:
        if node is None:
            return None
        seen = set() if seen is None else seen
        if isinstance(node, ast.Name):
            if node.id in seen:
                return None
            if node.id in aliases:
                seen.add(node.id)
                return aliases[node.id]
            if node.id in _DANGEROUS_ROOTS:
                return "builtins"
            return None
        if isinstance(node, ast.Call):
            if isinstance(node.func, ast.Name):
                if node.func.id in {"globals", "locals"}:
                    if node.args or node.keywords:
                        raise ValueError(f"Unsupported namespace call in {path}")
                    return "namespace"
                if node.func.id == "vars":
                    if node.keywords or len(node.args) > 1:
                        raise ValueError(f"Unsupported namespace call in {path}")
                    return "namespace" if not node.args else None
                if node.func.id == "__import__":
                    module = _literal_string(node.args[0] if node.args else None)
                    if module is None or module.split(".", 1)[0] not in _FIXED_IMPORT_ROOTS:
                        raise ValueError(f"Unsupported dynamic import in {path}")
                    return "module"
                if node.func.id in {"exec", "eval", "compile"}:
                    raise ValueError(f"Unsupported dynamic execution in {path}")
                if origin(node.func) in {"namespace", "dynamic", "builtins"}:
                    raise ValueError(f"Unsupported namespace call in {path}")
            elif isinstance(node.func, ast.Attribute):
                attr = node.func.attr
                base = origin(node.func.value)
                if attr in _NAMESPACE_MUTATIONS and base in {"namespace", "module_registry", "module_object", "builtins"}:
                    raise ValueError(f"Unsupported namespace mutation in {path}")
                if attr in _NAMESPACE_READS and base == "namespace":
                    key = _literal_string(node.args[0] if node.args else None)
                    if key is None or key in {"__builtins__", "__import__", "exec", "eval", "compile", "import_module"}:
                        raise ValueError(f"Unsupported dynamic namespace key in {path}")
                if attr in {"__getattribute__", "__setattr__", "__delattr__"} and base in {"module_registry", "module_object", "builtins", "namespace"}:
                    raise ValueError(f"Unsupported namespace attribute in {path}")
                if attr == "import_module" and base in {"builtins", "module_registry", "module_object"}:
                    raise ValueError(f"Unsupported dynamic import in {path}")
            return None
        if isinstance(node, ast.Attribute):
            if node.attr in _DANGEROUS_ATTRIBUTES:
                raise ValueError(f"Unsupported namespace attribute in {path}")
            if node.attr == "modules":
                return "module_registry"
            base = origin(node.value)
            if node.attr in {"__getattribute__", "__setattr__", "__delattr__"} and base in {"module_registry", "module_object", "builtins", "namespace"}:
                raise ValueError(f"Unsupported namespace attribute in {path}")
            return "module_object" if base == "module_registry" else None
        if isinstance(node, ast.Subscript):
            base = origin(node.value)
            if base in {"namespace", "module_registry", "module_object", "builtins"}:
                key = _literal_string(node.slice)
                if isinstance(node.ctx, (ast.Store, ast.Del)):
                    raise ValueError(f"Unsupported namespace mutation in {path}")
                if key is None or key in {"__builtins__", "__import__", "exec", "eval", "compile", "import_module"}:
                    raise ValueError(f"Unsupported dynamic namespace key in {path}")
                return "module_object" if base == "module_registry" else None
        return None

    # Gather aliases first so a one-line indirection cannot hide a dynamic root.
    for _ in range(3):
        changed = False
        for node in ast.walk(tree):
            if isinstance(node, ast.Assign):
                value = origin(node.value)
                if value in {"namespace", "module_registry", "module_object", "builtins", "dynamic"}:
                    for target in node.targets:
                        for name in _target_names(target):
                            if aliases.get(name) != value:
                                aliases[name] = value
                                changed = True
            elif isinstance(node, ast.AnnAssign) and node.value is not None:
                value = origin(node.value)
                if value in {"namespace", "module_registry", "module_object", "builtins", "dynamic"}:
                    for name in _target_names(node.target):
                        if aliases.get(name) != value:
                            aliases[name] = value
                            changed = True
        if not changed:
            break

    for node in ast.walk(tree):
        if isinstance(node, ast.Import):
            for alias in node.names:
                root = alias.name.split(".", 1)[0]
                bound = alias.asname or root
                if root in _DANGEROUS_ROOTS:
                    raise ValueError(f"Unsupported builtins import in {path}")
                if root == "sys":
                    aliases[bound] = "sys"
        elif isinstance(node, ast.ImportFrom):
            root = (node.module or "").split(".", 1)[0]
            if root in _DANGEROUS_ROOTS:
                raise ValueError(f"Unsupported builtins import in {path}")
            for alias in node.names:
                if alias.name in {"__import__", "exec", "eval", "compile"}:
                    raise ValueError(f"Unsupported dynamic import in {path}")
                if root == "sys" and alias.name == "modules":
                    aliases[alias.asname or alias.name] = "module_registry"

        if isinstance(node, ast.Name):
            parent = parents.get(id(node))
            if node.id == "__import__" and not (
                isinstance(parent, ast.Call) and parent.func is node
            ):
                raise ValueError(f"Unsupported dynamic import alias in {path}")
            if node.id in _NAMESPACE_ROOTS and not (
                isinstance(parent, ast.Call) and parent.func is node
            ):
                raise ValueError(f"Unsupported namespace alias in {path}")
            if node.id in {"exec", "eval", "compile"}:
                raise ValueError(f"Unsupported dynamic execution in {path}")

        origin(node)

        if isinstance(node, ast.Call) and isinstance(node.func, ast.Name) and node.func.id in {"getattr", "setattr", "delattr"}:
            if node.args and origin(node.args[0]) in {"namespace", "module_registry", "module_object", "builtins"}:
                raise ValueError(f"Unsupported namespace access in {path}")
            if any(_literal_string(argument) in _DANGEROUS_ATTRIBUTES | {"__import__", "exec", "eval", "compile", "import_module"} for argument in node.args[1:]):
                raise ValueError(f"Unsupported namespace access in {path}")


def _section_namespace_guard(source: str, path: str) -> ast.Module:
    tree = ast.parse(source, filename=path)
    _validate_namespace_access(tree, path)
    return tree


def _section_exports(source: str, path: str) -> frozenset[str]:
    """Resolve the generated section ``__all__`` contract statically."""

    tree = _section_namespace_guard(source, path)
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


def _initializer_lines(initializer: Path) -> tuple[str, ...]:
    tree = ast.parse(initializer.read_text(encoding="utf-8"), filename=str(initializer))
    return tuple(ast.unparse(statement).strip() for statement in tree.body)


def _is_docstring_statement(statement: str) -> bool:
    try:
        tree = ast.parse(statement)
    except SyntaxError:
        return False
    return (
        len(tree.body) == 1
        and isinstance(tree.body[0], ast.Expr)
        and isinstance(tree.body[0].value, ast.Constant)
        and isinstance(tree.body[0].value.value, str)
    )


def _statement_bound_names(statement: str) -> frozenset[str]:
    try:
        tree = ast.parse(statement)
    except SyntaxError:
        return frozenset()
    if len(tree.body) != 1:
        return frozenset()
    node = tree.body[0]
    names: set[str] = set()

    def add_target(target: ast.AST) -> None:
        if isinstance(target, ast.Name):
            names.add(target.id)
        elif isinstance(target, (ast.Tuple, ast.List)):
            for item in target.elts:
                add_target(item)

    if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef, ast.ClassDef)):
        names.add(node.name)
    elif isinstance(node, ast.Import):
        for alias in node.names:
            names.add(alias.asname or alias.name.split(".", 1)[0])
    elif isinstance(node, ast.ImportFrom):
        for alias in node.names:
            if alias.name != "*":
                names.add(alias.asname or alias.name)
    elif isinstance(node, (ast.Assign, ast.AnnAssign)):
        targets = node.targets if isinstance(node, ast.Assign) else [node.target]
        for target in targets:
            add_target(target)
    elif isinstance(node, ast.If):
        for item in [*node.body, *node.orelse]:
            if isinstance(item, (ast.Assign, ast.AnnAssign)):
                targets = item.targets if isinstance(item, ast.Assign) else [item.target]
                for target in targets:
                    add_target(target)
            elif isinstance(item, (ast.FunctionDef, ast.AsyncFunctionDef, ast.ClassDef)):
                names.add(item.name)
    return frozenset(names)


def _statement_loaded_names(statement: str) -> frozenset[str]:
    try:
        tree = ast.parse(statement)
    except SyntaxError:
        return frozenset()
    return frozenset(
        node.id
        for node in ast.walk(tree)
        if isinstance(node, ast.Name) and isinstance(node.ctx, ast.Load)
    )


def _simple_assignment_targets(node: ast.Assign | ast.AnnAssign) -> bool:
    targets = node.targets if isinstance(node, ast.Assign) else [node.target]

    def valid(target: ast.AST) -> bool:
        if isinstance(target, ast.Name):
            return True
        return isinstance(target, (ast.Tuple, ast.List)) and all(
            valid(item) for item in target.elts
        )

    return all(valid(target) for target in targets)


def _safe_initializer_value(node: ast.AST, available: frozenset[str]) -> bool:
    if isinstance(node, ast.Constant):
        return True
    if isinstance(node, ast.Name):
        return node.id in available
    if isinstance(node, (ast.List, ast.Tuple, ast.Set)):
        return all(_safe_initializer_value(item, available) for item in node.elts)
    if isinstance(node, ast.Dict):
        return all(
            (key is None or _safe_initializer_value(key, available))
            and _safe_initializer_value(value, available)
            for key, value in zip(node.keys, node.values)
        )
    if isinstance(node, ast.UnaryOp) and isinstance(node.op, (ast.UAdd, ast.USub)):
        return _safe_initializer_value(node.operand, available)
    return False


_SAFE_IMPORT_ROOTS = frozenset(
    {
        "abc",
        "argparse",
        "array",
        "ast",
        "asyncio",
        "base64",
        "collections",
        "contextlib",
        "copy",
        "csv",
        "dataclasses",
        "datetime",
        "enum",
        "functools",
        "hashlib",
        "io",
        "itertools",
        "json",
        "logging",
        "math",
        "os",
        "pathlib",
        "re",
        "sys",
        "tempfile",
        "textwrap",
        "time",
        "types",
        "typing",
        "uuid",
    }
)

_SAFE_BUILTIN_NAMES = frozenset(
    {
        "BaseException",
        "Exception",
        "False",
        "None",
        "NotImplemented",
        "True",
        "ValueError",
        "TypeError",
        "RuntimeError",
        "bool",
        "bytes",
        "dict",
        "float",
        "frozenset",
        "int",
        "list",
        "object",
        "set",
        "str",
        "tuple",
        "type",
    }
)


def _safe_definition_expression(node: ast.AST | None, available: frozenset[str]) -> bool:
    if node is None:
        return True
    if any(
        isinstance(item, (ast.Call, ast.Lambda, ast.NamedExpr, ast.Await, ast.Yield, ast.YieldFrom))
        for item in ast.walk(node)
    ):
        return False
    loaded = {
        item.id
        for item in ast.walk(node)
        if isinstance(item, ast.Name) and isinstance(item.ctx, ast.Load)
    }
    return loaded <= available


def _safe_function_definition(
    node: ast.FunctionDef | ast.AsyncFunctionDef,
    available: frozenset[str],
) -> bool:
    if node.decorator_list:
        return False
    expressions = [
        node.returns,
        *node.args.defaults,
        *[value for value in node.args.kw_defaults if value is not None],
        *[
            argument.annotation
            for argument in [
                *node.args.posonlyargs,
                *node.args.args,
                *node.args.kwonlyargs,
                *([node.args.vararg] if node.args.vararg else []),
                *([node.args.kwarg] if node.args.kwarg else []),
            ]
        ],
    ]
    return all(_safe_definition_expression(expression, available) for expression in expressions)


def _safe_class_definition(node: ast.ClassDef, available: frozenset[str]) -> bool:
    if node.decorator_list or node.keywords:
        return False
    if any(not _safe_definition_expression(base, available) for base in node.bases):
        return False
    for statement in node.body:
        if isinstance(statement, ast.Pass):
            continue
        if (
            isinstance(statement, ast.Expr)
            and isinstance(statement.value, ast.Constant)
            and isinstance(statement.value.value, str)
        ):
            continue
        if isinstance(statement, (ast.FunctionDef, ast.AsyncFunctionDef)):
            if not _safe_function_definition(statement, available):
                return False
            continue
        if isinstance(statement, ast.Assign):
            if (
                not _simple_assignment_targets(statement)
                or not _safe_initializer_value(statement.value, available)
            ):
                return False
            continue
        if isinstance(statement, ast.AnnAssign):
            if (
                not _simple_assignment_targets(statement)
                or
                statement.value is None
                or not _safe_initializer_value(statement.value, available)
                or not _safe_definition_expression(statement.annotation, available)
            ):
                return False
            continue
        return False
    return True


def _safe_initializer_addition(
    statement: str,
    available: frozenset[str],
    forbidden: frozenset[str],
) -> bool:
    try:
        tree = ast.parse(statement)
    except SyntaxError:
        return True
    if len(tree.body) != 1:
        return False
    node = tree.body[0]
    bound = _statement_bound_names(statement)
    if bound & forbidden or any(name.startswith("__") for name in bound):
        return False
    if isinstance(node, ast.Expr):
        return isinstance(node.value, ast.Constant) and isinstance(node.value.value, str)
    if isinstance(node, ast.Pass):
        return True
    if isinstance(node, ast.Import):
        return bool(node.names) and all(
            alias.name.split(".", 1)[0] in _SAFE_IMPORT_ROOTS
            and not (alias.asname or "").startswith("__")
            for alias in node.names
        )
    if isinstance(node, ast.ImportFrom):
        if node.module == "__future__" or not node.names:
            return False
        if node.level:
            return all(
                alias.name != "*" and not (alias.asname or "").startswith("__")
                for alias in node.names
            )
        return (
            (node.module or "").split(".", 1)[0] in _SAFE_IMPORT_ROOTS
            and all(
                alias.name != "*" and not (alias.asname or "").startswith("__")
                for alias in node.names
            )
        )
    if isinstance(node, ast.Assign):
        return (
            bool(bound)
            and _simple_assignment_targets(node)
            and _safe_initializer_value(node.value, available)
        )
    if isinstance(node, ast.AnnAssign):
        return (
            bool(bound)
            and _simple_assignment_targets(node)
            and node.value is not None
            and _safe_initializer_value(node.value, available)
            and _safe_initializer_value(node.annotation, available)
        )
    if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef)):
        return _safe_function_definition(node, available)
    if isinstance(node, ast.ClassDef):
        return _safe_class_definition(node, available)
    if isinstance(node, ast.If):
        inert = (
            isinstance(node.test, ast.Name)
            and node.test.id == "TYPE_CHECKING"
        ) or (
            isinstance(node.test, ast.Attribute)
            and isinstance(node.test.value, ast.Name)
            and node.test.value.id == "typing"
            and node.test.attr == "TYPE_CHECKING"
        ) or (isinstance(node.test, ast.Constant) and node.test.value is False)
        return (
            inert
            and not node.orelse
            and all(
                _safe_initializer_addition(ast.unparse(item), available, forbidden)
                for item in node.body
            )
        )
    if isinstance(node, ast.Assert):
        return _safe_initializer_value(node.test, available) and (
            node.msg is None or _safe_initializer_value(node.msg, available)
        )
    return False


def _initializer_is_additive(
    base: tuple[str, ...] | None,
    head: tuple[str, ...] | None,
    base_symbols: frozenset[str],
) -> bool:
    if base is None or head is None:
        return True
    base_lines = list(base)
    head_lines = list(head)
    base_docstring = bool(base_lines) and _is_docstring_statement(base_lines[0])
    head_docstring = bool(head_lines) and _is_docstring_statement(head_lines[0])
    if base_docstring != head_docstring:
        return False
    if base_docstring:
        base_lines = base_lines[1:]
        head_lines = head_lines[1:]
    matched: set[int] = set()
    cursor = 0
    for statement in base_lines:
        try:
            index = head_lines.index(statement, cursor)
        except ValueError:
            return False
        matched.add(index)
        cursor = index + 1
    forbidden = set(base_symbols)
    available = set(_SAFE_BUILTIN_NAMES)
    for statement in base_lines:
        names = _statement_bound_names(statement)
        forbidden.update(names)
    for index, statement in enumerate(head_lines):
        if index in matched:
            available.update(_statement_bound_names(statement))
            available.update(_statement_loaded_names(statement))
            continue
        if not _safe_initializer_addition(
            statement,
            frozenset(available),
            frozenset(forbidden),
        ):
            return False
        names = _statement_bound_names(statement)
        if names & forbidden:
            return False
        forbidden.update(names)
        available.update(names)
        available.update(_statement_loaded_names(statement))
    return True


def _sections_preserve_order(base: tuple[str, ...], head: tuple[str, ...]) -> bool:
    if len(set(base)) != len(base) or len(set(head)) != len(head):
        return False
    cursor = 0
    for section in head:
        if cursor < len(base) and section == base[cursor]:
            cursor += 1
    return cursor == len(base)


def _unpack_surface(surface: tuple) -> tuple[
    frozenset[str], tuple[str, ...], str | None, tuple[str, ...] | None, bool | None
]:
    if len(surface) < 2:
        raise ValueError(f"unsupported identity surface shape: {len(surface)} fields")
    symbols = surface[0]
    sections = surface[1]
    raw_initializer = surface[2] if len(surface) > 2 else None
    initializer_digest = raw_initializer if isinstance(raw_initializer, str) else None
    initializer_lines = (
        tuple(raw_initializer)
        if isinstance(raw_initializer, (tuple, list))
        and all(isinstance(item, str) for item in raw_initializer)
        else None
    )
    entrypoint: bool | None = None
    for value in surface[3:]:
        if isinstance(value, bool) and entrypoint is None:
            entrypoint = value
        elif isinstance(value, str) and initializer_digest is None:
            initializer_digest = value
        elif (
            isinstance(value, (tuple, list))
            and initializer_lines is None
            and all(isinstance(item, str) for item in value)
        ):
            initializer_lines = tuple(value)
    return symbols, sections, initializer_digest, initializer_lines, entrypoint


def _surfaces(
    root: Path,
    *,
    strict: bool = True,
) -> dict[str, tuple]:
    """Build identity surfaces for all split facades."""
    result: dict[str, tuple] = {}
    for facade in _split_facades(root):
        relative = facade.relative_to(root).as_posix()
        package = facade.with_suffix("")
        listed = {path.name for path in section_modules(package)}
        actual = {path.name for path in package.glob("*.py")
                  if path.name not in {"__init__.py", "__main__.py"}}
        if listed - actual or (strict and actual - listed):
            missing = ", ".join(sorted(actual - listed))
            extra = ", ".join(sorted(listed - actual))
            raise ValueError(f"{relative}: exported sections differ from package files; "
                             f"missing={missing or '-'} extra={extra or '-'}")
        symbols, sections, initializer_digest = _surface(root, facade)
        result[relative] = (
            symbols,
            sections,
            _initializer_lines(package / "__init__.py"),
            initializer_digest,
            (package / "__main__.py").is_file(),
        )
    return result


def _compare_surfaces(
    base_surfaces: dict[str, tuple],
    head_surfaces: dict[str, tuple],
    *,
    strict: bool = True,
) -> list[str]:
    """Return every removed or added facade, section, symbol, or initializer."""
    errors: list[str] = []
    for relative, base_surface in sorted(base_surfaces.items()):
        current = head_surfaces.get(relative)
        if current is None:
            errors.append(f"REMOVED_FACADE {relative}")
            continue
        (
            base_symbols,
            base_sections,
            base_initializer_digest,
            base_initializer_lines,
            base_entrypoint,
        ) = _unpack_surface(base_surface)
        (
            head_symbols,
            head_sections,
            head_initializer_digest,
            head_initializer_lines,
            head_entrypoint,
        ) = _unpack_surface(current)
        initializer_changed = (
            base_initializer_digest is not None
            and head_initializer_digest is not None
            and base_initializer_digest != head_initializer_digest
        )
        if (
            not initializer_changed
            and base_initializer_lines is not None
            and head_initializer_lines is not None
            and base_initializer_lines != head_initializer_lines
        ):
            initializer_changed = True
        if strict and initializer_changed:
            errors.append(f"PACKAGE_INITIALIZER_CHANGED {relative}")
        missing_sections = sorted(set(base_sections) - set(head_sections))
        extra_sections = sorted(set(head_sections) - set(base_sections))
        missing_symbols = sorted(set(base_symbols) - set(head_symbols))
        extra_symbols = sorted(set(head_symbols) - set(base_symbols))
        if not strict:
            if (
                set(base_sections).issubset(head_sections)
                and not _sections_preserve_order(base_sections, head_sections)
            ):
                errors.append(f"SECTION_ORDER_CHANGED {relative}")
            if base_entrypoint and head_entrypoint is False:
                errors.append(f"MISSING_MODULE_ENTRYPOINT {relative}")
            if not _initializer_is_additive(
                base_initializer_lines,
                head_initializer_lines,
                frozenset(base_symbols),
            ):
                errors.append(f"PACKAGE_INITIALIZER_CHANGED {relative}")
        if missing_sections:
            errors.append(f"MISSING_SECTIONS {relative}: {','.join(missing_sections)}")
        if strict and extra_sections:
            errors.append(f"EXTRA_SECTIONS {relative}: {','.join(extra_sections)}")
        if missing_symbols:
            errors.append(f"MISSING_SYMBOLS {relative}: {','.join(missing_symbols)}")
        if strict and extra_symbols:
            errors.append(f"EXTRA_SYMBOLS {relative}: {','.join(extra_symbols)}")
    if strict:
        for relative in sorted(set(head_surfaces) - set(base_surfaces)):
            errors.append(f"ADDED_FACADE {relative}")
    return errors


def compare_revisions(
    repository: Path,
    base: str,
    head: str,
    *,
    strict: bool = True,
) -> int:
    """Fail unless split facade identity surfaces are exactly equivalent."""
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
    if strict:
        print(f"IDENTITY_EQUIVALENT base={base} head={head} facades={len(base_surfaces)}")
    else:
        print(
            f"IDENTITY_COMPATIBLE base={base} head={head} "
            f"base_facades={len(base_surfaces)} head_facades={len(head_surfaces)}"
        )
    return 0


def main(argv: list[str] | None = None) -> int:
    """Run the identity comparison CLI."""
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
        help="reject additions and initializer changes (default)",
    )
    modes.add_argument(
        "--compatibility",
        dest="strict",
        action="store_false",
        help="allow additive surfaces and initializer changes while blocking removals",
    )
    args = parser.parse_args(argv)
    return compare_revisions(
        args.repository.resolve(), args.base, args.head, strict=args.strict
    )


if __name__ == "__main__":
    raise SystemExit(main())
