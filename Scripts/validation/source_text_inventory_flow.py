"""Control-flow and helper-call analysis for source-text inventory."""

from __future__ import annotations

import hashlib
import re
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


@dataclass(frozen=True)
class MethodInfo:
    """Describe one C# method body discovered by the lightweight parser."""

    name: str
    start: int
    body_start: int
    body_end: int
    signature: str


def _method_infos(text: str, masked: str) -> list[MethodInfo]:
    """Discover method-like declarations and their balanced bodies."""
    pattern = re.compile(
        r"(?P<signature>(?:(?:public|private|protected|internal|static|async|sealed|virtual|override|partial|unsafe|new)\s+)*"
        r"(?:(?:[A-Za-z_]\w*)\s+)?"
        r"(?P<name>[A-Za-z_]\w*)\s*\([^;{}]*\))\s*\{",
        re.DOTALL,
    )
    methods: list[MethodInfo] = []
    for match in pattern.finditer(masked):
        name = match.group("name")
        if name in CONTROL_KEYWORDS:
            continue
        body_start = masked.find("{", match.start(), match.end())
        body_end = _balanced_brace_end(masked, body_start)
        if body_start < 0 or body_end is None:
            continue
        signature = re.sub(r"\s+", " ", text[match.start() : body_start].strip())
        methods.append(MethodInfo(name, match.start(), body_start, body_end, signature))
    return methods


def _method_for_offset(methods: list[MethodInfo], offset: int) -> MethodInfo | None:
    """Return the smallest method body containing an offset."""
    candidates = [method for method in methods if method.body_start < offset < method.body_end]
    return min(candidates, key=lambda method: method.body_end - method.body_start, default=None)


def _method_call_graph(text: str, masked: str, methods: list[MethodInfo]) -> dict[str, set[str]]:
    """Build direct same-file method calls for call-path binding."""
    graph: dict[str, set[str]] = {method.name: set() for method in methods}
    known = set(graph)
    call_pattern = re.compile(r"\b(?P<name>[A-Za-z_]\w*)\s*\(")
    for method in methods:
        body = masked[method.body_start : method.body_end]
        for match in call_pattern.finditer(body):
            name = match.group("name")
            if name in known and name not in CONTROL_KEYWORDS:
                graph[method.name].add(name)
    return graph


def _call_path_for_offset(text: str, masked: str, offset: int, entrypoint: str) -> str:
    """Return a registry-to-method path or ``UNKNOWN`` when static binding fails."""
    methods = _method_infos(text, masked)
    target = _method_for_offset(methods, offset)
    if target is None or not entrypoint:
        return "UNKNOWN"
    graph = _method_call_graph(text, masked, methods)
    if entrypoint not in graph:
        return "UNKNOWN"
    queue: list[tuple[str, list[str]]] = [(entrypoint, [entrypoint])]
    seen = {entrypoint}
    while queue:
        current, path = queue.pop(0)
        if current == target.name:
            return "->".join(path)
        for callee in sorted(graph.get(current, ())):
            if callee not in seen:
                seen.add(callee)
                queue.append((callee, path + [callee]))
    return "UNKNOWN"


def _call_path_index(
    text: str, masked: str, entrypoint: str
) -> tuple[list[MethodInfo], dict[str, str]]:
    """Build method metadata and registry call paths once per source file."""
    methods = _method_infos(text, masked)
    graph = _method_call_graph(text, masked, methods)
    paths: dict[str, str] = {}
    if entrypoint not in graph:
        return methods, paths
    queue: list[tuple[str, list[str]]] = [(entrypoint, [entrypoint])]
    while queue:
        current, path = queue.pop(0)
        if current in paths:
            continue
        paths[current] = "->".join(path)
        for callee in sorted(graph.get(current, ())):
            if callee not in paths:
                queue.append((callee, path + [callee]))
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
        if not re.search(
            r"\b(?:Check|Contains|IndexOf|DoesNotContain|Assert\.(?:True|False|Contains|DoesNotContain)|Read[A-Za-z_]\w*|Source[A-Za-z_]\w*)\s*\(",
            body,
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
    lines: list[str], calls: list[dict[str, object]], text: str, masked: str
) -> dict[int, str]:
    """Return preprocessor and enclosing control-flow state for calls in one file."""
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
            intervals.append((cursor, end, f"{match.group('kind')}({condition})"))
    exits: list[tuple[int, int, str]] = []
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
        exits.append((match.start(), match.end(), f"exit:{kind}{suffix}"))
    result: dict[int, str] = {}
    for call in calls:
        start = int(call["start"])
        line = int(call["start_line"])
        block_start = max(masked.rfind("{", 0, start), masked.rfind("}", 0, start))
        active = [value for begin, end, value in intervals if begin <= start <= end]
        active.extend(value for begin, end, value in exits if block_start < begin and end < start)
        result[start] = "|".join(
            ([states[line]] if line < len(states) and states[line] else []) + active
        )
    return result
