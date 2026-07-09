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
from concurrent.futures import ThreadPoolExecutor, as_completed
from dataclasses import dataclass
import os
import subprocess
import sys
import time
import uuid
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parents[2]
RUN_ID = os.environ.get("UNITY2FOXGLOVE_CI_RUN_ID") or f"{os.getpid()}-{uuid.uuid4().hex[:8]}"
CI_ROOT = REPO_ROOT / "build/ci" / RUN_ID
ISOLATED_DOTNET_ROOT = CI_ROOT / "dotnet"

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
DEFAULT_COMMAND_TIMEOUT_SECONDS = 600
DEFAULT_JOB_TIMEOUT_SECONDS = 1800
DEFAULT_PARALLEL_JOBS = 2


@dataclass(frozen=True)
class CiJob:
    """One isolated CI suite executed through a self-subcommand."""

    name: str
    command: list[str]


@dataclass(frozen=True)
class CiJobResult:
    """Captured result for one parallel CI suite."""

    name: str
    ok: bool
    returncode: int
    elapsed_seconds: float
    log_path: Path


def command_timeout_seconds() -> int:
    """Return the per-command CI timeout in seconds."""
    raw = os.environ.get("UNITY2FOXGLOVE_CI_TIMEOUT", "").strip()
    if not raw:
        return DEFAULT_COMMAND_TIMEOUT_SECONDS
    try:
        return max(1, int(raw))
    except ValueError:
        print(
            red(
                f"{FAIL} invalid UNITY2FOXGLOVE_CI_TIMEOUT={raw!r}; "
                f"using {DEFAULT_COMMAND_TIMEOUT_SECONDS}s"
            )
        )
        return DEFAULT_COMMAND_TIMEOUT_SECONDS


def job_timeout_seconds() -> int:
    """Return the per-suite CI timeout in seconds."""
    raw = os.environ.get("UNITY2FOXGLOVE_CI_JOB_TIMEOUT", "").strip()
    if not raw:
        return DEFAULT_JOB_TIMEOUT_SECONDS
    try:
        return max(1, int(raw))
    except ValueError:
        print(
            red(
                f"{FAIL} invalid UNITY2FOXGLOVE_CI_JOB_TIMEOUT={raw!r}; "
                f"using {DEFAULT_JOB_TIMEOUT_SECONDS}s"
            )
        )
        return DEFAULT_JOB_TIMEOUT_SECONDS


def default_parallel_jobs() -> int:
    """Return the default top-level local CI parallelism."""
    raw = os.environ.get("UNITY2FOXGLOVE_CI_JOBS", "").strip()
    if not raw:
        return DEFAULT_PARALLEL_JOBS
    try:
        return max(1, int(raw))
    except ValueError:
        print(
            red(
                f"{FAIL} invalid UNITY2FOXGLOVE_CI_JOBS={raw!r}; "
                f"using {DEFAULT_PARALLEL_JOBS}"
            )
        )
        return DEFAULT_PARALLEL_JOBS


def _msbuild_dir(path: Path) -> str:
    """Return an absolute MSBuild directory property value with a trailing slash."""
    normalized = str(path.resolve()).replace("\\", "/")
    return normalized if normalized.endswith("/") else normalized + "/"


def dotnet_msbuild_props(suite: str) -> list[str]:
    """Return isolated bin/obj/restore paths for one dotnet project suite."""
    suite_root = ISOLATED_DOTNET_ROOT / suite
    bin_root = suite_root / "bin"
    obj_root = suite_root / "obj"
    return [
        f"-p:BaseOutputPath={_msbuild_dir(bin_root)}",
        f"-p:BaseIntermediateOutputPath={_msbuild_dir(obj_root)}",
        f"-p:MSBuildProjectExtensionsPath={_msbuild_dir(obj_root)}",
        f"-p:RestoreOutputPath={_msbuild_dir(obj_root)}",
    ]


def validator_msbuild_args(msbuild_props: list[str]) -> list[str]:
    """Convert MSBuild properties into validator pass-through arguments."""
    args: list[str] = []
    for prop in msbuild_props:
        args.extend(["--msbuild-prop", prop])
    return args


ANALYZER_PROPS = dotnet_msbuild_props("analyzer")
ANALYZER_RUNTIME_TEST_PROPS = dotnet_msbuild_props("analyzer-runtime-tests")
RUNTIME_TEST_PROPS = dotnet_msbuild_props("runtime-tests")
UNIT_TEST_PROPS = dotnet_msbuild_props("unit-tests")
ANALYZER_OUTPUT_DIR = ISOLATED_DOTNET_ROOT / "analyzer-output"
UNIT_TEST_RESULTS_DIR = CI_ROOT / "test-results" / "unit"


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
    try:
        result = subprocess.run(cmd, cwd=REPO_ROOT, timeout=command_timeout_seconds())
    except subprocess.TimeoutExpired:
        print(red(f"{FAIL} {label} timed out after {command_timeout_seconds()}s"))
        if fatal:
            raise SystemExit(124)
        return False
    ok = result.returncode == 0
    if ok:
        print(green(f"{PASS} {label}"))
    else:
        print(red(f"{FAIL} {label} (exit {result.returncode})"))
        if fatal:
            raise SystemExit(result.returncode)
    return ok


def run_captured(cmd: list[str], label: str) -> tuple[str, bool, int, str, str]:
    """Run a subprocess and capture output for later ordered replay."""
    try:
        result = subprocess.run(
            cmd,
            cwd=REPO_ROOT,
            text=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            errors="replace",
            timeout=command_timeout_seconds(),
        )
    except subprocess.TimeoutExpired as ex:
        stdout = ex.stdout.decode(errors="replace") if isinstance(ex.stdout, bytes) else (ex.stdout or "")
        stderr = ex.stderr.decode(errors="replace") if isinstance(ex.stderr, bytes) else (ex.stderr or "")
        stderr += f"\n{FAIL} {label} timed out after {command_timeout_seconds()}s\n"
        return label, False, 124, stdout, stderr
    return label, result.returncode == 0, result.returncode, result.stdout, result.stderr


def run_parallel(commands: list[tuple[str, list[str]]]) -> dict[str, bool]:
    """Run independent commands concurrently and replay their output in declaration order."""
    print(f"\n{cyan('--- package validators (parallel) ---')}")
    results_by_label: dict[str, tuple[bool, int, str, str]] = {}
    with ThreadPoolExecutor(max_workers=len(commands)) as executor:
        futures = {
            executor.submit(run_captured, cmd, label): label
            for label, cmd in commands
        }
        for future in as_completed(futures):
            label, ok, returncode, stdout, stderr = future.result()
            results_by_label[label] = (ok, returncode, stdout, stderr)

    ordered_results: dict[str, bool] = {}
    for label, _ in commands:
        ok, returncode, stdout, stderr = results_by_label[label]
        print(f"\n{cyan('--- ' + label + ' ---')}")
        if stdout:
            print(stdout, end="" if stdout.endswith("\n") else "\n")
        if stderr:
            print(stderr, end="" if stderr.endswith("\n") else "\n", file=sys.stderr)
        if ok:
            print(green(f"{PASS} {label}"))
        else:
            print(red(f"{FAIL} {label} (exit {returncode})"))
        ordered_results[label] = ok
    return ordered_results


def build_default_ci_jobs(args: argparse.Namespace) -> list[CiJob]:
    """Build independent self-subcommands for the default local CI run."""
    script = str(Path(__file__).resolve())
    jobs: list[CiJob] = []
    if not args.skip_analyzer:
        jobs.append(CiJob("analyzer", [sys.executable, script, "--only", "analyzer"]))
    jobs.extend(
        [
            CiJob("dotnet", [sys.executable, script, "--only", "dotnet"]),
            CiJob("packages", [sys.executable, script, "--only", "packages"]),
            CiJob("boundary", [sys.executable, script, "--only", "boundary"]),
        ]
    )
    return jobs


def _job_log_tail(log_path: Path, *, max_chars: int = 8000) -> str:
    """Return the tail of a job log for concise failure diagnostics."""
    try:
        text = log_path.read_text(encoding="utf-8", errors="replace")
    except OSError as exc:
        return f"<failed to read {log_path}: {exc}>"
    if len(text) <= max_chars:
        return text
    return "... <log truncated to tail> ...\n" + text[-max_chars:]


def _run_ci_job(job: CiJob, log_dir: Path) -> CiJobResult:
    """Run one CI suite as a captured self-subcommand."""
    log_path = log_dir / f"{job.name}.log"
    env = os.environ.copy()
    env["UNITY2FOXGLOVE_CI_RUN_ID"] = RUN_ID
    env.setdefault("PYTHONUNBUFFERED", "1")
    start = time.monotonic()
    try:
        result = subprocess.run(
            job.command,
            cwd=REPO_ROOT,
            env=env,
            text=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            errors="replace",
            timeout=job_timeout_seconds(),
        )
        elapsed = time.monotonic() - start
        log_path.write_text(result.stdout or "", encoding="utf-8")
        return CiJobResult(job.name, result.returncode == 0, result.returncode, elapsed, log_path)
    except subprocess.TimeoutExpired as ex:
        elapsed = time.monotonic() - start
        stdout = ex.stdout.decode(errors="replace") if isinstance(ex.stdout, bytes) else (ex.stdout or "")
        timeout_message = (
            f"\n{FAIL} {job.name} timed out after {job_timeout_seconds()}s "
            f"({elapsed:.1f}s elapsed)\n"
        )
        log_path.write_text(stdout + timeout_message, encoding="utf-8")
        return CiJobResult(job.name, False, 124, elapsed, log_path)


def run_ci_jobs(jobs: list[CiJob], max_workers: int) -> dict[str, bool]:
    """Run top-level CI suites in parallel through captured self-subcommands."""
    if not jobs:
        return {}

    worker_count = max(1, min(max_workers, len(jobs)))
    log_dir = CI_ROOT / "logs"
    log_dir.mkdir(parents=True, exist_ok=True)

    print(f"\n{cyan('--- local CI jobs (parallel) ---')}")
    print(f"Run id: {RUN_ID}")
    print(f"Logs: {log_dir}")
    print(f"Workers: {worker_count}")
    for job in jobs:
        print(f"  - {job.name}: {' '.join(job.command)}")

    results_by_name: dict[str, CiJobResult] = {}
    with ThreadPoolExecutor(max_workers=worker_count) as executor:
        futures = {executor.submit(_run_ci_job, job, log_dir): job.name for job in jobs}
        for future in as_completed(futures):
            result = future.result()
            results_by_name[result.name] = result
            status = PASS if result.ok else FAIL
            colour = green if result.ok else red
            print(
                colour(
                    f"{status} {result.name} "
                    f"({result.elapsed_seconds:.1f}s, log: {result.log_path})"
                )
            )
            if not result.ok:
                print(_job_log_tail(result.log_path), file=sys.stderr)

    return {job.name: results_by_name[job.name].ok for job in jobs}


def restore_with_ignoring_failed_sources(
    project: str,
    label: str,
    msbuild_props: list[str] | None = None,
) -> bool:
    """Restore a project while allowing ignored failed sources."""
    msbuild_props = msbuild_props or []
    cmd = ["dotnet", "restore", project, *msbuild_props, *IGNORE_FAILED_SOURCES_OPTION]
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


def _check_changelog_verified_stubs() -> bool:
    """Fail if release changelog verification stubs were not replaced."""
    stub = "should be run before tagging this release"
    path = REPO_ROOT / "CHANGELOG.md"
    text = path.read_text(encoding="utf-8")
    if stub not in text:
        print(f"\n{green(PASS)} Changelog verified sections contain no release stubs")
        return True

    print(f"\n{red('FAIL')} CHANGELOG.md still contains release verification stub text:")
    for index, line in enumerate(text.splitlines(), start=1):
        if stub in line:
            print(f"  CHANGELOG.md:{index}: {line}")
    return False


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
    parser.add_argument(
        "--jobs",
        type=int,
        default=default_parallel_jobs(),
        help="Top-level parallel job count for default CI runs.",
    )
    args = parser.parse_args()

    results: dict[str, bool] = {}
    all_pass = True

    if args.only is None:
        results.update(run_ci_jobs(build_default_ci_jobs(args), args.jobs))

        print(f"\n{'=' * 60}")
        for name, ok in results.items():
            print(f"  {green(PASS) if ok else red(FAIL)} {name}")

        if all(results.values()):
            print(f"\n{green('All CI checks passed.')}")
            return 0

        failed = [n for n, ok in results.items() if not ok]
        print(f"\n{red('Failed: ' + ', '.join(failed))}")
        return 1

    # --- analyzer build + freshness ---
    if args.only in (None, "analyzer"):
        if args.skip_analyzer:
            print(f"{SKIP} Analyzer build (--skip-analyzer)")
        else:
            results["analyzer-restore"] = restore_with_ignoring_failed_sources(
                SOURCE_GENERATOR_PROJ, "Restore Roslyn analyzer project", ANALYZER_PROPS
            )
            results["analyzer-build"] = run_with_restore_fallback(
                [
                    "dotnet", "build", SOURCE_GENERATOR_PROJ,
                    *ANALYZER_PROPS,
                    "-c", "Release",
                    "-o", str(ANALYZER_OUTPUT_DIR),
                    "--no-restore",
                ],
                [
                    "dotnet", "build", SOURCE_GENERATOR_PROJ,
                    *ANALYZER_PROPS,
                    "-c", "Release",
                    "-o", str(ANALYZER_OUTPUT_DIR),
                ],
                "Build Roslyn analyzer DLL",
            )
            results["analyzer-dll"] = run(
                [
                    sys.executable,
                    SOURCE_GENERATOR_VALIDATOR,
                    "--build-output-dir",
                    str(ANALYZER_OUTPUT_DIR),
                    *validator_msbuild_args(ANALYZER_PROPS),
                ],
                "Source generator DLL freshness"
            )
            if results.get("analyzer-build"):
                results["analyzer-runtime-restore"] = restore_with_ignoring_failed_sources(
                    RUNTIME_TESTS_PROJ,
                    "Restore runtime test project for analyzer freshness",
                    ANALYZER_RUNTIME_TEST_PROPS,
                )
                results["analyzer-freshness"] = run_with_restore_fallback(
                    [
                        "dotnet", "run", "--no-restore",
                        "--project", RUNTIME_TESTS_PROJ,
                        *ANALYZER_RUNTIME_TEST_PROPS,
                        "--", "--phase115f",
                    ],
                    [
                        "dotnet", "run",
                        "--project", RUNTIME_TESTS_PROJ,
                        *ANALYZER_RUNTIME_TEST_PROPS,
                        "--", "--phase115f",
                    ],
                    "Analyzer DLL freshness (--phase115f)",
                )

    # --- dotnet validation suite ---
    if args.only in (None, "dotnet"):
        results["dotnet-restore"] = restore_with_ignoring_failed_sources(
            RUNTIME_TESTS_PROJ, "Restore runtime test project", RUNTIME_TEST_PROPS
        )
        results["dotnet"] = run_with_restore_fallback(
            [
                "dotnet", "run", "--no-restore",
                "--project", RUNTIME_TESTS_PROJ,
                *RUNTIME_TEST_PROPS,
            ],
            [
                "dotnet", "run",
                "--project", RUNTIME_TESTS_PROJ,
                *RUNTIME_TEST_PROPS,
            ],
            "Dotnet validation suite (default CI)",
        )
        results["mcap-conformance-ci-smoke"] = run(
            [
                sys.executable,
                "Scripts/mcap/conformance/run_phase121_conformance.py",
                "--ci-smoke",
                "--report-path",
                "build/mcap-conformance/phase121-conformance-ci-smoke.json",
            ],
            "MCAP conformance wrapper CI smoke",
        )
        results["xunit-restore"] = restore_with_ignoring_failed_sources(
            UNIT_TESTS_PROJ, "Restore xUnit unit test project", UNIT_TEST_PROPS
        )
        results["xunit"] = run_with_restore_fallback(
            [
                "dotnet", "test",
                "--no-restore", UNIT_TESTS_PROJ,
                *UNIT_TEST_PROPS,
                "--logger", "trx;LogFileName=unit-tests.trx",
                "--results-directory", str(UNIT_TEST_RESULTS_DIR),
            ],
            [
                "dotnet", "test", UNIT_TESTS_PROJ,
                *UNIT_TEST_PROPS,
                "--logger", "trx;LogFileName=unit-tests.trx",
                "--results-directory", str(UNIT_TEST_RESULTS_DIR),
            ],
            "xUnit unit tests",
        )

    # --- package validators ---
    if args.only in (None, "packages"):
        package_results = run_parallel([
            ("validate_unity_package.py", [sys.executable, "Scripts/package/validate_unity_package.py"]),
            ("validate_local_entrypoints.py", [sys.executable, "Scripts/package/validate_local_entrypoints.py"]),
            ("sync_full_demo.py", [sys.executable, "Scripts/samples/sync_full_demo.py", "--mode", "validate"]),
            ("validate_schema_generated_outputs.py", [sys.executable, SCHEMA_GENERATED_OUTPUT_VALIDATOR]),
            (
                "validate_r2fu_runtime_package.py",
                [sys.executable, "Scripts/ros2forunity/windows/jazzy/validate_r2fu_runtime_package.py"],
            ),
            (
                "validate_ros2forunity_package.py",
                [sys.executable, "Scripts/ros2forunity/windows/jazzy/validate_ros2forunity_package.py"],
            ),
        ])
        results["validate-package"] = package_results["validate_unity_package.py"]
        results["validate-entrypoints"] = package_results["validate_local_entrypoints.py"]
        results["validate-full-demo-sync"] = package_results["sync_full_demo.py"]
        results["validate-schema-generated"] = package_results["validate_schema_generated_outputs.py"]
        results["validate-r2fu"] = package_results["validate_r2fu_runtime_package.py"]
        results["validate-adapter"] = package_results["validate_ros2forunity_package.py"]

    # --- boundary check ---
    if args.only in (None, "boundary"):
        boundary_ok = _check_boundary()
        results["boundary"] = boundary_ok
        results["changelog-verified"] = _check_changelog_verified_stubs()

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
