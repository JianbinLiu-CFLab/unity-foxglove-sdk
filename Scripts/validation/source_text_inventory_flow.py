"""Control-flow and helper-call analysis for source-text inventory."""

from __future__ import annotations

import hashlib
import re
from collections import Counter
from dataclasses import dataclass

try:
    from .source_text_inventory_text import (
        _balanced_brace_end,
        _balanced_call_end,
        _strip_csharp_comments_and_strings,
    )
except ImportError:
    from source_text_inventory_text import (
        _balanced_brace_end,
        _balanced_call_end,
        _strip_csharp_comments_and_strings,
    )


CONTROL_KEYWORDS = {
    "if",
    "else",
    "for",
    "foreach",
    "while",
    "switch",
    "catch",
    "using",
    "lock",
    "fixed",
    "nameof",
    "return",
    "throw",
}
CALL_PATTERN = re.compile(r"\b(?P<name>[A-Za-z_]\w*)\s*\(")


@dataclass(frozen=True)
class MethodInfo:
    """Describe one C# method body discovered by the lightweight parser."""

    name: str
    start: int
    body_start: int
    body_end: int
    signature: str
    parameter_types: tuple[str, ...] = ()

    @property
    def identity(self) -> str:
        """Return a source-stable identity for one method declaration."""
        return f"{self.name}@{self.start}"


def _method_infos(text: str, masked: str) -> list[MethodInfo]:
    """Discover method-like declarations and their balanced bodies."""
    pattern = re.compile(
        r"(?<![A-Za-z0-9_\[])(?P<signature>(?:(?:public|private|protected|internal|static|async|sealed|virtual|override|partial|unsafe|new)\s+)*"
        r"(?:(?:[A-Za-z_]\w*)\s+)?"
        r"(?P<name>[A-Za-z_]\w*)\s*\([^;{}]*\))\s*\{",
        re.DOTALL,
    )
    methods: list[MethodInfo] = []
    for match in pattern.finditer(masked):
        if masked.rfind("[", 0, match.start()) > masked.rfind("]", 0, match.start()):
            continue
        name = match.group("name")
        if name in CONTROL_KEYWORDS:
            continue
        body_start = masked.find("{", match.start(), match.end())
        body_end = _balanced_brace_end(masked, body_start)
        if body_start < 0 or body_end is None:
            continue
        signature = re.sub(r"\s+", " ", text[match.start() : body_start].strip())
        methods.append(
            MethodInfo(
                name,
                match.start(),
                body_start,
                body_end,
                signature,
                _method_parameter_types(masked, match.start(), body_start),
            )
        )
    expression_pattern = re.compile(
        r"(?<![A-Za-z0-9_\[])(?P<signature>(?:(?:public|private|protected|internal|static|async|sealed|virtual|override|partial|unsafe|new)\s+)*"
        r"(?:(?:[A-Za-z_]\w*)\s+)?"
        r"(?P<name>[A-Za-z_]\w*)\s*\([^;{}]*\))\s*=>\s*"
        r"(?P<body>[^;{}]+);",
        re.DOTALL,
    )
    for match in expression_pattern.finditer(masked):
        if masked.rfind("[", 0, match.start()) > masked.rfind("]", 0, match.start()):
            continue
        name = match.group("name")
        if name in CONTROL_KEYWORDS:
            continue
        body_start = match.start("body")
        body_end = match.end("body")
        signature = re.sub(
            r"\s+",
            " ",
            text[match.start() : match.start("body")].removesuffix("=>").strip(),
        )
        methods.append(
            MethodInfo(
                name,
                match.start(),
                body_start,
                body_end,
                signature,
                _method_parameter_types(masked, match.start(), match.start("body")),
            )
        )
    return sorted(methods, key=lambda method: method.start)


def _method_for_offset(methods: list[MethodInfo], offset: int) -> MethodInfo | None:
    """Return the smallest method body containing an offset."""
    candidates = [method for method in methods if method.body_start < offset < method.body_end]
    return min(candidates, key=lambda method: method.body_end - method.body_start, default=None)


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


def _local_types(text: str, method: MethodInfo) -> dict[str, str]:
    """Infer simple local and parameter types used by overload calls."""
    result: dict[str, str] = {}
    for parameter in _split_arguments(
        method.signature[method.signature.find("(") + 1 : method.signature.rfind(")")]
    ):
        tokens = parameter.strip().split()
        tokens = [token for token in tokens if token not in {"this", "ref", "out", "in", "params"}]
        if len(tokens) >= 2:
            result[tokens[-1]] = _normalize_type(" ".join(tokens[:-1]))

    body = text[method.body_start : method.body_end]
    for match in re.finditer(
        r"\b(?P<type>[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)?(?:<[^;=]+>)?(?:\[\])?|var)\s+"
        r"(?P<name>[A-Za-z_]\w*)\s*=\s*(?P<value>[^;]+);",
        body,
        re.DOTALL,
    ):
        declared = match.group("type")
        value = match.group("value").strip()
        result[match.group("name")] = (
            _infer_expression_type(value, result) if declared == "var" else _normalize_type(declared)
        )
    return result


def _infer_expression_type(expression: str, local_types: dict[str, str]) -> str:
    """Infer a small set of C# expression types used for overload binding."""
    expression = expression.strip()
    if re.fullmatch(r'"(?:\\.|[^"\\])*"', expression, re.DOTALL):
        return "string"
    if re.fullmatch(r"'(?:\\.|[^'\\])'", expression, re.DOTALL):
        return "char"
    if re.fullmatch(r"(?:true|false)", expression):
        return "bool"
    if re.fullmatch(r"[-+]?\d+[uUlLfFdDmM]?", expression):
        return "int"
    match = re.match(r"new\s+([A-Za-z_]\w*(?:\.[A-Za-z_]\w*)?(?:<[^>]+>)?)", expression)
    if match:
        return _normalize_type(match.group(1))
    if expression == "null":
        return "null"
    if re.fullmatch(r"[A-Za-z_]\w*", expression):
        return local_types.get(expression, "")
    match = re.match(r"\(([^)]+)\)", expression)
    return _normalize_type(match.group(1)) if match else ""


def _call_sites(text: str, masked: str, method: MethodInfo):
    """Yield same-file method call names and opening offsets for one method."""
    body = masked[method.body_start : method.body_end]
    for match in CALL_PATTERN.finditer(body):
        name = match.group("name")
        if name in CONTROL_KEYWORDS:
            continue
        open_index = method.body_start + match.end() - 1
        yield name, open_index


def _call_arguments(text: str, masked: str, open_index: int) -> str | None:
    """Return one call's argument text when its parentheses are balanced."""
    close_index = _balanced_call_end(masked, open_index)
    return text[open_index + 1 : close_index] if close_index is not None else None


def _resolve_call_target(
    name: str,
    arguments: str,
    caller: MethodInfo,
    methods_by_name: dict[str, list[MethodInfo]],
    text: str,
    local_types: dict[str, str] | None = None,
) -> MethodInfo | None:
    """Resolve a same-file call when its overload is statically identifiable."""
    candidates = methods_by_name.get(name, [])
    if not candidates:
        return None
    if len(candidates) == 1:
        return candidates[0]
    local_types = local_types if local_types is not None else _local_types(text, caller)
    argument_types = [_infer_expression_type(item, local_types) for item in _split_arguments(arguments)]
    exact: list[MethodInfo] = []
    for candidate in candidates:
        if len(candidate.parameter_types) != len(argument_types):
            continue
        if all(
            not actual or actual == "null" or actual == expected
            for actual, expected in zip(argument_types, candidate.parameter_types)
        ):
            exact.append(candidate)
    return exact[0] if len(exact) == 1 else None


def _method_call_graph(text: str, masked: str, methods: list[MethodInfo]) -> dict[str, set[str]]:
    """Build direct same-file method calls using method identities."""
    graph: dict[str, set[str]] = {method.identity: set() for method in methods}
    methods_by_name: dict[str, list[MethodInfo]] = {}
    for method in methods:
        methods_by_name.setdefault(method.name, []).append(method)
    for method in methods:
        local_types: dict[str, str] | None = None
        for name, open_index in _call_sites(text, masked, method):
            candidates = methods_by_name.get(name, [])
            if len(candidates) == 1:
                target = candidates[0]
            elif candidates:
                if local_types is None:
                    local_types = _local_types(text, method)
                arguments = _call_arguments(text, masked, open_index)
                if arguments is None:
                    continue
                target = _resolve_call_target(
                    name,
                    arguments,
                    method,
                    methods_by_name,
                    text,
                    local_types,
                )
            else:
                continue
            if len(candidates) > 1 and local_types is None:
                local_types = _local_types(text, method)
            if target is not None:
                graph[method.identity].add(target.identity)
    return graph


def _ambiguous_call_bindings(
    text: str, masked: str, methods: list[MethodInfo]
) -> dict[str, str]:
    """Hash ambiguous overload call sites so deleting one cannot preserve a row."""
    methods_by_name: dict[str, list[MethodInfo]] = {}
    for method in methods:
        methods_by_name.setdefault(method.name, []).append(method)
    counts = Counter(method.name for method in methods)
    bindings: dict[str, str] = {}
    for name, count in counts.items():
        if count < 2:
            continue
        calls: list[str] = []
        for method in methods:
            local_types = _local_types(text, method)
            for call_name, open_index in _call_sites(text, masked, method):
                if call_name != name:
                    continue
                arguments = _call_arguments(text, masked, open_index)
                if arguments is None:
                    continue
                target = _resolve_call_target(
                    call_name,
                    arguments,
                    method,
                    methods_by_name,
                    text,
                    local_types,
                )
                argument_types = ",".join(
                    _infer_expression_type(item, local_types) or "?"
                    for item in _split_arguments(arguments)
                )
                identity = target.identity if target is not None else "AMBIGUOUS"
                calls.append(f"{call_name}({re.sub(r'\\s+', ' ', arguments).strip()}):{argument_types}->{identity}")
        digest = hashlib.sha256("\n".join(sorted(calls)).encode()).hexdigest()
        for method in methods:
            if method.name == name:
                bindings[method.identity] = f"ambiguous:{digest}"
    return bindings


def _method_labels(methods: list[MethodInfo]) -> dict[str, str]:
    """Return stable display labels that distinguish overloaded methods."""
    counts = Counter(method.name for method in methods)
    return {
        method.identity: (
            method.name
            if counts[method.name] == 1
            else f"{method.name}({','.join(method.parameter_types)})"
        )
        for method in methods
    }


def _call_path_for_offset(text: str, masked: str, offset: int, entrypoint: str) -> str:
    """Return a registry-to-method path or ``UNKNOWN`` when static binding fails."""
    methods = _method_infos(text, masked)
    target = _method_for_offset(methods, offset)
    if target is None:
        return "UNKNOWN"
    _, paths = _call_path_index(text, masked, entrypoint)
    return paths.get(target.identity, "UNKNOWN")


def _call_path_index(
    text: str, masked: str, entrypoint: str
) -> tuple[list[MethodInfo], dict[str, str]]:
    """Build method metadata and registry call paths once per source file."""
    methods = _method_infos(text, masked)
    graph = _method_call_graph(text, masked, methods)
    paths: dict[str, str] = {}
    entrypoints = [name for name in entrypoint.split(";") if name]
    methods_by_name: dict[str, list[MethodInfo]] = {}
    for method in methods:
        methods_by_name.setdefault(method.name, []).append(method)
    entrypoint_methods = [
        methods_by_name[name][0]
        for name in entrypoints
        if len(methods_by_name.get(name, [])) == 1
    ]
    if not graph or not entrypoint_methods:
        return methods, paths
    labels = _method_labels(methods)
    queue: list[tuple[str, list[str]]] = [
        (method.identity, [labels[method.identity]]) for method in entrypoint_methods
    ]
    while queue:
        current, path = queue.pop(0)
        if current in paths:
            continue
        paths[current] = "->".join(path)
        for callee in sorted(graph.get(current, ())):
            if callee not in paths:
                queue.append((callee, path + [labels[callee]]))
    return methods, paths


def _validation_harness_signature(
    text: str,
    masked: str | None = None,
    methods: list[MethodInfo] | None = None,
) -> str:
    """Hash assertion and reader helper bodies while excluding Validate entrypoints."""
    masked = masked if masked is not None else _strip_csharp_comments_and_strings(text)
    methods = methods if methods is not None else _method_infos(text, masked)
    pieces: list[str] = []
    for method in methods:
        if method.name.startswith("Validate"):
            continue
        body = masked[method.body_start : method.body_end]
        if (
            method.name != "Check"
            and not method.name.startswith(("Assert", "Read", "Source"))
            and not re.search(
                r"\b(?:Check|Contains|IndexOf|DoesNotContain|Assert\.(?:True|False|Contains|DoesNotContain)|Read[A-Za-z_]\w*|Source[A-Za-z_]\w*)\s*\(",
                body,
            )
        ):
            continue
        pieces.append(
            re.sub(
                r"\s+",
                " ",
                method.signature + " " + text[method.body_start : method.body_end + 1],
            ).strip()
        )
    return hashlib.sha256("\n".join(pieces).encode()).hexdigest() if pieces else ""


def _reachability_signatures(
    lines: list[str],
    calls: list[dict[str, object]],
    text: str,
    masked: str,
    methods: list[MethodInfo] | None = None,
) -> dict[int, str]:
    """Return preprocessor and enclosing control-flow state for calls in one file."""
    methods = methods if methods is not None else _method_infos(text, masked)
    states: list[str] = []
    directives: list[str] = []
    for line in lines:
        directive = line.strip()
        states.append("|".join(directives))
        if directive.startswith("#if"):
            directives.append("if:" + re.sub(r"\s+", " ", directive[3:].strip()))
        elif directive.startswith("#elif") and directives:
            directives[-1] = "elif:" + re.sub(r"\s+", " ", directive[5:].strip())
        elif directive.startswith("#else") and directives:
            directives[-1] = "else"
        elif directive.startswith("#endif") and directives:
            directives.pop()
    intervals: list[tuple[int, int, str]] = []
    control_intervals: list[tuple[int, int, str]] = []
    for match in re.finditer(
        r"\b(?P<kind>if|else\s+if|while|foreach|for|switch|catch)\s*\(", masked
    ):
        open_index = masked.find("(", match.start(), match.end())
        close_index = _balanced_call_end(masked, open_index)
        if close_index is None:
            continue
        cursor = close_index + 1
        while cursor < len(masked) and masked[cursor].isspace():
            cursor += 1
        condition = re.sub(r"\s+", " ", text[open_index + 1 : close_index].strip())
        end = (
            _balanced_brace_end(masked, cursor)
            if cursor < len(masked) and masked[cursor] == "{"
            else masked.find(";", cursor)
        )
        if end is not None and end >= cursor:
            kind = match.group("kind")
            intervals.append((cursor, end, f"{kind}({condition})"))
            control_intervals.append((cursor, end, kind))
    for match in re.finditer(r"\btry\s*\{", masked):
        open_index = masked.find("{", match.start(), match.end())
        end = _balanced_brace_end(masked, open_index)
        if end is None:
            continue
        after = masked[end + 1 :]
        value = "try-catch" if re.match(r"\s*catch\b", after) else "try"
        intervals.append((open_index + 1, end, value))
    for match in re.finditer(r"\bcatch(?:\s*\([^)]*\))?\s*\{", masked):
        open_index = masked.find("{", match.start(), match.end())
        end = _balanced_brace_end(masked, open_index)
        if end is not None:
            intervals.append((open_index + 1, end, "catch"))
    for match in re.finditer(r"\belse\b(?!\s+if)", masked):
        cursor = match.end()
        while cursor < len(masked) and masked[cursor].isspace():
            cursor += 1
        end = (
            _balanced_brace_end(masked, cursor)
            if cursor < len(masked) and masked[cursor] == "{"
            else masked.find(";", cursor)
        )
        if end is not None and end >= cursor:
            begin = cursor + 1 if cursor < len(masked) and masked[cursor] == "{" else cursor
            intervals.append((begin, end, "else"))
    exits: list[tuple[int, int, str, str]] = []
    for match in re.finditer(
        r"\b(?P<kind>return|throw|continue|break|goto)\b[^;]*;|\byield\s+break\s*;",
        masked,
    ):
        kind = match.group("kind") or "yield break"
        conditions = [
            value
            for begin, end, value in intervals
            if begin <= match.start() <= end and value.startswith("if(")
        ]
        condition = conditions[-1][3:-1] if conditions else ""
        suffix = f":{condition}" if condition else ""
        exits.append((match.start(), match.end(), kind, f"exit:{kind}{suffix}"))
    result: dict[int, str] = {}
    for call in calls:
        start = int(call["start"])
        line = int(call["start_line"])
        method = _method_for_offset(methods, start)
        active = [value for begin, end, value in intervals if begin <= start <= end]
        for begin, end, kind, value in exits:
            if method is None or not (method.body_start < begin and end < start):
                continue
            if _method_for_offset(methods, begin) is not method:
                continue
            if kind in {"continue", "break"} and not any(
                loop_kind in {"while", "foreach", "for"}
                and loop_start <= begin <= loop_end
                and loop_start <= start <= loop_end
                for loop_start, loop_end, loop_kind in control_intervals
            ):
                continue
            active.append(value)
        result[start] = "|".join(
            ([states[line]] if line < len(states) and states[line] else []) + active
        )
    return result
