#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
# Purpose: Run the exact supported Unity batch import and compile gate.
# Usage: python -B Scripts/release/run_unity_batch_compile.py --project-path Unity2Foxglove
# Inputs: Unity executable, Unity project path, log path, and full user environment.
# Outputs: PASS, FAIL, or NOT_RUN JSON evidence plus the Unity log.

"""Run the repository's real Unity import/compile gate in batch mode."""

from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import re
import subprocess
import sys

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(errors="replace")

REPO_ROOT = Path(__file__).resolve().parents[2]
DEFAULT_UNITY = Path(r"C:\Program Files\Unity\Hub\Editor\6000.3.14f1\Editor\Unity.exe")
REQUIRED_ENVIRONMENT = ("ALLUSERSPROFILE", "ProgramData", "APPDATA", "LOCALAPPDATA", "USERPROFILE")
INVALID_MARKERS = (
    '[Package Manager] The "path" argument must be of type string',
    "Failed to resolve packages",
    "No packages loaded",
)
ERROR_PATTERN = re.compile(r"\berror CS\d+\b")
WARNING_PATTERN = re.compile(r"\bwarning CS\d+\b")
DEFAULT_TIMEOUT_SECONDS = 30 * 60


def compile_verdict(exit_code: int, log_text: str) -> tuple[str, list[str], list[str]]:
    """Return PASS, FAIL, or NOT_RUN with the exact compiler diagnostics."""
    invalid = [marker for marker in INVALID_MARKERS if marker in log_text]
    errors = [line for line in log_text.splitlines() if ERROR_PATTERN.search(line)]
    warnings = [line for line in log_text.splitlines() if WARNING_PATTERN.search(line)]
    if invalid:
        return "NOT_RUN", invalid, errors + warnings
    if exit_code != 0 or errors or warnings:
        return "FAIL", errors, warnings
    return "PASS", [], []


def _active_unity_project(project_path: Path) -> bool:
    """Detect a live Unity process that already owns this project, if psutil exists."""
    try:
        import psutil
    except ImportError as exc:
        raise RuntimeError("psutil is required to verify Unity project ownership") from exc
    needle = str(project_path.resolve()).lower().rstrip("\\/")
    for process in psutil.process_iter(("name", "cmdline")):
        try:
            name = (process.info.get("name") or "").lower()
            command_line = " ".join(process.info.get("cmdline") or []).lower()
        except (psutil.Error, OSError):
            continue
        if "unity" in name and needle in command_line:
            return True
    return False


def _write_result(log_path: Path, result: dict) -> None:
    """Write and print the adjacent JSON gate result."""
    log_path.parent.mkdir(parents=True, exist_ok=True)
    result_path = log_path.with_suffix(".json")
    result_path.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result, indent=2))


def _not_run(log_path: Path, reason: str) -> int:
    """Record a preflight/environment block without launching Unity."""
    result = {
        "verdict": "NOT_RUN",
        "exit_code": 7,
        "log": str(log_path),
        "diagnostics": [reason],
        "timed_out": False,
        "residual_pids": [],
    }
    _write_result(log_path, result)
    return 7


def run(
    unity: Path,
    project_path: Path,
    log_path: Path,
    timeout_seconds: int = DEFAULT_TIMEOUT_SECONDS,
) -> int:
    """Run one owned Unity batch process and classify its compiler diagnostics."""
    missing = [name for name in REQUIRED_ENVIRONMENT if not os.environ.get(name)]
    if missing:
        return _not_run(log_path, "missing environment " + ", ".join(missing))
    if not unity.is_file():
        return _not_run(log_path, f"Unity executable not found: {unity}")
    if not project_path.is_dir():
        return _not_run(log_path, f"project path not found: {project_path}")
    try:
        active_project = _active_unity_project(project_path)
    except RuntimeError as exc:
        return _not_run(log_path, str(exc))
    if active_project:
        return _not_run(log_path, f"Unity already owns project: {project_path}")

    log_path.parent.mkdir(parents=True, exist_ok=True)
    log_path.write_text("", encoding="utf-8")
    command = [
        str(unity),
        "-batchmode",
        "-nographics",
        "-quit",
        "-projectPath",
        str(project_path),
        "-logFile",
        str(log_path),
    ]
    from Scripts.unity_build.unity_il2cpp import await_tree_quiescence, start_owned_process

    try:
        tree = start_owned_process(command, REPO_ROOT)
    except OSError as exc:
        return _not_run(log_path, f"failed to launch Unity: {exc}")
    residual = []
    residual_detected = False
    timed_out = False
    try:
        try:
            exit_code = tree.process.wait(timeout=max(1, timeout_seconds))
        except subprocess.TimeoutExpired:
            timed_out = True
            exit_code = 124
            residual = tree.terminate()
        else:
            residual = await_tree_quiescence(tree, 2.0)
            if residual:
                residual_detected = True
                residual = tree.terminate()
    finally:
        tree.close()

    text = log_path.read_text(encoding="utf-8", errors="replace") if log_path.exists() else ""
    if residual_detected:
        exit_code = 125
        verdict, first, second = "FAIL", ["Unity process tree did not quiesce after exit"], []
    elif not log_path.is_file():
        verdict, first, second = "FAIL", ["Unity did not create the requested log file"], []
    elif not text:
        verdict, first, second = "FAIL", ["Unity created an empty compile log"], []
    else:
        verdict, first, second = compile_verdict(exit_code, text)
    result = {
        "verdict": verdict,
        "exit_code": exit_code,
        "log": str(log_path),
        "errors": first if verdict != "NOT_RUN" else [],
        "warnings": second if verdict == "FAIL" else [],
        "diagnostics": first + second,
        "timed_out": timed_out,
        "residual_pids": residual,
    }
    _write_result(log_path, result)
    return 0 if verdict == "PASS" else (7 if verdict == "NOT_RUN" else 1)


def main(argv: list[str] | None = None) -> int:
    """Parse gate arguments and execute the requested Unity batch check."""
    parser = argparse.ArgumentParser()
    parser.add_argument("--unity", type=Path, default=DEFAULT_UNITY)
    parser.add_argument("--project-path", type=Path, default=REPO_ROOT / "Unity2Foxglove")
    parser.add_argument(
        "--log-file",
        type=Path,
        default=REPO_ROOT / "build" / "unity-batch-compile" / "unity.log",
    )
    parser.add_argument("--timeout-seconds", type=int, default=DEFAULT_TIMEOUT_SECONDS)
    args = parser.parse_args(argv)
    return run(args.unity, args.project_path, args.log_file, args.timeout_seconds)


if __name__ == "__main__":
    raise SystemExit(main())
