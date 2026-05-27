#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Run Phase 35 performance baseline scenarios through the .NET performance project.
# Usage: python Scripts/performance/run_baseline.py --quick
# Inputs: --quick or --full, optional --output directory.
# Outputs: Writes performance JSON results under build/performance/ by default.

"""Phase 35 performance baseline runner.

Usage:
  python Scripts/performance/run_baseline.py --quick
  python Scripts/performance/run_baseline.py --full
  python Scripts/performance/run_baseline.py --quick --output build/performance/
"""

import argparse
import json
import os
import shutil
import subprocess
import sys

# Number of parent directories between this file and the repository root.
REPO_ROOT_PARENT_DEPTH = 3

# Process exit codes returned by this script.
EXIT_SUCCESS = 0
EXIT_FAILURE = 1

# Binary GiB conversion used for conservative disk-space checks.
BYTES_PER_GIB = 1024 * 1024 * 1024

# Decimal GB conversion used only for human-readable budget output.
BYTES_PER_DECIMAL_GB = 1_000_000_000.0

# Minimum free space warnings by benchmark mode.
QUICK_MODE_MIN_FREE_GIB = 1
FULL_MODE_MIN_FREE_GIB = 5

# Hard lower bound for full mode; below this, the run is not useful enough to start.
FULL_MODE_ABORT_FREE_BYTES = 500_000_000

# Safety margin applied to the estimated full-mode output budget.
FULL_MODE_SAFETY_MULTIPLIER = 1.2

# Default metric value used when an older JSON summary omits an optional metric.
MISSING_METRIC_DEFAULT = 0

# The sorted result list puts the latest timestamped JSON first.
LATEST_RESULT_INDEX = 0
DEFAULT_QUICK_TIMEOUT_MINUTES = 30
DEFAULT_FULL_TIMEOUT_MINUTES = 180
SECONDS_PER_MINUTE = 60
EXIT_TIMEOUT = 124

REPO_ROOT = os.path.abspath(__file__)
for _ in range(REPO_ROOT_PARENT_DEPTH):
    REPO_ROOT = os.path.dirname(REPO_ROOT)
PROJECT = "Packages/dev.unity2foxglove.sdk/Tests/Performance/FoxgloveSdk.Performance.csproj"
OUTPUT_DIR_DEFAULT = "build/performance"

# Estimated data bounds for full-mode disk-space budget checks.
FULL_MODE_ESTIMATED_BYTES = 2_000_000_000  # ~2 GB approximate upper bound for 50 topics x 50K msg


def _resolve_output(args_output: str | None) -> str:
    """Resolve the output directory from an optional CLI value."""
    if args_output:
        return args_output if os.path.isabs(args_output) else os.path.join(REPO_ROOT, args_output)
    return os.path.join(REPO_ROOT, OUTPUT_DIR_DEFAULT)


def _free_disk_bytes(path: str) -> int:
    """Return free bytes on the filesystem that contains the given path."""
    usage = shutil.disk_usage(path)
    return usage.free


def _setup_nuget_cache() -> dict[str, str]:
    """Reuse the user-level NuGet package cache when the environment has no cache set."""
    env = os.environ.copy()
    if "NUGET_PACKAGES" not in env:
        user_nuget = os.path.join(os.path.expanduser("~"), ".nuget", "packages")
        if os.path.isdir(user_nuget):
            env["NUGET_PACKAGES"] = user_nuget
            print(f"[perf-baseline] NuGet cache set to: {user_nuget}")
    return env


def main() -> int:
    """Run the selected performance baseline mode and print a compact summary."""
    parser = argparse.ArgumentParser(description="Phase 35 performance baseline runner")
    parser.add_argument("--quick", action="store_true", help="Quick mode (~1 minute)")
    parser.add_argument("--full", action="store_true", help="Full mode (release candidate)")
    parser.add_argument("--output", type=str, default=None, help="Output directory for JSON results")
    parser.add_argument(
        "--timeout-minutes",
        type=int,
        default=None,
        help="Maximum dotnet runtime before failing. Defaults are mode-specific; use 0 to disable.",
    )
    args = parser.parse_args()

    mode = "full" if args.full else "quick"
    default_timeout = DEFAULT_FULL_TIMEOUT_MINUTES if mode == "full" else DEFAULT_QUICK_TIMEOUT_MINUTES
    timeout_minutes = default_timeout if args.timeout_minutes is None else args.timeout_minutes
    timeout_seconds = timeout_minutes * SECONDS_PER_MINUTE if timeout_minutes > 0 else None
    output_dir = _resolve_output(args.output)
    os.makedirs(output_dir, exist_ok=True)

    # Disk space check
    free = _free_disk_bytes(output_dir)
    warn_gib = QUICK_MODE_MIN_FREE_GIB if mode == "quick" else FULL_MODE_MIN_FREE_GIB
    if free < warn_gib * BYTES_PER_GIB:
        print(f"[perf-baseline] WARNING: less than {warn_gib} GiB free on drive for {output_dir}")
        if mode == "full" and free < FULL_MODE_ABORT_FREE_BYTES:
            print("[perf-baseline] ABORT: insufficient disk space for full mode.")
            return EXIT_FAILURE

    # Full mode budget estimate
    if mode == "full":
        print(f"[perf-baseline] Full mode estimated output budget: ~{FULL_MODE_ESTIMATED_BYTES / BYTES_PER_DECIMAL_GB:.1f} GB")
        if free < FULL_MODE_ESTIMATED_BYTES * FULL_MODE_SAFETY_MULTIPLIER:
            print("[perf-baseline] ABORT: projected output exceeds available disk space.")
            return EXIT_FAILURE

    # Build + run
    env = _setup_nuget_cache()
    cmd = [
        "dotnet", "run",
        "--project", os.path.join(REPO_ROOT, PROJECT),
        "--", f"--{mode}", "--output", output_dir
    ]
    print(f"[perf-baseline] Running: {' '.join(cmd)}")

    try:
        result = subprocess.run(cmd, cwd=REPO_ROOT, env=env, timeout=timeout_seconds)
    except subprocess.TimeoutExpired:
        print(f"[perf-baseline] dotnet process timed out after {timeout_minutes} minute(s)")
        return EXIT_TIMEOUT
    if result.returncode != EXIT_SUCCESS:
        print(f"[perf-baseline] dotnet process exited with code {result.returncode}")
        return result.returncode

    # Find and print summary from the latest JSON
    jsons = sorted(
        [f for f in os.listdir(output_dir) if f.startswith("phase35_performance") and f.endswith(".json")],
        reverse=True
    )
    if not jsons:
        print("[perf-baseline] No result JSON found.")
        return EXIT_FAILURE

    result_path = os.path.join(output_dir, jsons[LATEST_RESULT_INDEX])
    with open(result_path, "r", encoding="utf-8") as f:
        data = json.load(f)

    scenarios = data.get("scenarios", [])
    all_passed = True
    for s in scenarios:
        status = "PASS" if s.get("passed") else "FAIL"
        name = s.get("name", "?")
        msg = s.get("messageCount", MISSING_METRIC_DEFAULT)
        elapsed = s.get("elapsedMs", MISSING_METRIC_DEFAULT)
        tp = s.get("messagesPerSecond", MISSING_METRIC_DEFAULT)
        bpm = s.get("allocatedBytesPerMessage", MISSING_METRIC_DEFAULT)
        print(f"[{status}] {name} - {msg} msgs, {elapsed}ms, {tp:.0f} msg/s, {bpm:.1f} B/msg")
        if not s.get("passed"):
            all_passed = False

    if all_passed:
        print("Performance baseline complete")
        print("Result: PASS")
        return EXIT_SUCCESS
    else:
        print("Result: FAIL (see above)")
        return EXIT_FAILURE


if __name__ == "__main__":
    sys.exit(main())
