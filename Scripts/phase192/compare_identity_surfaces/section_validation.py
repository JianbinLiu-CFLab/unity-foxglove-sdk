from __future__ import annotations

from .foundation import *

def _validate_generated_section(
    source: str,
    path: Path,
    package_dir: Path,
    section_names: frozenset[str],
    *,
    require_all: bool = True,
) -> None:
    """Validate a moved implementation section without pretending it is inert.

    Split sections intentionally contain the original classes and functions, so
    the strict initializer validator is not applicable to their bodies.  The
    section still goes through the ordinary export/namespace parser and this
    pass rejects import-time dynamic execution that cannot be part of a source
    section.
    """
    tree = _parse_source(source, path)
    _relative_import_contract(tree, package_dir, path, section_names)
    if require_all:
        _section_exports(source, str(path), reject_conditional=False)
    else:
        _symbols(source, str(path), reject_conditional=False)

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
    if any(
        isinstance(statement, ast.Assign)
        and any(
            isinstance(target, ast.Name)
            and target.id in path_ctor_names | path_module_names
            for target in statement.targets
        )
        for statement in ast.walk(tree)
    ):
        raise ValueError(f"Path constructor rebinding is not allowed in generated section: {path}")

    def path_from_file(node: ast.AST, names: frozenset[str]) -> bool:
        """Recognize a path value derived from this source file."""
        if isinstance(node, ast.Name):
            return node.id in names
        if isinstance(node, ast.Call):
            return (
                isinstance(node.func, ast.Attribute)
                and node.func.attr == "resolve"
                and not node.args
                and not node.keywords
                and isinstance(node.func.value, ast.Call)
                and isinstance(node.func.value.func, (ast.Name, ast.Attribute))
                and (
                    isinstance(node.func.value.func, ast.Name)
                    and node.func.value.func.id in path_ctor_names
                    or isinstance(node.func.value.func, ast.Attribute)
                    and node.func.value.func.attr == "Path"
                    and isinstance(node.func.value.func.value, ast.Name)
                    and node.func.value.func.value.id in path_module_names
                )
                and len(node.func.value.args) == 1
                and isinstance(node.func.value.args[0], ast.Name)
                and node.func.value.args[0].id == "__file__"
            )
        if isinstance(node, ast.Attribute) and node.attr == "parent":
            return path_from_file(node.value, names)
        if isinstance(node, ast.Subscript) and isinstance(node.value, ast.Attribute) and node.value.attr == "parents":
            return path_from_file(node.value.value, names) and isinstance(node.slice, ast.Constant) and isinstance(node.slice.value, int)
        return False

    path_names: set[str] = set()
    changed = True
    while changed:
        changed = False
        for statement in ast.walk(tree):
            if not isinstance(statement, ast.Assign):
                continue
            if not path_from_file(statement.value, frozenset(path_names)):
                continue
            for target in statement.targets:
                if isinstance(target, ast.Name) and target.id not in path_names:
                    path_names.add(target.id)
                    changed = True

    def allowed_sys_path_insert(node: ast.Call) -> bool:
        """Allow only the reviewed file-derived sys.path bootstrap."""
        return (
            isinstance(node.func, ast.Attribute)
            and node.func.attr == "insert"
            and isinstance(node.func.value, ast.Attribute)
            and node.func.value.attr == "path"
            and isinstance(node.func.value.value, ast.Name)
            and node.func.value.value.id in {"sys", "_sys", "_phase192_sys"}
            and len(node.args) == 2
            and not node.keywords
            and isinstance(node.args[0], ast.Constant)
            and node.args[0].value == 0
            and isinstance(node.args[1], ast.Call)
            and isinstance(node.args[1].func, ast.Name)
            and node.args[1].func.id == "str"
            and len(node.args[1].args) == 1
            and not node.args[1].keywords
            and path_from_file(node.args[1].args[0], frozenset(path_names))
        )

    for node in ast.walk(tree):
        if isinstance(node, ast.Call) and isinstance(node.func, ast.Attribute) and node.func.attr == "insert":
            if isinstance(node.func.value, ast.Attribute) and node.func.value.attr == "path":
                if not allowed_sys_path_insert(node):
                    raise ValueError(f"Unsupported generated sys.path bootstrap in {path}")
    namespace_risks = set(_namespace_risk_signatures(tree, allow_public_globals=True))
    normalized_path = path.as_posix().replace("\\", "/")
    if normalized_path.startswith("Scripts/") or "/Scripts/" in normalized_path:
        namespace_risks = {
            risk
            for risk in namespace_risks
            if not ("attr='insert'" in risk and "attr='path'" in risk)
        }
    if namespace_risks:
        raise ValueError(f"Unsupported import-time namespace risk in generated section: {path}")
    parents: dict[int, ast.AST] = {}
    for parent in ast.walk(tree):
        for child in ast.iter_child_nodes(parent):
            parents[id(child)] = parent

    module_aliases: dict[str, str] = {}
    callable_aliases: dict[str, str] = {}
    for statement in tree.body:
        if isinstance(statement, ast.Import):
            for alias in statement.names:
                module_aliases[alias.asname or alias.name.split(".", 1)[0]] = alias.name
        elif isinstance(statement, ast.ImportFrom) and statement.level == 0:
            module = statement.module or ""
            for alias in statement.names:
                if alias.name != "*":
                    callable_aliases[alias.asname or alias.name] = f"{module}.{alias.name}"

    function_body_nodes: set[int] = set()
    for definition in ast.walk(tree):
        if isinstance(definition, (ast.FunctionDef, ast.AsyncFunctionDef)):
            function_body_nodes.update(id(node) for statement in definition.body for node in ast.walk(statement))

    def is_inside_callable(node: ast.AST) -> bool:
        """Return whether node executes only when a function is called."""
        return id(node) in function_body_nodes

    def is_export_globals_call(node: ast.Call) -> bool:
        """Allow the generated __all__ comprehension to enumerate globals."""
        current: ast.AST = node
        while id(current) in parents and not isinstance(current, (ast.ListComp, ast.SetComp, ast.DictComp, ast.GeneratorExp)):
            current = parents[id(current)]
        if not isinstance(current, (ast.ListComp, ast.SetComp, ast.DictComp, ast.GeneratorExp)):
            return False
        owner = parents.get(id(current))
        return isinstance(owner, ast.Assign) and any(
            isinstance(target, ast.Name) and target.id == "__all__"
            for target in owner.targets
        )

    def qualified_call(node: ast.Call) -> tuple[str, str] | None:
        """Resolve a call target through import aliases without executing it."""
        if isinstance(node.func, ast.Name):
            target = callable_aliases.get(node.func.id)
            if target:
                module, _, attr = target.rpartition(".")
                return module, attr
        if isinstance(node.func, ast.Attribute) and isinstance(node.func.value, ast.Name):
            module = module_aliases.get(node.func.value.id)
            if module:
                return module, node.func.attr
        if isinstance(node.func, ast.Attribute):
            return None, node.func.attr
        return None

    for node in ast.walk(tree):
        if isinstance(node, ast.Global) and not is_inside_callable(node):
            raise ValueError(f"Global rebinding is not allowed in generated section: {path}")
        if isinstance(node, ast.Call):
            if is_inside_callable(node):
                continue
            qualified = qualified_call(node)
            if (
                isinstance(node.func, ast.Name)
                and node.func.id in {
                    "__import__", "compile", "eval", "exec", "open", "getattr",
                    "setattr", "delattr", "globals", "locals", "vars",
                }
            ):
                if node.func.id == "globals" and is_export_globals_call(node):
                    if "globals" not in _top_level_names(tree, include_imports=True):
                        continue
                if not (
                    node.func.id == "__import__"
                    and node.args
                    and isinstance(node.args[0], ast.Constant)
                    and isinstance(node.args[0].value, str)
                    and node.args[0].value == "pathlib"
                    and all(
                        keyword.arg == "fromlist"
                        and isinstance(keyword.value, (ast.List, ast.Tuple))
                        and all(
                            isinstance(item, ast.Constant)
                            and isinstance(item.value, str)
                            for item in keyword.value.elts
                        )
                        for keyword in node.keywords
                    )
                ):
                    raise ValueError(f"Unsupported dynamic execution in generated section: {path}")
            if qualified and (
                qualified[1]
                in {
                    "system", "popen", "run", "Popen", "check_call", "check_output",
                    "rmtree", "remove", "unlink", "write_text", "write_bytes", "mkdir",
                    "rmdir", "rename", "replace", "chmod", "insert", "open", "touch",
                    "append", "extend",
                }
                and (
                    qualified[0] in {"os", "subprocess", "shutil", "sys"}
                    or qualified[0] is None
                    or qualified[0].startswith("pathlib")
                )
                and not (
                    qualified[1] == "insert"
                    and allowed_sys_path_insert(node)
                )
            ):
                raise ValueError(f"Unsupported process execution in generated section: {path}")
        if isinstance(node, ast.Attribute) and node.attr in {
            "__class__", "__bases__", "__mro__", "__subclasses__",
        } and isinstance(node.ctx, ast.Store):
            raise ValueError(f"Unsupported type mutation in generated section: {path}")

def _validate_import_time_section(
    source: str,
    path: Path,
    package_dir: Path,
    new_sections: frozenset[str],
    seen: set[Path] | None = None,
) -> None:
    """Reject import-time control flow and side effects in an additive section."""
    seen = set() if seen is None else seen
    path = path.resolve()
    if path in seen:
        return
    seen.add(path)
    tree = _parse_source(source, path)
    initializer_kind = _phase192_initializer_kind(source)
    if path.name == "__init__.py" and initializer_kind is not None:
        if initializer_kind == "phase192":
            _phase192_template_validate_initializer(tree, str(path))
        else:
            _phase138u_simple_initializer(source, str(path))
        return
    postponed_annotations = any(
        isinstance(node, ast.ImportFrom)
        and node.module == "__future__"
        and not node.level
        and any(alias.name == "annotations" for alias in node.names)
        for node in tree.body
    )
    available_names: set[str] = set(_SAFE_BUILTIN_NAMES) | {
        "__doc__", "__name__", "__package__", "__spec__"
    }

    def bound_names(node: ast.stmt) -> set[str]:
        """Return simple names introduced by an import or literal definition."""
        if isinstance(node, ast.Import):
            return {alias.asname or alias.name.split(".", 1)[0] for alias in node.names}
        if isinstance(node, ast.ImportFrom):
            return {
                alias.asname or alias.name
                for alias in node.names
                if alias.name != "*" and node.module != "__future__"
            }
        if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef, ast.ClassDef)):
            return {node.name}
        if isinstance(node, ast.Assign):
            return set().union(*(target_names(target) for target in node.targets))
        if isinstance(node, ast.AnnAssign):
            return target_names(node.target)
        return set()

    def target_names(target: ast.AST) -> set[str]:
        """Return names for a literal assignment target."""
        if isinstance(target, ast.Name):
            return {target.id}
        if isinstance(target, (ast.Tuple, ast.List)):
            return set().union(*(target_names(item) for item in target.elts))
        return set()

    def relative_target(name: str) -> Path:
        """Resolve a package-relative section target without executing it."""
        parts = name.split(".")
        if not parts or any(_SECTION_NAME_RE.fullmatch(part) is None for part in parts):
            raise ValueError(f"Unsafe relative import in {path}: {name}")
        target = package_dir.joinpath(*parts).with_suffix(".py")
        if not target.is_file():
            target = package_dir.joinpath(*parts) / "__init__.py"
        if not target.is_file():
            raise ValueError(f"Missing relative import target in {path}: {name}")
        return target

    def validate_block(
        statements: list[ast.stmt],
        names: set[str],
        *,
        inert: bool = False,
        module_level: bool = True,
    ) -> None:
        """Validate a restricted import-time statement block."""
        for index, node in enumerate(statements):
            if (
                index == 0
                and isinstance(node, ast.Expr)
                and isinstance(node.value, ast.Constant)
                and isinstance(node.value.value, str)
            ):
                continue
            if isinstance(node, ast.Expr):
                raise ValueError(f"Unsafe import-time expression in {path}")
            if isinstance(node, ast.Pass):
                continue
            if isinstance(node, ast.Import):
                if not node.names or not _absolute_import_resolves(node):
                    raise ValueError(f"Unsafe import-time import in {path}")
                names.update(bound_names(node))
                continue
            if isinstance(node, ast.ImportFrom):
                if node.module == "__future__":
                    if node.level or any(alias.name != "annotations" for alias in node.names):
                        raise ValueError(f"Unsafe future import in {path}")
                    continue
                if not node.level:
                    if not node.names or not _absolute_import_resolves(node):
                        raise ValueError(f"Unsafe import-time import in {path}")
                    names.update(bound_names(node))
                    continue
                if node.level != 1:
                    raise ValueError(f"Unsafe parent-relative import in {path}")
                imported_sections = [node.module] if node.module else [
                    alias.name for alias in node.names if alias.name != "*"
                ]
                for imported in imported_sections:
                    target = relative_target(imported)
                    if imported.split(".", 1)[0] in new_sections:
                        _validate_import_time_section(
                            target.read_text(encoding="utf-8"),
                            target,
                            package_dir,
                            new_sections,
                            seen,
                        )
                names.update(bound_names(node))
                continue
            if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef)):
                if not _safe_function_definition(
                    node,
                    frozenset(names),
                    postponed_annotations=postponed_annotations or inert,
                ):
                    raise ValueError(f"Unsafe import-time function definition in {path}")
                names.add(node.name)
                continue
            if isinstance(node, ast.ClassDef):
                raise ValueError(f"Unsafe import-time class definition in {path}")
            if isinstance(node, ast.If):
                if (
                    node.orelse
                    or not _safe_inert_initializer_condition(node.test)
                    or not _loaded_names(node.test) <= frozenset(names)
                ):
                    raise ValueError(f"Unsafe import-time conditional in {path}")
                validate_block(node.body, set(names), inert=True, module_level=False)
                continue
            if isinstance(node, (ast.Assign, ast.AnnAssign)):
                targets = list(node.targets) if isinstance(node, ast.Assign) else [node.target]
                if any(
                    not _literal_assignment_target_matches(target, node.value)
                    for target in targets
                ):
                    raise ValueError(f"Unsafe import-time assignment target in {path}")
                if any(
                    isinstance(target, ast.Name)
                    and target.id == "__all__"
                    and _is_public_globals_comprehension(node.value)
                    for target in targets
                ):
                    names.update(bound_names(node))
                    continue
                if node.value is None or not _literal_initializer_value(node.value):
                    raise ValueError(f"Non-literal import-time assignment in {path}")
                if isinstance(node, ast.AnnAssign):
                    if not _safe_annotation_expression(
                        node.annotation,
                        frozenset(names),
                        postponed_annotations=postponed_annotations or inert,
                    ):
                        raise ValueError(f"Unsafe import-time annotation in {path}")
                    if not (postponed_annotations or inert) and not _loaded_names(node.annotation) <= frozenset(names):
                        raise ValueError(f"Unbound import-time annotation in {path}")
                names.update(bound_names(node))
                continue
            raise ValueError(f"Unsafe import-time statement in {path}: {type(node).__name__}")

    validate_block(tree.body, available_names)

def _validate_compatibility_new_sections(
    base_root: Path,
    head_root: Path,
    base_surfaces: Mapping[str, tuple],
    head_surfaces: Mapping[str, tuple],
) -> None:
    """Validate every section newly imported by compatibility mode."""
    base_section_modules = _section_modules_for_root(base_root)
    head_section_modules = _section_modules_for_root(head_root)
    for relative in sorted(head_surfaces):
        head_facade = head_root / relative
        head_package = _section_package_for_facade(head_facade)
        if head_package is None:
            raise ValueError(f"Missing source section package for {head_facade}")
        head_sections = head_section_modules(head_package)
        head_initializer = head_package / "__init__.py"
        manifest_path = head_package / "sections.manifest"
        head_initializer_source = (
            head_initializer.read_text(encoding="utf-8")
            if head_initializer.is_file()
            else ""
        )
        generated_kind = (
            "manifest"
            if manifest_path.is_file() and not head_initializer.is_file()
            else _phase192_initializer_kind(head_initializer_source)
        )
        generated_package = generated_kind is not None
        base_package = _section_package_for_facade(base_root / relative)
        base_sections = (
            base_section_modules(base_package)
            if relative in base_surfaces and base_package is not None
            else ()
        )
        old_names = frozenset(path.stem for path in base_sections)
        new_names = frozenset(path.stem for path in head_sections) - old_names
        if relative not in base_surfaces:
            initializer = head_initializer if head_initializer.is_file() else manifest_path
            initializer_source = head_initializer_source
            if generated_package:
                initializer_tree = (
                    ast.parse("pass\n", filename=str(initializer))
                    if generated_kind == "manifest"
                    else _parse_source(initializer_source, initializer)
                )
                if generated_kind == "phase192":
                    _phase192_template_validate_initializer(initializer_tree, str(initializer))
                elif generated_kind not in {"manifest"}:
                    _phase138u_simple_initializer(initializer_source, str(initializer))
                section_names = frozenset(path.stem for path in head_sections)
                for section in head_sections:
                    _validate_generated_section(
                        section.read_text(encoding="utf-8"),
                        section,
                        head_package,
                        section_names,
                        require_all=generated_kind != "manifest",
                    )
            else:
                _validate_import_time_section(
                    initializer_source,
                    initializer,
                    head_package,
                    frozenset(path.stem for path in head_sections),
                )
        for section in head_sections:
            if section.stem not in new_names:
                continue
            if generated_package:
                _validate_generated_section(
                    section.read_text(encoding="utf-8"),
                    section,
                    head_package,
                    frozenset(path.stem for path in head_sections),
                    require_all=generated_kind != "manifest",
                )
                continue
            _validate_import_time_section(
                section.read_text(encoding="utf-8"),
                section,
                head_package,
                new_names,
            )

__all__ = ['_validate_generated_section', '_validate_import_time_section', '_validate_compatibility_new_sections']
