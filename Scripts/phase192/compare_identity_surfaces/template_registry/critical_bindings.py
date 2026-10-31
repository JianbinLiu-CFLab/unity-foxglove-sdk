from __future__ import annotations

from Scripts.phase192.compare_identity_surfaces.foundation import *

def _phase192_critical_bindings_are_immutable(
    tree: ast.Module,
    cleanup_loops,
    restore_loops,
    modules_value,
    rooted_in,
    exact_snapshot,
    transaction: ast.Try | None,
) -> bool:
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

__all__ = ['_phase192_critical_bindings_are_immutable']
