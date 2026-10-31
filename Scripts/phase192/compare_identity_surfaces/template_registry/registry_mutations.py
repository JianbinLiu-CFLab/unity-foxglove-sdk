from __future__ import annotations

from Scripts.phase192.compare_identity_surfaces.foundation import *
from .closure_mutations import _phase192_validate_closure_mutations
from .critical_bindings import _phase192_critical_bindings_are_immutable

def _phase192_validate_template_registry_mutations(
    tree: ast.Module,
    path: str,
    repository_root: Path | None = None,
) -> None:
    """Allow only the exact sys.modules forwarding mutations emitted by templates."""

    _phase192_validate_closure_mutations(tree, path)
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
        and _phase192_critical_bindings_are_immutable(
        tree,
        cleanup_loops,
        restore_loops,
        modules_value,
        rooted_in,
        exact_snapshot,
        transaction,
    )
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

__all__ = ['_phase192_validate_template_registry_mutations']
