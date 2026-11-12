#!/usr/bin/env python3
"""Guard the CI-safe known-failure baseline against unreviewed additions."""

from __future__ import annotations

import argparse
import re
import subprocess
import sys
from pathlib import Path


BASELINE_PATH = Path("Scripts/validation/ci_safe_known_failures.txt")
REVIEWED_REPLACEMENT_PATTERN = re.compile(
    r"^\s*#\s*reviewed replacement:\s*(\S+)\s*->\s*(\S+)\s*$"
)


def parse_entries(text: str) -> tuple[list[str], list[str]]:
    """Return ordered baseline entries and duplicate entries."""
    entries: list[str] = []
    duplicates: list[str] = []
    seen: set[str] = set()
    for raw in (text or "").replace("\r\n", "\n").splitlines():
        value = raw.split("#", 1)[0].strip()
        if not value:
            continue
        if value in seen and value not in duplicates:
            duplicates.append(value)
        entries.append(value)
        seen.add(value)
    return entries, duplicates


def parse_reviewed_replacements(text: str) -> set[tuple[str, str]]:
    """Return explicit one-for-one baseline replacement approvals."""
    replacements: set[tuple[str, str]] = set()
    for raw in (text or "").replace("\r\n", "\n").splitlines():
        match = REVIEWED_REPLACEMENT_PATTERN.match(raw)
        if match:
            replacements.add((match.group(1), match.group(2)))
    return replacements


def baseline_problems(base_text: str, head_text: str) -> list[str]:
    """Return duplicate or unreviewed baseline changes."""
    base_entries, base_duplicates = parse_entries(base_text)
    head_entries, head_duplicates = parse_entries(head_text)
    base_set = set(base_entries)
    head_set = set(head_entries)
    removed = base_set - head_set
    added = head_set - base_set
    reviewed_replacements = parse_reviewed_replacements(head_text)
    problems = [f"duplicate baseline entry: {entry}" for entry in head_duplicates]
    if len(added) > len(removed):
        problems.extend(
            f"baseline entry added without reviewed replacement: {entry}"
            for entry in sorted(added - removed)
        )
    elif added and len(added) == len(removed):
        approved_removed = {old for old, new in reviewed_replacements if old in removed and new in added}
        approved_added = {new for old, new in reviewed_replacements if old in removed and new in added}
        for entry in sorted(removed - approved_removed):
            problems.append(f"baseline replacement missing explicit approval for removed entry: {entry}")
        for entry in sorted(added - approved_added):
            problems.append(f"baseline replacement missing explicit approval for added entry: {entry}")
    problems.extend(f"duplicate baseline entry in base: {entry}" for entry in base_duplicates)
    return problems


def git_show(root: Path, revision: str, path: Path) -> str:
    """Read one tracked file from a Git revision."""
    result = subprocess.run(
        ["git", "show", f"{revision}:{path.as_posix()}"],
        cwd=root,
        check=True,
        capture_output=True,
        text=True,
        encoding="utf-8",
    )
    return result.stdout


def main(argv: list[str] | None = None) -> int:
    """Compare the checked-out baseline with a base revision."""
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base-ref", required=True)
    parser.add_argument("--head-ref", default="HEAD")
    parser.add_argument("--root", type=Path, default=Path.cwd())
    args = parser.parse_args(argv)

    base_text = git_show(args.root, args.base_ref, BASELINE_PATH)
    head_text = git_show(args.root, args.head_ref, BASELINE_PATH)
    problems = baseline_problems(base_text, head_text)
    if problems:
        for problem in problems:
            print("[FAIL] " + problem, file=sys.stderr)
        return 1

    print("CI_SAFE_BASELINE_GUARD_OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
