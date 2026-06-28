#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Run local CI checks that match the GitHub Actions workflows.
# Usage:   python Scripts/release/run_ci.py
#          python Scripts/release/run_ci.py --skip-analyzer
#          python Scripts/release/run_ci.py --only dotnet

"""Run local CI checks (dotnet tests + package validators)."""

from __future__ import annotations

import argparse
import subprocess
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]

PASS = "[PASS]"
FAIL = "[FAIL]"
SKIP = "[SKIP]"
IGNORE_FAILED_SOURCES_OPTION = ["--ignore-failed-sources"]
RUNTIME_TESTS_PROJ = "Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj"
UNIT_TESTS_PROJ = "Packages/dev.unity2foxglove.sdk/Tests/Unit/FoxgloveSdk.UnitTests.csproj"
SOURCE_GENERATOR_PROJ = (
    "Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/FoxgloveLogSourceGenerator.csproj"
)
SOURCE_GENERATOR_VALIDATOR = "Scripts/package/validate_source_generator_dll.py"
SCHEMA_GENERATED_OUTPUT_VALIDATOR = "Scripts/schema/validate_schema_generated_outputs.py"


def green(msg: str) -> str:
    """Wrap a message with green ANSI colour codes."""
    return f"\033[32m{msg}\033[0m"


def red(msg: str) -> str:
    """Wrap a message with red ANSI colour codes."""
    return f"\033[31m{msg}\033[0m"


def cyan(msg: str) -> str:
    """Wrap a message with cyan ANSI colour codes."""
    return f"\033[36m{msg}\033[0m"


def run(cmd: list[str], label: str, *, fatal: bool = False) -> bool:
    """Run a subprocess command and return True on success."""
    print(f"\n{cyan('--- ' + label + ' ---')}")
    result = subprocess.run(cmd, cwd=REPO_ROOT)
    ok = result.returncode == 0
    if ok:
        print(green(f"{PASS} {label}"))
    else:
        print(red(f"{FAIL} {label} (exit {result.returncode})"))
        if fatal:
            raise SystemExit(result.returncode)
    return ok


def restore_with_ignoring_failed_sources(project: str, label: str) -> bool:
    """Restore a project while allowing ignored failed sources."""
    cmd = ["dotnet", "restore", project, *IGNORE_FAILED_SOURCES_OPTION]
    return run(cmd, label, fatal=True)


def run_with_restore_fallback(
    project_cmd: list[str],
    fallback_cmd: list[str],
    label: str,
) -> bool:
    """Run command with --no-restore first, then retry with restore."""
    if run(project_cmd, label, fatal=False):
        return True

    return run(fallback_cmd, f"{label} (retry with restore)")


def _check_boundary() -> bool:
    """Verify no tracked Plan/ or Developer/ files (matches repository-boundary-check)."""
    root_private = subprocess.run(
        ["git", "ls-files", "--", "Plan/**", "Developer/**"],
        capture_output=True, text=True, cwd=REPO_ROOT,
    )
    if root_private.returncode != 0:
        print(f"\n{red('FAIL')} git ls-files failed while checking Plan/Developer/:")
        print(root_private.stderr.strip())
        return False
    if root_private.stdout.strip():
        print(f"\n{red('FAIL')} Plan/ or Developer/ files are tracked and must not be:")
        print(root_private.stdout)
        return False

    all_tracked = subprocess.run(
        ["git", "ls-files"],
        capture_output=True, text=True, cwd=REPO_ROOT,
    )
    if all_tracked.returncode != 0:
        print(f"\n{red('FAIL')} git ls-files failed while checking nested Developer/ files:")
        print(all_tracked.stderr.strip())
        return False
    nested_dev = [
        path for path in all_tracked.stdout.splitlines()
        if "/Developer/" in path or path.endswith("/Developer.meta")
    ]
    if nested_dev:
        print(f"\n{red('FAIL')} Nested Developer/ files are tracked:")
        print("\n".join(nested_dev))
        return False

    print(f"\n{green(PASS)} Boundary check (no tracked Plan/Developer/)")
    return True


def main() -> int:
    """Parse args, run selected CI suites, and return exit code."""

    parser = argparse.ArgumentParser(description="Run local CI checks.")
    parser.add_argument(
        "--skip-analyzer",
        action="store_true",
        help="Skip Roslyn analyzer DLL build and freshness check.",
    )
    parser.add_argument(
        "--only",
        type=str,
        help="Run only one suite: dotnet, packages, boundary, analyzer",
    )
    args = parser.parse_args()

    results: dict[str, bool] = {}
    all_pass = True

    # --- analyzer build + freshness ---
    if args.only in (None, "analyzer"):
        if args.skip_analyzer:
            print(f"{SKIP} Analyzer build (--skip-analyzer)")
        else:
            results["analyzer-restore"] = restore_with_ignoring_failed_sources(
                SOURCE_GENERATOR_PROJ, "Restore Roslyn analyzer project"
            )
            results["analyzer-build"] = run_with_restore_fallback(
                [
                    "dotnet", "build", SOURCE_GENERATOR_PROJ,
                    "-c", "Release",
                    "-o", "build/SourceGenerators/Release/netstandard2.0",
                    "--no-restore",
                ],
                [
                    "dotnet", "build", SOURCE_GENERATOR_PROJ,
                    "-c", "Release",
                    "-o", "build/SourceGenerators/Release/netstandard2.0",
                ],
                "Build Roslyn analyzer DLL",
            )
            results["analyzer-dll"] = run(
                [sys.executable, SOURCE_GENERATOR_VALIDATOR],
                "Source generator DLL freshness"
            )
            if results.get("analyzer-build"):
                results["analyzer-freshness"] = run_with_restore_fallback(
                    [
                        "dotnet", "run", "--no-restore",
                        "--project", RUNTIME_TESTS_PROJ,
                        "--", "--phase115f",
                    ],
                    [
                        "dotnet", "run",
                        "--project", RUNTIME_TESTS_PROJ,
                        "--", "--phase115f",
                    ],
                    "Analyzer DLL freshness (--phase115f)",
                )

    # --- dotnet validation suite ---
    if args.only in (None, "dotnet"):
        results["dotnet-restore"] = restore_with_ignoring_failed_sources(
            RUNTIME_TESTS_PROJ, "Restore runtime test project"
        )
        results["dotnet"] = run_with_restore_fallback(
            ["dotnet", "run", "--no-restore", "--project", RUNTIME_TESTS_PROJ],
            ["dotnet", "run", "--project", RUNTIME_TESTS_PROJ],
            "Dotnet validation suite (default CI)",
        )
        results["xunit-restore"] = restore_with_ignoring_failed_sources(
            UNIT_TESTS_PROJ, "Restore xUnit unit test project"
        )
        results["xunit"] = run_with_restore_fallback(
            [
                "dotnet", "test",
                "--no-restore", UNIT_TESTS_PROJ,
                "--logger", "trx;LogFileName=unit-tests.trx",
                "--results-directory", "build/TestResults/Unit",
            ],
            [
                "dotnet", "test", UNIT_TESTS_PROJ,
                "--logger", "trx;LogFileName=unit-tests.trx",
                "--results-directory", "build/TestResults/Unit",
            ],
            "xUnit unit tests",
        )

    # --- package validators ---
    if args.only in (None, "packages"):
        results["validate-package"] = run(
            [sys.executable, "Scripts/package/validate_unity_package.py"],
            "validate_unity_package.py"
        )
        results["validate-entrypoints"] = run(
            [sys.executable, "Scripts/package/validate_local_entrypoints.py"],
            "validate_local_entrypoints.py"
        )
        results["validate-schema-generated"] = run(
            [sys.executable, SCHEMA_GENERATED_OUTPUT_VALIDATOR],
            "validate_schema_generated_outputs.py"
        )
        results["validate-r2fu"] = run(
            [sys.executable, "Scripts/ros2forunity/windows/jazzy/validate_r2fu_runtime_package.py"],
            "validate_r2fu_runtime_package.py"
        )
        results["validate-adapter"] = run(
            [sys.executable, "Scripts/ros2forunity/windows/jazzy/validate_ros2forunity_package.py"],
            "validate_ros2forunity_package.py"
        )

    # --- boundary check ---
    if args.only in (None, "boundary"):
        boundary_ok = _check_boundary()
        results["boundary"] = boundary_ok

    # --- summary ---
    print(f"\n{'=' * 60}")
    for name, ok in results.items():
        print(f"  {green(PASS) if ok else red(FAIL)} {name}")

    for ok in results.values():
        if not ok:
            all_pass = False

    if all_pass:
        print(f"\n{green('All CI checks passed.')}")
    else:
        failed = [n for n, ok in results.items() if not ok]
        print(f"\n{red('Failed: ' + ', '.join(failed))}")
    return 0 if all_pass else 1


if __name__ == "__main__":
    raise SystemExit(main())
