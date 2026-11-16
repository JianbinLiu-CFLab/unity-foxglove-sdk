"""Support functions for the source-text count ratchet."""

from __future__ import annotations

import hashlib
import json
import re
import subprocess
import tarfile
import tempfile
from collections import Counter
from pathlib import Path

try:
    from . import source_text_inventory_flow as _flow_module
except ImportError:
    import source_text_inventory_flow as _flow_module

RATCHET_APPROVALS_PATH = Path("Scripts/validation/source_text_ratchet_approvals.json")
RATCHET_CLASSES = frozenset({"A", "B", "C", "UNKNOWN", "UNRESOLVED"})
RATCHET_APPROVAL_FIELDS = frozenset(
    {"file", "validation_flag", "class", "base_count", "head_count", "reason", "replacement"}
)

def _ratchet_path(value: object) -> str:
    """Normalize one inventory path for count-ratchet keys."""
    path = str(value).strip().replace("\\", "/")
    while path.startswith("./"):
        path = path[2:]
    return path.casefold()

def _ratchet_key(row: dict[str, object]) -> tuple[str, str, str]:
    """Return the normalized count-ratchet key for one inventory row."""
    return (
        _ratchet_path(row.get("file", "")),
        str(row.get("validation_flag", "")).strip(),
        str(row.get("class", "")).strip(),
    )

def _validate_ratchet_rows(rows: list[dict[str, object]], source: str) -> list[str]:
    """Validate the trusted or reviewed inventory shape before counting it."""
    errors: list[str] = []
    required_fields = {
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
        "checked_expression",
        "assertion_context",
        "assertion_literal",
        "assertion_polarity",
        "harness_signature",
        "legacy_harness_signature",
        "comment_only",
        "reachability",
        "call_path",
        "call_binding",
    }
    for index, row in enumerate(rows):
        if not isinstance(row, dict):
            errors.append(f"{source} row {index} is not an object")
            continue
        missing_fields = sorted(required_fields - row.keys())
        if missing_fields:
            errors.append(f"{source} row {index} missing fields {','.join(missing_fields)}")
            continue
        row_class = row.get("class")
        if row_class not in RATCHET_CLASSES:
            errors.append(f"{source} row {index} has invalid class {row_class!r}")
        file_name = _ratchet_path(row.get("file", ""))
        if not file_name or file_name.startswith("/") or ".." in file_name.split("/"):
            errors.append(f"{source} row {index} has invalid file path")
    return errors

def _ratchet_counts(rows: list[dict[str, object]]) -> Counter[tuple[str, str, str]]:
    """Count inventory rows by file, validation flag, and class."""
    return Counter(_ratchet_key(row) for row in rows)

def _load_ratchet_approvals(
    root: Path, revision: str | None = None
) -> tuple[list[dict[str, object]], list[str]]:
    """Load exact A/B count approvals from the trusted revision only."""
    if revision is not None:
        completed = subprocess.run(
            ["git", "show", f"{revision}:{RATCHET_APPROVALS_PATH.as_posix()}"],
            cwd=root,
            check=False,
            capture_output=True,
        )
        if completed.returncode != 0:
            return [], []
        raw = completed.stdout.decode("utf-8")
    else:
        path = root / RATCHET_APPROVALS_PATH
        if not path.is_file():
            return [], []
        raw = path.read_text(encoding="utf-8")
    try:
        payload = json.loads(raw)
    except json.JSONDecodeError:
        return [], ["ratchet approvals must contain valid JSON"]
    if not isinstance(payload, dict) or set(payload) != {"approvals"}:
        return [], ["ratchet approvals must contain only the approvals field"]
    entries = payload["approvals"]
    if not isinstance(entries, list):
        return [], ["ratchet approvals must be a list"]
    errors: list[str] = []
    approvals: list[dict[str, object]] = []
    seen: set[tuple[str, str, str]] = set()
    for index, entry in enumerate(entries):
        if not isinstance(entry, dict):
            errors.append(f"approval {index} is not an object")
            continue
        if set(entry) != RATCHET_APPROVAL_FIELDS:
            errors.append(f"approval {index} has unexpected fields")
            continue
        key = (
            _ratchet_path(entry["file"]),
            str(entry["validation_flag"]).strip(),
            str(entry["class"]).strip(),
        )
        entry_errors: list[str] = []
        if key in seen:
            entry_errors.append(f"approval {index} is duplicated")
        seen.add(key)
        if key[2] not in {"A", "B"}:
            entry_errors.append(f"approval {index} has invalid class")
        if (
            not isinstance(entry["base_count"], int)
            or not isinstance(entry["head_count"], int)
            or entry["base_count"] < 0
            or entry["head_count"] < 0
        ):
            entry_errors.append(f"approval {index} has invalid counts")
        if not isinstance(entry["reason"], str) or len(entry["reason"].strip()) < 20:
            entry_errors.append(f"approval {index} reason is too short")
        if not isinstance(entry["replacement"], str) or not entry["replacement"].strip():
            entry_errors.append(f"approval {index} replacement is empty")
        if entry_errors:
            errors.extend(entry_errors)
        else:
            approvals.append(entry)
    return approvals, errors

def _ratchet_change_tokens(rows: list[dict[str, object]]) -> dict[tuple[str, str, str], Counter[str]]:
    """Collect token multisets for non-blocking changed-key diagnostics."""
    tokens: dict[tuple[str, str, str], Counter[str]] = {}
    for row in rows:
        key = _ratchet_key(row)
        tokens.setdefault(key, Counter())[str(row.get("token", ""))] += 1
    return tokens

def _ratchet_assertion_semantics(
    rows: list[dict[str, object]],
) -> dict[tuple[str, str, str], Counter[tuple[str, ...]]]:
    """Collect assertion context values whose changes must fail closed."""
    fields = (
        "assertion_context",
        "assertion_literal",
        "assertion_polarity",
        "checked_expression",
        "reachability",
        "assertion_signature",
        "dispatch",
        "call_path",
    )
    semantics: dict[tuple[str, str, str], Counter[tuple[str, ...]]] = {}
    for row in rows:
        key = _ratchet_key(row)
        signature = tuple(str(row.get(field, "")) for field in fields)
        semantics.setdefault(key, Counter())[signature] += 1
    return semantics

def _helper_signature_records(text: str) -> dict[str, str]:
    """Return hashes for recognized assertion/read/source helpers and bindings."""
    if not re.search(
        r"\b(?:Check|Assert\w*|Read\w*|Source\w*|Validate\w*|Action|Func|Predicate|delegate)\b|=>",
        text,
    ):
        return {}
    masked = _flow_module._strip_csharp_comments_and_strings(text)
    methods = _flow_module._method_infos(text, masked)
    records: dict[str, str] = {}
    for method in methods:
        delegate_parameter = any(
            _flow_module._normalize_type(parameter).removeprefix("System.").startswith(
                ("Action", "Func", "Predicate")
            )
            for parameter in method.parameter_types
        )
        if not (
            method.name == "Check"
            or method.name.startswith(("Assert", "Read", "Source", "Validate"))
            or delegate_parameter
        ):
            continue
        if method.name == "Validate":
            continue
        helper_name = method.name
        if helper_name in records:
            helper_name += ":" + hashlib.sha256(method.signature.encode("utf-8")).hexdigest()[:12]
        records[helper_name] = hashlib.sha256(
            text[method.body_start : method.body_end + 1].encode("utf-8")
        ).hexdigest()

    binding_pattern = re.compile(
        r"\b(?P<type>(?:(?:System\.)?(?:Action|Func|Predicate)(?:<[^;=]+>)?|var))?\s*"
        r"(?P<name>[A-Za-z_]\w*)\s*=\s*(?P<target>[^;]+);"
    )
    for match in binding_pattern.finditer(masked):
        target = match.group("target")
        declared_type = match.group("type") or ""
        if (
            (not declared_type or declared_type == "var")
            and "=>" not in target
            and "delegate" not in target
        ):
            continue
        name = "binding:" + match.group("name")
        records[name] = hashlib.sha256(text[match.start() : match.end()].encode("utf-8")).hexdigest()
    return records

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

def _helper_signatures_at_revision(
    root: Path, revision: str, files: set[str]
) -> dict[tuple[str, str], str]:
    """Hash recognized helper bodies from one committed revision."""
    signatures: dict[tuple[str, str], str] = {}
    build_review = root / "build" / "review"
    build_review.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="source-text-helper-base-", dir=build_review) as directory:
        tree = Path(directory)
        _archive_revision(root, revision, tree)
        for relative in sorted(files):
            path = tree / relative
            if not path.is_file():
                continue
            for helper, digest in _helper_signature_records(path.read_text(encoding="utf-8", errors="replace")).items():
                signatures[(relative, helper)] = digest
    return signatures

def _helper_signatures_at_head(root: Path, files: set[str]) -> dict[tuple[str, str], str]:
    """Hash recognized helper bodies from the reviewed working tree."""
    signatures: dict[tuple[str, str], str] = {}
    for relative in sorted(files):
        path = root / relative
        if not path.is_file():
            continue
        for helper, digest in _helper_signature_records(path.read_text(encoding="utf-8", errors="replace")).items():
            signatures[(relative, helper)] = digest
    return signatures

def _file_has_source_assertion(path: Path) -> bool:
    """Return whether a live validation file still contains a scannable assertion."""
    if not path.is_file():
        return False
    text = path.read_text(encoding="utf-8", errors="replace")
    return re.search(r"\.(?:Contains|IndexOf|DoesNotContain)\s*\(|\bAssert\.", text) is not None
