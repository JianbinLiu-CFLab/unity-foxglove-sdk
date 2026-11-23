"""Reader-specific source-text ratchet checks."""

from __future__ import annotations

import hashlib
import re
import subprocess
from collections import Counter
from pathlib import Path

try:
    from . import source_text_inventory_flow as _flow_module
    from . import source_text_inventory_readers as _readers_module
    from .source_text_inventory_ratchet_helpers import (
        _discovered_readers,
        _apply_harness_replacements,
        _load_ratchet_replacements,
        _ratchet_key,
        _ratchet_path,
    )
except ImportError:
    import source_text_inventory_flow as _flow_module
    import source_text_inventory_readers as _readers_module
    from source_text_inventory_ratchet_helpers import (
        _discovered_readers,
        _apply_harness_replacements,
        _load_ratchet_replacements,
        _ratchet_key,
        _ratchet_path,
    )

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

def _canonical_path_collisions(paths: list[str]) -> set[str]:
    """Return case-folded paths that refer to more than one raw source path."""
    raw_paths: dict[str, set[str]] = {}
    for path in paths:
        raw_paths.setdefault(_ratchet_path(path), set()).add(path)
    return {
        normalized
        for normalized, candidates in raw_paths.items()
        if len(candidates) > 1
    }

def _approved_reader_change_isolated(
    root: Path,
    revision: str,
    approved_reader_helpers: set[tuple[str, str]],
    base_helpers: dict[tuple[str, str], str],
    head_helpers: dict[tuple[str, str], str],
    changed_source_paths: set[str],
) -> bool:
    """Return whether approved reader edits leave every other source byte unchanged."""
    helper_paths = [path for path, _ in set(base_helpers) | set(head_helpers)]
    if _canonical_path_collisions(helper_paths):
        return False
    path_lookup = {_ratchet_path(path): path for path in helper_paths}
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
                for method in [
                    *_flow_module._method_infos(text, masked),
                    *_readers_module._generic_method_infos(masked),
                ]
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
    root: Path,
    revision: str,
    base_rows: list[dict[str, object]],
    head_rows: list[dict[str, object]],
    base_helpers: dict[tuple[str, str], str],
    head_helpers: dict[tuple[str, str], str],
    approved_reader_helpers: set[tuple[str, str]],
    reader_changes_isolated: bool,
    changed_source_paths: set[str],
) -> tuple[list[str], list[str]]:
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
    approval_errors = _apply_harness_replacements(
        root,
        revision,
        failures,
        base_rows,
        head_rows,
        changed_helpers,
        changed_source_paths,
    )
    return failures, approval_errors

