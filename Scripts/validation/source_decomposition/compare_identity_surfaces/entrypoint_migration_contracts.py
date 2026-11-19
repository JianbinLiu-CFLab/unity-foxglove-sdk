from Scripts.validation.source_decomposition.compare_identity_surfaces.foundation import *
from Scripts.validation.source_decomposition.compare_identity_surfaces.surface_contracts import (
    _entrypoint_section_exports,
    _function_signature_is_inert,
)
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
    bound_names = set()
    loaded_names = set()
    for statement in expected_body:
        for node in ast.walk(statement):
            if isinstance(node, ast.Name):
                (loaded_names if isinstance(node.ctx, ast.Load) else bound_names).add(node.id)
            elif isinstance(node, ast.ExceptHandler) and node.name:
                bound_names.add(node.name)
            elif isinstance(node, ast.alias):
                bound_names.add(node.asname or node.name.split(".", 1)[0])
    if loaded_names - (allowed_names | set(delegate_names) | bound_names | {"sys"}):
        return None
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
    """Return whether a wrapper body matches the trusted mechanical transformation."""
    full_body = list(function.body)
    if (
        full_body
        and isinstance(full_body[0], ast.Expr)
        and isinstance(full_body[0].value, ast.Constant)
        and type(full_body[0].value.value) is str
    ):
        full_body.pop(0)
    expected_body_for_comparison = list(expected_body)
    if (
        expected_body_for_comparison
        and isinstance(expected_body_for_comparison[0], ast.Expr)
        and isinstance(expected_body_for_comparison[0].value, ast.Constant)
        and type(expected_body_for_comparison[0].value.value) is str
    ):
        expected_body_for_comparison.pop(0)
    body = list(full_body)
    imports: list[ast.Import | ast.ImportFrom] = []
    while body and isinstance(body[0], (ast.Import, ast.ImportFrom)):
        imports.append(body.pop(0))
    if expected_imports is None:
        return ast.dump(
            ast.Module(body=full_body, type_ignores=[]),
            include_attributes=False,
        ) == ast.dump(
            ast.Module(body=expected_body_for_comparison, type_ignores=[]),
            include_attributes=False,
        )
    return (
        bool(imports)
        and ast.dump(
            ast.Module(body=imports, type_ignores=[]),
            include_attributes=False,
        ) == ast.dump(
            ast.Module(body=list(expected_imports), type_ignores=[]),
            include_attributes=False,
        )
        and ast.dump(
            ast.Module(body=body, type_ignores=[]),
            include_attributes=False,
        ) == ast.dump(
            ast.Module(body=expected_body_for_comparison, type_ignores=[]),
            include_attributes=False,
        )
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
    """Return whether a package gained only its facade's exact main guard."""
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
        or not base_package.is_dir()
        or not head_package.is_dir()
        or base_package.is_symlink()
        or head_package.is_symlink()
        or not base_main.is_file()
        or not head_main.is_file()
        or base_main.is_symlink()
        or head_main.is_symlink()
        or base_facade.read_bytes() != head_facade.read_bytes()
    ):
        return False

    def relative_files(package: Path) -> set[str]:
        """Return package files other than the module entrypoint."""
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
    if any(
        (base_package / relative).read_bytes() != (head_package / relative).read_bytes()
        for relative in base_files
    ):
        return False

    try:
        base_main_tree = _parse_source(base_main.read_text(encoding="utf-8"), base_main)
        head_main_tree = _parse_source(head_main.read_text(encoding="utf-8"), head_main)
        base_facade_tree = _parse_source(
            base_facade.read_text(encoding="utf-8"),
            base_facade,
        )
    except (OSError, SyntaxError, ValueError):
        return False
    if len(head_main_tree.body) != len(base_main_tree.body) + 1:
        return False
    if any(
        ast.dump(base_node, include_attributes=False)
        != ast.dump(head_node, include_attributes=False)
        for base_node, head_node in zip(base_main_tree.body, head_main_tree.body)
    ):
        return False
    base_entrypoint_statements = [
        node
        for node in base_main_tree.body
        if not (
            isinstance(node, ast.Expr)
            and isinstance(node.value, ast.Constant)
            and isinstance(node.value.value, str)
        )
        and not (
            isinstance(node, ast.ImportFrom)
            and node.level == 0
            and node.module == "__future__"
            and all(alias.name == "annotations" for alias in node.names)
        )
    ]
    if len(base_entrypoint_statements) != 1:
        return False
    wildcard_import = base_entrypoint_statements[0]
    if not (
        isinstance(wildcard_import, ast.ImportFrom)
        and wildcard_import.level == 1
        and wildcard_import.module is None
        and len(wildcard_import.names) == 1
        and wildcard_import.names[0].name == "*"
        and wildcard_import.names[0].asname is None
    ):
        return False

    def main_guard(tree: ast.Module) -> ast.If | None:
        """Return one exact facade main guard."""
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
        return guards[0] if len(guards) == 1 and len(guards[0].body) == 1 else None

    facade_guard = main_guard(base_facade_tree)
    if facade_guard is None:
        return False
    main_block = head_main_tree.body[-1]
    if ast.dump(main_block, include_attributes=False) != ast.dump(
        facade_guard,
        include_attributes=False,
    ):
        return False
    called_names = [
        node.func.id
        for node in ast.walk(facade_guard)
        if isinstance(node, ast.Call)
        and isinstance(node.func, ast.Name)
        and node.func.id != "SystemExit"
    ]
    if len(called_names) != 1:
        return False
    called_name = called_names[0]
    package_name = base_package.name
    star_imports = [
        node
        for node in ast.walk(base_facade_tree)
        if (
            isinstance(node, ast.ImportFrom)
            and node.names
            and len(node.names) == 1
            and node.names[0].name == "*"
            and node.names[0].asname is None
            and node.lineno < facade_guard.lineno
        )
    ]
    if not star_imports or any(
        not (
            (node.level == 1 and node.module in {None, package_name})
            or (node.level == 0 and node.module == package_name)
        )
        for node in star_imports
    ) or any(node.lineno >= facade_guard.lineno for node in star_imports):
        return False
    for node in ast.walk(base_facade_tree):
        if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef, ast.ClassDef)) and node.name == called_name:
            return False
        if isinstance(node, ast.Name) and node.id == called_name and isinstance(node.ctx, (ast.Store, ast.Del)):
            return False
        if isinstance(node, (ast.Import, ast.ImportFrom)):
            for alias in node.names:
                if alias.name != "*" and (alias.name == called_name or alias.asname == called_name):
                    return False
    return True


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
    if (
        not facade.is_file()
        or facade.is_symlink()
        or not package.is_dir()
        or package.is_symlink()
        or not module_entrypoint.is_file()
        or module_entrypoint.is_symlink()
    ):
        return False
    try:
        facade_tree = _parse_source(facade.read_text(encoding="utf-8"), facade)
        module_tree = _parse_source(module_entrypoint.read_text(encoding="utf-8"), module_entrypoint)
    except (OSError, SyntaxError, ValueError):
        return False
    expected_body = None
    expected_imports = None
    expected_module_imports = None
    base_is_standard = False
    if base_root is not None:
        base_facade = base_root / (base_relative or relative)
        base_is_standard = _facade_already_has_standard_run_entrypoint(base_facade)
        if base_is_standard:
            base_entrypoint = base_facade.with_suffix("") / "entrypoint.py"
            expected_body = _standard_entrypoint_body(base_entrypoint)
            try:
                base_entrypoint_tree = _parse_source(
                    base_entrypoint.read_text(encoding="utf-8"),
                    base_entrypoint,
                )
            except (OSError, SyntaxError, ValueError):
                return False
            expected_module_imports = tuple(
                node
                for node in base_entrypoint_tree.body
                if isinstance(node, (ast.Import, ast.ImportFrom))
            )
        else:
            expected_body = _mechanically_transformed_entrypoint_body(base_facade)
            if expected_body is not None:
                expected_imports = _mechanical_entrypoint_imports(base_facade, expected_body)
        if expected_body is None or (not base_is_standard and expected_imports is None):
            return False
    if not (
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
    ):
        return False
    if base_is_standard:
        head_entrypoint = package / "entrypoint.py"
        try:
            head_entrypoint_tree = _parse_source(
                head_entrypoint.read_text(encoding="utf-8"),
                head_entrypoint,
            )
        except (OSError, SyntaxError, ValueError):
            return False
        head_module_imports = tuple(
            node
            for node in head_entrypoint_tree.body
            if isinstance(node, (ast.Import, ast.ImportFrom))
        )
        return ast.dump(
            ast.Module(body=list(head_module_imports), type_ignores=[]),
            include_attributes=False,
        ) == ast.dump(
            ast.Module(body=list(expected_module_imports or ()), type_ignores=[]),
            include_attributes=False,
        )
    return True


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
    for root_path, root_relative in (
        (root, relative),
        (base_root, base_relative or relative),
    ):
        package = root_path / root_relative.removesuffix(".py")
        initializer = package / "__init__.py"
        if (
            not package.is_dir()
            or package.is_symlink()
            or not initializer.is_file()
            or initializer.is_symlink()
        ):
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
