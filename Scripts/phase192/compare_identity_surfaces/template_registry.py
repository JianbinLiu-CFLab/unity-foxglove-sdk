from __future__ import annotations

from .foundation import *

def _phase192_template_name(name: str) -> bool:
    """Return whether a name belongs to the generated forwarding template."""
    return name.startswith("_phase192_") or name.startswith("_phase138u_") or name.startswith("_PHASE192_") or name.startswith("_PHASE138U_")


def _phase192_template_call_is_safe(call: ast.Call) -> bool:
    """Reject calls that could execute arbitrary import-time work in a wrapper."""
    function = call.func
    if isinstance(function, ast.Name):
        return function.id in _PHASE192_TEMPLATE_CALLS or function.id in {
            "_phase192_entrypoint",
            "_phase192_main",
            "_phase192_repository_root",
        } or function.id.endswith("Path")
    if isinstance(function, ast.Attribute):
        return function.attr in _PHASE192_TEMPLATE_METHODS and (
            isinstance(function.value, (ast.Name, ast.Attribute, ast.Call, ast.Subscript))
        )
    return False


def _phase192_template_validate_calls(tree: ast.AST, path: str) -> None:
    """Validate the deliberately small call vocabulary used by wrappers."""
    trusted_path_imports = {"pathlib.Path"}
    trusted_special_aliases = {"_phase192_entrypoint", "_phase192_main", "_phase192_repository_root"}
    for node in ast.walk(tree):
        if isinstance(node, ast.ImportFrom):
            module = node.module or ""
            for alias in node.names:
                bound_name = alias.asname or alias.name
                if bound_name.endswith("Path") and f"{module}.{alias.name}" not in trusted_path_imports:
                    raise ValueError(f"Untrusted Path alias in {path}")
                if bound_name in trusted_special_aliases:
                    raise ValueError(f"Untrusted generated-wrapper alias in {path}")
    for node in ast.walk(tree):
        if isinstance(node, ast.Call) and not _phase192_template_call_is_safe(node):
            raise ValueError(f"Unsupported generated-wrapper call in {path}")


def _phase192_template_validate_imports(tree: ast.Module, path: str) -> None:
    """Validate imports used by a generated facade or package initializer."""
    local_facade = Path(path).stem
    for node in ast.walk(tree):
        if isinstance(node, ast.Import):
            for alias in node.names:
                if alias.name.split(".", 1)[0] not in _PHASE192_TEMPLATE_IMPORTS:
                    raise ValueError(f"Unsupported generated-wrapper import in {path}")
        elif isinstance(node, ast.ImportFrom):
            if node.module == "__future__":
                if node.level or any(alias.name != "annotations" for alias in node.names):
                    raise ValueError(f"Unsupported generated-wrapper future import in {path}")
            elif node.level != 1:
                root = (node.module or "").split(".", 1)[0]
                if not (
                    root in _PHASE192_TEMPLATE_IMPORTS
                    or node.module == local_facade
                    and any(alias.name == "*" for alias in node.names)
                ):
                    raise ValueError(f"Unsupported generated-wrapper import in {path}")


def _phase192_template_validate_class(node: ast.ClassDef, path: str, *, package: bool) -> None:
    """Validate the forwarding ModuleType subclass without executing it."""
    expected = "_Phase192PackageModule" if package else "_Phase192FacadeModule"
    if node.name not in {expected, "_Phase138UPackageModule"}:
        raise ValueError(f"Unsupported generated-wrapper class in {path}")
    if len(node.bases) != 1 or not isinstance(node.bases[0], ast.Attribute):
        raise ValueError(f"Unsupported generated-wrapper base in {path}")
    if node.bases[0].attr != "ModuleType":
        raise ValueError(f"Unsupported generated-wrapper base in {path}")
    for statement in node.body:
        if isinstance(statement, ast.Expr) and isinstance(statement.value, ast.Constant) and isinstance(statement.value.value, str):
            continue
        if not isinstance(statement, (ast.FunctionDef, ast.AsyncFunctionDef)):
            raise ValueError(f"Unsupported generated-wrapper class body in {path}")
        if statement.name not in {"__setattr__", "__delattr__"}:
            raise ValueError(f"Unsupported generated-wrapper method in {path}")
        if statement.decorator_list:
            raise ValueError(f"Unsupported generated-wrapper method decorator in {path}")


def _phase192_template_validate_facade(
    tree: ast.Module,
    path: str,
    repository_root: Path | None = None,
) -> None:
    """Validate the generated facade shape before allowing its namespace shim."""
    classes = [node for node in tree.body if isinstance(node, ast.ClassDef)]
    if len(classes) != 1:
        raise ValueError(f"Generated facade must contain one forwarding class: {path}")
    _phase192_template_validate_class(classes[0], path, package=False)
    _phase192_template_validate_imports(tree, path)
    _phase192_template_validate_calls(tree, path)
    _phase192_validate_template_registry_mutations(tree, path, repository_root)
    for node in tree.body:
        if isinstance(node, (ast.Expr, ast.Import, ast.ImportFrom, ast.Assign, ast.Delete, ast.If, ast.Try, ast.ClassDef)):
            continue
        raise ValueError(f"Unsupported generated facade statement in {path}")
    if not any(
        isinstance(node, ast.ImportFrom)
        and (
            node.level == 1
            or (node.level == 0 and node.module == Path(path).stem)
        )
        for node in ast.walk(tree)
    ):
        raise ValueError(f"Generated facade has no package import in {path}")


def _phase192_validate_template_registry_mutations(
    tree: ast.Module,
    path: str,
    repository_root: Path | None = None,
) -> None:
    """Allow only the exact sys.modules forwarding mutations emitted by templates."""
    def validate_closure_mutations() -> None:
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

    validate_closure_mutations()
    expected_module_key = Path(path).stem
    allowed_module_keys = {expected_module_key, expected_module_key + ".implementation"}
    try:
        relative_path = Path(path).resolve().relative_to(Path(repository_root).resolve()).as_posix()
    except (TypeError, ValueError):
        relative_path = None
    if relative_path in {
        "Scripts/ros2forunity/windows/humble/validate_ros2forunity_package.py",
        "Scripts/ros2forunity/windows/jazzy/validate_ros2forunity_package.py",
        "Scripts/ros2forunity/windows/lyrical/validate_ros2forunity_package.py",
    }:
        allowed_module_keys.update(
            f"validate_ros2forunity_package.{name}"
            for name in (
                "core",
                "package_metadata",
                "manifest",
                "text_boundaries",
                "runtime_checks",
                "sample_checks",
                "boundary_checks",
                "entrypoint",
            )
        )
    sys_aliases: set[str] = set()
    registry_aliases: set[str] = set()
    for node in tree.body:
        if isinstance(node, ast.Import):
            for alias in node.names:
                if alias.name == "sys":
                    sys_aliases.add(alias.asname or "sys")
        elif isinstance(node, ast.ImportFrom) and node.module == "sys":
            for alias in node.names:
                if alias.name == "modules":
                    registry_aliases.add(alias.asname or alias.name)

    def modules_value(node: ast.AST) -> bool:
        """Recognize the generated module registry aliases."""
        if isinstance(node, ast.Name):
            return node.id in registry_aliases
        return (
            isinstance(node, ast.Attribute)
            and node.attr == "modules"
            and isinstance(node.value, ast.Name)
            and node.value.id in sys_aliases
        )

    changed = True
    while changed:
        changed = False
        for statement in ast.walk(tree):
            if not isinstance(statement, ast.Assign) or len(statement.targets) != 1:
                continue
            target = statement.targets[0]
            if not isinstance(target, ast.Name) or not modules_value(statement.value):
                continue
            if target.id not in registry_aliases:
                registry_aliases.add(target.id)
                changed = True

    def contains_registry_reference(node: ast.AST) -> bool:
        """Recognize mutation targets rooted in the module registry."""
        if modules_value(node):
            return True
        if isinstance(node, ast.Subscript):
            return contains_registry_reference(node.value)
        if isinstance(node, ast.Attribute):
            return contains_registry_reference(node.value)
        return False

    protected_alias_sources = {
        "_phase192_sys",
        "_phase192_types",
        "_phase192_self",
        "_phase192_package_module",
        "_phase192_module",
        "_phase192_canonical_self",
        "_phase192_previous_submodules",
        "_phase192_previous_path",
        "_Phase192FacadeModule",
        "main",
        "_phase192_main",
        "_phase192_entrypoint",
        "__spec__",
        "__builtins__",
        "__dict__",
    }

    def contains_protected_source(node: ast.AST) -> bool:
        """Recognize protected sources propagated through loop aliases."""
        return any(
            isinstance(item, ast.Name) and item.id in protected_alias_sources
            or isinstance(item, ast.Call)
            and isinstance(item.func, ast.Name)
            and item.func.id == "globals"
            for item in ast.walk(node)
        )

    try:
        relative_path = Path(path).resolve().relative_to(Path(repository_root).resolve()).as_posix()
    except (TypeError, ValueError):
        relative_path = None
    loader_path = relative_path == "Scripts/package/validate_source_generator_dll.py"
    if loader_path:
        loader_type_imports = [
            alias
            for node in ast.walk(tree)
            if isinstance(node, ast.Import)
            for alias in node.names
            if alias.asname == "_phase192_types"
        ]
        if len(loader_type_imports) != 1 or loader_type_imports[0].name != "types":
            raise ValueError(f"Unsupported loader types binding in {path}")
        if any(
            isinstance(node, ast.ImportFrom)
            and any((alias.asname or alias.name) == "_phase192_types" for alias in node.names)
            for node in ast.walk(tree)
        ):
            raise ValueError(f"Unsupported loader types binding in {path}")
        if any(
            isinstance(node, ast.Assign)
            and any(
                isinstance(target, ast.Name)
                and target.id == "_Phase192FacadeModule"
                for target in node.targets
            )
            for node in ast.walk(tree)
        ):
            raise ValueError(f"Unsupported loader facade class binding in {path}")
        loader_path_imports = [
            alias
            for node in ast.walk(tree)
            if isinstance(node, ast.ImportFrom)
            and node.module == "pathlib"
            for alias in node.names
            if alias.asname == "_Phase192Path"
        ]
        if len(loader_path_imports) != 1 or loader_path_imports[0].name != "Path":
            raise ValueError(f"Unsupported loader path binding in {path}")
        if any(
            isinstance(node, (ast.Assign, ast.AnnAssign, ast.AugAssign, ast.NamedExpr))
            and any(
                isinstance(target, ast.Name) and target.id == "_Phase192Path"
                for target in (
                    node.targets
                    if isinstance(node, ast.Assign)
                    else [node.target]
                )
            )
            for node in ast.walk(tree)
        ):
            raise ValueError(f"Unsupported loader path binding in {path}")
        trusted_special_aliases = {
            "_phase192_entrypoint",
            "_phase192_main",
            "_phase192_repository_root",
        }
        if any(
            isinstance(node, (ast.Assign, ast.AnnAssign, ast.AugAssign, ast.NamedExpr))
            and any(
                isinstance(target, ast.Name) and target.id in trusted_special_aliases
                for target in (
                    node.targets
                    if isinstance(node, ast.Assign)
                    else [node.target]
                )
            )
            for node in ast.walk(tree)
        ):
            raise ValueError(f"Unsupported loader trusted alias binding in {path}")

        def exact_top_level_assignment(name: str, expression: str) -> bool:
            """Recognize one exact top-level loader binding."""
            expected = ast.parse(expression, mode="eval").body
            assignments = [
                statement
                for statement in ast.walk(tree)
                if isinstance(statement, ast.Assign)
                and len(statement.targets) == 1
                and isinstance(statement.targets[0], ast.Name)
                and statement.targets[0].id == name
            ]
            matches = [
                statement
                for statement in assignments
                if statement in tree.body
                and ast.dump(statement.value, include_attributes=False)
                == ast.dump(expected, include_attributes=False)
            ]
            return len(assignments) == len(matches) == 1

        if not (
            exact_top_level_assignment(
                "__name__",
                "__spec__.name if __spec__ is not None else __name__",
            )
            and exact_top_level_assignment(
                "__package__",
                "__spec__.parent if __spec__ is not None else __package__",
            )
        ):
            raise ValueError(f"Unsupported loader module metadata binding in {path}")

        def exact_module_class_guard(statement: ast.AST) -> bool:
            """Recognize the exact guarded facade class assignment."""
            if not (
                isinstance(statement, ast.If)
                and not statement.orelse
                and len(statement.body) == 1
                and isinstance(statement.test, ast.Compare)
                and len(statement.test.ops) == 1
                and isinstance(statement.test.ops[0], ast.IsNot)
                and isinstance(statement.test.left, ast.Name)
                and statement.test.left.id == "_phase192_module"
                and len(statement.test.comparators) == 1
                and isinstance(statement.test.comparators[0], ast.Constant)
                and statement.test.comparators[0].value is None
            ):
                return False
            assignment = statement.body[0]
            return (
                isinstance(assignment, ast.Assign)
                and len(assignment.targets) == 1
                and isinstance(assignment.targets[0], ast.Attribute)
                and isinstance(assignment.targets[0].value, ast.Name)
                and assignment.targets[0].value.id == "_phase192_module"
                and assignment.targets[0].attr == "__class__"
                and isinstance(assignment.value, ast.Name)
                and assignment.value.id == "_Phase192FacadeModule"
            )

        all_module_class_assignments = [
            statement
            for statement in ast.walk(tree)
            if isinstance(statement, ast.Assign)
            and len(statement.targets) == 1
            and isinstance(statement.targets[0], ast.Attribute)
            and isinstance(statement.targets[0].value, ast.Name)
            and statement.targets[0].value.id == "_phase192_module"
            and statement.targets[0].attr == "__class__"
        ]
        if (
            len(all_module_class_assignments) != 1
            or sum(exact_module_class_guard(statement) for statement in tree.body) != 1
        ):
            raise ValueError(f"Unsupported loader module class binding in {path}")

        def exact_cli_guard(statement: ast.AST) -> bool:
            """Recognize the exact validator command-line dispatch guard."""
            return (
                isinstance(statement, ast.If)
                and not statement.orelse
                and isinstance(statement.test, ast.Compare)
                and len(statement.test.ops) == 1
                and isinstance(statement.test.ops[0], ast.Eq)
                and isinstance(statement.test.left, ast.Name)
                and statement.test.left.id == "__name__"
                and len(statement.test.comparators) == 1
                and isinstance(statement.test.comparators[0], ast.Constant)
                and statement.test.comparators[0].value == "__main__"
                and len(statement.body) == 1
                and isinstance(statement.body[0], ast.Raise)
                and isinstance(statement.body[0].exc, ast.Call)
                and isinstance(statement.body[0].exc.func, ast.Name)
                and statement.body[0].exc.func.id == "SystemExit"
                and len(statement.body[0].exc.args) == 1
                and isinstance(statement.body[0].exc.args[0], ast.Call)
                and isinstance(statement.body[0].exc.args[0].func, ast.Name)
                and statement.body[0].exc.args[0].func.id == "main"
                and not statement.body[0].exc.args[0].args
                and not statement.body[0].exc.args[0].keywords
                and not statement.body[0].exc.keywords
            )

        def is_main_name_guard(statement: ast.AST) -> bool:
            """Recognize any module-level __name__ main guard."""
            if not (
                isinstance(statement, ast.If)
                and isinstance(statement.test, ast.Compare)
                and len(statement.test.ops) == 1
                and isinstance(statement.test.ops[0], ast.Eq)
                and len(statement.test.comparators) == 1
            ):
                return False
            left, right = statement.test.left, statement.test.comparators[0]
            return (
                isinstance(left, ast.Name)
                and left.id == "__name__"
                and isinstance(right, ast.Constant)
                and right.value == "__main__"
            ) or (
                isinstance(right, ast.Name)
                and right.id == "__name__"
                and isinstance(left, ast.Constant)
                and left.value == "__main__"
            )

        main_guards = [statement for statement in tree.body if is_main_name_guard(statement)]
        if len(main_guards) != 1 or not exact_cli_guard(main_guards[0]):
            raise ValueError(f"Unsupported loader command-line dispatch in {path}")

    snapshot_filter = ast.parse(
        "(name == 'validate_source_generator_dll' or "
        "name.startswith('validate_source_generator_dll.'))",
        mode="eval",
    ).body
    cleanup_condition = ast.parse(
        "(_phase192_name == 'validate_source_generator_dll' or "
        "_phase192_name.startswith('validate_source_generator_dll.')) "
        "and _phase192_name not in _phase192_previous_submodules",
        mode="eval",
    ).body
    cleanup_iter = ast.parse("tuple(_phase192_sys.modules)", mode="eval").body

    def exact_snapshot(statement: ast.AST) -> bool:
        """Recognize the one immutable loader module snapshot assignment."""
        if not isinstance(statement, ast.Assign) or len(statement.targets) != 1:
            return False
        target = statement.targets[0]
        value = statement.value
        return (
            isinstance(target, ast.Name)
            and target.id == "_phase192_previous_submodules"
            and isinstance(value, ast.DictComp)
            and len(value.generators) == 1
            and isinstance(value.generators[0].target, ast.Name)
            and value.generators[0].target.id == "name"
            and isinstance(value.generators[0].iter, ast.Attribute)
            and value.generators[0].iter.attr == "modules"
            and isinstance(value.generators[0].iter.value, ast.Name)
            and value.generators[0].iter.value.id == "_phase192_sys"
            and isinstance(value.key, ast.Name)
            and value.key.id == "name"
            and isinstance(value.value, ast.Subscript)
            and isinstance(value.value.value, ast.Attribute)
            and value.value.value.attr == "modules"
            and isinstance(value.value.value.value, ast.Name)
            and value.value.value.value.id == "_phase192_sys"
            and isinstance(value.value.slice, ast.Name)
            and value.value.slice.id == "name"
            and len(value.generators[0].ifs) == 1
            and ast.dump(value.generators[0].ifs[0], include_attributes=False)
            == ast.dump(snapshot_filter, include_attributes=False)
        )

    def exact_cleanup_loop(statement: ast.AST) -> tuple[int, int] | None:
        """Recognize the guarded cleanup loop and its sole pop call."""
        if not (
            isinstance(statement, ast.For)
            and isinstance(statement.target, ast.Name)
            and statement.target.id == "_phase192_name"
            and ast.dump(statement.iter, include_attributes=False)
            == ast.dump(cleanup_iter, include_attributes=False)
            and not statement.orelse
            and len(statement.body) == 1
            and isinstance(statement.body[0], ast.If)
        ):
            return None
        guard = statement.body[0]
        if (
            ast.dump(guard.test, include_attributes=False)
            != ast.dump(cleanup_condition, include_attributes=False)
            or guard.orelse
            or len(guard.body) != 1
            or not isinstance(guard.body[0], ast.Expr)
        ):
            return None
        call = guard.body[0].value
        if not (
            isinstance(call, ast.Call)
            and isinstance(call.func, ast.Attribute)
            and modules_value(call.func.value)
            and call.func.attr == "pop"
            and len(call.args) == 2
            and isinstance(call.args[0], ast.Name)
            and call.args[0].id == "_phase192_name"
            and isinstance(call.args[1], ast.Constant)
            and call.args[1].value is None
            and not call.keywords
        ):
            return None
        return id(statement), id(call)

    def exact_restore_loop(statement: ast.AST) -> tuple[int, int] | None:
        """Recognize the sole immutable snapshot restore loop."""
        if not (
            isinstance(statement, ast.For)
            and isinstance(statement.target, ast.Name)
            and statement.target.id == "_phase192_name"
            and isinstance(statement.iter, ast.Name)
            and statement.iter.id == "_phase192_previous_submodules"
            and not statement.orelse
            and len(statement.body) == 1
            and isinstance(statement.body[0], ast.Assign)
        ):
            return None
        assignment = statement.body[0]
        target = assignment.targets[0] if len(assignment.targets) == 1 else None
        return (
            (id(statement), id(assignment))
            if (
                isinstance(target, ast.Subscript)
                and modules_value(target.value)
                and isinstance(target.slice, ast.Name)
                and target.slice.id == "_phase192_name"
                and isinstance(assignment.value, ast.Subscript)
                and isinstance(assignment.value.value, ast.Name)
                and assignment.value.value.id == "_phase192_previous_submodules"
                and isinstance(assignment.value.slice, ast.Name)
                and assignment.value.slice.id == "_phase192_name"
            )
            else None
        )

    for node in ast.walk(tree):
        if (
            isinstance(node, (ast.For, ast.AsyncFor))
            and contains_protected_source(node.iter)
            and exact_cleanup_loop(node) is None
            and exact_restore_loop(node) is None
        ):
            raise ValueError(f"Unsupported protected alias in {path}")

    def rooted_in(node: ast.AST, name: str) -> bool:
        """Return whether a registry expression is rooted in one binding."""
        if isinstance(node, ast.Name):
            return node.id == name
        if isinstance(node, (ast.Attribute, ast.Subscript)):
            return rooted_in(node.value, name)
        return False

    def critical_bindings_are_immutable(transaction: ast.Try | None) -> bool:
        """Reject rebinding of loader transaction state after initialization."""
        if transaction is None:
            return False

        def exact_canonical_setup_guard(statement: ast.AST) -> tuple[ast.Assign, ast.Assign] | None:
            """Recognize the exact initial canonical facade metadata setup."""
            if not (
                isinstance(statement, ast.If)
                and not statement.orelse
                and isinstance(statement.test, ast.Compare)
                and len(statement.test.ops) == 1
                and isinstance(statement.test.ops[0], ast.IsNot)
                and isinstance(statement.test.left, ast.Name)
                and statement.test.left.id == "_phase192_canonical_self"
                and len(statement.test.comparators) == 1
                and isinstance(statement.test.comparators[0], ast.Constant)
                and statement.test.comparators[0].value is None
                and len(statement.body) == 4
                and isinstance(statement.body[0], ast.Assign)
                and isinstance(statement.body[1], ast.Assign)
                and isinstance(statement.body[2], ast.Assign)
                and isinstance(statement.body[3], ast.Expr)
            ):
                return None
            assignments = tuple(statement.body[:2])
            if not all(
                len(item.targets) == 1
                and isinstance(item.targets[0], ast.Attribute)
                and isinstance(item.targets[0].value, ast.Name)
                and item.targets[0].value.id == "_phase192_canonical_self"
                and isinstance(item.value, ast.Constant)
                for item in assignments
            ):
                return None
            first, second = assignments
            if not (
                first.targets[0].attr == "__package__"
                and first.value.value == ""
                and second.targets[0].attr == "__name__"
                and second.value.value == "validate_source_generator_dll"
            ):
                return None
            registry_assignment, registry_pop = statement.body[2], statement.body[3]
            if not (
                len(registry_assignment.targets) == 1
                and isinstance(registry_assignment.targets[0], ast.Subscript)
                and modules_value(registry_assignment.targets[0].value)
                and isinstance(registry_assignment.targets[0].slice, ast.Constant)
                and registry_assignment.targets[0].slice.value == "validate_source_generator_dll"
                and isinstance(registry_assignment.value, ast.Name)
                and registry_assignment.value.id == "_phase192_canonical_self"
                and isinstance(registry_pop.value, ast.Call)
                and isinstance(registry_pop.value.func, ast.Attribute)
                and modules_value(registry_pop.value.func.value)
                and registry_pop.value.func.attr == "pop"
                and len(registry_pop.value.args) == 2
                and isinstance(registry_pop.value.args[0], ast.Constant)
                and registry_pop.value.args[0].value == "validate_source_generator_dll"
                and isinstance(registry_pop.value.args[1], ast.Constant)
                and registry_pop.value.args[1].value is None
                and not registry_pop.value.keywords
            ):
                return None
            return first, second

        canonical_setup_guards = [
            exact_canonical_setup_guard(statement)
            for statement in transaction.body
        ]
        canonical_setup_guards = [guard for guard in canonical_setup_guards if guard is not None]
        if len(canonical_setup_guards) != 1:
            return False
        canonical_setup_assignments = canonical_setup_guards[0]

        def metadata_restore_kind(node: ast.AST) -> str | None:
            """Recognize one exact facade metadata restore call."""
            if not (
                isinstance(node, ast.Call)
                and isinstance(node.func, ast.Name)
                and node.func.id == "setattr"
                and len(node.args) == 3
                and not node.keywords
                and isinstance(node.args[0], ast.Name)
                and node.args[0].id == "_phase192_canonical_self"
                and isinstance(node.args[1], ast.Constant)
                and isinstance(node.args[2], ast.Name)
            ):
                return None
            if node.args[1].value == "__name__" and node.args[2].id == "_phase192_previous_name":
                return "name"
            if node.args[1].value == "__package__" and node.args[2].id == "_phase192_previous_package":
                return "package"
            return None

        def exact_metadata_restore_guard(statement: ast.AST) -> tuple[ast.Call, ast.Call] | None:
            """Recognize the exact guarded metadata restoration block."""
            if not (
                isinstance(statement, ast.If)
                and not statement.orelse
                and isinstance(statement.test, ast.Compare)
                and len(statement.test.ops) == 1
                and isinstance(statement.test.ops[0], ast.IsNot)
                and isinstance(statement.test.left, ast.Name)
                and statement.test.left.id == "_phase192_canonical_self"
                and len(statement.test.comparators) == 1
                and isinstance(statement.test.comparators[0], ast.Constant)
                and statement.test.comparators[0].value is None
                and len(statement.body) == 2
                and all(isinstance(item, ast.Expr) for item in statement.body)
            ):
                return None
            calls = tuple(item.value for item in statement.body)
            if not all(isinstance(call, ast.Call) for call in calls):
                return None
            return calls  # type: ignore[return-value]

        metadata_guards = [
            exact_metadata_restore_guard(statement)
            for statement in transaction.finalbody
        ]
        metadata_guards = [guard for guard in metadata_guards if guard is not None]
        if len(metadata_guards) != 1:
            return False
        metadata_calls = metadata_guards[0]
        if tuple(metadata_restore_kind(node) for node in metadata_calls) != ("name", "package"):
            return False
        allowed_metadata_call_ids = {id(node) for node in metadata_calls}
        for statement in transaction.finalbody:
            for node in ast.walk(statement):
                if (
                    isinstance(node, ast.Call)
                    and isinstance(node.func, ast.Name)
                    and node.func.id in {"setattr", "delattr"}
                    and id(node) not in allowed_metadata_call_ids
                ):
                    return False
        expected_top_level = {
            "_phase192_canonical_self",
            "_phase192_previous_submodules",
            "_phase192_previous_path",
            "_phase192_previous_name",
            "_phase192_previous_package",
        }
        transaction_binding_names = expected_top_level | {
            "_Phase192FacadeModule",
            "_phase192_types",
            "_phase192_module",
            "_phase192_self",
            "_phase192_package_module",
            "main",
            "__name__",
            "__package__",
            "SystemExit",
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
        }
        top_level_assignments = {
            target.id
            for statement in tree.body
            if isinstance(statement, ast.Assign)
            for target in statement.targets
            if isinstance(target, ast.Name)
        }
        if not expected_top_level.issubset(top_level_assignments):
            return False
        for name in expected_top_level:
            if sum(
                1
                for statement in tree.body
                if isinstance(statement, ast.Assign)
                for target in statement.targets
                if isinstance(target, ast.Name) and target.id == name
            ) != 1:
                return False
        critical_order = [
            "_phase192_canonical_self",
            "_phase192_previous_submodules",
            "_phase192_previous_path",
            "_phase192_previous_name",
            "_phase192_previous_package",
        ]
        critical_positions = {
            name: index
            for index, statement in enumerate(tree.body)
            if isinstance(statement, ast.Assign)
            for target in statement.targets
            if isinstance(target, ast.Name)
            and target.id in critical_order
            for name in [target.id]
        }
        if [critical_positions[name] for name in critical_order] != sorted(
            critical_positions.values()
        ):
            return False
        expected_values = {
            "_phase192_module": [
                "_phase192_sys.modules.get(__name__)",
                "_phase192_sys.modules.get(__name__)",
            ],
            "_phase192_canonical_self": [
                "_phase192_sys.modules.get(__name__) if __package__ and _phase192_sys.modules.get(__name__) is not None else None",
            ],
            "_phase192_previous_path": ["_phase192_sys.path[:]"],
            "_phase192_previous_name": [
                "__name__ if _phase192_canonical_self is not None else None",
            ],
            "_phase192_previous_package": [
                "__package__ if _phase192_canonical_self is not None else None",
            ],
            "_phase192_self": [
                "_phase192_sys.modules.get(__name__) if __name__ == 'validate_source_generator_dll' else None",
            ],
            "_phase192_package_module": [
                "_phase192_sys.modules.get(__package__ + '.validate_source_generator_dll')",
                "_phase192_sys.modules.get('validate_source_generator_dll')",
            ],
        }
        for name, expressions in expected_values.items():
            values = [
                node.value
                for node in ast.walk(tree)
                if isinstance(node, ast.Assign)
                and len(node.targets) == 1
                and isinstance(node.targets[0], ast.Name)
                and node.targets[0].id == name
            ]
            expected_nodes = [ast.parse(expression, mode="eval").body for expression in expressions]
            if len(values) != len(expected_nodes) or any(
                ast.dump(value, include_attributes=False)
                != ast.dump(expected, include_attributes=False)
                for value, expected in zip(values, expected_nodes)
            ):
                return False
        allowed_metadata_store_ids: set[int] = set()
        metadata_expressions = {
            "__name__": "__spec__.name if __spec__ is not None else __name__",
            "__package__": "__spec__.parent if __spec__ is not None else __package__",
        }
        for statement in tree.body:
            if not (
                isinstance(statement, ast.Assign)
                and len(statement.targets) == 1
                and isinstance(statement.targets[0], ast.Name)
                and statement.targets[0].id in metadata_expressions
            ):
                continue
            expected = ast.parse(
                metadata_expressions[statement.targets[0].id],
                mode="eval",
            ).body
            if ast.dump(statement.value, include_attributes=False) == ast.dump(
                expected,
                include_attributes=False,
            ):
                allowed_metadata_store_ids.add(id(statement.targets[0]))
        allowed_module_class_store_ids = {
            id(node.targets[0])
            for node in ast.walk(tree)
            if (
                isinstance(node, ast.Assign)
                and len(node.targets) == 1
                and isinstance(node.targets[0], ast.Attribute)
                and isinstance(node.targets[0].value, ast.Name)
                and node.targets[0].value.id == "_phase192_module"
                and node.targets[0].attr == "__class__"
                and isinstance(node.value, ast.Name)
                and node.value.id == "_Phase192FacadeModule"
            )
        }
        allowed_canonical_store_ids = {
            id(item.targets[0]) for item in canonical_setup_assignments
        }
        allowed_attribute_store_ids = set(allowed_module_class_store_ids)
        allowed_attribute_store_ids.update(allowed_canonical_store_ids)
        allowed_mutating_call_ids = set(allowed_metadata_call_ids)
        allowed_mutating_call_ids.update(
            call_id for _, call_id in cleanup_loops + restore_loops
        )
        def is_allowed_facade_forward_call(node: ast.Call) -> bool:
            """Recognize the exact facade-to-package attribute forwarding calls."""
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

        for statement in tree.body:
            if isinstance(statement, ast.ClassDef) and statement.name in {
                "_Phase192FacadeModule",
                "_Phase138UPackageModule",
                "_Phase192PackageModule",
            }:
                for node in ast.walk(statement):
                    if isinstance(node, ast.Call) and is_allowed_facade_forward_call(node):
                        allowed_mutating_call_ids.add(id(node))
        for loop_id, assignment_id in restore_loops:
            assignment = next(
                node
                for node in ast.walk(transaction)
                if isinstance(node, ast.Assign) and id(node) == assignment_id
            )
            allowed_attribute_store_ids.add(id(assignment.targets[0]))
        canonical_guard = next(
            statement
            for statement in transaction.body
            if exact_canonical_setup_guard(statement) is not None
        )
        registry_assignment = canonical_guard.body[2]
        registry_pop = canonical_guard.body[3].value
        allowed_attribute_store_ids.add(id(registry_assignment.targets[0]))
        allowed_mutating_call_ids.add(id(registry_pop))
        for node in ast.walk(transaction):
            if (
                isinstance(node, ast.Assign)
                and len(node.targets) == 1
                and isinstance(node.targets[0], ast.Subscript)
                and modules_value(node.targets[0].value)
                and isinstance(node.targets[0].slice, ast.Constant)
                and node.targets[0].slice.value == "validate_source_generator_dll"
                and isinstance(node.value, ast.Name)
                and node.value.id in {"_phase192_self", "_phase192_canonical_self"}
            ):
                allowed_attribute_store_ids.add(id(node.targets[0]))
        for statement in ast.walk(transaction):
            if not (
                isinstance(statement, ast.If)
                and not statement.orelse
                and isinstance(statement.test, ast.Compare)
                and len(statement.test.ops) == 1
                and isinstance(statement.test.ops[0], ast.IsNot)
                and isinstance(statement.test.left, ast.Name)
                and statement.test.left.id == "_phase192_self"
                and len(statement.test.comparators) == 1
                and isinstance(statement.test.comparators[0], ast.Constant)
                and statement.test.comparators[0].value is None
                and len(statement.body) == 1
                and isinstance(statement.body[0], ast.Expr)
            ):
                continue
            call = statement.body[0].value
            if (
                isinstance(call, ast.Call)
                and isinstance(call.func, ast.Attribute)
                and modules_value(call.func.value)
                and call.func.attr == "pop"
                and len(call.args) == 2
                and isinstance(call.args[0], ast.Constant)
                and call.args[0].value == "validate_source_generator_dll"
                and isinstance(call.args[1], ast.Constant)
                and call.args[1].value is None
                and not call.keywords
            ):
                allowed_mutating_call_ids.add(id(call))
        path_restore_expression = ast.parse(
            "_phase192_sys.path[:] = _phase192_previous_path",
            mode="exec",
        ).body[0]
        path_restore_target = path_restore_expression.targets[0]
        for node in ast.walk(transaction):
            if (
                isinstance(node, ast.Assign)
                and len(node.targets) == 1
                and ast.dump(node.targets[0], include_attributes=False)
                == ast.dump(path_restore_target, include_attributes=False)
                and isinstance(node.value, ast.Name)
                and node.value.id == "_phase192_previous_path"
            ):
                allowed_attribute_store_ids.add(id(node.targets[0]))
        path_insert_expression = ast.parse(
            "_phase192_sys.path.insert(0, str(_phase192_parent))",
            mode="eval",
        ).body
        for node in ast.walk(transaction):
            if (
                isinstance(node, ast.Call)
                and ast.dump(node, include_attributes=False)
                == ast.dump(path_insert_expression, include_attributes=False)
            ):
                allowed_mutating_call_ids.add(id(node))

        def mutation_root(node: ast.AST) -> str | None:
            """Resolve a direct critical binding mutation root."""
            roots = {
                "_phase192_sys",
                "_phase192_types",
                "_phase192_previous_path",
                "_phase192_self",
                "_phase192_package_module",
                "_phase192_module",
                "_phase192_canonical_self",
                "_Phase192FacadeModule",
                "main",
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
            }
            current = node
            while isinstance(current, (ast.Attribute, ast.Subscript)):
                current = current.value
            return current.id if isinstance(current, ast.Name) and current.id in roots else None

        critical_mutation_names = {
            "_phase192_sys",
            "_phase192_types",
            "_phase192_previous_path",
            "_phase192_self",
            "_phase192_package_module",
            "_phase192_module",
            "_phase192_canonical_self",
            "_Phase192FacadeModule",
            "main",
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
        }

        def contains_critical_mutation_name(node: ast.AST) -> bool:
            """Detect critical binding names anywhere in a mutation expression."""
            return any(
                isinstance(item, ast.Name) and item.id in critical_mutation_names
                for item in ast.walk(node)
            )

        def contains_globals_lookup(node: ast.AST) -> bool:
            """Detect a globals() lookup inside a mutation expression."""
            return any(
                isinstance(item, ast.Call)
                and isinstance(item.func, ast.Name)
                and item.func.id == "globals"
                for item in ast.walk(node)
            )

        def contains_protected_globals_store(node: ast.AST) -> bool:
            """Detect writes through globals() to protected binding names."""
            protected_names = critical_mutation_names | {"_phase192_name"}
            for item in ast.walk(node):
                if not (
                    isinstance(item, ast.Subscript)
                    and contains_globals_lookup(item.value)
                    and isinstance(item.slice, ast.Constant)
                    and item.slice.value in protected_names
                ):
                    continue
                return True
            return False

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

        protected_alias_sources = {
            "_phase192_sys",
            "_phase192_types",
            "_phase192_self",
            "_phase192_package_module",
            "_phase192_module",
            "_phase192_canonical_self",
            "_Phase192FacadeModule",
            "main",
            "__spec__",
            "__builtins__",
        }

        def contains_protected_alias_source(node: ast.AST) -> bool:
            """Detect values that would propagate a protected object through an alias."""
            return contains_globals_lookup(node) or any(
                isinstance(item, ast.Name) and item.id in protected_alias_sources
                for item in ast.walk(node)
            )

        def is_mutating_call(node: ast.Call) -> bool:
            """Identify calls that can mutate a critical binding target."""
            if isinstance(node.func, ast.Name) and node.func.id in {"setattr", "delattr"}:
                return True
            return (
                isinstance(node.func, ast.Attribute)
                and node.func.attr
                in {"pop", "insert", "append", "extend", "remove", "clear", "update", "__setattr__", "__delattr__"}
            )

        def iter_module_nodes() -> Iterator[ast.AST]:
            """Iterate module nodes, including forwarding class method bodies."""
            for statement in tree.body:
                yield from ast.walk(statement)

        for node in iter_module_nodes():
            if isinstance(node, ast.Assign):
                targets = node.targets
            elif isinstance(node, (ast.AnnAssign, ast.AugAssign, ast.NamedExpr)):
                targets = [node.target]
            elif isinstance(node, ast.Delete):
                targets = node.targets
            else:
                targets = []
            for target in targets:
                root = mutation_root(target) if isinstance(target, (ast.Attribute, ast.Subscript)) else None
                if (
                    isinstance(target, (ast.Attribute, ast.Subscript))
                    and (
                        contains_critical_mutation_name(target)
                        or contains_protected_globals_store(target)
                    )
                    and id(target) not in allowed_attribute_store_ids
                ):
                    return False
            if isinstance(node, ast.Call) and is_mutating_call(node):
                root = mutation_root(node.func.value) if isinstance(node.func, ast.Attribute) else (
                    mutation_root(node.args[0]) if node.args else None
                )
                if (
                    (
                        root is not None
                        or contains_critical_mutation_name(node)
                        or contains_protected_globals_store(node)
                    )
                    and id(node) not in allowed_mutating_call_ids
                ):
                    return False
            if isinstance(node, (ast.Assign, ast.AnnAssign, ast.AugAssign, ast.NamedExpr)):
                value = node.value
                targets = node.targets if isinstance(node, ast.Assign) else [node.target]
                if contains_protected_alias_source(value):
                    for target in targets:
                        if any(
                            name not in transaction_binding_names
                            for name in target_names(target)
                        ):
                            return False
        for node in ast.walk(tree):
            if isinstance(node, ast.Assign):
                targets = node.targets
            elif isinstance(node, (ast.AnnAssign, ast.AugAssign, ast.NamedExpr)):
                targets = [node.target]
            else:
                targets = []
            for target in targets:
                if isinstance(target, ast.Name) and target.id in {"__name__", "__package__"}:
                    if id(target) not in allowed_metadata_store_ids:
                        return False
                if (
                    isinstance(target, ast.Attribute)
                    and isinstance(target.value, ast.Name)
                    and target.value.id == "_phase192_module"
                    and id(target) not in allowed_module_class_store_ids
                ):
                    return False
                if (
                    isinstance(target, ast.Attribute)
                    and isinstance(target.value, ast.Name)
                    and target.value.id == "_phase192_canonical_self"
                    and id(target) not in allowed_canonical_store_ids
                ):
                    return False
                if (
                    isinstance(target, ast.Attribute)
                    and isinstance(target.value, ast.Name)
                    and target.value.id in {"_phase192_self", "_phase192_package_module"}
                ):
                    return False
        for statement in (*transaction.body, *transaction.finalbody):
            for node in ast.walk(statement):
                if (
                    isinstance(node, ast.Call)
                    and isinstance(node.func, ast.Name)
                    and node.func.id in {"setattr", "delattr"}
                    and node.args
                    and isinstance(node.args[0], ast.Name)
                    and node.args[0].id in {"_phase192_self", "_phase192_package_module"}
                ):
                    return False
        allowed_store_ids = {
            id(target)
            for statement in tree.body
            if isinstance(statement, ast.Assign)
            for target in statement.targets
            if isinstance(target, ast.Name)
            and target.id in expected_top_level
            and (
                target.id != "_phase192_previous_submodules"
                or exact_snapshot(statement)
            )
        }
        allowed_store_ids.update(allowed_metadata_store_ids)
        for node in ast.walk(tree):
            if not (
                isinstance(node, ast.Assign)
                and len(node.targets) == 1
                and isinstance(node.targets[0], ast.Name)
                and node.targets[0].id in {
                    "_phase192_module",
                    "_phase192_self",
                    "_phase192_package_module",
                }
            ):
                continue
            expected_nodes = [
                ast.parse(expression, mode="eval").body
                for expression in expected_values[node.targets[0].id]
            ]
            if any(
                ast.dump(node.value, include_attributes=False)
                == ast.dump(expected, include_attributes=False)
                for expected in expected_nodes
            ):
                allowed_store_ids.add(id(node.targets[0]))
        for loop_id, _ in cleanup_loops + restore_loops:
            loop = next(
                node for node in ast.walk(transaction)
                if isinstance(node, ast.For) and id(node) == loop_id
            )
            allowed_store_ids.add(id(loop.target))
        for node in ast.walk(tree):
            if isinstance(node, ast.Assign):
                targets = node.targets
            elif isinstance(node, (ast.AnnAssign, ast.AugAssign, ast.NamedExpr)):
                targets = [node.target]
            else:
                targets = []
            for target in targets:
                if isinstance(target, ast.Name) and target.id in transaction_binding_names:
                    if id(target) not in allowed_store_ids:
                        return False
                if (
                    rooted_in(target, "_phase192_previous_submodules")
                    and not (
                        isinstance(target, ast.Name)
                        and target.id == "_phase192_previous_submodules"
                        and isinstance(node, ast.Assign)
                        and exact_snapshot(node)
                        and node in tree.body
                    )
                ):
                    return False
                if isinstance(target, ast.Name) and target.id == "_phase192_sys":
                    return False
            if isinstance(node, ast.Delete) and any(
                rooted_in(target, "_phase192_previous_submodules")
                or isinstance(target, ast.Name)
                and target.id in transaction_binding_names | {"_phase192_sys"}
                for target in node.targets
            ):
                return False
            if (
                isinstance(node, ast.Call)
                and isinstance(node.func, ast.Attribute)
                and rooted_in(node.func.value, "_phase192_previous_submodules")
            ):
                return False
            if isinstance(node, ast.Name) and isinstance(node.ctx, ast.Store):
                if node.id in transaction_binding_names | {"_phase192_sys", "_phase192_name"}:
                    if id(node) not in allowed_store_ids:
                        return False
            if isinstance(node, ast.ExceptHandler) and node.name in (
                transaction_binding_names | {"_phase192_sys", "_phase192_name"}
            ):
                return False
            if isinstance(node, ast.MatchAs) and node.name in (
                transaction_binding_names | {"_phase192_sys", "_phase192_name"}
            ):
                return False
            if isinstance(node, ast.MatchStar) and node.name in (
                transaction_binding_names | {"_phase192_sys", "_phase192_name"}
            ):
                return False
        sys_imports = [
            alias
            for node in ast.walk(tree)
            if isinstance(node, ast.Import)
            for alias in node.names
            if alias.asname == "_phase192_sys"
        ]
        if len(sys_imports) != 1 or sys_imports[0].name != "sys":
            return False
        forbidden_import_bindings = (
            (transaction_binding_names - {"_phase192_types"})
            | {"_phase192_entrypoint", "_phase192_main", "_phase192_repository_root"}
            | {"_phase192_name"}
        )
        for node in ast.walk(tree):
            if isinstance(node, ast.Import):
                for alias in node.names:
                    bound = alias.asname or alias.name.split(".", 1)[0]
                    if alias.name in {"sys", "types"}:
                        expected = {
                            "sys": "_phase192_sys",
                            "types": "_phase192_types",
                        }[alias.name]
                        if bound != expected:
                            return False
                    if bound in forbidden_import_bindings:
                        return False
            elif isinstance(node, ast.ImportFrom):
                for alias in node.names:
                    bound = alias.asname or alias.name
                    if bound in forbidden_import_bindings:
                        return False
        for node in ast.walk(tree):
            if isinstance(node, ast.Assign):
                targets = node.targets
            elif isinstance(node, (ast.AnnAssign, ast.AugAssign, ast.NamedExpr)):
                targets = [node.target]
            else:
                targets = []
            if any(isinstance(target, ast.Name) and target.id == "_phase192_name" for target in targets):
                return False
        return True

    snapshot_bindings = [
        node
        for node in ast.walk(tree)
        if isinstance(node, ast.Assign)
        and any(
            isinstance(target, ast.Name)
            and target.id == "_phase192_previous_submodules"
            for target in node.targets
        )
    ]
    snapshot_assignments = [node for node in snapshot_bindings if exact_snapshot(node)]
    top_level_snapshot = [node for node in tree.body if exact_snapshot(node)]
    outer_transactions = [node for node in tree.body if isinstance(node, ast.Try)]
    cleanup_loops: list[tuple[int, int]] = []
    restore_loops: list[tuple[int, int]] = []
    transaction: ast.Try | None = None
    if (
        loader_path
        and len(snapshot_bindings) == len(snapshot_assignments) == len(top_level_snapshot) == 1
        and len(outer_transactions) == 1
    ):
        transaction = outer_transactions[0]
        cleanup_loops = [
            match for node in transaction.finalbody
            if (match := exact_cleanup_loop(node)) is not None
        ]
        restore_loops = [
            match for node in transaction.finalbody
            if (match := exact_restore_loop(node)) is not None
        ]
        final_name_loops = [
            node
            for node in transaction.finalbody
            if isinstance(node, ast.For)
            and isinstance(node.target, ast.Name)
            and node.target.id == "_phase192_name"
        ]
        final_name_rebindings = any(
            isinstance(node, (ast.Assign, ast.AnnAssign, ast.AugAssign, ast.NamedExpr))
            and any(
                isinstance(target, ast.Name) and target.id == "_phase192_name"
                for target in (
                    node.targets
                    if isinstance(node, ast.Assign)
                    else [node.target]
                    if isinstance(node, ast.AnnAssign)
                    else [node.target]
                )
            )
            for statement in transaction.finalbody
            for node in ast.walk(statement)
        )
    else:
        final_name_loops = []
        final_name_rebindings = False
    loader_transaction = (
        len(cleanup_loops) == 1
        and len(restore_loops) == 1
        and len(final_name_loops) == 2
        and not final_name_rebindings
        and critical_bindings_are_immutable(transaction)
    )
    cleanup_pop_ids = {cleanup_loops[0][1]} if loader_transaction else set()
    restore_assign_ids = {restore_loops[0][1]} if loader_transaction else set()

    for statement in ast.walk(tree):
        if isinstance(statement, ast.Assign):
            for target in statement.targets:
                if (
                    isinstance(target, ast.Name)
                    and modules_value(statement.value)
                ):
                    continue
                if contains_registry_reference(target):
                    dynamic_restore = loader_transaction and id(statement) in restore_assign_ids and (
                        isinstance(target, ast.Subscript)
                        and modules_value(target.value)
                        and isinstance(target.slice, ast.Name)
                        and target.slice.id == "_phase192_name"
                        and isinstance(statement.value, ast.Subscript)
                        and isinstance(statement.value.value, ast.Name)
                        and statement.value.value.id == "_phase192_previous_submodules"
                        and isinstance(statement.value.slice, ast.Name)
                        and statement.value.slice.id == "_phase192_name"
                    )
                    if not (
                        isinstance(target, ast.Subscript)
                        and modules_value(target.value)
                        and isinstance(target.slice, ast.Constant)
                        and isinstance(target.slice.value, str)
                        and target.slice.value in allowed_module_keys
                        and isinstance(statement.value, ast.Name)
                        and statement.value.id in {"_phase192_self", "_phase192_canonical_self"}
                    ) and not dynamic_restore:
                        raise ValueError(f"Unsupported generated module registry mutation in {path}")
        if isinstance(statement, (ast.AnnAssign, ast.AugAssign, ast.NamedExpr)):
            target = statement.target
            if contains_registry_reference(target):
                raise ValueError(f"Unsupported generated module registry mutation in {path}")
        if isinstance(statement, ast.Delete):
            if any(contains_registry_reference(target) for target in statement.targets):
                raise ValueError(f"Unsupported generated module registry deletion in {path}")
        node = statement
        if (
            isinstance(node, ast.Call)
            and node.args
            and (
                (
                    isinstance(node.func, ast.Name)
                    and node.func.id in {"setattr", "delattr"}
                    and contains_registry_reference(node.args[0])
                )
                or (
                    isinstance(node.func, ast.Attribute)
                    and node.func.attr in {"__setattr__", "__delattr__"}
                    and contains_registry_reference(node.func.value)
                )
            )
        ):
            raise ValueError(f"Unsupported generated module registry attribute mutation in {path}")
        if isinstance(node, ast.Call) and isinstance(node.func, ast.Attribute) and node.func.attr == "pop" and modules_value(node.func.value):
            if not (
                len(node.args) == 2
                and isinstance(node.args[0], ast.Constant)
                and isinstance(node.args[0].value, str)
                and node.args[0].value in allowed_module_keys
                and isinstance(node.args[1], ast.Constant)
                and node.args[1].value is None
                and not node.keywords
            ) and not (
                len(node.args) == 2
                and isinstance(node.args[0], ast.Name)
                and node.args[0].id == "_phase192_name"
                and isinstance(node.args[1], ast.Constant)
                and node.args[1].value is None
                and not node.keywords
                and loader_transaction
                and id(node) in cleanup_pop_ids
            ):
                raise ValueError(f"Unsupported generated module registry pop in {path}")

__all__ = ['_phase192_template_name', '_phase192_template_call_is_safe', '_phase192_template_validate_calls', '_phase192_template_validate_imports', '_phase192_template_validate_class', '_phase192_template_validate_facade', '_phase192_validate_template_registry_mutations']
