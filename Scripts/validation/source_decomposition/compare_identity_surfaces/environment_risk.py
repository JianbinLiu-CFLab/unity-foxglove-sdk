"""Analyze import-time environment execution semantics."""
from __future__ import annotations

import ast
import operator

_FUNCTION_BODY_RISK_NAMES = frozenset({"__import__", "compile", "eval", "exec", "globals", "locals", "vars"})
_ENVIRONMENT_MUTATORS = frozenset({"clear", "pop", "popitem", "setdefault", "update"})
_ENVIRONMENT_FUNCTIONS = frozenset({"putenv", "unsetenv"})
_TRY_TYPES = (ast.Try, ast.TryStar) if hasattr(ast, "TryStar") else (ast.Try,)
__all__ = []
_DEFERRED_CONSUMERS = frozenset({"all", "any", "bytearray", "bytes", "dict", "frozenset", "list", "max", "min", "next", "set", "sorted", "sum", "tuple"})
_DEFERRED_PRODUCERS = frozenset({"enumerate", "filter", "islice", "iter", "map", "zip"})


def _static_truth(node: ast.AST) -> bool | None:
    """Return a statically known truth value for simple literals."""
    if isinstance(node, ast.Constant):
        return bool(node.value)
    if isinstance(node, (ast.List, ast.Tuple, ast.Set)):
        return bool(node.elts)
    return None


def _static_cardinality(node: ast.AST) -> int | None:
    """Return a known element count for simple literal or zip expressions."""
    if isinstance(node, (ast.List, ast.Tuple, ast.Set)):
        return len(node.elts)
    if isinstance(node, ast.GeneratorExp) and node.generators:
        return 0 if _static_cardinality(node.generators[0].iter) == 0 else None
    if isinstance(node, ast.Call) and isinstance(node.func, ast.Name) and node.func.id == "zip":
        counts = [_static_cardinality(argument) for argument in node.args]
        return min(counts) if counts and all(count is not None for count in counts) else 0 if any(count == 0 for count in counts) else None
    if isinstance(node, ast.Call) and isinstance(node.func, ast.Name) and node.func.id == "range":
        try:
            values = tuple(ast.literal_eval(argument) for argument in node.args)
            return len(range(*values)) if all(isinstance(value, int) for value in values) else None
        except (TypeError, ValueError, SyntaxError):
            return None
    return None


def _reachable_calls(root: ast.FunctionDef | ast.AsyncFunctionDef) -> list[ast.Call]:
    """Collect calls from reachable statement paths while skipping nested definitions."""
    calls: list[ast.Call] = []

    def walk_expression(node: ast.AST) -> None:
        """Collect calls in one expression without descending into nested bodies."""
        if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef)):
            for expression in (*node.decorator_list, *node.args.defaults, *node.args.kw_defaults):
                if expression is not None:
                    walk_expression(expression)
            return
        if isinstance(node, ast.ClassDef):
            for expression in (*node.decorator_list, *node.bases, *(keyword.value for keyword in node.keywords)):
                walk_expression(expression)
            return
        if isinstance(node, ast.Lambda):
            for expression in (*node.args.defaults, *node.args.kw_defaults):
                if expression is not None:
                    walk_expression(expression)
            return
        if isinstance(node, ast.Call):
            calls.append(node)
        for child in ast.iter_child_nodes(node):
            walk_expression(child)

    def walk_statements(statements: list[ast.stmt]) -> bool:
        """Collect calls from a statement block and report whether it can fall through."""
        normal = True
        for statement in statements:
            if not normal:
                break
            if isinstance(statement, ast.If):
                walk_expression(statement.test)
                truth = _static_truth(statement.test)
                if truth is True:
                    normal = walk_statements(statement.body)
                elif truth is False:
                    normal = walk_statements(statement.orelse)
                else:
                    normal = walk_statements(statement.body) or walk_statements(statement.orelse)
                continue
            if isinstance(statement, (ast.For, ast.AsyncFor, ast.While)):
                is_for = isinstance(statement, (ast.For, ast.AsyncFor))
                iterator = statement.iter if is_for else statement.test
                walk_expression(iterator)
                empty = _static_cardinality(iterator) == 0 if is_for else _static_truth(iterator) is False
                if empty:
                    normal = walk_statements(statement.orelse)
                else:
                    walk_statements(statement.body)
                    walk_statements(statement.orelse)
                continue
            walk_expression(statement)
            if isinstance(statement, (ast.Return, ast.Raise, ast.Break, ast.Continue)):
                normal = False
        return normal

    walk_statements(root.body)
    return calls


def _producer_callback_parameters(
    root: ast.FunctionDef | ast.AsyncFunctionDef,
    calls: list[ast.Call],
    parameters: set[str],
) -> set[str]:
    """Find callback parameters executed by drained map/filter producers."""
    producer_callbacks: dict[str, str] = {}
    eager = set()
    for call in calls:
        if not ((isinstance(call.func, ast.Name) and call.func.id in {"map", "filter"}) or (isinstance(call.func, ast.Attribute) and call.func.attr == "starmap")) or not call.args:
            continue
        callback = call.args[0]
        if not isinstance(callback, ast.Name) or callback.id not in parameters:
            continue
        if any(_static_cardinality(source) == 0 for source in call.args[1:]):
            continue
        for outer in calls:
            if outer is call or not isinstance(outer.func, ast.Name) or outer.func.id not in _DEFERRED_CONSUMERS:
                continue
            if any(child is call for child in ast.walk(outer)):
                eager.add(callback.id)
        for assignment in ast.walk(root):
            if not isinstance(assignment, ast.Assign) or assignment.value is not call:
                continue
            for target in assignment.targets:
                if isinstance(target, ast.Name):
                    producer_callbacks[target.id] = callback.id
    for call in calls:
        if not isinstance(call.func, ast.Name) or call.func.id not in _DEFERRED_CONSUMERS:
            continue
        for argument in call.args:
            if isinstance(argument, ast.Name):
                callback = producer_callbacks.get(argument.id)
                if callback is not None:
                    eager.add(callback)
    return eager

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
    deferred_consumers = _DEFERRED_CONSUMERS
    deferred_producers = _DEFERRED_PRODUCERS
    def is_environ(node: ast.AST) -> bool:
        """Return whether an expression names os.environ."""
        if isinstance(node, ast.Name):
            return node.id in environ_names
        return isinstance(node, ast.Attribute) and node.attr == "environ" and isinstance(node.value, ast.Name) and node.value.id in os_names
    future_annotations = any(isinstance(node, ast.ImportFrom) and node.module == "__future__" and any(alias.name == "annotations" for alias in node.names) for node in getattr(tree, "body", ()))
    class EnvironmentWriteVisitor(ast.NodeVisitor):
        """Find environment writes that execute in the current expression."""
        def __init__(self) -> None:
            """Initialize deferred-expression execution state."""
            self.found = False
            self.executing_deferred: ast.AST | None = None
            self.deferred_bindings: dict[str, ast.AST] = {}
            self.consumer_aliases: dict[str, str] = {}
            self.producer_aliases: set[str] = set(deferred_producers)
            self.object_aliases: dict[str, set[str]] = {}
            self.eager_callbacks: set[str] = set()
            self.eager_callback_specs: dict[str, tuple[ast.arguments, tuple[str, ...]]] = {}
            self.flow = "normal"
        def _check_targets(self, targets: list[ast.AST]) -> None:
            """Check assignment targets for direct environment mutation."""
            if any(isinstance(getattr(candidate, "ctx", None), (ast.Store, ast.Del)) and (is_environ(candidate) or (isinstance(candidate, ast.Subscript) and is_environ(candidate.value))) for target in targets for candidate in ast.walk(target)):
                self.found = True
        def visit_Assign(self, node: ast.Assign) -> None:
            """Visit assignment targets and their eagerly evaluated values."""
            self._check_targets(node.targets)
            for target in node.targets: self.visit(target)
            if any(isinstance(target, (ast.Tuple, ast.List)) for target in node.targets):
                deferred = None if isinstance(node.value, (ast.Tuple, ast.List, ast.Set)) else self._deferred_value(node.value)
                if isinstance(deferred, ast.GeneratorExp) or self._is_producer(deferred):
                    self._visit_deferred(node.value)
                else:
                    self.visit(node.value)
            else:
                self.visit(node.value)
            for target in node.targets: self._bind_deferred(target, node.value)
        def visit_AugAssign(self, node: ast.AugAssign) -> None:
            """Visit augmented assignment targets and values."""
            self._check_targets([node.target])
            self.visit(node.target)
            self.visit(node.value)
            self._discard_deferred(node.target)
        def visit_AnnAssign(self, node: ast.AnnAssign) -> None:
            """Visit annotated assignments with postponed-annotation semantics."""
            self._check_targets([node.target])
            self.visit(node.target)
            if node.value is not None: (self.visit(node.value), self._bind_deferred(node.target, node.value))
            else: self._discard_deferred(node.target)
            if node.annotation is not None and not future_annotations: self.visit(node.annotation)
        def visit_Delete(self, node: ast.Delete) -> None:
            """Visit deleted environment targets."""
            self._check_targets(node.targets)
            for target in node.targets: self.visit(target) or self._discard_deferred(target)
        def visit_Call(self, node: ast.Call) -> None:
            """Visit calls while executing only proven deferred callbacks."""
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
            if isinstance(node.func, ast.Lambda) or self._deferred_value(node.func) is not None:
                self._visit_deferred(node.func)
            else:
                self.visit(node.func)
            consumer_kind = self._consumer_kind(node.func)
            producer_consumed = self.executing_deferred is node and self._is_producer(node)
            if producer_consumed:
                if self._producer_is_empty(node):
                    for argument in (*node.args, *(keyword.value for keyword in node.keywords)): self.visit(argument)
                else:
                    for argument in (*node.args, *(keyword.value for keyword in node.keywords)): self._visit_consumable(argument)
            elif consumer_kind is not None:
                if consumer_kind == "chain_from_iterable" and node.args:
                    nested = node.args[0].elts if isinstance(node.args[0], (ast.Tuple, ast.List, ast.Set)) else (node.args[0],)
                    for element in nested: self._visit_consumable(element)
                    return
                if consumer_kind in {"all", "any"} and node.args:
                    self._visit_short_circuit(node.args[0], consumer_kind)
                    for argument in node.args[1:]: self.visit(argument)
                    for keyword in node.keywords: self.visit(keyword.value)
                    return
                if consumer_kind == "next" and node.args:
                    (self._visit_short_circuit(node.args[0], consumer_kind), tuple(self.visit(argument) for argument in (*node.args[1:], *(keyword.value for keyword in node.keywords))))
                    return
                if consumer_kind == "callback" and isinstance(node.func, ast.Name):
                    self._visit_callback_defaults(node)
                if consumer_kind == "reduce" and len(node.args) > 1 and not (isinstance(node.args[1], (ast.List, ast.Tuple, ast.Set)) and len(node.args[1].elts) < 2): self._visit_deferred(node.args[0])
                if consumer_kind == "callback":
                    consumed = self._callback_consumed_positions(node)
                else:
                    consumed = set(range(len(node.args))) if consumer_kind in {"chain", "container_mutator", "itertools_materializer"} else ({1} if consumer_kind == "reduce" else {0})
                for index, argument in enumerate(node.args):
                    if index in consumed:
                        self._visit_consumable(argument)
                    else:
                        self.visit(argument)
                for keyword in node.keywords:
                    if keyword.arg == "key" and consumer_kind in {"max", "min", "sorted"} and not (
                        node.args and isinstance(node.args[0], (ast.List, ast.Tuple, ast.Set)) and not node.args[0].elts
                    ):
                        self._visit_consumable(keyword.value)
                    elif (
                        (consumer_kind == "dict.fromkeys" and keyword.arg == "iterable")
                        or (consumer_kind in {"bytes", "bytearray"} and keyword.arg == "source")
                        or (consumer_kind == "callback" and isinstance(node.func, ast.Name) and self._callback_parameter_is_eager(node.func.id, keyword.arg))
                    ):
                        self._visit_consumable(keyword.value)
                    else:
                        self.visit(keyword.value)
            else:
                for argument in (*node.args, *(keyword.value for keyword in node.keywords)): self.visit(argument)
        def _visit_short_circuit(self, node: ast.AST, consumer_kind: str) -> None:
            """Visit a simple all/any generator until a constant result is decisive."""
            if not isinstance(node, ast.GeneratorExp) or len(node.generators) != 1: self._visit_deferred(node); return
            generator = node.generators[0]
            if not isinstance(generator.iter, (ast.List, ast.Tuple, ast.Set)) or not isinstance(generator.target, ast.Name): self._visit_deferred(node); return
            operations = {ast.Eq: operator.eq, ast.NotEq: operator.ne, ast.Lt: operator.lt, ast.LtE: operator.le, ast.Gt: operator.gt, ast.GtE: operator.ge}
            (self.visit(generator.iter), tuple(self.visit(condition) for condition in generator.ifs))
            for element in generator.iter.elts[:1] if consumer_kind == "next" else generator.iter.elts:
                decisive = None
                if isinstance(node.elt, ast.IfExp) and isinstance(element, ast.Constant):
                    test = node.elt.test
                    if isinstance(test, ast.Compare) and len(test.ops) == 1 and isinstance(test.left, ast.Name) and test.left.id == generator.target.id and isinstance(test.comparators[0], ast.Constant):
                        condition = operations.get(type(test.ops[0]))
                        if condition is not None:
                            chosen = node.elt.body if condition(element.value, test.comparators[0].value) else node.elt.orelse
                            self.visit(chosen)
                            if isinstance(chosen, ast.Constant): decisive = bool(chosen.value)
                elif isinstance(node.elt, ast.Constant):
                    decisive = bool(node.elt.value)
                    self.visit(node.elt)
                else:
                    self.visit(node.elt); return
                if consumer_kind == "next" or (decisive is not None and ((consumer_kind == "all" and not decisive) or (consumer_kind == "any" and decisive))):
                    return
        def visit_FunctionDef(self, node: ast.FunctionDef) -> None:
            """Visit function definition expressions without entering its body."""
            for decorator in node.decorator_list: self.visit(decorator)
            for expression in (*node.args.defaults, *node.args.kw_defaults): self.visit(expression) if expression is not None else None
            if not future_annotations:
                annotations = [argument.annotation for argument in (*getattr(node.args, "posonlyargs", ()), *node.args.args, *node.args.kwonlyargs)]
                annotations.extend(argument.annotation for argument in (node.args.vararg, node.args.kwarg) if argument is not None)
                annotations.append(node.returns)
                for annotation in annotations:
                    if annotation is not None: self.visit(annotation)
            self._discard_name(node.name)
        def visit_Import(self, node: ast.Import) -> None:
            """Invalidate deferred bindings shadowed by imported modules."""
            for alias in node.names:
                (name := alias.asname or alias.name.split(".", 1)[0], self._discard_name(name))
                if alias.name in {"itertools", "functools", "collections"}: self.consumer_aliases[name] = f"__{alias.name}__"
        def visit_ImportFrom(self, node: ast.ImportFrom) -> None:
            """Invalidate deferred bindings shadowed by imported names."""
            for alias in node.names:
                if alias.name == "*":
                    (self.deferred_bindings.clear(), self.consumer_aliases.clear(), self.producer_aliases.clear(), self.producer_aliases.update(deferred_producers), self.eager_callbacks.clear(), self.eager_callback_specs.clear())
                    continue
                name = alias.asname or alias.name
                self._discard_name(name)
                if node.module == "builtins" and alias.name in deferred_consumers: self.consumer_aliases[name] = alias.name
                if node.module == "builtins" and alias.name in deferred_producers: self.producer_aliases.add(name)
                if node.module in {"itertools", "functools", "collections"} and alias.name in {"chain", "reduce", "deque"}: self.consumer_aliases[name] = alias.name
                if node.module == "itertools" and alias.name in {"combinations", "permutations", "product"}: self.consumer_aliases[name] = "itertools_materializer"
                if node.module == "itertools" and alias.name in {"filterfalse", "islice", "takewhile", "dropwhile", "groupby", "starmap"}: self.producer_aliases.add(name)
        def visit_Lambda(self, node: ast.Lambda) -> None:
            """Visit lambda defaults and execute its body only when called."""
            for expression in (*node.args.defaults, *node.args.kw_defaults): self.visit(expression) if expression is not None else None
            if self.executing_deferred is node: self.visit(node.body)
        def visit_ClassDef(self, node: ast.ClassDef) -> None:
            """Visit class definition expressions and its executing body."""
            for decorator in node.decorator_list: self.visit(decorator)
            for base in node.bases: self.visit(base)
            for keyword in node.keywords: self.visit(keyword.value)
            previous_bindings, previous_consumers, previous_producers, previous_objects = self.deferred_bindings, self.consumer_aliases, self.producer_aliases, self.object_aliases
            self.deferred_bindings, self.consumer_aliases, self.producer_aliases, self.object_aliases = dict(previous_bindings), dict(previous_consumers), set(previous_producers), {key: set(value) for key, value in previous_objects.items()}
            try:
                for statement in node.body: self.visit(statement)
            finally:
                self.deferred_bindings, self.consumer_aliases, self.producer_aliases, self.object_aliases = previous_bindings, previous_consumers, previous_producers, previous_objects
            self._discard_name(node.name)
        def visit_GeneratorExp(self, node: ast.GeneratorExp) -> None:
            """Visit the eager outer iterable or the consumed generator body."""
            if self.executing_deferred is node: self._visit_comprehension(node)
            elif node.generators: self.visit(node.generators[0].iter)
        def visit_With(self, node: ast.With) -> None:
            """Visit context expressions and binding targets in execution order."""
            for item in node.items:
                self.visit(item.context_expr)
                if item.optional_vars is not None: (self._check_targets([item.optional_vars]), self.visit(item.optional_vars), self._discard_deferred(item.optional_vars))
            self._visit_statements(node.body)
        def _visit_statements(self, statements: list[ast.stmt]) -> None:
            """Visit a statement block until a terminating flow statement."""
            for statement in statements:
                if self.flow != "normal":
                    break
                self.visit(statement)
        def visit_Break(self, node: ast.Break) -> None:
            """Stop the current loop path at a break statement."""
            self.flow = "break"
        def visit_Continue(self, node: ast.Continue) -> None:
            """Stop the current loop path at a continue statement."""
            self.flow = "continue"
        def visit_For(self, node: ast.For) -> None:
            """Visit a loop and consume a generator used as its iterable."""
            self._visit_loop(node.iter, node.target, node.body, node.orelse, isinstance(node.iter, (ast.List, ast.Tuple, ast.Set)) and bool(node.iter.elts))
        def _visit_loop(
            self,
            iterator: ast.AST,
            target: ast.AST | None,
            body: list[ast.stmt],
            orelse: list[ast.stmt], definite: bool = False,
        ) -> None:
            """Visit loop paths while retaining the zero-iteration state."""
            empty = self._cardinality(iterator) == 0
            if empty or (target is None and isinstance(iterator, ast.Constant) and not bool(iterator.value)):
                self.visit(iterator)
                tuple(self.visit(statement) for statement in orelse)
                return
            if target is None:
                self.visit(iterator)
            else:
                (self._visit_consumable(iterator), self._check_targets([target]), self.visit(target), self._discard_deferred(target), self._bind_deferred(target, candidate) if isinstance(container := iterator if isinstance(iterator, (ast.List, ast.Tuple, ast.Set)) else self._deferred_value(iterator), (ast.List, ast.Tuple, ast.Set)) and (candidate := next((element for element in container.elts if self._deferred_value(element) is not None), None)) is not None else None)
            previous_flow = self.flow
            self.flow = "normal"
            baseline = self._state()
            self._visit_statements(body)
            body_state = self._state()
            body_flow = self.flow
            self._restore_state(body_state if definite else baseline)
            self.flow = "normal"
            if body_flow != "break":
                self._visit_statements(orelse)
            states = (body_state, self._state()) if definite else (baseline, body_state, self._state())
            self._merge_states(*states)
            self.flow = previous_flow
        def visit_While(self, node: ast.While) -> None:
            """Visit while-loop paths while retaining the zero-iteration state."""
            self._visit_loop(node.test, None, node.body, [] if isinstance(node.test, ast.Constant) and node.test.value is True else node.orelse, isinstance(node.test, ast.Constant) and node.test.value is True)
        def visit_Try(self, node: ast.Try) -> None:
            """Visit normal and exceptional paths without losing prior bindings."""
            baseline = self._state()
            for statement in node.body: self.visit(statement)
            normal_state = self._state()
            if len(node.body) == 1 and isinstance(node.body[0], ast.Assign) and all(isinstance(target, ast.Name) for target in node.body[0].targets) and isinstance(node.body[0].value, (ast.Constant, ast.List, ast.Tuple, ast.Set)):
                return tuple(self.visit(statement) for statement in (*node.orelse, *node.finalbody))
            paths = [normal_state]
            for handler in node.handlers:
                (self._restore_state(baseline), self._merge_states(baseline, normal_state))
                if handler.type is not None: self.visit(handler.type)
                if handler.name is not None: self._discard_name(handler.name)
                for statement in handler.body: self.visit(statement)
                paths.append(self._state())
            self._restore_state(paths[0])
            tuple(self.visit(statement) for statement in node.orelse)
            paths.append(self._state())
            (self._restore_state(baseline), self._merge_states(baseline, *paths))
            tuple(self.visit(statement) for statement in node.finalbody)
            self._merge_states(baseline, *paths, self._state())
        def visit_If(self, node: ast.If) -> None:
            """Merge deferred bindings conservatively across conditional paths."""
            self.visit(node.test)
            if isinstance(node.test, ast.Constant):
                for statement in node.body if bool(node.test.value) else node.orelse: self.visit(statement)
                return
            baseline = self._state()
            tuple(self.visit(statement) for statement in node.body)
            body_state = self._state()
            self._restore_state(baseline)
            tuple(self.visit(statement) for statement in node.orelse)
            self._merge_states(body_state, self._state())
        def visit_ListComp(self, node: ast.ListComp) -> None:
            """Visit list comprehensions, which consume all generator inputs."""
            self._visit_comprehension(node)
        def _visit_comprehension(self, node: ast.AST) -> None:
            """Visit comprehension inputs and expressions in execution order."""
            for generator in node.generators:
                self._visit_consumable(generator.iter)
                if self._cardinality(generator.iter) == 0:
                    return
                self._check_targets([generator.target])
                self.visit(generator.target)
                self._discard_deferred(generator.target)
                for condition in generator.ifs: self.visit(condition)
                if any(isinstance(condition, ast.Constant) and not bool(condition.value) for condition in generator.ifs): return
            if isinstance(node, ast.DictComp):
                self.visit(node.key)
                self.visit(node.value)
            else:
                self.visit(node.elt)
        def visit_Starred(self, node: ast.Starred) -> None:
            """Consume a deferred expression expanded into a container."""
            elements = node.value.elts if isinstance(node.value, (ast.Tuple, ast.List, ast.Set)) else (node.value,)
            for element in elements:
                self._visit_consumable(element)
        def visit_NamedExpr(self, node: ast.NamedExpr) -> None:
            """Track named expressions and consume their value when required."""
            self.visit(node.target)
            if self.executing_deferred is node:
                self._visit_deferred(node.value)
            else:
                self.visit(node.value)
            self._bind_deferred(node.target, node.value)
        def _bind_deferred(self, target: ast.AST, value: ast.AST) -> None:
            """Track simple names bound to deferred expressions for later consumers."""
            if isinstance(target, (ast.Name, ast.Attribute, ast.Subscript)):
                is_name = isinstance(target, ast.Name)
                consumer_kind = self._consumer_kind(value)
                producer_value = self._is_producer(value) or (isinstance(value, ast.Name) and value.id in self.producer_aliases) or (isinstance(value, ast.Attribute) and isinstance(value.value, ast.Name) and self.consumer_aliases.get(value.value.id) == "__itertools__" and value.attr in {"filterfalse", "islice", "takewhile", "dropwhile", "groupby", "starmap"})
                if is_name:
                    self._discard_name(target.id)
                    if isinstance(value, ast.Name):
                        aliases = set(self.object_aliases.get(value.id, {value.id}))
                        aliases.add(target.id)
                        for alias in aliases: self.object_aliases[alias] = aliases
                        needle = f"Name(id='{value.id}', ctx=Load())"
                        self.deferred_bindings.update({key.replace(needle, f"Name(id='{target.id}', ctx=Load())"): deferred for key, deferred in self.deferred_bindings.items() if key.startswith("target:") and needle in key})
                elif consumer_kind is not None: self.consumer_aliases[self._target_key(target)] = consumer_kind
                deferred = value if isinstance(value, (ast.Tuple, ast.List, ast.Set)) else self._deferred_value(value)
                keys = [self._target_key(target)]
                if isinstance(target, ast.Attribute) and isinstance(target.value, ast.Name):
                    keys.extend(self._target_key(ast.Attribute(value=ast.Name(id=alias, ctx=ast.Load()), attr=target.attr, ctx=ast.Load())) for alias in self.object_aliases.get(target.value.id, {target.value.id}) if alias != target.value.id)
                for key in keys:
                    if deferred is not None: self.deferred_bindings[key] = deferred
                    else: self.deferred_bindings.pop(key, None)
                if is_name:
                    if consumer_kind is not None:
                        self.consumer_aliases[target.id] = consumer_kind
                    elif target.id not in deferred_consumers:
                        self.consumer_aliases.pop(target.id, None)
                    if producer_value:
                        self.producer_aliases.add(target.id)
                    else:
                        self.producer_aliases.discard(target.id)
                    if isinstance(value, ast.Constant) and isinstance(value.value, str): self.consumer_aliases[target.id] = "__string__"
                return
            for key in self._target_keys(target):
                self.deferred_bindings.pop(key, None)
        def _discard_deferred(self, target: ast.AST) -> None:
            """Forget deferred bindings overwritten or deleted by a target."""
            for key in self._target_keys(target):
                self.deferred_bindings.pop(key, None)
            if isinstance(target, ast.Name):
                self._discard_name(target.id)
            elif isinstance(target, (ast.Tuple, ast.List)):
                for child in target.elts: self._discard_deferred(child)
            else: self.consumer_aliases.pop(self._target_key(target), None)
        def _discard_name(self, name: str, *, clear_eager: bool = True) -> None:
            """Forget all deferred semantics for one rebound name."""
            aliases = self.object_aliases.pop(name, set())
            for alias in tuple(aliases):
                if alias in self.object_aliases:
                    self.object_aliases[alias].discard(name)
            self.deferred_bindings.pop(f"name:{name}", None)
            needle = f"Name(id='{name}', ctx=Load())"
            for key in tuple(self.deferred_bindings):
                if key.startswith("target:") and needle in key:
                    self.deferred_bindings.pop(key, None)
            if name in deferred_consumers:
                self.consumer_aliases[name] = ""
            else:
                self.consumer_aliases.pop(name, None)
            self.producer_aliases.discard(name)
            if clear_eager:
                self.eager_callbacks.discard(name)
                self.eager_callback_specs.pop(name, None)
        def _state(self) -> tuple[dict[str, ast.AST], dict[str, str], set[str], dict[str, set[str]]]:
            """Snapshot deferred bindings and aliases for a control-flow path."""
            return dict(self.deferred_bindings), dict(self.consumer_aliases), set(self.producer_aliases), {key: set(value) for key, value in self.object_aliases.items()}
        def _restore_state(self, state: tuple[dict[str, ast.AST], dict[str, str], set[str], dict[str, set[str]]]) -> None:
            """Restore deferred bindings and aliases from one control-flow path."""
            self.deferred_bindings, self.consumer_aliases, self.producer_aliases, self.object_aliases = dict(state[0]), dict(state[1]), set(state[2]), {key: set(value) for key, value in state[3].items()}
        def _merge_states(self, *states: tuple[dict[str, ast.AST], dict[str, str], set[str], dict[str, set[str]]]) -> None:
            """Merge path states conservatively after control-flow joins."""
            self.deferred_bindings = {key: value for state in states for key, value in state[0].items()}
            self.consumer_aliases = {key: value for state in states for key, value in state[1].items() if key not in deferred_consumers or all(key in path[1] for path in states)}
            self.producer_aliases = {value for state in states for value in state[2]}
            self.object_aliases = {key: {alias for path in states for alias in path[3].get(key, set())} for key in {key for state in states for key in state[3]}}
        @staticmethod
        def _target_key(target: ast.AST) -> str:
            """Return a stable key for a name, attribute, or subscript target."""
            if isinstance(target, ast.Name):
                return f"name:{target.id}"
            dumped = ast.dump(target, include_attributes=False).replace("Store()", "Load()")
            return f"target:{dumped}"
        @classmethod
        def _target_keys(cls, target: ast.AST) -> tuple[str, ...]:
            """Return stable keys for simple and destructured assignment targets."""
            if isinstance(target, (ast.Tuple, ast.List)):
                return tuple(key for child in target.elts for key in cls._target_keys(child))
            if isinstance(target, (ast.Name, ast.Attribute, ast.Subscript)):
                return (cls._target_key(target),)
            return ()
        def _deferred_value(self, node: ast.AST) -> ast.AST | None:
            """Resolve a direct or aliased deferred expression binding."""
            if isinstance(node, (ast.Lambda, ast.GeneratorExp)) or self._is_producer(node):
                return node
            if isinstance(node, ast.Call):
                target = node.func if isinstance(node.func, ast.Lambda) else self._deferred_value(node.func)
                if isinstance(target, ast.Lambda): return self._deferred_value(target.body)
            if isinstance(node, ast.IfExp): return self._deferred_value(node.body if isinstance(node.test, ast.Constant) and bool(node.test.value) else node.orelse) if isinstance(node.test, ast.Constant) else self._deferred_value(node.body) or self._deferred_value(node.orelse)
            if isinstance(node, ast.BoolOp):
                for value in node.values:
                    deferred = self._deferred_value(value)
                    if isinstance(value, ast.Constant) or isinstance(value, (ast.List, ast.Tuple, ast.Set)):
                        truth = bool(value.value) if isinstance(value, ast.Constant) else bool(value.elts)
                        if (isinstance(node.op, ast.And) and not truth) or (isinstance(node.op, ast.Or) and truth): return deferred
                        continue
                    if deferred is not None:
                        if isinstance(node.op, ast.Or): return deferred
                        continue
                    return None
                return None
            if isinstance(node, ast.Subscript):
                try: index = ast.literal_eval(node.slice)
                except (TypeError, ValueError, SyntaxError): index = None
                container = node.value if isinstance(node.value, (ast.Tuple, ast.List, ast.Set)) else self._deferred_value(node.value)
                if isinstance(index, int) and isinstance(container, (ast.Tuple, ast.List, ast.Set)) and -len(container.elts) <= index < len(container.elts): return self._deferred_value(container.elts[index])
                return None
            if isinstance(node, (ast.Name, ast.Attribute, ast.Subscript)):
                return self.deferred_bindings.get(self._target_key(node))
            return None
        def _cardinality(self, node: ast.AST) -> int | None:
            """Return a known element count after resolving deferred aliases."""
            resolved = self._deferred_value(node)
            candidate = resolved if resolved is not None else node
            count = _static_cardinality(candidate)
            if count is not None:
                return count
            if isinstance(candidate, ast.Call) and isinstance(candidate.func, ast.Name) and candidate.func.id == "zip":
                counts = [self._cardinality(argument) for argument in candidate.args]
                return min(counts) if counts and all(value is not None for value in counts) else 0 if any(value == 0 for value in counts) else None
            return None
        def _producer_is_empty(self, node: ast.Call) -> bool:
            """Return whether a lazy producer has a statically empty source."""
            if (isinstance(node.func, ast.Name) and node.func.id in {"map", "filter"}) or (isinstance(node.func, ast.Attribute) and node.func.attr == "starmap"):
                return any(self._cardinality(argument) == 0 for argument in node.args[1:])
            if isinstance(node.func, ast.Name) and node.func.id == "zip":
                return any(self._cardinality(argument) == 0 for argument in node.args)
            return False
        def _consumer_kind(self, node: ast.AST) -> str | None:
            """Return the builtin consumer kind for a call target or alias."""
            if isinstance(node, ast.Attribute) and isinstance(node.value, ast.Name):
                if self.consumer_aliases.get(self._target_key(node)) == "callback": return "callback"
                if node.attr in {"chain", "reduce", "deque"} and self.consumer_aliases.get(node.value.id) == {"chain": "__itertools__", "reduce": "__functools__", "deque": "__collections__"}[node.attr]: return node.attr
                if node.attr in {"combinations", "permutations", "product"} and self.consumer_aliases.get(node.value.id) == "__itertools__": return "itertools_materializer"
                if node.attr == "fromkeys" and node.value.id == "dict" and "dict" not in self.consumer_aliases: return "dict.fromkeys"
                if node.attr in {"extend", "update"}: return "container_mutator"
            if isinstance(node, ast.Attribute) and node.attr == "from_iterable" and isinstance(node.value, ast.Name) and self.consumer_aliases.get(node.value.id) == "chain": return "chain_from_iterable"
            if isinstance(node, ast.Attribute) and isinstance(node.value, ast.Attribute) and node.attr == "from_iterable" and node.value.attr == "chain" and isinstance(node.value.value, ast.Name) and self.consumer_aliases.get(node.value.value.id) == "__itertools__": return "chain_from_iterable"
            if isinstance(node, ast.Attribute) and isinstance(node.value, ast.Constant) and isinstance(node.value.value, str) and node.attr == "join": return "join"
            if isinstance(node, ast.Attribute) and isinstance(node.value, ast.Name) and node.attr == "join" and self.consumer_aliases.get(node.value.id) == "__string__": return "join"
            if not isinstance(node, ast.Name):
                return None
            if node.id in self.eager_callbacks: return "callback"
            return (self.consumer_aliases[node.id] or None) if node.id in self.consumer_aliases else (node.id if node.id in deferred_consumers else None)
        def _callback_parameter_is_eager(self, name: str, parameter: str | None) -> bool:
            """Return whether a callback consumer invokes the named parameter."""
            spec = self.eager_callback_specs.get(name)
            return parameter is not None and (spec is None or parameter in spec[1])
        def _callback_consumed_positions(self, node: ast.Call) -> set[int]:
            """Return positional arguments consumed as eager callback parameters."""
            spec = self.eager_callback_specs.get(node.func.id) if isinstance(node.func, ast.Name) else None
            if spec is None:
                return set(range(len(node.args)))
            arguments, eager = spec
            positional = (*getattr(arguments, "posonlyargs", ()), *arguments.args)
            return {index for index, parameter in enumerate(positional[:len(node.args)]) if parameter.arg in eager}
        def _visit_callback_defaults(self, node: ast.Call) -> None:
            """Execute only omitted defaults for eager callback parameters."""
            spec = self.eager_callback_specs.get(node.func.id) if isinstance(node.func, ast.Name) else None
            if spec is None:
                return
            arguments, eager = spec
            positional = (*getattr(arguments, "posonlyargs", ()), *arguments.args)
            names, provided = tuple(parameter.arg for parameter in positional), set(parameter.arg for parameter in positional[:len(node.args)])
            provided.update(keyword.arg for keyword in node.keywords if keyword.arg)
            defaults = tuple(zip(names[len(positional) - len(arguments.defaults):], arguments.defaults)) + tuple((parameter.arg, default) for parameter, default in zip(arguments.kwonlyargs, arguments.kw_defaults) if default is not None)
            for name, default in defaults:
                if name in eager and name not in provided:
                    self._visit_deferred(default)
        def _is_producer(self, node: ast.AST) -> bool:
            """Return whether a call creates a lazy iterator with deferred callbacks."""
            return isinstance(node, ast.Call) and ((isinstance(node.func, ast.Name) and node.func.id in self.producer_aliases) or (isinstance(node.func, ast.Attribute) and isinstance(node.func.value, ast.Name) and self.consumer_aliases.get(node.func.value.id) == "__itertools__" and node.func.attr in {"filterfalse", "islice", "takewhile", "dropwhile", "groupby", "starmap"}))
        def _visit_consumable(self, node: ast.AST) -> None:
            """Consume a tracked deferred value while leaving ordinary calls opaque."""
            if isinstance(node, (ast.IfExp, ast.BoolOp)):
                if isinstance(node, ast.IfExp):
                    self.visit(node.test)
                    values = ((node.body if bool(node.test.value) else node.orelse,) if isinstance(node.test, ast.Constant) else (node.body, node.orelse))
                    for value in values:
                        self._visit_consumable(value)
                    return
                for value in node.values:
                    self._visit_consumable(value)
                    if isinstance(value, ast.Constant) and (
                        (isinstance(node.op, ast.And) and not bool(value.value))
                        or (isinstance(node.op, ast.Or) and bool(value.value))
                    ):
                        break
                return
            if isinstance(node, ast.NamedExpr):
                self.visit(node)
                deferred = self._deferred_value(node.value)
                if isinstance(deferred, ast.GeneratorExp) or self._is_producer(deferred): self._visit_deferred(node.value)
                return
            if isinstance(node, ast.Call):
                target = node.func if isinstance(node.func, ast.Lambda) else self._deferred_value(node.func)
                if isinstance(target, ast.Lambda):
                    for expression in (*target.args.defaults, *target.args.kw_defaults):
                        if expression is not None:
                            self.visit(expression)
                    self._visit_consumable(target.body)
                    return
            if self._deferred_value(node) is not None:
                self._visit_deferred(node)
            else:
                self.visit(node)
        def _visit_deferred(self, node: ast.AST) -> None:
            """Execute a deferred lambda/generator only in a consuming context."""
            deferred = self._deferred_value(node)
            target = deferred if deferred is not None else node
            previous = self.executing_deferred
            self.executing_deferred = target
            self.visit(target)
            self.executing_deferred = previous
    EnvironmentWriteVisitor.visit_AsyncFunctionDef = EnvironmentWriteVisitor.visit_FunctionDef
    EnvironmentWriteVisitor.visit_AsyncFor, EnvironmentWriteVisitor.visit_AsyncWith = EnvironmentWriteVisitor.visit_For, EnvironmentWriteVisitor.visit_With
    EnvironmentWriteVisitor.visit_SetComp = EnvironmentWriteVisitor.visit_ListComp
    EnvironmentWriteVisitor.visit_DictComp = EnvironmentWriteVisitor.visit_ListComp
    environment_visitor = EnvironmentWriteVisitor()
    def writes_environment(statement: ast.AST, *, execute_deferred: bool = False) -> bool:
        """Return whether one import-time statement writes the process environment."""
        environment_visitor.found = False
        previous = environment_visitor.executing_deferred
        environment_visitor.executing_deferred = statement if execute_deferred else None
        if execute_deferred:
            environment_visitor._visit_deferred(statement)
        else:
            environment_visitor.visit(statement)
        environment_visitor.executing_deferred = previous
        return environment_visitor.found
    signatures: set[str] = set()
    def record_import_time_environment(
        node: ast.AST,
        scope: tuple[str, ...],
        in_function: bool,
        *,
        execute_deferred: bool = False,
    ) -> None:
        """Record an environment write that executes during a definition."""
        if not in_function and writes_environment(node, execute_deferred=execute_deferred):
            qualified = ".".join(scope) or "<module>"
            signatures.add(
                f"import-time-environment:{qualified}:{ast.dump(node, include_attributes=False)}"
            )
    def visit(
        node: ast.AST,
        scope: tuple[str, ...],
        in_function: bool,
        *,
        skip_environment: bool = False,
    ) -> None:
        """Collect signatures while tracking the enclosing definition scope."""
        if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef)):
            parameters = {argument.arg for argument in ast.walk(node.args) if isinstance(argument, ast.arg)}
            calls = _reachable_calls(node)
            callback_parameters = {call.func.id for call in calls if isinstance(call.func, ast.Name) and call.func.id in parameters}
            callback_parameters.update(argument.id for call in calls if isinstance(call.func, ast.Name) and call.func.id in deferred_consumers for argument in call.args if isinstance(argument, ast.Name) and argument.id in parameters)
            callback_parameters.update(_producer_callback_parameters(node, calls, parameters))
            callback_aliases = {target.id for assignment in ast.walk(node) if isinstance(assignment, ast.Assign) for target in assignment.targets if isinstance(target, ast.Name) and isinstance(assignment.value, ast.Name) and assignment.value.id in parameters}
            callback_parameters.update({assignment.value.id for assignment in ast.walk(node) if isinstance(assignment, ast.Assign) and isinstance(assignment.value, ast.Name) and assignment.value.id in parameters and any(isinstance(target, ast.Name) and target.id in callback_aliases for target in assignment.targets)} | {call.func.id for call in calls if isinstance(call.func, ast.Name) and call.func.id in callback_aliases} | {call.func.value.id for call in calls if isinstance(call.func, ast.Attribute) and call.func.attr == "__call__" and isinstance(call.func.value, ast.Name) and call.func.value.id in parameters | callback_aliases})
            for decorator in node.decorator_list:
                record_import_time_environment(
                    decorator, scope, in_function, execute_deferred=True
                )
                visit(decorator, scope, in_function)
            environment_visitor._discard_name(node.name)
            environment_visitor.eager_callback_specs[node.name] = (node.args, tuple(callback_parameters))
            if callback_parameters: environment_visitor.eager_callbacks.add(node.name)
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
            environment_visitor._discard_name(node.name, clear_eager=False)
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
                record_import_time_environment(
                    decorator, scope, in_function, execute_deferred=True
                )
                visit(decorator, scope, in_function)
            for base in node.bases:
                record_import_time_environment(base, scope, in_function)
                visit(base, scope, in_function)
            for keyword in node.keywords:
                record_import_time_environment(keyword.value, scope, in_function)
                visit(keyword.value, scope, in_function)
            class_scope = scope + (node.name,)
            previous_bindings, previous_consumers, previous_producers, previous_objects = environment_visitor.deferred_bindings, environment_visitor.consumer_aliases, environment_visitor.producer_aliases, environment_visitor.object_aliases
            environment_visitor.deferred_bindings, environment_visitor.consumer_aliases, environment_visitor.producer_aliases, environment_visitor.object_aliases = dict(previous_bindings), dict(previous_consumers), set(previous_producers), {key: set(value) for key, value in previous_objects.items()}
            try:
                for statement in node.body:
                    if not in_function and not isinstance(statement, (ast.FunctionDef, ast.AsyncFunctionDef)):
                        record_import_time_environment(statement, class_scope, in_function)
                    visit(statement, class_scope, in_function)
                class_bindings = dict(environment_visitor.deferred_bindings)
            finally:
                environment_visitor.deferred_bindings, environment_visitor.consumer_aliases, environment_visitor.producer_aliases, environment_visitor.object_aliases = previous_bindings, previous_consumers, previous_producers, previous_objects
            environment_visitor._discard_name(node.name)
            nested_names = {statement.name for statement in node.body if isinstance(statement, ast.ClassDef)}
            for key, deferred in class_bindings.items():
                if key.startswith("name:") and (key not in previous_bindings or previous_bindings[key] is not deferred):
                    attribute = ast.Attribute(value=ast.Name(id=node.name, ctx=ast.Load()), attr=key[5:], ctx=ast.Load())
                    environment_visitor.deferred_bindings[EnvironmentWriteVisitor._target_key(attribute)] = deferred
                elif key.startswith("target:"):
                    promoted = key
                    for nested_name in nested_names:
                        promoted = promoted.replace(
                            f"Name(id='{nested_name}', ctx=Load())",
                            f"Attribute(value=Name(id='{node.name}', ctx=Load()), attr='{nested_name}', ctx=Load())",
                            1,
                        )
                    if promoted != key:
                        environment_visitor.deferred_bindings[promoted] = deferred
                    else:
                        environment_visitor.found = True
            return
        if isinstance(node, ast.If) and not in_function:
            visit(node.test, scope, in_function, skip_environment=True)
            for statement in (*node.body, *node.orelse): visit(statement, scope, in_function, skip_environment=True)
            return
        if (
            isinstance(node, (ast.For, ast.AsyncFor, ast.While)) or isinstance(node, _TRY_TYPES)
        ) and not in_function:
            for child in ast.iter_child_nodes(node):
                visit(child, scope, in_function, skip_environment=True)
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
            if (
                not skip_environment
                and not in_function
                and isinstance(child, ast.stmt)
                and writes_environment(child)
            ):
                signatures.add(
                    f"import-time-environment:{qualified}:{ast.dump(child, include_attributes=False)}"
                )
            visit(child, scope, in_function, skip_environment=skip_environment)
    visit(tree, (), False)
    return frozenset(signatures)
