#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Keep local ROS2/R2FU resource entrypoints repository-relative.
# Usage:   python Scripts/package/validate_local_entrypoints.py

"""Validate that Python scripts do not hard-code machine-local ROS2 paths."""

from __future__ import annotations

import os
import re
import signal
import subprocess
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
SCRIPT_ROOT = REPO_ROOT / "Scripts"
THIS_SCRIPT_RELATIVE_PATH = "Scripts/package/validate_local_entrypoints.py"
GIT_GREP_TIMEOUT_SECONDS = 900
PROCESS_CLEANUP_TIMEOUT_SECONDS = 10

FORBIDDEN_PATTERNS: tuple[tuple[str, re.Pattern[str]], ...] = (
    ("Windows ROS2 install root", re.compile(r"C:[\\/]+ros2_[A-Za-z0-9_-]+[\\/]+ros2-windows", re.IGNORECASE)),
    ("external R2FU artifact cache", re.compile(r"D:[\\/]+ros2unity", re.IGNORECASE)),
    (
        "temporary GitHub signed release asset URL",
        re.compile(r"(https?|ftp)://release-assets\.githubusercontent\.com/", re.IGNORECASE),
    ),
)


def _terminate_owned_process(process: subprocess.Popen) -> None:
    """Terminate the complete process tree owned by one validation call."""
    if os.name == "nt":
        try:
            subprocess.run(
                ["taskkill", "/PID", str(process.pid), "/T", "/F"],
                stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL,
                check=False,
                timeout=PROCESS_CLEANUP_TIMEOUT_SECONDS,
            )
        except (OSError, subprocess.TimeoutExpired):
            try:
                process.kill()
            except OSError:
                pass
    else:
        try:
            os.killpg(process.pid, signal.SIGKILL)
        except ProcessLookupError:
            pass
        except OSError:
            try:
                process.kill()
            except OSError:
                pass
    try:
        process.wait(timeout=PROCESS_CLEANUP_TIMEOUT_SECONDS)
    except subprocess.TimeoutExpired:
        try:
            process.kill()
        except OSError:
            pass
        try:
            process.wait(timeout=PROCESS_CLEANUP_TIMEOUT_SECONDS)
        except (OSError, subprocess.TimeoutExpired):
            pass


def _run_owned_git_grep(command: list[str]) -> subprocess.CompletedProcess:
    """Run git grep with a bounded wait and descendant cleanup on timeout."""
    popen_kwargs: dict[str, object] = {}
    if os.name == "nt":
        popen_kwargs["creationflags"] = getattr(
            subprocess,
            "CREATE_NEW_PROCESS_GROUP",
            0x00000200,
        )
    else:
        popen_kwargs["start_new_session"] = True
    try:
        process = subprocess.Popen(
            command,
            cwd=REPO_ROOT,
            text=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            **popen_kwargs,
        )
    except FileNotFoundError as exc:
        raise RuntimeError(
            "git executable is unavailable; install git or add it to PATH"
        ) from exc
    try:
        stdout, stderr = process.communicate(
            input=None,
            timeout=GIT_GREP_TIMEOUT_SECONDS,
        )
    except subprocess.TimeoutExpired as exc:
        _terminate_owned_process(process)
        try:
            process.communicate(timeout=PROCESS_CLEANUP_TIMEOUT_SECONDS)
        except (OSError, ValueError, subprocess.TimeoutExpired):
            pass
        raise RuntimeError(
            f"git grep timed out after {GIT_GREP_TIMEOUT_SECONDS}s"
        ) from exc
    return subprocess.CompletedProcess(
        command,
        process.returncode,
        stdout=stdout,
        stderr=stderr,
    )


def git_grep_failures(label: str, pattern: re.Pattern[str]) -> list[str]:
    """Return git-grep matches for one forbidden tracked-script pattern."""
    result = _run_owned_git_grep(
        [
            "git",
            "grep",
            "-n",
            "-I",
            "-E",
            pattern.pattern,
            "--",
            ":(glob)Scripts/**/*.py",
            ":(exclude,glob)Scripts/**/regression_checks/**/*.py",
            f":!{THIS_SCRIPT_RELATIVE_PATH}",
        ]
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
