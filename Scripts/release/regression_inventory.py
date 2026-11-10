# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
# Purpose: Keep discovered Python regression modules mapped to CI or reviewed exclusions.
# Usage: python -B Scripts/release/regression_inventory.py
# Inputs: Repository scripts, workflows, and the explicit exclusion ledger below.
# Outputs: REGRESSION_INVENTORY_OK or a nonzero completeness diagnostic.
"""Reverse-completeness inventory for Python regression checks."""

from __future__ import annotations

import argparse
import re
from pathlib import Path
import subprocess
import sys
from typing import Iterable

_REPO_ROOT = Path(__file__).resolve().parents[2]
_MODULE_PATTERN = re.compile(r"Scripts(?:[./][A-Za-z0-9_]+)+\.regression_checks\.test_[A-Za-z0-9_]+")

# These lanes are invoked by workflow commands rather than spelling every
# module in YAML.  Keep the expansion here explicit so a module mentioned only
# in run_ci.py is not mistaken for CI coverage unless the workflow invokes the
# corresponding lane.
_WORKFLOW_RUNNER_MODULES: dict[str, tuple[str, ...]] = {
    "phase184-acceptance-tooling": (
        "Scripts.smoke.foxrun.regression_checks.test_phase184_profile_acceptance_protocol",
        "Scripts.smoke.foxrun.regression_checks.test_phase184_profile_acceptance",
        "Scripts.smoke.foxrun.regression_checks.test_phase184_foxglove_desktop_live_protocol",
        "Scripts.smoke.foxrun.regression_checks.test_phase184_foxglove_cli_install",
        "Scripts.smoke.foxrun.regression_checks.test_phase184_windows_job_owner",
        "Scripts.smoke.foxrun.regression_checks.test_phase184_foxglove_desktop_live_acceptance",
    ),
    "phase186-bridge-tooling": (
        "Scripts.smoke.foxrun.regression_checks.test_phase186_bridge_acceptance_protocol",
        "Scripts.smoke.foxrun.regression_checks.test_phase186_bridge_acceptance",
        "Scripts.smoke.foxrun.regression_checks.test_phase186_bridge_live",
        "Scripts.smoke.foxrun.regression_checks.test_phase186_bridge_certification",
        "Scripts.smoke.foxrun.regression_checks.test_phase186_bridge_build",
        "Scripts.smoke.foxrun.regression_checks.test_phase186_bridge_capability_probe",
        "Scripts.smoke.foxrun.regression_checks.test_phase186_provenance",
    ),
    "phase181-interface-tooling": (
        "Scripts.ros2forunity.interfaces.regression_checks.test_h01_delivery_enum_contract",
        "Scripts.ros2forunity.interfaces.regression_checks.test_h01_mismatch_cleanup_contract",
        "Scripts.ros2forunity.interfaces.regression_checks.test_h01_preflight_metadata_contract",
        "Scripts.ros2forunity.interfaces.regression_checks.test_h01_provider_freeze_contract",
        "Scripts.ros2forunity.interfaces.regression_checks.test_h01_registry_ownership_contract",
        "Scripts.ros2forunity.interfaces.regression_checks.test_h01_selection_error_attribution",
        "Scripts.ros2forunity.interfaces.regression_checks.test_h02_assembly_identity_contract",
        "Scripts.ros2forunity.interfaces.regression_checks.test_h02_enum_width_fixture_contract",
        "Scripts.ros2forunity.interfaces.regression_checks.test_h02_inherited_member_contract",
        "Scripts.ros2forunity.interfaces.regression_checks.test_h02_null_sequence_contract",
        "Scripts.ros2forunity.interfaces.regression_checks.test_h02_writer_rollback_contract",
        "Scripts.ros2forunity.interfaces.regression_checks.test_h03_fatal_boundary_contract",
        "Scripts.ros2forunity.interfaces.regression_checks.test_h03_stop_admission_contract",
        "Scripts.ros2forunity.interfaces.regression_checks.test_h04_draco_framing_contract",
        "Scripts.ros2forunity.interfaces.regression_checks.test_h04_native_registration_phase_contract",
        "Scripts.ros2forunity.interfaces.regression_checks.test_h04_node_lifetime_contract",
        "Scripts.ros2forunity.interfaces.regression_checks.test_h04_post_init_cleanup_contract",
        "Scripts.ros2forunity.interfaces.regression_checks.test_h04_runtime_identity_contract",
        "Scripts.ros2forunity.interfaces.regression_checks.test_h06_sigpipe_contract",
        "Scripts.ros2forunity.interfaces.regression_checks.test_h06_write_deadline_contract",
        "Scripts.ros2forunity.interfaces.regression_checks.test_h07_command_runner_contract",
        "Scripts.ros2forunity.interfaces.regression_checks.test_h07_payload_ownership_contract",
        "Scripts.ros2forunity.interfaces.regression_checks.test_h07_queue_accounting_contract",
        "Scripts.ros2forunity.interfaces.regression_checks.test_h07_startup_rollback_contract",
        "Scripts.ros2forunity.interfaces.regression_checks.test_interface_digest_h02",
        "Scripts.ros2forunity.interfaces.regression_checks.test_interface_digest_h02005",
    ),
    "windows-parity-regression-tooling": (
        "Scripts.ros2forunity.interfaces.regression_checks.test_h04_tool_identity_contract",
        "Scripts.ros2forunity.windows.humble.regression_checks.test_build_r2fu_runtime_package",
        "Scripts.ros2forunity.windows.humble.regression_checks.test_inspect_r2fu_runtime_artifact",
        "Scripts.ros2forunity.windows.humble.regression_checks.test_phase160_build_defaults",
        "Scripts.ros2forunity.windows.humble.regression_checks.test_sync_r2fu_artifact_to_unity2foxglove",
        "Scripts.ros2forunity.windows.humble.regression_checks.test_validate_r2fu_runtime_package",
        "Scripts.ros2forunity.windows.humble.regression_checks.test_validate_ros2forunity_package",
        "Scripts.ros2forunity.windows.jazzy.regression_checks.test_build_r2fu_runtime_package",
        "Scripts.ros2forunity.windows.jazzy.regression_checks.test_phase138b_build_defaults",
        "Scripts.ros2forunity.windows.jazzy.regression_checks.test_sync_r2fu_artifact_to_unity2foxglove",
        "Scripts.ros2forunity.windows.jazzy.regression_checks.test_validate_r2fu_runtime_package",
        "Scripts.ros2forunity.windows.lyrical.regression_checks.test_build_r2fu_runtime_package",
        "Scripts.ros2forunity.windows.lyrical.regression_checks.test_phase146b_build_defaults",
        "Scripts.ros2forunity.windows.lyrical.regression_checks.test_sync_r2fu_artifact_to_unity2foxglove",
        "Scripts.ros2forunity.windows.lyrical.regression_checks.test_validate_r2fu_runtime_package",
    ),
    "portable-regression-tooling": (
        "Scripts.mcap.regression_checks.test_mcap_time_sync",
        "Scripts.package.regression_checks.test_validate_source_generator_dll_facade",
        "Scripts.smoke.foxrun.regression_checks.test_atomic_publication",
        "Scripts.smoke.foxrun.regression_checks.test_phase189_component_messagepack_manual",
        "Scripts.smoke.foxrun.regression_checks.test_phase189_component_messagepack_probe",
        "Scripts.smoke.ros2.regression_checks.test_i10_native_cleanup",
        "Scripts.smoke.ros2.regression_checks.test_i10_phase106_cleanup",
        "Scripts.smoke.ros2.regression_checks.test_i10_phase109_cleanup",
        "Scripts.smoke.ros2.regression_checks.test_i10_remaining_cleanup",
        "Scripts.smoke.ros2.regression_checks.test_launch_phase138l_rviz2",
        "Scripts.smoke.ros2.regression_checks.test_phase138u_followup",
    ),
    "packages": (
        "Scripts.release.regression_checks.test_release_tooling",
        "Scripts.samples.regression_checks.test_sample_sync_tooling",
        "Scripts.remotegateway.regression_checks.test_remote_gateway_tooling",
        "Scripts.validation.regression_checks.test_ci_safe_baseline",
        "Scripts.validation.regression_checks.test_plan_audit_lib",
        "Scripts.validation.regression_checks.test_performance_evidence",
        "Scripts.validation.regression_checks.test_source_text_inventory",
        "Scripts.validation.regression_checks.test_source_text_inventory_gate",
        "Scripts.validation.regression_checks.test_source_text_inventory_semantics",
        "Scripts.architecture.regression_checks.test_structure_guards",
        "Scripts.release.regression_checks.test_regression_inventory",
        "Scripts.native.regression_checks.test_native_sources",
        "Scripts.package.regression_checks.test_validate_local_entrypoints",
        "Scripts.package.regression_checks.test_validate_phase186_package_matrix",
        "Scripts.package.regression_checks.test_validate_unity_package",
        "Scripts.schema.regression_checks.test_schema_tooling",
        "Scripts.smoke.test_core_smoke_scripts",
        "Scripts.smoke.ros2.regression_checks.test_phase162_lyrical_zenoh_player_smoke",
        "Scripts.smoke.ros2.regression_checks.test_ros2_windows_env",
        "Scripts.smoke.foxrun.regression_checks.test_phase185_foxrun_messagepack_probe",
        "Scripts.smoke.foxrun.regression_checks.test_phase186_bridge_manual",
        "Scripts.validation.conformance.regression_checks.test_phase190_validators",
        "Scripts.validation.conformance.regression_checks.test_phase190_script_documentation",
        "Scripts.validation.conformance.regression_checks.test_run_conformance",
        "Scripts.validation.source_decomposition.regression_checks.test_identity_tooling",
        "Scripts.validation.source_decomposition.regression_checks.test_freeze_baseline",
        "Scripts.validation.source_decomposition.regression_checks.test_u2r2_protocol_limits",
        "Scripts.validation.source_decomposition.regression_checks.test_identity_gate_hardening",
        "Scripts.performance.regression_checks.test_phase188_replay",
        "Scripts.smoke.replay.regression_checks.test_phase188_deterministic_replay_acceptance",
    ),
}

# Every discovered module must either appear in a CI/workflow command or have
# an explicit, reviewed reason for its environment-specific exclusion.
EXPLICIT_EXCLUSIONS: dict[str, str] = {}


def discover_regression_modules(root: Path = _REPO_ROOT) -> set[str]:
    """Return every regression-check module discovered under Scripts."""
    return {
        ".".join(path.relative_to(root).with_suffix("").parts)
        for path in root.glob("Scripts/**/regression_checks/test_*.py")
    }

def _workflow_run_text(text: str) -> str:
    """Return commands from active workflow run blocks, excluding comments/disabled steps."""
    lines = text.splitlines()
    blocks: list[list[str]] = []
    current: list[str] | None = None
    step_indent: int | None = None
    for line in lines:
        stripped = line.lstrip()
        indent = len(line) - len(stripped)
        if re.match(r"^-\s+name:\s*", stripped):
            if current is not None:
                blocks.append(current)
            current = [line]
            step_indent = indent
            continue
        if current is not None:
            if stripped and indent <= (step_indent or 0) and re.match(r"^-\s+", stripped):
                blocks.append(current)
                current = None
                step_indent = None
            else:
                current.append(line)
    if current is not None:
        blocks.append(current)
    if not blocks:
        blocks = [lines]

    commands: list[str] = []
    for block in blocks:
        if any(
            re.match(
                r"^\s*if:\s*(?:false|0|\$\{\{\s*false\s*\}\})\s*$",
                line,
                flags=re.IGNORECASE,
            )
            for line in block
        ):
            continue
        in_run = False
        run_indent = 0
        for line in block:
            stripped = line.lstrip()
            if not stripped or stripped.startswith("#"):
                continue
            indent = len(line) - len(stripped)
            if re.match(r"^run:\s*", stripped):
                in_run = True
                run_indent = indent
                commands.append(re.sub(r"^run:\s*", "", stripped))
                continue
            if in_run and indent > run_indent:
                commands.append(line)
            elif in_run:
                in_run = False
    return "\n".join(line for line in commands if not line.lstrip().startswith("#"))


def discover_ci_modules(root: Path = _REPO_ROOT) -> set[str]:
    """Return regression modules executed by the checked-in workflows."""
    sources = sorted((root / ".github/workflows").glob("*.y*ml"))
    text = "\n".join(
        path.read_text(encoding="utf-8", errors="replace")
        for path in sources
        if path.exists()
    )
    commands = _workflow_run_text(text)
    modules = {match.replace("/", ".") for match in _MODULE_PATTERN.findall(commands)}
    for lane, lane_modules in _WORKFLOW_RUNNER_MODULES.items():
        if re.search(rf"run_ci\.py\s+--only\s+{re.escape(lane)}\b", commands):
            modules.update(lane_modules)
        if re.search(
            rf"regression_inventory\.py\s+--run-lane\s+{re.escape(lane)}\b",
            commands,
        ):
            modules.update(lane_modules)
    return modules


def inventory_errors(
    discovered: Iterable[str] | None = None,
    ci_modules: Iterable[str] | None = None,
    exclusions: dict[str, str] | None = None,
) -> list[str]:
    """Return completeness, overlap, stale-entry, and reason-validation errors."""
    discovered_set = set(discovered if discovered is not None else discover_regression_modules())
    ci_set = set(ci_modules if ci_modules is not None else discover_ci_modules())
    excluded = dict(EXPLICIT_EXCLUSIONS if exclusions is None else exclusions)
    errors: list[str] = []
    missing = sorted(discovered_set - ci_set - set(excluded))
    if missing:
        errors.append("UNASSIGNED: " + ", ".join(missing))
    overlap = sorted(ci_set & set(excluded))
    if overlap:
        errors.append("CI_AND_EXCLUDED: " + ", ".join(overlap))
    stale = sorted(set(excluded) - discovered_set)
    if stale:
        errors.append("STALE_EXCLUSION: " + ", ".join(stale))
    for module, reason in sorted(excluded.items()):
        if not reason.strip():
            errors.append("EMPTY_EXCLUSION_REASON: " + module)
    return errors


def validate_inventory(root: Path = _REPO_ROOT) -> None:
    """Raise when the repository regression inventory is not complete."""
    errors = inventory_errors(
        discover_regression_modules(root),
        discover_ci_modules(root),
    )
    if errors:
        raise AssertionError("Regression inventory is incomplete: " + "; ".join(errors))


def main(argv: list[str] | None = None) -> int:
    """Validate the inventory or run one reviewed regression lane."""
    parser = argparse.ArgumentParser()
    parser.add_argument("--run-lane", choices=sorted(_WORKFLOW_RUNNER_MODULES))
    args = parser.parse_args(argv)
    errors = inventory_errors()
    if errors:
        for error in errors:
            print(error)
        return 1
    if args.run_lane:
        completed = subprocess.run(
            [sys.executable, "-B", "-m", "unittest", *_WORKFLOW_RUNNER_MODULES[args.run_lane]],
            cwd=_REPO_ROOT,
            check=False,
        )
        return completed.returncode
    print("REGRESSION_INVENTORY_OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
