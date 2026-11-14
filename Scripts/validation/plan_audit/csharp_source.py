"""C# source masking and lexical evidence helpers for Phase197."""

from __future__ import annotations

import re
from pathlib import Path

PREPROCESSOR_SYMBOLS = {"UNITY_EDITOR": False, "UNITY_2020_3_OR_NEWER": True}

TYPE_DECLARATION_PATTERN = re.compile(
    r"\b(?:class|struct|record|interface)\s+(?P<name>[A-Za-z_][A-Za-z0-9_]*)"
)

FIELD_DECLARATION_PATTERN = re.compile(
    r"\b(?P<access>private|protected|internal|public)\s+"
    r"(?P<modifiers>(?:(?:static|readonly|volatile)\s+)*)"
    r"(?!(?:const|event)\b)[A-Za-z_][A-Za-z0-9_:<>,.?()\[\]\s]*\s+"
    r"(?P<name>_?[A-Za-z][A-Za-z0-9_]*)\s*(?:=|;)"
)


def _mask_csharp_comments_and_strings(text: str) -> str:
    """Mask C# comments and string literals while preserving line positions."""
    chars = list(text)
    length = len(text)
    index = 0

    def mask(position: int) -> None:
        """Replace one non-newline source character with a space."""
        if chars[position] != "\n":
            chars[position] = " "

    while index < length:
        if text.startswith("//", index):
            mask(index)
            mask(index + 1)
            index += 2
            while index < length and text[index] != "\n":
                mask(index)
                index += 1
            continue
        if text.startswith("/*", index):
            mask(index)
            mask(index + 1)
            index += 2
            while index < length:
                if text.startswith("*/", index):
                    mask(index)
                    mask(index + 1)
                    index += 2
                    break
                mask(index)
                index += 1
            continue
        if text.startswith("$@\"", index) or text.startswith("@$\"", index):
            mask(index)
            mask(index + 1)
            mask(index + 2)
            index += 3
            while index < length:
                if text.startswith("\"\"", index):
                    mask(index)
                    mask(index + 1)
                    index += 2
                    continue
                if text[index] == "\"":
                    mask(index)
                    index += 1
                    break
                mask(index)
                index += 1
            continue
        if text.startswith("@\"", index):
            mask(index)
            mask(index + 1)
            index += 2
            while index < length:
                if text.startswith("\"\"", index):
                    mask(index)
                    mask(index + 1)
                    index += 2
                    continue
                if text[index] == "\"":
                    mask(index)
                    index += 1
                    break
                mask(index)
                index += 1
            continue
        if text.startswith("$\"", index):
            mask(index)
            mask(index + 1)
            index += 2
            escaped = False
            while index < length:
                character = text[index]
                if character == "\n":
                    escaped = False
                    index += 1
                    continue
                mask(index)
                if escaped:
                    escaped = False
                elif character == "\\":
                    escaped = True
                elif character == "\"":
                    index += 1
                    break
                index += 1
            continue
        if text[index] in {"\"", "'"}:
            quote = text[index]
            mask(index)
            index += 1
            escaped = False
            while index < length:
                character = text[index]
                if character == "\n":
                    escaped = False
                    index += 1
                    continue
                mask(index)
                if escaped:
                    escaped = False
                elif character == "\\":
                    escaped = True
                elif character == quote:
                    index += 1
                    break
                index += 1
            continue
        index += 1
    return "".join(chars)


def _preprocessor_condition(expression: str, symbols: dict[str, bool] | None = None) -> bool | None:
    """Evaluate known audit symbols and reject unsupported conditions."""
    value = expression.strip().lower()
    if value in {"true", "1"}:
        return True
    if value in {"false", "0"}:
        return False
    return (symbols or PREPROCESSOR_SYMBOLS).get(value.upper())


def _mask_inactive_csharp_regions(masked_text: str, symbols: dict[str, bool] | None = None) -> str:
    """Mask explicitly inactive C# conditional-compilation branches."""
    chars = list(masked_text)
    frames: list[tuple[bool, bool, bool]] = []
    active = True
    offset = 0
    for line in masked_text.splitlines(keepends=True):
        directive = re.match(r"^\s*#\s*(if|elif|else|endif)\b(.*)$", line)
        if directive:
            kind, expression = directive.groups()
            if kind == "if":
                condition = _preprocessor_condition(expression, symbols)
                branch_active = active and condition is True
                frames.append((active, condition is True, condition is None))
                active = branch_active
            elif frames and kind in {"elif", "else"}:
                parent, taken, indeterminate = frames[-1]
                if indeterminate:
                    active = False
                elif kind == "elif":
                    condition = _preprocessor_condition(expression, symbols)
                    active = parent and not taken and condition is True
                    indeterminate = condition is None
                    taken = taken or condition is True
                else:
                    active = parent and not taken
                    taken = True
                frames[-1] = (parent, taken, indeterminate)
            elif frames:
                parent, _, _ = frames.pop()
                active = parent
            offset += len(line)
            continue
        if not active:
            for index in range(offset, offset + len(line)):
                if chars[index] != "\n":
                    chars[index] = " "
        offset += len(line)
    return "".join(chars)


def _mask_csharp_source(text: str, symbols: dict[str, bool] | None = None) -> str:
    """Mask comments, strings, and explicitly inactive conditional regions."""
    return _mask_inactive_csharp_regions(_mask_csharp_comments_and_strings(text), symbols)


def _type_spans(text: str, masked_text: str | None = None) -> dict[str, tuple[int, int]]:
    """Return source spans for nested C# declaring types."""
    masked = masked_text if masked_text is not None else _mask_csharp_source(text)
    openings: dict[int, str] = {}
    for match in TYPE_DECLARATION_PATTERN.finditer(masked):
        opening = masked.find("{", match.end())
        if opening >= 0:
            openings[opening] = match.group("name")
    stack: list[tuple[int, str | None, str | None]] = []
    spans: dict[str, tuple[int, int]] = {}
    for index, character in enumerate(masked):
        if character == "{":
            name = openings.get(index)
            parent = next((item[2] for item in reversed(stack) if item[2]), None)
            path = f"{parent}::{name}" if parent and name else name
            stack.append((index, name, path))
        elif character == "}" and stack:
            start, _, path = stack.pop()
            if path:
                spans[path] = (start, index + 1)
    return spans


def _line_refs(
    text: str,
    relative: Path,
    pattern: re.Pattern[str],
    masked_text: str | None = None,
    scopes: tuple[tuple[int, int], ...] | None = None,
) -> list[str]:
    """Return source locations matching a lifecycle evidence pattern."""
    masked = masked_text if masked_text is not None else _mask_csharp_source(text)
    refs: list[str] = []
    offset = 0
    for line_number, line in enumerate(masked.splitlines(keepends=True), 1):
        positions = [offset + match.start() for match in pattern.finditer(line)]
        if positions and (scopes is None or any(start <= position < end for position in positions for start, end in scopes)):
            refs.append(f"{relative.as_posix()}:{line_number}")
        offset += len(line)
    return refs


def _nested_receiver_names(masked_texts: dict[Path, str], declaring_type: str) -> tuple[str, ...]:
    """Infer receiver expressions whose static type matches a nested declaring type."""
    leaf = declaring_type.rsplit("::", 1)[-1]
    text = "\n".join(masked_texts.values())
    receivers = set(re.findall(rf"\b{re.escape(leaf)}\s+([A-Za-z_][A-Za-z0-9_]*)\b", text))
    receivers.update(re.findall(rf"\b([A-Za-z_][A-Za-z0-9_]*)\s*=\s*new\s+{re.escape(leaf)}\b", text))
    dictionary_value = bool(re.search(rf"\bDictionary\s*<[^;\n]*,\s*{re.escape(leaf)}\s*>", text))
    if dictionary_value:
        receivers.update(re.findall(r"\bout\s+var\s+([A-Za-z_][A-Za-z0-9_]*)\b", text))
        receivers.update(re.findall(r"\bforeach\s*\(\s*var\s+([A-Za-z_][A-Za-z0-9_]*)\s+in\s+[^)]*\.Values", text))
        receivers.update(f"{name}.Value" for name in re.findall(r"\bforeach\s*\(\s*var\s+([A-Za-z_][A-Za-z0-9_]*)\s+in\s+[^)]*\)", text))
    return tuple(sorted(receivers))
