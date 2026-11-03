#!/usr/bin/env python3
"""Reject new phase-numbered paths in maintained source roots."""

from __future__ import annotations

import argparse
import os
import re
import subprocess
from pathlib import Path
from typing import Iterable

REPOSITORY_ROOT = Path(__file__).resolve().parents[2]
PHASE_PATH_PATTERN = re.compile(r"(?i)phase\d{2,3}")
ALLOWLISTED_PATH_PATTERNS = (
    re.compile(
        r"^Packages/dev\.unity2foxglove\.foxrun\.ros2\.interfaces\.typesupport\.[^/]+\.win64/"
        r"Runtime/Ros2ForUnity/Plugins/Windows/x86_64/"
    ),
)


def is_maintained_path(path: str) -> bool:
    """Return whether a repository-relative path is in the guarded roots."""
    normalized = path.replace("\\", "/").lstrip("./")
    parts = normalized.split("/")
    if parts and parts[0] == "Scripts":
        return True
    return len(parts) >= 3 and parts[0] == "Packages" and parts[2] in {
        "Runtime",
        "Editor",
        "Tests",
    }


def is_allowlisted_path(path: str, allowlist: Iterable[str] = ()) -> bool:
    """Return whether a generated path is covered by the reviewed allowlist."""
    normalized = path.replace("\\", "/")
    return normalized in set(allowlist) or any(
        pattern.search(normalized) for pattern in ALLOWLISTED_PATH_PATTERNS
    )


def check_new_paths(
    current_paths: Iterable[str],
    *,
    baseline_paths: Iterable[str],
    allowlist: Iterable[str] = (),
) -> list[str]:
    """Return errors for newly introduced phase-numbered maintained paths."""
    baseline = {path.replace("\\", "/") for path in baseline_paths}
    errors = []
    for path in sorted({path.replace("\\", "/") for path in current_paths}):
        if (
            path not in baseline
            and is_maintained_path(path)
            and PHASE_PATH_PATTERN.search(path)
            and not is_allowlisted_path(path, allowlist)
        ):
            errors.append(f"NEW_PHASE_PATH {path}")
    return errors


def _git_paths(repository: Path, revision: str) -> set[str]:
    result = subprocess.run(
        ["git", "-C", str(repository), "ls-tree", "-r", "--name-only", revision],
        check=False,
        capture_output=True,
        text=True,
        encoding="utf-8",
    )
    if result.returncode != 0:
        raise RuntimeError(result.stderr.strip() or f"git ls-tree failed for {revision}")
    return {line for line in result.stdout.splitlines() if line}


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repository", type=Path, default=REPOSITORY_ROOT)
    parser.add_argument(
        "--base",
        default=os.environ.get("SOURCE_DECOMPOSITION_BASE_SHA", "origin/main"),
        help="Git revision containing the frozen path baseline.",
    )
    parser.add_argument("--head", default="HEAD")
    args = parser.parse_args(argv)
    repository = args.repository.resolve()
    baseline = _git_paths(repository, args.base)
    current = _git_paths(repository, args.head)
    errors = check_new_paths(current, baseline_paths=baseline)
    if errors:
        for error in errors:
            print(error)
        return 1
    print("MAINTAINED_NAME_PATH_GUARD_OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
