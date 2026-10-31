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


def _reviewed_validator_initializer_is_safe(source: str, path: str) -> None:
    """Validate the exact import-time shape allowed for the reviewed validator split."""
    tree = _parse_source(source, path)
    _phase192_template_validate_initializer(tree, path)
    parents: dict[int, ast.AST] = {}
    for parent in ast.walk(tree):
        for child in ast.iter_child_nodes(parent):
            parents[id(child)] = parent

    def has_runtime_parent(node: ast.AST) -> bool:
        """Return whether a node is nested in a function or class body."""
        current = parents.get(id(node))
        while current is not None:
            if isinstance(current, (ast.FunctionDef, ast.AsyncFunctionDef, ast.ClassDef, ast.Lambda)):
                return True
            current = parents.get(id(current))
        return False

    def exact_section_export_assignment(node: ast.Assign) -> bool:
        """Recognize the generated globals export assignment."""
        if len(node.targets) != 1 or not isinstance(node.targets[0], ast.Subscript):
            return False
        target = node.targets[0]
        return (
            isinstance(target.value, ast.Call)
            and isinstance(target.value.func, ast.Name)
            and target.value.func.id == "globals"
            and isinstance(target.slice, ast.Name)
            and target.slice.id == "_phase192_name"
        )

    def exact_section_sync_assignment(node: ast.Assign) -> bool:
        """Recognize the generated section namespace sync assignment."""
        if len(node.targets) != 1 or not isinstance(node.targets[0], ast.Subscript):
            return False
        target = node.targets[0]
        return (
            isinstance(target.value, ast.Attribute)
            and isinstance(target.value.value, ast.Name)
            and target.value.value.id == "_phase192_section"
            and target.value.attr == "__dict__"
            and isinstance(target.slice, ast.Name)
            and target.slice.id == "_phase192_name"
        )

    def expression(source_text: str) -> ast.AST:
        """Parse one reviewed expression without source-location attributes."""
        return ast.parse(source_text, filename=path, mode="eval").body

    def same_expression(actual: ast.AST, expected: str) -> bool:
        """Compare an AST expression with its reviewed source form."""
        return ast.dump(actual, include_attributes=False) == ast.dump(
            expression(expected),
            include_attributes=False,
        )

    expected_assignments = {
        "_PHASE192_FACADE_FILE": (
            "_Phase192Path(__file__).resolve().parents[1] / "
            '"validate_r2fu_runtime_package.py"'
        ),
        "__file__": "str(_PHASE192_FACADE_FILE)",
        "_PHASE192_PARENT": "_PHASE192_FACADE_FILE.parent",
        "_PHASE192_SECTION_MODULES": "(_phase192_section_0, _phase192_section_1, _phase192_section_2)",
        "_PHASE192_EXPORT_NAMES": "set()",
        "_phase192_module": "_phase192_sys.modules.get(__name__)",
    }
    expected_configuration = {
        "ROOT": "_Phase192Path(_phase192_section_0.__file__).resolve().parents[5]",
        "PACKAGE": '_phase192_section_0.ROOT / "Packages" / _phase192_section_0.PACKAGE_NAME',
        "ADAPTER_PACKAGE": '_phase192_section_0.ROOT / "Packages" / "dev.unity2foxglove.ros2forunity"',
        "CORE_PACKAGE": '_phase192_section_0.ROOT / "Packages" / "dev.unity2foxglove.sdk"',
        "RUNTIME_ROOT": '_phase192_section_0.PACKAGE / "Runtime" / "Ros2ForUnity"',
        "PLUGIN_ROOT": '_phase192_section_0.RUNTIME_ROOT / "Plugins" / "Windows" / "x86_64"',
        "MANIFEST": '_phase192_section_0.PACKAGE / "RuntimeSupport" / "runtime-manifest.json"',
        "INVENTORY": '_phase192_section_0.PACKAGE / "RuntimeSupport" / "r2fu-lyrical-win64-runtime-inventory.json"',
        "PUBLIC_DOCS": (
            '(_phase192_section_0.PACKAGE / "README.md", '
            '_phase192_section_0.PACKAGE / "THIRD_PARTY_NOTICES.md", '
            '_phase192_section_0.PACKAGE / "package.json", '
            '_phase192_section_0.MANIFEST)'
        ),
    }

    expected_imports = (
        ("from", "__future__", 0, "annotations", None),
        ("import", "sys", 0, "sys", "_phase192_sys"),
        ("import", "types", 0, "types", "_phase192_types"),
        ("from", "pathlib", 0, "Path", "_Phase192Path"),
        ("from", "", 1, "implementation_lyrical", "_phase192_section_0"),
        ("from", "", 1, "manifest_and_inventory", "_phase192_section_1"),
        ("from", "", 1, "runtime_checks", "_phase192_section_2"),
    )
    actual_imports: list[tuple[str, str, int, str, str | None]] = []
    for node in tree.body:
        if isinstance(node, ast.Import):
            actual_imports.extend(
                ("import", alias.name, 0, alias.name, alias.asname)
                for alias in node.names
            )
        elif isinstance(node, ast.ImportFrom):
            actual_imports.extend(
                ("from", node.module or "", node.level, alias.name, alias.asname)
                for alias in node.names
            )
    if tuple(actual_imports) != expected_imports:
        raise ValueError(f"Unsupported generated package imports in {path}")

    def exact_path_guard(node: ast.If) -> bool:
        """Recognize the reviewed package-parent path insertion guard."""
        if not same_expression(
            node.test,
            "str(_PHASE192_PARENT) not in _phase192_sys.path",
        ) or node.orelse or len(node.body) != 1:
            return False
        statement = node.body[0]
        return isinstance(statement, ast.Expr) and same_expression(
            statement.value,
            "_phase192_sys.path.insert(0, str(_PHASE192_PARENT))",
        )

    def exact_module_guard(node: ast.If) -> bool:
        """Recognize the reviewed forwarding-module class guard."""
        if not same_expression(
            node.test,
            "_phase192_module is not None",
        ) or node.orelse or len(node.body) != 1:
            return False
        statement = node.body[0]
        return (
            isinstance(statement, ast.Assign)
            and len(statement.targets) == 1
            and isinstance(statement.targets[0], ast.Attribute)
            and isinstance(statement.targets[0].value, ast.Name)
            and statement.targets[0].value.id == "_phase192_module"
            and statement.targets[0].attr == "__class__"
            and same_expression(statement.value, "_Phase192PackageModule")
        )

    def exact_population_loop(node: ast.For) -> bool:
        """Recognize the generated export collection loop in full."""
        if (
            not isinstance(node.target, ast.Name)
            or node.target.id != "_phase192_section"
            or not isinstance(node.iter, ast.Name)
            or node.iter.id != "_PHASE192_SECTION_MODULES"
            or node.orelse
            or len(node.body) != 1
            or not isinstance(node.body[0], ast.For)
        ):
            return False
        inner = node.body[0]
        if (
            not isinstance(inner.target, ast.Name)
            or inner.target.id != "_phase192_name"
            or not isinstance(inner.iter, ast.Attribute)
            or not isinstance(inner.iter.value, ast.Name)
            or inner.iter.value.id != "_phase192_section"
            or inner.iter.attr != "__all__"
            or inner.orelse
            or len(inner.body) != 2
        ):
            return False
        export, record = inner.body
        return (
            isinstance(export, ast.Assign)
            and exact_section_export_assignment(export)
            and same_expression(
                export.value,
                "getattr(_phase192_section, _phase192_name)",
            )
            and isinstance(record, ast.Expr)
            and same_expression(
                record.value,
                "_PHASE192_EXPORT_NAMES.add(_phase192_name)",
            )
        )

    def exact_sync_loop(node: ast.For) -> bool:
        """Recognize the generated section namespace synchronization loop."""
        if (
            not isinstance(node.target, ast.Name)
            or node.target.id != "_phase192_section"
            or not isinstance(node.iter, ast.Name)
            or node.iter.id != "_PHASE192_SECTION_MODULES"
            or node.orelse
            or len(node.body) != 1
            or not isinstance(node.body[0], ast.For)
        ):
            return False
        inner = node.body[0]
        if (
            not isinstance(inner.target, ast.Name)
            or inner.target.id != "_phase192_name"
            or not isinstance(inner.iter, ast.Name)
            or inner.iter.id != "_PHASE192_EXPORT_NAMES"
            or inner.orelse
            or len(inner.body) != 1
            or not isinstance(inner.body[0], ast.Assign)
            or not exact_section_sync_assignment(inner.body[0])
        ):
            return False
        return same_expression(
            inner.body[0].value,
            "globals()[_phase192_name]",
        )

    top_level_loops = [node for node in tree.body if isinstance(node, ast.For)]
    if len(top_level_loops) != 2 or not exact_population_loop(top_level_loops[0]) or not exact_sync_loop(top_level_loops[1]):
        raise ValueError(f"Unsupported generated package section loops in {path}")
    top_level_ifs = [node for node in tree.body if isinstance(node, ast.If)]
    if len(top_level_ifs) != 2 or not any(exact_path_guard(node) for node in top_level_ifs) or not any(exact_module_guard(node) for node in top_level_ifs):
        raise ValueError(f"Unsupported generated package guards in {path}")

    seen_assignments: set[str] = set()
    seen_configuration: set[str] = set()
    seen_module_class = False

    for node in ast.walk(tree):
        if has_runtime_parent(node):
            continue
        if isinstance(node, ast.Assign):
            if exact_section_export_assignment(node):
                if not same_expression(node.value, "getattr(_phase192_section, _phase192_name)"):
                    raise ValueError(f"Unsupported generated section export assignment in {path}")
                continue
            if exact_section_sync_assignment(node):
                if not same_expression(node.value, "globals()[_phase192_name]"):
                    raise ValueError(f"Unsupported generated section sync assignment in {path}")
                continue
            if len(node.targets) != 1:
                raise ValueError(f"Unsupported generated package rebinding in {path}")
            target = node.targets[0]
            if isinstance(target, ast.Name) and target.id in expected_assignments:
                if target.id in seen_assignments or not same_expression(node.value, expected_assignments[target.id]):
                    raise ValueError(f"Unsupported generated package rebinding in {path}")
                seen_assignments.add(target.id)
                continue
            if isinstance(target, ast.Name) and target.id == "__all__":
                if "__all__" in seen_assignments or not same_expression(node.value, "sorted(_PHASE192_EXPORT_NAMES)"):
                    raise ValueError(f"Unsupported generated package export assignment in {path}")
                seen_assignments.add("__all__")
                continue
            if (
                isinstance(target, ast.Attribute)
                and isinstance(target.value, ast.Name)
                and target.value.id == "_phase192_section_0"
                and target.attr in expected_configuration
            ):
                if target.attr in seen_configuration or not same_expression(node.value, expected_configuration[target.attr]):
                    raise ValueError(f"Unsupported generated package configuration in {path}")
                seen_configuration.add(target.attr)
                continue
            if (
                isinstance(target, ast.Attribute)
                and isinstance(target.value, ast.Name)
                and target.value.id == "_phase192_module"
                and target.attr == "__class__"
            ):
                if seen_module_class or not same_expression(node.value, "_Phase192PackageModule"):
                    raise ValueError(f"Unsupported generated package module class in {path}")
                seen_module_class = True
                continue
            raise ValueError(f"Unsupported generated package rebinding in {path}")
        elif isinstance(node, ast.Delete):
            raise ValueError(f"Unsupported generated package deletion in {path}")

    if (
        set(expected_assignments) != (seen_assignments - {"__all__"})
        or set(expected_configuration) != seen_configuration
        or "__all__" not in seen_assignments
        or not seen_module_class
    ):
        raise ValueError(f"Incomplete generated package initializer in {path}")


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

__all__ = ['_phase192_template_validate_initializer', '_reviewed_validator_initializer_is_safe', '_phase192_initializer_kind', '_phase192_generated_initializer', '_phase138u_simple_initializer']
