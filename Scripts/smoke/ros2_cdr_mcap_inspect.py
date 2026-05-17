#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Generate or inspect the Phase 93 ROS 2 .msg + CDR full-schema MCAP smoke fixture.
# Usage:
#   python Scripts/smoke/ros2_cdr_mcap_inspect.py
#   python Scripts/smoke/ros2_cdr_mcap_inspect.py build/test_mcap/phase93_ros2_full_schema.mcap
# Inputs: Optional positional MCAP path; invokes the runtime validation project.
# Outputs: Strictly validates 41 ros2msg schemas, 41 cdr channels, and 41 CDR payloads.

"""Generate or inspect the Phase 93 ROS 2 .msg + CDR full-schema MCAP smoke fixture."""

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
DEFAULT_OUTPUT = REPO_ROOT / "build" / "test_mcap" / "phase93_ros2_full_schema.mcap"


def resolve_path(path: str | None) -> Path:
    """Resolve an optional MCAP path relative to the repository root."""
    if not path:
        return DEFAULT_OUTPUT

    candidate = Path(path)
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


def run_dotnet(*runtime_args: str) -> int:
    """Run the runtime validation project with the supplied arguments."""
    cmd = [
        "dotnet",
        "run",
        "--no-restore",
        "--project",
        str(PROJECT),
        "--",
        *runtime_args,
    ]
    result = subprocess.run(cmd, cwd=REPO_ROOT, env=setup_nuget_cache())
    return result.returncode


def main() -> int:
    """Generate a default MCAP when needed, then run strict Phase93 inspection."""
    parser = argparse.ArgumentParser(
        description="Generate or inspect the Phase 93 ROS 2 .msg + CDR full-schema MCAP smoke fixture."
    )
    parser.add_argument(
        "mcap",
        nargs="?",
        default=None,
        help="Existing MCAP to inspect. If omitted, the script generates the default Phase93 smoke MCAP first.",
    )
    parser.add_argument(
        "--output",
        type=str,
        default=None,
        help="Output MCAP path to generate before inspection. Ignored when the positional MCAP path is provided.",
    )
    args = parser.parse_args()

    if shutil.which("dotnet") is None:
        print("[phase93] dotnet was not found on PATH.", file=sys.stderr)
        return EXIT_FAILURE

    mcap_path = resolve_path(args.mcap or args.output)
    if args.mcap is None:
        mcap_path.parent.mkdir(parents=True, exist_ok=True)
        rc = run_dotnet("--phase93-ros2-full-mcap", str(mcap_path))
        if rc != EXIT_SUCCESS:
            print(f"[phase93] dotnet generation exited with code {rc}", file=sys.stderr)
            return rc

    if not mcap_path.is_file():
        print(f"[phase93] ROS 2 CDR full-schema MCAP was not found: {mcap_path}", file=sys.stderr)
        return EXIT_FAILURE

    size = mcap_path.stat().st_size
    if size <= EMPTY_FILE_SIZE_BYTES:
        print(f"[phase93] ROS 2 CDR full-schema MCAP is empty: {mcap_path}", file=sys.stderr)
        return EXIT_FAILURE

    rc = run_dotnet("--phase93-inspect-mcap", str(mcap_path))
    if rc != EXIT_SUCCESS:
        print(f"[phase93] dotnet inspect exited with code {rc}", file=sys.stderr)
        return rc

    print(f"[phase93] ROS 2 CDR full-schema MCAP: {mcap_path}")
    print(f"[phase93] size: {size} bytes")
    return EXIT_SUCCESS


if __name__ == "__main__":
    raise SystemExit(main())
