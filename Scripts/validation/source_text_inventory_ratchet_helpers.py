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
    from . import source_text_inventory_readers as _readers_module
except ImportError:
    import source_text_inventory_flow as _flow_module
    import source_text_inventory_readers as _readers_module

RATCHET_APPROVALS_PATH = Path("Scripts/validation/source_text_ratchet_approvals.json")
RATCHET_CLASSES = frozenset({"A", "B", "C", "UNKNOWN", "UNRESOLVED"})
RATCHET_APPROVAL_FIELDS = frozenset(
    {"file", "validation_flag", "class", "base_count", "head_count", "reason", "replacement"}
)
RATCHET_REPLACEMENTS_PATH = Path("Scripts/validation/source_text_ratchet_replacements.json")
RATCHET_REPLACEMENT_FIELDS = frozenset(
    {"file", "validation_flag", "class", "base_signature", "head_signature", "reason", "replacement"}
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

def _ratchet_key_signature(
    tokens: Counter[str] | None, semantics: Counter[tuple[str, ...]] | None
) -> str:
    """Hash the exact assertion tokens and semantics recorded for one ratchet key."""
    payload = {
        "tokens": sorted((tokens or Counter()).items()),
        "semantics": sorted([list(signature), count] for signature, count in (semantics or Counter()).items()),
    }
    return hashlib.sha256(
        json.dumps(payload, sort_keys=True, separators=(",", ":")).encode("utf-8")
    ).hexdigest()

def _load_ratchet_replacements(
    root: Path, revision: str | None = None
) -> tuple[list[dict[str, object]], list[str]]:
    """Load exact Class C in-place replacement approvals from the trusted revision only."""
    if revision is not None:
        completed = subprocess.run(
            ["git", "show", f"{revision}:{RATCHET_REPLACEMENTS_PATH.as_posix()}"],
            cwd=root,
            check=False,
            capture_output=True,
        )
        if completed.returncode != 0:
            return [], []
        raw = completed.stdout.decode("utf-8")
    else:
        path = root / RATCHET_REPLACEMENTS_PATH
        if not path.is_file():
            return [], []
        raw = path.read_text(encoding="utf-8")
    try:
        payload = json.loads(raw)
    except json.JSONDecodeError:
        return [], ["ratchet replacements must contain valid JSON"]
    if not isinstance(payload, dict) or set(payload) != {"replacements"}:
        return [], ["ratchet replacements must contain only the replacements field"]
    entries = payload["replacements"]
    if not isinstance(entries, list):
        return [], ["ratchet replacements must be a list"]
    errors: list[str] = []
    replacements: list[dict[str, object]] = []
    seen: set[tuple[str, str]] = set()
    for index, entry in enumerate(entries):
        if not isinstance(entry, dict) or set(entry) != RATCHET_REPLACEMENT_FIELDS:
            errors.append(f"replacement {index} has unexpected fields")
            continue
        key = (_ratchet_path(entry["file"]), str(entry["validation_flag"]).strip(), str(entry["class"]))
        entry_errors: list[str] = []
        if key in seen:
            entry_errors.append(f"replacement {index} is duplicated")
        seen.add(key)
        if entry["class"] not in {"C", "READER"}:
            entry_errors.append(f"replacement {index} has invalid class")
        for field in ("base_signature", "head_signature"):
            value = entry[field]
            if not isinstance(value, str) or re.fullmatch(r"[0-9a-f]{64}", value) is None:
                entry_errors.append(f"replacement {index} has invalid {field}")
        if entry["base_signature"] == entry["head_signature"]:
            entry_errors.append(f"replacement {index} does not change the assertion")
        if not isinstance(entry["reason"], str) or len(entry["reason"].strip()) < 20:
            entry_errors.append(f"replacement {index} reason is too short")
        if not isinstance(entry["replacement"], str) or not entry["replacement"].strip():
            entry_errors.append(f"replacement {index} replacement is empty")
        if entry_errors:
            errors.extend(entry_errors)
        else:
            replacements.append(entry)
    return replacements, errors

def _apply_ratchet_replacements(
    root: Path,
    revision: str | None,
    failures: list[str],
    base: tuple[dict[tuple[str, str, str], Counter[str]], dict[tuple[str, str, str], Counter[tuple[str, ...]]]],
    head: tuple[dict[tuple[str, str, str], Counter[str]], dict[tuple[str, str, str], Counter[tuple[str, ...]]]],
) -> list[str]:
    """Clear Class C assertion changes whose exact signatures the trusted base approves."""
    replacements, errors = _load_ratchet_replacements(root, revision)
    for entry in replacements:
        if entry["class"] != "C":
            continue
        key = (_ratchet_path(entry["file"]), str(entry["validation_flag"]).strip(), "C")
        message = f"SOURCE_TEXT_RATCHET_ASSERTION_CHANGED file={key[0]} flag={key[1]} class=C"
        if message not in failures:
            continue
        base_signature = _ratchet_key_signature(base[0].get(key), base[1].get(key))
        head_signature = _ratchet_key_signature(head[0].get(key), head[1].get(key))
        verdict = "MISMATCH"
        if entry["base_signature"] == base_signature and entry["head_signature"] == head_signature:
            failures.remove(message)
            verdict = "APPROVED"
        print(
            f"SOURCE_TEXT_RATCHET_REPLACEMENT_{verdict} file={key[0]} flag={key[1]} "
            f"base={base_signature} head={head_signature}"
        )
    return errors

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

def _helper_signature_records(text: str, readers: frozenset[str] = frozenset()) -> dict[str, str]:
    """Return hashes for recognized assertion/read/source helpers, discovered readers and bindings."""
    if not readers and not re.search(
        r"\b(?:Check|Assert\w*|Read\w*|Source\w*|Validate\w*|Action|Func|Predicate|delegate)\b|=>",
        text,
    ):
        return {}
    masked = _flow_module._strip_csharp_comments_and_strings(text)
    methods = _flow_module._method_infos(text, masked)
    generic = [method for method in _readers_module._generic_method_infos(masked) if method.name in readers]
    records: dict[str, str] = {}
    for method in [*methods, *generic]:
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
            or method.name in readers
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

def _discovered_readers(root: Path) -> tuple[dict[str, frozenset[str]], frozenset[str]]:
    """Return the head discovery's reader declarations per file and every discovered reader name."""
    discovered = _readers_module.DISCOVERED_READER_DECLARATIONS.get(str(root.resolve()), {})
    return discovered, frozenset().union(*discovered.values())

def _helper_signatures_at_revision(
    root: Path, revision: str, files: set[str], reader_names: frozenset[str] = frozenset()
) -> dict[tuple[str, str], str]:
    """Hash recognized helper bodies from one committed revision, including test files the head deletes."""
    signatures: dict[tuple[str, str], str] = {}
    discovered, names = _discovered_readers(root)
    names = names | reader_names
    build_review = root / "build" / "review"
    build_review.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="source-text-helper-base-", dir=build_review) as directory:
        tree = Path(directory)
        _archive_revision(root, revision, tree)
        deleted = {
            path.relative_to(tree).as_posix()
            for pattern in ("Packages/*/Tests/Runtime/**/*.cs", "Packages/*/Tests/Unit/**/*.cs")
            for path in tree.glob(pattern)
            if not (root / path.relative_to(tree)).is_file()
        }
        for relative in sorted(files | set(discovered) | deleted):
            path = tree / relative
            if not path.is_file():
                continue
            text = path.read_text(encoding="utf-8", errors="replace")
            for helper, digest in _helper_signature_records(text, names).items():
                signatures[(relative, helper)] = digest
    return signatures

def _helper_signatures_at_head(
    root: Path, files: set[str], reader_names: frozenset[str] = frozenset()
) -> dict[tuple[str, str], str]:
    """Hash recognized helper bodies from the reviewed working tree."""
    signatures: dict[tuple[str, str], str] = {}
    discovered, names = _discovered_readers(root)
    names = names | reader_names
    for relative in sorted(files | set(discovered)):
        path = root / relative
        if not path.is_file():
            continue
        text = path.read_text(encoding="utf-8", errors="replace")
        for helper, digest in _helper_signature_records(text, names).items():
            signatures[(relative, helper)] = digest
    return signatures

def _reader_helper_failures(
    root: Path,
    revision: str,
    base_helpers: dict[tuple[str, str], str],
    head_helpers: dict[tuple[str, str], str],
    reader_names: frozenset[str] = frozenset(),
) -> list[str]:
    """Fail seed or discovered reader changes in base files unless the trusted base approves the exact digests."""
    _, names = _discovered_readers(root)
    names = names | reader_names
    listed = subprocess.run(
        ["git", "ls-tree", "-r", "--name-only", revision], cwd=root, check=False, capture_output=True, text=True
    )
    base_files = set(listed.stdout.splitlines())
    empty = hashlib.sha256(b"").hexdigest()
    approved = {
        (_ratchet_path(entry["file"]), str(entry["validation_flag"]).strip(), entry["base_signature"], entry["head_signature"])
        for entry in _load_ratchet_replacements(root, revision)[0]
        if entry["class"] == "READER"
    }
    failures: list[str] = []
    for relative, helper in sorted(set(base_helpers) | set(head_helpers)):
        name = helper.split(":", 1)[0]
        base_digest = base_helpers.get((relative, helper), empty)
        head_digest = head_helpers.get((relative, helper), empty)
        if base_digest == head_digest or relative not in base_files:
            continue
        if not (_readers_module._text_module.SEED_READER_NAME.fullmatch(name) or name in names):
            continue
        verdict = "APPROVED" if (_ratchet_path(relative), helper, base_digest, head_digest) in approved else "CHANGED"
        message = f"SOURCE_TEXT_RATCHET_READER_{verdict} file={relative} helper={helper} base={base_digest} head={head_digest}"
        if verdict == "APPROVED":
            print(message)
        else:
            failures.append(message)
    return failures

def _approved_reader_replacement_helpers(
    root: Path,
    revision: str,
    base_helpers: dict[tuple[str, str], str],
    head_helpers: dict[tuple[str, str], str],
) -> set[tuple[str, str]]:
    """Return helper identities with an exact trusted reader replacement approval."""
    replacements, _ = _load_ratchet_replacements(root, revision)
    approved: set[tuple[str, str]] = set()
    for entry in replacements:
        if entry["class"] != "READER":
            continue
        path = _ratchet_path(entry["file"])
        helper = str(entry["validation_flag"]).strip()
        base_digest = next(
            (digest for (candidate_path, candidate_helper), digest in base_helpers.items()
             if _ratchet_path(candidate_path) == path and candidate_helper == helper),
            None,
        )
        head_digest = next(
            (digest for (candidate_path, candidate_helper), digest in head_helpers.items()
             if _ratchet_path(candidate_path) == path and candidate_helper == helper),
            None,
        )
        if base_digest == entry["base_signature"] and head_digest == entry["head_signature"]:
            approved.add((path, helper))
    return approved

def _reader_replacement_context(
    root: Path, revision: str
) -> tuple[set[str], frozenset[str]]:
    """Return trusted reader replacement paths and helper names."""
    replacements, _ = _load_ratchet_replacements(root, revision)
    readers = [entry for entry in replacements if entry["class"] == "READER"]
    paths = {
        str(entry["file"]).strip().replace("\\", "/")
        for entry in readers
    }
    names = frozenset(str(entry["validation_flag"]).strip() for entry in readers)
    return paths, names

def _changed_source_paths(root: Path, revision: str) -> set[str]:
    """Return tracked C# paths changed since one trusted revision."""
    completed = subprocess.run(
        ["git", "diff", "--name-only", revision, "--", "*.cs"],
        cwd=root,
        check=True,
        capture_output=True,
        text=True,
    )
    return {_ratchet_path(path) for path in completed.stdout.splitlines() if path.strip()}

def _approved_reader_change_isolated(
    root: Path,
    revision: str,
    approved_reader_helpers: set[tuple[str, str]],
    base_helpers: dict[tuple[str, str], str],
    head_helpers: dict[tuple[str, str], str],
    changed_source_paths: set[str],
) -> bool:
    """Return whether approved reader edits leave every other source byte unchanged."""
    path_lookup = {
        _ratchet_path(path): path
        for path, _ in set(base_helpers) | set(head_helpers)
    }
    helpers_by_path: dict[str, set[str]] = {}
    for path, helper in approved_reader_helpers:
        helpers_by_path.setdefault(path, set()).add(helper.split(":", 1)[0])
    for normalized_path in changed_source_paths:
        original_path = path_lookup.get(normalized_path)
        names = helpers_by_path.get(normalized_path)
        if original_path is None or not names:
            return False
        base_file = subprocess.run(
            ["git", "show", f"{revision}:{original_path}"],
            cwd=root,
            check=False,
            capture_output=True,
        )
        head_path = root / original_path
        if base_file.returncode != 0 or not head_path.is_file():
            return False
        base_text = base_file.stdout.decode("utf-8", errors="replace")
        head_text = head_path.read_text(encoding="utf-8", errors="replace")
        normalized = []
        for text in (base_text, head_text):
            masked = _flow_module._strip_csharp_comments_and_strings(text)
            spans = [
                (method.body_start, method.body_end)
                for method in _flow_module._method_infos(text, masked)
                if method.name in names
            ]
            if len(spans) != len(names):
                return False
            for start, end in sorted(spans, reverse=True):
                text = text[:start] + "<approved-reader-body>" + text[end + 1 :]
            normalized.append(text)
        if normalized[0] != normalized[1]:
            return False
    return True

def _harness_change_failures(
    base_rows: list[dict[str, object]],
    head_rows: list[dict[str, object]],
    base_helpers: dict[tuple[str, str], str],
    head_helpers: dict[tuple[str, str], str],
    approved_reader_helpers: set[tuple[str, str]],
    reader_changes_isolated: bool,
) -> list[str]:
    """Fail closed when a source assertion's harness signature changes unexpectedly."""
    base_harness: dict[tuple[str, str, str], Counter[str]] = {}
    head_harness: dict[tuple[str, str, str], Counter[str]] = {}
    for row in base_rows:
        base_harness.setdefault(_ratchet_key(row), Counter())[str(row.get("harness_signature", ""))] += 1
    for row in head_rows:
        head_harness.setdefault(_ratchet_key(row), Counter())[str(row.get("harness_signature", ""))] += 1
    changed_helpers = {
        (_ratchet_path(path), helper)
        for path, helper in set(base_helpers) | set(head_helpers)
        if base_helpers.get((path, helper)) != head_helpers.get((path, helper))
    }
    only_approved_reader_changes = (
        bool(changed_helpers)
        and changed_helpers <= approved_reader_helpers
        and reader_changes_isolated
    )
    failures: list[str] = []
    for key in sorted(set(base_harness) | set(head_harness)):
        if key not in base_harness or key not in head_harness:
            continue
        if base_harness.get(key, Counter()) == head_harness.get(key, Counter()):
            continue
        if not only_approved_reader_changes:
            failures.append(
                f"SOURCE_TEXT_RATCHET_HARNESS_CHANGED file={key[0]} "
                f"flag={key[1]} class={key[2]}"
            )
    return failures

def _file_has_source_assertion(path: Path) -> bool:
    """Return whether a live validation file still contains a scannable assertion."""
    if not path.is_file():
        return False
    text = path.read_text(encoding="utf-8", errors="replace")
    return re.search(r"\.(?:Contains|IndexOf|DoesNotContain)\s*\(|\bAssert\.", text) is not None
