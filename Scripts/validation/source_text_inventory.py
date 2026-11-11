"""Inventory source-text assertions in runtime and unit validation files."""

from __future__ import annotations

import argparse
import csv
import hashlib
import importlib.util
import re
import subprocess
import sys
import tarfile
import tempfile
from collections import Counter
from dataclasses import replace
from pathlib import Path

MODULE_DIR = Path(__file__).resolve().parent
PARSER_SEMANTICS_VERSION = "2"
_VALIDATION_METADATA_CACHE: dict[tuple[str, str], tuple[str, str]] = {}
if str(MODULE_DIR) not in sys.path:
    sys.path.insert(0, str(MODULE_DIR))

try:
    from . import source_text_inventory_ratchet as _ratchet_module
    from . import source_text_inventory_report as _report_module
    from . import source_text_inventory_text as _text_module
    from . import source_text_inventory_flow as _flow_module
    from . import source_text_inventory_symbols as _symbols_module
    from .source_text_inventory_text import *
    from .source_text_inventory_symbols import *
    from .source_text_inventory_flow import *
    from .source_text_inventory_ratchet import *
    from .source_text_inventory_report import *
except ImportError:
    import source_text_inventory_ratchet as _ratchet_module
    import source_text_inventory_report as _report_module
    import source_text_inventory_text as _text_module
    import source_text_inventory_flow as _flow_module
    import source_text_inventory_symbols as _symbols_module
    from source_text_inventory_text import *
    from source_text_inventory_symbols import *
    from source_text_inventory_flow import *
    from source_text_inventory_ratchet import *
    from source_text_inventory_report import *

for _module in (_text_module, _symbols_module, _flow_module, _ratchet_module, _report_module):
    globals().update(
        {
            _name: getattr(_module, _name)
            for _name in dir(_module)
            if _name.startswith("_") and not _name.startswith("__")
        }
    )


class _ExternalHelperIndex:
    """Bind selected package helper methods across namespaces and partial files."""

    def __init__(self, root: Path):
        """Index type identities once and defer parsing methods until referenced."""
        self.root = root
        self.units = {}
        self.types = {}
        self.methods = {}
        self.closures = {}
        self.node_work_cache = {}
        self.signature_cache = {}
        self.fields = {}
        self.receivers = {}
        self.clr_builder_receivers = {}
        self.fallbacks = {}
        self.type_candidates_cache = {}
        self.declaration_files = None
        for path in sorted(root.glob("Packages/*/**/*.cs")):
            source = path.read_text(encoding="utf-8", errors="replace")
            masked = _strip_csharp_comments_and_strings(source)
            regions = _source_type_regions(masked)
            imports = re.findall(
                r"\busing\s+(?:(static)\s+)?(?:([A-Za-z_]\w*)\s*=\s*)?((?:global::)?[\w.]+)\s*;", masked
            )
            self.units[path] = {"text": source, "masked": masked, "regions": regions, "imports": imports}
            for begin, end, name in regions:
                self.types.setdefault(name, []).append((path, begin, end))

    def _load(self, path: Path):
        """Cache method bodies and class initializer definitions for one source file."""
        unit = self.units[path]
        if unit.get("indexed"):
            return
        methods = unit.get("methods")
        if methods is None:
            methods = _flow_module._method_infos(unit["text"], unit["masked"])
        regions = unit["regions"]
        unit["fields"] = _symbols_module._argument_definition_index(
            unit["text"], unit["masked"],
            [(begin, end) for begin, end, _ in regions] + [(method.body_start, method.body_end) for method in methods],
        )
        for method in methods:
            if _flow_module._method_for_offset(methods, method.start) is not None:
                continue
            owner = min(
                (region for region in regions if region[0] < method.body_start < region[1]),
                key=lambda region: region[1] - region[0], default=None,
            )
            if owner is not None:
                self.methods.setdefault((owner[2], method.name), []).append((path, owner, method))
        unit.update(methods=methods, indexed=True)

    def _package(self, path: Path) -> tuple[str, ...]:
        """Return the package identity of one indexed source file."""
        return path.relative_to(self.root).parts[:2]

    def _fragments(self, owner: str, path: Path):
        """Prefer the caller package's type fragments while retaining external-only types."""
        fragments = self.types.get(owner, [])
        package = self._package(path)
        local = [fragment for fragment in fragments if self._package(fragment[0]) == package]
        return local or fragments

    def _method_records(self, owner: str, name: str, path: Path):
        """Select package-local method records even when foreign namesakes are loaded."""
        fragments = self._fragments(owner, path)
        paths = {fragment[0] for fragment in fragments}
        for candidate_path, _, _ in fragments:
            self._load(candidate_path)
        return [record for record in self.methods.get((owner, name), []) if record[0] in paths]

    def _type_candidates(self, reference: str, path: Path, owner: str) -> list[str]:
        """Cache positive and negative type lookups within this immutable source index."""
        key = (path, owner, reference)
        if key not in self.type_candidates_cache:
            self.type_candidates_cache[key] = tuple(self._lookup_type_candidates(reference, path, owner))
        return list(self.type_candidates_cache[key])

    def _lookup_type_candidates(self, reference: str, path: Path, owner: str) -> list[str]:
        """Resolve lexical namespaces and imports before conservative same-name candidates."""
        unit = self.units[path]
        aliases = {alias: target for static, alias, target in unit["imports"] if alias}
        head, separator, tail = reference.partition(".")
        reference = aliases.get(head, head) + (separator + tail if separator else "")
        if reference.startswith("global::"):
            return [reference[8:]] if reference[8:] in self.types else []
        if reference in {"this", "base"}:
            return [owner]
        scope = owner.split(".")[:-1]
        for size in range(len(scope), -1, -1):
            candidate = ".".join(scope[:size] + [reference])
            if candidate in self.types:
                return [candidate]
        imported = sorted({
            target + "." + reference for static, alias, target in unit["imports"]
            if not static and not alias and target + "." + reference in self.types
        })
        return imported or sorted(name for name in self.types if name.endswith("." + reference))

    def _receiver_types(self, path: Path, owner: str, method):
        """Cache declared and constructor receiver types without losing helper return types."""
        if method is None:
            return {}
        key = (path, method.start)
        if key not in self.receivers:
            unit = self.units[path]
            local_types = _flow_module._local_types(unit["text"], method)
            self.receivers[key] = local_types
            masked_body = unit["masked"][method.body_start : method.body_end]
            trusted_method = replace(method, signature=unit["masked"][method.start : method.body_start])
            trusted_types = _flow_module._local_types(unit["masked"], trusted_method)
            declarations = [match for match in re.finditer(
                r"\bvar\s+([A-Za-z_]\w*)\s*=\s*([^;]+);", unit["text"][method.body_start : method.body_end]
            ) if masked_body[match.start() : match.start() + 3] == "var"]
            for declaration in declarations:
                constructor = re.match(
                    r"new\s+((?:global::)?[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)\s*\(",
                    masked_body[declaration.start(2) : declaration.end(2)].strip(),
                )
                if constructor:
                    local_types[declaration.group(1)] = trusted_types[declaration.group(1)] = constructor.group(1)
            writes = Counter(match.group(1) for match in re.finditer(
                r"(?<![\w.])([A-Za-z_]\w*)\s*(?:\?\?=|>>>?=|<<=|[-+*/%&|^]=|=(?!=|>)|\+\+|--)", masked_body
            ))
            parameters = trusted_method.signature.split("(", 1)[1].rsplit(")", 1)[0]
            writes.update(
                parameter.split("=", 1)[0].strip().split()[-1]
                for parameter in _split_arguments(parameters) if parameter.strip()
            )
            self.clr_builder_receivers[key] = {
                name for name, reference in trusted_types.items()
                if writes[name] <= 1 and self._is_clr_builder(reference, path) and not self._type_candidates(reference, path, owner)
            }
            changed = True
            while changed:
                changed = False
                for declaration in declarations:
                    name, value = declaration.groups()
                    if local_types.get(name):
                        continue
                    inferred = local_types.get(value.strip(), "")
                    call = re.match(r"((?:global::)?[A-Za-z_]\w*(?:\.[A-Za-z_]\w*)*)\s*\(", value.strip())
                    if call:
                        returns = {record[2].return_type for record in self._resolve(call.group(1), path, owner, method)}
                        inferred = next(iter(returns)) if len(returns) == 1 else ""
                    if inferred:
                        local_types[name] = inferred
                        changed = True
        return self.receivers[key]

    def _unresolved_candidates(self, name: str):
        """Retain repository candidates when an instance receiver cannot be proved."""
        if name not in self.fallbacks:
            if self.declaration_files is None:
                self.declaration_files = {}
                declaration = re.compile(r"(?=\b(?P<name>[A-Za-z_]\w*)\s*\([^;{}]*\)\s*(?:\{|=>))")
                for path, unit in self.units.items():
                    names = {match.group("name") for match in declaration.finditer(unit["masked"])}
                    for candidate in names:
                        self.declaration_files.setdefault(candidate, []).append(path)
            for path in self.declaration_files.get(name, []):
                self._load(path)
            self.fallbacks[name] = [record for (_, terminal), records in self.methods.items() if terminal == name for record in records]
        return self.fallbacks[name]

    def _is_clr_builder(self, reference: str, path: Path) -> bool:
        """Recognize the sealed CLR builder only through qualified or imported type evidence."""
        imports = self.units[path]["imports"]
        aliases = {alias: target for static, alias, target in imports if alias}
        head, separator, tail = reference.partition(".")
        reference = aliases.get(head, head) + (separator + tail if separator else "")
        reference = reference.removeprefix("global::")
        return reference == "System.Text.StringBuilder" or (
            reference == "StringBuilder"
            and any(not static and not alias and target == "System.Text" for static, alias, target in imports)
        )

    def _resolve(self, name: str, path: Path, owner: str, method=None):
        """Resolve referenced methods while retaining every plausible overload."""
        qualifier, separator, terminal = name.rpartition(".")
        local_types = self._receiver_types(path, owner, method)
        receiver = qualifier.removeprefix("global::").split(".", 1)[0]
        instance = receiver in local_types
        declared = local_types.get(qualifier, "")
        reference = declared if instance else qualifier
        types = self._type_candidates(reference, path, owner) if separator and reference else ([] if separator else [owner])
        if not separator:
            if not self._method_records(owner, terminal, path):
                types = []
                for static, alias, target in self.units[path]["imports"]:
                    if static:
                        types.extend(self._type_candidates(target, path, owner))
        candidates = []
        for candidate in types:
            candidates.extend(self._method_records(candidate, terminal, path))
        primitives = {"string", "bool", "char", "int", "long", "float", "double", "decimal"}
        framework = receiver in {
            "System", "Microsoft", "UnityEngine", "File", "Directory", "Path", "Array", "Math", "Console",
            "String", "Enumerable", "Task", "Regex", "Encoding", "StringBuilder", "Environment",
        }
        if not candidates and separator and (instance or not framework):
            if declared not in primitives and qualifier not in primitives:
                candidates = self._unresolved_candidates(terminal)
                if instance and not types and qualifier in self.clr_builder_receivers.get((path, method.start), ()):
                    candidates = [record for record in candidates if re.search(r"\bstatic\b", record[2].signature)]
        return candidates

    def _fields(self, owner: str, path: Path):
        """Merge partial initializer definitions with their declaring source imports."""
        key = (self._package(path), owner)
        if key not in self.fields:
            definitions = []
            origins = {}
            fragments = [fragment for fragment in self.types.get(owner, []) if self._package(fragment[0]) == key[0]]
            for source, begin, _ in sorted(fragments, key=lambda item: item[0].relative_to(self.root).as_posix()):
                self._load(source)
                for definition in self.units[source]["fields"].get(begin, []):
                    definitions.append(definition)
                    origins[id(definition)] = source
            self.fields[key] = (definitions, origins)
        return self.fields[key]

    def _calls(self, expression: str, path: Path, owner: str, method=None):
        """Yield method records selected by calls in an actual dependency expression."""
        masked = _strip_csharp_comments_and_strings(expression)
        pattern = re.compile(r"(?<![\w.])(?:global\s*::\s*)?(?:[A-Za-z_]\w*\s*\.\s*)*[A-Za-z_]\w*\s*\(")
        for match in pattern.finditer(masked):
            name = re.sub(r"\s+", "", masked[match.start() : match.end() - 1])
            if name in _flow_module.CONTROL_KEYWORDS:
                continue
            end = _balanced_call_end(masked, match.end() - 1)
            if end is not None:
                yield from self._resolve(name, path, owner, method)

    def _node_work(self, record):
        """Memoize one immutable node's piece, ordered field origins, and direct callees."""
        key = (record[0], record[2].start)
        if key not in self.node_work_cache:
            path, owner, method = record
            unit = self.units[path]
            body = unit["text"][method.start : method.body_end + 1]
            definitions, origins = self._fields(owner[2], path)
            limit = max((definition[0] for definition in definitions), default=0) + 1
            fields = _dependency_definitions(body, limit, definitions, {})
            state = "\n".join(definition[2] for definition in fields)
            piece = owner[2] + ":" + _normalize_csharp_expression(body) + "|state:" + state
            outgoing = list(self._calls(body, path, owner[2], method))
            selected_origins = []
            for definition in fields:
                origin = origins[id(definition)]
                selected_origins.append(origin)
                outgoing.extend(self._calls(definition[2], origin, owner[2]))
            self.node_work_cache[key] = (piece, tuple(selected_origins), tuple(outgoing))
        return self.node_work_cache[key]

    def _closure(self, record):
        """Hash transitive helper bodies and referenced class initializer dependencies."""
        key = (record[0], record[2].start)
        if key in self.closures:
            return self.closures[key]
        pending = [record]
        visited = set()
        pieces = set()
        paths = set()
        while pending:
            path, owner, method = pending.pop()
            identity = (path, method.start)
            if identity in visited:
                continue
            visited.add(identity)
            paths.add(path)
            piece, origins, outgoing = self._node_work((path, owner, method))
            pieces.add(piece)
            paths.update(origins)
            pending.extend(outgoing)
        result = (pieces, paths)
        self.closures[key] = result
        return result

    def signature(self, path: Path, method, expression: str) -> str:
        """Sign only external helper dependencies of one source assertion."""
        if method is None:
            return ""
        unit = self.units[path]
        owner = min(
            (region for region in unit["regions"] if region[0] < method.body_start < region[1]),
            key=lambda region: region[1] - region[0], default=None,
        )
        if owner is None:
            return ""
        selected_closures = []
        roots = set()
        for record in self._calls(expression, path, owner[2], method):
            selected_closures.append(self._closure(record))
            roots.add((record[0], record[2].start))
        key = (path, frozenset(roots))
        if key in self.signature_cache:
            return self.signature_cache[key]
        pieces = set()
        paths = set()
        for selected, selected_paths in selected_closures:
            pieces.update(selected)
            paths.update(selected_paths)
        result = hashlib.sha256("\n".join(sorted(pieces)).encode()).hexdigest() if paths - {path} else ""
        self.signature_cache[key] = result
        return result


def _unresolved_reason(
    call: dict[str, object],
    target: str,
    source_text: str,
    masked: str,
    method_infos: list[object] | None = None,
) -> str:
    """Explain why a source assertion target could not be resolved."""
    if "<directory>" in target:
        return "directory_target"
    if "<unresolved>" not in target:
        return ""
    call_text = str(call.get("text", ""))
    if ".ToFullString" in call_text:
        return "roslyn_syntax"
    if re.search(r'"(?:/|[A-Za-z]:[\\/]|[^"\\r\\n]*\.\.[\\/])', call_text):
        return "outside_root"
    receiver = str(call.get("receiver") or "")
    start = int(call.get("start", 0))
    if receiver:
        methods = method_infos or _flow_module._method_infos(source_text, masked)
        method = _flow_module._method_for_offset(methods, start)
        if method is not None:
            signature = source_text[method.start : method.body_start]
            if re.search(rf"\b{re.escape(receiver.split('.', 1)[0])}\b", signature):
                return "parameter_without_caller"
        prefix = masked[:start]
        if re.search(rf"\b{re.escape(receiver.split('.', 1)[0])}\s*=", prefix):
            return "reassigned"
    if READER_CALL_PATTERN.search(call_text):
        return "dynamic_path"
    return "unsupported_reader"


def scan_file(
    path: Path,
    root: Path,
    xunit_index: dict[str, list[str]],
    shared_paths: dict[str, str] | None = None,
    comment_cache: dict[str, tuple[str, str]] | None = None,
    partial_harness_signature: str | None = None,
    external_helpers: _ExternalHelperIndex | None = None,
    partial_termination_barriers: dict[str, str] | None = None,
) -> list[dict[str, object]]:
    """Collect source-text assertions from one validation file."""
    lines = path.read_text(encoding="utf-8").splitlines()
    paths = _source_targets(lines, include_variables=False)
    paths.update(shared_paths or {})
    local_symbols = _scoped_source_symbols(lines, paths)
    scopes = _brace_paths(lines)
    comment_cache = comment_cache if comment_cache is not None else {}
    for _, _, name, target in local_symbols:
        paths[name] = target
    source_text = "\n".join(lines)
    masked_file = _strip_csharp_comments_and_strings(source_text)
    assertion_calls = _call_spans(lines, ASSERTION_CALL_PATTERN, source_text, masked_file)
    receiver_calls = _call_spans(lines, RECEIVER_PATTERN, source_text, masked_file)
    derived_calls = _call_spans(lines, DERIVED_METHOD_PATTERN, source_text, masked_file)
    harness_calls = _call_spans(lines, HARNESS_CALL_PATTERN, source_text, masked_file)
    for call in receiver_calls + derived_calls + assertion_calls:
        call.update(text_file=source_text, masked=masked_file)
    validation_flag, dispatch = _validation_metadata(root, path.relative_to(root).as_posix())
    entrypoint = dispatch.rsplit("|", 1)[-1] if dispatch else ""
    method_infos = _flow_module._method_infos(source_text, masked_file)
    if external_helpers is not None:
        external_helpers.units[path]["methods"] = method_infos
    argument_definitions = _flow_module._argument_definition_index(source_text, masked_file, method_infos)
    reachability = _flow_module._method_call_contexts(
        source_text,
        masked_file,
        method_infos,
        receiver_calls + derived_calls + assertion_calls + harness_calls,
        argument_definitions,
    )
    local_method_names = {method.name for method in method_infos}
    for method in method_infos:
        for name, open_index in _flow_module._call_sites(source_text, masked_file, method):
            terminal = name.rsplit(".", 1)[-1].rsplit("::", 1)[-1]
            digest = (partial_termination_barriers or {}).get(terminal)
            if digest is None or terminal in local_method_names:
                continue
            end = _flow_module._balanced_call_end(masked_file, open_index)
            if end is None:
                continue
            descriptor = f"before:terminate-partial:{terminal}:helper({digest})"
            for position in list(reachability):
                if position <= end or _flow_module._method_for_offset(method_infos, position) is not method:
                    continue
                reachability[position] = "|".join(
                    value for value in (reachability[position], descriptor) if value
                )
    _, call_paths = _flow_module._call_path_index(
        source_text,
        masked_file,
        entrypoint,
        reachability,
        method_infos,
        argument_definitions,
    )
    call_bindings = _flow_module._ambiguous_call_bindings(source_text, masked_file, method_infos)
    harness_signature = _validation_harness_signature(
        source_text,
        masked_file,
        method_infos,
        entrypoint,
        argument_definitions,
        reachability,
    )
    if partial_harness_signature:
        harness_signature = partial_harness_signature
    legacy_harness_signature = _legacy_validation_harness_signature(
        source_text, masked_file
    )
    receiver_ends = {int(call["end"]) for call in receiver_calls}
    candidates: list[tuple[int, dict[str, object], str]] = []
    for call in receiver_calls:
        if call.get("receiver") == "Assert":
            continue
        line = int(call["start_line"])
        receiver_name = str(call["receiver"]).split(".", 1)[0]
        target = _lookup_symbol(local_symbols, receiver_name, line, scopes[line])
        if target is None and receiver_name in {name for _, _, name, _ in local_symbols}:
            target = "<unresolved>"
        if target is not None:
            candidates.append((int(call["start"]), call, target))
    for call in derived_calls:
        if int(call["end"]) in receiver_ends:
            continue
        target = _resolve_derived_target(lines, call, paths, local_symbols, scopes)
        if target is not None:
            candidates.append((int(call["start"]), call, target))
    for call in assertion_calls:
        line = int(call["start_line"])
        target = _resolve_assertion_target(call, paths, local_symbols, scopes)
        if target is not None:
            candidates.append((int(call["start"]), call, target))
    rows: list[dict[str, object]] = []
    for _, call, target in sorted(candidates, key=lambda item: item[0]):
        index = int(call["start_line"])
        token_text = str(call["text"]).strip()
        classification_text = _call_context(lines, call)
        target_parts = []
        for part in target.split(";"):
            candidate = (root / part).resolve() if part and part != "<unresolved>" else None
            if candidate is not None and candidate.is_dir():
                target_parts.append(f"<directory>:{part}")
            elif part:
                target_parts.append(part)
        target = ";".join(dict.fromkeys(target_parts)) or "<unresolved>"
        unresolved_reason = _unresolved_reason(
            call,
            target,
            source_text,
            masked_file,
            method_infos,
        )
        containing_method = _flow_module._method_for_offset(
            method_infos,
            int(call["start"]),
        )
        call_path = (
            call_paths.get(containing_method.identity, "UNKNOWN")
            if containing_method is not None
            else "UNKNOWN"
        )
        reader_signature = _reader_expression_signature(
            source_text, call, method_infos, argument_definitions, reachability
        )
        label_match = PHASE_LABEL_PATTERN.search("".join(lines[max(0, index - 5) : index + 1]))
        containing_harness = min(
            (
                candidate
                for candidate in harness_calls
                if int(candidate["start"]) <= int(call["start"]) < int(candidate["end"])
            ),
            key=lambda candidate: int(candidate["end"]) - int(candidate["start"]),
            default=None,
        )
        checked_expression = _checked_expression_signature(
            token_text, str(containing_harness["text"]) if containing_harness else None
        )
        row_harness_signature = harness_signature
        if external_helpers is not None:
            helper_expression = _external_dependency_expression(
                source_text, masked_file, call, containing_method, method_infos, checked_expression,
                containing_harness, harness_calls, argument_definitions, reachability, call_path,
            )
            external_signature = external_helpers.signature(path, containing_method, helper_expression)
            if external_signature:
                row_harness_signature = hashlib.sha256(
                    f"{harness_signature}|external:{external_signature}".encode()
                ).hexdigest()
        assertion_signature = _normalize_csharp_expression(token_text)
        if containing_method is not None:
            control_signature = _local_dependency_signature(
                checked_expression,
                int(call["start"]),
                argument_definitions.get(containing_method.body_start, []),
                reachability,
            )
            if control_signature:
                assertion_signature += f"|control:{control_signature}"
        if containing_harness is None:
            consumer_signature = _predicate_consumer_signature(
                source_text, masked_file, call, method_infos, reachability, argument_definitions
            )
            if consumer_signature:
                assertion_signature += f"|consumer:{consumer_signature}"
        rows.append(
            {
                "file": path.relative_to(root).as_posix(),
                "line": index + 1,
                "label": label_match.group("label") if label_match else "",
                "validation_flag": validation_flag,
                "dispatch": dispatch,
                "parser_version": PARSER_SEMANTICS_VERSION,
                "class": classify(target, classification_text),
                "target": target,
                "unresolved_reason": unresolved_reason,
                "xunit_coverage": _xunit_coverage(target, root, xunit_index),
                "token": token_text,
                "assertion_signature": assertion_signature,
                "reader_signature": reader_signature,
                "comment_only": _target_comment_only(target, token_text, root, comment_cache),
                "checked_expression": checked_expression,
                "assertion_context": _assertion_context(
                    token_text, str(containing_harness["text"]) if containing_harness else None
                ),
                "assertion_literal": _assertion_literal(token_text),
                "assertion_polarity": _assertion_polarity(
                    token_text, str(containing_harness["text"]) if containing_harness else None
                ),
                "harness_signature": row_harness_signature,
                "legacy_harness_signature": legacy_harness_signature,
                "reachability": reachability.get(int(call["start"]), ""),
                "call_path": call_path,
                "call_binding": (
                    call_bindings.get(containing_method.identity, "")
                    if containing_method is not None
                    else ""
                ),
            }
        )
    return rows


def _external_dependency_expression(
    text: str, masked: str, call: dict[str, object], method, methods: list[object], checked: str,
    harness, harness_calls: list[dict[str, object]], definitions: dict[int, list[tuple[int, str, str, str]]],
    contexts: dict[int, str], call_path: str,
) -> str:
    """Select reader definitions and actual assertion consumers for external binding."""
    if method is None:
        return ""
    offset = int(call["start"])
    receiver = str(call.get("receiver") or "").split(".", 1)[0]
    local_definitions = definitions.get(method.body_start, [])
    selected = _dependency_definitions(checked + " " + receiver, offset, local_definitions, contexts)
    expressions = [checked, call_path] + [definition[2] for definition in selected]
    consumers = [harness] if harness else []
    if not harness:
        begin = max(masked.rfind(";", method.body_start, offset), masked.rfind("{", method.body_start, offset)) + 1
        assignment = re.search(r"([A-Za-z_]\w*)\s*=(?!=)\s*$", masked[begin:offset])
        if assignment:
            names = {assignment.group(1)}
            for position, name, _, references in local_definitions:
                if position > offset and names.intersection(re.findall(r"\b[A-Za-z_]\w*\b", references)):
                    names.add(name)
            consumers.extend(
                candidate for candidate in harness_calls
                if int(candidate["start"]) > offset
                and _flow_module._method_for_offset(methods, int(candidate["start"])) is method
                and names.intersection(re.findall(r"\b[A-Za-z_]\w*\b", _strip_csharp_comments_and_strings(str(candidate["text"]))))
            )
    for consumer in consumers:
        start = int(consumer["start"])
        qualifier = re.search(r"(?:global\s*::\s*)?(?:[A-Za-z_]\w*\s*\.\s*)+$", masked[method.body_start:start])
        if qualifier:
            start = method.body_start + qualifier.start()
        expressions.append(text[start : int(consumer["end"])])
    return "\n".join(expressions)


def _predicate_consumer_signature(
    text: str,
    masked: str,
    call: dict[str, object],
    methods: list[object],
    contexts: dict[int, str],
    argument_definitions: dict[int, list[tuple[int, str, str, str]]],
) -> str:
    """Bind a stored source predicate to subsequent uses and assertion consumers."""
    start = int(call["start"])
    method = _flow_module._method_for_offset(methods, start)
    if method is None:
        return ""
    statement_start = max(
        masked.rfind(";", method.body_start, start),
        masked.rfind("{", method.body_start, start),
    ) + 1
    assignment = re.search(
        r"(?<![\w.])(?P<name>[A-Za-z_]\w*)\s*=(?!=)\s*[^;{}]*$",
        masked[statement_start:start],
    )
    statement_end = masked.find(";", int(call["end"]), method.body_end)
    if assignment is None or statement_end < 0:
        return ""
    names = {assignment.group("name")}
    pieces = [text[statement_start : statement_end + 1]]
    cursor = statement_end + 1
    for statement in re.finditer(r"[^;]+;", masked[cursor : method.body_end]):
        begin = cursor + statement.start()
        end = cursor + statement.end()
        references = re.search(
            r"\b(?:" + "|".join(re.escape(name) for name in sorted(names)) + r")\b",
            statement.group(),
        )
        if references is None or _flow_module._method_for_offset(
            methods, begin + references.start()
        ) is not method:
            continue
        prefix_length = re.match(r"[\s}]*", statement.group()).end()
        pieces.append(text[begin + prefix_length : end])
        forwarded = re.search(r"(?<![\w.])([A-Za-z_]\w*)\s*=(?!=)", statement.group())
        if forwarded:
            names.add(forwarded.group(1))
        for position in contexts:
            if not begin <= position < end:
                continue
            pieces.append(contexts.get(position, ""))
            pieces.append(
                _local_dependency_signature(
                    text[begin:end], position, argument_definitions.get(method.body_start, []), contexts
                )
            )
    normalized = _normalize_csharp_expression("\n".join(pieces))
    return hashlib.sha256(normalized.encode()).hexdigest()


def inventory(root: Path) -> list[dict[str, object]]:
    """Scan runtime and unit C# validation files under every package."""
    _text_module._clear_text_caches()
    _symbols_module._clear_symbol_caches()
    _VALIDATION_METADATA_CACHE.clear()
    rows: list[dict[str, object]] = []
    xunit_index = _build_xunit_index(root)
    files = sorted(
        {
            path
            for pattern in ("Packages/*/Tests/Runtime/**/*.cs", "Packages/*/Tests/Unit/**/*.cs")
            for path in root.glob(pattern)
        }
    )
    scopes = _shared_scope_paths(files)
    external_helpers = _ExternalHelperIndex(root)
    partial_signatures: dict[Path, str] = {}
    partial_termination_barriers: dict[Path, dict[str, str]] = {}
    partial_groups: dict[tuple[tuple[str, ...], str], list[Path]] = {}
    for path in files:
        unit = external_helpers.units[path]
        source = unit["masked"]
        package_key = tuple(path.parts[: path.parts.index("Tests")]) if "Tests" in path.parts else ()
        for match in re.finditer(r"\bpartial\s+class\s+(?P<name>[A-Za-z_]\w*)", source):
            body_start = source.find("{", match.end())
            qualified = next((name for begin, _, name in unit["regions"] if begin == body_start), match.group("name"))
            partial_groups.setdefault((package_key, qualified), []).append(path)
    for group_paths in partial_groups.values():
        if len(group_paths) < 2:
            continue
        combined = "\n".join(
            path.read_text(encoding="utf-8", errors="replace")
            for path in sorted(set(group_paths), key=lambda item: item.relative_to(root).as_posix())
        )
        masked = _strip_csharp_comments_and_strings(combined)
        methods = _flow_module._method_infos(combined, masked)
        barriers = _flow_module._partial_termination_barriers(combined, masked, methods)
        entrypoints = set()
        for path in group_paths:
            _, dispatch = _validation_metadata(root, path.relative_to(root).as_posix())
            if dispatch:
                entrypoints.update(dispatch.rsplit("|", 1)[-1].split(";"))
        signature = _validation_harness_signature(
            combined, masked, methods, ";".join(sorted(entrypoints))
        )
        for path in group_paths:
            partial_signatures[path] = signature
            partial_termination_barriers[path] = barriers
    comment_cache: dict[str, tuple[str, str]] = {}
    for path in files:
        rows.extend(
            scan_file(
                path,
                root,
                xunit_index,
                scopes.get(path),
                comment_cache,
                partial_signatures.get(path),
                external_helpers,
                partial_termination_barriers.get(path),
            )
        )
    return rows


def _validation_harness_signature(
    text: str,
    masked: str | None = None,
    methods: list[object] | None = None,
    entrypoint: str = "",
    argument_definitions: dict[int, list[tuple[int, str, str, str]]] | None = None,
    call_contexts: dict[int, str] | None = None,
) -> str:
    """Hash local assertion and source-reader helper implementations."""
    return _flow_module._validation_harness_signature(
        text, masked, methods, entrypoint, argument_definitions, call_contexts
    )


def _legacy_validation_harness_signature(
    text: str, masked: str | None = None
) -> str:
    """Hash harness bodies with the pre-migration parser semantics."""
    masked = masked if masked is not None else _strip_csharp_comments_and_strings(text)
    pattern = re.compile(
        r"\b(?:Check|Read[A-Za-z_]\w*|Source[A-Za-z_]\w*|Validate(?:[A-Za-z_]\w*)?)\s*"
        r"\([^;{}]*\)\s*\{"
    )
    pieces: list[str] = []
    for match in pattern.finditer(masked):
        brace = masked.find("{", match.start(), match.end())
        end = _balanced_brace_end(masked, brace)
        if end is not None:
            pieces.append(text[match.start() : end + 1])
    return hashlib.sha256("\n".join(pieces).encode()).hexdigest() if pieces else ""


def _validation_metadata(root: Path, source_file: str) -> tuple[str, str]:
    """Resolve a validation file to its registry flag and dispatch metadata."""
    cache_key = (str(root.resolve()), source_file)
    cached = _VALIDATION_METADATA_CACHE.get(cache_key)
    if cached is not None:
        return cached
    stem = Path(source_file).stem
    registry = (
        root
        / "Packages"
        / "dev.unity2foxglove.sdk"
        / "Tests"
        / "Runtime"
        / "PhaseValidationRegistry.cs"
    )
    if registry.is_file():
        text = registry.read_text(encoding="utf-8", errors="replace")
        entry_start = re.compile(
            r"^\s*(?P<kind>Ci|Local|DefaultOnly)\(\"(?P<flag>--[A-Za-z0-9-]+)\"", re.MULTILINE
        )
        matches = list(entry_start.finditer(text))
        for index, match in enumerate(matches):
            end = matches[index + 1].start() if index + 1 < len(matches) else len(text)
            block = text[match.start() : end]
            entrypoint = re.search(rf"\b{re.escape(stem)}\.(?P<entry>Validate\w*)\b", block)
            if entrypoint:
                default = "false" if re.search(r"\bincludeInDefault\s*:\s*false\b", block) else "true"
                result = (
                    match.group("flag"),
                    f"{match.group('kind')}|{default}|{entrypoint.group('entry')}",
                )
                _VALIDATION_METADATA_CACHE[cache_key] = result
                return result
    source_path = root / source_file
    if source_path.is_file():
        source = source_path.read_text(encoding="utf-8", errors="replace")
        test_entries: list[str] = []
        source_lines = source.splitlines()
        test_attribute = re.compile(r"\[(?:Fact|Theory|Test|TestCase|UnityTest)\b")
        method_pattern = re.compile(r"\b(?P<entry>[A-Za-z_]\w*)\s*\([^;{}]*\)\s*\{")
        index = 0
        while index < len(source_lines):
            attribute_block = False
            inline_entry = ""
            cursor = index
            while cursor < len(source_lines):
                stripped = source_lines[cursor].strip()
                if not stripped or stripped.startswith("["):
                    attribute_block = attribute_block or bool(test_attribute.search(stripped))
                    if attribute_block:
                        inline_match = method_pattern.search(stripped)
                        if inline_match:
                            inline_entry = inline_match.group("entry")
                            cursor += 1
                            break
                    cursor += 1
                    continue
                break
            if inline_entry:
                test_entries.append(inline_entry)
                index = max(index + 1, cursor)
                continue
            if attribute_block:
                for candidate_line in source_lines[cursor : min(cursor + 8, len(source_lines))]:
                    match = method_pattern.search(candidate_line)
                    if match:
                        test_entries.append(match.group("entry"))
                        break
            index = max(index + 1, cursor)
        if test_entries:
            result = "<unit>", f"Unit|true|{';'.join(dict.fromkeys(test_entries))}"
            _VALIDATION_METADATA_CACHE[cache_key] = result
            return result
    result = "", ""
    _VALIDATION_METADATA_CACHE[cache_key] = result
    return result


def _reader_expression_signature(
    text: str,
    call: dict[str, object],
    methods: list[object] | None = None,
    argument_definitions: dict[int, list[tuple[int, str, str, str]]] | None = None,
    call_contexts: dict[int, str] | None = None,
) -> str:
    """Return preceding source assignments and their recursive local dependencies."""
    receiver = str(call.get("receiver") or "").split(".", 1)[0]
    if not receiver:
        return ""
    offset = int(call["start"])
    masked = str(call.get("masked") or _strip_csharp_comments_and_strings(text))
    methods = methods if methods is not None else _flow_module._method_infos(text, masked)
    method = _flow_module._method_for_offset(methods, offset)
    start = method.body_start + 1 if method is not None else 0
    assignments = re.compile(
        rf"(?<![\w.]){re.escape(receiver)}\s*"
        r"(?P<operator>\?\?=|>>>?=|<<=|[-+*/%&|^]=|=(?!=|>))(?P<expression>[^;]*);",
        re.DOTALL,
    )
    expressions: list[str] = []
    for match in assignments.finditer(masked, start, offset):
        if _flow_module._method_for_offset(methods, match.start()) != method:
            continue
        expression = _normalize_csharp_expression(
            text[match.start("expression") : match.end("expression")]
        )
        operator = match.group("operator")
        if operator == "=" and not expressions:
            expressions.append(expression)
        else:
            expressions.append(f"{operator} {expression}")
    if method is not None:
        if argument_definitions is None:
            argument_definitions = _flow_module._argument_definition_index(text, masked, methods)
        if call_contexts is None:
            call_contexts = _flow_module._method_call_contexts(
                text,
                masked,
                methods,
                [{"start": offset, "start_line": text.count("\n", 0, offset)}],
                argument_definitions,
            )
        dependency = _local_dependency_signature(
            receiver, offset, argument_definitions.get(method.body_start, []), call_contexts
        )
        if dependency:
            expressions.append(f"upstream:{dependency}")
    return "; ".join(expressions)


def main(argv: list[str] | None = None) -> int:
    """Run the source-text inventory command."""
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, default=Path.cwd())
    parser.add_argument("--output", type=Path, default=Path("build/review/source-text-inventory"))
    parser.add_argument("--base-ref")
    parser.add_argument("--parser-migration-approval", type=Path)
    args = parser.parse_args(argv)
    if args.parser_migration_approval and not args.base_ref:
        parser.error("--parser-migration-approval requires --base-ref")
    root = args.root.resolve()
    rows = inventory(root)
    write_report(rows, args.output)
    print(
        f"SOURCE_TEXT_INVENTORY_OK rows={len(rows)} "
        f"unknown={sum(row['class'] == 'UNKNOWN' for row in rows)} "
        f"unresolved={sum(row['class'] == 'UNRESOLVED' for row in rows)}"
    )
    if args.base_ref:
        return _verify_base_inventory(
            root,
            rows,
            args.base_ref,
            inventory_callback=inventory,
            migration_approval=args.parser_migration_approval,
            output=args.output,
        )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
