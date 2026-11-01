"""Namespace-risk analysis for split Python identity surfaces."""

from __future__ import annotations

import ast

from Scripts.validation.source_decomposition.identity_surface_conditions import _is_public_globals_comprehension


_NAMESPACE_RISK_NAMES = frozenset({
    "__builtins__", "__import__", "builtins", "compile", "delattr", "eval",
    "exec", "getattr", "globals", "import_module", "locals", "setattr",
    "vars", "print", "open",
})
_NAMESPACE_RISK_ATTRIBUTES = frozenset({
    "__dict__", "__getattribute__", "__setattr__", "__delattr__",
    "f_globals", "f_locals", "import_module", "modules", "_getframe",
})
_NAMESPACE_RISK_MODULE_ATTRIBUTES = {
    "builtins": frozenset(_NAMESPACE_RISK_NAMES),
    "os": frozenset({
        "chmod", "mkdir", "open", "popen", "remove", "rename", "replace",
        "rmdir", "system", "unlink",
    }),
    "shutil": frozenset({"copy", "copy2", "copytree", "make_archive", "move", "rmtree"}),
    "subprocess": frozenset({"Popen", "call", "check_call", "check_output", "run"}),
}

def _namespace_risk_signatures(
    tree: ast.AST,
    *,
    allow_public_globals: bool = False,
) -> frozenset[str]:
    """Return stable AST fingerprints for namespace and import escape surfaces."""
    signatures: set[str] = set()
    seen_callables: set[tuple[int, tuple[tuple[str, str], ...], str]] = set()
    allowed_public_global_calls: set[int] = set()
    if allow_public_globals and isinstance(tree, ast.Module):
        for statement in tree.body:
            targets = (
                statement.targets
                if isinstance(statement, ast.Assign)
                else (statement.target,)
                if isinstance(statement, ast.AnnAssign)
                else ()
            )
            if any(
                isinstance(target, ast.Name) and target.id == "__all__"
                for target in targets
            ) and _is_public_globals_comprehension(statement.value):
                allowed_public_global_calls.update(
                    id(node)
                    for node in ast.walk(statement.value)
                    if isinstance(node, ast.Call)
                    and isinstance(node.func, ast.Name)
                    and node.func.id == "globals"
                )
    class FrozenAlias:
        """Preserve the AST object captured by an alias assignment."""

        def __init__(self, node: ast.AST) -> None:
            """Store the AST node captured at assignment time."""
            self.node = node

    class AliasSequence:
        """Preserve tuple and list elements for destructuring assignments."""

        def __init__(self, items: tuple["AliasValue", ...]) -> None:
            """Store the values captured by a tuple or list expression."""
            self.items = items

    AliasAtom = ast.AST | FrozenAlias | AliasSequence | None
    AliasValue = AliasAtom | tuple[AliasAtom, ...]
    definition_envs: dict[int, dict[str, AliasValue]] = {}
    future_annotations = isinstance(tree, ast.Module) and any(
        isinstance(statement, ast.ImportFrom)
        and statement.module == "__future__"
        and any(alias.name == "annotations" for alias in statement.names)
        for statement in tree.body
    )

    def value_key(value: AliasValue) -> tuple[str, ...]:
        """Return a stable key for one binding value."""
        if value is None:
            return ("<unknown>",)
        if isinstance(value, tuple):
            return ("<choice>", *sum((value_key(item) for item in value), ()))
        if isinstance(value, FrozenAlias):
            return ("<frozen>", ast.dump(value.node, include_attributes=False))
        if isinstance(value, AliasSequence):
            return ("<sequence>", *sum((value_key(item) for item in value.items), ()))
        return (ast.dump(value, include_attributes=False),)

    def candidate_values(value: AliasValue) -> tuple[AliasAtom, ...]:
        """Expand a possibly merged binding into individual candidates."""
        if isinstance(value, tuple):
            return value
        return (value,)

    def freeze_value(value: AliasValue) -> AliasValue:
        """Freeze aliases so later rebinding cannot change their captured target."""
        if isinstance(value, tuple):
            return tuple(freeze_value(item) for item in value)
        if isinstance(value, FrozenAlias) or value is None:
            return value
        if isinstance(value, AliasSequence):
            return AliasSequence(tuple(freeze_value(item) for item in value.items))
        return FrozenAlias(value)

    def merge_values(*values: AliasValue) -> AliasValue:
        """Merge branch bindings while preserving every possible target."""
        merged: list[AliasAtom] = []
        seen: set[tuple[str, ...]] = set()
        for value in values:
            for candidate in candidate_values(value):
                key = value_key(candidate)
                if key not in seen:
                    seen.add(key)
                    merged.append(candidate)
        if not merged:
            return None
        if len(merged) == 1:
            return merged[0]
        return tuple(merged)

    def environment_key(env: dict[str, AliasValue]) -> tuple[tuple[str, str], ...]:
        """Return a deterministic key for the callable's visible bindings."""
        return tuple(sorted((name, repr(value_key(value))) for name, value in env.items()))

    def resolve_target(
        node: ast.AST,
        env: dict[str, AliasValue],
        seen: frozenset[str] = frozenset(),
    ) -> AliasValue:
        """Resolve aliases using the bindings visible at the call site."""
        if isinstance(node, ast.Name):
            if node.id in seen or node.id not in env or env[node.id] is None:
                return node
            bound = env[node.id]
            resolved: list[AliasAtom] = []
            for candidate in candidate_values(bound):
                if candidate is None:
                    resolved.append(node)
                elif isinstance(candidate, FrozenAlias):
                    resolved.append(candidate)
                else:
                    value = resolve_target(candidate, env, seen | {node.id})
                    resolved.extend(candidate_values(value))
            return merge_values(*resolved)
        if isinstance(node, ast.Attribute):
            value = resolve_target(node.value, env, seen)
            resolved: list[AliasAtom] = []
            for candidate in candidate_values(value):
                if isinstance(candidate, FrozenAlias):
                    resolved.append(
                        ast.Attribute(value=candidate.node, attr=node.attr, ctx=ast.Load())
                    )
                elif isinstance(candidate, ast.AST):
                    resolved.append(ast.Attribute(value=candidate, attr=node.attr, ctx=ast.Load()))
                else:
                    resolved.append(node)
            return merge_values(*resolved)
        if isinstance(node, ast.IfExp):
            return merge_values(
                resolve_target(node.body, env, seen),
                resolve_target(node.orelse, env, seen),
            )
        if isinstance(node, ast.Subscript):
            base = resolve_target(node.value, env, seen)
            try:
                index = ast.literal_eval(node.slice)
            except (TypeError, ValueError, SyntaxError):
                index = None
            if isinstance(index, int):
                resolved: list[AliasAtom] = []
                for candidate in candidate_values(base):
                    if isinstance(candidate, AliasSequence) and -len(candidate.items) <= index < len(candidate.items):
                        resolved.append(candidate.items[index])
                if resolved:
                    return merge_values(*resolved)
            return node
        if isinstance(node, (ast.Tuple, ast.List)):
            return AliasSequence(tuple(resolve_target(element, env, seen) for element in node.elts))
        return node

    def is_risky_resolved(
        target: AliasValue,
        env: dict[str, AliasValue] | None = None,
    ) -> bool:
        """Return whether any resolved target exposes an import-time risk."""
        if isinstance(target, tuple):
            return any(is_risky_resolved(candidate, env) for candidate in target)
        if isinstance(target, FrozenAlias):
            return is_risky_resolved(target.node, env)
        if isinstance(target, ast.Name) and target.id in _NAMESPACE_RISK_NAMES:
            return True
        if isinstance(target, ast.Attribute):
            if target.attr in _NAMESPACE_RISK_ATTRIBUTES:
                return True
            if (
                isinstance(target.value, ast.Name)
                and target.attr in _NAMESPACE_RISK_MODULE_ATTRIBUTES.get(target.value.id, ())
            ):
                return True
            if target.attr in {
                "open", "touch", "write_text", "write_bytes", "unlink", "mkdir",
                "rmdir", "rename", "replace", "symlink_to", "hardlink_to",
                "chmod", "lchmod",
            }:
                if isinstance(target.value, ast.Name) and target.value.id == "Path":
                    return True
                if (
                    isinstance(target.value, ast.Attribute)
                    and target.value.attr == "Path"
                    and isinstance(target.value.value, ast.Name)
                    and target.value.value.id == "pathlib"
                ):
                    return True
                if isinstance(target.value, ast.Call):
                    constructor = resolve_target(target.value.func, env or {})
                    constructor_node = (
                        constructor.node if isinstance(constructor, FrozenAlias) else constructor
                    )
                    if isinstance(constructor_node, ast.Name) and constructor_node.id == "Path":
                        return True
                    if (
                        isinstance(constructor_node, ast.Attribute)
                        and constructor_node.attr == "Path"
                        and isinstance(constructor_node.value, ast.Name)
                        and constructor_node.value.id == "pathlib"
                    ):
                        return True
            if (
                target.attr in {"append", "extend", "insert", "remove", "pop", "clear"}
                and isinstance(target.value, ast.Attribute)
                and target.value.attr == "path"
                and isinstance(target.value.value, ast.Name)
                and target.value.value.id == "sys"
            ):
                return True
        return False

    def risky_target(node: ast.AST, env: dict[str, AliasValue]) -> bool:
        """Recognize calls that can mutate or expose import-time process state."""
        return is_risky_resolved(resolve_target(node, env), env)

    def driver_mode(node: ast.AST, env: dict[str, AliasValue]) -> str:
        """Classify built-in consumers after resolving aliases at the call site."""
        modes: set[str] = set()
        for candidate in candidate_values(resolve_target(node, env)):
            candidate_node = candidate.node if isinstance(candidate, FrozenAlias) else candidate
            if isinstance(candidate_node, ast.Name):
                name = candidate_node.id
                if name in _FULL_ITERATION_DRIVERS:
                    modes.add(_DEFERRED_FULL_ITERATION)
                elif name == "anext":
                    modes.add(_DEFERRED_ASYNC_FIRST_YIELD)
                elif name == "next":
                    modes.add(_DEFERRED_FIRST_YIELD)
            elif isinstance(candidate_node, ast.Attribute) and isinstance(candidate_node.value, ast.Name):
                if candidate_node.value.id in {"builtins", "__builtins__"}:
                    if candidate_node.attr in _FULL_ITERATION_DRIVERS:
                        modes.add(_DEFERRED_FULL_ITERATION)
                    elif candidate_node.attr == "anext":
                        modes.add(_DEFERRED_ASYNC_FIRST_YIELD)
                    elif candidate_node.attr == "next":
                        modes.add(_DEFERRED_FIRST_YIELD)
        if _DEFERRED_FULL_ITERATION in modes:
            return _DEFERRED_FULL_ITERATION
        if _DEFERRED_FIRST_YIELD in modes:
            return _DEFERRED_FIRST_YIELD
        if _DEFERRED_ASYNC_FIRST_YIELD in modes:
            return _DEFERRED_ASYNC_FIRST_YIELD
        return _DEFERRED_NONE

    def resolved_callable_names(node: ast.AST, env: dict[str, AliasValue]) -> frozenset[str]:
        """Return simple callable names visible at a call site."""
        names: set[str] = set()
        for candidate in candidate_values(resolve_target(node, env)):
            candidate_node = candidate.node if isinstance(candidate, FrozenAlias) else candidate
            if isinstance(candidate_node, ast.Name):
                names.add(candidate_node.id)
            elif isinstance(candidate_node, ast.Attribute):
                names.add(candidate_node.attr)
        return frozenset(names)

    def expression_always_truthy(node: ast.AST, env: dict[str, AliasValue]) -> bool:
        """Return whether an expression is known to produce a truthy value."""
        truth = constant_truth(node)
        if truth is True:
            return True
        for candidate in candidate_values(resolve_target(node, env)):
            candidate_node = candidate.node if isinstance(candidate, FrozenAlias) else candidate
            if isinstance(candidate_node, ast.Call):
                target = resolve_target(candidate_node.func, env)
                if any(
                    isinstance(function, (ast.FunctionDef, ast.AsyncFunctionDef))
                    and contains_yield(function)
                    for function in candidate_values(target)
                ):
                    return True
        return False

    def combine_deferred_modes(outer: str, driver: str) -> str:
        """Combine enclosing execution with the consumer's execution mode."""
        if outer == _DEFERRED_FULL_ITERATION or driver == _DEFERRED_FULL_ITERATION:
            return _DEFERRED_FULL_ITERATION
        if outer == _DEFERRED_FIRST_YIELD or driver == _DEFERRED_FIRST_YIELD:
            return _DEFERRED_FIRST_YIELD
        if driver == _DEFERRED_ASYNC_FIRST_YIELD:
            return (
                _DEFERRED_FIRST_YIELD
                if outer == _DEFERRED_COROUTINE_COMPLETION
                else _DEFERRED_NONE
            )
        return outer

    def bind_import(node: ast.Import | ast.ImportFrom, env: dict[str, AliasValue]) -> None:
        """Bind only absolute imports to known targets; relative modules remain unknown."""
        if isinstance(node, ast.Import):
            for imported in node.names:
                bound = imported.asname or imported.name.split(".", 1)[0]
                env[bound] = freeze_value(
                    ast.Name(id=imported.name.split(".", 1)[0], ctx=ast.Load())
                )
            return
        for imported in node.names:
            if imported.name == "*":
                continue
            bound = imported.asname or imported.name
            if node.level:
                env[bound] = freeze_value(
                    ast.Name(id=f"<relative:{node.module or ''}>", ctx=ast.Load())
                )
            else:
                env[bound] = freeze_value(
                    ast.Attribute(
                        value=ast.Name(id=node.module or "", ctx=ast.Load()),
                        attr=imported.name,
                        ctx=ast.Load(),
                    )
                )

    def bind_assignment(target: ast.AST, value: AliasValue, env: dict[str, AliasValue]) -> None:
        """Update only names bound by a simple assignment."""
        if isinstance(target, ast.Name):
            env[target.id] = freeze_value(value)
        elif isinstance(target, (ast.Tuple, ast.List)):
            sequences = [
                candidate
                for candidate in candidate_values(value)
                if isinstance(candidate, AliasSequence)
            ]
            for index, element in enumerate(target.elts):
                candidates = [
                    sequence.items[index]
                    for sequence in sequences
                    if index < len(sequence.items)
                ]
                bind_assignment(element, merge_values(*candidates), env)

    _DEFERRED_NONE = "NONE"
    _DEFERRED_FIRST_YIELD = "FIRST_YIELD"
    _DEFERRED_ASYNC_FIRST_YIELD = "ASYNC_FIRST_YIELD"
    _DEFERRED_FULL_ITERATION = "FULL_ITERATION"
    _DEFERRED_COROUTINE_COMPLETION = "COROUTINE_COMPLETION"
    _FIRST_YIELD_DRIVERS = frozenset({"next", "anext"})
    _FULL_ITERATION_DRIVERS = frozenset({
        "list", "tuple", "sum", "set", "frozenset", "sorted", "dict", "bytes", "bytearray",
        "min", "max", "any", "all",
    })

    def contains_yield(node: ast.FunctionDef | ast.AsyncFunctionDef) -> bool:
        """Return whether a normal function is a generator function."""
        found = False

        class YieldVisitor(ast.NodeVisitor):
            """Find yields without descending into nested deferred scopes."""

            def visit_Yield(self, current: ast.Yield) -> None:
                """Mark a direct yield expression."""
                nonlocal found
                found = True

            def visit_YieldFrom(self, current: ast.YieldFrom) -> None:
                """Mark a direct yield-from expression."""
                nonlocal found
                found = True

            def visit_FunctionDef(self, current: ast.FunctionDef) -> None:
                """Visit only the function whose generator status is queried."""
                if current is not node:
                    return
                self.generic_visit(current)

            def visit_AsyncFunctionDef(self, current: ast.AsyncFunctionDef) -> None:
                """Visit only the root async function when encountered."""
                if current is not node:
                    return
                self.generic_visit(current)

            def visit_Lambda(self, current: ast.Lambda) -> None:
                """Skip lambda bodies nested inside the function."""
                return

            def visit_ClassDef(self, current: ast.ClassDef) -> None:
                """Skip class bodies nested inside the function."""
                return

        YieldVisitor().visit(node)
        return found

    def inspect_definition(
        node: ast.FunctionDef | ast.AsyncFunctionDef | ast.Lambda,
        env: dict[str, AliasValue],
    ) -> None:
        """Inspect expressions evaluated while a callable is defined."""
        definition_envs[id(node)] = dict(env)
        if isinstance(node, ast.Lambda):
            expressions = (*node.args.defaults, *node.args.kw_defaults)
        else:
            for decorator in node.decorator_list:
                scan_node(decorator, env)
                if not isinstance(decorator, ast.Call):
                    decorator_call = ast.Call(func=decorator, args=[node], keywords=[])
                    if risky_target(decorator, env):
                        signatures.add(ast.dump(decorator_call, include_attributes=False))
                    record_callable(decorator, env, decorator_call)
            expressions = (*node.args.defaults, *node.args.kw_defaults)
        for expression in expressions:
            if expression is not None:
                scan_node(expression, env)
        if isinstance(node, ast.Lambda) or future_annotations:
            return
        for argument in (*node.args.posonlyargs, *node.args.args, *node.args.kwonlyargs):
            if argument.annotation is not None:
                scan_node(argument.annotation, env)
        if node.args.vararg and node.args.vararg.annotation is not None:
            scan_node(node.args.vararg.annotation, env)
        if node.args.kwarg and node.args.kwarg.annotation is not None:
            scan_node(node.args.kwarg.annotation, env)
        if node.returns is not None:
            scan_node(node.returns, env)

    def bind_callable_parameters(
        target: ast.FunctionDef | ast.AsyncFunctionDef | ast.Lambda,
        call: ast.Call,
        caller_env: dict[str, AliasValue],
        local_env: dict[str, AliasValue],
    ) -> None:
        """Bind call arguments and definition-time defaults into a local environment."""
        arguments = target.args
        positional = [*arguments.posonlyargs, *arguments.args]
        defaults = list(arguments.defaults)
        definition_env = definition_envs.get(id(target), caller_env)
        default_start = len(positional) - len(defaults)
        for index, argument in enumerate(positional):
            if index < len(call.args):
                local_env[argument.arg] = freeze_value(
                    resolve_target(call.args[index], caller_env)
                )
            elif index >= default_start:
                local_env[argument.arg] = freeze_value(
                    resolve_target(defaults[index - default_start], definition_env)
                )
            else:
                local_env[argument.arg] = None
        keyword_names = {argument.arg for argument in positional}
        keyword_names.update(argument.arg for argument in arguments.kwonlyargs)
        for keyword in call.keywords:
            if keyword.arg is not None and keyword.arg in keyword_names:
                local_env[keyword.arg] = freeze_value(
                    resolve_target(keyword.value, caller_env)
                )
        kw_defaults = dict(zip((argument.arg for argument in arguments.kwonlyargs), arguments.kw_defaults))
        for argument in arguments.kwonlyargs:
            if argument.arg not in local_env:
                default = kw_defaults[argument.arg]
                local_env[argument.arg] = (
                    None
                    if default is None
                    else freeze_value(resolve_target(default, definition_env))
                )
        if arguments.vararg:
            local_env[arguments.vararg.arg] = None
        if arguments.kwarg:
            local_env[arguments.kwarg.arg] = None

    def record_callable(
        target: ast.AST,
        caller_env: dict[str, AliasValue],
        call: ast.Call,
        deferred_mode: str = _DEFERRED_NONE,
    ) -> None:
        """Record risks reachable through a function called during import."""
        resolved = resolve_target(target, caller_env)
        for candidate in candidate_values(resolved):
            candidate_node = candidate.node if isinstance(candidate, FrozenAlias) else candidate
            if not isinstance(candidate_node, (ast.FunctionDef, ast.AsyncFunctionDef, ast.Lambda)):
                continue
            local_env = dict(caller_env)
            bind_callable_parameters(candidate_node, call, caller_env, local_env)
            callable_key = (id(candidate_node), environment_key(local_env), deferred_mode)
            if callable_key in seen_callables:
                continue
            seen_callables.add(callable_key)
            if isinstance(candidate_node, ast.AsyncFunctionDef) and deferred_mode == _DEFERRED_NONE:
                continue
            if (
                isinstance(candidate_node, (ast.FunctionDef, ast.AsyncFunctionDef))
                and contains_yield(candidate_node)
                and deferred_mode == _DEFERRED_NONE
            ):
                continue
            if isinstance(candidate_node, ast.Lambda):
                scan_node(candidate_node.body, local_env, deferred_mode=deferred_mode)
            elif contains_yield(candidate_node) and deferred_mode == _DEFERRED_FIRST_YIELD:
                scan_prefix_sequence(
                    candidate_node.body,
                    local_env,
                    evaluate_annotations=False,
                    deferred_mode=deferred_mode,
                )
            else:
                scan_sequence(
                    candidate_node.body,
                    local_env,
                    evaluate_annotations=False,
                    deferred_mode=deferred_mode,
                )

    def statement_yield_line(statement: ast.stmt) -> int | None:
        """Return the first yield in a statement without entering nested scopes."""
        first: int | None = None

        class StatementYieldVisitor(ast.NodeVisitor):
            """Find direct yields in one statement."""

            def visit_FunctionDef(self, current: ast.FunctionDef) -> None:
                """Skip nested function definitions."""
                return

            def visit_AsyncFunctionDef(self, current: ast.AsyncFunctionDef) -> None:
                """Skip nested async function definitions."""
                return

            def visit_Lambda(self, current: ast.Lambda) -> None:
                """Skip nested lambda bodies."""
                return

            def visit_ClassDef(self, current: ast.ClassDef) -> None:
                """Skip nested class bodies."""
                return

            def visit_Yield(self, current: ast.Yield) -> None:
                """Record a direct yield expression."""
                nonlocal first
                if first is None or current.lineno < first:
                    first = current.lineno

            def visit_YieldFrom(self, current: ast.YieldFrom) -> None:
                """Record a direct yield-from expression."""
                nonlocal first
                if first is None or current.lineno < first:
                    first = current.lineno

        StatementYieldVisitor().visit(statement)
        return first

    def loop_breaks_before_yield(statements: list[ast.stmt]) -> bool:
        """Return whether an enclosing loop can break before its first yield."""
        first_yield: tuple[int, int] | None = None

        class YieldPositionVisitor(ast.NodeVisitor):
            """Find the first direct yield position in the loop body."""

            def visit_Yield(self, current: ast.Yield) -> None:
                """Record a direct yield position."""
                nonlocal first_yield
                position = (current.lineno, current.col_offset)
                if first_yield is None or position < first_yield:
                    first_yield = position

            def visit_YieldFrom(self, current: ast.YieldFrom) -> None:
                """Record a direct yield-from position."""
                nonlocal first_yield
                position = (current.lineno, current.col_offset)
                if first_yield is None or position < first_yield:
                    first_yield = position

            def visit_For(self, current: ast.For) -> None:
                """Skip nested loop bodies."""
                return

            def visit_AsyncFor(self, current: ast.AsyncFor) -> None:
                """Skip nested async loop bodies."""
                return

            def visit_While(self, current: ast.While) -> None:
                """Skip nested while-loop bodies."""
                return

            def visit_FunctionDef(self, current: ast.FunctionDef) -> None:
                """Skip nested function bodies."""
                return

            def visit_AsyncFunctionDef(self, current: ast.AsyncFunctionDef) -> None:
                """Skip nested async-function bodies."""
                return

            def visit_ClassDef(self, current: ast.ClassDef) -> None:
                """Skip nested class bodies."""
                return

        position_visitor = YieldPositionVisitor()
        for statement in statements:
            position_visitor.visit(statement)
        if first_yield is None:
            return False

        class BreakVisitor(ast.NodeVisitor):
            """Find breaks belonging to the enclosing loop only."""

            found = False

            def visit_Break(self, current: ast.Break) -> None:
                """Record a break before the first yield."""
                if (current.lineno, current.col_offset) < first_yield:
                    self.found = True

            def visit_Continue(self, current: ast.Continue) -> None:
                """Record a continue before the first yield."""
                if (current.lineno, current.col_offset) < first_yield:
                    self.found = True

            def visit_For(self, current: ast.For) -> None:
                """Skip nested for-loop bodies."""
                return

            def visit_AsyncFor(self, current: ast.AsyncFor) -> None:
                """Skip nested async for-loop bodies."""
                return

            def visit_While(self, current: ast.While) -> None:
                """Skip nested while-loop bodies."""
                return

            def visit_FunctionDef(self, current: ast.FunctionDef) -> None:
                """Skip nested function bodies."""
                return

            def visit_AsyncFunctionDef(self, current: ast.AsyncFunctionDef) -> None:
                """Skip nested async-function bodies."""
                return

            def visit_ClassDef(self, current: ast.ClassDef) -> None:
                """Skip nested class bodies."""
                return

        visitor = BreakVisitor()
        for statement in statements:
            visitor.visit(statement)
        return visitor.found

    def yield_value_may_raise(
        statements: list[ast.stmt],
        env: dict[str, AliasValue],
    ) -> bool:
        """Return whether a yield value can raise before producing its first item."""
        class YieldValueVisitor(ast.NodeVisitor):
            """Detect calls and other evaluation operations inside yield values."""

            found = False

            def visit_Yield(self, current: ast.Yield) -> None:
                """Detect potentially raising yield-value evaluation."""
                if current.value is None or isinstance(current.value, ast.Constant):
                    return
                if isinstance(current.value, ast.Name) and current.value.id in env:
                    return
                self.found = True

            def visit_YieldFrom(self, current: ast.YieldFrom) -> None:
                """Treat delegate iteration as potentially raising."""
                self.found = True

            def visit_FunctionDef(self, current: ast.FunctionDef) -> None:
                """Skip nested function bodies."""
                return

            def visit_AsyncFunctionDef(self, current: ast.AsyncFunctionDef) -> None:
                """Skip nested async-function bodies."""
                return

            def visit_ClassDef(self, current: ast.ClassDef) -> None:
                """Skip nested class bodies."""
                return

        visitor = YieldValueVisitor()
        for statement in statements:
            visitor.visit(statement)
        return visitor.found

    def definitely_nonempty_iterable(
        node: ast.AST,
        env: dict[str, AliasValue],
    ) -> bool:
        """Return whether a loop iterable is statically known to execute once."""
        try:
            value = ast.literal_eval(node)
        except (TypeError, ValueError, SyntaxError):
            value = None
        if isinstance(value, (str, bytes, tuple, list, set, frozenset, dict)):
            return bool(value)
        if isinstance(node, ast.Call) and isinstance(node.func, ast.Name) and node.func.id == "range":
            resolved_range = resolve_target(node.func, env)
            builtin_range = False
            for candidate in candidate_values(resolved_range):
                candidate_node = candidate.node if isinstance(candidate, FrozenAlias) else candidate
                if (
                    isinstance(candidate_node, ast.Name)
                    and candidate_node.id == "range"
                    and node.func.id not in env
                ):
                    builtin_range = True
                elif (
                    isinstance(candidate_node, ast.Attribute)
                    and candidate_node.attr == "range"
                    and isinstance(candidate_node.value, ast.Name)
                    and candidate_node.value.id in {"builtins", "__builtins__"}
                ):
                    builtin_range = True
            if not builtin_range:
                return False
            try:
                arguments = tuple(ast.literal_eval(argument) for argument in node.args)
                return bool(range(*arguments))
            except (TypeError, ValueError, SyntaxError):
                return False
        return False

    def scan_prefix_expression(
        node: ast.AST,
        env: dict[str, AliasValue],
        *,
        deferred_mode: str = _DEFERRED_FIRST_YIELD,
    ) -> bool:
        """Delegate prefix-expression scanning to the split scanner."""
        return context.scan_prefix_expression(node, env, deferred_mode=deferred_mode)

    def scan_prefix_statement(
        statement: ast.stmt,
        env: dict[str, AliasValue],
        *,
        evaluate_annotations: bool = True,
        deferred_mode: str = _DEFERRED_FIRST_YIELD,
    ) -> bool:
        """Delegate prefix-statement scanning to the split scanner."""
        return context.scan_prefix_statement(
            statement, env, evaluate_annotations=evaluate_annotations, deferred_mode=deferred_mode
        )

    def scan_prefix_sequence(
        statements: list[ast.stmt],
        env: dict[str, AliasValue],
        *,
        evaluate_annotations: bool = True,
        deferred_mode: str = _DEFERRED_FIRST_YIELD,
    ) -> bool:
        """Delegate prefix-sequence scanning to the split scanner."""
        return context.scan_prefix_sequence(
            statements, env, evaluate_annotations=evaluate_annotations, deferred_mode=deferred_mode
        )

    def scan_node(
        node: ast.AST,
        env: dict[str, AliasValue],
        *,
        deferred_mode: str = _DEFERRED_NONE,
    ) -> None:
        """Delegate node scanning to the split scanner."""
        return context.scan_node(node, env, deferred_mode=deferred_mode)

    def scan_statement(
        statement: ast.stmt,
        env: dict[str, AliasValue],
        *,
        evaluate_annotations: bool = True,
        deferred_mode: str = _DEFERRED_NONE,
    ) -> None:
        """Delegate statement scanning to the split scanner."""
        return context.scan_statement(
            statement,
            env,
            evaluate_annotations=evaluate_annotations,
            deferred_mode=deferred_mode,
        )

    def scan_sequence(
        statements: list[ast.stmt],
        env: dict[str, AliasValue],
        *,
        evaluate_annotations: bool = True,
        deferred_mode: str = _DEFERRED_NONE,
    ) -> None:
        """Delegate sequence scanning to the split scanner."""
        return context.scan_sequence(
            statements,
            env,
            evaluate_annotations=evaluate_annotations,
            deferred_mode=deferred_mode,
        )

    def scan_returned_sequence(
        call: ast.Call,
        env: dict[str, AliasValue],
        deferred_mode: str,
    ) -> None:
        """Delegate returned-sequence scanning to the split scanner."""
        return context.scan_returned_sequence(call, env, deferred_mode)

    def constant_truth(node: ast.AST) -> bool | None:
        """Delegate constant truth evaluation to the split scanner."""
        return context.constant_truth(node)

    def merge_branch_environments(
        env: dict[str, AliasValue],
        branches: tuple[dict[str, AliasValue], ...],
    ) -> None:
        """Delegate branch-environment merging to the split scanner."""
        return context.merge_branch_environments(env, branches)

    from Scripts.validation.source_decomposition.identity_namespace_scanner import scan_namespace
    from types import SimpleNamespace

    context = SimpleNamespace(
        signatures=signatures,
        allowed_public_global_calls=allowed_public_global_calls,
        future_annotations=future_annotations,
        FrozenAlias=FrozenAlias,
        AliasSequence=AliasSequence,
        candidate_values=candidate_values,
        merge_values=merge_values,
        resolve_target=resolve_target,
        risky_target=risky_target,
        driver_mode=driver_mode,
        resolved_callable_names=resolved_callable_names,
        expression_always_truthy=expression_always_truthy,
        combine_deferred_modes=combine_deferred_modes,
        bind_import=bind_import,
        bind_assignment=bind_assignment,
        contains_yield=contains_yield,
        inspect_definition=inspect_definition,
        bind_callable_parameters=bind_callable_parameters,
        record_callable=record_callable,
        statement_yield_line=statement_yield_line,
        loop_breaks_before_yield=loop_breaks_before_yield,
        yield_value_may_raise=yield_value_may_raise,
        definitely_nonempty_iterable=definitely_nonempty_iterable,
        scan_prefix_expression=scan_prefix_expression,
        scan_context_manager_enter=None,
        scan_prefix_statement=scan_prefix_statement,
        scan_prefix_sequence=scan_prefix_sequence,
        namespace_risk_attributes=_NAMESPACE_RISK_ATTRIBUTES,
        namespace_risk_names=_NAMESPACE_RISK_NAMES,
        deferred_none=_DEFERRED_NONE,
        deferred_first_yield=_DEFERRED_FIRST_YIELD,
        deferred_full_iteration=_DEFERRED_FULL_ITERATION,
        deferred_coroutine_completion=_DEFERRED_COROUTINE_COMPLETION,
        scan_node=None,
        scan_statement=None,
        scan_sequence=None,
        scan_returned_sequence=None,
        constant_truth=None,
        merge_branch_environments=None,
    )
    return scan_namespace(context, tree)
