#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Validate that committed ROS 2 schema generated outputs match fresh generation.
# Usage: python Scripts/schema/validate_schema_generated_outputs.py

"""Validate committed ROS 2 schema generated outputs against fresh generator output."""

from __future__ import annotations

import subprocess
import sys
import tempfile
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parents[2]
CATALOG_SCRIPT = REPO_ROOT / "Scripts/schema/generate_ros2_msg_schema_catalog.py"
CDR_SCRIPT = REPO_ROOT / "Scripts/schema/generate_ros2_cdr_serializers.py"
COMMITTED_CATALOG = (
    REPO_ROOT
    / "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Ros2Msg/FoxgloveRos2MsgSchemaCatalog.cs"
)
COMMITTED_CDR_DIR = (
    REPO_ROOT
    / "Packages/dev.unity2foxglove.sdk/Runtime/Schemas/Ros2Msg/Generated"
)
EXPECTED_CDR_SOURCES = (
    "Ros2CdrGeneratedSerializers.g.cs",
    "Ros2CdrGeneratedDeserializers.g.cs",
    "Ros2CdrSerializerRegistry.g.cs",
    "Ros2CdrDeserializerRegistry.g.cs",
    "Ros2CdrSampleFactory.g.cs",
)


def rel(path: Path) -> str:
    """Return a repository-relative path string when possible."""
    try:
        return path.resolve().relative_to(REPO_ROOT.resolve()).as_posix()
    except ValueError:
        return path.as_posix()


def run_generator(command: list[str]) -> None:
    """Run one generator command, surfacing failures as subprocess exceptions."""
    subprocess.run(command, cwd=REPO_ROOT, check=True)


def compare_file(committed: Path, fresh: Path, failures: list[str]) -> None:
    """Append a failure when a committed file is missing or differs from fresh output."""
    if not committed.is_file():
        failures.append(f"missing committed file: {rel(committed)}")
        return
    if not fresh.is_file():
        failures.append(f"fresh generator did not produce: {fresh.name}")
        return
    if committed.read_bytes() != fresh.read_bytes():
        failures.append(
            f"stale generated output: {rel(committed)} "
            f"(run the matching generator and commit the result)"
        )


def validate_generated_outputs() -> list[str]:
    """Return generated-output freshness failures, or an empty list when current."""
    failures: list[str] = []
    build_root = REPO_ROOT / "build"
    build_root.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="u2f_schema_generated_", dir=build_root) as temp:
        temp_root = Path(temp)
        fresh_catalog = temp_root / "FoxgloveRos2MsgSchemaCatalog.cs"
        fresh_cdr = temp_root / "Generated"

        run_generator(["python", str(CATALOG_SCRIPT), "--output", str(fresh_catalog)])
        run_generator(["python", str(CDR_SCRIPT), "--output-dir", str(fresh_cdr)])

        compare_file(COMMITTED_CATALOG, fresh_catalog, failures)
        for name in EXPECTED_CDR_SOURCES:
            compare_file(COMMITTED_CDR_DIR / name, fresh_cdr / name, failures)

    return failures


def main() -> int:
    """Run validation and return a process exit code."""
    try:
        failures = validate_generated_outputs()
    except subprocess.CalledProcessError as exc:
        print(f"[FAIL] Schema generator command failed with exit code {exc.returncode}: {exc.cmd}", file=sys.stderr)
        return 1

    if failures:
        print("[FAIL] ROS 2 schema generated outputs are stale:", file=sys.stderr)
        for failure in failures:
            print(f"  {failure}", file=sys.stderr)
        return 1

    print("[PASS] ROS 2 schema generated outputs match fresh generation")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
