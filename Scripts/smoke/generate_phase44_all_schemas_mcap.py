#!/usr/bin/env python3
"""Generate the Phase 44 all-schema MCAP smoke fixture.

This script is intentionally thin: the MCAP generation logic lives in the
runtime validation project so the smoke fixture and automated tests use the
same protobuf catalog/sample path.
"""

from __future__ import annotations

import argparse
import os
from pathlib import Path
import shutil
import subprocess
import sys


REPO_ROOT = Path(__file__).resolve().parents[2]
PROJECT = REPO_ROOT / "Packages" / "dev.unity2foxglove.sdk" / "Tests" / "Runtime" / "FoxgloveSdk.Tests.csproj"
DEFAULT_OUTPUT = REPO_ROOT / "build" / "test_mcap" / "phase44_all_schemas_smoke.mcap"


def resolve_output(output: str | None) -> Path:
    if not output:
        return DEFAULT_OUTPUT

    candidate = Path(output)
    if not candidate.is_absolute():
        candidate = REPO_ROOT / candidate
    return candidate.resolve()


def setup_nuget_cache() -> dict[str, str]:
    env = os.environ.copy()
    if "NUGET_PACKAGES" not in env:
        user_nuget = Path.home() / ".nuget" / "packages"
        if user_nuget.is_dir():
            env["NUGET_PACKAGES"] = str(user_nuget)
    return env


def main() -> int:
    parser = argparse.ArgumentParser(description="Generate the Phase 44 all-schema MCAP smoke fixture.")
    parser.add_argument(
        "--output",
        type=str,
        default=None,
        help="Output MCAP path. Relative paths are resolved from the repository root.",
    )
    args = parser.parse_args()

    if shutil.which("dotnet") is None:
        print("[phase44] dotnet was not found on PATH.", file=sys.stderr)
        return 1

    output_path = resolve_output(args.output)
    output_path.parent.mkdir(parents=True, exist_ok=True)

    cmd = [
        "dotnet",
        "run",
        "--no-restore",
        "--project",
        str(PROJECT),
        "--",
        "--phase44-all-schemas-mcap",
        str(output_path),
    ]

    result = subprocess.run(cmd, cwd=REPO_ROOT, env=setup_nuget_cache())
    if result.returncode != 0:
        print(f"[phase44] dotnet process exited with code {result.returncode}", file=sys.stderr)
        return result.returncode

    if not output_path.is_file():
        print(f"[phase44] all-schema smoke MCAP was not generated: {output_path}", file=sys.stderr)
        return 1

    size = output_path.stat().st_size
    if size <= 0:
        print(f"[phase44] all-schema smoke MCAP is empty: {output_path}", file=sys.stderr)
        return 1

    print(f"[phase44] all-schema smoke MCAP: {output_path}")
    print(f"[phase44] size: {size} bytes")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
