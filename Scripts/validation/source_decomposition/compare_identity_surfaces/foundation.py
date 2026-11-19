"""Compare split Python identity surfaces between two Git revisions."""
from __future__ import annotations
_REVIEWED_IDENTITY_TOOLING_SECTIONS = frozenset({"aggregate", "foundation", "surface_rows_tests", "split_surface_tests", "split_contract_tests", "compatibility_surface_tests", "compatibility_initializer_tests", "loader_isolation_tests", "loader_entrypoint_tests", "loader_mutation_tests", "namespace_risk_tests", "namespace_execution_core_tests", "namespace_execution_edges_tests"})
def _reviewed_identity_tooling_test_split(relative: str, sections: tuple[Path, ...]) -> bool:
    """Recognize only the exact reviewed identity-tooling decomposition."""
    return (
        _canonical_source_decomposition_relative(relative)
        == "Scripts/validation/source_decomposition/regression_checks/test_identity_tooling.py"
        and {section.stem for section in sections} == _REVIEWED_IDENTITY_TOOLING_SECTIONS
        and len(sections) == len(_REVIEWED_IDENTITY_TOOLING_SECTIONS)
    )
import argparse, ast, copy, hashlib, importlib, importlib.util, json, keyword, operator, re
from contextlib import contextmanager
from functools import lru_cache
from pathlib import Path
import subprocess, tarfile, tempfile
from typing import Iterator, Mapping
REPOSITORY_ROOT = Path(__file__).resolve().parents[4]
try:
    _TRY_TYPES = (ast.Try, ast.TryStar)
except AttributeError:
    _TRY_TYPES = (ast.Try,)
try:
    _TYPE_ALIAS_TYPE = ast.TypeAlias
except AttributeError:
    _TYPE_ALIAS_TYPE = None
_SECTION_NAME_RE = re.compile(r"[A-Za-z_][A-Za-z0-9_]*\Z")
SPLIT_ROOTS, _LEGACY_SOURCE_DECOMPOSITION_PREFIX = (Path("Scripts"),), "Scripts/phase192/"
SOURCE_DECOMPOSITION_PREFIX = "Scripts/validation/source_decomposition/"
IDENTITY_WAIVER_PATH = Path(SOURCE_DECOMPOSITION_PREFIX + "identity_waivers.json")
def _legacy_relative_path(relative: str) -> str | None:
    """Return the pre-validation path for a migrated source-decomposition file."""
    if relative.startswith(SOURCE_DECOMPOSITION_PREFIX):
        return _LEGACY_SOURCE_DECOMPOSITION_PREFIX + relative[len(SOURCE_DECOMPOSITION_PREFIX):]
    return None
def _canonical_source_decomposition_relative(relative: str) -> str:
    """Normalize a source-decomposition path to the validation layout."""
    if relative.startswith(_LEGACY_SOURCE_DECOMPOSITION_PREFIX):
        return SOURCE_DECOMPOSITION_PREFIX + relative[len(_LEGACY_SOURCE_DECOMPOSITION_PREFIX):]
    return relative
def _current_source_decomposition_relative(relative: str) -> str:
    """Return the current validation path for a legacy relative path."""
    if relative.startswith(_LEGACY_SOURCE_DECOMPOSITION_PREFIX):
        return SOURCE_DECOMPOSITION_PREFIX + relative[len(_LEGACY_SOURCE_DECOMPOSITION_PREFIX):]
    return relative
def _resolve_source_decomposition_path(root: Path, relative: str) -> Path:
    """Resolve a source-decomposition path in current or legacy layouts."""
    path = root / relative
    if path.is_file() or path.is_dir():
        return path
    legacy = _legacy_relative_path(relative)
    return root / legacy if legacy is not None else path
def _load_identity_waivers(root: Path) -> dict[str, frozenset[str]]:
    """Load the reviewed public-symbol removal waivers for one revision."""
    path = _resolve_source_decomposition_path(root, IDENTITY_WAIVER_PATH.as_posix())
    if not path.is_file():
        return {}
    try:
        payload = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        raise ValueError(f"Invalid identity waiver file: {path}") from exc
    if not isinstance(payload, dict) or payload.get("version") != 1:
        raise ValueError(f"Invalid identity waiver schema: {path}")
    entries = payload.get("waived_symbols", {})
    if not isinstance(entries, dict):
        raise ValueError(f"Invalid identity waiver entries: {path}")
    waivers: dict[str, frozenset[str]] = {}
    for relative, names in entries.items():
        relative_path = Path(relative) if isinstance(relative, str) else None
        if (not isinstance(relative, str) or not relative.endswith(".py") or "\\" in relative or ":" in relative
                or relative_path.is_absolute() or ".." in relative_path.parts):
            raise ValueError(f"Invalid identity waiver path: {relative!r}")
        if not isinstance(names, list) or any(not isinstance(name, str) or not name or name.startswith("_") for name in names):
            raise ValueError(f"Invalid identity waiver symbols: {relative}")
        waivers[relative] = frozenset(names)
    return waivers
def _git(repository: Path, *args: str) -> str:
    """Run Git and return standard output."""
    try:
        completed = subprocess.run(["git", *args], cwd=repository, text=True, stdout=subprocess.PIPE,
                                   stderr=subprocess.PIPE, check=False, timeout=60)
    except subprocess.TimeoutExpired as exc:
        raise RuntimeError(f"git command timed out: {' '.join(args)}") from exc
    if completed.returncode != 0:
        raise RuntimeError(completed.stderr.strip() or "git command failed")
    return completed.stdout
def _extract_archive_safely(archive: tarfile.TarFile, destination: Path) -> None:
    """Internal helper for _extract_archive_safely."""
    root = destination.resolve()
    for member in archive.getmembers():
        target = (destination / member.name).resolve()
        if not target.is_relative_to(root):
            raise RuntimeError(f"git archive member escapes checkout: {member.name}")
        if member.issym() or member.islnk():
            raise RuntimeError(f"git archive member is a link: {member.name}")
        if member.isdir():
            target.mkdir(parents=True, exist_ok=True)
            continue
        if not member.isfile():
            raise RuntimeError(f"unsupported git archive member: {member.name}")
        target.parent.mkdir(parents=True, exist_ok=True)
        source = archive.extractfile(member)
        if source is None:
            raise RuntimeError(f"git archive member cannot be read: {member.name}")
        with source, target.open("wb") as output:
            output.write(source.read())
@contextmanager
def _revision_checkout(repository: Path, revision: str) -> Iterator[Path]:
    """Extract a revision without registering a persistent Git worktree."""
    scratch_root = Path(repository).resolve() / "build" / "identity-gate"
    scratch_root.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="phase192-identity-", dir=scratch_root) as temporary:
        checkout = Path(temporary) / "tree"
        checkout.mkdir()
        archive_path = Path(temporary) / "revision.tar"
        try:
            with archive_path.open("wb") as archive:
                archive_paths = [str(path) for path in SPLIT_ROOTS]
                completed = subprocess.run(
                    ["git", "archive", "--format=tar", revision, "--", *archive_paths],
                    cwd=repository,
                    stdout=archive,
                    stderr=subprocess.PIPE,
                    text=False,
                    check=False,
                    timeout=60,
                )
        except subprocess.TimeoutExpired as exc:
            raise RuntimeError(f"git archive timed out for {revision}") from exc
        if completed.returncode != 0:
            detail = completed.stderr.decode(errors="replace").strip()
            raise RuntimeError(detail or f"git archive failed for {revision}")
        with tarfile.open(archive_path, mode="r") as archive:
            _extract_archive_safely(archive, checkout)
        yield checkout
_FUNCTION_BODY_RISK_NAMES = frozenset({"__import__", "compile", "eval", "exec", "globals", "locals", "vars"})
_ENVIRONMENT_MUTATORS = frozenset({"clear", "pop", "popitem", "setdefault", "update"})
_ENVIRONMENT_FUNCTIONS = frozenset({"putenv", "unsetenv"})
def _without_function_bodies(source: str) -> str:
    """Return source whose function bodies are replaced by pass, keeping import-time statements."""
    tree = ast.parse(source)
    for node in ast.walk(tree):
        if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef)):
            node.body = [ast.Pass()]
    return ast.unparse(ast.fix_missing_locations(tree)) + "\n"
from .environment_risk import _function_body_and_environment_risk_signatures
def _parse_source(source: str, path: str | Path) -> ast.Module:
    """Compile and parse source so syntax-only identity checks match Python imports."""
    compile(source, str(path), "exec")
    return ast.parse(source, filename=str(path))
def _section_modules_for_root(root: Path):
    """Return a non-executing parser for the revision's package initializers."""
    del root
    def section_modules(package_dir: Path) -> tuple[Path, ...]:
        """Internal helper for section_modules."""
        manifest_path = package_dir / "sections.manifest"
        if manifest_path.is_file():
            names = tuple(
                line.strip()
                for line in manifest_path.read_text(encoding="utf-8").splitlines()
                if line.strip() and not line.lstrip().startswith("#")
            )
            if not names or len(names) != len(set(names)):
                raise ValueError(f"Invalid source section manifest: {manifest_path}")
            if any(
                not name.endswith(".py")
                or "/" in name
                or "\\" in name
                or re.fullmatch(r"[A-Za-z0-9_]+(?:\.[A-Za-z0-9_]+)*", Path(name).stem) is None
                for name in names
            ):
                raise ValueError(f"Invalid source section manifest: {manifest_path}")
            paths = tuple(package_dir / name for name in names)
            if any(not path.is_file() for path in paths):
                missing = [path.name for path in paths if not path.is_file()]
                raise FileNotFoundError(
                    f"Declared source sections are missing from {manifest_path}: {', '.join(missing)}"
                )
            actual = {
                path.name
                for path in package_dir.glob("*.py")
                if path.name not in {"__init__.py", "__main__.py"}
            }
            if actual != set(names):
                raise ValueError(
                    f"Source section manifest differs from package files: {manifest_path}; "
                    f"missing={sorted(set(names) - actual)} extra={sorted(actual - set(names))}"
                )
            return paths
        init_path = package_dir / "__init__.py"
        tree = _parse_source(init_path.read_text(encoding="utf-8"), init_path)
        modules: list[Path] = []
        seen: dict[str, Path] = {}
        for node in tree.body:
            if not isinstance(node, ast.ImportFrom) or node.level != 1:
                continue
            imported = [node.module] if node.module else [alias.name for alias in node.names]
            for module_name in imported:
                if module_name == "__init__":
                    continue
                if (
                    not module_name
                    or _SECTION_NAME_RE.fullmatch(module_name) is None
                    or keyword.iskeyword(module_name)
                ):
                    raise ValueError(f"Invalid declared source section: {module_name!r} in {init_path}")
                candidate = package_dir / f"{module_name}.py"
                if not candidate.is_file():
                    raise FileNotFoundError(
                        f"Declared source section is missing: {candidate}"
                    )
                key = module_name.casefold()
                if key in seen:
                    raise ValueError(
                        f"Duplicate declared source section (case-insensitive): {candidate}"
                    )
                seen[key] = candidate
                modules.append(candidate)
        if not modules:
            raise ValueError(f"No exported source sections found in {init_path}")
        return tuple(modules)
    return section_modules
def _section_package_for_facade(facade: Path) -> Path | None:
    """Return the standard or manifest-backed section directory for a facade."""
    package = facade.with_suffix("")
    if (package / "__init__.py").is_file():
        return package
    manifest_package = facade.parent / "Decomposed" / facade.stem
    if manifest_package.is_dir():
        manifest_path = manifest_package / "sections.manifest"
        if not manifest_path.is_file():
            raise ValueError(f"Missing source section manifest: {manifest_path}")
        return manifest_package
    return None
def _split_facades(root: Path) -> tuple[Path, ...]:
    """Return split facade paths under supported roots."""
    paths: list[Path] = []
    seen: dict[str, Path] = {}
    for relative_root in SPLIT_ROOTS:
        root_path = root / relative_root
        if not root_path.is_dir():
            continue
        for facade in sorted(root_path.rglob("*.py")):
            package = _section_package_for_facade(facade)
            if package is not None:
                key = facade.relative_to(root).as_posix().casefold()
                if key in seen:
                    raise ValueError(f"Duplicate facade path (case-insensitive): {facade}")
                seen[key] = facade
                paths.append(facade)
    return tuple(paths)
def _top_level_names(tree: ast.Module, *, include_imports: bool = True) -> set[str]:
    """Approximate a section's runtime globals without executing its code."""
    names: set[str] = set()
    def add_target(target: ast.AST) -> None:
        """Internal helper for add_target."""
        if isinstance(target, ast.Name):
            names.add(target.id)
        elif isinstance(target, (ast.Tuple, ast.List)):
            for item in target.elts:
                add_target(item)
    def visit(statements: list[ast.stmt]) -> None:
        """Internal helper for visit."""
        for node in statements:
            if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef, ast.ClassDef)):
                names.add(node.name)
            elif isinstance(node, ast.Import):
                if include_imports:
                    for alias in node.names:
                        names.add(alias.asname or alias.name.split(".", 1)[0])
            elif isinstance(node, ast.ImportFrom):
                if include_imports and node.module != "__future__":
                    for alias in node.names:
                        if alias.name != "*":
                            names.add(alias.asname or alias.name)
            elif isinstance(node, (ast.Assign, ast.AnnAssign, ast.AugAssign)):
                targets = node.targets if isinstance(node, ast.Assign) else [node.target]
                for target in targets:
                    add_target(target)
            elif isinstance(node, (ast.For, ast.AsyncFor)):
                add_target(node.target)
                visit(node.body)
                visit(node.orelse)
            elif isinstance(node, (ast.With, ast.AsyncWith)):
                for item in node.items:
                    if item.optional_vars is not None:
                        add_target(item.optional_vars)
                visit(node.body)
            elif isinstance(node, ast.If):
                visit(node.body)
                visit(node.orelse)
            elif isinstance(node, ast.While):
                visit(node.body)
                visit(node.orelse)
            elif isinstance(node, _TRY_TYPES):
                visit(node.body)
                for handler in node.handlers:
                    # ``except ... as name`` is cleared by Python after the handler.
                    visit(handler.body)
                visit(node.orelse)
                visit(node.finalbody)
            elif isinstance(node, ast.Match):
                for case in node.cases:
                    visit(case.body)
            elif isinstance(node, ast.Delete):
                def discard_target(target: ast.AST) -> None:
                    """Internal helper for discard_target."""
                    if isinstance(target, ast.Name):
                        names.discard(target.id)
                    elif isinstance(target, (ast.Tuple, ast.List)):
                        for item in target.elts:
                            discard_target(item)
                for target in node.targets:
                    discard_target(target)
    visit(tree.body)
    return names
def _direct_import_bound_names(tree: ast.Module) -> frozenset[str]:
    """Return names bound by top-level import statements."""
    names: set[str] = set()
    for node in tree.body:
        if isinstance(node, ast.Import):
            names.update(alias.asname or alias.name.split(".", 1)[0] for alias in node.names)
        elif isinstance(node, ast.ImportFrom):
            names.update(alias.asname or alias.name for alias in node.names if alias.name != "*")
    return frozenset(names)
def _declared_public_names(tree: ast.Module) -> frozenset[str]:
    """Internal helper for _declared_public_names."""
    return frozenset(
        name for name in _top_level_names(tree, include_imports=True)
        if not name.startswith("_")
    )
_PHASE192_TEMPLATE_IMPORTS = frozenset({"pathlib", "sys", "types", "unittest"})
_PHASE192_TEMPLATE_CALLS = frozenset({
    "add", "callable", "delattr", "getattr", "globals", "hasattr", "isinstance", "issubclass", "main", "print", "set",
    "setattr", "sorted", "str", "super", "tuple", "SystemExit",
})
_PHASE192_TEMPLATE_METHODS = frozenset({
    "__class__", "__delattr__", "__dict__", "__setattr__", "add", "get", "insert", "main", "pop", "resolve", "startswith",
})
def _facade_entrypoint_is_standard(tree: ast.Module, relative: str) -> bool:
    """Return whether a facade bootstraps the repository before forwarding execution."""
    body = list(tree.body)
    if body and isinstance(body[0], ast.Expr) and isinstance(body[0].value, ast.Constant):
        if not isinstance(body[0].value.value, str):
            return False
        body.pop(0)
    repository_parent_index = len(Path(relative).parts) - 1
    package_module = ".".join(Path(relative).with_suffix("").parts)
    expected = ast.parse(
        "from pathlib import Path as _Phase192Path\n"
        "import sys as _phase192_sys\n"
        f"_PHASE192_REPOSITORY_ROOT = _Phase192Path(__file__).resolve().parents[{repository_parent_index}]\n"
        "if str(_PHASE192_REPOSITORY_ROOT) not in _phase192_sys.path:\n"
        "    _phase192_sys.path.insert(0, str(_PHASE192_REPOSITORY_ROOT))\n"
        f"from {package_module}.entrypoint import run_entrypoint\n"
        "if __name__ == '__main__':\n"
        "    raise SystemExit(run_entrypoint())\n"
    )
    return ast.dump(ast.Module(body=body, type_ignores=[]), include_attributes=False) == ast.dump(
        expected,
        include_attributes=False,
    )
def _single_run_entrypoint_binding(tree: ast.Module) -> bool:
    """Return whether run_entrypoint has exactly one synchronous function binding."""
    bindings: list[ast.AST] = []
    for node in tree.body:
        if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef, ast.ClassDef)):
            if node.name == "run_entrypoint":
                bindings.append(node)
        elif isinstance(node, (ast.Import, ast.ImportFrom)):
            for alias in node.names:
                if (alias.asname or alias.name.split(".", 1)[0]) == "run_entrypoint":
                    bindings.append(node)
        elif isinstance(node, (ast.Assign, ast.AnnAssign, ast.AugAssign)):
            targets = node.targets if isinstance(node, ast.Assign) else [node.target]
            if any(isinstance(target, ast.Name) and target.id == "run_entrypoint" for target in targets):
                bindings.append(node)
    return (
        len(bindings) == 1
        and isinstance(bindings[0], ast.FunctionDef)
        and bindings[0].name == "run_entrypoint"
    )
def _approved_test_facade_removal(
    relative: str,
    base_root: Path,
    head_root: Path,
) -> bool:
    """Return whether removing a test facade preserves every package section."""
    facade_name = Path(relative).name
    if not (
        relative.startswith("Scripts/")
        and facade_name.startswith("test")
        and facade_name.endswith(".py")
    ):
        return False
    base_package = base_root / relative.removesuffix(".py")
    head_package = head_root / relative.removesuffix(".py")
    if not head_package.is_dir():
        migrated_relative = _current_source_decomposition_relative(relative)
        head_package = head_root / migrated_relative.removesuffix(".py")
    if not base_package.is_dir() or not head_package.is_dir():
        return False
    from .entrypoint_contracts import _approved_initializer_contract
    head_relative = relative
    if not (head_root / relative.removesuffix(".py")).is_dir():
        head_relative = _current_source_decomposition_relative(relative)
    if not _approved_initializer_contract(
        head_root,
        head_relative,
        base_root=base_root,
        base_relative=relative,
    ):
        return False
    section_modules = _section_modules_for_root(base_root)
    base_declared_paths = section_modules(base_package)
    head_declared_paths = section_modules(head_package)
    base_declared = {path.name for path in base_declared_paths}
    head_declared = {path.name for path in head_declared_paths}
    if (
        base_declared != head_declared
        or tuple(path.name for path in base_declared_paths)
        != tuple(path.name for path in head_declared_paths)
    ):
        return False
    base_manifest = base_package / "sections.manifest"
    head_manifest = head_package / "sections.manifest"
    if base_manifest.is_file() != head_manifest.is_file():
        return False
    if base_manifest.is_file() and base_manifest.read_bytes() != head_manifest.read_bytes():
        return False
    base_main = base_package / "__main__.py"
    head_main = head_package / "__main__.py"
    if base_main.is_file() != head_main.is_file():
        return False
    if base_main.is_file() and base_main.read_bytes() != head_main.read_bytes():
        return False
    def package_files(package: Path) -> dict[str, Path]:
        """Return every tracked package file except the generated root initializer."""
        return {
            path.relative_to(package).as_posix(): path
            for path in package.rglob("*")
            if path.is_file() and not (path == package / "__init__.py")
        }
    base_files = package_files(base_package)
    head_files = package_files(head_package)
    if set(base_files) != set(head_files):
        return False
    return all(
        base_files[name].read_bytes() == head_files[name].read_bytes()
        for name in base_files
    )
def _initializer_exact_identity_signature(
    root: Path,
    relative: str,
    *,
    allow_legacy_hook_removal: bool = False,
) -> tuple[str, ...] | None:
    """Return the exact initializer template with section names normalized."""
    path = root / relative.removesuffix(".py") / "__init__.py"
    package = path.parent
    if (
        not package.is_dir()
        or package.is_symlink()
        or not path.is_file()
        or path.is_symlink()
    ):
        return None
    try:
        sections = _section_modules_for_root(root)(package)
        tree = _parse_source(path.read_text(encoding="utf-8"), path)
    except (OSError, SyntaxError, ValueError, FileNotFoundError):
        return None
    section_names = tuple(section.stem for section in sections)
    section_aliases: dict[str, str] = {}
    section_imports: list[ast.ImportFrom] = []
    section_modules: ast.Assign | None = None
    for node in tree.body:
        if (
            isinstance(node, ast.ImportFrom)
            and node.level == 1
            and node.module is None
        ):
            if not node.names or any(
                alias.name == "*"
                or alias.name not in section_names
                or not alias.asname
                for alias in node.names
            ):
                return None
            section_imports.append(node)
            for alias in node.names:
                if alias.name in section_aliases:
                    return None
                section_aliases[alias.name] = alias.asname
            continue
        if isinstance(node, ast.Assign) and any(
            isinstance(target, ast.Name) and target.id == "_PHASE192_SECTION_MODULES"
            for target in node.targets
        ):
            if section_modules is not None or not isinstance(node.value, (ast.Tuple, ast.List)):
                return None
            section_modules = node
    if tuple(alias.name for node in section_imports for alias in node.names) != section_names:
        return None
    section_pairs = tuple(
        (alias.name, alias.asname or "")
        for node in section_imports
        for alias in node.names
    )
    if len({alias for _, alias in section_pairs}) != len(section_pairs):
        return None
    if section_modules is None:
        return None
    if any(not isinstance(item, ast.Name) for item in section_modules.value.elts):
        return None
    module_aliases = tuple(item.id for item in section_modules.value.elts)
    if (
        len(module_aliases) != len(section_names)
        or len(set(module_aliases)) != len(module_aliases)
        or set(module_aliases) != set(section_aliases.values())
    ):
        return None
    def is_legacy_hook_node(node: ast.AST) -> bool:
        """Return whether a top-level node belongs to the trusted discovery hook."""
        if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef)):
            return node.name == "load_tests"
        return isinstance(node, ast.Assign) and any(
            isinstance(target, ast.Name) and target.id == "_phase192_load_tests_used"
            for target in node.targets
        )
    body = list(tree.body)
    if allow_legacy_hook_removal:
        while body and is_legacy_hook_node(body[-1]):
            body.pop()
        if any(is_legacy_hook_node(node) for node in body):
            return None
    elif any(is_legacy_hook_node(node) for node in body):
        return None
    signature: list[str] = []
    imports_marker_added = False
    imports_closed = False
    modules_marker_added = False
    for node in body:
        if node in section_imports:
            if imports_closed:
                return None
            if not imports_marker_added:
                signature.append("SECTION_IMPORTS")
                signature.append(json.dumps(section_pairs, separators=(",", ":")))
                imports_marker_added = True
            continue
        if node is section_modules:
            if modules_marker_added:
                return None
            signature.append("SECTION_MODULES")
            signature.append(json.dumps(module_aliases, separators=(",", ":")))
            modules_marker_added = True
            continue
        if imports_marker_added:
            imports_closed = True
        signature.append(ast.dump(node, include_attributes=False))
    if not imports_marker_added or not modules_marker_added:
        return None
    return tuple(signature)

def _initializer_signatures_compatible(
    base_signature: tuple[str, ...],
    head_signature: tuple[str, ...],
) -> bool:
    """Return whether exact initializer wiring preserves the trusted order."""
    def split_signature(signature: tuple[str, ...]) -> tuple[
        tuple[str, ...], tuple[tuple[str, str], ...], tuple[str, ...]
    ] | None:
        """Split exact initializer skeleton and ordered section wiring."""
        skeleton: list[str] = []
        imports: tuple[tuple[str, str], ...] | None = None
        modules: tuple[str, ...] | None = None
        index = 0
        while index < len(signature):
            token = signature[index]
            if token not in {"SECTION_IMPORTS", "SECTION_MODULES"}:
                skeleton.append(token)
                index += 1
                continue
            if index + 1 >= len(signature):
                return None
            try:
                value = json.loads(signature[index + 1])
            except (TypeError, ValueError, json.JSONDecodeError):
                return None
            if token == "SECTION_IMPORTS":
                if imports is not None or not isinstance(value, list):
                    return None
                if any(
                    not isinstance(item, list)
                    or len(item) != 2
                    or any(not isinstance(part, str) or not part for part in item)
                    for item in value
                ):
                    return None
                imports = tuple((item[0], item[1]) for item in value)
            else:
                if modules is not None or not isinstance(value, list) or any(
                    not isinstance(item, str) or not item for item in value
                ):
                    return None
                modules = tuple(value)
            index += 2
        if imports is None or modules is None:
            return None
        return tuple(skeleton), imports, modules

    def preserves_order(
        baseline: tuple[object, ...], candidate: tuple[object, ...]
    ) -> bool:
        """Return whether baseline section wiring remains an ordered subsequence."""
        cursor = 0
        for item in baseline:
            try:
                cursor = candidate.index(item, cursor) + 1
            except ValueError:
                return False
        return True

    base_parts = split_signature(base_signature)
    head_parts = split_signature(head_signature)
    return (
        base_parts is not None
        and head_parts is not None
        and base_parts[0] == head_parts[0]
        and preserves_order(base_parts[1], head_parts[1])
        and preserves_order(base_parts[2], head_parts[2])
    )


def _approved_namespace_change(
    relative: str,
    base_root: Path | None,
    head_root: Path | None,
    *,
    head_relative: str | None = None,
) -> bool:
    """Allow namespace-risk changes only when wrapper and initializer contracts pass."""
    if base_root is None or head_root is None:
        return False
    head_relative = head_relative or relative
    from .entrypoint_contracts import (
        _approved_entrypoint_contract,
        _approved_entrypoint_section,
        _approved_initializer_contract,
    )

    if not _approved_entrypoint_contract(
        head_root,
        head_relative,
        base_root=base_root,
        base_relative=relative,
    ):
        return False
    if not _approved_initializer_contract(
        head_root,
        head_relative,
        base_root=base_root,
        base_relative=relative,
    ):
        return False
    facade = head_root / head_relative
    try:
        facade_tree = _parse_source(facade.read_text(encoding="utf-8"), facade)
    except (OSError, SyntaxError, ValueError):
        return False
    if not _facade_entrypoint_is_standard(facade_tree, head_relative):
        return False
    base_package = base_root / relative.removesuffix(".py")
    head_package = head_root / head_relative.removesuffix(".py")
    base_sections = {
        path.name
        for path in base_package.glob("*.py")
        if path.name not in {"__init__.py", "__main__.py"}
    }
    head_sections = {
        path.name
        for path in head_package.glob("*.py")
        if path.name not in {"__init__.py", "__main__.py"}
    }

    changed_existing = {
        name
        for name in base_sections & head_sections
        if (base_package / name).read_bytes() != (head_package / name).read_bytes()
    }
    added_sections = head_sections - base_sections
    if changed_existing - {"entrypoint.py"} or added_sections - {"entrypoint.py"}:
        return False
    return True


def _discovery_contract_errors(
    base_root: Path,
    head_root: Path,
    *,
    allow_directory_migration: bool = False,
) -> list[str]:
    """Reject newly retained duplicate test facades and validate intentional removals."""
    def test_facades(root: Path) -> dict[str, tuple[str, ...]]:
        """Return canonical test facade paths and every raw path that maps to them."""
        grouped: dict[str, list[str]] = {}
        for path in root.joinpath("Scripts").rglob("test*.py"):
            if path.with_suffix("").is_dir():
                raw = path.relative_to(root).as_posix()
                grouped.setdefault(raw, []).append(raw)
        return {relative: tuple(sorted(paths)) for relative, paths in grouped.items()}

    def normalize(relative: str) -> str:
        """Normalize supported source-decomposition migration paths."""
        if allow_directory_migration:
            return _current_source_decomposition_relative(relative)
        return relative

    def normalize_facades(raw_facades: dict[str, tuple[str, ...]]) -> dict[str, tuple[str, ...]]:
        """Normalize raw facade paths only during the supported directory migration."""
        grouped: dict[str, list[str]] = {}
        for relative, paths in raw_facades.items():
            grouped.setdefault(normalize(relative), []).extend(paths)
        return {relative: tuple(sorted(paths)) for relative, paths in grouped.items()}

    base_facades = normalize_facades(test_facades(base_root))
    head_facades = normalize_facades(test_facades(head_root))
    from .entrypoint_contracts import _approved_initializer_contract

    errors: list[str] = []
    for relative, paths in sorted(head_facades.items()):
        if len(paths) > 1:
            errors.append(f"DUPLICATE_DISCOVERY_FACADE {relative}: {','.join(paths)}")
    for relative in sorted(set(head_facades) - set(base_facades)):
        errors.append(f"DUPLICATE_DISCOVERY_FACADE {relative}")
    for relative in sorted(set(base_facades) - set(head_facades)):
        head_relative = relative
        base_relative = relative
        if allow_directory_migration and not (base_root / relative.removesuffix(".py")).is_dir():
            base_relative = _legacy_relative_path(relative) or relative
        if allow_directory_migration and not (head_root / relative.removesuffix(".py")).is_dir():
            head_relative = _current_source_decomposition_relative(relative)
        if not _approved_initializer_contract(
            head_root,
            head_relative,
            base_root=base_root,
            base_relative=base_relative,
        ):
            errors.append(f"INVALID_DISCOVERY_FACADE_REMOVAL {relative}")
    return errors



__all__ = ['_REVIEWED_IDENTITY_TOOLING_SECTIONS', '_reviewed_identity_tooling_test_split', 'argparse', 'ast', 'copy', 'hashlib', 'importlib', 'json', 'keyword', 're', 'contextmanager', 'lru_cache', 'Path', 'subprocess', 'tarfile', 'tempfile', 'Iterator', 'Mapping', 'REPOSITORY_ROOT', '_TRY_TYPES', '_TYPE_ALIAS_TYPE', '_SECTION_NAME_RE', 'SPLIT_ROOTS', 'IDENTITY_WAIVER_PATH', '_legacy_relative_path', '_canonical_source_decomposition_relative', '_current_source_decomposition_relative', '_resolve_source_decomposition_path', '_load_identity_waivers', '_git', '_extract_archive_safely', '_revision_checkout', '_FUNCTION_BODY_RISK_NAMES', '_ENVIRONMENT_MUTATORS', '_without_function_bodies', '_function_body_and_environment_risk_signatures', '_parse_source', '_section_modules_for_root', '_section_package_for_facade', '_split_facades', '_top_level_names', '_direct_import_bound_names', '_declared_public_names', '_PHASE192_TEMPLATE_IMPORTS', '_PHASE192_TEMPLATE_CALLS', '_PHASE192_TEMPLATE_METHODS', '_facade_entrypoint_is_standard', '_single_run_entrypoint_binding', '_approved_test_facade_removal', '_initializer_exact_identity_signature', '_initializer_signatures_compatible', '_approved_namespace_change', '_discovery_contract_errors']
