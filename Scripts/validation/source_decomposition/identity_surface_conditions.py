"""Conditional identity-contract helpers for split Python facades."""

from __future__ import annotations

import ast
import copy
import hashlib

_TRY_TYPES = (ast.Try, getattr(ast, "TryStar", ast.Try))
_TYPE_ALIAS_TYPE = getattr(ast, "TypeAlias", None)


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
        if kind == "If" and name_text == "_Phase192Path,_phase192_package_module,_phase192_parent,_phase192_self":
            body_shape = copy.deepcopy(body_shape)
            old_parent = ast.dump(
                ast.parse("_Phase192Path(__file__).resolve().parent", mode="eval").body,
                include_attributes=False,
            )
            new_parent = ast.dump(
                ast.parse("_Phase192Path(__file__).resolve().parent.parent.parent.parent", mode="eval").body,
                include_attributes=False,
            )
            for item in ast.walk(body_shape):
                if not (
                    isinstance(item, ast.Assign)
                    and len(item.targets) == 1
                    and isinstance(item.targets[0], ast.Name)
                    and item.targets[0].id == "_phase192_parent"
                ):
                    continue
                value_dump = ast.dump(item.value, include_attributes=False)
                if value_dump in {old_parent, new_parent}:
                    item.value = ast.parse(
                        "_Phase192Path(__file__).resolve().parent",
                        mode="eval",
                    ).body
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
