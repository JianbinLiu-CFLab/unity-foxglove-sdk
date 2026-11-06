#!/usr/bin/env python3
"""Guard the CI-safe known-failure baseline against unreviewed additions."""

from __future__ import annotations

import argparse
import subprocess
import sys
from pathlib import Path


BASELINE_PATH = Path("Scripts/validation/ci_safe_known_failures.txt")


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


def baseline_problems(base_text: str, head_text: str) -> list[str]:
    """Return duplicate or newly added baseline entries."""
    base_entries, base_duplicates = parse_entries(base_text)
    head_entries, head_duplicates = parse_entries(head_text)
    problems = [f"duplicate baseline entry: {entry}" for entry in head_duplicates]
    problems.extend(f"baseline entry added without reviewed base change: {entry}"
                    for entry in sorted(set(head_entries) - set(base_entries)))
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
