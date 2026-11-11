"""Reader symbol and xUnit index helpers for source-text inventory."""

from __future__ import annotations

import hashlib
import re
from pathlib import Path

_SCOPED_SYMBOL_CACHE: dict[
    tuple[str, tuple[tuple[str, str], ...]], list[tuple[int, tuple[int, ...], str, str]]
] = {}


def _clear_symbol_caches() -> None:
    """Clear scope-bound symbol caches before scanning a tree."""
    _SCOPED_SYMBOL_CACHE.clear()

try:
    from . import source_text_inventory_text as _text_module
    from .source_text_inventory_text import *
except ImportError:
    import source_text_inventory_text as _text_module
    from source_text_inventory_text import *

globals().update(
    {
        _name: getattr(_text_module, _name)
        for _name in dir(_text_module)
        if _name.startswith("_") and not _name.startswith("__")
    }
)


def _split_arguments(text: str) -> list[str]:
    """Split a masked C# argument or parameter list at top-level commas."""
    parts: list[str] = []
    start = 0
    depth = 0
    for index, character in enumerate(text):
        if character in "(<[{":
            depth += 1
        elif character in ")>]}" and depth:
            depth -= 1
        elif character == "," and depth == 0:
            parts.append(text[start:index].strip())
            start = index + 1
    tail = text[start:].strip()
    if tail:
        parts.append(tail)
    return parts


def _normalize_type(value: str) -> str:
    """Normalize common C# aliases for lightweight overload matching."""
    normalized = re.sub(r"\s+", "", value).replace("global::", "").rstrip("?")
    aliases = {
        "System.String": "string",
        "System.Int32": "int",
        "System.Boolean": "bool",
        "System.Object": "object",
    }
    return aliases.get(normalized, normalized)


def _parameter_types(parameter_text: str) -> tuple[str, ...]:
    """Return normalized parameter types from a method declaration."""
    result: list[str] = []
    for parameter in _split_arguments(parameter_text):
        parameter = re.sub(r"\s*=.*$", "", parameter).strip()
        parameter = re.sub(r"\[[^\]]*\]\s*", "", parameter)
        tokens = parameter.split()
        tokens = [token for token in tokens if token not in {"this", "ref", "out", "in", "params"}]
        if len(tokens) >= 2:
            result.append(_normalize_type(" ".join(tokens[:-1])))
        elif tokens:
            result.append(_normalize_type(tokens[0]))
    return tuple(result)


def _method_parameter_types(masked: str, start: int, body_start: int) -> tuple[str, ...]:
    """Extract a method's parameter types from its declaration span."""
    open_index = masked.find("(", start, body_start)
    close_index = masked.rfind(")", start, body_start)
    if open_index < 0 or close_index < open_index:
        return ()
    return _parameter_types(masked[open_index + 1 : close_index])


def _method_return_type(signature: str, name: str) -> str:
    """Extract a simple declared return type from a method signature."""
    match = re.search(
        rf"(?P<return>[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)?(?:<[^>]+>)?(?:\[\])?)\s+"
        rf"{re.escape(name)}\s*\(",
        signature,
    )
    return _normalize_type(match.group("return")) if match else ""


def _constant_values(text: str, masked: str) -> dict[str, str]:
    """Collect simple boolean, integer, and string initializers for branch selectors."""
    values: dict[str, str] = {}
    for match in re.finditer(
        r"\b(?:(?:bool|int|string|var)\s+)?(?P<name>[A-Za-z_]\w*)\s*=\s*"
        r"(?P<value>true|false|-?\d+|\"(?:\\.|[^\"\\])*\")\s*;",
        masked,
    ):
        values[match.group("name")] = _normalize_csharp_expression(text[match.start("value") : match.end("value")])
    return values


def _argument_definition_index(
    text: str, masked: str, method_regions: list[tuple[int, int]]
) -> dict[int, list[tuple[int, str, str, str]]]:
    """Index caller-local definitions and writes once for subsequent dependency queries."""
    result: dict[int, list[tuple[int, str, str, str]]] = {begin: [] for begin, _ in method_regions}
    writes = re.compile(
        r"(?<![\w.])(?P<name>[A-Za-z_]\w*)\s*"
        r"(?:\?\?=|>>>?=|<<=|[-+*/%&|^]=|=(?!=|>)|\+\+|--)"
    )
    declarations = re.compile(
        r"\b(?:bool|byte|sbyte|short|ushort|int|uint|long|ulong|float|double|decimal|char|string|object)\s+"
        r"(?P<name>[A-Za-z_]\w*)\s*(?=;)"
    )
    matches = sorted([*writes.finditer(masked), *declarations.finditer(masked)], key=lambda match: match.start())
    for match in matches:
        owner = min(
            ((begin, end) for begin, end in method_regions if begin < match.start() < end),
            key=lambda region: region[1] - region[0],
            default=None,
        )
        if owner is None:
            continue
        begin, end = owner
        statement_end = masked.find(";", match.end(), end)
        if statement_end < 0:
            statement_end = end
        statement_start = max([begin] + [masked.rfind(delimiter, begin, match.start()) + 1 for delimiter in ";{}"])
        statement = _normalize_csharp_expression(text[statement_start : statement_end + 1])
        result[begin].append((match.start(), match.group("name"), statement, masked[match.end() : statement_end]))
    return result


def _byref_argument_definitions(
    text: str, masked: str, method_regions: list[tuple[int, int]]
) -> dict[int, list[tuple[int, str, str, str]]]:
    """Index ref/out writes with their complete invocation and containing method."""
    result: dict[int, list[tuple[int, str, str, str]]] = {}
    if not re.search(r"\b(?:ref|out)\b", masked):
        return result
    invocation = re.compile(
        r"(?<![\w.])(?:[A-Za-z_]\w*\s*(?:::|\.)\s*)*[A-Za-z_]\w*"
        r"(?:\s*<[^;{}()]+>)?\s*\("
    )
    byref = re.compile(
        r"(?:[A-Za-z_]\w*\s*:\s*)?(?:ref|out)\s+"
        r"(?:[\w.<>\[\]?]+\s+)?(?P<name>[A-Za-z_]\w*)"
    )
    for match in invocation.finditer(masked):
        open_index = match.end() - 1
        close_index = _balanced_call_end(masked, open_index)
        if close_index is None or re.match(r"\s*(?:\{|=>)", masked[close_index + 1 :]):
            continue
        targets = [
            target.group("name")
            for argument in _split_arguments(masked[open_index + 1 : close_index])
            if (target := byref.fullmatch(argument)) is not None
        ]
        if not targets:
            continue
        owner = min(
            ((begin, end) for begin, end in method_regions if begin < match.start() < end),
            key=lambda region: region[1] - region[0],
            default=None,
        )
        if owner is not None:
            statement = _normalize_csharp_expression(text[match.start() : close_index + 1])
            references = masked[match.start() : close_index + 1]
            result.setdefault(owner[0], []).extend(
                (match.start(), name, statement, references) for name in targets
            )
    return result


def _dependency_definitions(
    expression: str,
    offset: int,
    definitions: list[tuple[int, str, str, str]],
    contexts: dict[int, str],
) -> list[tuple[int, str, str, str]]:
    """Select preceding definitions reached through an expression and its control guards."""
    names = set(re.findall(r"\b[A-Za-z_]\w*\b", _strip_csharp_comments_and_strings(expression + " " + contexts.get(offset, ""))))
    preceding = [definition for definition in definitions if definition[0] < offset]
    previous_size = -1
    while previous_size != len(names):
        previous_size = len(names)
        for start, name, _, references in preceding:
            if name in names:
                dependency_text = references + " " + contexts.get(start, "")
                names.update(re.findall(r"\b[A-Za-z_]\w*\b", _strip_csharp_comments_and_strings(dependency_text)))
    return [definition for definition in preceding if definition[1] in names]


def _local_dependency_signature(
    expression: str,
    offset: int,
    definitions: list[tuple[int, str, str, str]],
    contexts: dict[int, str],
) -> str:
    """Hash all preceding definitions reached through an expression and its control guards."""
    selected = _dependency_definitions(expression, offset, definitions, contexts)
    pieces = [f"{statement}@{contexts.get(start, '')}" for start, _, statement, _ in selected]
    return hashlib.sha256("\n".join(pieces).encode()).hexdigest() if pieces else ""


def _source_type_regions(masked: str) -> list[tuple[int, int, str]]:
    """Resolve namespace and nested type identities without merging same-name classes."""
    namespaces: list[tuple[int, int, str]] = []
    for match in re.finditer(r"\bnamespace\s+([A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)\s*([;{])", masked):
        begin = match.end() - 1
        end = len(masked) if match.group(2) == ";" else _balanced_brace_end(masked, begin)
        if end is None:
            continue
        parents = [item for item in namespaces if item[0] < begin < item[1]]
        parent = min(parents, key=lambda item: item[1] - item[0], default=None)
        name = (parent[2] + "." if parent else "") + match.group(1)
        namespaces.append((begin, end, name))
    regions: list[tuple[int, int, str]] = []
    for match in re.finditer(r"\b(?:class|struct|record)\s+([A-Za-z_]\w*)[^;{}]*\{", masked):
        begin = match.end() - 1
        end = _balanced_brace_end(masked, begin)
        if end is None:
            continue
        parents = [item for item in regions if item[0] < begin < item[1]]
        parents += [item for item in namespaces if item[0] < begin < item[1]]
        parent = min(parents, key=lambda item: item[1] - item[0], default=None)
        name = (parent[2] + "." if parent else "") + match.group(1)
        regions.append((begin, end, name))
    return regions


def _embedded_statement_end(masked: str, start: int, ends: dict[int, int | None] | None = None) -> int | None:
    """Locate a complete embedded statement, including its nested dangling else branches."""
    ends = ends if ends is not None else {}
    cursor = start
    while cursor < len(masked) and masked[cursor].isspace():
        cursor += 1
    if cursor in ends:
        return ends[cursor]
    if cursor >= len(masked):
        return None
    end = None
    header = re.match(r"(?P<kind>if|for|foreach|while|switch|using|lock|fixed)\s*\(", masked[cursor:])
    if masked[cursor] == "{":
        end = _balanced_brace_end(masked, cursor)
    elif header:
        open_index = cursor + header.end() - 1
        close_index = _balanced_call_end(masked, open_index)
        if close_index is not None:
            end = _embedded_statement_end(masked, close_index + 1, ends)
            if header.group("kind") == "if" and end is not None:
                alternate = re.match(r"\s*else\b", masked[end + 1 :])
                if alternate:
                    end = _embedded_statement_end(masked, end + 1 + alternate.end(), ends)
    elif re.match(r"do\b", masked[cursor:]):
        body_end = _embedded_statement_end(masked, cursor + 2, ends)
        tail = re.match(r"\s*while\s*\(", masked[body_end + 1 :]) if body_end is not None else None
        if tail is not None:
            close_index = _balanced_call_end(masked, body_end + tail.end())
            if close_index is not None:
                semicolon = close_index + 1
                while semicolon < len(masked) and masked[semicolon].isspace():
                    semicolon += 1
                if masked[semicolon : semicolon + 1] == ";":
                    end = semicolon
    else:
        depth = 0
        for index in range(cursor, len(masked)):
            character = masked[index]
            if character in "([{":
                depth += 1
            elif character in ")]}":
                if depth == 0:
                    break
                depth -= 1
            elif character == ";" and depth == 0:
                end = index
                break
    ends[cursor] = end
    return end


def _conditional_branch_intervals(
    text: str, masked: str, constant_values: dict[str, str]
) -> list[tuple[int, int, str]]:
    """Bind each runtime branch to its adjacent predecessor and complete condition history."""
    intervals: list[tuple[int, int, str]] = []
    branches: list[tuple[int, int, tuple[str, ...]]] = []
    ends: dict[int, int | None] = {}
    pattern = re.compile(r"\b(?P<kind>else\s+if|if)\s*\(|\b(?P<alternate>else)\b")
    for match in pattern.finditer(masked):
        line_start = masked.rfind("\n", 0, match.start()) + 1
        if masked[line_start : match.start()].lstrip().startswith("#"):
            continue
        kind = _normalize_csharp_expression(match.group("kind") or "else")
        previous = max(
            (branch for branch in branches if branch[1] < match.start() and not masked[branch[1] + 1 : match.start()].strip()),
            key=lambda branch: branch[0],
            default=None,
        ) if kind != "if" else None
        history = previous[2] if previous else (() if kind == "if" else ("UNKNOWN",))
        prefix = " && ".join(f"!({condition})" for condition in history)
        body_start = match.end()
        if kind != "else":
            open_index = match.end() - 1
            close_index = _balanced_call_end(masked, open_index)
            if close_index is None:
                continue
            condition = _normalize_csharp_expression(text[open_index + 1 : close_index])
            if condition in constant_values:
                condition = f"{condition}={constant_values[condition]}"
            history += (condition,)
            condition = f"{prefix} && ({condition})" if prefix else condition
            descriptor = f"{kind}({condition})"
            body_start = close_index + 1
        else:
            descriptor = f"else({prefix})"
        while body_start < len(masked) and masked[body_start].isspace():
            body_start += 1
        end = _embedded_statement_end(masked, body_start, ends)
        if end is not None and end >= body_start:
            intervals.append((body_start, end, descriptor))
            if kind != "else":
                branches.append((match.start(), end, history))
    return intervals


def _execution_barriers(
    text: str, masked: str, method_regions: list[tuple[int, int]]
) -> tuple[list[tuple[int, int, int, str, str]], list[tuple[int, int]]]:
    """Locate direct awaits and process termination calls within their execution frames."""
    lambda_regions: list[tuple[int, int]] = []
    for match in re.finditer(
        r"(?:\([^;{}]*\)|[A-Za-z_]\w*)\s*=>|\bdelegate(?:\s*\([^)]*\))?\s*\{", masked
    ):
        cursor = match.end() - 1 if match.group().endswith("{") else match.end()
        while cursor < len(masked) and masked[cursor].isspace():
            cursor += 1
        if any(begin == cursor for begin, _ in method_regions):
            continue
        if masked[cursor : cursor + 1] == "{":
            end = _balanced_brace_end(masked, cursor)
        else:
            end = None
            depth = 0
            for index in range(cursor, len(masked)):
                character = masked[index]
                if character in "([{":
                    depth += 1
                elif character in ")]}":
                    if depth == 0:
                        end = index - 1
                        break
                    depth -= 1
                elif character in ",;" and depth == 0:
                    end = index - 1
                    break
        if end is not None and end >= cursor:
            lambda_regions.append((cursor, end))
    pattern = re.compile(
        r"(?<![@\w])(?P<await>await)\b|(?<![\w.:])(?:global\s*::\s*)?"
        r"(?:System\s*\.\s*)?Environment\s*\.\s*(?P<terminate>Exit|FailFast)\s*\("
    )
    barriers: list[tuple[int, int, int, str, str]] = []
    for match in pattern.finditer(masked):
        if any(begin <= match.start() <= end for begin, end in lambda_regions):
            continue
        owner = min(
            ((begin, end) for begin, end in method_regions if begin < match.start() < end),
            key=lambda region: region[1] - region[0],
            default=None,
        )
        if owner is None:
            continue
        kind = "await" if match.group("await") else "terminate"
        end = (
            _embedded_statement_end(masked, match.start())
            if kind == "await"
            else _balanced_call_end(masked, match.end() - 1)
        )
        if end is not None:
            expression = _normalize_csharp_expression(text[match.start() : end + 1])
            barriers.append((match.start(), end, owner[0], kind, expression))
    return barriers, lambda_regions


def _bind_execution_barriers(
    barriers: list[tuple[int, int, int, str, str]],
    lambda_regions: list[tuple[int, int]],
    contexts: dict[int, str],
    owners: dict[int, int | None],
    argument_definitions: dict[int, list[tuple[int, str, str, str]]],
) -> dict[int, str]:
    """Bind prior same-frame barriers and local dependencies to existing call contexts."""
    grouped: dict[int, list[tuple[int, str]]] = {}
    for begin, end, owner, kind, expression in barriers:
        dependency = _local_dependency_signature(expression, begin, argument_definitions.get(owner, []), contexts)
        descriptor = f"before:{kind}:{expression}@{contexts.get(begin, '')}:locals({dependency})"
        grouped.setdefault(owner, []).append((end, descriptor))
    result = dict(contexts)
    for position, context in contexts.items():
        if any(begin <= position <= end for begin, end in lambda_regions):
            continue
        preceding = [descriptor for end, descriptor in grouped.get(owners.get(position), []) if end < position]
        result[position] = "|".join(value for value in [context, *preceding] if value)
    return result


def _source_targets(lines: list[str], include_variables: bool = True) -> dict[str, str]:
    """Build a local symbol-to-source-path map from reader calls and path arrays."""
    paths: dict[str, str] = {}
    text = "\n".join(lines)
    for match in ARRAY_PATTERN.finditer(text):
        values = [
            _normalise_path(value) for value in PATH_LITERAL_PATTERN.findall(match.group("body"))
        ]
        if values:
            paths[match.group("name")] = ";".join(values)
    for match in CONSTANT_PATH_PATTERN.finditer(text):
        value = _normalise_path(match.group("path"))
        if "/" in value or "\\" in value:
            paths[match.group("name")] = value
    for match in STATIC_READONLY_PATH_PATTERN.finditer(text):
        value = _normalise_path(match.group("path"))
        if "/" in value or "\\" in value:
            paths[match.group("name")] = value
    if not include_variables:
        for _ in range(3):
            changed = False
            for match in re.finditer(
                r"(?:(?:const\s+string)|(?:static\s+readonly\s+string)|(?:readonly\s+static\s+string))\s+"
                r"(?P<name>[A-Za-z_]\w*)\s*=\s*(?P<expression>[^;]+)",
                text,
            ):
                expression = match.group("expression")
                targets: list[str] = []
                for name, base_value in paths.items():
                    if not re.search(rf"\b{re.escape(name)}\b", expression):
                        continue
                    suffix_match = re.search(
                        rf"\b{re.escape(name)}\s*\+\s*\"([^\"]*)\"", expression
                    )
                    if suffix_match:
                        suffix = suffix_match.group(1).replace("\\", "/")
                        targets.extend(
                            item.rstrip("/") + "/" + suffix.lstrip("/")
                            for item in base_value.split(";")
                        )
                    else:
                        targets.extend(base_value.split(";"))
                if targets and paths.get(match.group("name")) != ";".join(dict.fromkeys(targets)):
                    paths[match.group("name")] = ";".join(dict.fromkeys(targets))
                    changed = True
            if not changed:
                break
        return paths
    for _ in range(3):
        changed = False
        for match in VARIABLE_READER_PATTERN.finditer(text):
            target = _extract_argument_path(match.group("argument"), paths)
            target = target or "<unresolved>"
            if paths.get(match.group("name")) != target:
                paths[match.group("name")] = target
                changed = True
        for match in re.finditer(
            r"(?:var|string)\s+(?P<name>[A-Za-z_]\w*)\s*=\s*(?P<expression>[^;]+)", text
        ):
            expression = match.group("expression")
            expression_without_literals = re.sub(r'@?"(?:""|\\.|[^"\\])*"', '""', expression)
            referenced = re.findall(r"\b[A-Za-z_]\w*\b", expression_without_literals)
            targets = []
            for name in referenced:
                if name not in paths:
                    continue
                suffix_match = re.search(rf"\b{re.escape(name)}\s*\+\s*\"([^\"]*)\"", expression)
                if suffix_match:
                    suffix = suffix_match.group(1).replace("\\", "/")
                    targets.extend(
                        value.rstrip("/") + "/" + suffix.lstrip("/")
                        for value in paths[name].split(";")
                    )
                else:
                    targets.extend(paths[name].split(";"))
            target = ";".join(dict.fromkeys(";".join(targets).split(";"))) if targets else None
            if target and paths.get(match.group("name")) != target:
                paths[match.group("name")] = target
                changed = True
        if not changed:
            break
    return paths


def _scoped_source_symbols(
    lines: list[str], shared_paths: dict[str, str]
) -> list[tuple[int, tuple[int, ...], str, str]]:
    """Collect reader symbols with method and block scope boundaries."""
    text = "\n".join(lines)
    cache_key = (text, tuple(sorted(shared_paths.items())))
    cached = _SCOPED_SYMBOL_CACHE.get(cache_key)
    if cached is not None:
        return cached
    scopes = _brace_paths(lines)
    symbols: list[tuple[int, tuple[int, ...], str, str]] = [
        (-1, (), name, target) for name, target in shared_paths.items()
    ]
    masked_text = _strip_csharp_comments_and_strings(text)
    line_starts = _line_starts(text)
    local_paths = dict(shared_paths)
    definitions: list[tuple[int, tuple[int, ...], str, str]] = []
    reader_assignment_starts: set[tuple[int, str, int]] = set()
    for call in _call_spans(lines, READER_CALL_PATTERN, text, masked_text):
        start = int(call["start"])
        line = int(call["start_line"])
        prefix = text[max(0, start - 256) : start]
        assignment = re.search(r"(?:var|string)\s+(?P<name>[A-Za-z_]\w*)\s*=\s*$", prefix)
        if not assignment:
            continue
        assignment_start = start - len(prefix) + assignment.start("name")
        target = _reader_target(call, local_paths) or "<unresolved>"
        definition = (line, scopes[line], assignment.group("name"), target)
        definitions.append(definition)
        local_paths[assignment.group("name")] = target
        reader_assignment_starts.add((line, assignment.group("name"), assignment_start))
    foreach_pattern = r"foreach\s*\(\s*var\s+(?P<name>[A-Za-z_]\w*)\s+in\s+(?P<expression>[^)]*)\)"
    for match in re.finditer(foreach_pattern, text, re.DOTALL):
        line = _line_for_offset(line_starts, match.start())
        scope = scopes[line]
        targets: list[str] = []
        for reference in re.findall(
            r"\b[A-Za-z_]\w*\b", _strip_csharp_comments_and_strings(match.group("expression"))
        ):
            target = next(
                (
                    value
                    for definition_line, definition_scope, name, value in reversed(definitions)
                    if name == reference
                    and definition_line < line
                    and len(definition_scope) <= len(scope)
                    and tuple(scope[: len(definition_scope)]) == definition_scope
                ),
                None,
            )
            if target and target != "<unresolved>":
                targets.extend(target.split(";"))
        if targets:
            definitions.append((line, scope, match.group("name"), ";".join(dict.fromkeys(targets))))
    assignment_pattern = re.compile(
        r"(?:var|string)\s+(?P<name>[A-Za-z_]\w*)\s*=\s*(?P<expression>[^;]+);", re.DOTALL
    )
    assignment_expressions: list[tuple[int, str, int, str]] = []
    direct_keys: set[tuple[int, str, int]] = set()
    for match in assignment_pattern.finditer(text):
        line = _line_for_offset(line_starts, match.start())
        assignment_expressions.append(
            (line, match.group("name"), match.start("name"), match.group("expression"))
        )
        if (line, match.group("name"), match.start("name")) in reader_assignment_starts:
            direct_keys.add((line, match.group("name"), match.start("name")))
    assignments = sorted(assignment_expressions, key=lambda item: item[0])
    known_names = {name for _, _, name, _ in definitions}
    for match in re.finditer(
        r"(?<![=!<>])\b(?P<name>[A-Za-z_]\w*)\s*=\s*(?P<expression>[^;]+);", text, re.DOTALL
    ):
        name = match.group("name")
        if name not in known_names:
            continue
        line = _line_for_offset(line_starts, match.start())
        if any(
            key[0] == line and key[1] == name and key[2] == match.start("name")
            for key in direct_keys
        ):
            continue
        expression = match.group("expression")
        target = next(
            (
                value
                for value in (
                    _reader_target(reader, local_paths)
                    for reader in _call_spans(
                        expression.splitlines(),
                        READER_CALL_PATTERN,
                        expression,
                        _strip_csharp_comments_and_strings(expression),
                    )
                )
                if value
            ),
            None,
        )
        definitions.append((line, scopes[line], name, target or "<unresolved>"))
        definitions.sort(key=lambda item: item[0])
    indexed: dict[str, list[tuple[int, tuple[int, ...], str]]] = {}
    for line, scope, name, target in definitions:
        indexed.setdefault(name, []).append((line, scope, target))

    def lookup(name: str, line: int, scope: tuple[int, ...]) -> str | None:
        """Resolve a symbol to the nearest definition in the active scope."""
        for definition_line, definition_scope, target in reversed(indexed.get(name, [])):
            if (
                definition_line < line
                and len(definition_scope) <= len(scope)
                and tuple(scope[: len(definition_scope)]) == definition_scope
            ):
                return target
        return None

    for line, name, assignment_start, expression in assignments:
        if (line, name, assignment_start) in direct_keys:
            continue
        scope = scopes[line]
        references = re.findall(r"\b[A-Za-z_]\w*\b", expression)
        targets: list[str] = []
        for reference in references:
            reference_target = lookup(reference, line, scope)
            if reference_target:
                targets.extend(reference_target.split(";"))
        unresolved_reader = False
        for reader in _call_spans(
            expression.splitlines(),
            READER_CALL_PATTERN,
            expression,
            _strip_csharp_comments_and_strings(expression),
        ):
            reader_target = _reader_target(reader, local_paths)
            if reader_target:
                targets.extend(reader_target.split(";"))
            else:
                unresolved_reader = True
        if targets or unresolved_reader:
            target = ";".join(dict.fromkeys(targets)) or "<unresolved>"
            definitions.append((line, scope, name, target))
            indexed.setdefault(name, []).append((line, scope, target))
    invalidated = {
        (line, name) for line, _, name, target in definitions if target == "<unresolved>"
    }
    definitions = [
        definition
        for definition in definitions
        if (definition[0], definition[2]) not in invalidated or definition[3] == "<unresolved>"
    ]
    symbols.extend(definitions)
    _SCOPED_SYMBOL_CACHE[cache_key] = symbols
    return symbols


def _lookup_symbol(
    symbols: list[tuple[int, tuple[int, ...], str, str]],
    name: str,
    line: int,
    scope: tuple[int, ...],
) -> str | None:
    """Resolve the nearest symbol visible from a source line."""
    candidates = [
        (definition_line, definition_scope, target)
        for definition_line, definition_scope, symbol_name, target in symbols
        if symbol_name == name
        and definition_line <= line
        and len(definition_scope) <= len(scope)
        and tuple(scope[: len(definition_scope)]) == definition_scope
    ]
    if not candidates:
        return None
    _, _, target = max(candidates, key=lambda value: (len(value[1]), value[0]))
    return target


def _resolve_assertion_target(
    call: dict[str, object],
    paths: dict[str, str],
    symbols: list[tuple[int, tuple[int, ...], str, str]],
    scopes: list[tuple[int, ...]],
) -> str | None:
    """Resolve a source-text variable used as an assertion argument."""
    line = int(call["start_line"])
    scope = scopes[line]
    call_text = str(call["text"])
    code = _strip_csharp_comments_and_strings(call_text)
    names = {name for _, _, name, _ in symbols if name != "__last_reader__"}
    names.update(name for name in paths if name != "__last_reader__")
    targets: list[str] = []
    for name in names:
        if not re.search(rf"\b{re.escape(name)}\b", code):
            continue
        target = _lookup_symbol(symbols, name, line, scope)
        if target:
            targets.extend(target.split(";"))
    if targets:
        return ";".join(dict.fromkeys(targets))
    reader_targets: list[str] = []
    reader_seen = False
    for reader in _call_spans(call_text.splitlines(), READER_CALL_PATTERN):
        reader_seen = True
        target = _reader_target(reader, paths)
        if target:
            reader_targets.extend(target.split(";"))
    return ";".join(dict.fromkeys(reader_targets)) or ("<unresolved>" if reader_seen else None)


def _receiver_expression_start(masked: str, start: int) -> int:
    """Return the start of one receiver expression, excluding sibling operands."""
    cursor = start - 1
    depth = 0
    while cursor >= 0:
        char = masked[cursor]
        if char == ")":
            depth += 1
        elif char == "(" and depth:
            depth -= 1
        elif char == "(":
            return cursor + 1
        elif depth == 0 and (char in ";{}," or masked[cursor : cursor + 2] in ("&&", "||")):
            return cursor + 1
        cursor -= 1
    return 0


def _resolve_derived_target(
    lines: list[str],
    call: dict[str, object],
    paths: dict[str, str],
    symbols: list[tuple[int, tuple[int, ...], str, str]],
    scopes: list[tuple[int, ...]],
) -> str | None:
    """Resolve source origins for receiver expressions that contain method calls."""
    line = int(call["start_line"])
    text = str(call.get("text_file") or "\n".join(lines))
    start = int(call["start"])
    masked = str(call.get("masked") or _strip_csharp_comments_and_strings(text))
    statement_start = _receiver_expression_start(masked, start)
    expression = text[statement_start:start]
    code = _strip_csharp_comments_and_strings(expression)
    targets: list[str] = []
    unresolved = False
    names = {name for _, _, name, _ in symbols if name != "__last_reader__"}
    for name in names:
        if not re.search(rf"\b{re.escape(name)}\b", code):
            continue
        target = _lookup_symbol(symbols, name, line, scopes[line])
        if target:
            targets.extend(target.split(";"))
        else:
            unresolved = True
    for reader in _call_spans(expression.splitlines(), READER_CALL_PATTERN):
        target = _reader_target(reader, paths)
        if target:
            targets.extend(target.split(";"))
        else:
            unresolved = True
    if targets:
        return ";".join(dict.fromkeys(targets))
    return "<unresolved>" if unresolved else None


def _shared_scope_paths(files: list[Path]) -> dict[Path, dict[str, str]]:
    """Build shared const and reader symbols for partial validation classes."""
    groups: dict[str, list[Path]] = {}
    class_pattern = re.compile(r"\b(?:partial\s+)?(?:class|struct)\s+([A-Za-z_]\w*)")
    for path in files:
        text = path.read_text(encoding="utf-8", errors="replace")
        names = class_pattern.findall(text)
        key = names[0] if names else path.stem
        groups.setdefault(key, []).append(path)
    result: dict[Path, dict[str, str]] = {}
    for group in groups.values():
        lines = []
        for path in group:
            lines.extend(path.read_text(encoding="utf-8", errors="replace").splitlines())
        shared = _source_targets(lines, include_variables=False)
        local_symbols = {
            path: _scoped_source_symbols(
                path.read_text(encoding="utf-8", errors="replace").splitlines(), shared
            )
            for path in group
        }
        caller_texts = {path: path.read_text(encoding="utf-8", errors="replace") for path in group}
        caller_lines = {path: text.splitlines() for path, text in caller_texts.items()}
        caller_scopes = {path: _brace_paths(lines) for path, lines in caller_lines.items()}
        parameter_targets: dict[str, set[str]] = {}
        declaration_pattern = re.compile(
            r"\b(?P<name>[A-Za-z_]\w*)\s*\((?P<parameters>[^)]*)\)\s*\{"
        )
        relevant: list[tuple[str, list[str]]] = []
        for callee_path in group:
            callee_text = caller_texts[callee_path]
            for declaration in declaration_pattern.finditer(callee_text):
                parameters = [
                    match.group(1)
                    for match in re.finditer(
                        r"\b[A-Za-z_]\w*\s+([A-Za-z_]\w*)\s*(?:,|$)",
                        declaration.group("parameters"),
                    )
                ]
                if not parameters:
                    continue
                callee_masked = _strip_csharp_comments_and_strings(callee_text)
                body_start = callee_masked.find("{", declaration.end() - 1)
                body_end = (
                    _balanced_brace_end(callee_masked, body_start) if body_start >= 0 else None
                )
                body = callee_masked[body_start:body_end] if body_end is not None else ""
                parameters = [
                    parameter
                    for parameter in parameters
                    if re.search(rf"\b{re.escape(parameter)}\b", body)
                    and re.search(r"\b(?:Contains|IndexOf|DoesNotContain)\s*\(", body)
                ]
                if not parameters:
                    continue
                relevant.append((declaration.group("name"), parameters))
        call_index: dict[str, list[tuple[Path, re.Match[str]]]] = {}
        relevant_names = {name for name, _ in relevant}
        for caller_path in group:
            caller_text = caller_texts[caller_path]
            for call in re.finditer(
                r"\b(?P<name>[A-Za-z_]\w*)\s*\((?P<arguments>[^)]*)\)", caller_text
            ):
                if call.group("name") in relevant_names:
                    call_index.setdefault(call.group("name"), []).append((caller_path, call))
        for callee_name, parameters in relevant:
            for caller_path, call in call_index.get(callee_name, ()):
                caller_text = caller_texts[caller_path]
                arguments = [item.strip() for item in call.group("arguments").split(",")]
                line = caller_text.count("\n", 0, call.start())
                scope = caller_scopes[caller_path][line]
                for parameter, argument in zip(parameters, arguments):
                    target = (
                        _lookup_symbol(local_symbols[caller_path], argument, line, scope)
                        if re.fullmatch(r"[A-Za-z_]\w*", argument)
                        else None
                    )
                    reader = READER_CALL_PATTERN.search(argument) if target is None else None
                    if reader:
                        reader_argument = argument[argument.find("(", reader.start()) + 1 :]
                        first = _split_first_argument(reader_argument)
                        target = _extract_argument_path(reader_argument, shared) or _lookup_symbol(
                            local_symbols[caller_path], first, line, scope
                        )
                    if target and target != "<unresolved>":
                        parameter_targets.setdefault(parameter, set()).update(target.split(";"))
        for name, targets in parameter_targets.items():
            if name not in shared:
                shared[name] = next(iter(targets)) if len(targets) == 1 else "<unresolved>"
        for path in group:
            result[path] = shared
    return result


def _build_xunit_index(root: Path) -> dict[str, list[str]]:
    """Index unit-test files by the type names they reference."""
    index: dict[str, list[str]] = {}
    unit_files = sorted(root.glob("Packages/*/Tests/Unit/**/*.cs"))
    texts = [
        (
            path,
            _strip_csharp_comments_and_strings(path.read_text(encoding="utf-8", errors="replace")),
        )
        for path in unit_files
    ]
    product_types: set[str] = set()
    declaration = re.compile(r"\b(?:class|struct|record|interface|enum)\s+([A-Za-z_]\w*)")
    for path in root.glob("Packages/**/*.cs"):
        if "\\Tests\\" in str(path) or "/Tests/" in path.as_posix():
            continue
        text = path.read_text(encoding="utf-8", errors="replace")
        product_types.update(declaration.findall(text))
    for path, text in texts:
        referenced = set(re.findall(r"\b[A-Za-z_]\w*\b", text)) & product_types
        for stem in referenced:
            index.setdefault(stem, []).append(path.relative_to(root).as_posix())
    return index
