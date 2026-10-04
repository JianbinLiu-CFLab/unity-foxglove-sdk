"""Deterministic identity surfaces used by the Phase192 split gates."""

from __future__ import annotations

import argparse
import ast
import csv
import hashlib
import importlib
import io
import posixpath
import re
import sys
import tokenize
import unicodedata
import unittest
from pathlib import Path


SOURCE_EXTENSIONS = {".cs", ".py", ".cpp", ".cc", ".c", ".h", ".hpp", ".ts"}
TEST_ATTRIBUTES = re.compile(r"\[(Fact|Theory)(?:[^]]*)\]")
CS_METHOD = re.compile(
    r"\b(?:public|private|protected|internal)\s+(?:static\s+)?"
    r"(?:async\s+)?[\w<>,.?\[\]]+\s+(\w+)\s*\("
)
CPP_DECLARATION = re.compile(
    r"\b(?:namespace|class|struct|enum|union)\s+([A-Za-z_]\w*)|"
    r"\b([A-Za-z_]\w*)\s*\([^;{}]*\)\s*(?:const\s*)?(?:\{|$)"
)
SOURCE_NON_CODE = re.compile(
    r"//[^\r\n]*|/\*.*?\*/|\"(?:\\.|[^\"\\])*\"|'(?:\\.|[^'\\])*'",
    re.DOTALL,
)

SOURCE_SYMBOL = re.compile(r"^[A-Za-z_]\w*(?:(?:\.|::)[A-Za-z_]\w*)*$")
SOURCE_SYMBOL_PYTHON = re.compile(r"^[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*$")
_WINDOWS_RESERVED_DEVICE_NAMES = frozenset(
    {"con", "prn", "aux", "nul", *(f"com{index}" for index in range(1, 10)),
     *(f"lpt{index}" for index in range(1, 10)), "com¹", "com²", "com³", "lpt¹", "lpt²", "lpt³"}
)
_WINDOWS_FORBIDDEN_SEGMENT_CHARACTERS = frozenset(':*?"<>|')


def _is_safe_path_segment(segment: str) -> bool:
    """Return whether a path segment is stable on Windows and POSIX."""
    if not segment or segment in {".", ".."}:
        return False
    if segment[-1] in {".", " "}:
        return False
    if any(ord(character) < 32 for character in segment):
        return False
    if any(character in _WINDOWS_FORBIDDEN_SEGMENT_CHARACTERS for character in segment):
        return False
    device_name = segment.split(".", 1)[0].casefold()
    return device_name not in _WINDOWS_RESERVED_DEVICE_NAMES


def is_repository_relative(value: str) -> bool:
    """Return whether a path is a canonical repository-relative identity."""
    if not isinstance(value, str) or not value or "\0" in value:
        return False
    if (
        value.startswith(("/", "\\"))
        or re.match(r"^[A-Za-z]:", value)
        or "\\" in value
    ):
        return False
    normalized = unicodedata.normalize("NFC", value)
    if normalized != value or posixpath.normpath(value) != value:
        return False
    return all(_is_safe_path_segment(segment) for segment in value.split("/"))


def _strip_c_like_source(text: str) -> str:
    """Replace C-family comments and literals with whitespace."""
    output: list[str] = []
    index = 0
    length = len(text)
    block_depth = 0
    while index < length:
        if block_depth:
            if text.startswith("/*", index):
                block_depth += 1
                output.extend("  ")
                index += 2
            elif text.startswith("*/", index):
                block_depth -= 1
                output.extend("  ")
                index += 2
            else:
                output.append(text[index] if text[index] in "\r\n" else " ")
                index += 1
            continue
        if text.startswith("//", index):
            output.extend("  ")
            index += 2
            while index < length and text[index] not in "\r\n":
                output.append(" ")
                index += 1
            continue
        if text.startswith("/*", index):
            block_depth = 1
            output.extend("  ")
            index += 2
            continue
        if text[index] in {'"', "'"}:
            quote = text[index]
            if quote == '"':
                run = 1
                while index + run < length and text[index + run] == '"':
                    run += 1
                if run >= 3:
                    close = index + run
                    while close < length:
                        if text.startswith('"' * run, close):
                            close += run
                            break
                        close += 1
                    output.extend(
                        character if character in "\r\n" else " "
                        for character in text[index:close]
                    )
                    index = close
                    continue
            # C++ raw strings use R"delimiter(... )delimiter". Preserve no
            # literal contents while retaining line structure for diagnostics.
            if quote == '"' and index > 0 and text[index - 1] == "R":
                delimiter_end = text.find("(", index + 1)
                if delimiter_end >= 0 and delimiter_end - index <= 17:
                    delimiter = text[index + 1:delimiter_end]
                    terminator = ")" + delimiter + '"'
                    close = text.find(terminator, delimiter_end + 1)
                    end = length if close < 0 else close + len(terminator)
                    output.extend(
                        character if character in "\r\n" else " "
                        for character in text[index:end]
                    )
                    index = end
                    continue
            output.append(" ")
            index += 1
            escaped = False
            verbatim = quote == '"' and (
                (index > 0 and text[index - 1] == "@")
                or (index > 1 and text[index - 2:index] in {"@$", "$@"})
            )
            while index < length:
                character = text[index]
                if character in "\r\n":
                    output.append(character)
                    index += 1
                    if not verbatim:
                        break
                    continue
                output.append(" ")
                if verbatim and character == '"':
                    if index + 1 < length and text[index + 1] == '"':
                        output.append(" ")
                        index += 2
                        continue
                    index += 1
                    break
                if not verbatim and character == quote and not escaped:
                    index += 1
                    break
                if not verbatim:
                    if escaped:
                        escaped = False
                    elif character == "\\":
                        escaped = True
                index += 1
            continue
        output.append(text[index])
        index += 1
    return "".join(output)


def _strip_python_non_code(text: str) -> str | None:
    """Replace Python strings/comments while preserving f-string expressions."""
    try:
        tokens = tokenize.generate_tokens(io.StringIO(text).readline)
        ignored = {
            tokenize.COMMENT,
            tokenize.STRING,
            getattr(tokenize, "FSTRING_START", -1),
            getattr(tokenize, "FSTRING_MIDDLE", -1),
            getattr(tokenize, "FSTRING_END", -1),
        }
        return tokenize.untokenize(
            (token.type, " " if token.type in ignored else token.string)
            for token in tokens
        )
    except (IndentationError, tokenize.TokenError):
        return None


def source_without_non_code(path: Path, text: str) -> str | None:
    """Return source tokens suitable for moved-symbol checks."""
    if path.suffix.casefold() == ".py":
        return _strip_python_non_code(text)
    return _strip_c_like_source(text)



def _python_target_names(target: ast.AST) -> set[str]:
    """Return names bound by one Python target."""
    if isinstance(target, ast.Name):
        return {target.id}
    if isinstance(target, (ast.Tuple, ast.List)):
        names: set[str] = set()
        for item in target.elts:
            names.update(_python_target_names(item))
        return names
    return set()


def _python_declared_symbols(text: str) -> frozenset[str] | None:
    """Return qualified Python declarations, excluding comment/string text."""
    try:
        tree = ast.parse(text)
    except SyntaxError:
        return None
    symbols: set[str] = set()

    def visit_body(body: list[ast.stmt], prefix: str = "") -> None:
        for node in body:
            if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef, ast.ClassDef)):
                qualified = f"{prefix}.{node.name}" if prefix else node.name
                symbols.add(qualified)
                if isinstance(node, ast.ClassDef):
                    visit_body(node.body, qualified)
                continue
            elif isinstance(node, (ast.Assign, ast.AnnAssign, ast.AugAssign)):
                targets = node.targets if isinstance(node, (ast.Assign, ast.AugAssign)) else [node.target]
                for target in targets:
                    for name in _python_target_names(target):
                        symbols.add(f"{prefix}.{name}" if prefix else name)
            elif isinstance(node, (ast.For, ast.AsyncFor)):
                for name in _python_target_names(node.target):
                    symbols.add(f"{prefix}.{name}" if prefix else name)
            elif isinstance(node, (ast.With, ast.AsyncWith)):
                for item in node.items:
                    if item.optional_vars is not None:
                        for name in _python_target_names(item.optional_vars):
                            symbols.add(f"{prefix}.{name}" if prefix else name)

    visit_body(tree.body)
    return frozenset(symbols)


_CLIKE_DECLARATION_KEYWORDS = (
    "class", "struct", "interface", "enum", "namespace", "record", "union",
    "delegate", "function", "type",
)
_CLIKE_MODIFIERS = (
    "public", "private", "protected", "internal", "static", "const", "readonly",
    "virtual", "override", "abstract", "sealed", "async", "extern", "export",
    "default", "unsafe", "inline", "constexpr", "mutable", "volatile", "final",
)


def _clike_declares_symbol(text: str, symbol: str) -> bool:
    """Return whether a C-family source contains a declaration for ``symbol``."""
    leaf = re.split(r"(?:::|\.)", symbol)[-1]
    escaped = re.escape(leaf)
    # Namespace/type aliases are references, not moved declarations.
    if re.search(rf"(?m)^\s*using\s+{escaped}\s*=", text):
        return False
    if re.search(rf"(?m)^\s*typedef\b[^;]*\b{escaped}\s*(?:;|$)", text):
        return False
    text = re.sub(r"(?m)^\s*(?:using|typedef)\b[^;]*(?:;|$)", "", text)
    keyword_pattern = rf"\b(?:{'|'.join(_CLIKE_DECLARATION_KEYWORDS)})\s+{escaped}\b"
    if re.search(keyword_pattern, text):
        return True
    modifier = rf"(?:(?:{'|'.join(_CLIKE_MODIFIERS)})\s+)*"
    type_name = r"(?:[A-Za-z_]\w*(?:\s*<[^;{}()]*>)?|[A-Za-z_]\w*(?:::[A-Za-z_]\w*)*)"
    declaration_pattern = rf"(?m)^\s*{modifier}(?:{type_name}\s+)+{escaped}\s*(?:[=;{{:(]|$)"
    if re.search(declaration_pattern, text):
        return True
    function_pattern = rf"(?m)^\s*{modifier}{escaped}\s*\([^;{{}}]*\)\s*(?:const\s*)?(?:{{|$)"
    return re.search(function_pattern, text) is not None


def declares_source_symbol(path: Path, text: str, symbol: str) -> bool:
    """Check a moved symbol at a declaration site, not merely as a token."""
    if path.suffix.casefold() == ".py":
        declarations = _python_declared_symbols(text)
        if declarations is None:
            return False
        return symbol in declarations or any(
            declaration.endswith("." + symbol) for declaration in declarations
        )
    stripped = source_without_non_code(path, text)
    return stripped is not None and _clike_declares_symbol(stripped, symbol)


def clean(line: str) -> str:
    """Phase192 clean."""
    return re.sub(r"\s+", " ", line.strip())[:500]


def managed_rows(path: str, text: str) -> list[list[str]]:
    """Phase192 managed rows."""
    rows: list[list[str]] = []
    visibility = re.compile(r"\b(public|internal|protected)\b")
    for number, line in enumerate(text.splitlines(), 1):
        stripped = line.strip()
        if not stripped or stripped.startswith(("//", "/*", "*", "[")):
            continue
        match = visibility.search(stripped)
        if not match or not re.search(r"\b(class|struct|interface|enum|record|delegate)\b|[({;}]", stripped):
            continue
        rows.append([path, str(number), match.group(1), clean(stripped)])
    return rows


def python_surface(path: str, text: str) -> list[list[str]]:
    """Phase192 python surface."""
    tree = ast.parse(text, filename=path)
    all_value = ""
    exports: list[str] = []
    for node in tree.body:
        if isinstance(node, ast.Assign):
            for target in node.targets:
                if isinstance(target, ast.Name) and target.id == "__all__":
                    try:
                        all_value = repr(ast.literal_eval(node.value))
                    except Exception:
                        all_value = ast.unparse(node.value)
        if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef, ast.ClassDef)) and not node.name.startswith("_"):
            exports.append(node.name)
    return [[path, all_value, ",".join(sorted(set(exports)))]]


def python_annotation_rows(path: str, text: str) -> list[list[str]]:
    """Return stable rows for future-import and callable annotation semantics."""

    tree = ast.parse(text, filename=path)
    future_annotations = any(
        isinstance(node, ast.ImportFrom)
        and node.module == "__future__"
        and any(alias.name == "annotations" for alias in node.names)
        for node in tree.body
    )
    rows = [[path, "<module>", "future_annotations", str(future_annotations)]]
    callables = [
        node
        for node in ast.walk(tree)
        if isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef, ast.ClassDef))
    ]
    for node in sorted(callables, key=lambda item: (item.lineno, item.col_offset)):
        if isinstance(node, ast.ClassDef):
            detail = "bases=" + ",".join(ast.unparse(base) for base in node.bases)
            kind = "class"
        else:
            arguments = [
                *node.args.posonlyargs,
                *node.args.args,
                *node.args.kwonlyargs,
            ]
            if node.args.vararg is not None:
                arguments.append(node.args.vararg)
            if node.args.kwarg is not None:
                arguments.append(node.args.kwarg)
            details = [
                f"{argument.arg}:{ast.unparse(argument.annotation) if argument.annotation else ''}"
                for argument in arguments
            ]
            detail = "args=" + ",".join(details)
            detail += f";return={ast.unparse(node.returns) if node.returns else ''}"
            kind = "async-function" if isinstance(node, ast.AsyncFunctionDef) else "function"
        rows.append([path, node.name, kind, detail])
    return rows


def _flatten_test_suite(suite: unittest.TestSuite):
    """Yield every concrete test case from a unittest suite."""
    for item in suite:
        if isinstance(item, unittest.TestSuite):
            yield from _flatten_test_suite(item)
        else:
            yield item


def _module_name_for_path(path: str) -> str:
    """Convert a repository-relative Python path to its import name."""
    normalized = path.replace("\\", "/")
    if not normalized.casefold().endswith(".py"):
        raise ValueError(f"Python test path must end in .py: {path}")
    return normalized[:-3].replace("/", ".").rstrip(".")


def python_test_rows(
    path: str,
    text: str,
    root: Path | None = None,
) -> list[list[str]]:
    """Return fully-qualified tests discovered by unittest, not AST-looking names."""
    del text
    if root is None:
        raise ValueError("A repository root is required for unittest discovery")

    module_name = _module_name_for_path(path)
    prefixes = (module_name,)
    for loaded_name in tuple(sys.modules):
        if any(
            loaded_name == prefix or loaded_name.startswith(prefix + ".")
            for prefix in prefixes
        ):
            sys.modules.pop(loaded_name, None)
    importlib.invalidate_caches()

    root_text = str(root.resolve())
    inserted = root_text not in sys.path
    if inserted:
        sys.path.insert(0, root_text)
    try:
        suite = unittest.defaultTestLoader.loadTestsFromName(module_name)
    finally:
        if inserted:
            sys.path.remove(root_text)

    rows: list[list[str]] = []
    for case in _flatten_test_suite(suite):
        if case.__class__.__name__ == "_FailedTest":
            raise RuntimeError(
                f"unittest discovery failed for {module_name}: {case.id()}"
            )
        method_name = getattr(case, "_testMethodName", None)
        if not method_name:
            continue
        class_name = type(case).__name__
        rows.append([path, f"{module_name}.{class_name}.{method_name}", "unittest"] )
    return sorted(rows)


def cpp_rows(path: str, text: str) -> list[list[str]]:
    """Phase192 cpp rows."""
    rows: list[list[str]] = []
    for number, line in enumerate(text.splitlines(), 1):
        stripped = line.strip()
        if not stripped or stripped.startswith(("//", "/*", "*", "#")):
            continue
        match = CPP_DECLARATION.search(stripped)
        if match:
            rows.append([path, str(number), match.group(1) or match.group(2), clean(stripped)])
    return rows


def csharp_test_rows(path: str, text: str) -> list[list[str]]:
    """Phase192 csharp test rows."""
    rows: list[list[str]] = []
    lines = text.splitlines()
    for index, line in enumerate(lines):
        attribute = TEST_ATTRIBUTES.search(line)
        if not attribute:
            continue
        for look in range(index, min(index + 8, len(lines))):
            method = CS_METHOD.search(lines[look])
            if method:
                rows.append([path, method.group(1), attribute.group(1)])
                break
    return rows


def write_rows(path: Path, header: list[str], rows: list[list[str]]) -> None:
    """Phase192 write rows."""
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", newline="", encoding="utf-8") as handle:
        writer = csv.writer(handle, delimiter="\t")
        writer.writerow(header)
        writer.writerows(sorted(rows))


def compare_tsv(expected: Path, actual: Path) -> int:
    """Phase192 compare tsv."""
    expected_text = expected.read_text(encoding="utf-8")
    actual_text = actual.read_text(encoding="utf-8")
    if expected_text == actual_text:
        print("IDENTITY_EQUAL")
        return 0
    print("IDENTITY_DIFF")
    return 1


def _read_table(path: Path, expected_fields: frozenset[str]) -> tuple[list[str], list[dict[str, str]]] | None:
    """Read a strict TSV table, rejecting duplicate or extra columns."""
    with path.open(encoding="utf-8", newline="") as handle:
        reader = csv.DictReader(handle, delimiter="\t")
        fieldnames = reader.fieldnames
        if (
            fieldnames is None
            or len(fieldnames) != len(set(fieldnames))
            or set(fieldnames) != set(expected_fields)
        ):
            return None
        rows = list(reader)
    if any(None in row for row in rows):
        return None
    return fieldnames, rows


def _strict_field(row: dict[str, str], name: str) -> str | None:
    """Return a nonempty field without silently normalizing its identity."""
    value = row.get(name)
    if not isinstance(value, str) or not value or value != value.strip():
        return None
    return value


def _resolved_identity(path: Path) -> str:
    """Return a case-folded NFC path identity for duplicate detection."""
    return unicodedata.normalize("NFC", str(path)).casefold()


def check_hash_manifest(manifest: Path, root: Path) -> int:
    """Validate every nonempty, repository-relative generated-file hash row."""
    table = _read_table(manifest, frozenset({"path", "sha256"}))
    if table is None:
        print("GENERATED_HASH_SCHEMA_DIFF")
        return 1
    _, rows = table
    if not rows:
        print("GENERATED_HASH_EMPTY")
        return 1
    try:
        root_resolved = root.resolve(strict=True)
    except (OSError, RuntimeError):
        print("GENERATED_HASH_ROOT_DIFF")
        return 1
    seen: set[str] = set()
    resolved_seen: set[str] = set()
    for row in rows:
        relative = _strict_field(row, "path")
        expected = _strict_field(row, "sha256")
        relative = relative or ""
        expected = expected or ""
        relative_path = Path(relative)
        if (
            not is_repository_relative(relative)
            or not re.fullmatch(r"[0-9a-fA-F]{64}", expected)
        ):
            print(f"GENERATED_HASH_SCHEMA_DIFF {relative or '-'}")
            return 1
        try:
            path = (root / relative_path).resolve(strict=False)
        except (OSError, RuntimeError):
            print(f"GENERATED_HASH_PATH_DIFF {relative}")
            return 1
        path_key = unicodedata.normalize("NFC", relative).casefold()
        resolved_key = _resolved_identity(path)
        if (
            not path.is_relative_to(root_resolved)
            or path_key in seen
            or resolved_key in resolved_seen
        ):
            print(f"GENERATED_HASH_PATH_DIFF {relative}")
            return 1
        seen.add(path_key)
        resolved_seen.add(resolved_key)
        if not path.is_file() or hashlib.sha256(path.read_bytes()).hexdigest().upper() != expected.upper():
            print(f"GENERATED_HASH_DIFF {relative}")
            return 1
    print(f"GENERATED_HASH_EQUAL count={len(rows)}")
    return 0


def check_source_map(source_map: Path, root: Path) -> int:
    """Validate source-map paths, hashes, and code-level moved symbols."""
    table = _read_table(
        source_map,
        frozenset({"old_path", "new_path", "original_hash", "moved_symbol"}),
    )
    if table is None:
        print("SOURCE_MAP_SCHEMA_DIFF")
        return 1
    _, rows = table
    if not rows:
        print("SOURCE_MAP_EMPTY")
        return 1
    try:
        root_resolved = root.resolve(strict=True)
    except (OSError, RuntimeError):
        print("SOURCE_MAP_PATH_DIFF root")
        return 1
    seen_pairs: set[tuple[str, str]] = set()
    for row in rows:
        old_name = _strict_field(row, "old_path") or ""
        new_name = _strict_field(row, "new_path") or ""
        original_hash = _strict_field(row, "original_hash") or ""
        moved_symbol = _strict_field(row, "moved_symbol") or ""
        old_relative = Path(old_name)
        new_relative = Path(new_name)
        if (
            not old_name
            or not new_name
            or not is_repository_relative(old_name)
            or not is_repository_relative(new_name)
            or not original_hash
            or not moved_symbol
        ):
            print("SOURCE_MAP_EMPTY_FIELD")
            return 1
        lexical_pair = (
            unicodedata.normalize("NFC", old_name).casefold(),
            unicodedata.normalize("NFC", new_name).casefold(),
        )
        if lexical_pair in seen_pairs:
            print(f"SOURCE_MAP_PATH_DIFF {old_name} {new_name}")
            return 1
        seen_pairs.add(lexical_pair)
        old_path = root / old_relative
        new_path = root / new_relative
        try:
            old_path = old_path.resolve(strict=True)
            new_path = new_path.resolve(strict=True)
        except (OSError, RuntimeError):
            print(f"SOURCE_MAP_PATH_DIFF {old_name} {new_name}")
            return 1
        if old_path == new_path or _resolved_identity(old_path) == _resolved_identity(new_path):
            print(f"SOURCE_MAP_PATH_DIFF {old_name} {new_name}")
            return 1
        if (not old_path.is_file() or not new_path.is_file()
                or not old_path.is_relative_to(root_resolved)
                or not new_path.is_relative_to(root_resolved)):
            print(f"SOURCE_MAP_PATH_DIFF {old_name} {new_name}")
            return 1
        old_suffix = old_path.suffix.casefold()
        new_suffix = new_path.suffix.casefold()
        if (
            old_suffix not in SOURCE_EXTENSIONS
            or new_suffix not in SOURCE_EXTENSIONS
            or old_suffix != new_suffix
        ):
            print(f"SOURCE_MAP_PATH_DIFF {old_name} {new_name}")
            return 1
        if not re.fullmatch(r"[0-9a-fA-F]{64}", original_hash):
            print(f"SOURCE_MAP_HASH_DIFF {old_name}")
            return 1
        actual_hash = hashlib.sha256(old_path.read_bytes()).hexdigest()
        if actual_hash.casefold() != original_hash.casefold():
            print(f"SOURCE_MAP_HASH_DIFF {old_name}")
            return 1
        old_raw = old_path.read_text(encoding="utf-8", errors="replace")
        target_raw = new_path.read_text(encoding="utf-8", errors="replace")
        if source_without_non_code(old_path, old_raw) is None or source_without_non_code(new_path, target_raw) is None:
            print(f"SOURCE_MAP_SYNTAX_DIFF {old_name} {new_name}")
            return 1
        symbol_expression = (
            SOURCE_SYMBOL_PYTHON
            if old_path.suffix.casefold() == ".py"
            else SOURCE_SYMBOL
        )
        if not symbol_expression.fullmatch(moved_symbol):
            print(f"SOURCE_MAP_SYMBOL_DIFF {old_name} {new_name} {moved_symbol}")
            return 1
        if not declares_source_symbol(old_path, old_raw, moved_symbol) or not declares_source_symbol(new_path, target_raw, moved_symbol):
            print(f"SOURCE_MAP_SYMBOL_DIFF {old_name} {new_name} {moved_symbol}")
            return 1
    print(f"SOURCE_MAP_VALID rows={len(rows)}")
    return 0


def files_from_manifest(path: Path) -> list[str]:
    """Phase192 files from manifest."""
    with path.open(encoding="utf-8", newline="") as handle:
        reader = csv.DictReader(handle, delimiter="\t")
        return [row["path"] for row in reader]


def main(argv: list[str] | None = None) -> int:
    """Phase192 main."""
    parser = argparse.ArgumentParser()
    sub = parser.add_subparsers(dest="command", required=True)
    for name in ("managed", "python", "python-annotations", "cpp", "tests"):
        command = sub.add_parser(name)
        command.add_argument("--root", type=Path, default=Path("."))
        command.add_argument("--paths", type=Path, required=True)
        command.add_argument("--output", type=Path, required=True)
    compare = sub.add_parser("compare")
    compare.add_argument("--expected", type=Path, required=True)
    compare.add_argument("--actual", type=Path, required=True)
    generated = sub.add_parser("generated")
    generated.add_argument("--manifest", type=Path, required=True)
    generated.add_argument("--root", type=Path, default=Path("."))
    source_map = sub.add_parser("source-map")
    source_map.add_argument("--input", type=Path, required=True)
    source_map.add_argument("--root", type=Path, default=Path("."))
    args = parser.parse_args(argv)
    if args.command == "compare":
        return compare_tsv(args.expected, args.actual)
    if args.command == "generated":
        return check_hash_manifest(args.manifest, args.root)
    if args.command == "source-map":
        return check_source_map(args.input, args.root)
    try:
        root = args.root.resolve(strict=True)
    except (OSError, RuntimeError):
        print("IDENTITY_PATH_DIFF root")
        return 1
    paths: list[str] = []
    for line in args.paths.read_text(encoding="utf-8").splitlines():
        if not line.strip():
            continue
        if line != line.strip():
            print(f"IDENTITY_PATH_DIFF {line}")
            return 1
        paths.append(line)
    rows: list[list[str]] = []
    seen_paths: set[str] = set()
    for path in paths:
        if not is_repository_relative(path):
            print(f"IDENTITY_PATH_DIFF {path}")
            return 1
        path_key = unicodedata.normalize("NFC", path).casefold()
        if path_key in seen_paths:
            print(f"IDENTITY_PATH_DIFF {path}")
            return 1
        seen_paths.add(path_key)
        try:
            item = (root / path).resolve(strict=True)
        except (OSError, RuntimeError):
            print(f"IDENTITY_PATH_DIFF {path}")
            return 1
        if not item.is_relative_to(root) or not item.is_file():
            print(f"IDENTITY_PATH_DIFF {path}")
            return 1
        if args.command == "managed":
            rows.extend(managed_rows(path, item.read_text(encoding="utf-8", errors="replace")))
        elif args.command == "python":
            rows.extend(python_surface(path, item.read_text(encoding="utf-8", errors="replace")))
        elif args.command == "python-annotations":
            rows.extend(python_annotation_rows(path, item.read_text(encoding="utf-8", errors="replace")))
        elif args.command == "cpp":
            rows.extend(cpp_rows(path, item.read_text(encoding="utf-8", errors="replace")))
        else:
            text = item.read_text(encoding="utf-8", errors="replace")
            rows.extend(csharp_test_rows(path, text) if path.casefold().endswith(".cs") else python_test_rows(path, text, root))
    headers = {
        "managed": ["path", "line", "visibility", "declaration"],
        "python": ["path", "all", "exports"],
        "python-annotations": ["path", "symbol", "kind", "detail"],
        "cpp": ["path", "line", "symbol", "declaration"],
        "tests": ["path", "test_name", "framework"],
    }
    write_rows(args.output, headers[args.command], rows)
    print(f"IDENTITY_WRITTEN command={args.command} rows={len(rows)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
