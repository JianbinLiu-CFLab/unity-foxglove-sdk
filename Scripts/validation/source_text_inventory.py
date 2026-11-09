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
    from . import source_text_inventory_text as _text_module
    from . import source_text_inventory_flow as _flow_module
    from . import source_text_inventory_symbols as _symbols_module
    from .source_text_inventory_text import *
    from .source_text_inventory_symbols import *
    from .source_text_inventory_flow import *
except ImportError:
    import source_text_inventory_text as _text_module
    import source_text_inventory_flow as _flow_module
    import source_text_inventory_symbols as _symbols_module
    from source_text_inventory_text import *
    from source_text_inventory_symbols import *
    from source_text_inventory_flow import *

for _module in (_text_module, _symbols_module, _flow_module):
    globals().update(
        {
            _name: getattr(_module, _name)
            for _name in dir(_module)
            if _name.startswith("_") and not _name.startswith("__")
        }
    )


def scan_file(
    path: Path,
    root: Path,
    xunit_index: dict[str, list[str]],
    shared_paths: dict[str, str] | None = None,
    comment_cache: dict[str, tuple[str, str]] | None = None,
) -> list[dict[str, object]]:
    """Collect source-text assertions from one validation file."""
    lines = path.read_text(encoding="utf-8").splitlines()
    paths = dict(shared_paths or {})
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
    harness_signature = _validation_harness_signature(source_text)
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
                "xunit_coverage": _xunit_coverage(target, root, xunit_index),
                "token": token_text,
                "comment_only": _target_comment_only(target, token_text, root, comment_cache),
                "checked_expression": _checked_expression_signature(
                    token_text, str(containing_harness["text"]) if containing_harness else None
                ),
                "harness_signature": harness_signature,
                "reachability": reachability.get(int(call["start"]), ""),
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


def _inventory_at_revision(
    root: Path, revision: str, use_current_parser: bool = False
) -> list[dict[str, object]]:
    """Materialize a Git revision under build/ and scan it with the current inventory rules."""
    build_root = root / "build" / "review"
    build_root.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(
        prefix="source-text-inventory-base-", dir=build_root
    ) as directory:
        process = subprocess.Popen(
            ["git", "archive", revision],
            cwd=root,
            stdout=subprocess.PIPE,
        )
        assert process.stdout is not None
        with tarfile.open(fileobj=process.stdout, mode="r|") as archive:
            archive.extractall(directory)
        if process.wait() != 0:
            raise RuntimeError(f"git archive failed for {revision}")
        script_path = Path(directory) / "Scripts" / "validation" / "source_text_inventory.py"
        spec = importlib.util.spec_from_file_location("source_text_inventory_base", script_path)
        if spec is None or spec.loader is None:
            raise RuntimeError(f"base inventory script is missing at {script_path}")
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        return (
            inventory(Path(directory)) if use_current_parser else module.inventory(Path(directory))
        )


def _token_signatures(token: str) -> set[tuple[str, str]]:
    """Return method/literal anchors that survive line and parser changes."""
    signatures: set[tuple[str, str]] = set()
    for match in re.finditer(r"\.(Contains|IndexOf|DoesNotContain)\s*\(", token):
        literal = re.match(r"\s*\"((?:\\.|[^\"\\])*)\"", token[match.end() :])
        signatures.add((match.group(1), literal.group(1) if literal else ""))
    nonempty = {item for item in signatures if item[1]}
    return nonempty or signatures or {("", re.sub(r"\s+", " ", token).strip())}


def _checked_expression_signature(token: str, harness: str | None = None) -> str:
    """Return the normalized expression whose value is checked by an assertion."""
    if harness:
        match = re.search(r"\bCheck\s*\(|\bAssert\.(?:True|False)\s*\(", harness)
        if match:
            open_index = harness.find("(", match.start(), match.end())
            close_index = _balanced_call_end(harness, open_index)
            if close_index is not None:
                return re.sub(
                    r"\s+",
                    " ",
                    _split_first_argument(harness[open_index + 1 : close_index]).strip(),
                )
    match = re.search(r"\bAssert\.(?:Contains|DoesNotContain)\s*\(", token)
    if match:
        open_index = token.find("(", match.start(), match.end())
        close_index = _balanced_call_end(token, open_index)
        if close_index is not None:
            arguments = token[open_index + 1 : close_index]
            first = _split_first_argument(arguments)
            first_offset = arguments.find(first)
            comma = arguments.find(",", first_offset + len(first)) if first_offset >= 0 else -1
            if comma >= 0:
                second = _split_first_argument(arguments[comma + 1 :])
                return re.sub(r"\s+", " ", second.strip())
    return re.sub(r"\s+", " ", token.strip())


def _validation_harness_signature(text: str) -> str:
    """Hash local assertion and source-reader helper implementations."""
    masked = _strip_csharp_comments_and_strings(text)
    pattern = re.compile(
        r"\b(?:Check|Read[A-Za-z_]\w*|Source[A-Za-z_]\w*|Validate(?:[A-Za-z_]\w*)?)\s*\([^;{}]*\)\s*\{"
    )
    pieces: list[str] = []
    for match in pattern.finditer(masked):
        brace = masked.find("{", match.start(), match.end())
        end = _balanced_brace_end(masked, brace)
        if end is not None:
            pieces.append(text[match.start() : end + 1])
    return hashlib.sha256("\n".join(pieces).encode()).hexdigest() if pieces else ""


def _row_prefix(row: dict[str, object]) -> tuple[str, str]:
    """Return the file and validation flag portion of a row identity."""
    return str(row["file"]), str(row["validation_flag"])


def _signatures_overlap(base: set[tuple[str, str]], head: set[tuple[str, str]]) -> bool:
    """Match multiline base tokens whose literal is on a continuation line."""
    return any(
        base_method == head_method
        and (not base_literal or not head_literal or base_literal == head_literal)
        for base_method, base_literal in base
        for head_method, head_literal in head
    )


def _targets_overlap(base: dict[str, object], head: dict[str, object]) -> bool:
    """Match legacy rows whose token shape changed but target stayed stable."""
    base_targets = {
        value for value in str(base["target"]).split(";") if value and value != "<unresolved>"
    }
    head_targets = {
        value for value in str(head["target"]).split(";") if value and value != "<unresolved>"
    }
    return bool(base_targets and base_targets & head_targets)


def _match_base_rows(
    base_rows: list[dict[str, object]], head_rows: list[dict[str, object]], strict: bool = False
) -> tuple[list[tuple[dict[str, object], dict[str, object]]], list[dict[str, object]]]:
    """Match base rows to head rows without relying on physical line numbers."""
    available = set(range(len(head_rows)))
    matches: list[tuple[dict[str, object], dict[str, object]]] = []
    pending = list(base_rows)
    for mode in range(3):
        next_pending: list[dict[str, object]] = []
        for base in pending:
            base_signatures = _token_signatures(str(base["token"]))
            base_token = re.sub(r"\s+", " ", str(base["token"]).strip())
            if mode == 0:
                candidate = next(
                    (
                        index
                        for index in sorted(available)
                        if str(base["file"]) == str(head_rows[index]["file"])
                        and base_token
                        == re.sub(r"\s+", " ", str(head_rows[index]["token"]).strip())
                    ),
                    None,
                )
            elif mode == 1:
                candidate = next(
                    (
                        index
                        for index in sorted(available)
                        if _row_prefix(base) == _row_prefix(head_rows[index])
                        and _signatures_overlap(
                            base_signatures, _token_signatures(str(head_rows[index]["token"]))
                        )
                    ),
                    None,
                )
            else:
                candidate = next(
                    (
                        index
                        for index in sorted(available)
                        if _row_prefix(base) == _row_prefix(head_rows[index])
                        and _targets_overlap(base, head_rows[index])
                    ),
                    None,
                )
            if candidate is None:
                next_pending.append(base)
            else:
                available.remove(candidate)
                matches.append((base, head_rows[candidate]))
        pending = next_pending
    missing = pending
    base_counts, head_counts = Counter(_row_prefix(row) for row in base_rows), Counter(
        _row_prefix(row) for row in head_rows
    )
    return matches, (
        missing
        if strict
        else [
            row
            for row in missing
            if str(row["target"]) != "<unresolved>"
            and head_counts[_row_prefix(row)] < base_counts[_row_prefix(row)]
            and not re.search(r"\b(?:warning|logLines)\b", str(row["token"]))
        ]
    )


def _verify_base_inventory(root: Path, rows: list[dict[str, object]], revision: str) -> int:
    """Reject silent row loss or new unresolved targets relative to a trusted base."""
    base_rows = _inventory_at_revision(root, revision)
    base_status_rows = _inventory_at_revision(root, revision, use_current_parser=True)
    status_matches, status_missing = _match_base_rows(base_status_rows, rows, strict=True)
    missing = status_missing
    fields = (
        "target",
        "class",
        "comment_only",
        "xunit_coverage",
        "reachability",
        "checked_expression",
        "dispatch",
        "harness_signature",
    )
    metadata_changed = [
        _row_prefix(base)
        for base, head in status_matches
        if (
            tuple(str(base.get(field, "")) for field in fields)
            + (str(_token_signatures(str(base["token"]))),)
        )
        != (
            tuple(str(head.get(field, "")) for field in fields)
            + (str(_token_signatures(str(head["token"]))),)
        )
    ]
    base_unresolved = sum(row["class"] == "UNRESOLVED" for row in base_status_rows)
    head_unresolved = sum(row["class"] == "UNRESOLVED" for row in rows)
    base_unknown = sum(row["class"] == "UNKNOWN" for row in base_status_rows)
    head_unknown = sum(row["class"] == "UNKNOWN" for row in rows)
    print(
        f"SOURCE_TEXT_INVENTORY_BASE_OK base_rows={len(base_rows)} head_rows={len(rows)} missing={len(missing)} base_unresolved={base_unresolved} head_unresolved={head_unresolved} base_unknown={base_unknown} head_unknown={head_unknown} metadata_changed={len(metadata_changed)}"
    )
    if (
        missing
        or status_missing
        or metadata_changed
        or head_unresolved > base_unresolved
        or head_unknown > base_unknown
    ):
        if missing:
            print(
                f"SOURCE_TEXT_INVENTORY_BASE_MISSING sample={[(row['file'], row['line'], row['label']) for row in missing[:5]]}"
            )
        if metadata_changed:
            print(f"SOURCE_TEXT_INVENTORY_METADATA_CHANGED sample={metadata_changed[:5]}")
        if status_missing:
            print(
                f"SOURCE_TEXT_INVENTORY_STATUS_MISSING sample={[(row['file'], row['line']) for row in status_missing[:5]]}"
            )
        return 1
    return 0


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


def write_report(rows: list[dict[str, object]], output: Path) -> None:
    """Write CSV and Markdown summary files for the inventory."""
    output.mkdir(parents=True, exist_ok=True)
    csv_path = output / "source_text_inventory.csv"
    with csv_path.open("w", encoding="utf-8", newline="") as handle:
        writer = csv.DictWriter(
            handle,
            fieldnames=(
                "file",
                "line",
                "label",
                "validation_flag",
                "dispatch",
                "class",
                "target",
                "xunit_coverage",
                "token",
                "checked_expression",
                "harness_signature",
                "comment_only",
                "reachability",
            ),
        )
        writer.writeheader()
        writer.writerows(rows)
    counts = {
        label: sum(row["class"] == label for row in rows)
        for label in ("A", "B", "C", "UNKNOWN", "UNRESOLVED")
    }
    comments = sum(bool(row["comment_only"]) for row in rows)
    by_flag: dict[str, dict[str, int]] = {}
    for row in rows:
        flag = str(row["validation_flag"] or "<unregistered>")
        summary = by_flag.setdefault(
            flag, {"total": 0, "class_c": 0, "comment_only": 0, "covered": 0}
        )
        summary["total"] += 1
        summary["class_c"] += int(row["class"] == "C")
        summary["comment_only"] += int(bool(row["comment_only"]))
        summary["covered"] += int(bool(row["xunit_coverage"]))
    ranking = "\n## Validation ranking\n\n| Flag | Tokens | Class C | xUnit-covered targets | Comment-only |\n|---|---:|---:|---:|---:|\n"
    for flag, summary in sorted(by_flag.items(), key=lambda item: (-item[1]["class_c"], item[0])):
        ranking += f"| `{flag}` | {summary['total']} | {summary['class_c']} | {summary['covered']} | {summary['comment_only']} |\n"
    file_targets = sum(
        bool(row["target"])
        and "<directory>" not in str(row["target"])
        and "<unresolved>" not in str(row["target"])
        for row in rows
    )
    directory_targets = sum("<directory>" in str(row["target"]) for row in rows)
    unresolved_targets = sum("<unresolved>" in str(row["target"]) for row in rows)
    (output / "SUMMARY.md").write_text(
        "# Source-text assertion inventory\n\n"
        f"- Total tokens: {len(rows)}\n"
        f"- Class A boundary: {counts['A']}\n"
        f"- Class B wiring: {counts['B']}\n"
        f"- Class C implementation: {counts['C']}\n"
        f"- Unknown targets: {counts['UNKNOWN']}\n"
        f"- Unresolved targets: {counts['UNRESOLVED']}\n"
        f"- Comment-only tokens: {comments}\n"
        f"- File targets: {file_targets} / {len(rows)}\n"
        f"- Directory targets: {directory_targets}\n" + ranking,
        encoding="utf-8",
    )


def main() -> int:
    """Run the source-text inventory command."""
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, default=Path.cwd())
    parser.add_argument("--output", type=Path, default=Path("build/review/source-text-inventory"))
    parser.add_argument("--base-ref")
    args = parser.parse_args()
    root = args.root.resolve()
    rows = inventory(root)
    write_report(rows, args.output)
    print(
        f"SOURCE_TEXT_INVENTORY_OK rows={len(rows)} "
        f"unknown={sum(row['class'] == 'UNKNOWN' for row in rows)} "
        f"unresolved={sum(row['class'] == 'UNRESOLVED' for row in rows)}"
    )
    if args.base_ref:
        return _verify_base_inventory(root, rows, args.base_ref)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
