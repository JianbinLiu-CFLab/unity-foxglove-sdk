#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Verify Phase 68 McapIndexedReader against a recorded MCAP file.
# Usage: python Scripts/smoke/phase68_indexed_reader_smoke.py [Unity2Foxglove/Recordings/file.mcap]
# Inputs: Optional MCAP path/glob; defaults to latest Unity2Foxglove/Recordings/*.mcap.
# Outputs: Runs the runtime validation project and prints indexed-reader smoke results.

"""Verify Phase 68 McapIndexedReader against a recorded MCAP file.

This script is intentionally thin: Unity is used to create the real recording,
then the runtime validation project opens that MCAP with McapIndexedReader so the
manual smoke exercises the same C# surface that Phase 68 added.
"""

from __future__ import annotations

import argparse
import glob
import os
from pathlib import Path
import shutil
import subprocess
import sys


# Number of parent directories between this script and the repository root.
REPO_ROOT_PARENT_DEPTH = 2

# Process exit codes returned by this smoke script.
EXIT_SUCCESS = 0
EXIT_FAILURE = 1

# File size threshold that separates an empty failed recording from a usable MCAP.
EMPTY_FILE_SIZE_BYTES = 0

REPO_ROOT = Path(__file__).resolve().parents[REPO_ROOT_PARENT_DEPTH]
PROJECT = REPO_ROOT / "Packages" / "dev.unity2foxglove.sdk" / "Tests" / "Runtime" / "FoxgloveSdk.Tests.csproj"
DEFAULT_RECORDING_DIR = REPO_ROOT / "Unity2Foxglove" / "Recordings"
DEFAULT_TOPICS = ("/tf", "/scene")


def contains_glob(value: str) -> bool:
    """Return True when a path string contains glob wildcard characters."""
    return any(token in value for token in ("*", "?", "["))


def is_readable_mcap(path: Path) -> bool:
    """Return True when the MCAP file can be opened for reading."""
    try:
        with path.open("rb") as handle:
            handle.read(1)
        return True
    except OSError as exc:
        print(f"[phase68] skipping unreadable MCAP: {path} ({exc})", file=sys.stderr, flush=True)
        return False


def select_latest_readable_mcap(matches: list[Path], description: str) -> Path:
    """Select the newest readable non-empty MCAP from a candidate list."""
    sorted_matches = sorted(matches, key=lambda path: path.stat().st_mtime, reverse=True)
    for match in sorted_matches:
        if not match.is_file():
            continue
        if match.stat().st_size <= EMPTY_FILE_SIZE_BYTES:
            print(f"[phase68] skipping empty MCAP: {match}", file=sys.stderr, flush=True)
            continue
        if is_readable_mcap(match):
            return match.resolve()

    raise FileNotFoundError(f"No readable non-empty MCAP files were found in {description}")


def latest_mcap_under(recording_dir: Path) -> Path:
    """Return the newest MCAP recording under the Unity demo Recordings folder."""
    if not recording_dir.is_dir():
        raise FileNotFoundError(f"Recording folder was not found: {recording_dir}")

    matches = list(recording_dir.glob("*.mcap"))
    if not matches:
        matches = list(recording_dir.rglob("*.mcap"))
    if not matches:
        raise FileNotFoundError(f"No .mcap files were found under: {recording_dir}")

    return select_latest_readable_mcap(matches, str(recording_dir))


def resolve_mcap(mcap: str | None) -> Path:
    """Resolve an optional MCAP path/glob relative to the repository root."""
    if not mcap:
        selected = latest_mcap_under(DEFAULT_RECORDING_DIR)
        print(f"[phase68] selected latest demo recording: {selected}", flush=True)
        return selected

    candidate = Path(mcap)
    if not candidate.is_absolute():
        candidate = REPO_ROOT / candidate

    pattern = str(candidate)
    if contains_glob(pattern):
        matches = [Path(match) for match in glob.glob(pattern)]
        if not matches:
            raise FileNotFoundError(f"No MCAP files matched: {pattern}")

        selected = select_latest_readable_mcap(matches, pattern)
        print(f"[phase68] selected latest MCAP match: {selected}", flush=True)
        return selected

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
    """Invoke the Phase 68 indexed-reader smoke check."""
    parser = argparse.ArgumentParser(description="Verify Phase 68 McapIndexedReader against a recorded MCAP file.")
    parser.add_argument(
        "mcap",
        nargs="?",
        help="Optional MCAP path/glob. Defaults to latest Unity2Foxglove/Recordings/*.mcap.",
    )
    parser.add_argument(
        "--mcap",
        dest="mcap_option",
        default=None,
        help="Explicit MCAP path/glob. Overrides the optional positional path.",
    )
    parser.add_argument(
        "--topic",
        action="append",
        default=None,
        help="Topic that must exist and return messages. Repeatable. Defaults to /tf and /scene.",
    )
    parser.add_argument(
        "--max-messages",
        type=int,
        default=5,
        help="Latest message sample size to request from McapIndexedReader.",
    )
    parser.add_argument(
        "--min-messages",
        type=int,
        default=1,
        help="Minimum sampled messages required for the overall and per-topic queries.",
    )
    args = parser.parse_args()

    if shutil.which("dotnet") is None:
        print("[phase68] dotnet was not found on PATH.", file=sys.stderr)
        return EXIT_FAILURE

    if args.mcap and args.mcap_option:
        print("[phase68] pass either positional MCAP or --mcap, not both.", file=sys.stderr)
        return EXIT_FAILURE

    try:
        mcap_path = resolve_mcap(args.mcap_option or args.mcap)
    except FileNotFoundError as exc:
        print(f"[phase68] {exc}", file=sys.stderr)
        return EXIT_FAILURE

    if not mcap_path.is_file():
        print(f"[phase68] MCAP file was not found: {mcap_path}", file=sys.stderr)
        return EXIT_FAILURE

    size = mcap_path.stat().st_size
    if size <= EMPTY_FILE_SIZE_BYTES:
        print(f"[phase68] MCAP file is empty: {mcap_path}", file=sys.stderr)
        return EXIT_FAILURE

    if not is_readable_mcap(mcap_path):
        print(f"[phase68] MCAP file cannot be opened for reading: {mcap_path}", file=sys.stderr)
        return EXIT_FAILURE

    topics = args.topic if args.topic else list(DEFAULT_TOPICS)
    cmd = [
        "dotnet",
        "run",
        "--no-restore",
        "--project",
        str(PROJECT),
        "--",
        "--phase68-indexed-reader-smoke",
        str(mcap_path),
        "--phase68-max-messages",
        str(args.max_messages),
        "--phase68-min-messages",
        str(args.min_messages),
    ]
    for topic in topics:
        cmd.extend(["--phase68-topic", topic])

    print(f"[phase68] MCAP: {mcap_path}", flush=True)
    print(f"[phase68] size: {size} bytes", flush=True)
    print(f"[phase68] required topics: {', '.join(topics)}", flush=True)

    result = subprocess.run(cmd, cwd=REPO_ROOT, env=setup_nuget_cache(), capture_output=True, text=True)
    if result.returncode != EXIT_SUCCESS:
        if result.stdout:
            print(result.stdout, file=sys.stderr, end="")
        if result.stderr:
            print(result.stderr, file=sys.stderr, end="")
        print(f"[phase68] dotnet process exited with code {result.returncode}", file=sys.stderr)
        return result.returncode

    return EXIT_SUCCESS


if __name__ == "__main__":
    raise SystemExit(main())
