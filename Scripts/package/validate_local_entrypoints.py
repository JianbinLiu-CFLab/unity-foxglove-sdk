#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Keep local ROS2/R2FU resource entrypoints repository-relative.
# Usage:   python Scripts/package/validate_local_entrypoints.py

"""Validate that Python scripts do not hard-code machine-local ROS2 paths."""

from __future__ import annotations

import re
import subprocess
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
SCRIPT_ROOT = REPO_ROOT / "Scripts"
THIS_SCRIPT_RELATIVE_PATH = "Scripts/package/validate_local_entrypoints.py"

FORBIDDEN_PATTERNS: tuple[tuple[str, re.Pattern[str]], ...] = (
    ("Windows ROS2 install root", re.compile(r"C:[\\/]+ros2_[A-Za-z0-9_-]+[\\/]+ros2-windows", re.IGNORECASE)),
    ("external R2FU artifact cache", re.compile(r"D:[\\/]+ros2unity", re.IGNORECASE)),
    ("temporary GitHub signed release asset URL", re.compile(r"release-assets\.githubusercontent\.com", re.IGNORECASE)),
)


def git_grep_failures(label: str, pattern: re.Pattern[str]) -> list[str]:
    """Return git-grep matches for one forbidden tracked-script pattern."""
    result = subprocess.run(
        [
            "git",
            "grep",
            "-n",
            "-I",
            "-E",
            pattern.pattern,
            "--",
            ":(glob)Scripts/**/*.py",
            f":!{THIS_SCRIPT_RELATIVE_PATH}",
        ],
        cwd=REPO_ROOT,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
    )
    if result.returncode == 1:
        return []
    if result.returncode != 0:
        raise RuntimeError(result.stderr.strip() or "git grep failed")
    return [f"{line}: {label}" for line in result.stdout.splitlines() if line.strip()]


def main() -> int:
    """Validate tracked scripts and return a process exit code."""

    failures: list[str] = []
    for label, pattern in FORBIDDEN_PATTERNS:
        failures.extend(git_grep_failures(label, pattern))

    if failures:
        print("[FAIL] Local entrypoint validation found hard-coded resource paths:")
        for failure in failures:
            print(f"  {failure}")
        print(
            "\nUse repository entrypoints such as ros2-windows/<distro> and "
            "r2fu-runtime-artifacts/<distro> instead of machine-local defaults."
        )
        return 1

    print("[PASS] Local ROS2/R2FU entrypoint validation")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
