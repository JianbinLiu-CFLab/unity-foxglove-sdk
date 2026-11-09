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
from pathlib import Path

MODULE_DIR = Path(__file__).resolve().parent
if str(MODULE_DIR) not in sys.path:
    sys.path.insert(0, str(MODULE_DIR))

try:
    from . import source_text_inventory_ratchet as _ratchet_module
    from . import source_text_inventory_text as _text_module
    from . import source_text_inventory_flow as _flow_module
    from . import source_text_inventory_symbols as _symbols_module
    from .source_text_inventory_text import *
    from .source_text_inventory_symbols import *
    from .source_text_inventory_flow import *
    from .source_text_inventory_ratchet import *
except ImportError:
    import source_text_inventory_ratchet as _ratchet_module
    import source_text_inventory_text as _text_module
    import source_text_inventory_flow as _flow_module
    import source_text_inventory_symbols as _symbols_module
    from source_text_inventory_text import *
    from source_text_inventory_symbols import *
    from source_text_inventory_flow import *
    from source_text_inventory_ratchet import *

for _module in (_text_module, _symbols_module, _flow_module, _ratchet_module):
    globals().update(
        {
            _name: getattr(_module, _name)
            for _name in dir(_module)
            if _name.startswith("_") and not _name.startswith("__")
        }
    )


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
    method_infos, call_paths = _flow_module._call_path_index(
        source_text,
        masked_file,
        entrypoint,
    )
    harness_signature = _validation_harness_signature(
        source_text,
        masked_file,
        method_infos,
    )
    reachability = _reachability_signatures(
        lines, receiver_calls + derived_calls + assertion_calls, source_text, masked_file
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
                target_parts.append("<directory>")
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
            call_paths.get(containing_method.name, "UNKNOWN")
            if containing_method is not None
            else "UNKNOWN"
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
        rows.append(
            {
                "file": path.relative_to(root).as_posix(),
                "line": index + 1,
                "label": label_match.group("label") if label_match else "",
                "validation_flag": validation_flag,
                "dispatch": dispatch,
                "class": classify(target, classification_text),
                "target": target,
                "unresolved_reason": unresolved_reason,
                "xunit_coverage": _xunit_coverage(target, root, xunit_index),
                "token": token_text,
                "comment_only": _target_comment_only(target, token_text, root, comment_cache),
                "checked_expression": _checked_expression_signature(
                    token_text, str(containing_harness["text"]) if containing_harness else None
                ),
                "assertion_context": _assertion_context(
                    token_text, str(containing_harness["text"]) if containing_harness else None
                ),
                "assertion_literal": _assertion_literal(token_text),
                "harness_signature": harness_signature,
                "reachability": reachability.get(int(call["start"]), ""),
                "call_path": call_path,
            }
        )
    return rows


def inventory(root: Path) -> list[dict[str, object]]:
    """Scan runtime and unit C# validation files under every package."""
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
    comment_cache: dict[str, tuple[str, str]] = {}
    for path in files:
        rows.extend(scan_file(path, root, xunit_index, scopes.get(path), comment_cache))
    return rows


def _validation_harness_signature(
    text: str,
    masked: str | None = None,
    methods: list[object] | None = None,
) -> str:
    """Hash local assertion and source-reader helper implementations."""
    return _flow_module._validation_harness_signature(text, masked, methods)


def _validation_metadata(root: Path, source_file: str) -> tuple[str, str]:
    """Resolve a validation file to its registry flag and dispatch metadata."""
    stem = Path(source_file).stem
    registry = (
        root
        / "Packages"
        / "dev.unity2foxglove.sdk"
        / "Tests"
        / "Runtime"
        / "PhaseValidationRegistry.cs"
    )
    if not registry.is_file():
        return "", ""
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
            return (
                match.group("flag"),
                f"{match.group('kind')}|{default}|{entrypoint.group('entry')}",
            )
    return "", ""


def main(argv: list[str] | None = None) -> int:
    """Run the source-text inventory command."""
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, default=Path.cwd())
    parser.add_argument("--output", type=Path, default=Path("build/review/source-text-inventory"))
    parser.add_argument("--base-ref")
    args = parser.parse_args(argv)
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
        )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
