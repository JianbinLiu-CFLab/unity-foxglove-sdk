"""Scanning half of namespace-risk analysis for split identity tooling."""

from __future__ import annotations

import ast


def scan_namespace(context, tree: ast.AST) -> frozenset[str]:
    """Run the statement and expression scanner against shared analyzer state."""
    signatures = context.signatures
    allowed_public_global_calls = context.allowed_public_global_calls
    future_annotations = context.future_annotations
    FrozenAlias = context.FrozenAlias
    AliasSequence = context.AliasSequence
    candidate_values = context.candidate_values
    merge_values = context.merge_values
    resolve_target = context.resolve_target
    risky_target = context.risky_target
    driver_mode = context.driver_mode
    resolved_callable_names = context.resolved_callable_names
    expression_always_truthy = context.expression_always_truthy
    combine_deferred_modes = context.combine_deferred_modes
    bind_import = context.bind_import
    bind_assignment = context.bind_assignment
    contains_yield = context.contains_yield
    inspect_definition = context.inspect_definition
    bind_callable_parameters = context.bind_callable_parameters
    record_callable = context.record_callable
    statement_yield_line = context.statement_yield_line
    loop_breaks_before_yield = context.loop_breaks_before_yield
    yield_value_may_raise = context.yield_value_may_raise
    definitely_nonempty_iterable = context.definitely_nonempty_iterable
    scan_prefix_expression = context.scan_prefix_expression
    scan_context_manager_enter = context.scan_context_manager_enter
    scan_prefix_statement = context.scan_prefix_statement
    scan_prefix_sequence = context.scan_prefix_sequence
    namespace_risk_attributes = context.namespace_risk_attributes
    namespace_risk_names = context.namespace_risk_names
    deferred_none = context.deferred_none
    deferred_first_yield = context.deferred_first_yield
    deferred_full_iteration = context.deferred_full_iteration
    deferred_coroutine_completion = context.deferred_coroutine_completion

    def scan_prefix_expression(
        node: ast.AST,
        env: dict[str, AliasValue],
        *,
        deferred_mode: str = deferred_first_yield,
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
        deferred_mode: str = deferred_first_yield,
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
        deferred_mode: str = deferred_first_yield,
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
        deferred_mode: str = deferred_none,
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
                    else deferred_none
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
            scan_node(node.value, env, deferred_mode=deferred_coroutine_completion)
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
                        deferred_full_iteration
                        if deferred_mode != deferred_none
                        else deferred_none
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
                        deferred_full_iteration
                        if deferred_mode != deferred_none
                        else deferred_none
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
                    deferred_mode=deferred_mode if isinstance(element, ast.Starred) else deferred_none,
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
                            deferred_full_iteration
                            if isinstance(element, ast.Starred)
                            else deferred_none
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
                    if isinstance(node.value, ast.Call) and deferred_mode != deferred_none:
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
                deferred_mode=deferred_mode if has_yield else deferred_none,
            )
            return
        if isinstance(node, ast.Attribute) and node.attr in namespace_risk_attributes:
            signatures.add(ast.dump(node, include_attributes=False))
        elif isinstance(node, ast.Name) and node.id in namespace_risk_names:
            signatures.add(ast.dump(node, include_attributes=False))
        if deferred_mode != deferred_none and isinstance(node, (ast.Name, ast.Attribute)):
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
        deferred_mode: str = deferred_none,
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
                    deferred_mode=deferred_mode if value_has_yield else deferred_none,
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
                deferred_mode=deferred_mode if value_has_yield else deferred_none,
            )
            bind_assignment(statement.target, None, env)
            return
        if isinstance(statement, (ast.For, ast.AsyncFor)):
            scan_node(statement.iter, env, deferred_mode=deferred_full_iteration)
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
            deferred_mode=deferred_mode if statement_has_yield else deferred_none,
        )

    def scan_sequence(
        statements: list[ast.stmt],
        env: dict[str, AliasValue],
        *,
        evaluate_annotations: bool = True,
        deferred_mode: str = deferred_none,
    ) -> None:
        """Scan statements in source order within one lexical scope."""
        for statement in statements:
            scan_statement(
                statement,
                env,
                evaluate_annotations=evaluate_annotations,
                deferred_mode=deferred_mode,
            )

    context.scan_prefix_expression = scan_prefix_expression
    context.scan_prefix_statement = scan_prefix_statement
    context.scan_prefix_sequence = scan_prefix_sequence
    context.scan_node = scan_node
    context.scan_statement = scan_statement
    context.scan_sequence = scan_sequence
    context.scan_returned_sequence = scan_returned_sequence
    context.constant_truth = constant_truth
    context.merge_branch_environments = merge_branch_environments
    if isinstance(tree, ast.Module):
        scan_sequence(tree.body, {})
    else:
        scan_node(tree, {})
    return frozenset(signatures)
