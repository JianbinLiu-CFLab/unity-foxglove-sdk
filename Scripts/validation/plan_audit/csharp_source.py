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

METHOD_DECLARATION_PATTERN = re.compile(
    r"(?m)^[ \t]*(?:(?:public|private|protected|internal|static|async|virtual|override|sealed|new)\s+)*"
    r"(?:[A-Za-z_][A-Za-z0-9_<>,.?]*|void)\s+[A-Za-z_][A-Za-z0-9_]*\s*\([^{};]*\)\s*(?=\{)"
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


def field_name_is_shadowed(masked_text: str, field: str, position: int) -> bool:
    """Return whether a parameter or local shadows a field in one method."""
    method_matches = list(METHOD_DECLARATION_PATTERN.finditer(masked_text, 0, position))
    method_start = method_matches[-1].start() if method_matches else 0
    method_text = masked_text[method_start:min(len(masked_text), position + len(field) + 32)]
    name = re.escape(field)
    parameter = re.compile(rf"\([^(){{}};]*\b(?:(?:ref|out|in)\s+[A-Za-z_][A-Za-z0-9_<>.,?]*|(?!ref\b|out\b|in\b)[A-Za-z_][A-Za-z0-9_<>.,?]*)\s+{name}\b[^(){{}};]*\)(?=\s*(?:\{{|=>|:))")
    if parameter.search(method_text):
        return True
    local = re.compile(rf"(?:^|[;{{}}])\s*(?:var|(?!ref\b|out\b|in\b|return\b|if\b|else\b|for\b|foreach\b|while\b|switch\b|case\b|throw\b|new\b|lock\b|using\b|yield\b|await\b|_) [A-Za-z_][A-Za-z0-9_<>.,?]*)\s+{name}\s*(?:=|;|,)", re.MULTILINE | re.VERBOSE)
    for match in local.finditer(method_text):
        prefix = method_text[max(0, match.start() - 256):match.start()]
        if not re.search(r"\b(?:private|protected|internal|public)\b[^;{}]*$", prefix):
            return True
    return False


def qualified_receiver_is_bound(
    masked_text: str,
    position: int,
    receiver: str,
    receiver_type: str | None,
    type_context: str | None = None,
) -> bool:
    """Return whether a qualified receiver has the expected local static type."""
    if not receiver_type:
        return True
    base_receiver = receiver.split(".", 1)[0]
    method_matches = list(METHOD_DECLARATION_PATTERN.finditer(masked_text, 0, position))
    method_start = method_matches[-1].start() if method_matches else 0
    prefix = masked_text[method_start:position]
    typed = [
        match
        for match in re.finditer(rf"\b([A-Za-z_][A-Za-z0-9_.<>]*)\s+{re.escape(base_receiver)}\b", prefix)
        if match.group(1) not in {"var", "out", "ref", "in"}
    ]
    if typed:
        return typed[-1].group(1).rsplit(".", 1)[-1] == receiver_type
    dictionary_names = set(
        re.findall(
            rf"\b([A-Za-z_][A-Za-z0-9_]*)\s*\.\s*TryGetValue\([^;{{}}]*\bout\s+var\s+{re.escape(base_receiver)}\b",
            prefix,
            re.DOTALL,
        )
    )
    dictionary_names.update(
        re.findall(
            rf"\bforeach\s*\(\s*var\s+{re.escape(base_receiver)}\s+in\s+([A-Za-z_][A-Za-z0-9_]*)",
            prefix,
        )
    )
    declaration_text = type_context or masked_text
    return bool(
        any(
            re.search(
                rf"\bDictionary\s*<[^;\n,]+,\s*{re.escape(receiver_type)}\s*>\s+{re.escape(name)}\b",
                declaration_text,
            )
            for name in dictionary_names
        )
        or re.search(rf"\b{re.escape(base_receiver)}\s*=\s*new\s+{re.escape(receiver_type)}\b", prefix)
    )


def receiver_from_match(masked_text: str, start: int, end: int, receiver_pattern: str, field: str) -> str | None:
    """Extract a qualified receiver from one writer match."""
    match = re.search(rf"({receiver_pattern})\.{re.escape(field)}\b", masked_text[start:end])
    return match.group(1) if match else None


def _nested_receiver_names(masked_texts: dict[Path, str], declaring_type: str) -> tuple[str, ...]:
    """Infer receiver expressions whose static type matches a nested declaring type."""
    leaf = declaring_type.rsplit("::", 1)[-1]
    text = "\n".join(masked_texts.values())
    receivers = set(re.findall(rf"\b{re.escape(leaf)}\s+([A-Za-z_][A-Za-z0-9_]*)\b", text))
    receivers.update(re.findall(rf"\b([A-Za-z_][A-Za-z0-9_]*)\s*=\s*new\s+{re.escape(leaf)}\b", text))
    dictionary_names = re.findall(
        rf"\bDictionary\s*<[^;\n,]+,\s*{re.escape(leaf)}\s*>\s+([A-Za-z_][A-Za-z0-9_]*)\b",
        text,
    )
    for dictionary_name in dictionary_names:
        receivers.update(
            re.findall(
                rf"\b{re.escape(dictionary_name)}\s*\.\s*TryGetValue\([^;{{}}]*\bout\s+var\s+([A-Za-z_][A-Za-z0-9_]*)\b",
                text,
                re.DOTALL,
            )
        )
        receivers.update(
            re.findall(
                rf"\bforeach\s*\(\s*var\s+([A-Za-z_][A-Za-z0-9_]*)\s+in\s+{re.escape(dictionary_name)}(?:\.Values)?\b",
                text,
            )
        )
        receivers.update(
            f"{name}.Value"
            for name in re.findall(
                rf"\bforeach\s*\(\s*var\s+([A-Za-z_][A-Za-z0-9_]*)\s+in\s+{re.escape(dictionary_name)}\b",
                text,
            )
        )
    return tuple(sorted(receivers))


def owner_type_scopes(text: str, masked_text: str, owner: str, support_names: tuple[str, ...]) -> tuple[tuple[int, int], ...]:
    """Return lexical spans belonging to an owner and its approved support types."""
    support_types = {Path(path).stem for path in support_names}
    allowed = {owner, *support_types}
    spans = _type_spans(text, masked_text)
    scopes: list[tuple[int, int]] = []
    for name, (start, end) in spans.items():
        if name not in allowed:
            continue
        cursor = start
        nested = sorted((nested_start, nested_end) for nested_name, (nested_start, nested_end) in spans.items() if nested_name not in allowed and start < nested_start and nested_end <= end)
        for nested_start, nested_end in nested:
            if cursor < nested_start:
                scopes.append((cursor, nested_start))
            cursor = max(cursor, nested_end)
        if cursor < end:
            scopes.append((cursor, end))
    return tuple(scopes)


def _method_contexts(masked_text: str) -> list[tuple[int, int, str]]:
    """Return method body spans and names from masked C# source."""
    pattern = re.compile(
        r"(?m)^[ \t]*(?:(?:public|private|protected|internal|static|async|virtual|override|sealed|new)\s+)*"
        r"(?:[A-Za-z_][A-Za-z0-9_<>,.?]*|void)\s+(?P<name>[A-Za-z_][A-Za-z0-9_]*)\s*\([^{};]*\)\s*(?=\{)"
    )
    contexts: list[tuple[int, int, str]] = []
    for match in pattern.finditer(masked_text):
        opening = masked_text.find("{", match.end())
        if opening < 0:
            continue
        depth = 0
        closing = len(masked_text)
        for index in range(opening, len(masked_text)):
            if masked_text[index] == "{":
                depth += 1
            elif masked_text[index] == "}":
                depth -= 1
                if depth == 0:
                    closing = index + 1
                    break
        contexts.append((match.start(), closing, match.group("name")))
    return contexts


def _method_body_writes_field(body: str, field_names: set[str]) -> bool:
    """Return whether a method body changes one of the declared fields."""
    for field in field_names:
        escaped = re.escape(field)
        if re.search(
            rf"(?:\b{escaped}\b\s*(?:(?<![=!<>])=(?!=)|\+=|-=|\?\?=|\+\+|--)|(?:\+\+|--)\s*\b{escaped}\b|\bref\s+\b{escaped}\b)",
            body,
        ):
            return True
    return False


def method_body_contains_field(masked_text: str, position: int, field_names: set[str]) -> bool:
    """Return whether a lifecycle method body references an owner field."""
    opening = masked_text.find("{", position)
    arrow = masked_text.find("=>", position, opening if opening >= 0 else len(masked_text))
    if arrow >= 0:
        body = masked_text[arrow + 2:masked_text.find(";", arrow) if ";" in masked_text[arrow:] else len(masked_text)]
    elif opening >= 0:
        depth = 0
        closing = len(masked_text)
        for index in range(opening, len(masked_text)):
            if masked_text[index] == "{":
                depth += 1
            elif masked_text[index] == "}":
                depth -= 1
                if depth == 0:
                    closing = index
                    break
        body = masked_text[opening:closing]
    else:
        return False
    return any(re.search(rf"\b{re.escape(field)}\b", body) for field in field_names)


def bound_lifecycle_refs(
    text: str,
    relative: Path,
    masked_text: str,
    scopes: tuple[tuple[int, int], ...],
    field_names: set[str],
    lifetime_pattern: re.Pattern[str],
) -> tuple[list[str], list[str]]:
    """Return lock and lifecycle references bound to declared owner fields."""
    lock_pattern = re.compile(r"\block\s*\(\s*(?:this\.)?(?P<name>[A-Za-z_][A-Za-z0-9_]*)")
    locks: set[str] = set()
    lifetime: set[str] = set()
    contexts = _method_contexts(masked_text)
    lifecycle_method_pattern = re.compile(
        r"(?:OnEnable|OnDisable|OnDestroy|StartServer|StopServer|CleanupStartupAfterFailure|Dispose|Reset|Connect|Disconnect|Disable)"
    )
    offset = 0
    for line_number, line in enumerate(masked_text.splitlines(keepends=True), 1):
        matches = list(lifetime_pattern.finditer(line))
        lock_match = lock_pattern.search(line)
        lock_position = offset + lock_match.start() if lock_match else None
        lock_context = next(
            (
                (start, end, name)
                for start, end, name in contexts
                if lock_position is not None and start <= lock_position < end
            ),
            None,
        )
        if (
            lock_match
            and lock_match.group("name") in field_names
            and lock_context is not None
            and any(start <= lock_position < end for start, end in scopes)
            and _method_body_writes_field(masked_text[lock_context[0]:lock_context[1]], field_names)
        ):
            locks.add(f"{relative.as_posix()}:{line_number}")
        for match in matches:
            position = offset + match.start()
            context = next((item for item in contexts if item[0] <= position < item[1]), None)
            if context is None or not any(start <= position < end for start, end in scopes):
                continue
            if not lifecycle_method_pattern.search(context[2]):
                continue
            if method_body_contains_field(masked_text, context[0], field_names):
                lifetime.add(f"{relative.as_posix()}:{line_number}")
        offset += len(line)
    return sorted(locks), sorted(lifetime)


def owner_source_is_canonical(
    path: Path,
    owner: str,
    masked_text: str,
    path_prefixes: dict[str, str],
    namespaces: dict[str, str],
    support_names: dict[str, tuple[str, ...]],
) -> bool:
    """Return whether a source file can contribute to the canonical owner."""
    normalized = path.as_posix()
    if not normalized.startswith(path_prefixes[owner]):
        return False
    if not re.search(rf"\bnamespace\s+{re.escape(namespaces[owner])}\b", masked_text):
        return False
    type_names = {owner, *(Path(name).stem for name in support_names.get(owner, ()))}
    return any(
        re.search(rf"\b(?:class|struct|record|interface)\s+{re.escape(type_name)}\b", masked_text)
        for type_name in type_names
    )
