"""Initializer contract helpers for split Python identity surfaces."""

from __future__ import annotations

import ast
import importlib
from functools import lru_cache
from typing import Mapping


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
    "Scripts", "socket", "ssl", "string", "subprocess", "sys", "tempfile", "textwrap",
    "threading", "time", "types", "typing", "unittest", "urllib", "uuid", "warnings", "weakref", "xml",
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
        if alias.name == "*" and root == "Scripts":
            continue
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
