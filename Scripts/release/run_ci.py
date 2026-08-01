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
from concurrent.futures import FIRST_COMPLETED, Future, ThreadPoolExecutor, as_completed, wait
from dataclasses import dataclass
import hashlib
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


def phase186_certification_run_id(head: str) -> str:
    identity = hashlib.sha256(RUN_ID.encode("utf-8")).hexdigest()[:6]
    return f"phase186h-cert-{head[:6]}{identity}"

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
FOXRUN_PUBLISH_PANEL_DIR = "Tools/foxglove-extensions/foxrun-publish-panel"
NPM_EXECUTABLE = "npm.cmd" if sys.platform == "win32" else "npm"
PHASE179_ROS2_INBOUND_ACCEPTANCE_REGRESSION = (
    "Scripts.smoke.ros2.regression_checks.test_phase179_foxrun_ros2_inbound_acceptance"
)
PHASE179_ROS2_PLAYER_HOST_REGRESSION = (
    "Scripts.smoke.ros2.regression_checks.test_phase179_foxrun_ros2_player_host"
)
PHASE179_ROS2_MATRIX_PROFILES_REGRESSION = (
    "Scripts.smoke.ros2.regression_checks.test_phase179_foxrun_ros2_matrix_profiles"
)
PHASE179_ZENOH_TOPOLOGY_REGRESSION = (
    "Scripts.smoke.ros2.regression_checks.test_phase179_zenoh_topology"
)
PHASE181_ROS2_PEER_PROTOCOL_REGRESSION = (
    "Scripts.smoke.ros2.regression_checks.test_phase181_custom_ros2_peer_protocol"
)
PHASE181_ROS2_PEER_REGRESSION = (
    "Scripts.smoke.ros2.regression_checks.test_phase181_custom_ros2_peer"
)
PHASE181_ROS2_MATRIX_PROFILES_REGRESSION = (
    "Scripts.smoke.ros2.regression_checks.test_phase181_custom_ros2_matrix_profiles"
)
PHASE181_ROS2_LINUX_PEER_REGRESSION = (
    "Scripts.smoke.ros2.regression_checks.test_phase181_custom_ros2_linux_peer"
)
PHASE184_PROFILE_ACCEPTANCE_PROTOCOL_REGRESSION = (
    "Scripts.smoke.foxrun.regression_checks.test_phase184_profile_acceptance_protocol"
)
PHASE184_PROFILE_ACCEPTANCE_ORCHESTRATOR_REGRESSION = (
    "Scripts.smoke.foxrun.regression_checks.test_phase184_profile_acceptance"
)
PHASE184_FOXGLOVE_DESKTOP_LIVE_PROTOCOL_REGRESSION = (
    "Scripts.smoke.foxrun.regression_checks.test_phase184_foxglove_desktop_live_protocol"
)
PHASE184_FOXGLOVE_CLI_INSTALL_REGRESSION = (
    "Scripts.smoke.foxrun.regression_checks.test_phase184_foxglove_cli_install"
)
PHASE184_WINDOWS_JOB_OWNER_REGRESSION = (
    "Scripts.smoke.foxrun.regression_checks.test_phase184_windows_job_owner"
)
PHASE184_FOXGLOVE_DESKTOP_LIVE_ACCEPTANCE_REGRESSION = (
    "Scripts.smoke.foxrun.regression_checks.test_phase184_foxglove_desktop_live_acceptance"
)
PHASE186_BRIDGE_TOOLING_REGRESSIONS = (
    "Scripts.smoke.foxrun.regression_checks.test_phase186_bridge_acceptance_protocol",
    "Scripts.smoke.foxrun.regression_checks.test_phase186_bridge_acceptance",
    "Scripts.smoke.foxrun.regression_checks.test_phase186_bridge_live",
    "Scripts.smoke.foxrun.regression_checks.test_phase186_bridge_certification",
    "Scripts.smoke.foxrun.regression_checks.test_phase186_bridge_build",
    "Scripts.smoke.foxrun.regression_checks.test_phase186_bridge_capability_probe",
    "Scripts.smoke.foxrun.regression_checks.test_phase186_provenance",
)
PHASE186_PACKAGE_MATRIX_VALIDATOR = "Scripts/package/validate_phase186_package_matrix.py"
PHASE186_PROVENANCE_MODULE = "Scripts.smoke.foxrun.phase186_provenance"
PHASE186_CERTIFICATION_MODULE = "Scripts.smoke.foxrun.phase186_bridge_certification"
PHASE181_INTERFACE_TOOLING_REGRESSIONS = (
    "Scripts.ros2forunity.interfaces.regression_checks.test_interface_digest",
    "Scripts.ros2forunity.interfaces.regression_checks.test_characterize_foxrun_custom_interface",
    "Scripts.ros2forunity.interfaces.regression_checks.test_build_foxrun_custom_typesupport_addon",
    "Scripts.ros2forunity.interfaces.regression_checks.test_refresh_phase181_custom_typesupport_addons",
    "Scripts.ros2forunity.interfaces.regression_checks.test_sync_foxrun_custom_typesupport_addon",
    "Scripts.ros2forunity.interfaces.regression_checks.test_validate_foxrun_custom_typesupport_addon",
    "Scripts.ros2forunity.interfaces.regression_checks.test_verify_foxrun_custom_typesupport_toolchain",
)
RELEASE_TOOLING_REGRESSION = "Scripts.release.regression_checks.test_release_tooling"
PHASE181_TYPESUPPORT_VALIDATOR = "Scripts/ros2forunity/interfaces/validate_foxrun_custom_typesupport_addon.py"
DEFAULT_COMMAND_TIMEOUT_SECONDS = 600
DEFAULT_JOB_TIMEOUT_SECONDS = 1800
DEFAULT_PARALLEL_JOBS = 2
DOTNET_CI_EXCLUSIVE_GROUP = "dotnet"


@dataclass(frozen=True)
class CiJob:
    """One isolated CI suite executed through a self-subcommand."""

    name: str
    command: list[str]
    disable_timeout: bool = False
    exclusive_group: str | None = None


@dataclass(frozen=True)
class CiJobResult:
    """Captured result for one parallel CI suite."""

    name: str
    ok: bool
    returncode: int
    elapsed_seconds: float
    log_path: Path


@dataclass(frozen=True)
class CapturedCommandResult:
    """Captured package-validator result with an optional enforced timeout limit."""

    label: str
    ok: bool
    returncode: int
    elapsed_seconds: float
    stdout: str
    stderr: str
    timeout_seconds: int | None = None


def current_git_head() -> str:
    """Return the exact tracked commit used to identity-bind live evidence."""

    completed = subprocess.run(
        ["git", "rev-parse", "HEAD"],
        cwd=REPO_ROOT,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=False,
    )
    head = completed.stdout.strip().lower()
    if completed.returncode != 0 or len(head) != 40:
        raise RuntimeError(
            "cannot resolve exact Git HEAD for Phase186 live certification: "
            + completed.stderr.strip()[:512]
        )
    try:
        int(head, 16)
    except ValueError as exc:
        raise RuntimeError(
            "Git HEAD is not a canonical 40-character hexadecimal object ID"
        ) from exc
    return head


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


def foxrun_publish_panel_npm(*args: str) -> list[str]:
    """Build an argument-array command for the checked-in FoxRun panel package."""
    return [NPM_EXECUTABLE, "--prefix", FOXRUN_PUBLISH_PANEL_DIR, *args]


def validator_msbuild_args(msbuild_props: list[str]) -> list[str]:
    """Convert MSBuild properties into validator pass-through arguments."""
    args: list[str] = []
    for prop in msbuild_props:
        args.append("--msbuild-prop=" + prop)
    return args


ANALYZER_PROPS = dotnet_msbuild_props("analyzer")
ANALYZER_RUNTIME_TEST_PROPS = dotnet_msbuild_props("analyzer-runtime-tests")
RUNTIME_TEST_PROPS = dotnet_msbuild_props("runtime-tests")
UNIT_TEST_PROPS = dotnet_msbuild_props("unit-tests")
UNIT_ADAPTER_TEST_PROPS = [
    *dotnet_msbuild_props("unit-tests-adapter"),
    "-p:IncludeRos2ForUnityAdapter=true",
]
UNIT_NATIVE_TEST_PROPS = [
    *dotnet_msbuild_props("unit-tests-native"),
    "-p:IncludeRos2ForUnityNative=true",
]
ANALYZER_OUTPUT_DIR = ISOLATED_DOTNET_ROOT / "analyzer-output"
UNIT_TEST_RESULTS_DIR = CI_ROOT / "test-results" / "unit"
UNIT_ADAPTER_TEST_RESULTS_DIR = CI_ROOT / "test-results" / "unit-adapter"
UNIT_NATIVE_TEST_RESULTS_DIR = CI_ROOT / "test-results" / "unit-native"


def green(msg: str) -> str:
    """Wrap a message with green ANSI colour codes."""
    return f"\033[32m{msg}\033[0m"


def red(msg: str) -> str:
    """Wrap a message with red ANSI colour codes."""
    return f"\033[31m{msg}\033[0m"


def cyan(msg: str) -> str:
    """Wrap a message with cyan ANSI colour codes."""
    return f"\033[36m{msg}\033[0m"


def run(
    cmd: list[str],
    label: str,
    *,
    fatal: bool = False,
    timeout_seconds: int | None = None,
    disable_timeout: bool = False,
) -> bool:
    """Run a subprocess command and return True on success."""
    print(f"\n{cyan('--- ' + label + ' ---')}")
    effective_timeout = (
        None
        if disable_timeout
        else command_timeout_seconds() if timeout_seconds is None else max(1, timeout_seconds)
    )
    start = time.monotonic()
    try:
        result = subprocess.run(cmd, cwd=REPO_ROOT, timeout=effective_timeout)
    except subprocess.TimeoutExpired:
        elapsed = time.monotonic() - start
        print(red(f"{FAIL} {label} timed out after {effective_timeout}s ({elapsed:.1f}s)"))
        if fatal:
            raise SystemExit(124)
        return False
    elapsed = time.monotonic() - start
    ok = result.returncode == 0
    if ok:
        print(green(f"{PASS} {label} ({elapsed:.1f}s)"))
    else:
        print(red(f"{FAIL} {label} (exit {result.returncode}) ({elapsed:.1f}s)"))
        if fatal:
            raise SystemExit(result.returncode)
    return ok


def run_captured(cmd: list[str], label: str) -> CapturedCommandResult:
    """Run a subprocess and capture output for later ordered replay."""
    effective_timeout = command_timeout_seconds()
    start = time.monotonic()
    try:
        result = subprocess.run(
            cmd,
            cwd=REPO_ROOT,
            text=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            errors="replace",
            timeout=effective_timeout,
        )
    except subprocess.TimeoutExpired as ex:
        elapsed = time.monotonic() - start
        stdout = ex.stdout.decode(errors="replace") if isinstance(ex.stdout, bytes) else (ex.stdout or "")
        stderr = ex.stderr.decode(errors="replace") if isinstance(ex.stderr, bytes) else (ex.stderr or "")
        return CapturedCommandResult(
            label,
            False,
            124,
            elapsed,
            stdout,
            stderr,
            timeout_seconds=effective_timeout,
        )
    elapsed = time.monotonic() - start
    return CapturedCommandResult(
        label,
        result.returncode == 0,
        result.returncode,
        elapsed,
        result.stdout,
        result.stderr,
    )


def run_parallel(commands: list[tuple[str, list[str]]]) -> dict[str, bool]:
    """Run independent commands concurrently and replay their output in declaration order."""
    print(f"\n{cyan('--- package validators (parallel) ---')}")
    results_by_label: dict[str, CapturedCommandResult] = {}
    with ThreadPoolExecutor(max_workers=len(commands)) as executor:
        futures = {
            executor.submit(run_captured, cmd, label): label
            for label, cmd in commands
        }
        for future in as_completed(futures):
            result = future.result()
            results_by_label[result.label] = result

    ordered_results: dict[str, bool] = {}
    for label, _ in commands:
        result = results_by_label[label]
        print(f"\n{cyan('--- ' + label + ' ---')}")
        if result.stdout:
            print(result.stdout, end="" if result.stdout.endswith("\n") else "\n")
        if result.stderr:
            print(result.stderr, end="" if result.stderr.endswith("\n") else "\n", file=sys.stderr)
        if result.ok:
            print(green(f"{PASS} {label} ({result.elapsed_seconds:.1f}s)"))
        elif result.timeout_seconds is not None:
            print(
                red(
                    f"{FAIL} {label} timed out after {result.timeout_seconds}s "
                    f"({result.elapsed_seconds:.1f}s)"
                )
            )
        else:
            print(red(f"{FAIL} {label} (exit {result.returncode}) ({result.elapsed_seconds:.1f}s)"))
        ordered_results[label] = result.ok
    return ordered_results


def build_dotnet_ci_jobs() -> list[CiJob]:
    """Build the independent runtime and xUnit lane self-subcommands."""
    script = str(Path(__file__).resolve())
    return [
        CiJob(
            "dotnet-runtime",
            [sys.executable, script, "--only", "dotnet-runtime"],
            exclusive_group=DOTNET_CI_EXCLUSIVE_GROUP,
        ),
        CiJob(
            "xunit",
            [sys.executable, script, "--only", "xunit"],
            exclusive_group=DOTNET_CI_EXCLUSIVE_GROUP,
        ),
        CiJob(
            "xunit-adapter",
            [sys.executable, script, "--only", "xunit-adapter"],
            exclusive_group=DOTNET_CI_EXCLUSIVE_GROUP,
        ),
        CiJob(
            "xunit-native",
            [sys.executable, script, "--only", "xunit-native"],
            exclusive_group=DOTNET_CI_EXCLUSIVE_GROUP,
        ),
    ]


def build_default_ci_jobs(args: argparse.Namespace) -> list[CiJob]:
    """Build independent self-subcommands for the default local CI run."""
    script = str(Path(__file__).resolve())
    jobs: list[CiJob] = []
    if not args.skip_analyzer:
        jobs.append(
            CiJob(
                "analyzer",
                [sys.executable, script, "--only", "analyzer"],
                exclusive_group=DOTNET_CI_EXCLUSIVE_GROUP,
            )
        )
    jobs.extend(build_dotnet_ci_jobs())
    jobs.extend(
        [
            CiJob("foxrun-publish-panel", [sys.executable, script, "--only", "foxrun-publish-panel"]),
            CiJob(
                "phase179-ros2-regression",
                [sys.executable, script, "--only", "phase179-ros2-regression"],
            ),
            CiJob(
                "phase181-ros2-regression",
                [sys.executable, script, "--only", "phase181-ros2-regression"],
            ),
            CiJob(
                "phase184-acceptance-tooling",
                [sys.executable, script, "--only", "phase184-acceptance-tooling"],
                disable_timeout=True,
            ),
            CiJob(
                "phase186-bridge-tooling",
                [sys.executable, script, "--only", "phase186-bridge-tooling"],
                disable_timeout=True,
            ),
            CiJob(
                "mcap-conformance",
                [sys.executable, script, "--only", "mcap-conformance"],
                disable_timeout=True,
            ),
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
    effective_timeout = None if job.disable_timeout else job_timeout_seconds()
    try:
        result = subprocess.run(
            job.command,
            cwd=REPO_ROOT,
            env=env,
            text=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            errors="replace",
            timeout=effective_timeout,
        )
        elapsed = time.monotonic() - start
        log_path.write_text(result.stdout or "", encoding="utf-8")
        return CiJobResult(job.name, result.returncode == 0, result.returncode, elapsed, log_path)
    except subprocess.TimeoutExpired as ex:
        elapsed = time.monotonic() - start
        stdout = ex.stdout.decode(errors="replace") if isinstance(ex.stdout, bytes) else (ex.stdout or "")
        timeout_description = (
            "without a configured wall-clock deadline"
            if effective_timeout is None
            else f"after {effective_timeout}s"
        )
        timeout_message = f"\n{FAIL} {job.name} timed out {timeout_description} ({elapsed:.1f}s elapsed)\n"
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
        pending = list(jobs)
        running: dict[Future[CiJobResult], CiJob] = {}
        active_groups: set[str] = set()

        while pending or running:
            while len(running) < worker_count:
                compatible_index = next(
                    (
                        index
                        for index, job in enumerate(pending)
                        if job.exclusive_group is None or job.exclusive_group not in active_groups
                    ),
                    None,
                )
                if compatible_index is None:
                    break

                job = pending.pop(compatible_index)
                if job.exclusive_group is not None:
                    active_groups.add(job.exclusive_group)
                running[executor.submit(_run_ci_job, job, log_dir)] = job

            if not running:
                raise RuntimeError("CI scheduler found no compatible pending job")

            completed, _ = wait(running, return_when=FIRST_COMPLETED)
            for future in completed:
                job = running.pop(future)
                try:
                    result = future.result()
                finally:
                    if job.exclusive_group is not None:
                        active_groups.remove(job.exclusive_group)

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


def report_ci_job_results(results: dict[str, bool]) -> int:
    """Print the standard top-level CI aggregate summary and return its exit code."""
    print(f"\n{'=' * 60}")
    for name, ok in results.items():
        print(f"  {green(PASS) if ok else red(FAIL)} {name}")

    if all(results.values()):
        print(f"\n{green('All CI checks passed.')}")
        return 0

    failed = [n for n, ok in results.items() if not ok]
    print(f"\n{red('Failed: ' + ', '.join(failed))}")
    return 1


def restore_with_ignoring_failed_sources(
    project: str,
    label: str,
    msbuild_props: list[str] | None = None,
    *,
    fatal: bool = True,
) -> bool:
    """Restore a project while allowing ignored failed sources."""
    msbuild_props = msbuild_props or []
    cmd = ["dotnet", "restore", project, *msbuild_props, *IGNORE_FAILED_SOURCES_OPTION]
    return run(cmd, label, fatal=fatal)


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
        help=(
            "Run only one suite: dotnet, dotnet-runtime, xunit, xunit-adapter, xunit-native, "
            "analyzer, foxrun-publish-panel, phase179-ros2-regression, "
            "phase181-ros2-regression, phase184-acceptance-tooling, "
            "phase186-bridge-tooling, phase186-bridge-windows-live, "
            "mcap-conformance, packages, boundary"
        ),
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
        return report_ci_job_results(results)

    if args.only == "dotnet":
        results.update(run_ci_jobs(build_dotnet_ci_jobs(), args.jobs))
        return report_ci_job_results(results)

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

    # --- independent dotnet validation lanes ---
    if args.only == "dotnet-runtime":
        results["dotnet-runtime-restore"] = restore_with_ignoring_failed_sources(
            RUNTIME_TESTS_PROJ,
            "Restore runtime test project",
            RUNTIME_TEST_PROPS,
            fatal=False,
        )
        results["dotnet-runtime"] = (
            run(
                [
                    "dotnet",
                    "run",
                    "--no-restore",
                    "--project",
                    RUNTIME_TESTS_PROJ,
                    *RUNTIME_TEST_PROPS,
                ],
                "Dotnet validation suite (default CI)",
            )
            if results["dotnet-runtime-restore"]
            else False
        )

    if args.only == "xunit":
        results["xunit-restore"] = restore_with_ignoring_failed_sources(
            UNIT_TESTS_PROJ,
            "Restore xUnit unit test project",
            UNIT_TEST_PROPS,
            fatal=False,
        )
        results["xunit"] = (
            run(
                [
                    "dotnet",
                    "test",
                    "--no-restore",
                    UNIT_TESTS_PROJ,
                    *UNIT_TEST_PROPS,
                    "--logger",
                    "trx;LogFileName=unit-tests.trx",
                    "--results-directory",
                    str(UNIT_TEST_RESULTS_DIR),
                ],
                "xUnit unit tests",
            )
            if results["xunit-restore"]
            else False
        )

    if args.only == "xunit-adapter":
        results["xunit-adapter-restore"] = restore_with_ignoring_failed_sources(
            UNIT_TESTS_PROJ,
            "Restore xUnit optional ROS2 adapter lane",
            UNIT_ADAPTER_TEST_PROPS,
            fatal=False,
        )
        results["xunit-adapter"] = (
            run(
                [
                    "dotnet",
                    "test",
                    "--no-restore",
                    UNIT_TESTS_PROJ,
                    *UNIT_ADAPTER_TEST_PROPS,
                    "--logger",
                    "trx;LogFileName=unit-tests-adapter.trx",
                    "--results-directory",
                    str(UNIT_ADAPTER_TEST_RESULTS_DIR),
                ],
                "xUnit optional ROS2 adapter unit tests",
            )
            if results["xunit-adapter-restore"]
            else False
        )

    if args.only == "xunit-native":
        results["xunit-native-restore"] = restore_with_ignoring_failed_sources(
            UNIT_TESTS_PROJ,
            "Restore xUnit Native ROS2 compilation lane",
            UNIT_NATIVE_TEST_PROPS,
            fatal=False,
        )
        results["xunit-native"] = (
            run(
                [
                    "dotnet",
                    "test",
                    "--no-restore",
                    UNIT_TESTS_PROJ,
                    *UNIT_NATIVE_TEST_PROPS,
                    "--logger",
                    "trx;LogFileName=unit-tests-native.trx",
                    "--results-directory",
                    str(UNIT_NATIVE_TEST_RESULTS_DIR),
                ],
                "xUnit Native ROS2 compilation unit tests",
            )
            if results["xunit-native-restore"]
            else False
        )

    # --- FoxRun Publish panel behavior suite ---
    if args.only in (None, "foxrun-publish-panel"):
        results["foxrun-publish-panel-install"] = run(
            foxrun_publish_panel_npm("ci"),
            "FoxRun Publish panel lockfile install",
            fatal=True,
        )
        results["foxrun-publish-panel-typecheck"] = run(
            foxrun_publish_panel_npm("run", "typecheck"),
            "FoxRun Publish panel typecheck",
            fatal=True,
        )
        results["foxrun-publish-panel-test"] = run(
            foxrun_publish_panel_npm("test"),
            "FoxRun Publish panel Vitest behavior tests",
            fatal=True,
        )

    # --- pure ROS2 acceptance-helper regression tests ---
    if args.only in (None, "phase179-ros2-regression"):
        results["phase179-ros2-inbound-acceptance"] = run(
            [sys.executable, "-m", "unittest", PHASE179_ROS2_INBOUND_ACCEPTANCE_REGRESSION],
            "Phase179 Linux ROS2 inbound acceptance helper regressions",
        )
        results["phase179-ros2-player-host"] = run(
            [sys.executable, "-m", "unittest", PHASE179_ROS2_PLAYER_HOST_REGRESSION],
            "Phase179 Windows Player host helper regressions",
        )
        results["phase179-ros2-matrix-profiles"] = run(
            [sys.executable, "-m", "unittest", PHASE179_ROS2_MATRIX_PROFILES_REGRESSION],
            "Phase179 named four-row interop profile regressions",
        )
        results["phase179-zenoh-topology"] = run(
            [sys.executable, "-m", "unittest", PHASE179_ZENOH_TOPOLOGY_REGRESSION],
            "Phase179 Zenoh topology ownership and readiness regressions",
        )

    # --- pure Phase184 acceptance tooling regressions ---
    if args.only in (None, "phase184-acceptance-tooling"):
        results["phase184-profile-acceptance-protocol"] = run(
            [
                sys.executable,
                "-m",
                "unittest",
                PHASE184_PROFILE_ACCEPTANCE_PROTOCOL_REGRESSION,
            ],
            "Phase184 acceptance protocol tooling regressions",
        )
        results["phase184-profile-acceptance-orchestrator"] = run(
            [
                sys.executable,
                "-m",
                "unittest",
                PHASE184_PROFILE_ACCEPTANCE_ORCHESTRATOR_REGRESSION,
            ],
            "Phase184 acceptance orchestrator tooling regressions",
        )
        results["phase184-foxglove-desktop-live-protocol"] = run(
            [
                sys.executable,
                "-m",
                "unittest",
                PHASE184_FOXGLOVE_DESKTOP_LIVE_PROTOCOL_REGRESSION,
            ],
            "Phase184 Foxglove Desktop live protocol regressions",
        )
        results["phase184-foxglove-cli-installer"] = run(
            [
                sys.executable,
                "-m",
                "unittest",
                PHASE184_FOXGLOVE_CLI_INSTALL_REGRESSION,
            ],
            "Phase184 Foxglove CLI installer regressions",
        )
        results["phase184-windows-job-owner"] = run(
            [
                sys.executable,
                "-m",
                "unittest",
                PHASE184_WINDOWS_JOB_OWNER_REGRESSION,
            ],
            "Phase184 Windows Job owner regressions",
        )
        results["phase184-foxglove-desktop-live-coordinator"] = run(
            [
                sys.executable,
                "-m",
                "unittest",
                PHASE184_FOXGLOVE_DESKTOP_LIVE_ACCEPTANCE_REGRESSION,
            ],
            "Phase184 Foxglove Desktop live coordinator regressions",
        )

    # --- pure Phase186 Bridge tooling and package-composition gates ---
    if args.only in (None, "phase186-bridge-tooling"):
        for module in PHASE186_BRIDGE_TOOLING_REGRESSIONS:
            label = module.rsplit(".", 1)[-1].removeprefix("test_").replace("_", " ")
            results["phase186-" + module.rsplit(".", 1)[-1]] = run(
                [sys.executable, "-m", "unittest", module],
                "Phase186 " + label + " regressions",
            )
        results["phase186-package-matrix"] = run(
            [sys.executable, PHASE186_PACKAGE_MATRIX_VALIDATOR],
            "Phase186 package-composition compile and boundary matrix",
            disable_timeout=True,
        )
        results["phase186-provenance"] = run(
            [sys.executable, "-m", PHASE186_PROVENANCE_MODULE],
            "Phase186 protocol and source provenance",
            disable_timeout=True,
        )

    # --- specifically provisioned Windows Unity + ROS/RMW live certification ---
    if args.only == "phase186-bridge-windows-live":
        head = current_git_head()
        certification_run_id = phase186_certification_run_id(head)
        results["phase186-bridge-windows-live"] = run(
            [
                sys.executable,
                "-m",
                PHASE186_CERTIFICATION_MODULE,
                "--expected-head",
                head,
                "--output-root",
                "build/phase186/windows-live",
                "--run-id",
                certification_run_id,
            ],
            "Phase186 provisioned Windows Unity and ROS/RMW live certification",
            disable_timeout=True,
        )

    # --- pure Phase181 custom-interface helper and source-package regressions ---
    if args.only in (None, "phase181-ros2-regression"):
        results["phase181-ros2-peer-protocol"] = run(
            [sys.executable, "-m", "unittest", PHASE181_ROS2_PEER_PROTOCOL_REGRESSION],
            "Phase181 custom ROS2 peer protocol regressions",
        )
        results["phase181-ros2-peer"] = run(
            [sys.executable, "-m", "unittest", PHASE181_ROS2_PEER_REGRESSION],
            "Phase181 Windows Editor and Player custom ROS2 peer regressions",
        )
        results["phase181-ros2-matrix-profiles"] = run(
            [sys.executable, "-m", "unittest", PHASE181_ROS2_MATRIX_PROFILES_REGRESSION],
            "Phase181 named custom ROS2 matrix profile regressions",
        )
        results["phase181-ros2-linux-peer"] = run(
            [sys.executable, "-m", "unittest", PHASE181_ROS2_LINUX_PEER_REGRESSION],
            "Phase181 caller-owned Linux custom ROS2 peer regressions",
        )
        results["phase181-interface-tooling"] = run(
            [sys.executable, "-m", "unittest", *PHASE181_INTERFACE_TOOLING_REGRESSIONS],
            "Phase181 static custom ROS2 interface tooling regressions",
        )
        for distro, rmw in (
            ("humble", "rmw_fastrtps_cpp"),
            ("jazzy", "rmw_fastrtps_cpp"),
            ("lyrical", "rmw_fastrtps_cpp"),
            ("lyrical", "rmw_zenoh_cpp"),
        ):
            results["phase181-typesupport-" + distro + "-" + rmw] = run(
                [
                    sys.executable,
                    PHASE181_TYPESUPPORT_VALIDATOR,
                    "--distro",
                    distro,
                    "--require-rmw",
                    rmw,
                ],
                "Phase181 " + distro + " " + rmw + " custom ROS2 typesupport validation",
            )

    # --- official MCAP differential conformance ---
    if args.only in (None, "mcap-conformance"):
        results["mcap-conformance-differential"] = run(
            [
                sys.executable,
                "Scripts/mcap/conformance/run_phase121_conformance.py",
                "--release-blocking",
                "--report-path",
                "build/mcap-conformance/phase121-conformance-report.json",
            ],
            "Official MCAP differential conformance",
            disable_timeout=True,
        )

    # --- package validators ---
    if args.only in (None, "packages"):
        package_results = run_parallel([
            (
                "test_release_tooling.py",
                [sys.executable, "-m", "unittest", RELEASE_TOOLING_REGRESSION],
            ),
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
        results["release-tooling-regression"] = package_results["test_release_tooling.py"]
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
