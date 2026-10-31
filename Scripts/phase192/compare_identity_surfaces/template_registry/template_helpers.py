from __future__ import annotations

from compare_identity_surfaces.foundation import *
from .registry_mutations import _phase192_validate_template_registry_mutations

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

__all__ = [
    '_phase192_template_name',
    '_phase192_template_call_is_safe',
    '_phase192_template_validate_calls',
    '_phase192_template_validate_imports',
    '_phase192_template_validate_class',
    '_phase192_template_validate_facade',
]
