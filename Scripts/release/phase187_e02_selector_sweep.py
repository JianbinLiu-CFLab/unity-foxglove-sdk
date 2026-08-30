"""Run the complete Round4 E02 runtime-selector process gate.

The sweep deliberately invokes the built runtime test assembly once per owned
selector.  It is kept as a release-process probe (rather than folded into the
general CI module list) so the E02 closure can prove selector reachability
without changing unrelated CI scheduling.
"""

from __future__ import annotations

import argparse
import json
import subprocess
from pathlib import Path


FLAGS = (
    "--phase115",
    "--phase115e",
    "--phase115f",
    "--phase147",
    "--phase154",
    "--phase163-23",
    "--phase163-24",
    "--phase164-12",
    "--phase164-22",
    "--phase164-23",
    "--phase164-24",
    "--phase164-43",
    "--phase164-49",
    "--phase175a",
    "--phase175b",
    "--phase175c",
    "--phase183a",
    "--phase184e",
)

ROOT = Path(__file__).resolve().parents[2]
PROJECT = ROOT / "Packages" / "dev.unity2foxglove.sdk" / "Tests" / "Runtime" / "FoxgloveSdk.Tests.csproj"


def run_sweep(timeout_seconds: int = 180) -> list[dict[str, object]]:
    """Invoke every selector in the frozen E02 order and return raw results."""

    results: list[dict[str, object]] = []
    for flag in FLAGS:
        try:
            completed = subprocess.run(
                [
                    "dotnet",
                    "run",
                    "--project",
                    str(PROJECT),
                    "--no-build",
                    "--no-restore",
                    "--",
                    flag,
                ],
                cwd=ROOT,
                capture_output=True,
                text=True,
                encoding="utf-8",
                errors="replace",
                timeout=timeout_seconds,
                check=False,
            )
            results.append(
                {
                    "flag": flag,
                    "exit": completed.returncode,
                    "stdout": completed.stdout,
                    "stderr": completed.stderr,
                }
            )
        except subprocess.TimeoutExpired as exc:
            stdout = exc.stdout or ""
            stderr = (exc.stderr or "") + "\nTIMEOUT"
            results.append({"flag": flag, "exit": 124, "stdout": stdout, "stderr": stderr})
    return results


def main() -> int:
    """Parse options, run the E02 selector sweep, and return its status."""

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--expect", choices=("green", "red"), default="green")
    parser.add_argument("--json", type=Path, help="write raw per-selector results to this path")
    args = parser.parse_args()

    results = run_sweep()
    if args.json:
        args.json.parent.mkdir(parents=True, exist_ok=True)
        args.json.write_text(json.dumps(results, ensure_ascii=False, indent=2), encoding="utf-8")

    failed = [result for result in results if result["exit"] != 0]
    failed_flags = {str(result["flag"]) for result in failed}
    print(f"E02_SELECTOR_SWEEP: {len(results) - len(failed)}/{len(results)} passed; failures={len(failed)}")
    for result in results:
        print(f"{result['flag']}: exit={result['exit']}")

    if args.expect == "green":
        return 0 if len(results) == len(FLAGS) and not failed else 1

    expected_red = {
        "--phase147",
        "--phase154",
        "--phase163-23",
        "--phase164-22",
        "--phase164-23",
        "--phase164-24",
        "--phase164-49",
    }
    return 0 if len(failed) == len(expected_red) and failed_flags == expected_red else 1


if __name__ == "__main__":
    raise SystemExit(main())
