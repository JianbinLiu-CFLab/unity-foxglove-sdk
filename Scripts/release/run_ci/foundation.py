#!/usr/bin/env python3
# Keep this release entrypoint under CI coverage for documentation-only ref updates.
# Release checks also cover post-merge checkpoint alignment commits.
# Keep final-reference checkpoint updates on the same required-check path.
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
import re
import subprocess
import sys
import time
import uuid
from pathlib import Path

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(errors="replace")
if hasattr(sys.stderr, "reconfigure"):
    sys.stderr.reconfigure(errors="replace")

REPO_ROOT = Path(__file__).resolve().parents[3]
if str(REPO_ROOT) not in sys.path:
    sys.path.insert(0, str(REPO_ROOT))

from Scripts.unity_build.unity_il2cpp import start_owned_process, await_tree_quiescence
CI_ONLY_CHOICES = (
    "dotnet",
    "dotnet-runtime",
    "mediafoundation-native-smoke",
    "xunit",
    "xunit-adapter",
    "xunit-native",
    "xunit-manager-boundary",
    "ros2bridge-xunit",
    "performance-regression",
    "phase188-replay-regression",
    "analyzer",
    "foxrun-publish-panel",
    "phase179-ros2-regression",
    "phase181-ros2-regression",
    "phase184-acceptance-tooling",
    "phase186-bridge-tooling",
    "phase186-bridge-windows-live",
    "mcap-conformance",
    "packages",
    "boundary",
)
RUN_ID_RE = re.compile(r"[A-Za-z0-9][A-Za-z0-9._-]{0,63}\Z")
MAX_CAPTURED_OUTPUT_CHARS = 262144


def sanitize_run_id(value: str | None) -> str:
    """Accept only a single safe path component for CI output isolation."""
    candidate = (value or "").strip()
    if not candidate:
        return f"{os.getpid()}-{uuid.uuid4().hex[:8]}"
    if not RUN_ID_RE.fullmatch(candidate):
        raise SystemExit("UNITY2FOXGLOVE_CI_RUN_ID must be one safe path component")
    return candidate


def unique_run_id(value: str | None) -> str:
    """Avoid reusing an existing CI output directory across invocations."""
    candidate = sanitize_run_id(value)
    run_root = REPO_ROOT / "build" / "ci" / candidate
    if run_root.exists():
        candidate = f"{candidate}-{uuid.uuid4().hex[:8]}"
    return candidate


RUN_ID = unique_run_id(os.environ.get("UNITY2FOXGLOVE_CI_RUN_ID"))
CI_ROOT = REPO_ROOT / "build/ci" / RUN_ID
ISOLATED_DOTNET_ROOT = CI_ROOT / "dotnet"
MCAP_REPORT_PATH = CI_ROOT / "mcap-conformance" / "phase121-conformance-report.json"


def phase186_certification_run_id(head: str) -> str:
    """Return a run identifier bound to both the current HEAD and CI invocation."""
    identity = hashlib.sha256(RUN_ID.encode("utf-8")).hexdigest()[:6]
    return f"phase186h-cert-{head[:6]}{identity}"

PASS = "[PASS]"
FAIL = "[FAIL]"
SKIP = "[SKIP]"
# Keep local restore semantics aligned with the authoritative remote workflow:
# an unavailable configured feed is a restore failure, even when a stale cache
# might otherwise make the project buildable.  This prevents a green local
# aggregate from claiming current dependency-source availability.
IGNORE_FAILED_SOURCES_OPTION: list[str] = []
RUNTIME_TESTS_PROJ = "Packages/dev.unity2foxglove.sdk/Tests/Runtime/FoxgloveSdk.Tests.csproj"
UNIT_TESTS_PROJ = "Packages/dev.unity2foxglove.sdk/Tests/Unit/FoxgloveSdk.UnitTests.csproj"
ROS2_BRIDGE_UNIT_TESTS_PROJ = "Packages/dev.unity2foxglove.ros2bridge/Tests/Unit/Unity2Foxglove.Ros2Bridge.UnitTests.csproj"
PERFORMANCE_REGRESSION_TESTS_PROJ = "Packages/dev.unity2foxglove.sdk/Tests/Performance/Regression/FoxgloveSdk.PerformanceRegressionTests.csproj"
SOURCE_GENERATOR_PROJ = (
    "Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/FoxgloveLogSourceGenerator.csproj"
)
SOURCE_GENERATOR_VALIDATOR = "Scripts/package/validate_source_generator_dll.py"
SCHEMA_GENERATED_OUTPUT_VALIDATOR = "Scripts/schema/validate_schema_generated_outputs.py"
R2FU_PACKAGE_VALIDATORS = tuple(
    (
        f"validate-{artifact}-{distro}",
        f"Scripts/ros2forunity/windows/{distro}/{script}.py",
    )
    for distro in ("humble", "jazzy", "lyrical")
    for artifact, script in (
        ("r2fu-runtime", "validate_r2fu_runtime_package"),
        ("ros2forunity-adapter", "validate_ros2forunity_package"),
    )
)
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
    "Scripts.ros2forunity.interfaces.regression_checks.test_h07_health_deadline_contract",
    "Scripts.ros2forunity.interfaces.regression_checks.test_h02_subst_cleanup_contract",
)
RELEASE_TOOLING_REGRESSION = "Scripts.release.regression_checks.test_release_tooling"
SAMPLE_SYNC_TOOLING_REGRESSION = (
    "Scripts.samples.regression_checks.test_sample_sync_tooling"
)
PACKAGE_LANE_REGRESSION_MODULES = (
    "Scripts.release.regression_checks.test_regression_inventory",
    "Scripts.native.regression_checks.test_native_sources",
    "Scripts.package.regression_checks.test_validate_local_entrypoints",
    "Scripts.package.regression_checks.test_validate_phase186_package_matrix",
    "Scripts.package.regression_checks.test_validate_unity_package",
    "Scripts.schema.regression_checks.test_schema_tooling",
    "Scripts.smoke.test_core_smoke_scripts",
    "Scripts.smoke.ros2.regression_checks.test_phase162_lyrical_zenoh_player_smoke",
    "Scripts.smoke.ros2.regression_checks.test_ros2_windows_env",
    "Scripts.smoke.ros2.regression_checks.test_i10_native_cleanup",
    "Scripts.smoke.foxrun.regression_checks.test_phase185_foxrun_messagepack_probe",
    "Scripts.smoke.foxrun.regression_checks.test_phase186_bridge_manual",
    "Scripts.phase190.regression_checks.test_phase190_validators",
    "Scripts.phase190.regression_checks.test_phase190_script_documentation",
    "Scripts.phase190.regression_checks.test_run_conformance",
    "Scripts.phase192.regression_checks.test_identity_tooling",
    "Scripts.phase192.regression_checks.test_freeze_baseline",
    "Scripts.phase192.regression_checks.test_u2r2_protocol_limits",
    "Scripts.phase192.regression_checks.test_identity_gate_hardening",
    "Scripts.performance.regression_checks.test_phase188_replay",
    "Scripts.smoke.replay.regression_checks.test_phase188_deterministic_replay_acceptance",
)
PHASE181_TYPESUPPORT_VALIDATOR = "Scripts/ros2forunity/interfaces/validate_foxrun_custom_typesupport_addon.py"
PHASE188_REPLAY_TOOLING_REGRESSION = (
    "Scripts.smoke.replay.regression_checks.test_phase188_deterministic_replay_acceptance"
)
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


@dataclass(frozen=True)
class OwnedCommandResult:
    """Result from a command whose complete process tree is owned by this run."""

    returncode: int
    stdout: str = ""
    stderr: str = ""
    timed_out: bool = False
    residual_pids: tuple[int, ...] = ()


def current_git_head() -> str:
    """Return the exact tracked commit used to identity-bind live evidence."""

    completed = subprocess.run(
        ["git", "rev-parse", "HEAD"],
        cwd=REPO_ROOT,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=False,
        timeout=command_timeout_seconds(),
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


def ensure_clean_source(relative_path: str) -> None:
    """Require executable certification inputs to match the checked-out tree."""
    completed = subprocess.run(
        ["git", "diff", "--quiet", "--", relative_path],
        cwd=REPO_ROOT,
        timeout=command_timeout_seconds(),
        check=False,
    )
    if completed.returncode != 0:
        raise RuntimeError(f"Certification source is modified: {relative_path}")


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


def validate_control_plane(args: argparse.Namespace) -> None:
    """Fail before launching children when CI controls are malformed or non-positive."""
    for name in ("UNITY2FOXGLOVE_CI_TIMEOUT", "UNITY2FOXGLOVE_CI_JOB_TIMEOUT", "UNITY2FOXGLOVE_CI_JOBS"):
        raw = os.environ.get(name, "").strip()
        if raw:
            try:
                if int(raw) < 1:
                    raise ValueError
            except ValueError as error:
                raise SystemExit(f"invalid {name}={raw!r}; expected a positive integer") from error
    if getattr(args, "jobs", None) is not None and args.jobs < 1:
        raise SystemExit("--jobs must be a positive integer")
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
UNIT_MANAGER_BOUNDARY_TEST_PROPS = [
    *dotnet_msbuild_props("unit-tests-manager-boundary"),
    "-p:IncludeManagerProductionBoundary=true",
]
ANALYZER_OUTPUT_DIR = ISOLATED_DOTNET_ROOT / "analyzer-output"
UNIT_TEST_RESULTS_DIR = CI_ROOT / "test-results" / "unit"
UNIT_ADAPTER_TEST_RESULTS_DIR = CI_ROOT / "test-results" / "unit-adapter"
UNIT_NATIVE_TEST_RESULTS_DIR = CI_ROOT / "test-results" / "unit-native"
UNIT_MANAGER_BOUNDARY_TEST_RESULTS_DIR = CI_ROOT / "test-results" / "unit-manager-boundary"
ROS2_BRIDGE_TEST_PROPS = dotnet_msbuild_props("ros2bridge-unit-tests")


def green(msg: str) -> str:
    """Wrap a message with green ANSI colour codes."""
    return f"\033[32m{msg}\033[0m"


def red(msg: str) -> str:
    """Wrap a message with red ANSI colour codes."""
    return f"\033[31m{msg}\033[0m"


def cyan(msg: str) -> str:
    """Wrap a message with cyan ANSI colour codes."""
    return f"\033[36m{msg}\033[0m"

__all__ = ['ANALYZER_OUTPUT_DIR', 'ANALYZER_PROPS', 'ANALYZER_RUNTIME_TEST_PROPS', 'CI_ONLY_CHOICES', 'CI_ROOT', 'CapturedCommandResult', 'CiJob', 'CiJobResult', 'DEFAULT_COMMAND_TIMEOUT_SECONDS', 'DEFAULT_JOB_TIMEOUT_SECONDS', 'DEFAULT_PARALLEL_JOBS', 'DOTNET_CI_EXCLUSIVE_GROUP', 'FAIL', 'FIRST_COMPLETED', 'FOXRUN_PUBLISH_PANEL_DIR', 'Future', 'IGNORE_FAILED_SOURCES_OPTION', 'ISOLATED_DOTNET_ROOT', 'MAX_CAPTURED_OUTPUT_CHARS', 'MCAP_REPORT_PATH', 'NPM_EXECUTABLE', 'OwnedCommandResult', 'PACKAGE_LANE_REGRESSION_MODULES', 'PASS', 'PERFORMANCE_REGRESSION_TESTS_PROJ', 'PHASE179_ROS2_INBOUND_ACCEPTANCE_REGRESSION', 'PHASE179_ROS2_MATRIX_PROFILES_REGRESSION', 'PHASE179_ROS2_PLAYER_HOST_REGRESSION', 'PHASE179_ZENOH_TOPOLOGY_REGRESSION', 'PHASE181_INTERFACE_TOOLING_REGRESSIONS', 'PHASE181_ROS2_LINUX_PEER_REGRESSION', 'PHASE181_ROS2_MATRIX_PROFILES_REGRESSION', 'PHASE181_ROS2_PEER_PROTOCOL_REGRESSION', 'PHASE181_ROS2_PEER_REGRESSION', 'PHASE181_TYPESUPPORT_VALIDATOR', 'PHASE184_FOXGLOVE_CLI_INSTALL_REGRESSION', 'PHASE184_FOXGLOVE_DESKTOP_LIVE_ACCEPTANCE_REGRESSION', 'PHASE184_FOXGLOVE_DESKTOP_LIVE_PROTOCOL_REGRESSION', 'PHASE184_PROFILE_ACCEPTANCE_ORCHESTRATOR_REGRESSION', 'PHASE184_PROFILE_ACCEPTANCE_PROTOCOL_REGRESSION', 'PHASE184_WINDOWS_JOB_OWNER_REGRESSION', 'PHASE186_BRIDGE_TOOLING_REGRESSIONS', 'PHASE186_CERTIFICATION_MODULE', 'PHASE186_PACKAGE_MATRIX_VALIDATOR', 'PHASE186_PROVENANCE_MODULE', 'PHASE188_REPLAY_TOOLING_REGRESSION', 'Path', 'R2FU_PACKAGE_VALIDATORS', 'RELEASE_TOOLING_REGRESSION', 'REPO_ROOT', 'ROS2_BRIDGE_TEST_PROPS', 'ROS2_BRIDGE_UNIT_TESTS_PROJ', 'RUNTIME_TESTS_PROJ', 'RUNTIME_TEST_PROPS', 'RUN_ID', 'RUN_ID_RE', 'SAMPLE_SYNC_TOOLING_REGRESSION', 'SCHEMA_GENERATED_OUTPUT_VALIDATOR', 'SKIP', 'SOURCE_GENERATOR_PROJ', 'SOURCE_GENERATOR_VALIDATOR', 'ThreadPoolExecutor', 'UNIT_ADAPTER_TEST_PROPS', 'UNIT_ADAPTER_TEST_RESULTS_DIR', 'UNIT_MANAGER_BOUNDARY_TEST_PROPS', 'UNIT_MANAGER_BOUNDARY_TEST_RESULTS_DIR', 'UNIT_NATIVE_TEST_PROPS', 'UNIT_NATIVE_TEST_RESULTS_DIR', 'UNIT_TESTS_PROJ', 'UNIT_TEST_PROPS', 'UNIT_TEST_RESULTS_DIR', '_msbuild_dir', 'annotations', 'argparse', 'as_completed', 'await_tree_quiescence', 'command_timeout_seconds', 'current_git_head', 'cyan', 'dataclass', 'default_parallel_jobs', 'dotnet_msbuild_props', 'ensure_clean_source', 'foxrun_publish_panel_npm', 'green', 'hashlib', 'job_timeout_seconds', 'os', 'phase186_certification_run_id', 're', 'red', 'sanitize_run_id', 'start_owned_process', 'subprocess', 'sys', 'time', 'unique_run_id', 'uuid', 'validate_control_plane', 'validator_msbuild_args', 'wait']
