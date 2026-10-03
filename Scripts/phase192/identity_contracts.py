"""Deterministic identity surfaces used by the Phase192 split gates."""

from __future__ import annotations

import argparse
import ast
import csv
import hashlib
import importlib
import re
import sys
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
    if not normalized.endswith(".py"):
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


def check_hash_manifest(manifest: Path, root: Path) -> int:
    """Phase192 check hash manifest."""
    with manifest.open(encoding="utf-8", newline="") as handle:
        rows = list(csv.DictReader(handle, delimiter="\t"))
    for row in rows:
        path = root / row["path"]
        if not path.is_file() or hashlib.sha256(path.read_bytes()).hexdigest().upper() != row["sha256"]:
            print(f"GENERATED_HASH_DIFF {row['path']}")
            return 1
    print(f"GENERATED_HASH_EQUAL count={len(rows)}")
    return 0


def check_source_map(source_map: Path, root: Path) -> int:
    """Phase192 check source map."""
    with source_map.open(encoding="utf-8", newline="") as handle:
        reader = csv.DictReader(handle, delimiter="\t")
        fieldnames = reader.fieldnames or []
        rows = list(reader)
    required = {"old_path", "new_path", "original_hash", "moved_symbol"}
    if not required.issubset(fieldnames):
        print("SOURCE_MAP_SCHEMA_DIFF")
        return 1
    if not rows:
        print("SOURCE_MAP_EMPTY")
        return 1
    try:
        root_resolved = root.resolve(strict=True)
    except OSError:
        print("SOURCE_MAP_PATH_DIFF root")
        return 1
    for row in rows:
        old_name = (row.get("old_path") or "").strip()
        new_name = (row.get("new_path") or "").strip()
        original_hash = (row.get("original_hash") or "").strip()
        moved_symbol = (row.get("moved_symbol") or "").strip()
        if not old_name or not new_name or not original_hash or not moved_symbol:
            print("SOURCE_MAP_EMPTY_FIELD")
            return 1
        old_path = root / old_name
        new_path = root / new_name
        try:
            old_path = old_path.resolve(strict=True)
            new_path = new_path.resolve(strict=True)
        except OSError:
            print(f"SOURCE_MAP_PATH_DIFF {old_name} {new_name}")
            return 1
        if (not old_path.is_file() or not new_path.is_file()
                or not old_path.is_relative_to(root_resolved)
                or not new_path.is_relative_to(root_resolved)):
            print(f"SOURCE_MAP_PATH_DIFF {old_name} {new_name}")
            return 1
        if not re.fullmatch(r"[0-9a-fA-F]{64}", original_hash):
            print(f"SOURCE_MAP_HASH_DIFF {old_name}")
            return 1
        actual_hash = hashlib.sha256(old_path.read_bytes()).hexdigest()
        if actual_hash.casefold() != original_hash.casefold():
            print(f"SOURCE_MAP_HASH_DIFF {old_name}")
            return 1
        old_text = old_path.read_text(encoding="utf-8", errors="replace")
        target_text = new_path.read_text(encoding="utf-8", errors="replace")
        symbol_pattern = rf"(?<!\w){re.escape(moved_symbol)}(?!\w)"
        if not re.search(symbol_pattern, old_text) or not re.search(symbol_pattern, target_text):
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
    root = args.root.resolve()
    paths = [line.strip() for line in args.paths.read_text(encoding="utf-8").splitlines() if line.strip()]
    rows: list[list[str]] = []
    for path in paths:
        item = root / path
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
            rows.extend(csharp_test_rows(path, text) if path.endswith(".cs") else python_test_rows(path, text, root))
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
