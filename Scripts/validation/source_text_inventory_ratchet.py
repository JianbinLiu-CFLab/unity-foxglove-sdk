"""Trusted-base comparison and report generation for source-text inventory."""

from __future__ import annotations

import hashlib
import json
import re
import subprocess
import sys
import tarfile
import tempfile
from collections import Counter
from pathlib import Path
from typing import Callable

try:
    from .source_text_inventory_text import _balanced_call_end, _normalize_csharp_expression, _split_first_argument
except ImportError:
    from source_text_inventory_text import _balanced_call_end, _normalize_csharp_expression, _split_first_argument


ACKNOWLEDGEMENTS_PATH = Path("Scripts/validation/source_text_inventory_acknowledgements.json")
NEW_FIELDS = {
    "assertion_signature",
    "assertion_context",
    "assertion_literal",
    "assertion_polarity",
    "call_binding",
    "call_path",
    "legacy_harness_signature",
    "reader_signature",
    "parser_version",
    "unresolved_reason",
}
COMPARE_FIELDS = (
    "target",
    "parser_version",
    "class",
    "comment_only",
    "xunit_coverage",
    "reachability",
    "checked_expression",
    "assertion_context",
    "assertion_literal",
    "assertion_polarity",
    "assertion_signature",
    "dispatch",
    "harness_signature",
    "call_path",
    "call_binding",
    "reader_signature",
)
PARSER_COMPARE_FIELDS = tuple(field for field in COMPARE_FIELDS if field != "parser_version")
REQUIRED_ROW_FIELDS = frozenset(
    {
        "file",
        "line",
        "label",
        "validation_flag",
        "dispatch",
        "parser_version",
        "class",
        "target",
        "unresolved_reason",
        "xunit_coverage",
        "token",
        "assertion_signature",
        "reader_signature",
        "comment_only",
        "checked_expression",
        "assertion_context",
        "assertion_literal",
        "assertion_polarity",
        "harness_signature",
        "legacy_harness_signature",
        "reachability",
        "call_path",
        "call_binding",
    }
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
                return _normalize_csharp_expression(
                    _split_first_argument(harness[open_index + 1 : close_index])
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
                return _normalize_csharp_expression(second)
    return _normalize_csharp_expression(token)


def _assertion_context(token: str, harness: str | None = None) -> str:
    """Classify whether a source expression is wrapped in an executable check."""
    if harness or re.match(r"\s*Assert\.(?:Contains|DoesNotContain)\s*\(", token):
        return "checked"
    return "bare"


def _assertion_literal(token: str) -> str:
    """Return the expected literal or expression from a source assertion."""
    match = re.search(r"\.(?:Contains|IndexOf|DoesNotContain)\s*\(", token)
    if match is None:
        return ""
    open_index = token.find("(", match.start(), match.end())
    close_index = _balanced_call_end(token, open_index)
    if close_index is None:
        return ""
    first = _split_first_argument(token[open_index + 1 : close_index]).strip()
    literal = re.fullmatch(r'"((?:\\.|[^"\\])*)"', first)
    return literal.group(1) if literal else re.sub(r"\s+", " ", first)


def _assertion_polarity(token: str, harness: str | None = None) -> str:
    """Return the outer assertion operator for a source expression."""
    if harness:
        match = re.search(r"\b(?:Check|Assert\.(?:True|False))\s*\(", harness)
        if match:
            return re.sub(r"\s+", "", match.group(0)[:-1])
    match = re.search(r"\bAssert\.(?:Contains|DoesNotContain)\s*\(", token)
    if match:
        return re.sub(r"\s+", "", match.group(0)[:-1])
    return "bare"


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
    base_counts = Counter(_row_prefix(row) for row in base_rows)
    head_counts = Counter(_row_prefix(row) for row in head_rows)
    if strict:
        return matches, missing
    return matches, [
        row
        for row in missing
        if str(row["target"]) != "<unresolved>"
        and head_counts[_row_prefix(row)] < base_counts[_row_prefix(row)]
        and not re.search(r"\b(?:warning|logLines)\b", str(row["token"]))
    ]


def _checker_paths(root: Path, revision: str | None = None) -> list[str]:
    """Return source-inventory checker paths from a worktree or Git revision."""
    if revision is None:
        return sorted(
            path.relative_to(root).as_posix()
            for path in root.glob("Scripts/validation/source_text_inventory*.py")
        )
    completed = subprocess.run(
        ["git", "ls-tree", "-r", "--name-only", revision, "--", "Scripts/validation"],
        cwd=root,
        check=True,
        text=True,
        stdout=subprocess.PIPE,
    )
    return sorted(
        line.strip()
        for line in completed.stdout.splitlines()
        if line.strip().startswith("Scripts/validation/source_text_inventory")
        and line.strip().endswith(".py")
    )


def _checker_digest(root: Path, revision: str | None = None) -> str:
    """Hash checker module paths and contents for parser-equality decisions."""
    digest = hashlib.sha256()
    for path in _checker_paths(root, revision):
        data = (
            subprocess.run(
                ["git", "show", f"{revision}:{path}"],
                cwd=root,
                check=True,
                stdout=subprocess.PIPE,
            ).stdout
            if revision
            else (root / path).read_bytes()
        )
        data = data.replace(b"\r\n", b"\n")
        digest.update(path.encode())
        digest.update(b"\0")
        digest.update(data)
    return digest.hexdigest()


def _inventory_in_isolated_process(tree: Path) -> list[dict[str, object]]:
    """Scan an archived tree without importing head checker modules."""
    runner = (
        "import json, sys\n"
        "from pathlib import Path\n"
        "tree = Path(sys.argv[1])\n"
        "sys.path.insert(0, str(tree))\n"
        "from Scripts.validation.source_text_inventory import inventory\n"
        "json.dump(inventory(tree), sys.stdout)\n"
    )
    completed = subprocess.run(
        [sys.executable, "-I", "-c", runner, str(tree)],
        cwd=tree,
        check=False,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
    )
    if completed.returncode != 0:
        raise RuntimeError(
            "isolated base inventory failed: "
            f"{completed.stderr.strip() or completed.stdout.strip()}"
        )
    rows = json.loads(completed.stdout)
    if not isinstance(rows, list):
        raise RuntimeError("isolated base inventory returned a non-list")
    return rows


def _archive_revision(root: Path, revision: str, destination: Path) -> None:
    """Extract one Git revision with safe tarfile filtering when available."""
    process = subprocess.Popen(["git", "archive", revision], cwd=root, stdout=subprocess.PIPE)
    assert process.stdout is not None
    with tarfile.open(fileobj=process.stdout, mode="r|") as archive:
        try:
            archive.extractall(destination, filter="data")
        except TypeError:
            archive.extractall(destination)
        process.stdout.read()
    process.stdout.close()
    if process.wait() != 0:
        raise RuntimeError(f"git archive failed for {revision}")


def _inventory_at_revision(
    root: Path,
    revision: str,
    use_current_parser: bool = False,
    inventory_callback: Callable[[Path], list[dict[str, object]]] | None = None,
) -> list[dict[str, object]]:
    """Materialize a Git revision and scan it with the selected parser."""
    build_root = root / "build" / "review"
    build_root.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(
        prefix="source-text-inventory-base-", dir=build_root
    ) as directory:
        tree = Path(directory)
        _archive_revision(root, revision, tree)
        if use_current_parser:
            if inventory_callback is None:
                raise RuntimeError("current inventory callback is required")
            return inventory_callback(tree)
        return _inventory_in_isolated_process(tree)


def _load_acknowledgements(
    root: Path, revision: str | None = None
) -> tuple[list[dict[str, object]], list[str]]:
    """Read and validate exact base-to-head acknowledgement entries."""
    if revision:
        completed = subprocess.run(
            ["git", "show", f"{revision}:{ACKNOWLEDGEMENTS_PATH.as_posix()}"],
            cwd=root,
            check=False,
            capture_output=True,
        )
        if completed.returncode != 0:
            return [], []
        raw = completed.stdout.decode("utf-8")
    else:
        path = root / ACKNOWLEDGEMENTS_PATH
        if not path.is_file():
            return [], []
        raw = path.read_text(encoding="utf-8")
    try:
        data = json.loads(raw)
    except json.JSONDecodeError:
        return [], ["acknowledgements must contain valid JSON"]
    entries = data.get("acknowledgements", data) if isinstance(data, dict) else data
    if not isinstance(entries, list):
        return [], ["acknowledgements must be a list"]
    errors: list[str] = []
    seen: set[tuple[object, ...]] = set()
    required = {"file", "validation_flag", "field", "base_sha256", "head_sha256", "reason"}
    for index, entry in enumerate(entries):
        if not isinstance(entry, dict):
            errors.append(f"entry {index} is not an object")
            continue
        missing = sorted(required - set(entry))
        if missing:
            errors.append(f"entry {index} missing {','.join(missing)}")
            continue
        has_anchor = bool(entry.get("label")) or bool(entry.get("token_sha256"))
        if entry.get("field") != "helper_signature" and not has_anchor:
            errors.append(f"entry {index} needs label or token_sha256")
        if not isinstance(entry["reason"], str) or len(entry["reason"].strip()) < 20:
            errors.append(f"entry {index} reason is too short")
        key = tuple(
            entry.get(name)
            for name in (
                "file",
                "validation_flag",
                "label",
                "token_sha256",
                "field",
                "base_sha256",
                "head_sha256",
            )
        )
        if key in seen:
            errors.append(f"entry {index} is duplicated")
        seen.add(key)
    return [entry for entry in entries if isinstance(entry, dict)], errors


def _sha(value: object) -> str:
    """Return a stable SHA-256 for one metadata value."""
    return hashlib.sha256(str(value).encode("utf-8")).hexdigest()


def _row_anchor(row: dict[str, object]) -> tuple[str, str]:
    """Return an exact row anchor using its label or token signature hash."""
    label = str(row.get("label", ""))
    if label:
        return "label", label
    signature = repr(sorted(_token_signatures(str(row.get("token", "")))))
    return "token_sha256", _sha(signature)


def _dispatch_default(value: object) -> bool:
    """Return whether dispatch metadata is registered for default execution."""
    parts = str(value).split("|")
    return len(parts) >= 2 and parts[1] == "true"


def _change(
    base: dict[str, object],
    head: dict[str, object] | None,
    field: str,
    base_value: object,
    head_value: object,
) -> dict[str, object]:
    """Build a serializable weakening or parser-resolution change record."""
    anchor_kind, anchor_value = _row_anchor(base)
    record: dict[str, object] = {
        "file": str(base.get("file", "")),
        "validation_flag": str(base.get("validation_flag", "")),
        "field": field,
        "base_value": str(base_value),
        "head_value": str(head_value),
        "base_sha256": _sha(base_value),
        "head_sha256": _sha(head_value),
        anchor_kind: anchor_value,
    }
    if head is not None and head.get("label"):
        record["head_label"] = str(head["label"])
    return record


def _ack_key(change: dict[str, object]) -> tuple[object, ...]:
    """Return the exact acknowledgement matching key for a change."""
    return tuple(
        change.get(name, "")
        for name in (
            "file",
            "validation_flag",
            "label",
            "token_sha256",
            "field",
            "base_sha256",
            "head_sha256",
        )
    )


def _classify_change(
    base: dict[str, object], head: dict[str, object], fields: tuple[str, ...] = COMPARE_FIELDS
) -> tuple[list[dict[str, object]], list[str]]:
    """Classify one matched base/head row pair into strengthening or weakening."""
    strengthened: list[dict[str, object]] = []
    weakening: list[str] = []
    for field in fields:
        if field not in base or field not in head:
            continue
        before = str(base.get(field, ""))
        after = str(head.get(field, ""))
        if before == after:
            continue
        if field == "xunit_coverage":
            before_set = {item for item in before.split(";") if item}
            after_set = {item for item in after.split(";") if item}
            if before_set <= after_set:
                strengthened.append(_change(base, head, field, before, after))
            else:
                weakening.append(field)
            continue
        if field == "target":
            if "<unresolved>" in before and "<unresolved>" not in after:
                strengthened.append(_change(base, head, field, before, after))
            else:
                weakening.append(field)
            continue
        if field == "class":
            if before in {"UNKNOWN", "UNRESOLVED"} and after not in {"UNKNOWN", "UNRESOLVED"}:
                strengthened.append(_change(base, head, field, before, after))
            else:
                weakening.append(field)
            continue
        if field == "comment_only":
            if before == "True" and after == "False":
                strengthened.append(_change(base, head, field, before, after))
            else:
                weakening.append(field)
            continue
        if field == "dispatch":
            if not _dispatch_default(base) and _dispatch_default(head):
                strengthened.append(_change(base, head, field, before, after))
            else:
                weakening.append(field)
            continue
        if field == "call_path":
            if before == "UNKNOWN" and after != "UNKNOWN":
                strengthened.append(_change(base, head, field, before, after))
            else:
                weakening.append(field)
            continue
        if field == "assertion_context":
            if before == "bare" and after == "checked":
                strengthened.append(_change(base, head, field, before, after))
            else:
                weakening.append(field)
            continue
        weakening.append(field)
    return strengthened, weakening


def _parser_semantic_changes(
    base_rows: list[dict[str, object]], current_rows: list[dict[str, object]]
) -> tuple[list[dict[str, object]], list[dict[str, object]]]:
    """Compare every shared parser field on the trusted base tree."""
    matches, missing = _match_base_rows(base_rows, current_rows, strict=True)
    changes: list[dict[str, object]] = _row_schema_changes(current_rows, "current-parser")
    strengthened: list[dict[str, object]] = []
    base_versions = {str(row.get("parser_version", "")) for row in base_rows if row.get("parser_version")}
    current_versions = {
        str(row.get("parser_version", "")) for row in current_rows if row.get("parser_version")
    }
    base_has_version_field = any("parser_version" in row for row in base_rows)
    legacy_unversioned = not base_has_version_field and len(current_versions) == 1
    if legacy_unversioned:
        strengthened.append(
            {
                **_inventory_parser_migration_change("legacy-unversioned", next(iter(current_versions))),
                "field": "parser_schema_added",
            }
        )
    elif base_versions != current_versions:
        changes.append(
            _inventory_parser_migration_change(
                "|".join(sorted(base_versions)) or ("<missing>" if base_has_version_field else "legacy-unversioned"),
                "|".join(sorted(current_versions)) or "<missing>",
            )
        )
    for base, current in matches:
        row_strengthened, weakening_fields = _classify_change(
            base, current, PARSER_COMPARE_FIELDS
        )
        strengthened.extend(
            {**change, "field": f"parser_{change['field']}"} for change in row_strengthened
        )
        for field in weakening_fields:
            before = str(base.get(field, ""))
            after = str(current.get(field, ""))
            changes.append(
                _change(
                    base,
                    current,
                    f"parser_{field}",
                    before,
                    after,
                )
            )
    changes.extend(
        _change(base, None, "parser_row_removed", base.get("token", ""), "")
        for base in missing
    )
    matched_current_ids = {id(current) for _, current in matches}
    strengthened.extend(
        {"file": row.get("file", ""), "field": "parser_row_added"}
        for row in current_rows
        if id(row) not in matched_current_ids
    )
    return changes, strengthened


def _row_added_change(row: dict[str, object]) -> dict[str, object]:
    """Build a weakening record for a newly unresolved or unknown row."""
    base = {
        "file": row.get("file", ""),
        "validation_flag": row.get("validation_flag", ""),
        "label": row.get("label", ""),
        "token": row.get("token", ""),
    }
    field = "row_added_unresolved" if row.get("class") == "UNRESOLVED" else "row_added_unknown"
    return _change(base, row, field, "<missing>", row.get("class", ""))


def _row_schema_changes(rows: list[dict[str, object]], parser_side: str) -> list[dict[str, object]]:
    """Build weakening records for rows that omit protected parser metadata."""
    changes: list[dict[str, object]] = []
    for row in rows:
        missing = REQUIRED_ROW_FIELDS - set(row)
        if not row.get("parser_version"):
            missing = missing | {"parser_version"}
        for field in sorted(missing):
            changes.append(
                {
                    "file": row.get("file", ""),
                    "validation_flag": row.get("validation_flag", ""),
                    "label": row.get("label", ""),
                    "token": row.get("token", ""),
                    "field": "parser_field_missing",
                    "missing_field": field,
                    "parser_side": parser_side,
                    "base_value": "present",
                    "head_value": "<missing>",
                    "base_sha256": _sha("present"),
                    "head_sha256": _sha(field),
                }
            )
    return changes


def _inventory_count_change(field: str, base_value: int, head_value: int) -> dict[str, object]:
    """Build an exact acknowledgement record for a monotonicity violation."""
    return {
        "file": "<inventory>",
        "validation_flag": "<inventory>",
        "label": f"inventory:{field}",
        "field": field,
        "base_value": str(base_value),
        "head_value": str(head_value),
        "base_sha256": _sha(base_value),
        "head_sha256": _sha(head_value),
    }


def _inventory_parser_migration_change(base_version: str, head_version: str) -> dict[str, object]:
    """Build one trusted acknowledgement record for a parser version migration."""
    return {
        "file": "<inventory>",
        "validation_flag": "<inventory>",
        "label": "inventory:parser_migration",
        "field": "parser_migration",
        "base_value": base_version,
        "head_value": head_version,
        "base_sha256": _sha(base_version),
        "head_sha256": _sha(head_version),
    }


def _inventory_fingerprint(rows: list[dict[str, object]]) -> str:
    """Hash every row value and duplicate independently of platform row ordering."""
    serialized = sorted(
        json.dumps(row, sort_keys=True, ensure_ascii=False, separators=(",", ":"))
        for row in rows
    )
    payload = "[" + ",".join(serialized) + "]"
    return hashlib.sha256(payload.encode("utf-8")).hexdigest()


def _migration_fingerprints(
    root: Path,
    revision: str,
    base_rows: list[dict[str, object]],
    current_rows: list[dict[str, object]],
) -> dict[str, str]:
    """Bind migration evidence to one base commit, checker, and pair of full scans."""
    base_revision = subprocess.run(
        ["git", "rev-parse", f"{revision}^{{commit}}"],
        cwd=root, check=True, capture_output=True, text=True,
    ).stdout.strip()
    return {
        "base_revision": base_revision,
        "checker_sha256": _checker_digest(root),
        "base_inventory_sha256": _inventory_fingerprint(base_rows),
        "current_inventory_sha256": _inventory_fingerprint(current_rows),
    }


def _verify_base_inventory(
    root: Path,
    rows: list[dict[str, object]],
    revision: str,
    inventory_callback: Callable[[Path], list[dict[str, object]]] | None = None,
    migration_approval: Path | None = None,
    output: Path | None = None,
) -> int:
    """Classify base/head changes and enforce exact acknowledgement entries."""
    if inventory_callback is None:
        raise RuntimeError("current inventory callback is required")
    checker_changed = _checker_digest(root) != _checker_digest(root, revision)
    base_rows = _inventory_at_revision(root, revision, inventory_callback=inventory_callback)
    migration_errors: list[str] = []
    approved_parser_changes: list[dict[str, object]] = []
    if checker_changed:
        base_status_rows = _inventory_at_revision(
            root,
            revision,
            use_current_parser=True,
            inventory_callback=inventory_callback,
        )
        parser_changes, parser_strengthened = _parser_semantic_changes(base_rows, base_status_rows)
        fingerprints = _migration_fingerprints(root, revision, base_rows, base_status_rows)
        if output is not None:
            evidence = {
                "fingerprints": fingerprints,
                "parser_changes": parser_changes,
                "parser_change_fields": dict(Counter(str(change["field"]) for change in parser_changes)),
            }
            output.mkdir(parents=True, exist_ok=True)
            (output / "parser_migration_evidence.json").write_text(
                json.dumps(evidence, sort_keys=True, indent=2) + "\n", encoding="utf-8"
            )
        if migration_approval is not None:
            approval = json.loads(migration_approval.read_text(encoding="utf-8"))
            if not isinstance(approval, dict):
                raise RuntimeError("parser migration approval must be an object")
            migration_errors = [
                field for field, value in fingerprints.items() if approval.get(field) != value
            ]
            if not isinstance(approval.get("reason"), str) or not approval["reason"].strip():
                migration_errors.append("reason")
            current_versions = {row.get("parser_version") for row in base_status_rows}
            if any("parser_version" in row for row in base_rows) or len(current_versions) != 1:
                migration_errors.append("not a legacy-to-versioned migration")
            if not migration_errors:
                approved_fields = {f"parser_{field}" for field in PARSER_COMPARE_FIELDS}
                approved_parser_changes = [
                    change for change in parser_changes if change["field"] in approved_fields
                ]
                parser_changes = [
                    change for change in parser_changes if change["field"] not in approved_fields
                ]
    else:
        base_status_rows = base_rows
        parser_changes = []
        parser_strengthened = []
        if migration_approval is not None:
            migration_errors.append("checker did not change")
    matches, missing = _match_base_rows(base_status_rows, rows, strict=True)
    strengthened: list[dict[str, object]] = list(parser_strengthened)
    changes: list[dict[str, object]] = _row_schema_changes(rows, "head")
    changes.extend(parser_changes)
    for base, head in matches:
        row_strengthened, weakening_fields = _classify_change(base, head)
        strengthened.extend(row_strengthened)
        for field in weakening_fields:
            changes.append(_change(base, head, field, base.get(field, ""), head.get(field, "")))
    for base in missing:
        changes.append(_change(base, None, "row_removed", base.get("token", ""), ""))
    matched_head_ids = {id(head) for _, head in matches}
    for row in rows:
        if id(row) not in matched_head_ids:
            if row.get("class") in {"UNKNOWN", "UNRESOLVED"}:
                changes.append(_row_added_change(row))
            else:
                strengthened.append({"file": row.get("file", ""), "field": "row_added"})
    base_unresolved = sum(row.get("class") == "UNRESOLVED" for row in base_status_rows)
    head_unresolved = sum(row.get("class") == "UNRESOLVED" for row in rows)
    base_unknown = sum(row.get("class") == "UNKNOWN" for row in base_status_rows)
    head_unknown = sum(row.get("class") == "UNKNOWN" for row in rows)
    if head_unresolved > base_unresolved:
        changes.append(_inventory_count_change("unresolved_count", base_unresolved, head_unresolved))
    if head_unknown > base_unknown:
        changes.append(_inventory_count_change("unknown_count", base_unknown, head_unknown))
    changes = list({_ack_key(change): change for change in changes}.values())
    acknowledgements, ack_errors = _load_acknowledgements(root, revision)
    _, head_ack_errors = _load_acknowledgements(root)
    ack_errors.extend(f"head {error}" for error in head_ack_errors)
    ack_map = {_ack_key(entry): entry for entry in acknowledgements}
    acknowledged: list[dict[str, object]] = []
    unacknowledged: list[dict[str, object]] = []
    for change in changes:
        change_key = _ack_key(change)
        entry = ack_map.get(change_key)
        if entry is None:
            unacknowledged.append(change)
        else:
            acknowledged.append(change)
    used_keys = {_ack_key(change) for change in changes}
    unused = [entry for entry in acknowledgements if _ack_key(entry) not in used_keys]
    print(
        "SOURCE_TEXT_INVENTORY_BASE_OK "
        f"base_rows={len(base_rows)} head_rows={len(rows)} missing={len(missing)} "
        f"base_unresolved={base_unresolved} head_unresolved={head_unresolved} "
        f"base_unknown={base_unknown} head_unknown={head_unknown} "
        f"strengthened={len(strengthened)} acknowledged={len(acknowledged)} "
        f"unacknowledged={len(unacknowledged)} ack_unused={len(unused)}"
    )
    for change in parser_strengthened:
        if change.get("field") == "parser_schema_added":
            print(
                "SOURCE_TEXT_INVENTORY_PARSER_MIGRATION "
                f"{change['base_value']}->{change['head_value']} additive_schema"
            )
    if migration_approval is not None and not migration_errors:
        print(
            "SOURCE_TEXT_INVENTORY_PARSER_MIGRATION_APPROVED "
            f"changes={len(approved_parser_changes)} "
            f"fingerprints={json.dumps(fingerprints, sort_keys=True)}"
        )
    if migration_errors:
        print(f"SOURCE_TEXT_INVENTORY_PARSER_MIGRATION_REJECTED {','.join(migration_errors)}")
    if ack_errors:
        for error in ack_errors:
            print(f"SOURCE_TEXT_INVENTORY_ACK_ERROR {error}")
    if unused:
        print(f"SOURCE_TEXT_INVENTORY_ACK_UNUSED count={len(unused)}")
    if unacknowledged:
        for change in unacknowledged[:20]:
            suggestion = dict(change)
            suggestion["reason"] = ""
            print(f"SOURCE_TEXT_INVENTORY_UNACKNOWLEDGED {json.dumps(suggestion, sort_keys=True)}")
    return 1 if ack_errors or unacknowledged or migration_errors else 0
