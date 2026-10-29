"""Namespace-risk analysis for split Python identity surfaces."""

from __future__ import annotations

import ast

from Scripts.phase192.identity_surface_conditions import _is_public_globals_comprehension


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
        """Scan an expression until its first direct yield and report whether it stopped."""
        if isinstance(node, (ast.Yield, ast.YieldFrom)):
            if node.value is not None:
                scan_node(
                    node.value,
                    env,
                    deferred_mode=deferred_mode,
                )
            return True
        if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef)):
            inspect_definition(node, env)
            env[node.name] = node
            return False
        if isinstance(node, ast.Lambda):
            inspect_definition(node, env)
            return False
        if isinstance(node, ast.Call) and not any(
            isinstance(child, (ast.Yield, ast.YieldFrom))
            for child in ast.walk(node)
        ):
            scan_node(node, env, deferred_mode=deferred_mode)
            return False
        for child in ast.iter_child_nodes(node):
            if scan_prefix_expression(child, env, deferred_mode=deferred_mode):
                return True
        return False

    def scan_context_manager_enter(
        context_expr: ast.AST,
        env: dict[str, AliasValue],
        deferred_mode: str,
    ) -> None:
        """Scan a statically resolved context manager's enter hook."""
        if not isinstance(context_expr, ast.Call):
            return
        for candidate in candidate_values(resolve_target(context_expr.func, env)):
            class_node = candidate.node if isinstance(candidate, FrozenAlias) else candidate
            if not isinstance(class_node, ast.ClassDef):
                continue
            for member in class_node.body:
                if not isinstance(member, (ast.FunctionDef, ast.AsyncFunctionDef)) or member.name != "__enter__":
                    continue
                local_env = dict(env)
                method_call = ast.Call(func=ast.Name(id="__enter__", ctx=ast.Load()), args=[context_expr], keywords=[])
                bind_callable_parameters(member, method_call, env, local_env)
                if isinstance(member, ast.AsyncFunctionDef):
                    continue
                scan_sequence(
                    member.body,
                    local_env,
                    evaluate_annotations=False,
                    deferred_mode=deferred_mode,
                )

    def scan_prefix_statement(
        statement: ast.stmt,
        env: dict[str, AliasValue],
        *,
        evaluate_annotations: bool = True,
        deferred_mode: str = _DEFERRED_FIRST_YIELD,
    ) -> bool:
        """Scan one statement up to the first yield in a generator prefix."""
        if isinstance(statement, ast.If):
            scan_node(statement.test, env)
            body_env = dict(env)
            else_env = dict(env)
            body_stopped = scan_prefix_sequence(
                statement.body, body_env,
                evaluate_annotations=evaluate_annotations,
                deferred_mode=deferred_mode,
            )
            else_stopped = scan_prefix_sequence(
                statement.orelse, else_env,
                evaluate_annotations=evaluate_annotations,
                deferred_mode=deferred_mode,
            )
            merge_branch_environments(env, (body_env, else_env))
            return body_stopped and else_stopped
        if isinstance(statement, (ast.For, ast.AsyncFor)):
            scan_node(statement.iter, env, deferred_mode=deferred_mode)
            body_stopped = scan_prefix_sequence(
                statement.body, env,
                evaluate_annotations=evaluate_annotations,
                deferred_mode=deferred_mode,
            )
            if (
                body_stopped
                and not loop_breaks_before_yield(statement.body)
                and definitely_nonempty_iterable(statement.iter, env)
            ):
                return True
            if not statement.orelse:
                return False
            return scan_prefix_sequence(
                statement.orelse, env,
                evaluate_annotations=evaluate_annotations,
                deferred_mode=deferred_mode,
            )
        if isinstance(statement, ast.While):
            scan_node(statement.test, env)
            body_stopped = scan_prefix_sequence(
                statement.body, env,
                evaluate_annotations=evaluate_annotations,
                deferred_mode=deferred_mode,
            )
            if (
                body_stopped
                and not loop_breaks_before_yield(statement.body)
                and constant_truth(statement.test) is True
            ):
                return True
            if not statement.orelse:
                return False
            return scan_prefix_sequence(
                statement.orelse, env,
                evaluate_annotations=evaluate_annotations,
                deferred_mode=deferred_mode,
            )
        if isinstance(statement, (ast.With, ast.AsyncWith)):
            for item in statement.items:
                scan_node(item.context_expr, env, deferred_mode=deferred_mode)
                scan_context_manager_enter(item.context_expr, env, deferred_mode)
                if item.optional_vars is not None:
                    bind_assignment(item.optional_vars, None, env)
            return scan_prefix_sequence(
                statement.body, env,
                evaluate_annotations=evaluate_annotations,
                deferred_mode=deferred_mode,
            )
        if isinstance(statement, ast.Try):
            body_stopped = scan_prefix_sequence(
                statement.body, env,
                evaluate_annotations=evaluate_annotations,
                deferred_mode=deferred_mode,
            )
            if body_stopped:
                if yield_value_may_raise(statement.body, env):
                    for handler in statement.handlers:
                        scan_prefix_sequence(
                            handler.body,
                            dict(env),
                            evaluate_annotations=evaluate_annotations,
                            deferred_mode=deferred_mode,
                        )
                if statement.finalbody:
                    scan_prefix_sequence(
                        statement.finalbody,
                        env,
                        evaluate_annotations=evaluate_annotations,
                        deferred_mode=deferred_mode,
                    )
                return True
            handlers_stopped = True
            for handler in statement.handlers:
                handlers_stopped = scan_prefix_sequence(
                    handler.body, dict(env),
                    evaluate_annotations=evaluate_annotations,
                    deferred_mode=deferred_mode,
                ) and handlers_stopped
            orelse_stopped = (
                scan_prefix_sequence(
                    statement.orelse, env,
                    evaluate_annotations=evaluate_annotations,
                    deferred_mode=deferred_mode,
                )
                if statement.orelse
                else True
            )
            final_stopped = (
                scan_prefix_sequence(
                    statement.finalbody, env,
                    evaluate_annotations=evaluate_annotations,
                    deferred_mode=deferred_mode,
                )
                if statement.finalbody
                else True
            )
            return body_stopped and handlers_stopped and orelse_stopped and final_stopped
        if isinstance(statement, ast.Assign):
            value = resolve_target(statement.value, env)
            stopped = scan_prefix_expression(
                statement.value, env, deferred_mode=deferred_mode
            )
            if not stopped:
                for target in statement.targets:
                    bind_assignment(target, value, env)
            return stopped
        if isinstance(statement, ast.AnnAssign):
            if statement.annotation is not None and evaluate_annotations and not future_annotations:
                if scan_prefix_expression(
                    statement.annotation, env, deferred_mode=deferred_mode
                ):
                    return True
            if statement.value is None:
                bind_assignment(statement.target, None, env)
                return False
            value = resolve_target(statement.value, env)
            stopped = scan_prefix_expression(
                statement.value, env, deferred_mode=deferred_mode
            )
            if not stopped:
                bind_assignment(statement.target, value, env)
            return stopped
        if isinstance(statement, ast.Expr):
            return scan_prefix_expression(
                statement.value, env, deferred_mode=deferred_mode
            )
        for child in ast.iter_child_nodes(statement):
            if scan_prefix_expression(child, env, deferred_mode=deferred_mode):
                return True
        return False

    def scan_prefix_sequence(
        statements: list[ast.stmt],
        env: dict[str, AliasValue],
        *,
        evaluate_annotations: bool = True,
        deferred_mode: str = _DEFERRED_FIRST_YIELD,
    ) -> bool:
        """Scan statements in order until every path reaches a first yield."""
        for statement in statements:
            if statement_yield_line(statement) is None:
                scan_statement(
                    statement,
                    env,
                    evaluate_annotations=evaluate_annotations,
                    deferred_mode=deferred_mode,
                )
                continue
            stopped = scan_prefix_statement(
                statement, env,
                evaluate_annotations=evaluate_annotations,
                deferred_mode=deferred_mode,
            )
            if stopped:
                return True
        return False

    def scan_node(
        node: ast.AST,
        env: dict[str, AliasValue],
        *,
        deferred_mode: str = _DEFERRED_NONE,
    ) -> None:
        """Scan an expression or definition without entering deferred function bodies."""
        if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef)):
            inspect_definition(node, env)
            env[node.name] = node
            return
        if isinstance(node, ast.Lambda):
            inspect_definition(node, env)
            return
        if isinstance(node, ast.YieldFrom):
            if node.value is not None:
                scan_node(node.value, env, deferred_mode=deferred_mode)
            return
        if isinstance(node, ast.Yield):
            if node.value is not None:
                scan_node(node.value, env, deferred_mode=deferred_mode)
            return
        if isinstance(node, ast.Call):
            if id(node) in allowed_public_global_calls:
                return
            if risky_target(node.func, env):
                signatures.add(ast.dump(node, include_attributes=False))
            else:
                scan_node(node.func, env)
            record_callable(node.func, env, node, deferred_mode=deferred_mode)
            nested_mode = combine_deferred_modes(deferred_mode, driver_mode(node.func, env))
            callable_names = resolved_callable_names(node.func, env)
            driven_indices = {0}
            if "map" in callable_names:
                driven_indices = set(range(1, len(node.args)))
            elif "filter" in callable_names:
                driven_indices = {1} if len(node.args) > 1 else set()
            elif "zip" in callable_names:
                driven_indices = set(range(len(node.args)))
            for index, argument in enumerate(node.args):
                argument_mode = (
                    nested_mode
                    if index in driven_indices
                    and isinstance(
                        argument,
                        (
                            ast.Call,
                            ast.Name,
                            ast.Attribute,
                            ast.GeneratorExp,
                            ast.YieldFrom,
                            ast.IfExp,
                            ast.BoolOp,
                            ast.NamedExpr,
                            ast.Subscript,
                            ast.Starred,
                            ast.List,
                            ast.Tuple,
                            ast.Set,
                            ast.Dict,
                            ast.ListComp,
                            ast.SetComp,
                            ast.DictComp,
                        ),
                    )
                    else _DEFERRED_NONE
                )
                scan_node(
                    argument,
                    env,
                    deferred_mode=argument_mode,
                )
            for keyword in node.keywords:
                scan_node(keyword.value, env)
            return
        if isinstance(node, ast.Await):
            scan_node(node.value, env, deferred_mode=_DEFERRED_COROUTINE_COMPLETION)
            return
        if isinstance(node, ast.GeneratorExp):
            scan_node(node.elt, env)
            for generator in node.generators:
                scan_node(generator.iter, env, deferred_mode=deferred_mode)
                for condition in generator.ifs:
                    scan_node(condition, env)
            return
        if isinstance(node, (ast.ListComp, ast.SetComp)):
            scan_node(node.elt, env)
            for generator in node.generators:
                scan_node(
                    generator.iter,
                    env,
                    deferred_mode=(
                        _DEFERRED_FULL_ITERATION
                        if deferred_mode != _DEFERRED_NONE
                        else _DEFERRED_NONE
                    ),
                )
                for condition in generator.ifs:
                    scan_node(condition, env)
            return
        if isinstance(node, ast.DictComp):
            scan_node(node.key, env)
            scan_node(node.value, env)
            for generator in node.generators:
                scan_node(
                    generator.iter,
                    env,
                    deferred_mode=(
                        _DEFERRED_FULL_ITERATION
                        if deferred_mode != _DEFERRED_NONE
                        else _DEFERRED_NONE
                    ),
                )
                for condition in generator.ifs:
                    scan_node(condition, env)
            return
        if isinstance(node, ast.IfExp):
            scan_node(node.test, env)
            truth = constant_truth(node.test)
            if truth is True:
                scan_node(node.body, env, deferred_mode=deferred_mode)
            elif truth is False:
                scan_node(node.orelse, env, deferred_mode=deferred_mode)
            else:
                scan_node(node.body, env, deferred_mode=deferred_mode)
                scan_node(node.orelse, env, deferred_mode=deferred_mode)
            return
        if isinstance(node, ast.BoolOp):
            if isinstance(node.op, ast.And):
                for value in node.values[:-1]:
                    scan_node(value, env)
                    if constant_truth(value) is False:
                        return
                if node.values:
                    scan_node(node.values[-1], env, deferred_mode=deferred_mode)
                return
            for value in node.values:
                truth = constant_truth(value)
                if truth is False:
                    scan_node(value, env)
                    continue
                scan_node(value, env, deferred_mode=deferred_mode)
                if truth is True or expression_always_truthy(value, env):
                    return
            if node.values:
                scan_node(node.values[-1], env, deferred_mode=deferred_mode)
            return
        if isinstance(node, ast.NamedExpr):
            value = resolve_target(node.value, env)
            scan_node(node.value, env, deferred_mode=deferred_mode)
            bind_assignment(node.target, value, env)
            return
        if isinstance(node, ast.Starred):
            scan_node(node.value, env, deferred_mode=deferred_mode)
            return
        if isinstance(node, (ast.List, ast.Tuple, ast.Set)):
            for element in node.elts:
                scan_node(
                    element,
                    env,
                    deferred_mode=deferred_mode if isinstance(element, ast.Starred) else _DEFERRED_NONE,
                )
            return
        if isinstance(node, ast.Subscript):
            literal_container = isinstance(node.value, (ast.List, ast.Tuple, ast.Set))
            if literal_container:
                for element in node.value.elts:
                    scan_node(
                        element.value if isinstance(element, ast.Starred) else element,
                        env,
                        deferred_mode=(
                            _DEFERRED_FULL_ITERATION
                            if isinstance(element, ast.Starred)
                            else _DEFERRED_NONE
                        ),
                    )
            resolved = resolve_target(node, env)
            if resolved is not node:
                for candidate in candidate_values(resolved):
                    resolved_node = candidate.node if isinstance(candidate, FrozenAlias) else candidate
                    if isinstance(resolved_node, ast.AST):
                        scan_node(resolved_node, env, deferred_mode=deferred_mode)
            else:
                base = resolve_target(node.value, env)
                sequence_candidates = [
                    item
                    for candidate in candidate_values(base)
                    if isinstance(candidate, AliasSequence)
                    for item in candidate.items
                ]
                if sequence_candidates:
                    for item in sequence_candidates:
                        scan_node(item, env, deferred_mode=deferred_mode)
                else:
                    if not literal_container:
                        scan_node(node.value, env)
                    if isinstance(node.value, ast.Call) and deferred_mode != _DEFERRED_NONE:
                        scan_returned_sequence(node.value, env, deferred_mode)
            scan_node(node.slice, env)
            return
        if isinstance(node, ast.Expr):
            has_yield = any(
                isinstance(candidate, (ast.Yield, ast.YieldFrom))
                for candidate in ast.walk(node.value)
            )
            scan_node(
                node.value,
                env,
                deferred_mode=deferred_mode if has_yield else _DEFERRED_NONE,
            )
            return
        if isinstance(node, ast.Attribute) and node.attr in _NAMESPACE_RISK_ATTRIBUTES:
            signatures.add(ast.dump(node, include_attributes=False))
        elif isinstance(node, ast.Name) and node.id in _NAMESPACE_RISK_NAMES:
            signatures.add(ast.dump(node, include_attributes=False))
        if deferred_mode != _DEFERRED_NONE and isinstance(node, (ast.Name, ast.Attribute)):
            resolved = resolve_target(node, env)
            for candidate in candidate_values(resolved):
                candidate_node = candidate.node if isinstance(candidate, FrozenAlias) else candidate
                if isinstance(candidate_node, ast.GeneratorExp):
                    scan_node(candidate_node, env, deferred_mode=deferred_mode)
                    return
                if isinstance(candidate_node, ast.Call):
                    target = resolve_target(candidate_node.func, env)
                    if any(
                        isinstance(function, (ast.FunctionDef, ast.AsyncFunctionDef))
                        and contains_yield(function)
                        for function in candidate_values(target)
                    ):
                        scan_node(candidate_node, env, deferred_mode=deferred_mode)
                        return
        for child in ast.iter_child_nodes(node):
            scan_node(child, env)

    def constant_truth(node: ast.AST) -> bool | None:
        """Evaluate literal branch conditions without executing arbitrary code."""
        try:
            return bool(ast.literal_eval(node))
        except (TypeError, ValueError, SyntaxError):
            return None

    def merge_branch_environments(
        env: dict[str, AliasValue],
        branches: tuple[dict[str, AliasValue], ...],
    ) -> None:
        """Keep every binding that can reach the next statement."""
        names = set(env)
        for branch in branches:
            names.update(branch)
        for name in names:
            env[name] = merge_values(*(branch.get(name) for branch in branches))

    def scan_returned_sequence(
        call: ast.Call,
        env: dict[str, AliasValue],
        deferred_mode: str,
    ) -> None:
        """Scan generator elements selected from a returned literal sequence."""
        for candidate in candidate_values(resolve_target(call.func, env)):
            function = candidate.node if isinstance(candidate, FrozenAlias) else candidate
            if not isinstance(function, (ast.FunctionDef, ast.AsyncFunctionDef)):
                continue
            local_env = dict(env)
            bind_callable_parameters(function, call, env, local_env)
            for statement in function.body:
                if not isinstance(statement, ast.Return) or statement.value is None:
                    continue
                value = statement.value
                if isinstance(value, (ast.List, ast.Tuple, ast.Set)):
                    for element in value.elts:
                        scan_node(element, local_env, deferred_mode=deferred_mode)
                else:
                    scan_node(value, local_env, deferred_mode=deferred_mode)

    def scan_statement(
        statement: ast.stmt,
        env: dict[str, AliasValue],
        *,
        evaluate_annotations: bool = True,
        deferred_mode: str = _DEFERRED_NONE,
    ) -> None:
        """Scan one statement in execution order and update its scope bindings."""
        if isinstance(statement, (ast.FunctionDef, ast.AsyncFunctionDef)):
            inspect_definition(statement, env)
            env[statement.name] = statement
            return
        if isinstance(statement, ast.ClassDef):
            for decorator in statement.decorator_list:
                scan_node(decorator, env)
                if not isinstance(decorator, ast.Call):
                    decorator_call = ast.Call(func=decorator, args=[statement], keywords=[])
                    if risky_target(decorator, env):
                        signatures.add(ast.dump(decorator_call, include_attributes=False))
                    record_callable(decorator, env, decorator_call)
            for base in statement.bases:
                scan_node(base, env)
            for keyword in statement.keywords:
                scan_node(keyword.value, env)
            class_env = dict(env)
            scan_sequence(
                statement.body,
                class_env,
                evaluate_annotations=True,
                deferred_mode=deferred_mode,
            )
            env[statement.name] = statement
            return
        if isinstance(statement, (ast.Import, ast.ImportFrom)):
            if isinstance(statement, ast.ImportFrom) and (
                (statement.module or "").split(".", 1)[0] in {"builtins", "__builtins__", "__builtin__"}
                or any(alias.name in {"modules", "getattr", "setattr", "delattr", "import_module", "__import__"} for alias in statement.names)
            ):
                signatures.add(ast.dump(statement, include_attributes=False))
            bind_import(statement, env)
            return
        if isinstance(statement, (ast.Assign, ast.AnnAssign, ast.NamedExpr)):
            value = None
            if (
                isinstance(statement, ast.AnnAssign)
                and statement.annotation is not None
                and evaluate_annotations
                and not future_annotations
            ):
                scan_node(statement.annotation, env)
            if statement.value is not None:
                value = resolve_target(statement.value, env)
                value_has_yield = any(
                    isinstance(candidate, (ast.Yield, ast.YieldFrom))
                    for candidate in ast.walk(statement.value)
                )
                scan_node(
                    statement.value,
                    env,
                    deferred_mode=deferred_mode if value_has_yield else _DEFERRED_NONE,
                )
            targets = statement.targets if isinstance(statement, ast.Assign) else (statement.target,)
            for target in targets:
                bind_assignment(target, value, env)
            return
        if isinstance(statement, ast.AugAssign):
            value_has_yield = any(
                isinstance(candidate, (ast.Yield, ast.YieldFrom))
                for candidate in ast.walk(statement.value)
            )
            scan_node(
                statement.value,
                env,
                deferred_mode=deferred_mode if value_has_yield else _DEFERRED_NONE,
            )
            bind_assignment(statement.target, None, env)
            return
        if isinstance(statement, (ast.For, ast.AsyncFor)):
            scan_node(statement.iter, env, deferred_mode=_DEFERRED_FULL_ITERATION)
            scan_sequence(
                statement.body,
                env,
                evaluate_annotations=evaluate_annotations,
                deferred_mode=deferred_mode,
            )
            scan_sequence(
                statement.orelse,
                env,
                evaluate_annotations=evaluate_annotations,
                deferred_mode=deferred_mode,
            )
            return
        if isinstance(statement, (ast.If, ast.While)):
            scan_node(statement.test, env)
            truth = constant_truth(statement.test)
            if truth is True:
                scan_sequence(
                    statement.body,
                    env,
                    evaluate_annotations=evaluate_annotations,
                    deferred_mode=deferred_mode,
                )
                return
            if truth is False:
                scan_sequence(
                    statement.orelse,
                    env,
                    evaluate_annotations=evaluate_annotations,
                    deferred_mode=deferred_mode,
                )
                return
            body_env = dict(env)
            else_env = dict(env)
            scan_sequence(
                statement.body,
                body_env,
                evaluate_annotations=evaluate_annotations,
                deferred_mode=deferred_mode,
            )
            scan_sequence(
                statement.orelse,
                else_env,
                evaluate_annotations=evaluate_annotations,
                deferred_mode=deferred_mode,
            )
            merge_branch_environments(env, (body_env, else_env))
            return
        if isinstance(statement, (ast.With, ast.AsyncWith)):
            for item in statement.items:
                scan_node(item.context_expr, env)
                if item.optional_vars is not None:
                    bind_assignment(item.optional_vars, None, env)
            scan_sequence(
                statement.body,
                env,
                evaluate_annotations=evaluate_annotations,
                deferred_mode=deferred_mode,
            )
            return
        if isinstance(statement, ast.Try):
            scan_sequence(
                statement.body,
                env,
                evaluate_annotations=evaluate_annotations,
                deferred_mode=deferred_mode,
            )
            for handler in statement.handlers:
                scan_sequence(
                    handler.body,
                    dict(env),
                    evaluate_annotations=evaluate_annotations,
                    deferred_mode=deferred_mode,
                )
            scan_sequence(
                statement.orelse,
                env,
                evaluate_annotations=evaluate_annotations,
                deferred_mode=deferred_mode,
            )
            scan_sequence(
                statement.finalbody,
                env,
                evaluate_annotations=evaluate_annotations,
                deferred_mode=deferred_mode,
            )
            return
        if isinstance(statement, ast.Global):
            signatures.add(ast.dump(statement, include_attributes=False))
        statement_has_yield = any(
            isinstance(candidate, (ast.Yield, ast.YieldFrom))
            for candidate in ast.walk(statement)
        )
        scan_node(
            statement,
            env,
            deferred_mode=deferred_mode if statement_has_yield else _DEFERRED_NONE,
        )

    def scan_sequence(
        statements: list[ast.stmt],
        env: dict[str, AliasValue],
        *,
        evaluate_annotations: bool = True,
        deferred_mode: str = _DEFERRED_NONE,
    ) -> None:
        """Scan statements in source order within one lexical scope."""
        for statement in statements:
            scan_statement(
                statement,
                env,
                evaluate_annotations=evaluate_annotations,
                deferred_mode=deferred_mode,
            )

    if isinstance(tree, ast.Module):
        scan_sequence(tree.body, {})
    else:
        scan_node(tree, {})
    return frozenset(signatures)
