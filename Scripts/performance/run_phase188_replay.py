#!/usr/bin/env python3
"""Run the deterministic Phase188 replay benchmark and validate its result."""

from __future__ import annotations

import argparse
import json
import pathlib
import subprocess
import sys
import time


ROOT = pathlib.Path(__file__).resolve().parents[2]
PROJECT = ROOT / "Packages" / "dev.unity2foxglove.sdk" / "Tests" / "Performance" / "FoxgloveSdk.Performance.csproj"


def latest_result(directory: pathlib.Path) -> tuple[pathlib.Path, dict]:
    """Load the newest Phase188 result JSON from a benchmark directory."""
    results = sorted(directory.glob("phase188-replay_*.json"), key=lambda p: p.stat().st_mtime_ns)
    if not results:
        raise RuntimeError(f"no Phase188 result JSON in {directory}")
    path = results[-1]
    return path, json.loads(path.read_text(encoding="utf-8"))


def compare_results(baseline_dir: pathlib.Path, candidate_dir: pathlib.Path, output: pathlib.Path) -> dict:
    """Compare paired deterministic runs without claiming an unmeasured noise band."""
    baseline_path, baseline = latest_result(baseline_dir)
    candidate_path, candidate = latest_result(candidate_dir)
    if baseline.get("fixtureHashSha256") != candidate.get("fixtureHashSha256"):
        raise RuntimeError("baseline and candidate fixtures differ")
    required = ("p50Milliseconds", "p95Milliseconds", "p99Milliseconds", "returnedMessages")
    for label, payload in (("baseline", baseline), ("candidate", candidate)):
        missing = [key for key in required if key not in payload]
        if missing:
            raise RuntimeError(f"{label} result missing: {', '.join(missing)}")
    def ratio(name: str) -> float:
        """Return candidate-over-baseline ratio, using zero for empty baselines."""
        value = float(candidate[name])
        return value / float(baseline[name]) if float(baseline[name]) > 0 else 0.0
    comparison = {
        "generatedAtUnix": time.time(),
        "fixtureHashSha256": baseline["fixtureHashSha256"],
        "baseline": {"path": str(baseline_path), "implementation": baseline.get("implementation"),
                     "p50Milliseconds": baseline["p50Milliseconds"], "p95Milliseconds": baseline["p95Milliseconds"],
                     "p99Milliseconds": baseline["p99Milliseconds"], "returnedMessages": baseline["returnedMessages"],
                     "decompressedChunks": baseline.get("decompressedChunks"), "headersScanned": baseline.get("headersScanned")},
        "candidate": {"path": str(candidate_path), "implementation": candidate.get("implementation"),
                      "p50Milliseconds": candidate["p50Milliseconds"], "p95Milliseconds": candidate["p95Milliseconds"],
                      "p99Milliseconds": candidate["p99Milliseconds"], "returnedMessages": candidate["returnedMessages"],
                      "decompressedChunks": candidate.get("decompressedChunks"), "headersScanned": candidate.get("headersScanned")},
        "latencyRatioCandidateOverBaseline": {"p50": ratio("p50Milliseconds"), "p95": ratio("p95Milliseconds"),
                                                "p99": ratio("p99Milliseconds")},
        "semanticParity": candidate["returnedMessages"] == baseline["returnedMessages"],
        "noiseBand": "not_estimated",
    }
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(comparison, indent=2, sort_keys=True), encoding="utf-8")
    return comparison


def main() -> int:
    """Run one or more deterministic replay benchmark passes and validate output."""
    parser = argparse.ArgumentParser()
    parser.add_argument("--quick", action="store_true")
    parser.add_argument("--full", action="store_true")
    parser.add_argument("--mode", choices=("baseline", "candidate"), default="candidate")
    parser.add_argument("--repeat", type=int, default=1)
    parser.add_argument("--output", required=True)
    parser.add_argument("--compare", nargs=2, metavar=("BASELINE_DIR", "CANDIDATE_DIR"))
    args = parser.parse_args()
    if args.compare:
        if args.quick or args.full:
            parser.error("--compare cannot be combined with --quick or --full")
        comparison = compare_results(pathlib.Path(args.compare[0]), pathlib.Path(args.compare[1]),
                                      pathlib.Path(args.output) / "phase188-replay-comparison.json")
        print(json.dumps(comparison, indent=2, sort_keys=True))
        return 0 if comparison["semanticParity"] else 1
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
        if args.mode == "baseline":
            command.append("--phase188-reference")
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
