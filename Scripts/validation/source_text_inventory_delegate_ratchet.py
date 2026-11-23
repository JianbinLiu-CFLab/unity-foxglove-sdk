"""Delegate-target manifest ratchet (Rule R) for the source-text inventory gate."""

from __future__ import annotations

import hashlib
import os
import re
import subprocess
from pathlib import Path

try:
    from .source_text_inventory_ratchet_helpers import DELEGATE_VARIANTS, _load_ratchet_replacements, _ratchet_path
except ImportError:
    from source_text_inventory_ratchet_helpers import DELEGATE_VARIANTS, _load_ratchet_replacements, _ratchet_path

DELEGATE_MANIFEST_PATH = "Scripts/validation/source_text_delegate_targets.tsv"
DELEGATE_BOOTSTRAP_ENV = "SOURCE_TEXT_DELEGATE_MANIFEST_BOOTSTRAP_BASE"
_MEMBER = re.compile(r"[A-Z]:\S+")
_TARGET = re.compile(r"M:\S+|L:[A-Z]:\S+\|\S+")
_COUNT = re.compile(r"[1-9][0-9]*")


def _delegate_manifest_problems(data: bytes) -> list[tuple[int, str]]:
    """Return (line, reason) well-formedness violations, mirroring the analyzer's manifest rules."""
    try:
        text = data.decode("utf-8")
    except UnicodeDecodeError:
        return [(0, "encoding")]
    if not text:
        return []
    problems: list[tuple[int, str]] = []
    if "\r" in text:
        problems.append((0, "cr"))
    if not text.endswith("\n"):
        problems.append((0, "final-lf"))
    lines = text.replace("\r", "").split("\n")
    if text.endswith("\n"):
        lines = lines[:-1]
    keys: set[tuple[str, ...]] = set()
    previous: str | None = None
    for number, line in enumerate(lines, start=1):
        fields = line.split("\t")
        if len(fields) != 5:
            problems.append((number, "fields"))
            continue
        if any(not field for field in fields):
            problems.append((number, "empty-field"))
            continue
        if fields[0] not in DELEGATE_VARIANTS:
            problems.append((number, "variant"))
        if _MEMBER.fullmatch(fields[2]) is None:
            problems.append((number, "member"))
        if _TARGET.fullmatch(fields[3]) is None:
            problems.append((number, "target"))
        if _COUNT.fullmatch(fields[4]) is None:
            problems.append((number, "count"))
            continue
        if tuple(fields[:4]) in keys:
            problems.append((number, "duplicate"))
        keys.add(tuple(fields[:4]))
        if previous is not None and previous > line:
            problems.append((number, "unsorted"))
        previous = line
    return problems


def _checked_manifest_lines(data: bytes, side: str, failures: list[str]) -> list[str] | None:
    """Return the manifest lines, or record every violation and return None."""
    problems = _delegate_manifest_problems(data)
    for number, reason in problems:
        failures.append(f"SOURCE_TEXT_RATCHET_DELEGATE_MANIFEST_INVALID side={side} line={number} reason={reason}")
    return None if problems else data.decode("utf-8").split("\n")[:-1]


def _delegate_key_signature(lines: list[str]) -> str:
    """Hash one key's sorted entry lines; an absent key hashes as the empty set."""
    return hashlib.sha256("".join(line + "\n" for line in sorted(lines)).encode("utf-8")).hexdigest()


def _delegate_manifest_failures(
    root: Path,
    revision: str,
    base_rows: list[dict[str, object]],
    head_rows: list[dict[str, object]],
) -> list[str]:
    """Fail changed (variant, file, member) entry sets of row files unless the trusted base approves them."""
    failures: list[str] = []
    head_path = root / DELEGATE_MANIFEST_PATH
    head_lines = None
    if head_path.is_file():
        head_lines = _checked_manifest_lines(head_path.read_bytes(), "head", failures)
    else:
        failures.append("SOURCE_TEXT_RATCHET_DELEGATE_MANIFEST_MISSING side=head")
    resolved = subprocess.run(
        ["git", "rev-parse", "--verify", f"{revision}^{{commit}}"], cwd=root, check=False, capture_output=True, text=True
    )
    listed = subprocess.run(
        ["git", "ls-tree", resolved.stdout.strip() or revision, "--", DELEGATE_MANIFEST_PATH],
        cwd=root, check=False, capture_output=True, text=True,
    )
    if resolved.returncode != 0 or listed.returncode != 0:
        return [*failures, f"SOURCE_TEXT_RATCHET_BASE_ERROR revision={revision}"]
    base_sha = resolved.stdout.strip()
    if not listed.stdout.strip():
        if base_sha == os.environ.get(DELEGATE_BOOTSTRAP_ENV, "").strip():
            print(f"SOURCE_TEXT_DELEGATE_MANIFEST_BOOTSTRAP base={base_sha}")
        else:
            failures.append("SOURCE_TEXT_RATCHET_DELEGATE_MANIFEST_MISSING side=base")
        return failures
    blob = subprocess.run(["git", "show", f"{base_sha}:{DELEGATE_MANIFEST_PATH}"], cwd=root, check=False, capture_output=True)
    if blob.returncode != 0:
        return [*failures, f"SOURCE_TEXT_RATCHET_BASE_ERROR revision={revision}"]
    base_lines = _checked_manifest_lines(blob.stdout, "base", failures)
    if base_lines is None or head_lines is None:
        return failures
    base_keys: dict[tuple[str, str, str], list[str]] = {}
    head_keys: dict[tuple[str, str, str], list[str]] = {}
    for lines, keys in ((base_lines, base_keys), (head_lines, head_keys)):
        for line in lines:
            variant, file, member, _target, _count = line.split("\t")
            keys.setdefault((variant, _ratchet_path(file), member), []).append(line)
    row_files = {_ratchet_path(row.get("file", "")) for row in (*base_rows, *head_rows)}
    head_row_files = {_ratchet_path(row.get("file", "")) for row in head_rows}
    approved = {
        (_ratchet_path(entry["file"]), str(entry["validation_flag"]).strip(), entry["base_signature"], entry["head_signature"])
        for entry in _load_ratchet_replacements(root, base_sha)[0]
        if entry["class"] == "DELEGATE"
    }
    for (variant, path, member), lines in sorted(base_keys.items()):
        file = lines[0].split("\t")[1]
        if path not in row_files or not ((root / file).is_file() or path in head_row_files):
            continue
        base_digest = _delegate_key_signature(lines)
        head_digest = _delegate_key_signature(head_keys.get((variant, path, member), []))
        if base_digest == head_digest:
            continue
        verdict = "APPROVED" if (path, f"{variant}|{member}", base_digest, head_digest) in approved else "CHANGED"
        message = (
            f"SOURCE_TEXT_RATCHET_DELEGATE_TARGETS_{verdict} variant={variant} file={file} "
            f"member={member} base={base_digest} head={head_digest}"
        )
        if verdict == "APPROVED":
            print(message)
        else:
            failures.append(message)
    return failures
