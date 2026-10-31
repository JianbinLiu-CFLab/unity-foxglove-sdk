from __future__ import annotations

from compare_identity_surfaces.foundation import *

def _phase192_validate_closure_mutations(tree: ast.Module, path: str) -> None:
        """Reject protected-object mutations hidden behind facade methods or aliases."""
        protected_roots = {
            "_phase192_types",
            "_phase192_package_module",
            "__name__",
            "__package__",
            "__spec__",
            "__future__",
            "__loader__",
            "__cached__",
            "__file__",
            "__path__",
            "__doc__",
            "__all__",
            "__annotations__",
            "__builtins__",
            "__dict__",
            "main",
            "_phase192_main",
            "_phase192_entrypoint",
            "_phase192_previous_path",
        }
        protected_alias_sources = {
            "_phase192_sys",
            "_phase192_types",
            "_phase192_self",
            "_phase192_package_module",
            "_phase192_module",
            "_phase192_canonical_self",
            "_Phase192FacadeModule",
            "main",
            "_phase192_main",
            "_phase192_entrypoint",
            "_phase192_previous_path",
            "__spec__",
            "__builtins__",
            "__dict__",
        }
        allowed_alias_targets = {
            "_phase192_sys",
            "_phase192_types",
            "_phase192_module",
            "_phase192_self",
            "_phase192_package_module",
            "_phase192_canonical_self",
            "_phase192_previous_submodules",
            "_phase192_previous_path",
            "_phase192_previous_name",
            "_phase192_previous_package",
            "_phase192_parent",
            "_phase192_name",
            "_phase192_main",
            "_phase192_entrypoint",
            "__name__",
            "__package__",
        }

        def target_names(target: ast.AST) -> tuple[str, ...]:
            """Return names bound by a simple assignment target."""
            if isinstance(target, ast.Name):
                return (target.id,)
            if isinstance(target, (ast.Tuple, ast.List)):
                names: list[str] = []
                for item in target.elts:
                    names.extend(target_names(item))
                return tuple(names)
            return ()

        def contains_globals_lookup(node: ast.AST) -> bool:
            """Detect a globals() lookup inside a mutation expression."""
            return any(
                isinstance(item, ast.Call)
                and isinstance(item.func, ast.Name)
                and item.func.id == "globals"
                for item in ast.walk(node)
            )

        def protected_globals_lookup(node: ast.AST) -> bool:
            """Detect direct or getattr-based function globals lookups."""
            def literal_string(value: ast.AST) -> str | None:
                """Resolve a statically concatenated lookup key."""
                if isinstance(value, ast.Constant) and isinstance(value.value, str):
                    return value.value
                if isinstance(value, ast.BinOp) and isinstance(value.op, ast.Add):
                    left = literal_string(value.left)
                    right = literal_string(value.right)
                    if left is not None and right is not None:
                        return left + right
                return None

            if isinstance(node, ast.Attribute) and node.attr == "__globals__":
                return True
            if not (
                isinstance(node, ast.Call)
                and isinstance(node.func, ast.Name)
                and node.func.id == "getattr"
            ):
                return False
            key_node = node.args[1] if len(node.args) >= 2 else next(
                (keyword.value for keyword in node.keywords if keyword.arg == "name"),
                None,
            )
            return key_node is None or literal_string(key_node) == "__globals__"

        def contains_protected_globals_store(node: ast.AST) -> bool:
            """Detect writes through globals() to protected binding names."""
            protected_names = protected_alias_sources | protected_roots | {"main", "_phase192_name"}
            for item in ast.walk(node):
                if isinstance(item, ast.Subscript) and contains_globals_lookup(item.value):
                    if isinstance(item.slice, ast.Constant) and item.slice.value in protected_names:
                        return True
                    if item is not node or isinstance(item.ctx, ast.Load):
                        return True
                if (
                    isinstance(item, ast.Call)
                    and isinstance(item.func, ast.Attribute)
                    and item.func.attr in {"get", "__getitem__"}
                    and contains_globals_lookup(item.func.value)
                    and item.args
                ):
                    return True
            return False

        def exact_global_get(node: ast.AST, key: str) -> bool:
            """Recognize the exact CLI dispatch lookup used by a facade."""
            return (
                isinstance(node, ast.Call)
                and isinstance(node.func, ast.Attribute)
                and node.func.attr == "get"
                and isinstance(node.func.value, ast.Call)
                and isinstance(node.func.value.func, ast.Name)
                and node.func.value.func.id == "globals"
                and len(node.args) == 1
                and isinstance(node.args[0], ast.Constant)
                and node.args[0].value == key
                and not node.keywords
            )

        def method_signature(function: ast.FunctionDef) -> tuple[object, ...]:
            """Normalize a facade method while ignoring its documentation string."""
            body = list(function.body)
            if (
                body
                and isinstance(body[0], ast.Expr)
                and isinstance(body[0].value, ast.Constant)
                and isinstance(body[0].value.value, str)
            ):
                body = body[1:]
            return (
                ast.dump(function.args, include_attributes=False),
                (
                    ast.dump(function.returns, include_attributes=False)
                    if function.returns is not None
                    else None
                ),
                tuple(
                    ast.dump(decorator, include_attributes=False)
                    for decorator in function.decorator_list
                ),
                tuple(ast.dump(statement, include_attributes=False) for statement in body),
            )

        expected_method_tree = ast.parse(
            "def __setattr__(self, name, value):\n"
            "    super().__setattr__(name, value)\n"
            "    if _phase192_package_module is not None and _phase192_package_module is not self:\n"
            "        setattr(_phase192_package_module, name, value)\n"
            "\n"
            "def __delattr__(self, name):\n"
            "    super().__delattr__(name)\n"
            "    if _phase192_package_module is not None and _phase192_package_module is not self:\n"
            "        try:\n"
            "            delattr(_phase192_package_module, name)\n"
            "        except AttributeError:\n"
            "            pass\n"
        )
        expected_methods = {
            function.name: method_signature(function)
            for function in expected_method_tree.body
            if isinstance(function, ast.FunctionDef)
        }

        def mutation_root(node: ast.AST) -> str | None:
            """Resolve a protected mutation root through runtime lookup forms."""
            protected_lookup_names = protected_alias_sources | protected_roots

            def constant_string(value: ast.AST) -> str | None:
                """Resolve a statically concatenated string expression."""
                if isinstance(value, ast.Constant) and isinstance(value.value, str):
                    return value.value
                if isinstance(value, ast.BinOp) and isinstance(value.op, ast.Add):
                    left = constant_string(value.left)
                    right = constant_string(value.right)
                    if left is not None and right is not None:
                        return left + right
                return None

            def globals_lookup_key(value: ast.AST) -> ast.AST | None:
                """Return the key expression used by a globals mapping lookup."""
                if isinstance(value, ast.Subscript):
                    return value.slice
                if (
                    isinstance(value, ast.Call)
                    and isinstance(value.func, ast.Attribute)
                    and value.func.attr in {"get", "__getitem__"}
                ):
                    if value.args:
                        return value.args[0]
                    for keyword in value.keywords:
                        if keyword.arg in {"key", "name"}:
                            return keyword.value
                return None

            def is_globals_call(value: ast.AST) -> bool:
                """Return whether an expression is a zero-argument globals call."""
                return (
                    isinstance(value, ast.Call)
                    and isinstance(value.func, ast.Name)
                    and value.func.id == "globals"
                    and not value.args
                    and not value.keywords
                )

            def getattr_object(value: ast.Call) -> ast.AST | None:
                """Return the object expression passed to getattr."""
                if value.args:
                    return value.args[0]
                for keyword in value.keywords:
                    if keyword.arg == "object":
                        return keyword.value
                return None

            current = node
            while True:
                if isinstance(current, ast.Attribute):
                    current = current.value
                elif isinstance(current, ast.Subscript):
                    if is_globals_call(current.value):
                        key = constant_string(current.slice)
                        return key if key in protected_lookup_names else "__phase192_globals_lookup__"
                    current = current.value
                elif (
                    isinstance(current, ast.Call)
                    and isinstance(current.func, ast.Attribute)
                    and current.func.attr in {"get", "__getitem__"}
                ):
                    if is_globals_call(current.func.value):
                        key_node = globals_lookup_key(current)
                        key = constant_string(key_node) if key_node is not None else None
                        return key if key in protected_lookup_names else "__phase192_globals_lookup__"
                    current = current.func.value
                elif (
                    isinstance(current, ast.Call)
                    and isinstance(current.func, ast.Name)
                    and current.func.id == "getattr"
                ):
                    object_node = getattr_object(current)
                    if object_node is None:
                        break
                    current = object_node
                else:
                    break
            return current.id if isinstance(current, ast.Name) and current.id in protected_roots else None

        def contains_protected_source(node: ast.AST) -> bool:
            """Detect a value that would propagate a protected object through an alias."""
            return contains_globals_lookup(node) or any(
                (
                    isinstance(item, ast.Name) and item.id in protected_alias_sources
                )
                or protected_globals_handle(item)
                for item in ast.walk(node)
            )

        def allowed_forward_call(node: ast.Call) -> bool:
            """Recognize the exact facade-to-package forwarding calls."""
            if not (
                isinstance(node.func, ast.Name)
                and node.func.id in {"setattr", "delattr"}
                and node.args
                and isinstance(node.args[0], ast.Name)
                and node.args[0].id == "_phase192_package_module"
            ):
                return False
            if node.func.id == "setattr":
                return (
                    len(node.args) == 3
                    and isinstance(node.args[1], ast.Name)
                    and node.args[1].id == "name"
                    and isinstance(node.args[2], ast.Name)
                    and node.args[2].id == "value"
                    and not node.keywords
                )
            return (
                len(node.args) == 2
                and isinstance(node.args[1], ast.Name)
                and node.args[1].id == "name"
                and not node.keywords
            )

        protected_mutating_methods = frozenset(
            {
                "append",
                "clear",
                "extend",
                "insert",
                "pop",
                "popitem",
                "remove",
                "reverse",
                "setdefault",
                "sort",
                "update",
                "__delitem__",
                "__setitem__",
            }
        )

        def protected_globals_handle(node: ast.AST) -> bool:
            """Detect a protected function globals mapping through runtime lookup forms."""
            if isinstance(node, ast.Attribute) and node.attr == "__globals__":
                return mutation_root(node.value) is not None
            return (
                isinstance(node, ast.Call)
                and isinstance(node.func, ast.Name)
                and node.func.id == "getattr"
                and (
                    mutation_root(node) is not None
                    or protected_globals_lookup(node)
                )
            )

        snapshot_expression = ast.parse(
            "{name: _phase192_sys.modules[name] for name in _phase192_sys.modules "
            "if name == 'validate_source_generator_dll' or "
            "name.startswith('validate_source_generator_dll.')}",
            mode="eval",
        ).body
        for node in ast.walk(tree):
            if (
                isinstance(node, (ast.ListComp, ast.SetComp, ast.DictComp, ast.GeneratorExp))
                and any(contains_protected_source(generator.iter) for generator in node.generators)
                and ast.dump(node, include_attributes=False)
                != ast.dump(snapshot_expression, include_attributes=False)
            ):
                raise ValueError(f"Unsupported protected alias in {path}")
            if isinstance(node, ast.ClassDef) and node.name == "_Phase192FacadeModule":
                methods = {
                    statement.name: statement
                    for statement in node.body
                    if isinstance(statement, ast.FunctionDef)
                }
                if set(methods) != set(expected_methods) or any(
                    method_signature(methods[name]) != expected_methods[name]
                    for name in expected_methods
                ):
                    raise ValueError(f"Unsupported facade method body in {path}")
            if isinstance(node, ast.Import):
                for alias in node.names:
                    if alias.name in {"sys", "types"}:
                        expected = {
                            "sys": "_phase192_sys",
                            "types": "_phase192_types",
                        }[alias.name]
                        if (alias.asname or alias.name) != expected:
                            raise ValueError(f"Unsupported protected alias in {path}")
            if isinstance(node, ast.Assign):
                targets = node.targets
                value = node.value
            elif isinstance(node, (ast.AnnAssign, ast.AugAssign, ast.NamedExpr)):
                targets = [node.target]
                value = node.value
            else:
                targets = []
                value = None
            names = tuple(name for target in targets for name in target_names(target))
            for name in names:
                if name == "_phase192_main" and not exact_global_get(value, "main"):
                    raise ValueError(f"Unsupported protected alias in {path}")
                if name == "_phase192_entrypoint" and not exact_global_get(value, "_entrypoint"):
                    raise ValueError(f"Unsupported protected alias in {path}")
            if value is not None and contains_protected_source(value):
                for target in targets:
                    if any(name not in allowed_alias_targets for name in target_names(target)):
                        raise ValueError(f"Unsupported protected alias in {path}")
            for target in targets:
                if not isinstance(target, (ast.Attribute, ast.Subscript)):
                    continue
                if (
                    (
                        mutation_root(target) is not None
                        or contains_protected_globals_store(target)
                        or any(
                            protected_globals_lookup(item)
                            for item in ast.walk(target)
                        )
                    )
                    and not (
                        isinstance(target, ast.Attribute)
                        and isinstance(target.value, ast.Name)
                        and target.value.id == "_phase192_module"
                        and target.attr == "__class__"
                    )
                    and not (
                        isinstance(target, ast.Subscript)
                        and isinstance(target.value, ast.Call)
                        and isinstance(target.value.func, ast.Name)
                        and target.value.func.id == "globals"
                        and isinstance(target.slice, ast.Name)
                        and target.slice.id == "_phase192_name"
                    )
                ):
                    raise ValueError(f"Unsupported protected mutation in {path}")
            if isinstance(node, ast.Call):
                function = node.func
                mutating = (
                    isinstance(function, ast.Name)
                    and function.id in {"setattr", "delattr"}
                ) or (
                    isinstance(function, ast.Attribute)
                    and function.attr in {"__setattr__", "__delattr__"}
                )
                protected_method_mutation = (
                    isinstance(function, ast.Attribute)
                    and function.attr in protected_mutating_methods
                    and (
                        mutation_root(function.value) is not None
                        or contains_globals_lookup(function.value)
                        or any(
                            protected_globals_lookup(item)
                            for item in ast.walk(function.value)
                        )
                    )
                )
                if protected_method_mutation:
                    raise ValueError(f"Unsupported protected mutation in {path}")
                if mutating and not allowed_forward_call(node):
                    root = mutation_root(function.value) if isinstance(function, ast.Attribute) else (
                        mutation_root(node.args[0]) if node.args else None
                    )
                    if root is not None or contains_protected_globals_store(node):
                        raise ValueError(f"Unsupported protected mutation in {path}")

__all__ = ['_phase192_validate_closure_mutations']
