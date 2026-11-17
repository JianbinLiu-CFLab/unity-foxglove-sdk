from __future__ import annotations

from .foundation import *

_SAFE_STDLIB_ENTRYPOINT_SYMBOLS = {
    "argparse": frozenset({
        "Action", "ArgumentParser", "ArgumentTypeError", "FileType", "HelpFormatter",
        "Namespace", "RawDescriptionHelpFormatter", "RawTextHelpFormatter", "SUPPRESS",
    }),
    "json": frozenset({"JSONDecodeError", "dump", "dumps", "load", "loads"}),
    "os": frozenset({"environ", "linesep", "name", "path", "sep"}),
    "pathlib": frozenset({
        "Path", "PurePath", "PurePosixPath", "PureWindowsPath", "PosixPath", "WindowsPath",
    }),
    "sys": frozenset({"argv", "executable", "path", "platform", "stderr", "stdin", "stdout"}),
    "time": frozenset({"monotonic", "perf_counter", "sleep", "time"}),
    "typing": frozenset({
        "Any", "Callable", "Mapping", "Sequence", "TypeVar", "cast", "overload",
    }),
}
_SAFE_STDLIB_ENTRYPOINT_MODULES = frozenset(_SAFE_STDLIB_ENTRYPOINT_SYMBOLS)

def _entrypoint_section_imports_are_trusted(tree: ast.Module, package: Path) -> bool:
    """Return whether entrypoint imports use the approved standard library set."""
    def absolute_import_resolves(node: ast.Import | ast.ImportFrom) -> bool:
        """Return whether a standard-library import is known without executing it."""
        if isinstance(node, ast.Import):
            return all(
                alias.name in _SAFE_STDLIB_ENTRYPOINT_MODULES
                for alias in node.names
            )
        if not node.module or node.module not in _SAFE_STDLIB_ENTRYPOINT_SYMBOLS:
            return False
        allowed = _SAFE_STDLIB_ENTRYPOINT_SYMBOLS[node.module]
        return all(alias.name in allowed for alias in node.names)

    for node in tree.body:
        if isinstance(node, ast.Import):
            if any(alias.name == "*" for alias in node.names) or not absolute_import_resolves(node):
                return False
            continue
        if not isinstance(node, ast.ImportFrom):
            continue
        if node.module == "__future__":
            if node.level or any(alias.name != "annotations" for alias in node.names):
                return False
            continue
        if node.level:
            return False
        if any(alias.name == "*" for alias in node.names) or not absolute_import_resolves(node):
            return False
    return True

def _function_signature_is_inert(node: ast.FunctionDef | ast.AsyncFunctionDef) -> bool:
    """Return whether a function signature has no import-time executable expressions."""
    arguments = node.args
    if node.returns is not None or arguments.defaults or any(arguments.kw_defaults):
        return False
    return not any(
        argument.annotation is not None or argument.type_comment
        for argument in (*arguments.posonlyargs, *arguments.args, *arguments.kwonlyargs)
    )

def _relative_import_contract(
    tree: ast.Module,
    package_dir: Path,
    source_path: str | Path,
    declared_sections: frozenset[str] | None = None,
) -> tuple[str, ...]:
    """Validate relative imports and return the star-import chain contract."""
    star_contract: list[str] = []
    for node in ast.walk(tree):
        if not isinstance(node, ast.ImportFrom) or not node.level:
            continue
        if node.level != 1:
            raise ValueError(f"Unsupported parent-relative import in {source_path}")
        star_contract.append(ast.dump(node, include_attributes=False))
        if node.module:
            parts = node.module.split(".")
            if any(_SECTION_NAME_RE.fullmatch(part) is None or keyword.iskeyword(part) for part in parts):
                raise ValueError(f"Invalid relative import in {source_path}: {node.module}")
            source_stem = Path(source_path).stem
            if (
                declared_sections is not None
                and parts[0] not in declared_sections
                and parts[0] != source_stem
            ):
                raise ValueError(
                    f"Relative import target is not a declared section in {source_path}: {node.module}"
                )
            target_base = package_dir.joinpath(*parts)
            target = target_base.with_suffix(".py") if target_base.with_suffix(".py").is_file() else target_base / "__init__.py"
            if not target.is_file():
                raise ValueError(f"Relative import target is missing in {source_path}: {node.module}")
        else:
            target = package_dir / "__init__.py"
        if any(alias.name == "*" for alias in node.names):
            continue
        for alias in node.names:
            if alias.name == "*":
                continue
            if node.module:
                target_tree = _parse_source(target.read_text(encoding="utf-8"), target)
                exports = _top_level_names(target_tree, include_imports=True)
                exports.difference_update(_conditional_only_public_names(target_tree, include_private=True))
                if alias.name not in exports:
                    raise ValueError(
                        f"Relative import symbol is missing in {source_path}: {node.module}:{alias.name}"
                    )
            else:
                candidate = package_dir / f"{alias.name}.py"
                candidate_package = package_dir / alias.name / "__init__.py"
                if (
                    declared_sections is not None
                    and alias.name not in declared_sections
                    and (candidate.is_file() or candidate_package.is_file())
                ):
                    raise ValueError(
                        f"Relative import target is not a declared section in {source_path}: {alias.name}"
                    )
                if not candidate.is_file() and not candidate_package.is_file():
                    # A package initializer may export a function or constant directly.
                    package_tree = _parse_source(target.read_text(encoding="utf-8"), target)
                    package_exports = _top_level_names(package_tree, include_imports=True)
                    package_exports.difference_update(
                        _conditional_only_public_names(package_tree, include_private=True)
                    )
                    if alias.name not in package_exports:
                        raise ValueError(
                            f"Relative import target is missing in {source_path}: {alias.name}"
                        )
    return tuple(star_contract)

def _symbols(
    source: str,
    path: str,
    *,
    reject_conditional: bool = True,
    repository_root: Path | None = None,
) -> frozenset[str]:
    """Internal helper for _symbols."""
    tree = _parse_source(source, path)
    source_file = Path(path)
    if any(isinstance(node, ast.ImportFrom) and node.level for node in ast.walk(tree)):
        if not source_file.is_file():
            raise ValueError(f"Relative import validation requires a concrete source path: {path}")
        _relative_import_contract(tree, source_file.parent, path)
    _reject_unsupported_module_bindings(tree, path)
    allowed_nodes: set[int] = set()
    for node in tree.body:
        if isinstance(node, (ast.Assign, ast.AnnAssign)):
            targets = node.targets if isinstance(node, ast.Assign) else [node.target]
            if any(isinstance(target, ast.Name) and target.id == "__all__" for target in targets):
                if _is_public_globals_comprehension(node.value):
                    allowed_nodes.update(id(item) for item in ast.walk(node.value))
    if any(
        isinstance(node, ast.ClassDef)
        and node.name in {"_Phase192FacadeModule", "_Phase138UPackageModule"}
        for node in tree.body
    ):
        _phase192_template_validate_facade(tree, path, repository_root)
        _validate_namespace_access(tree, allowed_nodes, path, reject_module_registry=False)
        _phase192_validate_template_registry_mutations(tree, path, repository_root)
    else:
        _validate_namespace_access(tree, allowed_nodes, path)
    conditional = _conditional_only_public_names(tree)
    if reject_conditional and conditional:
        raise ValueError(f"Conditional public bindings are not supported in {path}: {','.join(sorted(conditional))}")
    return frozenset(
        name
        for name in _top_level_names(tree, include_imports=True)
        if not name.startswith("__")
    )

def _section_exports(
    source: str,
    path: str,
    *,
    reject_conditional: bool = True,
    reject_module_registry: bool = True,
    validate_namespace: bool = True,
    repository_root: Path | None = None,
) -> frozenset[str]:
    """Internal helper for _section_exports."""
    tree = _parse_source(source, path)
    source_file = Path(path)
    if any(isinstance(node, ast.ImportFrom) and node.level for node in ast.walk(tree)):
        if not source_file.is_file():
            raise ValueError(f"Relative import validation requires a concrete source path: {path}")
        _relative_import_contract(tree, source_file.parent, path)
    _reject_unsupported_module_bindings(tree, path)
    def getattr_globals_candidate(node: ast.Call) -> bool:
        """Detect getattr calls that may resolve a function globals mapping."""
        key_node = node.args[1] if len(node.args) >= 2 else next(
            (keyword.value for keyword in node.keywords if keyword.arg == "name"),
            None,
        )
        if key_node is None:
            return True
        if isinstance(key_node, ast.Constant) and isinstance(key_node.value, str):
            return key_node.value == "__globals__"
        if isinstance(key_node, ast.BinOp) and isinstance(key_node.op, ast.Add):
            return True
        return False

    has_protected_lookup_candidate = any(
        (
            isinstance(node, ast.Attribute) and node.attr == "__globals__"
        )
        or (
            isinstance(node, ast.Call)
            and isinstance(node.func, ast.Name)
            and node.func.id == "getattr"
            and getattr_globals_candidate(node)
        )
        for node in ast.walk(tree)
    )
    if has_protected_lookup_candidate:
        _phase192_validate_template_registry_mutations(tree, path, repository_root)
    declared = _declared_public_names(tree)
    bound = _top_level_names(tree, include_imports=True)
    globals_is_builtin = "globals" not in bound
    direct_values: list[ast.AST] = []
    direct_ids: set[int] = set()
    for node in tree.body:
        targets = list(node.targets) if isinstance(node, ast.Assign) else [node.target] if isinstance(node, ast.AnnAssign) else []
        if any(isinstance(target, ast.Name) and target.id == "__all__" for target in targets):
            direct_values.append(node.value)
            direct_ids.add(id(node))
    allowed_nodes: set[int] = set()
    for value in direct_values:
        if _is_public_globals_comprehension(value):
            allowed_nodes.update(id(item) for item in ast.walk(value))
    if validate_namespace:
        _validate_namespace_access(
            tree,
            allowed_nodes,
            path,
            reject_module_registry=reject_module_registry,
            repository_root=repository_root,
        )
    for node in ast.walk(tree):
        if isinstance(node, (ast.Assign, ast.AnnAssign)):
            targets = list(node.targets) if isinstance(node, ast.Assign) else [node.target]
            if any(isinstance(target, ast.Name) and target.id == "__all__" for target in targets) and id(node) not in direct_ids:
                raise ValueError(f"Nested __all__ assignment in {path}")
        if isinstance(node, (ast.AugAssign, ast.Delete)):
            targets = node.targets if isinstance(node, ast.Delete) else [node.target]
            if any(isinstance(target, ast.Name) and target.id == "__all__" for target in targets):
                raise ValueError(f"Unsupported __all__ mutation in {path}")
        if isinstance(node, ast.Name) and node.id == "__all__" and isinstance(node.ctx, ast.Load):
            raise ValueError(f"Unsupported __all__ read in {path}")
    conditional = _conditional_only_public_names(tree)
    if reject_conditional and conditional:
        raise ValueError(f"Conditional public bindings are not supported in {path}: {','.join(sorted(conditional))}")
    if not direct_values:
        raise ValueError(f"Missing direct __all__ assignment in {path}")
    first = direct_values[0]
    if any(ast.dump(value, include_attributes=False) != ast.dump(first, include_attributes=False) for value in direct_values[1:]):
        raise ValueError(f"Conflicting __all__ assignments in {path}")
    if isinstance(first, (ast.ListComp, ast.SetComp)):
        if globals_is_builtin and _is_public_globals_comprehension(first):
            return frozenset(
                name
                for name in _top_level_names(tree, include_imports=True)
                if not name.startswith("__")
            )
        raise ValueError(f"Unsupported dynamic __all__ in {path}")
    try:
        literal = ast.literal_eval(first)
    except (TypeError, ValueError) as exc:
        raise ValueError(f"Unsupported __all__ in {path}") from exc
    if not isinstance(literal, (list, tuple, set)) or not all(isinstance(item, str) for item in literal):
        raise ValueError(f"Invalid __all__ in {path}")
    missing = sorted(set(literal) - bound)
    if missing and any(
        isinstance(node, ast.ImportFrom)
        and node.module == "__future__"
        and any(alias.name == "annotations" for alias in node.names)
        for node in tree.body
    ):
        missing = [name for name in missing if name != "annotations"]
    if missing:
        raise ValueError(f"__all__ names are not bound in {path}: {','.join(missing)}")
    return frozenset(literal)

def _initializer_lines(initializer: Path) -> tuple[str, ...]:
    """Internal helper for _initializer_lines."""
    tree = _parse_source(initializer.read_text(encoding="utf-8"), initializer)
    return tuple(ast.unparse(statement).strip() for statement in tree.body)

def _initializer_digest(initializer: Path) -> str:
    """Internal helper for _initializer_digest."""
    normalized = initializer.read_bytes().replace(b"\r\n", b"\n").replace(b"\r", b"\n")
    return hashlib.sha256(normalized).hexdigest()

from Scripts.validation.source_decomposition.identity_namespace_analysis import (
    _NAMESPACE_RISK_ATTRIBUTES,
    _NAMESPACE_RISK_MODULE_ATTRIBUTES,
    _NAMESPACE_RISK_NAMES,
    _namespace_risk_signatures,
)


def _is_phase121_configuration_path(
    path: str | Path,
    repository_root: Path | None = None,
) -> bool:
    """Return whether path is the reviewed Phase121 compatibility module."""
    target = (
        "Scripts",
        "mcap",
        "conformance",
        "run_phase121_conformance",
        "configuration.py",
    )
    candidate = Path(str(path).replace("\\", "/"))
    if repository_root is not None:
        try:
            root = Path(repository_root).resolve()
            candidate_path = candidate if candidate.is_absolute() else root / candidate
            relative = candidate_path.resolve().relative_to(root)
        except OSError:
            return False
        except ValueError:
            return False
        return tuple(relative.parts) == target
    if candidate.is_absolute():
        try:
            return candidate.resolve() == (Path(__file__).resolve().parents[3] / Path(*target)).resolve()
        except OSError:
            return False
    parts = tuple(part for part in candidate.parts if part not in {".", ""})
    return parts == target


def _entrypoint_executable_body_is_safe(
    tree: ast.Module,
    path: Path,
    function: ast.FunctionDef,
) -> bool:
    """Return whether the forwarding function has only reviewed executable statements."""
    imported_names: set[str] = set()
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
            if not (module_path.with_suffix(".py").is_file() or (module_path / "__init__.py").is_file()):
                return False
            for alias in statement.names:
                imported_names.add(alias.asname or alias.name)
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
            if not isinstance(statement.func, ast.Name) or statement.func.id not in imported_names:
                return False
    if dangerous_bound_names & imported_names:
        return False
    body = list(function.body)
    if not body or not isinstance(body[-1], ast.Return):
        return False
    if any(not isinstance(statement, (ast.Import, ast.ImportFrom, ast.Return)) for statement in body):
        return False
    if body[-1].value is not None:
        for statement in ast.walk(body[-1].value):
            if isinstance(statement, ast.Name) and statement.id not in imported_names:
                return False
    return True


def _entrypoint_initializer_exports(
    base_package: Path,
    package_exports: set[str],
) -> set[str] | None:
    """Return effective wildcard exports from an unchanged package initializer."""
    initializer = base_package / "__init__.py"
    if not initializer.is_file():
        return None
    tree = _parse_source(initializer.read_text(encoding="utf-8"), initializer)
    assignments = [node for node in tree.body if isinstance(node, ast.Assign) and any(
        isinstance(target, ast.Name) and target.id == "__all__" for target in node.targets
    )]
    if not assignments:
        return {name for name in _top_level_names(tree, include_imports=True) if not name.startswith("_")}
    try:
        names = ast.literal_eval(assignments[-1].value)
    except (TypeError, ValueError, SyntaxError):
        names = None
    if isinstance(names, (list, tuple, set)) and all(isinstance(name, str) for name in names):
        return set(names)
    if any(
        isinstance(node, ast.Call) and isinstance(node.func, ast.Name)
        and node.func.id in {"globals", "getattr"} for node in ast.walk(tree)
    ):
        return package_exports
    return None


_ENTRYPOINT_IMPORT_ROOTS = frozenset({"Scripts", "argparse", "sys", "unittest"})

_REVIEWED_DIRECT_ENTRYPOINTS = frozenset({
    "Scripts/ros2forunity/windows/lyrical/regression_checks/test_build_r2fu_runtime_package.py",
    "Scripts/ros2forunity/windows/lyrical/regression_checks/test_validate_r2fu_runtime_package.py",
})

__all__ = ['_relative_import_contract', '_symbols', '_section_exports', '_initializer_lines', '_initializer_digest', '_NAMESPACE_RISK_ATTRIBUTES', '_NAMESPACE_RISK_MODULE_ATTRIBUTES', '_NAMESPACE_RISK_NAMES', '_namespace_risk_signatures', '_is_phase121_configuration_path', '_ENTRYPOINT_IMPORT_ROOTS', '_REVIEWED_DIRECT_ENTRYPOINTS', '_entrypoint_section_imports_are_trusted', '_function_signature_is_inert', '_entrypoint_executable_body_is_safe']
