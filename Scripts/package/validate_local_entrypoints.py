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
THIS_SCRIPT = Path(__file__).resolve()

FORBIDDEN_PATTERNS: tuple[tuple[str, re.Pattern[str]], ...] = (
    ("Windows ROS2 install root", re.compile(r"C:[\\/]+ros2_[A-Za-z0-9_-]+[\\/]+ros2-windows", re.IGNORECASE)),
    ("external R2FU artifact cache", re.compile(r"D:[\\/]+ros2unity", re.IGNORECASE)),
    ("temporary GitHub signed release asset URL", re.compile(r"release-assets\.githubusercontent\.com", re.IGNORECASE)),
)


def tracked_python_scripts() -> list[Path]:
    """Return tracked Python scripts under Scripts/."""

    result = subprocess.run(
        ["git", "ls-files", "--", "Scripts/**/*.py"],
        cwd=REPO_ROOT,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=True,
    )
    return [REPO_ROOT / line for line in result.stdout.splitlines() if line.strip()]


def main() -> int:
    """Validate tracked scripts and return a process exit code."""

    failures: list[str] = []
    for path in tracked_python_scripts():
        if path.resolve() == THIS_SCRIPT:
            continue
        rel = path.relative_to(REPO_ROOT).as_posix()

        text = path.read_text(encoding="utf-8", errors="replace")
        for label, pattern in FORBIDDEN_PATTERNS:
            for match in pattern.finditer(text):
                line = text.count("\n", 0, match.start()) + 1
                failures.append(f"{rel}:{line}: {label}: {match.group(0)}")

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
