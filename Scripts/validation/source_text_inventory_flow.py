"""Control-flow and helper-call analysis for source-text inventory."""

from __future__ import annotations

import hashlib
import re
from bisect import bisect_right
from collections import Counter
from dataclasses import dataclass

try:
    from . import source_text_inventory_symbols as _symbol_helpers
    from .source_text_inventory_symbols import (
        _constant_values, _method_parameter_types, _method_return_type,
        _normalize_type, _parameter_types, _split_arguments,
    )
    from .source_text_inventory_text import (
        _balanced_brace_end,
        _balanced_call_end,
        _normalize_csharp_expression,
        _strip_csharp_comments_and_strings,
    )
except ImportError:
    import source_text_inventory_symbols as _symbol_helpers
    from source_text_inventory_symbols import (
        _constant_values, _method_parameter_types, _method_return_type,
        _normalize_type, _parameter_types, _split_arguments,
    )
    from source_text_inventory_text import (
        _balanced_brace_end,
        _balanced_call_end,
        _normalize_csharp_expression,
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
    return_type: str = ""

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
        signature = _normalize_csharp_expression(text[match.start() : body_start])
        methods.append(
            MethodInfo(
                name,
                match.start(),
                body_start,
                body_end,
                signature,
                _method_parameter_types(masked, match.start(), body_start),
                _method_return_type(signature, name),
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
        signature = _normalize_csharp_expression(text[match.start() : match.start("body")].removesuffix("=>"))
        methods.append(
            MethodInfo(
                name,
                match.start(),
                body_start,
                body_end,
                signature,
                _method_parameter_types(masked, match.start(), match.start("body")),
                _method_return_type(signature, name),
            )
        )
    return sorted(methods, key=lambda method: method.start)

def _method_for_offset(methods: list[MethodInfo], offset: int) -> MethodInfo | None:
    """Return the smallest method body containing an offset."""
    candidates = [method for method in methods if method.body_start < offset < method.body_end]
    return min(candidates, key=lambda method: method.body_end - method.body_start, default=None)

def _method_return_types(methods: list[MethodInfo]) -> dict[str, str]:
    """Return unambiguous same-file method return types."""
    grouped: dict[str, list[MethodInfo]] = {}
    for method in methods:
        grouped.setdefault(method.name, []).append(method)
    return {
        name: methods_for_name[0].return_type
        for name, methods_for_name in grouped.items()
        if len(methods_for_name) == 1 and methods_for_name[0].return_type
    }

def _field_types(text: str, masked: str, methods: list[MethodInfo]) -> dict[str, str]:
    """Collect simple field declarations outside method bodies."""
    result: dict[str, str] = {}
    for match in re.finditer(
        r"\b(?P<type>[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)?(?:<[^;=]+>)?(?:\[\])?)\s+"
        r"(?P<name>[A-Za-z_]\w*)\s*(?:=|;|\{)",
        masked,
    ):
        if any(method.body_start < match.start() < method.body_end for method in methods):
            continue
        result[match.group("name")] = _normalize_type(match.group("type"))
    return result

def _local_types(
    text: str,
    method: MethodInfo,
    field_types: dict[str, str] | None = None,
    return_types: dict[str, str] | None = None,
) -> dict[str, str]:
    """Infer simple local and parameter types used by overload calls."""
    result: dict[str, str] = dict(field_types or {})
    return_types = return_types or {}
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
            _infer_expression_type(value, result, return_types)
            if declared == "var"
            else _normalize_type(declared)
        )
    return result

def _infer_expression_type(
    expression: str,
    local_types: dict[str, str],
    return_types: dict[str, str] | None = None,
) -> str:
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
    if re.fullmatch(r"this\.[A-Za-z_]\w*", expression):
        return local_types.get(expression.split(".", 1)[1], "")
    call = re.fullmatch(r"([A-Za-z_]\w*)\s*\([^;{}]*\)", expression)
    if call:
        return (return_types or {}).get(call.group(1), "")
    match = re.match(r"\(([^)]+)\)", expression)
    return _normalize_type(match.group(1)) if match else ""

def _call_sites(
    text: str,
    masked: str,
    method: MethodInfo,
    known_names: set[str] | None = None,
):
    """Yield same-file method call names and opening offsets for one method."""
    body = masked[method.body_start : method.body_end]
    for match in CALL_PATTERN.finditer(body):
        name = match.group("name")
        if name in CONTROL_KEYWORDS:
            continue
        name_start = method.body_start + match.start()
        prefix = masked[method.body_start:name_start].rstrip()
        if prefix.endswith((".", "::")):
            qualifier_start = max(
                [method.body_start] + [masked.rfind(delimiter, method.body_start, name_start) + 1 for delimiter in ";{}"]
            )
            name = _normalize_csharp_expression(text[qualifier_start:name_start]) + name
        open_index = method.body_start + match.end() - 1
        if known_names is not None:
            if name not in known_names:
                continue
            close_index = _balanced_call_end(masked, open_index)
            if close_index is not None:
                cursor = close_index + 1
                while cursor < len(masked) and masked[cursor].isspace():
                    cursor += 1
                if cursor < len(masked) and masked[cursor] in "{=":
                    continue
        yield name, open_index

def _argument_definition_index(text: str, masked: str, methods: list[MethodInfo]) -> dict[int, list[tuple[int, str, str, str]]]:
    """Build one local-definition index from the already-discovered method bodies."""
    regions = [(method.body_start, method.body_end) for method in methods]
    result = _symbol_helpers._argument_definition_index(text, masked, regions)
    for owner, definitions in _symbol_helpers._byref_argument_definitions(text, masked, regions).items():
        result[owner].extend(definitions)
        result[owner].sort(key=lambda definition: definition[0])
    return result

def _method_call_contexts(
    text: str,
    masked: str,
    methods: list[MethodInfo],
    extra_calls: list[dict[str, object]] | None = None,
    argument_definitions: dict[int, list[tuple[int, str, str, str]]] | None = None,
) -> dict[int, str]:
    """Return one shared reachability map for method and assertion calls."""
    methods_by_name: dict[str, list[MethodInfo]] = {}
    for method in methods:
        methods_by_name.setdefault(method.name, []).append(method)
    calls = list(extra_calls or [])
    line_starts = _line_starts(text)
    argument_definitions = argument_definitions if argument_definitions is not None else _argument_definition_index(text, masked, methods)
    calls.extend(
        {"start": definition[0], "start_line": _line_number(line_starts, definition[0])}
        for definitions in argument_definitions.values() for definition in definitions
    )
    for method in methods:
        for name, open_index in _call_sites(text, masked, method):
            if name.rsplit(".", 1)[-1].rsplit("::", 1)[-1] not in methods_by_name:
                continue
            calls.append(
                {
                    "start": open_index,
                    "start_line": _line_number(line_starts, open_index),
                }
            )
    return _reachability_signatures(
        text.splitlines(), calls, text, masked, methods, argument_definitions
    )

def _line_starts(text: str) -> list[int]:
    """Return source line starts for indexed call-site lookups."""
    starts = [0]
    starts.extend(index + 1 for index, character in enumerate(text) if character == "\n")
    return starts

def _line_number(starts: list[int], offset: int) -> int:
    """Return a zero-based line number from precomputed line starts."""
    return bisect_right(starts, offset) - 1

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
    return_types: dict[str, str] | None = None,
) -> MethodInfo | None:
    """Resolve a same-file call when its overload is statically identifiable."""
    candidates = methods_by_name.get(name, [])
    if not candidates:
        return None
    if len(candidates) == 1:
        return candidates[0]
    local_types = local_types if local_types is not None else _local_types(text, caller)
    argument_types = [
        _infer_expression_type(item, local_types, return_types)
        for item in _split_arguments(arguments)
    ]
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

def _method_call_graph(
    text: str,
    masked: str,
    methods: list[MethodInfo],
    call_contexts: dict[int, str] | None = None,
    argument_definitions: dict[int, list[tuple[int, str, str, str]]] | None = None,
) -> dict[str, dict[str, set[str]]]:
    """Build same-file calls with their enclosing control-flow contexts."""
    graph: dict[str, dict[str, set[str]]] = {
        method.identity: {} for method in methods
    }
    methods_by_name: dict[str, list[MethodInfo]] = {}
    for method in methods:
        methods_by_name.setdefault(method.name, []).append(method)
    return_types = _method_return_types(methods)
    field_types = _field_types(text, masked, methods)
    call_sites_by_method: dict[str, list[tuple[str, int]]] = {}
    for method in methods:
        call_sites = list(
            _call_sites(text, masked, method, set(methods_by_name))
        )
        call_sites_by_method[method.identity] = call_sites
    argument_definitions = argument_definitions if argument_definitions is not None else _argument_definition_index(text, masked, methods)
    contexts = call_contexts if call_contexts is not None else _method_call_contexts(text, masked, methods, argument_definitions=argument_definitions)
    resolved_calls: dict[str, list[tuple[str, int, str, str]]] = {method.identity: [] for method in methods}
    for method in methods:
        local_types: dict[str, str] | None = None
        for name, open_index in call_sites_by_method[method.identity]:
            arguments = _call_arguments(text, masked, open_index)
            if arguments is None:
                continue
            candidates = methods_by_name.get(name, [])
            if len(candidates) == 1:
                target = candidates[0]
            elif candidates:
                if local_types is None:
                    local_types = _local_types(text, method, field_types, return_types)
                target = _resolve_call_target(
                    name,
                    arguments,
                    method,
                    methods_by_name,
                    text,
                    local_types,
                    return_types,
                )
            else:
                continue
            if target is not None:
                normalized_arguments = _normalize_csharp_expression(arguments)
                context = contexts.get(open_index, "")
                if normalized_arguments:
                    context = "|".join(value for value in (context, f"args({normalized_arguments})") if value)
                dependencies = _symbol_helpers._local_dependency_signature(
                    arguments, open_index, argument_definitions.get(method.body_start, []), contexts
                )
                if dependencies:
                    context = "|".join(value for value in (context, f"locals({dependencies})") if value)
                graph[method.identity].setdefault(target.identity, set()).add(context)
                resolved_calls[method.identity].append((target.identity, open_index, arguments, context))
    return _symbol_helpers._bind_state_writer_calls(
        text, masked, {method.identity: (method.body_start, method.body_end, method.signature) for method in methods},
        graph, resolved_calls, argument_definitions, set(field_types),
    )


def _partial_termination_barriers(
    text: str, masked: str, methods: list[MethodInfo]
) -> dict[str, str]:
    """Return same-class partial helper bodies that can terminate execution."""
    direct, _ = _symbol_helpers._execution_barriers(
        text, masked, [(method.body_start, method.body_end) for method in methods]
    )
    active = {
        method.identity
        for method in methods
        if any(owner == method.body_start and kind == "terminate" for _, _, owner, kind, _ in direct)
    }
    graph = _method_call_graph(text, masked, methods)
    incoming: dict[str, set[str]] = {}
    for caller, targets in graph.items():
        for callee in targets:
            incoming.setdefault(callee, set()).add(caller)
    pending = list(active)
    while pending:
        for caller in incoming.get(pending.pop(), set()):
            if caller not in active:
                active.add(caller)
                pending.append(caller)
    result: dict[str, str] = {}
    for method in methods:
        if method.identity not in active:
            continue
        body = _normalize_csharp_expression(
            method.signature + " " + text[method.body_start : method.body_end + 1]
        )
        result[method.name] = hashlib.sha256(body.encode()).hexdigest()[:16]
    return result

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
        return_types = _method_return_types(methods)
        field_types = _field_types(text, masked, methods)
        for method in methods:
            local_types = _local_types(text, method, field_types, return_types)
            for call_name, open_index in _call_sites(
                text, masked, method, set(methods_by_name)
            ):
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
                    return_types,
                )
                argument_types = ",".join(
                    _infer_expression_type(item, local_types, return_types) or "?"
                    for item in _split_arguments(arguments)
                )
                identity = target.identity if target is not None else "AMBIGUOUS"
                unknown_body = ""
                if target is None or "?" in argument_types:
                    unknown_body = hashlib.sha256(
                        re.sub(
                            r"\s+",
                            " ",
                            masked[method.body_start : method.body_end],
                        ).strip().encode()
                    ).hexdigest()[:16]
                calls.append(
                    f"{call_name}({_normalize_csharp_expression(arguments)}):"
                    f"{argument_types}->{identity}:unknown_body={unknown_body}"
                )
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
    text: str,
    masked: str,
    entrypoint: str,
    call_contexts: dict[int, str] | None = None,
    methods: list[MethodInfo] | None = None,
    argument_definitions: dict[int, list[tuple[int, str, str, str]]] | None = None,
) -> tuple[list[MethodInfo], dict[str, str]]:
    """Build method metadata and registry call paths once per source file."""
    methods = methods if methods is not None else _method_infos(text, masked)
    graph = _method_call_graph(text, masked, methods, call_contexts, argument_definitions)
    paths: dict[str, str] = {}
    depths: dict[str, int] = {}
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
        depths[current] = len(path) - 1
        for callee, contexts in sorted(graph.get(current, {}).items()):
            if callee not in paths:
                edge = labels[callee]
                context_values = sorted(value for value in contexts if value)
                if context_values:
                    edge += "[" + "|".join(context_values) + "]"
                queue.append((callee, path + [edge]))
    incoming: dict[str, list[tuple[str, str]]] = {}
    for caller in paths:
        for callee, contexts in graph.get(caller, {}).items():
            if callee in paths:
                for context in contexts:
                    edge = repr((labels[caller], labels[callee], context))
                    incoming.setdefault(callee, []).append((caller, edge))
    for target in paths:
        pending = [target]
        visited: set[str] = set()
        edges: set[str] = set()
        while pending:
            current = pending.pop()
            if current in visited:
                continue
            visited.add(current)
            for caller, edge in incoming.get(current, []):
                edges.add(edge)
                pending.append(caller)
        if len(edges) > depths[target]:
            digest = hashlib.sha256(repr(sorted(edges)).encode("utf-8")).hexdigest()
            paths[target] += "|edges(" + digest + ")"
    return methods, paths

def _validation_harness_signature(
    text: str,
    masked: str | None = None,
    methods: list[MethodInfo] | None = None,
    entrypoint: str = "",
    argument_definitions: dict[int, list[tuple[int, str, str, str]]] | None = None,
    call_contexts: dict[int, str] | None = None,
) -> str:
    """Hash assertion and reader helper bodies while excluding registered entrypoints."""
    masked = masked if masked is not None else _strip_csharp_comments_and_strings(text)
    methods = methods if methods is not None else _method_infos(text, masked)
    argument_definitions = argument_definitions if argument_definitions is not None else _argument_definition_index(text, masked, methods)
    call_contexts = call_contexts if call_contexts is not None else _method_call_contexts(text, masked, methods, argument_definitions=argument_definitions)
    entrypoints = set(entrypoint.split(";"))
    counts = Counter(method.name for method in methods)
    pieces: list[str] = []
    for method in methods:
        if method.name in entrypoints and counts[method.name] == 1:
            continue
        body = masked[method.body_start : method.body_end]
        delegate_parameter = any(
            _normalize_type(parameter).removeprefix("System.").startswith(
                ("Action", "Func", "Predicate")
            )
            for parameter in method.parameter_types
        )
        if (
            method.name != "Check"
            and not method.name.startswith(("Assert", "Read", "Source", "Validate"))
            and not delegate_parameter
            and not re.search(
                r"\b(?:Check|Contains|IndexOf|DoesNotContain|Assert\.(?:True|False|Contains|DoesNotContain)|Read[A-Za-z_]\w*|Source[A-Za-z_]\w*)\s*\(",
                body,
            )
        ):
            continue
        pieces.append(_normalize_csharp_expression(method.signature + " " + text[method.body_start : method.body_end + 1]))
    method_names = {method.name for method in methods}
    for method in methods:
        for name, open_index in _call_sites(text, masked, method):
            terminal = re.search(r"[A-Za-z_]\w*$", name)
            if name not in method_names and terminal and terminal.group() in method_names:
                arguments = _call_arguments(text, masked, open_index) or ""
                normalized = _normalize_csharp_expression(arguments)
                dependencies = _symbol_helpers._local_dependency_signature(
                    f"{name}({arguments})", open_index, argument_definitions.get(method.body_start, []), call_contexts
                )
                pieces.append(f"qualified:{method.signature}:{name}({normalized})@{call_contexts.get(open_index, '')}:locals({dependencies})")
    lambda_signature = _lambda_binding_signature(text, masked, methods)
    if lambda_signature:
        pieces.append(f"lambda:{lambda_signature}")
    delegate_signature = _delegate_binding_signature(text, masked, methods)
    if delegate_signature:
        pieces.append(f"delegate:{delegate_signature}")
    return hashlib.sha256("\n".join(pieces).encode()).hexdigest() if pieces else ""

def _delegate_binding_signature(
    text: str, masked: str, methods: list[MethodInfo]
) -> str:
    """Hash delegate assignments, method-group arguments, and forwarding calls."""
    delegate_methods = {
        method.name
        for method in methods
        if _normalize_type(method.return_type)
        .removeprefix("System.")
        .startswith(("Action", "Func", "Predicate"))
    }
    delegate_methods.update(
        match.group("name")
        for match in re.finditer(
            r"\b(?:System\.)?(?:Action|Func|Predicate)(?:<[^;{}]+>)?\s+"
            r"(?P<name>[A-Za-z_]\w*)\s*\([^;{}]*\)\s*=>",
            masked,
        )
    )
    pieces: list[str] = []
    method_names = {method.name for method in methods}
    delegate_variables: set[str] = set()
    assignment_pattern = re.compile(
        r"\b(?:(?P<type>(?:(?:System\.)?(?:Action|Func|Predicate)(?:<[^;=]+>)?|var))\s+)?"
        r"(?P<name>[A-Za-z_]\w*)\s*=\s*(?P<target>[^;]+);"
    )
    for match in assignment_pattern.finditer(masked):
        target = re.sub(r"\s+", " ", match.group("target")).strip()
        target_names = set(re.findall(r"\b[A-Za-z_]\w*\b", target))
        explicit_delegate = bool(match.group("type")) and match.group("type") != "var"
        is_delegate = (
            explicit_delegate
            or match.group("name") in delegate_variables
            or bool(target_names & delegate_methods)
            or "=>" in target
            or re.search(r"\bdelegate\s*\(", target) is not None
        )
        if not is_delegate:
            continue
        name = match.group("name")
        delegate_variables.add(name)
        invocation = re.search(
            rf"\b{re.escape(name)}(?:\s*\([^;]*\)|\s*\?\.\s*Invoke\s*\([^;]*\)|"
            rf"\s*\.\s*Invoke\s*\([^;]*\))\s*;|"
            rf"\b[A-Za-z_]\w*\s*\(\s*{re.escape(name)}\s*\)\s*;",
            masked[match.end() :],
        )
        invocation_text = re.sub(r"\s+", " ", invocation.group()).strip() if invocation else ""
        pieces.append(f"assign:{match.group('type') or ''} {name}={target}|invoke:{invocation_text}")
    for method in methods:
        for call_name, open_index in _call_sites(text, masked, method):
            arguments = _call_arguments(text, masked, open_index)
            if arguments is None:
                continue
            argument_names = set(re.findall(r"\b[A-Za-z_]\w*\b", arguments))
            if not argument_names & method_names and not re.search(
                r"=>|\bdelegate\s*\(", arguments
            ):
                continue
            normalized = _normalize_csharp_expression(arguments)
            pieces.append(f"call:{call_name}({normalized})")
    return hashlib.sha256("\n".join(sorted(pieces)).encode()).hexdigest() if pieces else ""


def _lambda_binding_signature(
    text: str, masked: str, methods: list[MethodInfo] | None = None
) -> str:
    """Hash lambda/delegate bodies, invocation forms, and enclosing control flow."""
    pieces: list[str] = []
    seen: set[int] = set()
    context_positions: list[int] = []
    line_starts = _line_starts(text)
    assignment_records: list[tuple[re.Match[str], int, list[re.Match[str]]]] = []
    inline_records: list[tuple[str, str, str, str, int]] = []
    delegate_records: list[tuple[str, str, str, str, int]] = []
    methods_by_name: dict[str, list[MethodInfo]] = {}
    for method in methods or []:
        methods_by_name.setdefault(method.name, []).append(method)

    def owner_signature(owner: str) -> str:
        """Return a stable digest for an inline lambda receiver method."""
        candidates = methods_by_name.get(owner, [])
        if not candidates:
            return ""
        payload = "\n".join(
            re.sub(
                r"\s+",
                " ",
                method.signature + " " + masked[method.body_start : method.body_end + 1],
            ).strip()
            for method in candidates
        )
        return hashlib.sha256(payload.encode()).hexdigest()[:16]

    def region_end(cursor: int) -> int | None:
        """Return the end of one lambda or delegate body."""
        while cursor < len(masked) and masked[cursor].isspace():
            cursor += 1
        if cursor < len(masked) and masked[cursor] == "{":
            return _balanced_brace_end(masked, cursor)
        return masked.find(";", cursor)

    def statement_for(lambda_start: int, body_end: int) -> str:
        """Return the normalized statement that binds one inline lambda."""
        start = masked.rfind(";", 0, lambda_start) + 1
        end = body_end if masked[body_end : body_end + 1] == ";" else masked.find(";", body_end + 1)
        if end < 0:
            return ""
        return re.sub(r"\s+", " ", masked[start : end + 1]).strip()

    pattern = re.compile(
        r"\b(?P<name>[A-Za-z_]\w*)\s*=\s*"
        r"(?P<lambda>(?:\([^)]*\)|[A-Za-z_]\w*)\s*=>|delegate(?:\s*\([^)]*\))?\s*\{)"
    )
    for match in pattern.finditer(masked):
        end = region_end(match.end())
        if end is None:
            continue
        seen.add(match.start())
        seen.add(match.start("lambda"))
        name = match.group("name")
        invocation_pattern = re.compile(
            rf"\b{re.escape(name)}(?:\s*\([^;]*\)|\s*\?\.\s*Invoke\s*\([^;]*\)|"
            rf"\s*\.\s*Invoke\s*\([^;]*\))\s*;|"
            rf"\b[A-Za-z_]\w*\s*\(\s*{re.escape(name)}\s*\)\s*;"
        )
        invocation_matches = list(invocation_pattern.finditer(masked, end + 1))
        assignment_records.append((match, end, invocation_matches))
        context_positions.extend(item.start() for item in invocation_matches)

    inline_pattern = re.compile(r"(?:\([^)]*\)|[A-Za-z_]\w*)\s*=>")
    for match in inline_pattern.finditer(masked):
        if match.start() in seen:
            continue
        end = region_end(match.end())
        if end is None:
            continue
        prefix = masked[max(0, match.start() - 200) : match.start() + 1]
        owner_match = re.search(r"\b([A-Za-z_]\w*)\s*\([^()]*$", prefix)
        owner = owner_match.group(1) if owner_match else "UNKNOWN"
        body = re.sub(r"\s+", " ", masked[match.start() : end + 1]).strip()
        statement = statement_for(match.start(), end)
        inline_records.append(
            (owner, owner_signature(owner), statement, body, match.start())
        )
        context_positions.append(match.start())

    delegate_pattern = re.compile(r"\bdelegate(?:\s*\([^)]*\))?\s*\{")
    for match in delegate_pattern.finditer(masked):
        if match.start() in seen:
            continue
        end = region_end(match.end())
        if end is None:
            continue
        prefix = masked[max(0, match.start() - 200) : match.start() + 1]
        owner_match = re.search(r"\b([A-Za-z_]\w*)\s*\([^()]*$", prefix)
        owner = owner_match.group(1) if owner_match else "UNKNOWN"
        body = re.sub(r"\s+", " ", masked[match.start() : end + 1]).strip()
        statement = statement_for(match.start(), end)
        delegate_records.append(
            (owner, owner_signature(owner), statement, body, match.start())
        )
        context_positions.append(match.start())
    contexts = _reachability_signatures(
        text.splitlines(),
        [
            {
                "start": position,
                "start_line": _line_number(line_starts, position),
            }
            for position in context_positions
        ],
        text,
        masked,
        methods,
    ) if context_positions else {}
    for match, end, invocation_matches in assignment_records:
        body = re.sub(r"\s+", " ", masked[match.start() : end + 1]).strip()
        calls = "|".join(
            f"{re.sub(r'\s+', ' ', item.group()).strip()}@{contexts.get(item.start(), '')}"
            for item in invocation_matches
        )
        pieces.append(f"{body}|calls:{calls}")
    for owner, owner_digest, statement, body, position in inline_records:
        pieces.append(
            f"inline:{owner}:{owner_digest}:{statement}:{body}:context:{contexts.get(position, '')}"
        )
    for owner, owner_digest, statement, body, position in delegate_records:
        pieces.append(
            f"delegate:{owner}:{owner_digest}:{statement}:{body}:context:{contexts.get(position, '')}"
        )
    return hashlib.sha256("\n".join(pieces).encode()).hexdigest() if pieces else ""

def _reachability_signatures(
    lines: list[str],
    calls: list[dict[str, object]],
    text: str,
    masked: str,
    methods: list[MethodInfo] | None = None,
    argument_definitions: dict[int, list[tuple[int, str, str, str]]] | None = None,
) -> dict[int, str]:
    """Return preprocessor and enclosing control-flow state for calls in one file."""
    methods = methods if methods is not None else _method_infos(text, masked)
    argument_definitions = argument_definitions if argument_definitions is not None else _argument_definition_index(text, masked, methods)
    barriers, lambda_regions = _symbol_helpers._execution_barriers(text, masked, [(method.body_start, method.body_end) for method in methods])
    if any(kind == "terminate" for _, _, _, kind, _ in barriers):
        methods_by_name: dict[str, list[MethodInfo]] = {}
        for method in methods:
            methods_by_name.setdefault(method.name, []).append(method)
        helper_calls = []
        for method in methods:
            for name, position in _call_sites(text, masked, method, set(methods_by_name)):
                if _method_for_offset(methods, position) is not method or any(begin <= position <= end for begin, end in lambda_regions):
                    continue
                end = _balanced_call_end(masked, position)
                if end is not None:
                    expression = name + "(" + text[position + 1 : end] + ")"
                    helper_calls.extend(
                        (method.body_start, target.body_start, position, end, _normalize_csharp_expression(expression))
                        for target in methods_by_name[name]
                    )
        barriers.extend(_symbol_helpers._helper_execution_barriers(
            text, {method.body_start: (method.body_end, method.signature) for method in methods}, helper_calls, barriers
        ))
    calls = list(calls)
    positions = {int(call["start"]) for call in calls}
    line_starts = _line_starts(text)
    extra_positions = [definition[0] for definitions in argument_definitions.values() for definition in definitions]
    extra_positions.extend(barrier[0] for barrier in barriers)
    calls.extend(
        {"start": position, "start_line": _line_number(line_starts, position)}
        for position in extra_positions if position not in positions
    )
    states: list[str] = []
    directives: list[list[str]] = []
    for line in lines:
        directive = line.strip()
        states.append("|".join("->".join(branches) for branches in directives))
        if directive.startswith("#if"):
            directives.append(["if:" + re.sub(r"\s+", " ", directive[3:].strip())])
        elif directive.startswith("#elif") and directives:
            directives[-1].append("elif:" + re.sub(r"\s+", " ", directive[5:].strip()))
        elif directive.startswith("#else") and directives:
            directives[-1].append("else")
        elif directive.startswith("#endif") and directives:
            directives.pop()
    constant_values = _constant_values(text, masked)
    intervals = _symbol_helpers._conditional_branch_intervals(text, masked, constant_values)
    control_intervals: list[tuple[int, int, str]] = []
    for match in re.finditer(
        r"\b(?P<kind>while|foreach|for|switch)\s*\(", masked
    ):
        open_index = masked.find("(", match.start(), match.end())
        close_index = _balanced_call_end(masked, open_index)
        if close_index is None:
            continue
        cursor = close_index + 1
        while cursor < len(masked) and masked[cursor].isspace():
            cursor += 1
        condition = _normalize_csharp_expression(text[open_index + 1 : close_index])
        selector = constant_values.get(condition)
        if selector is not None:
            condition = f"{condition}={selector}"
        end = _symbol_helpers._embedded_statement_end(masked, cursor)
        if end is not None and end >= cursor:
            kind = match.group("kind")
            intervals.append((cursor, end, f"{kind}({condition})"))
            control_intervals.append((cursor, end, kind))
            if kind == "switch" and cursor < len(masked) and masked[cursor] == "{":
                body_start = cursor + 1
                body_end = end
                case_matches = list(
                    re.finditer(
                        r"\b(?P<label>case\b[^:]*|default)\s*:",
                        masked[body_start:body_end],
                    )
                )
                for case_index, case_match in enumerate(case_matches):
                    case_start = body_start + case_match.end()
                    case_end = (
                        body_start + case_matches[case_index + 1].start() - 1
                        if case_index + 1 < len(case_matches)
                        else body_end
                    )
                    label_start = body_start + case_match.start()
                    label_end = body_start + case_match.end() - 1
                    label = _normalize_csharp_expression(text[label_start:label_end])
                    intervals.append((case_start, case_end, f"switch-case({label})"))
    for match in re.finditer(r"\bdo\s*", masked):
        cursor = match.end()
        end = _symbol_helpers._embedded_statement_end(masked, cursor)
        if end is None or end < cursor:
            continue
        tail = re.match(r"\s*while\s*\(", masked[end + 1 :])
        if tail is None:
            continue
        open_index = end + tail.end()
        close_index = _balanced_call_end(masked, open_index)
        if close_index is not None:
            condition = _normalize_csharp_expression(text[open_index + 1 : close_index])
            selector = constant_values.get(condition)
            if selector is not None:
                condition = f"{condition}={selector}"
            intervals.append((cursor, end, f"do({condition})"))
            control_intervals.append((cursor, end, "do"))
    intervals.extend(_symbol_helpers._exception_intervals(text, masked))
    exits: list[tuple[int, int, str, str]] = []
    for match in re.finditer(
        r"\b(?P<kind>return|throw|continue|break|goto)\b[^;]*;|\byield\s+break\s*;",
        masked,
    ):
        kind = match.group("kind") or "yield break"
        conditions = [
            value
            for begin, end, value in intervals
            if begin <= match.start() <= end
        ]
        condition = "|".join(conditions)
        suffix = f":{condition}" if condition else ""
        if kind == "goto":
            suffix += ":" + _normalize_csharp_expression(text[match.start() : match.end()])
        exits.append((match.start(), match.end(), kind, f"exit:{kind}{suffix}"))
    result: dict[int, str] = {}
    owners: dict[int, int | None] = {}
    for call in calls:
        start = int(call["start"])
        line = int(call["start_line"])
        method = _method_for_offset(methods, start)
        owners[start] = method.body_start if method is not None else None
        active = [value for begin, end, value in intervals if begin <= start <= end]
        for begin, end, kind, value in exits:
            if method is None or not (method.body_start < begin and end < start):
                continue
            if _method_for_offset(methods, begin) is not method:
                continue
            if kind == "continue" and not any(
                loop_kind in {"while", "foreach", "for", "do"}
                and loop_start <= begin <= loop_end
                and loop_start <= start <= loop_end
                for loop_start, loop_end, loop_kind in control_intervals
            ):
                continue
            if kind == "break" and not any(
                loop_kind in {"while", "foreach", "for", "do", "switch"}
                and loop_start <= begin <= loop_end
                and loop_start <= start <= loop_end
                for loop_start, loop_end, loop_kind in control_intervals
            ):
                continue
            active.append(value)
        result[start] = "|".join(
            ([states[line]] if line < len(states) and states[line] else []) + active
        )
    return _symbol_helpers._bind_execution_barriers(barriers, lambda_regions, result, owners, argument_definitions)
