#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Generate the Phase 91 ROS 2 .msg + CDR MCAP smoke fixture.
# Usage: python Scripts/smoke/ros2_cdr_mcap_inspect.py --output build/test_mcap/phase91_ros2_cdr_smoke.mcap
# Inputs: Optional --output MCAP path; invokes the runtime validation project.
# Outputs: Writes a ROS 2 .msg + CDR MCAP smoke fixture.

"""Generate the Phase 91 ROS 2 .msg + CDR MCAP smoke fixture."""

from __future__ import annotations

import argparse
import os
from pathlib import Path
import shutil
import subprocess
import sys


REPO_ROOT_PARENT_DEPTH = 2
EXIT_SUCCESS = 0
EXIT_FAILURE = 1
EMPTY_FILE_SIZE_BYTES = 0

REPO_ROOT = Path(__file__).resolve().parents[REPO_ROOT_PARENT_DEPTH]
PROJECT = REPO_ROOT / "Packages" / "dev.unity2foxglove.sdk" / "Tests" / "Runtime" / "FoxgloveSdk.Tests.csproj"
DEFAULT_OUTPUT = REPO_ROOT / "build" / "test_mcap" / "phase91_ros2_cdr_smoke.mcap"


def resolve_output(output: str | None) -> Path:
    """Resolve an optional output path relative to the repository root."""
    if not output:
        return DEFAULT_OUTPUT

    candidate = Path(output)
    if not candidate.is_absolute():
        candidate = REPO_ROOT / candidate
    return candidate.resolve()


def setup_nuget_cache() -> dict[str, str]:
    """Reuse the user-level NuGet package cache when no cache is configured."""
    env = os.environ.copy()
    if "NUGET_PACKAGES" not in env:
        user_nuget = Path.home() / ".nuget" / "packages"
        if user_nuget.is_dir():
            env["NUGET_PACKAGES"] = str(user_nuget)
    return env


def main() -> int:
    """Invoke Phase91 runtime generation and verify that an MCAP was written."""
    parser = argparse.ArgumentParser(description="Generate the Phase 91 ROS 2 .msg + CDR MCAP smoke fixture.")
    parser.add_argument(
        "--output",
        type=str,
        default=None,
        help="Output MCAP path. Relative paths are resolved from the repository root.",
    )
    args = parser.parse_args()

    if shutil.which("dotnet") is None:
        print("[phase91] dotnet was not found on PATH.", file=sys.stderr)
        return EXIT_FAILURE

    output_path = resolve_output(args.output)
    output_path.parent.mkdir(parents=True, exist_ok=True)

    cmd = [
        "dotnet",
        "run",
        "--no-restore",
        "--project",
        str(PROJECT),
        "--",
        "--phase91-ros2-cdr-mcap",
        str(output_path),
    ]

    result = subprocess.run(cmd, cwd=REPO_ROOT, env=setup_nuget_cache())
    if result.returncode != EXIT_SUCCESS:
        print(f"[phase91] dotnet process exited with code {result.returncode}", file=sys.stderr)
        return result.returncode

    if not output_path.is_file():
        print(f"[phase91] ROS 2 CDR smoke MCAP was not generated: {output_path}", file=sys.stderr)
        return EXIT_FAILURE

    size = output_path.stat().st_size
    if size <= EMPTY_FILE_SIZE_BYTES:
        print(f"[phase91] ROS 2 CDR smoke MCAP is empty: {output_path}", file=sys.stderr)
        return EXIT_FAILURE

    print(f"[phase91] ROS 2 CDR smoke MCAP: {output_path}")
    print(f"[phase91] size: {size} bytes")
    return EXIT_SUCCESS


if __name__ == "__main__":
    raise SystemExit(main())
