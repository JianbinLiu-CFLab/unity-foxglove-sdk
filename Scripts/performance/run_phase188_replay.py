#!/usr/bin/env python3
"""Run the deterministic Phase188 replay benchmark and validate its result."""

from __future__ import annotations

import argparse
import json
import pathlib
import subprocess
import sys


ROOT = pathlib.Path(__file__).resolve().parents[2]
PROJECT = ROOT / "Packages" / "dev.unity2foxglove.sdk" / "Tests" / "Performance" / "FoxgloveSdk.Performance.csproj"


def main() -> int:
    """Run one or more deterministic replay benchmark passes and validate output."""
    parser = argparse.ArgumentParser()
    parser.add_argument("--quick", action="store_true")
    parser.add_argument("--full", action="store_true")
    parser.add_argument("--mode", choices=("baseline",), default="baseline")
    parser.add_argument("--repeat", type=int, default=1)
    parser.add_argument("--output", required=True)
    args = parser.parse_args()
    if args.quick and args.full or not (args.quick or args.full):
        parser.error("choose exactly one of --quick or --full")
    if args.repeat < 1:
        parser.error("--repeat must be positive")

    output = pathlib.Path(args.output)
    output.mkdir(parents=True, exist_ok=True)
    mode_arg = "--full" if args.full else "--quick"
    hashes: list[str] = []
    for _ in range(args.repeat):
        command = [
            "dotnet", "run", "--project", str(PROJECT), "--",
            "--phase188", mode_arg, "--output", str(output),
            "--result-prefix", "phase188-replay",
        ]
        completed = subprocess.run(command, cwd=ROOT, text=True, capture_output=True)
        sys.stdout.write(completed.stdout)
        sys.stderr.write(completed.stderr)
        if completed.returncode != 0:
            return completed.returncode
        results = sorted(output.glob("phase188-replay_*.json"), key=lambda p: p.stat().st_mtime_ns)
        if not results:
            print("Phase188 result JSON was not produced.", file=sys.stderr)
            return 1
        data = json.loads(results[-1].read_text(encoding="utf-8"))
        required = ("fixtureHashSha256", "fixtureSeed", "p50Milliseconds", "p95Milliseconds", "p99Milliseconds", "payloadBytesCopied")
        if any(key not in data for key in required):
            print("Phase188 result is missing required deterministic fields.", file=sys.stderr)
            return 1
        if not isinstance(data["fixtureHashSha256"], str) or len(data["fixtureHashSha256"]) != 64:
            print("Phase188 fixture hash is not a 64-hex digest.", file=sys.stderr)
            return 1
        hashes.append(data["fixtureHashSha256"])

    if len(set(hashes)) != 1:
        print("Phase188 fixture hash changed across repeated runs.", file=sys.stderr)
        return 1
    print(f"Phase188 {args.mode} benchmark validated; fixture_sha256={hashes[0]}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
