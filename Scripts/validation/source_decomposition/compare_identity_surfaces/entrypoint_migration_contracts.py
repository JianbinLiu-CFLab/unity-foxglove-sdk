from Scripts.validation.source_decomposition.compare_identity_surfaces.foundation import *
from Scripts.validation.source_decomposition.compare_identity_surfaces.surface_contracts import (
    _entrypoint_initializer_exports,
    _entrypoint_module_bindings,
    _entrypoint_section_exports,
    _entrypoint_function_accepts_positional,
    _absolute_import_resolves_without_execution,
    _function_signature_is_inert,
    _public_globals_listcomp,
    _unconditional_top_level_bindings,
    _unconditional_top_level_names,
)
from Scripts.validation.source_decomposition.identity_surface_conditions import (
    _conditional_only_public_names,
)
def _entrypoint_resolve_module(package: Path, module: str) -> Path | None:
    """Resolve a relative module path to a regular file inside one package."""
    module_path = package.joinpath(*module.split("."))
    target = module_path.with_suffix(".py") if module_path.with_suffix(".py").is_file() else module_path / "__init__.py"
    if not target.is_file() or target.is_symlink():
        return None
    try:
        target.resolve().relative_to(package.resolve())
    except (OSError, ValueError):
        return None
    return target


def _entrypoint_binding_is_zero_arg_callable(
    package: Path,
    target: Path,
    name: str,
    seen: frozenset[Path] = frozenset(),
    *,
    positional_count: int = 0,
) -> bool:
    """Prove that one statically resolved binding accepts the requested call."""
    try:
        resolved_target = target.resolve()
        resolved_target.relative_to(package.resolve())
        tree = _parse_source(target.read_text(encoding="utf-8"), target)
    except (OSError, SyntaxError, ValueError):
        return False
    if resolved_target in seen:
        return False
    next_seen = seen | {resolved_target}
    for imported in tree.body:
        if isinstance(imported, ast.Import) or (
            isinstance(imported, ast.ImportFrom)
            and imported.level == 0
            and imported.module != "__future__"
        ):
            if not _absolute_import_resolves_without_execution(imported, target):
                return False
    if target.name != "__init__.py" and _entrypoint_section_exports(target) is None:
        return False
    if any(
        isinstance(node, ast.Name)
        and node.id == name
        and isinstance(node.ctx, (ast.Store, ast.Del))
        for node in ast.walk(tree)
    ):
        return False
    if target.name == "__init__.py":
        sections = [
            section
            for section in package.glob("*.py")
            if section.name not in {"__init__.py", "__main__.py"}
        ]
        package_exports: set[str] = set()
        for section in sections:
            exports = _entrypoint_section_exports(section)
            if exports is None:
                return False
            package_exports.update(exports)
        initializer_exports = _entrypoint_initializer_exports(package, package_exports)
        if initializer_exports is None or name not in initializer_exports:
            return False
    bindings = _unconditional_top_level_bindings(tree)
    binding = bindings.get(name)
    if _entrypoint_function_accepts_positional(binding, positional_count):
        return True
    if isinstance(binding, ast.ImportFrom):
        if binding.level != 1:
            return False
        alias = next(
            (
                candidate
                for candidate in binding.names
                if (candidate.asname or candidate.name) == name
            ),
            None,
        )
        if alias is None or alias.name == "*":
            return False
        if binding.module:
            target_module = _entrypoint_resolve_module(package, binding.module)
            return target_module is not None and _entrypoint_binding_is_zero_arg_callable(
                package,
                target_module,
                alias.name,
                next_seen,
                positional_count=positional_count,
            )
        module_target = _entrypoint_resolve_module(package, alias.name)
        if module_target is not None:
            return False
        initializer = package / "__init__.py"
        return initializer.is_file() and _entrypoint_binding_is_zero_arg_callable(
            package,
            initializer,
            alias.name,
            next_seen,
            positional_count=positional_count,
        )
    if target.name != "__init__.py":
        return False
    sections = [
        section
        for section in package.glob("*.py")
        if section.name not in {"__init__.py", "__main__.py"}
    ]
    return any(
        name in (_entrypoint_section_exports(section) or set())
        and _entrypoint_binding_is_zero_arg_callable(
            package,
            section,
            name,
            next_seen,
            positional_count=positional_count,
        )
        for section in sections
    )


def _entrypoint_package_main_is_callable(package: Path) -> bool:
    """Prove that the package wildcard export resolves to a zero-argument main."""
    initializer = package / "__init__.py"
    if not initializer.is_file():
        return False
    return _entrypoint_binding_is_zero_arg_callable(package, initializer, "main")


def _new_module_entrypoint_is_canonical(path: Path) -> bool:
    """Return whether a newly added module entrypoint dispatches to callable ``main``."""
    if not path.is_file() or path.is_symlink():
        return False
    try:
        path.resolve().relative_to(path.parent.resolve())
    except (OSError, ValueError):
        return False
    try:
        tree = _parse_source(path.read_text(encoding="utf-8"), path)
    except (OSError, SyntaxError, ValueError):
        return False
    body = list(tree.body)
    if body and isinstance(body[0], ast.Expr) and isinstance(body[0].value, ast.Constant):
        if not isinstance(body[0].value.value, str):
            return False
        body.pop(0)
    if len(body) < 2 or not isinstance(body[-1], ast.If):
        return False
    imports = body[:-1]
    if len(imports) != 1 or not isinstance(imports[0], ast.ImportFrom):
        return False
    imported = imports[0]
    if (
        imported.level != 1
        or len(imported.names) != 1
        or imported.names[0].asname is not None
        or imported.names[0].name not in {"*", "main"}
    ):
        return False
    guard = body[-1]
    if not (
        isinstance(guard.test, ast.Compare)
        and len(guard.test.ops) == 1
        and isinstance(guard.test.ops[0], ast.Eq)
        and isinstance(guard.test.left, ast.Name)
        and guard.test.left.id == "__name__"
        and len(guard.test.comparators) == 1
        and isinstance(guard.test.comparators[0], ast.Constant)
        and guard.test.comparators[0].value == "__main__"
        and not guard.orelse
        and len(guard.body) == 1
    ):
        return False
    statement = guard.body[0]
    if not (
        isinstance(statement, ast.Raise)
        and isinstance(statement.exc, ast.Call)
        and isinstance(statement.exc.func, ast.Name)
        and statement.exc.func.id == "SystemExit"
        and len(statement.exc.args) == 1
        and not statement.exc.keywords
        and isinstance(statement.exc.args[0], ast.Call)
        and isinstance(statement.exc.args[0].func, ast.Name)
        and statement.exc.args[0].func.id == "main"
        and not statement.exc.args[0].args
        and not statement.exc.args[0].keywords
    ):
        return False
    package = path.parent
    binds_main = False
    main_binding_count = 0
    for imported in body[:-1]:
        if not isinstance(imported, ast.ImportFrom) or imported.level != 1:
            continue
        for alias in imported.names:
            if alias.asname == "main" and alias.name != "main":
                return False
            if (alias.name == "*" and imported.module is None):
                if not _entrypoint_package_main_is_callable(package):
                    return False
                binds_main = True
                main_binding_count += 1
            elif alias.name == "main" and alias.asname is None:
                target = (
                    package / "__init__.py"
                    if imported.module is None
                    else _entrypoint_resolve_module(package, imported.module)
                )
                if target is None or not _entrypoint_binding_is_zero_arg_callable(
                    package,
                    target,
                    "main",
                ):
                    return False
                binds_main = True
                main_binding_count += 1
    return binds_main and main_binding_count == 1


def _stable_entrypoint_exports(target: Path) -> set[str]:
    """Return public bindings after rejecting unsafe export rebinding."""
    target_tree = _parse_source(target.read_text(encoding="utf-8"), target)
    for statement in ast.walk(target_tree):
        if isinstance(statement, ast.Assign):
            targets = statement.targets
            value = statement.value
        elif isinstance(statement, (ast.AnnAssign, ast.AugAssign)):
            targets = [statement.target]
            value = statement.value
        elif isinstance(statement, ast.Delete):
            targets = statement.targets
            value = None
        else:
            continue
        for target_node in targets:
            target_names = {
                candidate.id
                for candidate in ast.walk(target_node)
                if isinstance(candidate, ast.Name)
            }
            if "__all__" in target_names:
                if not isinstance(statement, ast.Assign):
                    return set()
                literal = None
                try:
                    literal = ast.literal_eval(value) if value is not None else None
                except (TypeError, ValueError, SyntaxError):
                    pass
                canonical_dynamic = (
                    isinstance(value, ast.Call)
                    and isinstance(value.func, ast.Name)
                    and value.func.id == "sorted"
                    and len(value.args) == 1
                    and isinstance(value.args[0], ast.Name)
                    and value.args[0].id == "_PHASE192_EXPORT_NAMES"
                    and not value.keywords
                )
                if not (
                    isinstance(literal, (list, tuple, set))
                    and all(isinstance(name, str) for name in literal)
                ) and not canonical_dynamic and not _public_globals_listcomp(value):
                    return set()
            if "_PHASE192_SECTION_MODULES" in target_names:
                if not isinstance(statement, ast.Assign) or not isinstance(value, ast.Tuple):
                    return set()
    names = _top_level_names(target_tree, include_imports=True)
    names.difference_update(_conditional_only_public_names(target_tree, include_private=True))
    return names

def _mechanically_transformed_entrypoint_body(facade: Path) -> tuple[ast.stmt, ...] | None:
    """Return the trusted facade main block after the standard mechanical rewrite."""
    try:
        tree = _parse_source(facade.read_text(encoding="utf-8"), facade)
    except (OSError, SyntaxError, ValueError):
        return None
    guards = [
        node
        for node in tree.body
        if (
            isinstance(node, ast.If)
            and isinstance(node.test, ast.Compare)
            and len(node.test.ops) == 1
            and isinstance(node.test.ops[0], ast.Eq)
            and isinstance(node.test.left, ast.Name)
            and node.test.left.id == "__name__"
            and len(node.test.comparators) == 1
            and isinstance(node.test.comparators[0], ast.Constant)
            and node.test.comparators[0].value == "__main__"
        )
    ]
    if len(guards) != 1 or guards[0].orelse:
        return None

    class MechanicalTransformer(ast.NodeTransformer):
        """Rewrite the trusted facade exit protocol for a callable wrapper."""

        def visit_Name(self, node: ast.Name) -> ast.AST:
            """Use the standard ``sys`` binding inside the new wrapper."""
            if node.id == "_phase192_sys":
                node.id = "sys"
            return node

        def visit_Raise(self, node: ast.Raise) -> ast.AST:
            """Convert one SystemExit return into the wrapper's return value."""
            exception = node.exc
            if (
                isinstance(exception, ast.Call)
                and isinstance(exception.func, ast.Name)
                and exception.func.id == "SystemExit"
                and len(exception.args) == 1
                and not exception.keywords
            ):
                return ast.copy_location(ast.Return(value=self.visit(exception.args[0])), node)
            return self.generic_visit(node)

    transformed = tuple(
        MechanicalTransformer().visit(copy.deepcopy(statement))
        for statement in guards[0].body
    )
    ast.fix_missing_locations(ast.Module(body=list(transformed), type_ignores=[]))
    if any(
        isinstance(node, ast.Raise)
        and isinstance(node.exc, ast.Call)
        and isinstance(node.exc.func, ast.Name)
        and node.exc.func.id == "SystemExit"
        for statement in transformed
        for node in ast.walk(statement)
    ):
        return None
    return transformed


def _mechanical_entrypoint_imports(
    facade: Path,
    expected_body: tuple[ast.stmt, ...],
) -> tuple[ast.Import | ast.ImportFrom, ...] | None:
    """Return the unique section imports required by a trusted legacy body."""
    allowed_names = {
        "BaseException", "Exception", "FileNotFoundError", "InconclusiveError", "KeyboardInterrupt",
        "RuntimeError", "SystemExit", "TimeoutError", "print",
    }
    delegate_names: list[str] = []
    for statement in expected_body:
        for node in ast.walk(statement):
            if not isinstance(node, ast.Call) or not isinstance(node.func, ast.Name):
                continue
            if node.func.id not in allowed_names and node.func.id not in delegate_names:
                delegate_names.append(node.func.id)
    package = facade.with_suffix("")
    sections = [
        path
        for path in sorted(package.glob("*.py"))
        if path.name not in {"__init__.py", "__main__.py", "entrypoint.py"}
    ]
    imports: list[ast.Import | ast.ImportFrom] = []
    if any(
        isinstance(node, ast.Name) and node.id == "sys" and isinstance(node.ctx, ast.Load)
        for statement in expected_body
        for node in ast.walk(statement)
    ):
        imports.append(ast.Import(names=[ast.alias(name="sys")]))
    for name in delegate_names:
        owners = []
        for section in sections:
            exports = _entrypoint_section_exports(section)
            if exports is not None and name in exports:
                owners.append(section.stem)
        if len(owners) != 1:
            return None
        imports.append(
            ast.ImportFrom(
                module=owners[0],
                names=[ast.alias(name=name)],
                level=1,
            )
        )
    return tuple(imports)


def _standard_entrypoint_body(entrypoint: Path) -> tuple[ast.stmt, ...] | None:
    """Return the trusted body of an already migrated run_entrypoint function."""
    try:
        tree = _parse_source(entrypoint.read_text(encoding="utf-8"), entrypoint)
    except (OSError, SyntaxError, ValueError):
        return None
    definitions = [
        node
        for node in tree.body
        if isinstance(node, ast.FunctionDef) and node.name == "run_entrypoint"
    ]
    if len(definitions) != 1:
        return None
    function = definitions[0]
    if function.decorator_list or not _function_signature_is_inert(function):
        return None
    return tuple(function.body) if function.body else None


def _entrypoint_body_matches_mechanical_transform(
    tree: ast.Module,
    path: Path,
    function: ast.FunctionDef,
    expected_body: tuple[ast.stmt, ...],
    expected_imports: tuple[ast.Import | ast.ImportFrom, ...] | None = None,
) -> bool:
    """Return whether a wrapper body is the trusted mechanical transformation."""
    full_body = list(function.body)
    body = list(full_body)
    imports: list[ast.Import | ast.ImportFrom] = []
    while body and isinstance(body[0], (ast.Import, ast.ImportFrom)):
        imports.append(body.pop(0))
    if expected_imports is None:
        body_matches = ast.dump(
            ast.Module(body=full_body, type_ignores=[]),
            include_attributes=False,
        ) == ast.dump(
            ast.Module(body=list(expected_body), type_ignores=[]),
            include_attributes=False,
        )
    else:
        body_matches = (
            bool(imports)
            and ast.dump(ast.Module(body=imports, type_ignores=[]), include_attributes=False)
            == ast.dump(ast.Module(body=list(expected_imports), type_ignores=[]), include_attributes=False)
            and ast.dump(ast.Module(body=body, type_ignores=[]), include_attributes=False)
            == ast.dump(ast.Module(body=list(expected_body), type_ignores=[]), include_attributes=False)
        )
    if not body_matches:
        return False
    call_counts: dict[str, set[int]] = {}
    for statement in body:
        for node in ast.walk(statement):
            if isinstance(node, ast.Call) and isinstance(node.func, ast.Name):
                call_counts.setdefault(node.func.id, set()).add(len(node.args))
    imported_names: set[str] = set()
    for node in imports:
        if isinstance(node, ast.Import):
            if any(alias.name != "sys" or alias.asname for alias in node.names):
                return False
            imported_names.update(alias.asname or alias.name.split(".", 1)[0] for alias in node.names)
            continue
        if node.level != 1 or not node.module or any(alias.name == "*" for alias in node.names):
            return False
        target_base = path.parent.joinpath(*node.module.split("."))
        target = target_base.with_suffix(".py") if target_base.with_suffix(".py").is_file() else target_base / "__init__.py"
        if not target.is_file() or target.is_symlink():
            return False
        try:
            target.resolve().relative_to(path.parent.resolve())
        except (OSError, ValueError):
            return False
        exports = _entrypoint_section_exports(target)
        bindings = _unconditional_top_level_names(_parse_source(target.read_text(encoding="utf-8"), target))
        if exports is None or any(alias.name not in exports or alias.name not in bindings for alias in node.names):
            return False
        for alias in node.names:
            bound_name = alias.asname or alias.name
            for positional_count in call_counts.get(bound_name, ()):
                if not _entrypoint_binding_is_zero_arg_callable(
                    path.parent,
                    target,
                    alias.name,
                    positional_count=positional_count,
                ):
                    return False
        imported_names.update(alias.asname or alias.name for alias in node.names)
    allowed_names = {
        "BaseException", "Exception", "FileNotFoundError", "InconclusiveError", "KeyboardInterrupt",
        "RuntimeError", "SystemExit", "TimeoutError", "print",
    }
    loaded_names = {
        node.id
        for statement in body
        for node in ast.walk(statement)
        if isinstance(node, ast.Name) and isinstance(node.ctx, ast.Load)
    }
    bound_names = {
        node.id
        for statement in body
        for node in ast.walk(statement)
        if isinstance(node, ast.Name) and isinstance(node.ctx, ast.Store)
    }
    bound_names.update(
        node.name
        for statement in body
        for node in ast.walk(statement)
        if isinstance(node, ast.ExceptHandler) and node.name
    )
    return loaded_names <= imported_names | allowed_names | bound_names



def _entrypoint_executable_body_is_safe(
    tree: ast.Module,
    path: Path,
    function: ast.FunctionDef,
    expected_body: tuple[ast.stmt, ...] | None = None,
    expected_imports: tuple[ast.Import | ast.ImportFrom, ...] | None = None,
) -> bool:
    """Return whether the forwarding function has only reviewed executable statements."""
    if expected_body is not None:
        return _entrypoint_body_matches_mechanical_transform(
            tree,
            path,
            function,
            expected_body,
            expected_imports,
        )
    imported_names: set[str] = set()
    delegate_names: set[str] = set()
    dangerous_bound_names: set[str] = set()
    for statement in tree.body:
        if isinstance(statement, ast.Import):
            for alias in statement.names:
                if alias.name == "*":
                    return False
                imported_names.add(alias.asname or alias.name.split(".", 1)[0])
        elif isinstance(statement, ast.ImportFrom):
            for alias in statement.names:
                if alias.name == "*":
                    return False
                bound = alias.asname or alias.name
                imported_names.add(bound)
                if statement.module == "os" and alias.name in {"environ", "putenv", "unsetenv"}:
                    dangerous_bound_names.add(bound)
    for statement in ast.walk(function):
        if isinstance(statement, ast.Import):
            if any(
                alias.name == "*"
                or alias.name not in {"sys", "argparse", "json", "os", "pathlib", "time", "typing"}
                for alias in statement.names
            ):
                return False
            for alias in statement.names:
                imported_names.add(alias.asname or alias.name.split(".", 1)[0])
        elif isinstance(statement, ast.ImportFrom):
            if statement.level != 1 or not statement.module or any(alias.name == "*" for alias in statement.names):
                return False
            module_path = path.parent / Path(*statement.module.split("."))
            target = module_path.with_suffix(".py") if module_path.with_suffix(".py").is_file() else module_path / "__init__.py"
            if not target.is_file() or target.is_symlink():
                return False
            try:
                target.resolve().relative_to(path.parent.resolve())
            except (OSError, ValueError):
                return False
            target_tree = _parse_source(target.read_text(encoding="utf-8"), target)
            bound_names = _unconditional_top_level_names(target_tree)
            exported: set[str] = set()
            for target_node in target_tree.body:
                if not (
                    isinstance(target_node, ast.Assign)
                    and any(isinstance(item, ast.Name) and item.id == "__all__" for item in target_node.targets)
                ):
                    continue
                try:
                    declared = ast.literal_eval(target_node.value)
                except (TypeError, ValueError, SyntaxError):
                    continue
                if isinstance(declared, (list, tuple, set)):
                    exported.update(name for name in declared if isinstance(name, str))
            for alias in statement.names:
                if alias.name not in bound_names or (exported and alias.name not in exported):
                    return False
                imported_names.add(alias.asname or alias.name)
                delegate_names.add(alias.asname or alias.name)
        elif isinstance(statement, ast.Attribute) and statement.attr in {
            "environ", "open", "read_text", "read_bytes", "write_text", "write_bytes",
            "unlink", "remove", "replace", "rename", "mkdir", "rmdir", "chmod",
            "system", "popen", "run", "Popen", "call", "check_call", "check_output",
        }:
            return False
        elif isinstance(statement, ast.Name) and statement.id in {
            "__import__", "compile", "eval", "exec", "globals", "locals", "vars", "open",
        }:
            return False
        elif isinstance(statement, ast.Call):
            if isinstance(statement.func, ast.Attribute):
                return False
            if not isinstance(statement.func, ast.Name) or statement.func.id not in delegate_names:
                return False
            if any(
                not (
                    isinstance(argument, ast.Subscript)
                    and isinstance(argument.value, ast.Attribute)
                    and isinstance(argument.value.value, ast.Name)
                    and argument.value.value.id == "sys"
                    and argument.value.attr == "argv"
                )
                for argument in statement.args
            ):
                return False
            if statement.keywords:
                return False
    if dangerous_bound_names & imported_names:
        return False
    body = list(function.body)
    if not body or not isinstance(body[-1], ast.Return):
        return False
    if any(not isinstance(statement, (ast.Import, ast.ImportFrom, ast.Return)) for statement in body):
        return False
    if body[-1].value is None or isinstance(body[-1].value, ast.Constant):
        return True
    return (
        isinstance(body[-1].value, ast.Call)
        and isinstance(body[-1].value.func, ast.Name)
        and body[-1].value.func.id in delegate_names
    )



def _facade_already_has_standard_run_entrypoint(facade: Path) -> bool:
    """Return whether a facade already delegates through run_entrypoint."""
    try:
        tree = _parse_source(facade.read_text(encoding="utf-8"), facade)
    except (OSError, SyntaxError, ValueError):
        return False
    guards = [
        node
        for node in tree.body
        if (
            isinstance(node, ast.If)
            and isinstance(node.test, ast.Compare)
            and len(node.test.ops) == 1
            and isinstance(node.test.ops[0], ast.Eq)
            and isinstance(node.test.left, ast.Name)
            and node.test.left.id == "__name__"
            and len(node.test.comparators) == 1
            and isinstance(node.test.comparators[0], ast.Constant)
            and node.test.comparators[0].value == "__main__"
            and not node.orelse
        )
    ]
    if len(guards) != 1 or len(guards[0].body) != 1:
        return False
    statement = guards[0].body[0]
    return (
        isinstance(statement, ast.Raise)
        and isinstance(statement.exc, ast.Call)
        and isinstance(statement.exc.func, ast.Name)
        and statement.exc.func.id == "SystemExit"
        and len(statement.exc.args) == 1
        and not statement.exc.keywords
        and isinstance(statement.exc.args[0], ast.Call)
        and isinstance(statement.exc.args[0].func, ast.Name)
        and statement.exc.args[0].func.id == "run_entrypoint"
        and not statement.exc.args[0].args
        and not statement.exc.args[0].keywords
    )

def _approved_main_block_only_contract(
    base_root: Path,
    head_root: Path,
    base_relative: str,
    head_relative: str,
) -> bool:
    """Return whether only a safe main block was appended to a package entrypoint."""
    if base_relative != head_relative:
        return False
    base_facade = base_root / base_relative
    head_facade = head_root / head_relative
    base_package = base_facade.with_suffix("")
    head_package = head_facade.with_suffix("")
    base_main = base_package / "__main__.py"
    head_main = head_package / "__main__.py"
    if (
        not base_facade.is_file()
        or not head_facade.is_file()
        or base_facade.is_symlink()
        or head_facade.is_symlink()
        or base_package.is_symlink()
        or head_package.is_symlink()
        or not base_main.is_file()
        or not head_main.is_file()
        or base_main.is_symlink()
        or head_main.is_symlink()
    ):
        return False
    if base_facade.read_bytes() != head_facade.read_bytes():
        return False

    def relative_files(package: Path) -> set[str]:
        """Return all package files relative to the package directory."""
        if any(path.is_symlink() for path in package.rglob("*")):
            return set()
        return {
            path.relative_to(package).as_posix()
            for path in package.rglob("*")
            if path.is_file() and path != package / "__main__.py"
        }

    base_files = relative_files(base_package)
    head_files = relative_files(head_package)
    if not base_files or base_files != head_files:
        return False
    for relative in base_files:
        if (base_package / relative).read_bytes() != (head_package / relative).read_bytes():
            return False

    try:
        base_tree = _parse_source(base_main.read_text(encoding="utf-8"), base_main)
        head_tree = _parse_source(head_main.read_text(encoding="utf-8"), head_main)
    except (OSError, SyntaxError, ValueError):
        return False
    if len(head_tree.body) != len(base_tree.body) + 1:
        return False
    if any(
        ast.dump(base_node, include_attributes=False)
        != ast.dump(head_node, include_attributes=False)
        for base_node, head_node in zip(base_tree.body, head_tree.body)
    ):
        return False

    def is_main_guard(node: ast.stmt) -> bool:
        """Return whether a top-level statement is a module main guard."""
        if not isinstance(node, ast.If) or not isinstance(node.test, ast.Compare):
            return False
        if len(node.test.ops) != 1:
            return False
        if not isinstance(node.test.ops[0], ast.Eq):
            return False
        left, right = node.test.left, node.test.comparators[0]
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

    if any(is_main_guard(node) for node in base_tree.body):
        return False
    main_block = head_tree.body[-1]
    if not (
        isinstance(main_block, ast.If)
        and not main_block.orelse
        and isinstance(main_block.test, ast.Compare)
        and isinstance(main_block.test.left, ast.Name)
        and main_block.test.left.id == "__name__"
        and len(main_block.test.ops) == 1
        and isinstance(main_block.test.ops[0], ast.Eq)
        and len(main_block.test.comparators) == 1
        and isinstance(main_block.test.comparators[0], ast.Constant)
        and main_block.test.comparators[0].value == "__main__"
        and len(main_block.body) == 1
        and isinstance(main_block.body[0], ast.Raise)
    ):
        return False
    raise_node = main_block.body[0]
    if not (
        isinstance(raise_node.exc, ast.Call)
        and isinstance(raise_node.exc.func, ast.Name)
        and raise_node.exc.func.id == "SystemExit"
        and len(raise_node.exc.args) == 1
        and not raise_node.exc.keywords
        and isinstance(raise_node.exc.args[0], ast.Call)
        and isinstance(raise_node.exc.args[0].func, ast.Name)
        and not raise_node.exc.args[0].args
        and not raise_node.exc.args[0].keywords
    ):
        return False
    called_name = raise_node.exc.args[0].func.id
    if called_name != "main":
        return False
    if any(
        isinstance(node, ast.Name)
        and node.id == called_name
        and isinstance(node.ctx, (ast.Store, ast.Del))
        for node in ast.walk(base_tree)
    ):
        return False

    def section_exports(section: Path) -> set[str]:
        """Return names exported by one existing package section."""
        exports = _entrypoint_section_exports(section)
        return set() if exports is None else exports

    def top_level_binding_names(node: ast.stmt) -> set[str]:
        """Return names a top-level statement can bind in a package section."""
        if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef, ast.ClassDef)):
            return {node.name}
        if isinstance(node, ast.Import):
            return {alias.asname or alias.name.split(".", 1)[0] for alias in node.names}
        if isinstance(node, ast.ImportFrom):
            return {
                alias.asname or alias.name
                for alias in node.names
                if alias.name != "*"
            } | ({"*"} if any(alias.name == "*" for alias in node.names) else set())
        names = {
            child.id
            for child in ast.walk(node)
            if isinstance(child, ast.Name) and isinstance(child.ctx, ast.Store)
        }
        names.update(
            child.name
            for child in ast.walk(node)
            if isinstance(child, ast.ExceptHandler) and child.name
        )
        return names

    base_sections = [
        path
        for path in base_package.glob("*.py")
        if path.name not in {"__init__.py", "__main__.py"}
    ]
    candidate = next(
        (
            section
            for section in base_sections
            for tree in (_parse_source(section.read_text(encoding="utf-8"), section),)
            for node_index, node in enumerate(tree.body)
            if isinstance(node, ast.FunctionDef)
            and node.name == called_name
            and not node.decorator_list
            and not node.args.vararg
            and not node.args.kwarg
            and len(node.args.defaults) == len(node.args.posonlyargs) + len(node.args.args)
            and all(default is not None for default in node.args.kw_defaults)
            and sum(
                isinstance(candidate_node, ast.FunctionDef)
                and candidate_node.name == called_name
                for candidate_node in tree.body
            ) == 1
            and max(
                binding_index
                for binding_index, candidate_node in enumerate(tree.body)
                if called_name in top_level_binding_names(candidate_node)
                or "*" in top_level_binding_names(candidate_node)
            ) == node_index
        ),
        None,
    )
    if candidate is None:
        return False
    import_nodes = [
        node
        for node in base_tree.body
        if isinstance(node, ast.ImportFrom) and node.level == 1
    ]
    if not import_nodes:
        return False
    if any(
        isinstance(node, ast.ImportFrom)
        and node.level
        and node not in import_nodes
        for node in ast.walk(base_tree)
    ):
        return False
    package_exports = set().union(*(section_exports(section) for section in base_sections))

    package_initializer_exports = _entrypoint_initializer_exports(base_package, package_exports)
    exports_by_module = {
        section.stem: section_exports(section)
        for section in base_sections
    }
    delegate_found = False
    delegate_binding_count = 0
    for node in import_nodes:
        if node.module is None:
            if package_initializer_exports is None:
                return False
            for alias in node.names:
                if alias.name == "*":
                    if called_name not in package_initializer_exports:
                        return False
                    delegate_found = True
                    delegate_binding_count += 1
                elif alias.name not in package_initializer_exports:
                    return False
                elif (alias.asname or alias.name) == called_name:
                    delegate_found = True
                    delegate_binding_count += 1
            continue
        module_exports = exports_by_module.get(node.module)
        if module_exports is None:
            return False
        module_bindings = _entrypoint_module_bindings(base_package, node.module)
        if not module_bindings:
            return False
        for alias in node.names:
            if alias.name == "*":
                if called_name not in module_exports or called_name not in module_bindings:
                    return False
                delegate_found = True
                delegate_binding_count += 1
                continue
            if alias.name not in module_exports or alias.name not in module_bindings:
                return False
            if (alias.asname or alias.name) == called_name:
                delegate_found = True
                delegate_binding_count += 1
    return delegate_found and delegate_binding_count == 1


def _approved_entrypoint_contract_impl(
    root: Path,
    relative: str,
    *,
    base_root: Path | None,
    base_relative: str | None,
    entrypoint_block_is_standard,
    approved_entrypoint_section,
) -> bool:
    """Validate a standard entrypoint using a trusted legacy or migrated body."""
    facade = root / relative
    package = facade.with_suffix("")
    module_entrypoint = package / "__main__.py"
    if not facade.is_file() or not module_entrypoint.is_file():
        return False
    try:
        facade_tree = _parse_source(facade.read_text(encoding="utf-8"), facade)
        module_tree = _parse_source(module_entrypoint.read_text(encoding="utf-8"), module_entrypoint)
    except (OSError, SyntaxError, ValueError):
        return False
    expected_body = None
    expected_imports = None
    if base_root is not None:
        base_facade = base_root / (base_relative or relative)
        base_is_standard = _facade_already_has_standard_run_entrypoint(base_facade)
        if base_is_standard:
            expected_body = _standard_entrypoint_body(
                base_facade.with_suffix("") / "entrypoint.py"
            )
        else:
            expected_body = _mechanically_transformed_entrypoint_body(base_facade)
            if expected_body is not None:
                expected_imports = _mechanical_entrypoint_imports(base_facade, expected_body)
        if expected_body is None or (not base_is_standard and expected_imports is None):
            return False
    return (
        _facade_entrypoint_is_standard(facade_tree, relative)
        and entrypoint_block_is_standard(
            module_tree,
            import_level=1,
            import_module="entrypoint",
        )
        and approved_entrypoint_section(
            package / "entrypoint.py",
            expected_body=expected_body,
            expected_imports=expected_imports,
        )
    )


def _approved_initializer_contract_impl(
    root: Path,
    relative: str,
    *,
    base_root: Path | None,
    base_relative: str | None,
) -> bool:
    """Validate the exact trusted-base initializer transition."""
    if base_root is None:
        return False
    head_signature = _initializer_exact_identity_signature(root, relative)
    base_signature = _initializer_exact_identity_signature(
        base_root,
        base_relative or relative,
        allow_legacy_hook_removal=True,
    )
    if head_signature is None or base_signature is None:
        return False
    return _initializer_signatures_compatible(base_signature, head_signature)


__all__ = []
