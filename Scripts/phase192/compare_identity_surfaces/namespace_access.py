from __future__ import annotations

from .foundation import *

from Scripts.phase192.identity_surface_conditions import (
    _bound_names_in_statement,
    _conditional_contract_signatures,
    _conditional_only_public_names,
    _is_public_globals_comprehension,
    _reject_unsupported_module_bindings,
)


def _validate_namespace_access(
    tree: ast.Module,
    allowed_nodes: set[int],
    path: str,
    *,
    reject_module_registry: bool = False,
    repository_root: Path | None = None,
) -> None:
    """Internal helper for _validate_namespace_access."""
    parents: dict[int, ast.AST] = {}
    for parent in ast.walk(tree):
        for child in ast.iter_child_nodes(parent):
            parents[id(child)] = parent

    function_body_nodes: set[int] = set()
    for definition in ast.walk(tree):
        if isinstance(definition, (ast.FunctionDef, ast.AsyncFunctionDef)):
            function_body_nodes.update(
                id(node)
                for statement in definition.body
                for node in ast.walk(statement)
            )
        elif isinstance(definition, ast.Lambda):
            function_body_nodes.update(id(node) for node in ast.walk(definition.body))

    def is_inside_callable(node: ast.AST) -> bool:
        """Return whether a node executes only when a callable is invoked."""
        return id(node) in function_body_nodes

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
            if (
                isinstance(node.ctx, ast.Store)
                and isinstance(node.value, ast.Attribute)
                and node.value.attr == "modules"
                and isinstance(node.value.value, ast.Name)
                and node.value.value.id in {"sys", "_sys"}
                and isinstance(node.slice, ast.Attribute)
                and node.slice.attr == "name"
                and isinstance(node.slice.value, ast.Name)
                and node.slice.value.id == "spec"
            ):
                return False
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
            if not (
                _is_phase121_configuration_path(path, repository_root)
                and set(node.names) <= {"OFFICIAL_ROOT", "OFFICIAL_CONFORMANCE"}
            ):
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
            if not is_inside_callable(node) and (node.id in builtin_names or node.id in dynamic_names):
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
            or (
                isinstance(node, ast.Subscript)
                and contains_module_registry(node.value)
                and not isinstance(node.ctx, ast.Load)
                and not (
                    isinstance(node.ctx, ast.Store)
                    and isinstance(node.value, ast.Attribute)
                    and node.value.attr == "modules"
                    and isinstance(node.value.value, ast.Name)
                    and node.value.value.id in {"sys", "_sys"}
                    and isinstance(node.slice, ast.Attribute)
                    and node.slice.attr == "name"
                    and isinstance(node.slice.value, ast.Name)
                    and node.slice.value.id == "spec"
                )
            )
        ):
            raise ValueError(f"Unsupported module namespace mutation in {path}")
        if reject_module_registry and isinstance(node, ast.Attribute) and node.attr in {"__setattr__", "__delattr__"} and contains_module_registry(node.value):
            raise ValueError(f"Unsupported module namespace mutation in {path}")
        if isinstance(node, ast.Attribute) and node.attr == "import_module":
            parent = parents.get(id(node))
            if not (isinstance(parent, ast.Call) and parent.func is node):
                raise ValueError(f"Unsupported dynamic import alias in {path}")
        if isinstance(node, ast.Call):
            if not is_inside_callable(node) and isinstance(node.func, ast.Name) and node.func.id in dynamic_names:
                raise ValueError(f"Unsupported dynamic execution in {path}")
            imported = dunder_import_call(node)
            if imported is not None:
                validate_dunder_import(imported)
            if not is_inside_callable(node) and isinstance(node.func, ast.Attribute) and node.func.attr in dynamic_names:
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

__all__ = ['_bound_names_in_statement', '_conditional_contract_signatures', '_conditional_only_public_names', '_is_public_globals_comprehension', '_reject_unsupported_module_bindings', '_validate_namespace_access']
