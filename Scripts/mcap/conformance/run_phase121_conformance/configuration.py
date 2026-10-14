#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

from __future__ import annotations

"""Shared configuration and process primitives for Phase121 MCAP conformance."""

import argparse
import json
import os
import re
import shutil
import subprocess
import sys
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Iterable

from pathlib import Path as _Phase192Path
_PHASE192_FACADE_FILE = _Phase192Path(__file__).resolve().parents[1] / "run_phase121_conformance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _Phase192Path, _PHASE192_FACADE_FILE


EXPECTED_OBSERVED_COMMIT = "c3cab6bd3ce79199e362766daec3a4689f3a0335"
OFFICIAL_REPOSITORY_URL = "https://github.com/foxglove/mcap.git"
SCRIPT_PATH = Path(__file__).resolve()
REPO_ROOT = SCRIPT_PATH.parents[3]
BUILD_ROOT = REPO_ROOT / "build/mcap-conformance"
BOOTSTRAPPED_OFFICIAL_ROOT = BUILD_ROOT / "official-mcap"
OFFICIAL_ROOT = BOOTSTRAPPED_OFFICIAL_ROOT
OFFICIAL_CONFORMANCE = OFFICIAL_ROOT / "tests/conformance"
OVERLAY_ROOT = BUILD_ROOT / "mcap-overlay"
DATA_DIR = BUILD_ROOT / "data"
REPORT_PATH = BUILD_ROOT / "phase121-conformance-report.json"
PROJECT_PATH = REPO_ROOT / "Packages/dev.unity2foxglove.sdk/Tests/McapConformance/Unity2Foxglove.McapConformance.csproj"
RUNNER_SOURCE_ROOT = REPO_ROOT / "Scripts/mcap/conformance/csharp-runners"
RUNNER_ARRAY_DECLARATION = "const runners: readonly (IndexedReadTestRunner | StreamedReadTestRunner | WriteTestRunner)[] = ["
CSHARP_RUNNER_INSERTION = (
    "const runners: readonly (IndexedReadTestRunner | StreamedReadTestRunner | WriteTestRunner)[] = [\n"
    "  new CsharpIndexedReaderTestRunner(),\n"
    "  new CsharpStreamedReaderTestRunner(),\n"
    "  new CsharpWriterTestRunner(),"
)
CSHARP_CONFORMANCE_DLL_NAME = "Unity2Foxglove.McapConformance.dll"
FAILURE_DETAILS_MAX_CHARACTERS = 4000
FAILURE_DETAILS_TAIL_CHARACTERS = 800
_PUBLIC_STATE_NAMES = ("OFFICIAL_ROOT", "OFFICIAL_CONFORMANCE", "REPORT_PATH")


@dataclass(frozen=True)
class CommandResult:
    """Captured process result used by the conformance wrapper."""

    exit_code: int
    stdout: str
    stderr: str
    command: str
    timed_out: bool = False


def parse_args(argv: list[str]) -> argparse.Namespace:
    """Parse command line arguments."""

    parser = argparse.ArgumentParser(description="Run Phase 121 MCAP conformance baseline.")
    parser.add_argument(
        "--release-blocking",
        action="store_true",
        help="Return a non-zero exit code when external tooling is skipped or measured failures exist.",
    )
    parser.add_argument(
        "--report-path",
        help="Optional report output path. Defaults to build/mcap-conformance/phase121-conformance-report.json.",
    )
    parser.add_argument(
        "--ci-smoke",
        action="store_true",
        help="Write the conformance report schema without running the official TypeScript harness.",
    )
    parser.add_argument(
        "--official-root",
        help=(
            "Explicit foxglove/mcap checkout for local development. Full release/CI runs omit this "
            "option and bootstrap the pinned commit under build/mcap-conformance."
        ),
    )
    return parser.parse_args(argv)


def set_official_root(path: Path) -> None:
    """Select the official source root used by the rest of the wrapper."""

    global OFFICIAL_ROOT, OFFICIAL_CONFORMANCE
    OFFICIAL_ROOT = path.resolve()
    OFFICIAL_CONFORMANCE = OFFICIAL_ROOT / "tests/conformance"
    sync_public_state()


def sync_public_state() -> None:
    """Mirror mutable configuration values into both compatibility facades."""

    for module in tuple(sys.modules.values()):
        if module is None or not module.__name__.endswith("run_phase121_conformance"):
            continue
        for name in _PUBLIC_STATE_NAMES:
            module.__dict__[name] = globals()[name]


def invoke_command_capture(
    command: list[str],
    *,
    cwd: Path = REPO_ROOT,
    env: dict[str, str] | None = None,
    timeout_seconds: int | None = None,
) -> CommandResult:
    """Run a command and capture stdout, stderr, exit code, and timeout state."""

    process_env = os.environ.copy()
    if env:
        process_env.update(env)

    resolved_command = command[:]
    executable = shutil.which(resolved_command[0])
    if executable:
        resolved_command[0] = executable

    command_text = " ".join(command)
    try:
        completed = subprocess.run(
            resolved_command,
            cwd=str(cwd),
            env=process_env,
            capture_output=True,
            text=True,
            encoding="utf-8",
            errors="replace",
            timeout=timeout_seconds,
            check=False,
        )
        return CommandResult(completed.returncode, completed.stdout, completed.stderr, command_text)
    except subprocess.TimeoutExpired as ex:
        stdout = ex.stdout if isinstance(ex.stdout, str) else (ex.stdout or b"").decode(errors="replace")
        stderr = ex.stderr if isinstance(ex.stderr, str) else (ex.stderr or b"").decode(errors="replace")
        timeout_message = f"Timed out after {timeout_seconds} second(s)."
        stderr = timeout_message if not stderr.strip() else stderr + "\n" + timeout_message
        return CommandResult(-1, stdout, stderr, command_text, timed_out=True)
    except OSError as ex:
        return CommandResult(-1, "", str(ex), command_text)


def get_official_commit() -> str | None:
    """Return the checked out official MCAP commit if available."""

    if not OFFICIAL_ROOT.exists() or shutil.which("git") is None:
        return None

    result = invoke_command_capture(["git", "-C", str(OFFICIAL_ROOT), "rev-parse", "HEAD"])
    if result.exit_code != 0:
        return None
    return result.stdout.strip()


def first_lines(text: str, count: int = 5) -> str:
    """Return the first non-empty lines from command output."""

    return " ".join(line for line in text.strip().splitlines()[:count])


__all__ = [name for name in globals() if not name.startswith("__")]
