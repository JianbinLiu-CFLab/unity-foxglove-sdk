from __future__ import annotations

from .foundation import *
from .surface_contracts import *

def _is_safe_unittest_main_call(call: ast.Call) -> bool:
    """Return whether a unittest.main call has only literal safe options."""
    return (
        isinstance(call.func, ast.Attribute)
        and isinstance(call.func.value, ast.Name)
        and call.func.value.id == "unittest"
        and call.func.attr == "main"
        and not call.args
        and all(
            keyword.arg in {"module", "exit"}
            and (
                (
                    keyword.arg == "exit"
                    and isinstance(keyword.value, ast.Constant)
                    and isinstance(keyword.value.value, bool)
                )
                or (
                    keyword.arg == "module"
                    and (
                        isinstance(keyword.value, ast.Constant)
                        and (keyword.value.value is None or isinstance(keyword.value.value, str))
                        or isinstance(keyword.value, ast.Name)
                        and keyword.value.id == "__name__"
                    )
                )
            )
            for keyword in call.keywords
        )
    )

def _is_unittest_module_entrypoint(tree: ast.Module) -> bool:
    """Return whether an entrypoint explicitly delegates to unittest.main."""
    for node in tree.body:
        if not isinstance(node, ast.If):
            continue
        if not (
            isinstance(node.test, ast.Compare)
            and len(node.test.ops) == 1
            and isinstance(node.test.ops[0], ast.Eq)
            and isinstance(node.test.left, ast.Name)
            and node.test.left.id == "__name__"
            and len(node.test.comparators) == 1
            and isinstance(node.test.comparators[0], ast.Constant)
            and node.test.comparators[0].value == "__main__"
            and not node.orelse
            and len(node.body) == 1
        ):
            continue
        statement = node.body[0]
        if not isinstance(statement, ast.Expr):
            if not isinstance(statement, ast.Raise) or not isinstance(statement.exc, ast.Call):
                continue
            call = statement.exc
            if (
                isinstance(call.func, ast.Name)
                and call.func.id == "SystemExit"
                and len(call.args) == 1
                and isinstance(call.args[0], ast.Call)
            ):
                call = call.args[0]
        else:
            call = statement.value
        if _is_safe_unittest_main_call(call):
            return True
    return False


def _entrypoint_contract_signatures(tree: ast.Module) -> tuple[str, ...]:
    """Return ordered normalized top-level AST signatures for entrypoint compatibility."""
    return tuple(
        ast.dump(statement, include_attributes=False)
        for statement in tree.body
    )


def _reviewed_direct_entrypoint_transition(
    relative: str,
    base_contract: tuple[str, ...],
    head_contract: tuple[str, ...],
) -> bool:
    """Recognize the reviewed Lyrical direct-path bootstrap transition."""
    if relative not in _REVIEWED_DIRECT_ENTRYPOINTS:
        return False
    package_module = Path(relative).stem
    package_import = (
        "Scripts.ros2forunity.windows.lyrical.regression_checks."
        f"{package_module}"
    )
    base_source = (
        "from __future__ import annotations\n"
        "import unittest\n"
        "from . import *\n"
        "if __name__ == '__main__':\n"
        "    raise SystemExit(unittest.main(module=None))\n"
    )
    head_source = (
        "from __future__ import annotations\n"
        "import unittest\n"
        "import sys\n"
        "from pathlib import Path\n"
        "sys.path.insert(0, str(Path(__file__).resolve().parents[6]))\n"
        f"from {package_import} import *\n"
        "if __name__ == '__main__':\n"
        "    raise SystemExit(unittest.main(module=__name__))\n"
    )
    return (
        tuple(base_contract) == _entrypoint_contract_signatures(ast.parse(base_source))
        and tuple(head_contract) == _entrypoint_contract_signatures(ast.parse(head_source))
    )


def _reviewed_direct_entrypoint_absolute_import(
    path: Path,
    node: ast.Import | ast.ImportFrom,
    repository_root: Path | None,
) -> bool:
    """Recognize the reviewed direct entrypoint import in an archive checkout."""
    if (
        repository_root is None
        or not isinstance(node, ast.ImportFrom)
        or node.level
        or node.module is None
    ):
        return False
    try:
        relative = path.resolve().relative_to(repository_root.resolve()).as_posix()
    except ValueError:
        return False
    if relative.endswith("/__main__.py"):
        relative = relative[: -len("/__main__.py")] + ".py"
    if relative not in _REVIEWED_DIRECT_ENTRYPOINTS:
        return False
    package_module = Path(relative).stem
    expected_module = (
        "Scripts.ros2forunity.windows.lyrical.regression_checks."
        f"{package_module}"
    )
    if node.module != expected_module or len(node.names) != 1 or node.names[0].name != "*":
        return False
    target = repository_root.joinpath(*expected_module.split("."))
    return (target / "__init__.py").is_file() or target.with_suffix(".py").is_file()


def _entrypoint_contract(
    path: Path,
    allowed_relative_modules: frozenset[str] | None = None,
    *,
    reject_conditional: bool = True,
    repository_root: Path | None = None,
) -> tuple[bool, str | None, tuple[str, ...], frozenset[str]]:
    """Internal helper for _entrypoint_contract."""
    if not path.is_file():
        return False, None, (), frozenset()
    source = path.read_text(encoding="utf-8")
    compile(source, str(path), "exec")
    tree = _parse_source(source, path)
    package_dir = path.parent
    package_init = package_dir / "__init__.py"
    allowed_relative_modules = (
        None if allowed_relative_modules is None else frozenset(allowed_relative_modules)
    )
    reviewed_identity_tooling = False
    if repository_root is not None:
        try:
            relative_path = path.resolve().relative_to(repository_root.resolve()).as_posix()
        except ValueError:
            relative_path = ""
        reviewed_identity_tooling = (
            relative_path == "Scripts/phase192/regression_checks/test_identity_tooling.py"
            and allowed_relative_modules == frozenset(
                {
                    "foundation",
                    "surface_rows_tests",
                    "split_contract_tests",
                    "split_surface_tests",
                    "compatibility_surface_tests",
                    "compatibility_initializer_tests",
                    "loader_isolation_tests",
                    "loader_entrypoint_tests",
                    "loader_mutation_tests",
                    "namespace_risk_tests",
                    "namespace_execution_core_tests",
                    "namespace_execution_edges_tests",
                    "aggregate",
                }
            )
        )
    top_level_relative_imports = tuple(
        node for node in tree.body if isinstance(node, ast.ImportFrom) and node.level == 1
    )
    relative_ids = {id(node) for node in top_level_relative_imports}
    for node in ast.walk(tree):
        if not isinstance(node, ast.ImportFrom) or not node.level:
            continue
        if node.level != 1 or id(node) not in relative_ids:
            raise ValueError(f"unsupported nested or parent-relative entrypoint import: {path}")

    def stable_exports(target: Path) -> set[str]:
        """Internal helper for stable_exports."""
        target_tree = _parse_source(target.read_text(encoding="utf-8"), target)
        names = _top_level_names(target_tree, include_imports=True)
        names.difference_update(_conditional_only_public_names(target_tree, include_private=True))
        return names

    package_exports: set[str] = set()
    if package_init.is_file():
        package_tree = _parse_source(package_init.read_text(encoding="utf-8"), package_init)
        package_exports.update(stable_exports(package_init))
        for initializer_node in package_tree.body:
            if not isinstance(initializer_node, ast.ImportFrom) or initializer_node.level != 1:
                continue
            section_names = [initializer_node.module] if initializer_node.module else [
                alias.name for alias in initializer_node.names if alias.name != "*"
            ]
            for section_name in section_names:
                if not section_name or _SECTION_NAME_RE.fullmatch(section_name) is None:
                    continue
                section_path = package_dir / f"{section_name}.py"
                if section_path.is_file():
                    section_tree = _parse_source(section_path.read_text(encoding="utf-8"), section_path)
                    package_exports.update(stable_exports(section_path))
                    if _section_package_for_facade(section_path) is not None:
                        section_exports = _symbols(
                            section_path.read_text(encoding="utf-8"),
                            str(section_path),
                            reject_conditional=reject_conditional,
                            repository_root=repository_root,
                        )
                    else:
                        section_exports = _section_exports(
                            section_path.read_text(encoding="utf-8"),
                            str(section_path),
                            reject_conditional=reject_conditional,
                            reject_module_registry=(
                                "regression_checks" not in section_path.parts
                                and not any(part.startswith("test_") for part in section_path.parts)
                                and not _is_phase121_configuration_path(section_path, repository_root)
                            ),
                            validate_namespace=(
                                not _is_phase121_configuration_path(section_path, repository_root)
                                and not reviewed_identity_tooling
                            ),
                            repository_root=repository_root,
                        )
                    package_exports.update(section_exports)

    relative_imports = top_level_relative_imports
    unittest_entrypoint = _is_unittest_module_entrypoint(tree)
    if not relative_imports and not unittest_entrypoint:
        raise ValueError(f"module entrypoint does not import its package: {path}")
    for node in relative_imports:
        if node.module is None:
            missing = sorted(
                alias.name
                for alias in node.names
                if alias.name != "*"
                and (
                    (
                        allowed_relative_modules is not None
                        and alias.name not in allowed_relative_modules
                        and alias.name not in package_exports
                    )
                    or (
                        allowed_relative_modules is None
                        and alias.name not in package_exports
                        and not (package_dir / f"{alias.name}.py").is_file()
                        and not (package_dir / alias.name / "__init__.py").is_file()
                    )
                )
            )
            if missing:
                raise ValueError(f"relative entrypoint export is missing: {path}: {','.join(missing)}")
            continue
        parts = node.module.split(".")
        if any(_SECTION_NAME_RE.fullmatch(part) is None or keyword.iskeyword(part) for part in parts):
            raise ValueError(f"invalid relative module import in entrypoint: {path}")
        if allowed_relative_modules is not None and parts[0] not in allowed_relative_modules:
            raise ValueError(f"unlisted relative entrypoint module: {path}: {node.module}")
        module_file = package_dir.joinpath(*parts).with_suffix(".py")
        module_package = package_dir.joinpath(*parts) / "__init__.py"
        target = module_file if module_file.is_file() else module_package if module_package.is_file() else None
        if target is None:
            raise ValueError(f"relative entrypoint module is missing: {path}: {node.module}")
        target_exports = stable_exports(target)
        if any(alias.name != "*" and alias.name not in target_exports for alias in node.names):
            missing = sorted(alias.name for alias in node.names if alias.name != "*" and alias.name not in target_exports)
            raise ValueError(f"relative entrypoint symbol is missing: {path}: {node.module}:{','.join(missing)}")

    for node in tree.body:
        if isinstance(node, ast.Import) or (isinstance(node, ast.ImportFrom) and node.level == 0 and node.module != "__future__"):
            roots = (
                {alias.name.split(".", 1)[0] for alias in node.names}
                if isinstance(node, ast.Import)
                else {(node.module or "").split(".", 1)[0]}
            )
            allowed_roots = set(_ENTRYPOINT_IMPORT_ROOTS)
            if "subprocess" in roots and any(
                isinstance(node, ast.Attribute)
                and node.attr == "TimeoutExpired"
                and isinstance(node.value, ast.Name)
                and node.value.id == "subprocess"
                for node in ast.walk(tree)
            ):
                allowed_roots.add("subprocess")
            pathlib_path_import = (
                isinstance(node, ast.ImportFrom)
                and node.module == "pathlib"
                and node.level == 0
                and len(node.names) == 1
                and node.names[0].name == "Path"
                and node.names[0].asname is None
            )
            if not roots <= allowed_roots and not pathlib_path_import:
                raise ValueError(f"unsupported absolute module entrypoint import: {path}")
            if not _absolute_import_resolves(node) and not _reviewed_direct_entrypoint_absolute_import(
                path,
                node,
                repository_root,
            ):
                raise ValueError(f"unresolvable absolute module entrypoint import: {path}")


    imported_names = {
        alias.asname or alias.name
        for node in tree.body
        if isinstance(node, (ast.Import, ast.ImportFrom))
        and (not isinstance(node, ast.ImportFrom) or node.module != "__future__")
        for alias in node.names
        if alias.name != "*"
    }
    has_star_import = any(
        alias.name == "*" for node in relative_imports for alias in node.names
    )
    dynamic_names: set[str] = set()
    local_names: set[str] = set()
    _ENTRYPOINT_DANGEROUS_NAMES = frozenset({
        "__import__", "builtins", "compile", "delattr", "eval", "exec", "getattr",
        "globals", "locals", "setattr", "vars",
    })
    _ENTRYPOINT_DANGEROUS_ATTRS = frozenset({
        "__dict__", "__getattribute__", "__setattr__", "__delattr__", "modules",
        "__class__", "__mro__", "__subclasses__", "__globals__", "__code__", "__closure__", "__func__",
        "system", "popen", "run", "Popen", "call", "check_call", "check_output",
        "import_module", "exec", "eval", "compile", "setattr", "delattr",
        "open", "write", "write_text", "write_bytes", "unlink", "remove", "mkdir",
        "rmdir", "rename", "replace", "chmod", "connect", "send", "recv",
    })
    entrypoint_builtins = frozenset(set(_SAFE_BUILTIN_NAMES) | {"sorted", "all", "any", "print"})

    def local_function_is_safe(node: ast.FunctionDef | ast.AsyncFunctionDef) -> bool:
        """Internal helper for local_function_is_safe."""
        if not _safe_function_definition(
            node,
            frozenset(set(entrypoint_builtins) | imported_names | local_names | {"__doc__"}),
            postponed_annotations=True,
        ):
            return False
        allowed_call_names = set(entrypoint_builtins) | imported_names | {
            statement.name
            for statement in body
            if isinstance(statement, (ast.FunctionDef, ast.AsyncFunctionDef))
        }
        for child in ast.walk(node):
            if isinstance(child, (ast.Import, ast.ImportFrom, ast.While, ast.For, ast.AsyncFor, *_TRY_TYPES, ast.With, ast.AsyncWith, ast.Global, ast.Nonlocal, ast.NamedExpr, ast.Delete, ast.Assert, ast.Lambda, ast.Yield, ast.YieldFrom, ast.Await)):
                return False
            if isinstance(child, ast.Name) and child.id in _ENTRYPOINT_DANGEROUS_NAMES:
                return False
            if isinstance(child, ast.Attribute) and child.attr in _ENTRYPOINT_DANGEROUS_ATTRS:
                return False
            if isinstance(child, ast.Call):
                if isinstance(child.func, ast.Name):
                    if child.func.id in _ENTRYPOINT_DANGEROUS_NAMES:
                        return False
                    if child.func.id not in allowed_call_names:
                        return False
                if isinstance(child.func, ast.Attribute) and child.func.attr in _ENTRYPOINT_DANGEROUS_ATTRS:
                    return False
        return True

    def is_main_guard(node: ast.If) -> bool:
        """Internal helper for is_main_guard."""
        test = node.test
        return (
            isinstance(test, ast.Compare)
            and len(test.ops) == 1
            and isinstance(test.ops[0], ast.Eq)
            and len(test.comparators) == 1
            and (
                (
                    isinstance(test.left, ast.Name)
                    and test.left.id == "__name__"
                    and isinstance(test.comparators[0], ast.Constant)
                    and test.comparators[0].value == "__main__"
                )
                or (
                    isinstance(test.comparators[0], ast.Name)
                    and test.comparators[0].id == "__name__"
                    and isinstance(test.left, ast.Constant)
                    and test.left.value == "__main__"
                )
            )
        )

    def safe_value(node: ast.AST) -> bool:
        """Internal helper for safe_value."""
        if isinstance(node, (ast.Constant, ast.Name)):
            return True
        if isinstance(node, ast.Attribute):
            return node.attr not in _ENTRYPOINT_DANGEROUS_ATTRS and safe_value(node.value)
        if isinstance(node, ast.Subscript):
            return safe_value(node.value) and safe_value(node.slice)
        if isinstance(node, ast.Slice):
            return all(part is None or safe_value(part) for part in (node.lower, node.upper, node.step))
        if isinstance(node, (ast.Tuple, ast.List, ast.Set)):
            return all(safe_value(item) for item in node.elts)
        return False

    def is_safe_dispatch_call(node: ast.Call) -> bool:
        """Internal helper for is_safe_dispatch_call."""
        if not isinstance(node.func, ast.Name):
            return False
        if node.func.id not in imported_names | local_names | dynamic_names | package_exports:
            return False
        return all(safe_value(argument) for argument in node.args) and all(
            safe_value(keyword_node.value) for keyword_node in node.keywords
        )

    def is_unittest_suite_assignment(target: str, value: ast.AST) -> bool:
        """Recognize the explicit section-by-section unittest entrypoint."""
        if target == "suite":
            return (
                isinstance(value, ast.Call)
                and isinstance(value.func, ast.Attribute)
                and isinstance(value.func.value, ast.Name)
                and value.func.value.id == "unittest"
                and value.func.attr == "TestSuite"
                and len(value.args) == 1
                and not value.keywords
                and isinstance(value.args[0], ast.GeneratorExp)
                and isinstance(value.args[0].elt, ast.Call)
                and isinstance(value.args[0].elt.func, ast.Attribute)
                and isinstance(value.args[0].elt.func.value, ast.Attribute)
                and isinstance(value.args[0].elt.func.value.value, ast.Name)
                and value.args[0].elt.func.value.value.id == "unittest"
                and value.args[0].elt.func.value.attr == "defaultTestLoader"
                and value.args[0].elt.func.attr == "loadTestsFromModule"
                and len(value.args[0].elt.args) == 1
                and not value.args[0].elt.keywords
                and isinstance(value.args[0].elt.args[0], ast.Name)
                and len(value.args[0].generators) == 1
                and isinstance(value.args[0].generators[0].target, ast.Name)
                and not value.args[0].generators[0].ifs
                and not value.args[0].generators[0].is_async
                and isinstance(value.args[0].generators[0].iter, ast.Name)
                and value.args[0].generators[0].iter.id in {
                    "modules", "_PHASE192_SECTION_MODULES", "_PHASE192_SECTIONS",
                }
                and value.args[0].elt.args[0].id == value.args[0].generators[0].target.id
            )
        if target == "result":
            return (
                isinstance(value, ast.Call)
                and isinstance(value.func, ast.Attribute)
                and value.func.attr == "run"
                and isinstance(value.func.value, ast.Call)
                and isinstance(value.func.value.func, ast.Attribute)
                and isinstance(value.func.value.func.value, ast.Name)
                and value.func.value.func.value.id == "unittest"
                and value.func.value.func.attr == "TextTestRunner"
                and not value.func.value.args
                and (
                    not value.func.value.keywords
                    or (
                        len(value.func.value.keywords) == 1
                        and value.func.value.keywords[0].arg == "verbosity"
                        and isinstance(value.func.value.keywords[0].value, ast.Constant)
                        and value.func.value.keywords[0].value.value == 2
                    )
                )
                and len(value.args) == 1
                and not value.keywords
                and isinstance(value.args[0], ast.Name)
                and value.args[0].id == "suite"
            )
        return False

    def is_unittest_exit_expression(node: ast.AST) -> bool:
        """Recognize the success-derived SystemExit status expression."""
        return (
            isinstance(node, ast.IfExp)
            and isinstance(node.test, ast.Call)
            and isinstance(node.test.func, ast.Attribute)
            and isinstance(node.test.func.value, ast.Name)
            and node.test.func.value.id == "result"
            and node.test.func.attr == "wasSuccessful"
            and not node.test.args
            and not node.test.keywords
            and isinstance(node.body, ast.Constant)
            and node.body.value == 0
            and isinstance(node.orelse, ast.Constant)
            and node.orelse.value == 1
        )

    def safe_log_argument(node: ast.AST, exception_names: frozenset[str]) -> bool:
        """Allow literals and direct references to the caught exception in messages."""
        if isinstance(node, ast.Constant):
            return True
        if not isinstance(node, ast.JoinedStr):
            return False
        for value in node.values:
            if isinstance(value, ast.Constant):
                continue
            if (
                isinstance(value, ast.FormattedValue)
                and isinstance(value.value, ast.Name)
                and value.value.id in exception_names
                and value.conversion == -1
                and value.format_spec is None
            ):
                continue
            return False
        return True

    def validate_guard_statements(
        statements: list[ast.stmt],
        exception_names: frozenset[str] = frozenset(),
    ) -> bool:
        """Internal helper for validate_guard_statements."""
        delegated = False
        for statement in statements:
            if isinstance(statement, ast.Expr) and isinstance(statement.value, ast.Call):
                call = statement.value
                if _is_safe_unittest_main_call(call):
                    delegated = True
                    continue
                if (
                    isinstance(call.func, ast.Name)
                    and call.func.id == "print"
                    and call.args
                    and all(safe_log_argument(argument, exception_names) for argument in call.args)
                    and len(call.keywords) == 1
                    and call.keywords[0].arg == "file"
                    and isinstance(call.keywords[0].value, ast.Attribute)
                    and isinstance(call.keywords[0].value.value, ast.Name)
                    and call.keywords[0].value.value.id == "sys"
                    and call.keywords[0].value.attr == "stderr"
                ):
                    continue
                raise ValueError(f"unsafe module entrypoint expression: {path}")
            if isinstance(statement, ast.Try):
                if statement.orelse or statement.finalbody or not statement.handlers:
                    raise ValueError(f"unsafe module entrypoint try block: {path}")
                body_delegated = validate_guard_statements(statement.body)
                for handler in statement.handlers:
                    handler_types: tuple[ast.AST, ...]
                    if isinstance(handler.type, ast.Name):
                        handler_types = (handler.type,)
                    elif isinstance(handler.type, ast.Tuple):
                        handler_types = tuple(handler.type.elts)
                    else:
                        handler_types = ()
                    allowed_handler_names = {
                        "KeyboardInterrupt", "Exception", "RuntimeError", "TimeoutError",
                        "FileNotFoundError", "InconclusiveError",
                    }
                    def allowed_handler_type(item: ast.AST) -> bool:
                        """Return whether a legacy entrypoint handler type is allowed."""
                        return (
                            isinstance(item, ast.Name)
                            and item.id in allowed_handler_names
                        ) or (
                            isinstance(item, ast.Attribute)
                            and item.attr == "TimeoutExpired"
                            and isinstance(item.value, ast.Name)
                            and item.value.id == "subprocess"
                        )

                    if not handler_types or any(not allowed_handler_type(item) for item in handler_types):
                        raise ValueError(f"unsafe module entrypoint exception handler: {path}")
                    handler_names = frozenset({handler.name}) if handler.name else frozenset()
                    validate_guard_statements(handler.body, handler_names)
                delegated = delegated or body_delegated
                continue
            if isinstance(statement, ast.Assign):
                if (
                    len(statement.targets) == 1
                    and isinstance(statement.targets[0], ast.Subscript)
                    and isinstance(statement.targets[0].value, ast.Attribute)
                    and isinstance(statement.targets[0].value.value, ast.Name)
                    and statement.targets[0].value.value.id == "sys"
                    and statement.targets[0].value.attr == "argv"
                    and isinstance(statement.targets[0].slice, ast.Constant)
                    and statement.targets[0].slice.value == 0
                    and isinstance(statement.value, ast.Constant)
                    and isinstance(statement.value.value, str)
                ):
                    continue
                if len(statement.targets) != 1 or not isinstance(statement.targets[0], ast.Name):
                    raise ValueError(f"unsafe module entrypoint assignment: {path}")
                target = statement.targets[0].id
                value = statement.value
                if is_unittest_suite_assignment(target, value):
                    continue
                if not target.startswith("_phase192_") or not (
                    isinstance(value, ast.Call)
                    and isinstance(value.func, ast.Attribute)
                    and isinstance(value.func.value, ast.Call)
                    and isinstance(value.func.value.func, ast.Name)
                    and value.func.value.func.id == "globals"
                    and not value.func.value.args
                    and not value.func.value.keywords
                    and value.func.attr == "get"
                    and len(value.args) == 1
                    and isinstance(value.args[0], ast.Constant)
                    and isinstance(value.args[0].value, str)
                    and not value.keywords
                ):
                    raise ValueError(f"unsafe module entrypoint assignment: {path}")
                dynamic_names.add(target)
                continue
            if isinstance(statement, ast.If):
                if statement.orelse or not (
                    isinstance(statement.test, ast.Call)
                    and isinstance(statement.test.func, ast.Name)
                    and statement.test.func.id == "callable"
                    and len(statement.test.args) == 1
                    and not statement.test.keywords
                    and isinstance(statement.test.args[0], ast.Name)
                    and statement.test.args[0].id in dynamic_names
                ):
                    raise ValueError(f"unsafe module entrypoint control flow: {path}")
                delegated = validate_guard_statements(statement.body) or delegated
                continue
            if isinstance(statement, ast.Raise):
                exception = statement.exc
                if not (
                    isinstance(exception, ast.Call)
                    and isinstance(exception.func, ast.Name)
                    and exception.func.id == "SystemExit"
                    and len(exception.args) == 1
                    and not exception.keywords
                    and (
                        (
                            isinstance(exception.args[0], ast.Call)
                            and (
                                is_safe_dispatch_call(exception.args[0])
                                or _is_safe_unittest_main_call(exception.args[0])
                            )
                        )
                        or is_unittest_exit_expression(exception.args[0])
                        or (
                            isinstance(exception.args[0], ast.Constant)
                            and exception.args[0].value in {1, 2, 130}
                        )
                    )
                ):
                    raise ValueError(f"unsafe module entrypoint raise: {path}")
                if statement.cause is not None and not (
                    isinstance(statement.cause, ast.Name)
                    and statement.cause.id in exception_names
                ):
                    raise ValueError(f"unsafe module entrypoint raise cause: {path}")
                delegated = True
                continue
            raise ValueError(f"unsafe module entrypoint statement: {path}")
        return delegated

    body = list(tree.body)
    if body and isinstance(body[0], ast.Expr) and isinstance(body[0].value, ast.Constant) and isinstance(body[0].value.value, str):
        body = body[1:]
    delegated = False
    definitions: list[ast.FunctionDef | ast.AsyncFunctionDef] = []

    path_ctor_names = {
        alias.asname or alias.name
        for statement in tree.body
        if isinstance(statement, ast.ImportFrom)
        and statement.module == "pathlib"
        and statement.level == 0
        for alias in statement.names
        if alias.name == "Path"
    }
    path_module_names = {
        alias.asname or alias.name.split(".", 1)[0]
        for statement in tree.body
        if isinstance(statement, ast.Import)
        for alias in statement.names
        if alias.name == "pathlib"
    }

    def is_safe_file_bootstrap(statement: ast.stmt) -> bool:
        """Recognize the reviewed direct-path repository bootstrap."""
        if not isinstance(statement, ast.Expr) or not isinstance(statement.value, ast.Call):
            return False
        call = statement.value
        if not (
            isinstance(call.func, ast.Attribute)
            and call.func.attr == "insert"
            and isinstance(call.func.value, ast.Attribute)
            and call.func.value.attr == "path"
            and isinstance(call.func.value.value, ast.Name)
            and call.func.value.value.id == "sys"
            and len(call.args) == 2
            and not call.keywords
            and isinstance(call.args[0], ast.Constant)
            and call.args[0].value == 0
        ):
            return False
        value = call.args[1]
        return (
            isinstance(value, ast.Call)
            and isinstance(value.func, ast.Name)
            and value.func.id == "str"
            and len(value.args) == 1
            and not value.keywords
            and isinstance(value.args[0], ast.Subscript)
            and isinstance(value.args[0].value, ast.Attribute)
            and value.args[0].value.attr == "parents"
            and isinstance(value.args[0].value.value, ast.Call)
            and isinstance(value.args[0].value.value.func, ast.Attribute)
            and value.args[0].value.value.func.attr == "resolve"
            and not value.args[0].value.value.args
            and not value.args[0].value.value.keywords
            and isinstance(value.args[0].value.value.func.value, ast.Call)
            and (
                isinstance(value.args[0].value.value.func.value.func, ast.Name)
                and value.args[0].value.value.func.value.func.id in path_ctor_names
                or isinstance(value.args[0].value.value.func.value.func, ast.Attribute)
                and value.args[0].value.value.func.value.func.attr == "Path"
                and isinstance(value.args[0].value.value.func.value.value, ast.Name)
                and value.args[0].value.value.func.value.value.id in path_module_names
            )
            and len(value.args[0].value.value.func.value.args) == 1
            and isinstance(value.args[0].value.value.func.value.args[0], ast.Name)
            and value.args[0].value.value.func.value.args[0].id == "__file__"
            and isinstance(value.args[0].slice, ast.Constant)
            and isinstance(value.args[0].slice.value, int)
            and 1 <= value.args[0].slice.value <= 8
        )

    for statement in body:
        if isinstance(statement, ast.Import):
            continue
        if isinstance(statement, ast.ImportFrom):
            if statement.module == "__future__":
                if statement.level != 0 or any(alias.name != "annotations" or alias.asname is not None for alias in statement.names):
                    raise ValueError(f"unsupported future import in module entrypoint: {path}")
                continue
            if statement.level == 0:
                continue
            if statement.level != 1:
                raise ValueError(f"unsupported relative import in module entrypoint: {path}")
            continue
        if isinstance(statement, (ast.FunctionDef, ast.AsyncFunctionDef)):
            if statement.decorator_list or not local_function_is_safe(statement):
                raise ValueError(f"unsafe module entrypoint definition: {path}")
            definitions.append(statement)
            local_names.add(statement.name)
            continue
        if isinstance(statement, ast.ClassDef):
            raise ValueError(f"unsafe module entrypoint class definition: {path}")
        if is_safe_file_bootstrap(statement):
            continue
        if isinstance(statement, ast.Try):
            if statement.orelse or statement.finalbody or not statement.handlers:
                raise ValueError(f"unsafe module entrypoint try block: {path}")
            delegated = validate_guard_statements(statement.body) or delegated
            for handler in statement.handlers:
                handler_types = (
                    tuple(handler.type.elts)
                    if isinstance(handler.type, ast.Tuple)
                    else (handler.type,)
                    if isinstance(handler.type, ast.Name)
                    else ()
                )
                if not handler_types or any(
                    not (
                        isinstance(item, ast.Name)
                        and item.id in {
                            "KeyboardInterrupt", "Exception", "RuntimeError", "TimeoutError",
                            "FileNotFoundError", "InconclusiveError",
                        }
                        or (
                            isinstance(item, ast.Attribute)
                            and item.attr == "TimeoutExpired"
                            and isinstance(item.value, ast.Name)
                            and item.value.id == "subprocess"
                        )
                    )
                    for item in handler_types
                ):
                    raise ValueError(f"unsafe module entrypoint exception handler: {path}")
                handler_names = frozenset({handler.name}) if handler.name else frozenset()
                validate_guard_statements(handler.body, handler_names)
            continue
        if isinstance(statement, ast.If) and is_main_guard(statement):
            delegated = validate_guard_statements(statement.body) or delegated
            continue
        raise ValueError(f"unsafe module entrypoint statement: {path}")
    if not has_star_import and not delegated:
        raise ValueError(f"module entrypoint has no delegated callable: {path}")
    normalized = ast.Module(body=body, type_ignores=[])
    digest = hashlib.sha256(ast.dump(normalized, include_attributes=False).encode("utf-8")).hexdigest()
    contract = _entrypoint_contract_signatures(normalized)
    risks = _namespace_risk_signatures(normalized)
    return True, digest, contract, risks

from Scripts.phase192.identity_initializer_contracts import (
    _SAFE_BUILTIN_NAMES,
    _SAFE_IMPORT_ROOTS,
    _absolute_import_resolves,
    _contract_preserves_order,
    _definitely_bound_names,
    _initializer_is_additive,
    _initializer_statements_match,
    _initializer_bound_names,
    _is_docstring_statement,
    _literal_assignment_target_matches,
    _literal_initializer_value,
    _literal_truth_value,
    _loaded_names,
    _module_spec_exists,
    _safe_annotation_expression,
    _safe_class_definition,
    _safe_definition_expression,
    _safe_function_definition,
    _safe_inert_initializer_condition,
    _safe_initializer_addition,
    _safe_runtime_expression,
    _section_alias_bindings,
    _section_tuple_names,
    _sections_preserve_order,
    _statement_bound_names,
    _statement_deleted_names,
    _statement_loaded_names,
)

__all__ = ['_is_safe_unittest_main_call', '_is_unittest_module_entrypoint', '_entrypoint_contract_signatures', '_reviewed_direct_entrypoint_transition', '_reviewed_direct_entrypoint_absolute_import', '_entrypoint_contract', '_SAFE_BUILTIN_NAMES', '_SAFE_IMPORT_ROOTS', '_absolute_import_resolves', '_contract_preserves_order', '_definitely_bound_names', '_initializer_is_additive', '_initializer_statements_match', '_initializer_bound_names', '_is_docstring_statement', '_literal_assignment_target_matches', '_literal_initializer_value', '_literal_truth_value', '_loaded_names', '_module_spec_exists', '_safe_annotation_expression', '_safe_class_definition', '_safe_definition_expression', '_safe_function_definition', '_safe_inert_initializer_condition', '_safe_initializer_addition', '_safe_runtime_expression', '_section_alias_bindings', '_section_tuple_names', '_sections_preserve_order', '_statement_bound_names', '_statement_deleted_names', '_statement_loaded_names']
