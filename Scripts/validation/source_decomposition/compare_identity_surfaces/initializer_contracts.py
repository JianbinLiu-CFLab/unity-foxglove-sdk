from __future__ import annotations

from .foundation import *

def _phase192_template_validate_initializer(tree: ast.Module, path: str) -> None:
    """Validate the generated package initializer shape."""
    parents: dict[int, ast.AST] = {}
    for parent in ast.walk(tree):
        for child in ast.iter_child_nodes(parent):
            parents[id(child)] = parent

    def ancestors(node: ast.AST) -> Iterator[ast.AST]:
        """Yield enclosing syntax nodes from the immediate parent outward."""
        current = parents.get(id(node))
        while current is not None:
            yield current
            current = parents.get(id(current))

    def exact_section_loop(node: ast.AST) -> bool:
        """Recognize the generated section-module loop."""
        return (
            isinstance(node, ast.For)
            and isinstance(node.target, ast.Name)
            and node.target.id == "_phase192_section"
            and isinstance(node.iter, ast.Name)
            and node.iter.id == "_PHASE192_SECTION_MODULES"
            and not node.orelse
        )

    def exact_section_export_loop(node: ast.AST) -> bool:
        """Recognize the generated loop deriving names from section __all__."""
        return (
            isinstance(node, ast.For)
            and isinstance(node.target, ast.Name)
            and node.target.id == "_phase192_name"
            and isinstance(node.iter, ast.Attribute)
            and isinstance(node.iter.value, ast.Name)
            and node.iter.value.id == "_phase192_section"
            and node.iter.attr == "__all__"
            and not node.orelse
        )

    def exact_export_names_loop(node: ast.AST) -> bool:
        """Recognize the generated compatibility export-name loop."""
        return (
            isinstance(node, ast.For)
            and isinstance(node.target, ast.Name)
            and node.target.id == "_phase192_name"
            and isinstance(node.iter, ast.Name)
            and node.iter.id == "_PHASE192_EXPORT_NAMES"
            and not node.orelse
        )

    def exact_hasattr_guard(node: ast.AST) -> bool:
        """Recognize the guarded section lookup used by collision-safe packages."""
        return (
            isinstance(node, ast.If)
            and not node.orelse
            and isinstance(node.test, ast.Call)
            and isinstance(node.test.func, ast.Name)
            and node.test.func.id == "hasattr"
            and len(node.test.args) == 2
            and isinstance(node.test.args[0], ast.Name)
            and node.test.args[0].id == "_phase192_section"
            and isinstance(node.test.args[1], ast.Name)
            and node.test.args[1].id == "_phase192_name"
        )

    section_exports_loop_present = any(
        exact_section_export_loop(node)
        for node in ast.walk(tree)
    )
    for node in ast.walk(tree):
        if not (
            isinstance(node, ast.Assign)
            and len(node.targets) == 1
            and isinstance(node.targets[0], ast.Subscript)
            and isinstance(node.targets[0].value, ast.Call)
            and isinstance(node.targets[0].value.func, ast.Name)
            and node.targets[0].value.func.id == "globals"
            and isinstance(node.targets[0].slice, ast.Name)
            and node.targets[0].slice.id == "_phase192_name"
        ):
            continue
        expected_value = ast.Call(
            func=ast.Name(id="getattr", ctx=ast.Load()),
            args=[
                ast.Name(id="_phase192_section", ctx=ast.Load()),
                ast.Name(id="_phase192_name", ctx=ast.Load()),
            ],
            keywords=[],
        )
        if ast.dump(node.value, include_attributes=False) != ast.dump(
            expected_value,
            include_attributes=False,
        ):
            raise ValueError(f"Unsupported generated section export assignment in {path}")
        enclosing = tuple(ancestors(node))
        if not any(exact_section_loop(item) for item in enclosing):
            raise ValueError(f"Unsupported generated section export assignment in {path}")
        if any(exact_section_export_loop(item) for item in enclosing):
            continue
        if not (
            section_exports_loop_present
            and any(exact_export_names_loop(item) for item in enclosing)
            and any(exact_hasattr_guard(item) for item in enclosing)
        ):
            raise ValueError(f"Unsupported generated section export assignment in {path}")

    classes = [node for node in tree.body if isinstance(node, ast.ClassDef)]
    if len(classes) != 1:
        raise ValueError(f"Generated package must contain one forwarding class: {path}")
    _phase192_template_validate_class(classes[0], path, package=True)
    _phase192_template_validate_imports(tree, path)
    _phase192_template_validate_calls(tree, path)
    _phase192_validate_template_registry_mutations(tree, path)
    for index, node in enumerate(tree.body):
        if isinstance(node, ast.FunctionDef) and node.name == "load_tests":
            if node.decorator_list or not node.body or any(
                not isinstance(statement, (ast.Expr, ast.Return, ast.Pass))
                for statement in node.body
            ):
                raise ValueError(f"Unsupported generated load_tests function in {path}")
            continue
        if isinstance(node, ast.Expr):
            if index != 0 or not isinstance(node.value, ast.Constant) or not isinstance(node.value.value, str):
                raise ValueError(f"Unsupported generated package expression in {path}")
            continue
        if isinstance(node, (ast.Import, ast.ImportFrom, ast.Assign, ast.Delete, ast.If, ast.For, ast.ClassDef)):
            continue
        raise ValueError(f"Unsupported generated package statement in {path}")
    if not any(
        isinstance(node, ast.ImportFrom) and node.level == 1
        for node in tree.body
    ):
        raise ValueError(f"Generated package has no relative section imports in {path}")





def _phase192_initializer_kind(source: str) -> str | None:
    """Return the AST-derived generated package initializer kind."""
    tree = ast.parse(source)
    for node in tree.body:
        if isinstance(node, ast.ClassDef) and node.name == "_Phase192PackageModule":
            return "phase192"
        if isinstance(node, ast.ClassDef) and node.name == "_Phase138UPackageModule":
            return "phase138u"
    if any(
        isinstance(node, ast.ImportFrom)
        and node.level == 1
        and node.module == "pointcloud_metrics"
        for node in tree.body
    ):
        return "phase138u"
    return None


def _phase192_generated_initializer(source: str) -> bool:
    """Return whether source declares one of the reviewed package templates."""
    return _phase192_initializer_kind(source) is not None


def _phase138u_simple_initializer(source: str, path: str) -> None:
    """Validate the earlier explicit Phase138U re-export initializer."""
    tree = _parse_source(source, path)
    allowed_modules = {"pointcloud_metrics", "subscription", "acceptance_cli"}
    for node in tree.body:
        if isinstance(node, ast.Expr) and isinstance(node.value, ast.Constant) and isinstance(node.value.value, str):
            continue
        if isinstance(node, ast.ImportFrom):
            if node.module == "__future__":
                if node.level or any(alias.name != "annotations" for alias in node.names):
                    raise ValueError(f"Unsupported Phase138U future import in {path}")
                continue
            if node.level != 1 or node.module not in allowed_modules:
                raise ValueError(f"Unsupported Phase138U initializer import in {path}")
            continue
        if isinstance(node, ast.Assign):
            if not any(isinstance(target, ast.Name) and target.id == "__all__" for target in node.targets):
                raise ValueError(f"Unsupported Phase138U initializer assignment in {path}")
            try:
                value = ast.literal_eval(node.value)
            except (TypeError, ValueError) as exc:
                raise ValueError(f"Unsupported Phase138U __all__ in {path}") from exc
            if not isinstance(value, list) or not all(isinstance(item, str) for item in value):
                raise ValueError(f"Unsupported Phase138U __all__ in {path}")
            continue
        raise ValueError(f"Unsupported Phase138U initializer statement in {path}")

__all__ = ['_phase192_template_validate_initializer', '_phase192_initializer_kind', '_phase192_generated_initializer', '_phase138u_simple_initializer']
