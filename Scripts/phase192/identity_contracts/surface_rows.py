"""Deterministic identity surfaces used by the Phase192 split gates."""

from __future__ import annotations

import ast
import re
import unittest
from pathlib import Path

from Scripts.phase192 import source_layout
from .source_parsing import source_without_non_code

__all__ = [
    "TEST_ATTRIBUTES",
    "CS_METHOD",
    "CPP_TYPE_DECLARATION",
    "CPP_FUNCTION_DECLARATION",
    "CPP_FUNCTION_NAME",
    "CPP_CONTROL_NAMES",
    "clean",
    "managed_rows",
    "python_surface",
    "python_annotation_rows",
    "_module_name_for_path",
    "python_test_rows",
    "cpp_rows",
    "csharp_test_rows",
]

TEST_ATTRIBUTES = re.compile(r"\[(Fact|Theory)(?:[^]]*)\]")

CS_METHOD = re.compile(
    r"\b(?:public|private|protected|internal)\s+(?:static\s+)?"
    r"(?:async\s+)?[\w<>,.?\[\]]+\s+(\w+)\s*\("
)

CPP_TYPE_DECLARATION = re.compile(
    r"\b(?:namespace|class|struct|enum|union)\s+([A-Za-z_]\w*)"
)

CPP_FUNCTION_DECLARATION = re.compile(
    r"\b([A-Za-z_]\w*(?:::[A-Za-z_]\w*)?)\s*"
    r"\(([^{};]*)\)\s*(?:const\b\s*)?(?:\{|;)"
)

CPP_FUNCTION_NAME = re.compile(
    r"(?<![.\w])((?:[A-Za-z_~][\w~]*(?:::[A-Za-z_~][\w~]*)*)|"
    r"operator\s*[A-Za-z_~][\w~]*=?)\s*\("
)

CPP_CONTROL_NAMES = frozenset(
    {
        "if", "for", "while", "switch", "catch", "sizeof", "decltype",
        "void", "bool", "char", "short", "int", "long", "float", "double",
        "auto", "const", "volatile", "unsigned", "signed",
    }
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
    source_path = root / Path(path)
    suite = source_layout.load_test_suite(module_name, source_path, root)

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
    """Return stable C++ type and function declaration rows."""
    stripped = source_without_non_code(Path(path), text)
    lines = stripped.splitlines()
    rows: list[list[str]] = []
    brace_depth = 0
    function_body_depth: int | None = None
    pending_function_body = False

    def signature(start: int, name_start: int, name: str) -> tuple[str, bool] | None:
        """Return a declaration signature without its implementation body."""
        prefix_start = start
        while prefix_start > 0 and start - prefix_start < 3:
            previous = lines[prefix_start - 1]
            if any(token in previous for token in ("{", "}", ";")):
                break
            prefix_start -= 1
        combined = "\n".join(lines[prefix_start : min(len(lines), start + 32)])
        candidate_offset = sum(len(line) + 1 for line in lines[prefix_start:start])
        candidate_offset += name_start
        open_paren = combined.find("(", candidate_offset)
        if open_paren < 0:
            return None
        depth = 0
        close_paren = None
        for offset in range(open_paren, len(combined)):
            if combined[offset] == "(":
                depth += 1
            elif combined[offset] == ")":
                depth -= 1
                if depth == 0:
                    close_paren = offset
                    break
        if close_paren is None:
            return None
        end = close_paren + 1
        body_marker = end
        while body_marker < len(combined) and combined[body_marker] not in "{;":
            body_marker += 1
        declaration = combined[:body_marker]
        declaration = re.sub(r"\s+:\s*[^;{]*$", "", declaration)
        if name not in declaration:
            return None
        return clean(declaration), combined[body_marker:body_marker + 1] == "{"

    for number, line in enumerate(lines, 1):
        stripped = line.strip()
        if function_body_depth is not None:
            brace_depth += line.count("{") - line.count("}")
            if brace_depth < function_body_depth:
                function_body_depth = None
            continue
        if pending_function_body:
            if "{" in line:
                function_body_depth = brace_depth + 1
                pending_function_body = False
            brace_depth += line.count("{") - line.count("}")
            if function_body_depth is not None and brace_depth < function_body_depth:
                function_body_depth = None
            continue
        if not stripped or stripped.startswith(("//", "/*", "*", "#")):
            brace_depth += line.count("{") - line.count("}")
            continue
        match = CPP_TYPE_DECLARATION.search(stripped)
        if match:
            rows.append([path, str(number), match.group(1), clean(stripped)])
        for candidate in CPP_FUNCTION_NAME.finditer(line):
                name = candidate.group(1)
                prefix = line[: candidate.start(1)]
                if re.fullmatch(r"[A-Z][A-Z0-9_]*", name):
                    if name.startswith(("TEST", "SCENARIO")):
                        if "{" in line[candidate.start(1) :]:
                            function_body_depth = brace_depth + 1
                        else:
                            pending_function_body = True
                    continue
                context = "\n".join(lines[max(0, number - 4) : number])
                control_prefix = re.search(r"\b(?:if|for|while|switch|catch)\b", prefix)
                has_return_type = bool(
                    re.search(
                        r"\b(?:extern|static|inline|constexpr|__declspec|const|volatile|void|bool|char|short|int|long|float|double|auto|unsigned|signed|size_t|ssize_t|u?int\d*_t)\b",
                        prefix + context,
                    )
                    or re.fullmatch(r"\s*[A-Za-z_]\w*\s*[*&]?\s*", prefix) is not None
                )
                if (
                    not name.startswith("__")
                    and name.split("::")[-1] not in CPP_CONTROL_NAMES
                    and not any(token in prefix for token in (".", "->", "=", "return "))
                    and control_prefix is None
                    and (
                        "::" in name
                        or has_return_type
                        or name.startswith("operator")
                        or (brace_depth > 0 and line.rstrip().endswith((";", "{")))
                    )
                ):
                    parsed = signature(number - 1, candidate.start(1), name)
                    if parsed is not None:
                        declaration, has_body = parsed
                        rows.append([path, str(number), name.split("::")[-1], declaration])
                        if has_body:
                            if "{" in line[candidate.start(1) :]:
                                function_body_depth = brace_depth + 1
                            else:
                                pending_function_body = True
                        break
        brace_depth += line.count("{") - line.count("}")
        if function_body_depth is not None and brace_depth < function_body_depth:
            function_body_depth = None
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
