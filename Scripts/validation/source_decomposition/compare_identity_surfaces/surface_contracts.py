from __future__ import annotations

from .foundation import *

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

_ENTRYPOINT_IMPORT_ROOTS = frozenset({"Scripts", "argparse", "sys", "unittest"})

_REVIEWED_DIRECT_ENTRYPOINTS = frozenset({
    "Scripts/ros2forunity/windows/lyrical/regression_checks/test_build_r2fu_runtime_package.py",
    "Scripts/ros2forunity/windows/lyrical/regression_checks/test_validate_r2fu_runtime_package.py",
})

__all__ = ['_relative_import_contract', '_symbols', '_section_exports', '_initializer_lines', '_initializer_digest', '_NAMESPACE_RISK_ATTRIBUTES', '_NAMESPACE_RISK_MODULE_ATTRIBUTES', '_NAMESPACE_RISK_NAMES', '_namespace_risk_signatures', '_is_phase121_configuration_path', '_ENTRYPOINT_IMPORT_ROOTS', '_REVIEWED_DIRECT_ENTRYPOINTS']
