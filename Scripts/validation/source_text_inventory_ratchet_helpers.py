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
    from .source_text_inventory_text import (
        _DELEGATE_ASSIGNMENT_PATTERN,
        DELEGATE_STATE_NON_DELEGATE,
        DELEGATE_STATE_UNSUPPORTED,
        _delegate_type_names,
        _delegate_invocation_text,
        _is_delegate_assignment,
        _is_delegate_type_name,
    )
except ImportError:
    import source_text_inventory_flow as _flow_module
    import source_text_inventory_readers as _readers_module
    from source_text_inventory_text import (
        _DELEGATE_ASSIGNMENT_PATTERN,
        DELEGATE_STATE_NON_DELEGATE,
        DELEGATE_STATE_UNSUPPORTED,
        _delegate_type_names,
        _delegate_invocation_text,
        _is_delegate_assignment,
        _is_delegate_type_name,
    )

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

def _ratchet_harness_key(row: dict[str, object]) -> tuple[str, str]:
    """Return the normalized file and validation flag for one harness group."""
    return (_ratchet_path(row.get("file", "")), str(row.get("validation_flag", "")).strip())

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

def _ratchet_harness_signature(rows: list[dict[str, object]], key: tuple[str, str]) -> str:
    """Hash the complete class-and-harness multiset for one validation file."""
    signatures = Counter(
        (str(row.get("class", "")), str(row.get("harness_signature", "")))
        for row in rows
        if _ratchet_harness_key(row) == key
    )
    payload = {"harness": sorted(([list(signature), count] for signature, count in signatures.items()))}
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
        if entry["class"] not in {"C", "READER", "HARNESS"}:
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

def _apply_harness_replacements(
    root: Path,
    revision: str | None,
    failures: list[str],
    base_rows: list[dict[str, object]],
    head_rows: list[dict[str, object]],
    changed_helpers: set[tuple[str, str]],
) -> list[str]:
    """Clear only exact harness transitions with no concurrent helper changes."""
    replacements, errors = _load_ratchet_replacements(root, revision)
    if changed_helpers:
        return errors
    for entry in replacements:
        if entry["class"] != "HARNESS":
            continue
        key = (_ratchet_path(entry["file"]), str(entry["validation_flag"]).strip())
        messages = {
            failure
            for failure in failures
            if failure.startswith(
                f"SOURCE_TEXT_RATCHET_HARNESS_CHANGED file={key[0]} flag={key[1]} class="
            )
        }
        if not messages:
            continue
        base_signature = _ratchet_harness_signature(base_rows, key)
        head_signature = _ratchet_harness_signature(head_rows, key)
        verdict = "MISMATCH"
        if entry["base_signature"] == base_signature and entry["head_signature"] == head_signature:
            failures[:] = [failure for failure in failures if failure not in messages]
            verdict = "APPROVED"
        print(
            f"SOURCE_TEXT_RATCHET_HARNESS_REPLACEMENT_{verdict} "
            f"file={key[0]} flag={key[1]} base={base_signature} head={head_signature}"
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
    method_names = {method.name for method in methods}
    delegate_methods = {
        method.name
        for method in methods
        if str(method.return_type).removeprefix("System.").startswith(("Action", "Func", "Predicate"))
    }
    delegate_type_names = _delegate_type_names(text, masked)
    file_delegate_variables = _flow_module._delegate_variable_names(masked, delegate_type_names)
    scoped_delegate_variables = {
        method.identity: _flow_module._delegate_variable_names(
            masked, delegate_type_names, method.start, method.body_end
        )
        for method in methods
    }
    generic = [method for method in _readers_module._generic_method_infos(masked) if method.name in readers]
    records: dict[str, str] = {}
    for method in [*methods, *generic]:
        delegate_parameter = any(_is_delegate_type_name(parameter, delegate_type_names) for parameter in method.parameter_types)
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

    delegate_wrapper_names, delegate_boundary_names, _, delegate_unsupported_offsets = _flow_module._delegate_parameter_boundaries(
        text, masked, methods, delegate_type_names
    )
    delegate_wrapper_names.update(_flow_module._KNOWN_DELEGATE_WRAPPER_NAMES); signature_methods = sorted(methods, key=lambda method: method.start); method_starts = [method.start for method in signature_methods]
    for scope_key, names in delegate_boundary_names.items():
        for marker in sorted(name for name in names if name.startswith("unknown:")):
            records[f"unknown-delegate-flow:{scope_key}:{marker}"] = hashlib.sha256(marker.encode("utf-8")).hexdigest()
    for match in (candidate for candidate in _DELEGATE_ASSIGNMENT_PATTERN.finditer(masked) if (scope_candidate := (_flow_module._method_for_offset(methods, candidate.start()) or (signature_methods[index] if (index := _flow_module.bisect_right(method_starts, candidate.start()) - 1) >= 0 and signature_methods[index].body_start > candidate.start() else None))) is None or candidate.start() >= scope_candidate.body_start):
        target = match.group("target")
        declared_type = match.group("type") or ""
        name = match.group("name")
        if declared_type == "using" or (not declared_type and target.lstrip().startswith(("=", ">", ")"))):
            continue
        scope = _flow_module._method_for_offset(methods, match.start()) or (signature_methods[index] if (index := _flow_module.bisect_right(method_starts, match.start()) - 1) >= 0 and signature_methods[index].body_start > match.start() else None)
        scope_key = _flow_module._delegate_scope_key(scope)
        delegate_variables = (
            scoped_delegate_variables.setdefault(scope_key, set())
            if scope
            else file_delegate_variables
        )
        scope_boundary_names = delegate_boundary_names.get(scope_key, set())
        invocation_text = _delegate_invocation_text(
            masked,
            match.end(),
            target,
            declared_type,
            name,
            delegate_variables,
            method_names,
            scope_boundary_names,
            scope.body_start if scope else 0,
            scope.body_end if scope else len(masked),
        )
        state = _is_delegate_assignment(
            target,
            declared_type,
            name,
            delegate_variables,
            delegate_methods,
            method_names,
            delegate_type_names,
            invocation_text,
            scope_boundary_names,
            delegate_wrapper_names,
        )
        if state == DELEGATE_STATE_UNSUPPORTED and "!" not in target and "?" not in target and not _is_delegate_type_name(declared_type, delegate_type_names):
            state = DELEGATE_STATE_NON_DELEGATE
        if state == DELEGATE_STATE_UNSUPPORTED and scope is None and ("," in target or "\n" in target):
            state = DELEGATE_STATE_NON_DELEGATE
        if state == DELEGATE_STATE_NON_DELEGATE:
            continue
        payload = text[match.start() : match.end()]
        if state == DELEGATE_STATE_UNSUPPORTED:
            line = text.count("\n", 0, match.start()) + 1
            binding_name = f"unsupported-delegate-flow:{name}@{line}:{invocation_text or target}"
            payload = f"state={state}|{payload}|invoke:{invocation_text}"
        else:
            binding_name = "binding:" + name
            if state != "KNOWN_WRAPPER" and state != "LAMBDA_FORWARD":
                payload = f"state={state}|{payload}"
        if binding_name in records:
            line = text.count("\n", 0, match.start()) + 1
            binding_name = f"{binding_name}@{scope_key or 'file'}:{line}"
        records[binding_name] = hashlib.sha256(
            payload.encode("utf-8")
        ).hexdigest()
        delegate_variables.add(name)
    for method in methods:
        for call_name, open_index in _flow_module._call_sites(text, masked, method):
            if open_index not in delegate_unsupported_offsets:
                continue
            arguments = _flow_module._call_arguments(text, masked, open_index) or ""
            line = text.count("\n", 0, open_index) + 1
            binding_name = f"unsupported-delegate-flow:{call_name}@{line}"
            payload = f"state=UNSUPPORTED_DELEGATE_FLOW|{call_name}({arguments})"
            records[binding_name] = hashlib.sha256(payload.encode("utf-8")).hexdigest()
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

def _file_has_source_assertion(path: Path) -> bool:
    """Return whether a live validation file still contains a scannable assertion."""
    if not path.is_file():
        return False
    text = path.read_text(encoding="utf-8", errors="replace")
    return re.search(r"\.(?:Contains|IndexOf|DoesNotContain)\s*\(|\bAssert\.", text) is not None
