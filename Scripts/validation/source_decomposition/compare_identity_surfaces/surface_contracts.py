from __future__ import annotations

from .foundation import *


def _unconditional_top_level_bindings(tree: ast.Module) -> dict[str, ast.AST]:
    """Return final bindings from unconditional top-level statements."""
    bindings: dict[str, ast.AST] = {}

    def bind_target(target: ast.AST, statement: ast.AST) -> None:
        """Record direct assignment targets."""
        if isinstance(target, ast.Name):
            bindings[target.id] = statement
        elif isinstance(target, (ast.Tuple, ast.List)):
            for item in target.elts:
                bind_target(item, statement)

    for node in tree.body:
        if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef, ast.ClassDef)):
            bindings[node.name] = node
        elif isinstance(node, ast.Import):
            for alias in node.names:
                bindings[alias.asname or alias.name.split(".", 1)[0]] = node
        elif isinstance(node, ast.ImportFrom):
            for alias in node.names:
                if alias.name != "*":
                    bindings[alias.asname or alias.name] = node
        elif isinstance(node, (ast.Assign, ast.AnnAssign, ast.AugAssign)):
            targets = node.targets if isinstance(node, ast.Assign) else [node.target]
            for target in targets:
                bind_target(target, node)
        elif isinstance(node, ast.Delete):
            for target in node.targets:
                for candidate in ast.walk(target):
                    if isinstance(candidate, ast.Name):
                        bindings.pop(candidate.id, None)
    return bindings


def _unconditional_top_level_names(tree: ast.Module) -> set[str]:
    """Return names bound by unconditional top-level statements."""
    return set(_unconditional_top_level_bindings(tree))


def _public_globals_listcomp(value: ast.AST) -> bool:
    """Return whether a value is the reviewed public-globals export expression."""
    if not isinstance(value, ast.ListComp) or not isinstance(value.elt, ast.Name) or value.elt.id != "name":
        return False
    if len(value.generators) != 1:
        return False
    generator = value.generators[0]
    if not (
        isinstance(generator.target, ast.Name)
        and generator.target.id == "name"
        and isinstance(generator.iter, ast.Call)
        and isinstance(generator.iter.func, ast.Name)
        and generator.iter.func.id == "globals"
        and not generator.iter.args
        and not generator.iter.keywords
        and len(generator.ifs) == 1
    ):
        return False
    condition = generator.ifs[0]
    return (
        isinstance(condition, ast.UnaryOp)
        and isinstance(condition.op, ast.Not)
        and isinstance(condition.operand, ast.Call)
        and isinstance(condition.operand.func, ast.Attribute)
        and isinstance(condition.operand.func.value, ast.Name)
        and condition.operand.func.value.id == "name"
        and condition.operand.func.attr == "startswith"
        and len(condition.operand.args) == 1
        and isinstance(condition.operand.args[0], ast.Constant)
        and condition.operand.args[0].value == "__"
        and not condition.operand.keywords
    )



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



def _entrypoint_section_exports(section: Path) -> set[str] | None:
    """Return statically proven exports from one unchanged source section."""
    try:
        tree = _parse_source(section.read_text(encoding="utf-8"), section)
        _relative_import_contract(tree, section.parent, section)
    except (OSError, SyntaxError, ValueError):
        return None
    bindings = _unconditional_top_level_bindings(tree)
    assignments = [
        node
        for node in tree.body
        if isinstance(node, ast.Assign)
        and any(isinstance(target, ast.Name) and target.id == "__all__" for target in node.targets)
    ]
    if not assignments:
        if any(
            isinstance(node, ast.Name) and node.id == "__all__"
            for node in ast.walk(tree)
        ):
            return None
        return {
            name
            for name in bindings
            if not name.startswith("_")
        }
    final_assignment_nodes = {id(node) for node in ast.walk(assignments[-1])}
    if any(
        isinstance(node, ast.Name)
        and node.id == "__all__"
        and id(node) not in final_assignment_nodes
        for node in ast.walk(tree)
    ):
        return None
    value = assignments[-1].value
    try:
        declared = ast.literal_eval(value)
    except (TypeError, ValueError, SyntaxError):
        declared = None
    if isinstance(declared, (list, tuple, set)) and all(isinstance(name, str) for name in declared):
        names = set(declared)
        return names if names <= set(bindings) else None
    if _public_globals_listcomp(value):
        return {
            name
            for name in bindings
            if not name.startswith("_")
        }
    return None



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
        return {name for name in _unconditional_top_level_names(tree) if not name.startswith("_")}
    try:
        names = ast.literal_eval(assignments[-1].value)
    except (TypeError, ValueError, SyntaxError):
        names = None
    if isinstance(names, (list, tuple, set)) and all(isinstance(name, str) for name in names):
        bindings = _unconditional_top_level_bindings(tree)
        declared = set(names)
        if not declared <= set(bindings):
            return None
        if any(
            not isinstance(bindings[name], (ast.Import, ast.ImportFrom, ast.FunctionDef, ast.AsyncFunctionDef, ast.ClassDef))
            for name in declared
        ):
            return None
        return declared
    if _dynamic_initializer_exports_are_trusted(tree, package_exports, base_package):
        return package_exports
    return None


def _entrypoint_module_bindings(package: Path, module: str) -> set[str]:
    """Return unconditional bindings from a relative entrypoint target."""
    module_path = package / Path(*module.split("."))
    target = module_path.with_suffix(".py") if module_path.with_suffix(".py").is_file() else module_path / "__init__.py"
    if not target.is_file() or target.is_symlink():
        return set()
    try:
        target.resolve().relative_to(package.resolve())
    except (OSError, ValueError):
        return set()
    try:
        tree = _parse_source(target.read_text(encoding="utf-8"), target)
        _relative_import_contract(tree, package, target)
    except (OSError, SyntaxError, ValueError):
        return set()
    return _unconditional_top_level_names(tree)


def _dynamic_initializer_exports_are_trusted(
    tree: ast.Module,
    package_exports: set[str],
    base_package: Path,
) -> bool:
    """Return whether a dynamic initializer follows the reviewed export loop."""
    all_assignments = [
        (index, node)
        for index, node in enumerate(tree.body)
        if isinstance(node, ast.Assign)
        and any(isinstance(target, ast.Name) and target.id == "__all__" for target in node.targets)
        and isinstance(node.value, ast.Call)
        and isinstance(node.value.func, ast.Name)
        and node.value.func.id == "sorted"
        and len(node.value.args) == 1
        and isinstance(node.value.args[0], ast.Name)
        and node.value.args[0].id == "_PHASE192_EXPORT_NAMES"
        and not node.value.keywords
    ]
    if len(all_assignments) != 1:
        return False
    all_assignment_index, all_assignment = all_assignments[0]
    section_assignments = [
        (index, node)
        for index, node in enumerate(tree.body)
        if isinstance(node, ast.Assign)
        and any(
            isinstance(target, ast.Name) and target.id == "_PHASE192_SECTION_MODULES"
            for target in node.targets
        )
    ]
    if len(section_assignments) != 1:
        return False
    section_assignment_index, section_assignment = section_assignments[0]
    if (
        section_assignment is None
        or not isinstance(section_assignment.value, ast.Tuple)
        or not section_assignment.value.elts
    ):
        return False
    section_names = [
        element.id
        for element in section_assignment.value.elts
        if isinstance(element, ast.Name)
    ]
    if len(section_names) != len(section_assignment.value.elts) or len(set(section_names)) != len(section_names):
        return False
    imported_sections = {
        alias.asname: alias.name
        for node in tree.body
        if isinstance(node, ast.ImportFrom) and node.level == 1 and node.module is None
        for alias in node.names
        if alias.asname and alias.name != "*"
    }
    expected_sections = {
        path.resolve()
        for path in base_package.glob("*.py")
        if path.name not in {"__init__.py", "__main__.py"}
    }
    actual_sections: set[Path] = set()
    actual_exports: set[str] = set()
    for section_name in section_names:
        module = imported_sections.get(section_name)
        if not module:
            return False
        module_path = base_package / Path(*module.split("."))
        target = module_path.with_suffix(".py") if module_path.with_suffix(".py").is_file() else module_path / "__init__.py"
        if not target.is_file() or target.is_symlink():
            return False
        try:
            resolved_target = target.resolve()
            resolved_target.relative_to(base_package.resolve())
        except (OSError, ValueError):
            return False
        exports = _entrypoint_section_exports(target)
        if exports is None:
            return False
        actual_exports.update(exports)
        actual_sections.add(resolved_target)
    if actual_sections != expected_sections or actual_exports != package_exports:
        return False
    allowed_calls: set[int] = set()
    matched_loop_index: int | None = None
    for outer_index, outer in enumerate(tree.body):
        if not (
            isinstance(outer, ast.For)
            and isinstance(outer.target, ast.Name)
            and outer.target.id == "_phase192_section"
            and isinstance(outer.iter, ast.Name)
            and outer.iter.id == "_PHASE192_SECTION_MODULES"
        ):
            continue
        for inner in outer.body:
            if (
                isinstance(inner, ast.For)
                and isinstance(inner.target, ast.Name)
                and inner.target.id == "_phase192_name"
                and isinstance(inner.iter, ast.Name)
                and inner.iter.id == "_PHASE192_EXPORT_NAMES"
            ):
                for statement in inner.body:
                    if not (
                        isinstance(statement, ast.Assign)
                        and len(statement.targets) == 1
                        and isinstance(statement.targets[0], ast.Subscript)
                        and isinstance(statement.targets[0].value, ast.Attribute)
                        and isinstance(statement.targets[0].value.value, ast.Name)
                        and statement.targets[0].value.value.id == "_phase192_section"
                        and statement.targets[0].value.attr == "__dict__"
                        and isinstance(statement.targets[0].slice, ast.Name)
                        and statement.targets[0].slice.id == "_phase192_name"
                        and isinstance(statement.value, ast.Subscript)
                        and isinstance(statement.value.value, ast.Call)
                        and isinstance(statement.value.value.func, ast.Name)
                        and statement.value.value.func.id == "globals"
                        and not statement.value.value.args
                        and not statement.value.value.keywords
                        and isinstance(statement.value.slice, ast.Name)
                        and statement.value.slice.id == "_phase192_name"
                    ):
                        continue
                    allowed_calls.add(id(statement.value.value))
                continue
            if not (
                isinstance(inner, ast.For)
                and isinstance(inner.target, ast.Name)
                and inner.target.id == "_phase192_name"
                and isinstance(inner.iter, ast.Attribute)
                and inner.iter.attr == "__all__"
                and isinstance(inner.iter.value, ast.Name)
                and inner.iter.value.id == "_phase192_section"
            ):
                continue
            copied = False
            recorded = False
            for statement in inner.body:
                if isinstance(statement, ast.Assign) and len(statement.targets) == 1:
                    target = statement.targets[0]
                    value = statement.value
                    if (
                        isinstance(target, ast.Subscript)
                        and isinstance(target.value, ast.Call)
                        and isinstance(target.value.func, ast.Name)
                        and target.value.func.id == "globals"
                        and not target.value.args
                        and not target.value.keywords
                        and isinstance(target.slice, ast.Name)
                        and target.slice.id == "_phase192_name"
                        and isinstance(value, ast.Call)
                        and isinstance(value.func, ast.Name)
                        and value.func.id == "getattr"
                        and len(value.args) == 2
                        and isinstance(value.args[0], ast.Name)
                        and value.args[0].id == "_phase192_section"
                        and isinstance(value.args[1], ast.Name)
                        and value.args[1].id == "_phase192_name"
                        and not value.keywords
                    ):
                        allowed_calls.update({id(target.value), id(value)})
                        copied = True
                if (
                    isinstance(statement, ast.Expr)
                    and isinstance(statement.value, ast.Call)
                    and isinstance(statement.value.func, ast.Attribute)
                    and isinstance(statement.value.func.value, ast.Name)
                    and statement.value.func.value.id == "_PHASE192_EXPORT_NAMES"
                    and statement.value.func.attr == "add"
                    and len(statement.value.args) == 1
                    and isinstance(statement.value.args[0], ast.Name)
                    and statement.value.args[0].id == "_phase192_name"
                    and not statement.value.keywords
                ):
                    recorded = True
            if copied and recorded:
                if matched_loop_index is not None:
                    return False
                matched_loop_index = outer_index
    if matched_loop_index is None:
        return False
    if not section_assignment_index < matched_loop_index < all_assignment_index:
        return False
    allowed_binding_references = {
        id(candidate)
        for target in (*all_assignment.targets, *section_assignment.targets)
        for candidate in ast.walk(target)
        if isinstance(candidate, ast.Name)
    }
    for outer in ast.walk(tree):
        if not (
            isinstance(outer, ast.For)
            and isinstance(outer.target, ast.Name)
            and outer.target.id == "_phase192_section"
            and isinstance(outer.iter, ast.Name)
            and outer.iter.id == "_PHASE192_SECTION_MODULES"
        ):
            continue
        allowed_binding_references.add(id(outer.iter))
    for candidate in ast.walk(tree):
        if (
            isinstance(candidate, ast.Name)
            and candidate.id in {"__all__", "_PHASE192_SECTION_MODULES"}
            and id(candidate) not in allowed_binding_references
        ):
            return False
    export_set_assignments = [
        index
        for index, node in enumerate(tree.body)
        if isinstance(node, ast.Assign)
        and any(
            isinstance(target, ast.Name) and target.id == "_PHASE192_EXPORT_NAMES"
            for target in node.targets
        )
    ]
    if len(export_set_assignments) != 1 or export_set_assignments[0] >= matched_loop_index:
        return False
    for index, node in enumerate(tree.body):
        targets = (
            node.targets
            if isinstance(node, (ast.Assign, ast.Delete))
            else [node.target]
            if isinstance(node, (ast.AnnAssign, ast.AugAssign))
            else ()
        )
        for target in targets:
            if any(
                isinstance(candidate, ast.Name) and candidate.id in section_names
                for candidate in ast.walk(target)
            ):
                return False
        if index < matched_loop_index and any(
            isinstance(candidate, ast.Attribute)
            and isinstance(candidate.value, ast.Name)
            and candidate.value.id in section_names
            for candidate in ast.walk(node)
        ):
            return False
    for node in tree.body:
        if isinstance(node, ast.Assign):
            for target in node.targets:
                if isinstance(target, ast.Name) and target.id == "_PHASE192_EXPORT_NAMES":
                    if not (
                        isinstance(node.value, ast.Call)
                        and isinstance(node.value.func, ast.Name)
                        and node.value.func.id == "set"
                        and not node.value.args
                        and not node.value.keywords
                    ):
                        return False
        if isinstance(node, ast.Delete):
            if any(
                isinstance(candidate, ast.Name) and candidate.id in package_exports
                for candidate in ast.walk(node)
            ):
                return False
        if any(
            isinstance(candidate, ast.Constant) and candidate.value in package_exports
            for candidate in ast.walk(node)
        ):
            return False
        for candidate in ast.walk(node):
            if (
                isinstance(candidate, ast.Call)
                and isinstance(candidate.func, ast.Attribute)
                and isinstance(candidate.func.value, ast.Name)
                and candidate.func.value.id in {"__all__", "_PHASE192_SECTION_MODULES"}
            ):
                return False
            if not isinstance(candidate, ast.Call) or not isinstance(candidate.func, ast.Attribute):
                continue
            if (
                isinstance(candidate.func.value, ast.Name)
                and candidate.func.value.id == "_PHASE192_EXPORT_NAMES"
                and candidate.func.attr != "add"
            ):
                return False
            if (
                isinstance(candidate.func.value, ast.Call)
                and isinstance(candidate.func.value.func, ast.Name)
                and candidate.func.value.func.id == "globals"
                and candidate.func.attr in {"clear", "pop", "popitem", "setdefault", "update", "__delitem__"}
            ):
                return False
    return all(
        not (
            isinstance(node, ast.Call)
            and isinstance(node.func, ast.Name)
            and node.func.id in {"globals", "getattr"}
        )
        or id(node) in allowed_calls
        for node in ast.walk(tree)
    )


_ENTRYPOINT_IMPORT_ROOTS = frozenset({"Scripts", "argparse", "sys", "unittest"})

_REVIEWED_DIRECT_ENTRYPOINTS = frozenset({
    "Scripts/ros2forunity/windows/lyrical/regression_checks/test_build_r2fu_runtime_package.py",
    "Scripts/ros2forunity/windows/lyrical/regression_checks/test_validate_r2fu_runtime_package.py",
})

__all__ = ['_relative_import_contract', '_symbols', '_section_exports', '_initializer_lines', '_initializer_digest', '_NAMESPACE_RISK_ATTRIBUTES', '_NAMESPACE_RISK_MODULE_ATTRIBUTES', '_NAMESPACE_RISK_NAMES', '_namespace_risk_signatures', '_is_phase121_configuration_path', '_ENTRYPOINT_IMPORT_ROOTS', '_REVIEWED_DIRECT_ENTRYPOINTS', '_entrypoint_section_imports_are_trusted', '_function_signature_is_inert']
