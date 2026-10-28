"""Deterministic identity surfaces used by the Phase192 split gates."""

from __future__ import annotations

import ast
import io
import re
import tokenize
from pathlib import Path

__all__ = [
    "SOURCE_EXTENSIONS",
    "SOURCE_NON_CODE",
    "SOURCE_SYMBOL",
    "SOURCE_SYMBOL_PYTHON",
    "source_without_non_code",
    "declares_source_symbol",
]

SOURCE_EXTENSIONS = {".cs", ".py", ".cpp", ".cc", ".c", ".h", ".hpp", ".inl", ".ts"}

SOURCE_NON_CODE = re.compile(
    r"//[^\r\n]*|/\*.*?\*/|\"(?:\\.|[^\"\\])*\"|'(?:\\.|[^'\\])*'",
    re.DOTALL,
)

SOURCE_SYMBOL = re.compile(r"^[A-Za-z_]\w*(?:(?:\.|::)[A-Za-z_]\w*)*$")

SOURCE_SYMBOL_PYTHON = re.compile(r"^[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*$")

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
        """Collect declarations from one syntax-tree body."""
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
