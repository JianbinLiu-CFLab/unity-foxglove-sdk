from .foundation import *
from .surface_contracts import (
    _entrypoint_initializer_exports,
    _entrypoint_module_bindings,
    _entrypoint_section_exports,
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
    package_exports = set().union(*(section_exports(section) for section in base_sections))

    package_initializer_exports = _entrypoint_initializer_exports(base_package, package_exports)
    exports_by_module = {
        section.stem: section_exports(section)
        for section in base_sections
    }
    for node in import_nodes:
        if node.module is None:
            if package_initializer_exports is None:
                continue
            if any(alias.name == "*" for alias in node.names) and called_name in package_initializer_exports:
                return True
            if any((alias.asname or alias.name) == called_name and alias.name in package_initializer_exports for alias in node.names):
                return True
            continue
        module_exports = exports_by_module.get(node.module)
        if module_exports is None:
            continue
        module_bindings = _entrypoint_module_bindings(base_package, node.module)
        if any(alias.name == "*" for alias in node.names) and called_name in module_exports:
            if called_name in module_bindings:
                return True
        if any((alias.asname or alias.name) == called_name and alias.name in module_exports and alias.name in module_bindings for alias in node.names):
            return True
    return False


__all__ = ['_facade_already_has_standard_run_entrypoint', '_approved_main_block_only_contract']
