#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
"""Phase 35 performance baseline runner.

Usage:
  python Scripts/run_performance_baseline.py --quick
  python Scripts/run_performance_baseline.py --full
  python Scripts/run_performance_baseline.py --quick --output build/performance/
"""

import argparse
import json
import os
import shutil
import subprocess
import sys

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PROJECT = "Packages/dev.unity2foxglove.sdk/Tests/Performance/FoxgloveSdk.Performance.csproj"
OUTPUT_DIR_DEFAULT = "build/performance"

# Estimated data bounds for full-mode budget check
FULL_MODE_ESTIMATED_BYTES = 2_000_000_000  # ~2 GB approximate upper bound for 50 topics x 50K msg


def _resolve_output(args_output: str | None) -> str:
    if args_output:
        return args_output if os.path.isabs(args_output) else os.path.join(REPO_ROOT, args_output)
    return os.path.join(REPO_ROOT, OUTPUT_DIR_DEFAULT)


def _free_disk_bytes(path: str) -> int:
    usage = shutil.disk_usage(path)
    return usage.free


def _setup_nuget_cache() -> dict[str, str]:
    env = os.environ.copy()
    if "NUGET_PACKAGES" not in env:
        user_nuget = os.path.join(os.path.expanduser("~"), ".nuget", "packages")
        if os.path.isdir(user_nuget):
            env["NUGET_PACKAGES"] = user_nuget
            print(f"[perf-baseline] NuGet cache set to: {user_nuget}")
    return env


def main() -> int:
    parser = argparse.ArgumentParser(description="Phase 35 performance baseline runner")
    parser.add_argument("--quick", action="store_true", help="Quick mode (~1 minute)")
    parser.add_argument("--full", action="store_true", help="Full mode (release candidate)")
    parser.add_argument("--output", type=str, default=None, help="Output directory for JSON results")
    args = parser.parse_args()

    mode = "full" if args.full else "quick"
    output_dir = _resolve_output(args.output)
    os.makedirs(output_dir, exist_ok=True)

    # Disk space check
    free = _free_disk_bytes(output_dir)
    warn_gib = 1 if mode == "quick" else 5
    if free < warn_gib * 1024 * 1024 * 1024:
        print(f"[perf-baseline] WARNING: less than {warn_gib} GiB free on drive for {output_dir}")
        if mode == "full" and free < 500_000_000:
            print("[perf-baseline] ABORT: insufficient disk space for full mode.")
            return 1

    # Full mode budget estimate
    if mode == "full":
        print(f"[perf-baseline] Full mode estimated output budget: ~{FULL_MODE_ESTIMATED_BYTES / 1e9:.1f} GB")
        if free < FULL_MODE_ESTIMATED_BYTES * 1.2:
            print("[perf-baseline] ABORT: projected output exceeds available disk space.")
            return 1

    # Build + run
    env = _setup_nuget_cache()
    cmd = [
        "dotnet", "run",
        "--project", os.path.join(REPO_ROOT, PROJECT),
        "--", f"--{mode}", "--output", output_dir
    ]
    print(f"[perf-baseline] Running: {' '.join(cmd)}")

    result = subprocess.run(cmd, cwd=REPO_ROOT, env=env)
    if result.returncode != 0:
        print(f"[perf-baseline] dotnet process exited with code {result.returncode}")
        return result.returncode

    # Find and print summary from the latest JSON
    jsons = sorted(
        [f for f in os.listdir(output_dir) if f.startswith("phase35_performance") and f.endswith(".json")],
        reverse=True
    )
    if not jsons:
        print("[perf-baseline] No result JSON found.")
        return 1

    result_path = os.path.join(output_dir, jsons[0])
    with open(result_path, "r", encoding="utf-8") as f:
        data = json.load(f)

    scenarios = data.get("scenarios", [])
    all_passed = True
    for s in scenarios:
        status = "PASS" if s.get("passed") else "FAIL"
        name = s.get("name", "?")
        msg = s.get("messageCount", 0)
        elapsed = s.get("elapsedMs", 0)
        tp = s.get("messagesPerSecond", 0)
        bpm = s.get("allocatedBytesPerMessage", 0)
        print(f"[{status}] {name} - {msg} msgs, {elapsed}ms, {tp:.0f} msg/s, {bpm:.1f} B/msg")
        if not s.get("passed"):
            all_passed = False

    if all_passed:
        print("Performance baseline complete")
        print("Result: PASS")
        return 0
    else:
        print("Result: FAIL (see above)")
        return 1


if __name__ == "__main__":
    sys.exit(main())
