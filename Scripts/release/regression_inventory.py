# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
# Purpose: Keep discovered Python regression modules mapped to CI or reviewed exclusions.
# Usage: python -B Scripts/release/regression_inventory.py
# Inputs: Repository scripts, workflows, and the explicit exclusion ledger below.
# Outputs: REGRESSION_INVENTORY_OK or a nonzero completeness diagnostic.
"""Reverse-completeness inventory for Python regression checks."""

from __future__ import annotations

import re
from pathlib import Path
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
}

# Every discovered module must either appear in a CI/workflow command or have
# an explicit, reviewed reason for its environment-specific exclusion.
EXPLICIT_EXCLUSIONS: dict[str, str] = {
    'Scripts.mcap.regression_checks.test_mcap_time_sync': 'MCAP timing diagnostic; manual/performance-only because it depends on external timing fixtures.',
    'Scripts.ros2forunity.interfaces.regression_checks.test_h01_delivery_enum_contract': 'ROS2 interface contract fixture; exercised by the dedicated interface/toolchain workflow when its external fixture is provisioned.',
    'Scripts.ros2forunity.interfaces.regression_checks.test_h01_mismatch_cleanup_contract': 'ROS2 interface contract fixture; exercised by the dedicated interface/toolchain workflow when its external fixture is provisioned.',
    'Scripts.ros2forunity.interfaces.regression_checks.test_h01_preflight_metadata_contract': 'ROS2 interface contract fixture; exercised by the dedicated interface/toolchain workflow when its external fixture is provisioned.',
    'Scripts.ros2forunity.interfaces.regression_checks.test_h01_provider_freeze_contract': 'ROS2 interface contract fixture; exercised by the dedicated interface/toolchain workflow when its external fixture is provisioned.',
    'Scripts.ros2forunity.interfaces.regression_checks.test_h01_registry_ownership_contract': 'ROS2 interface contract fixture; exercised by the dedicated interface/toolchain workflow when its external fixture is provisioned.',
    'Scripts.ros2forunity.interfaces.regression_checks.test_h01_selection_error_attribution': 'ROS2 interface contract fixture; exercised by the dedicated interface/toolchain workflow when its external fixture is provisioned.',
    'Scripts.ros2forunity.interfaces.regression_checks.test_h02_assembly_identity_contract': 'ROS2 interface contract fixture; exercised by the dedicated interface/toolchain workflow when its external fixture is provisioned.',
    'Scripts.ros2forunity.interfaces.regression_checks.test_h02_enum_width_fixture_contract': 'ROS2 interface contract fixture; exercised by the dedicated interface/toolchain workflow when its external fixture is provisioned.',
    'Scripts.ros2forunity.interfaces.regression_checks.test_h02_inherited_member_contract': 'ROS2 interface contract fixture; exercised by the dedicated interface/toolchain workflow when its external fixture is provisioned.',
    'Scripts.ros2forunity.interfaces.regression_checks.test_h02_null_sequence_contract': 'ROS2 interface contract fixture; exercised by the dedicated interface/toolchain workflow when its external fixture is provisioned.',
    'Scripts.ros2forunity.interfaces.regression_checks.test_h02_writer_rollback_contract': 'ROS2 interface contract fixture; exercised by the dedicated interface/toolchain workflow when its external fixture is provisioned.',
    'Scripts.ros2forunity.interfaces.regression_checks.test_h03_fatal_boundary_contract': 'ROS2 interface contract fixture; exercised by the dedicated interface/toolchain workflow when its external fixture is provisioned.',
    'Scripts.ros2forunity.interfaces.regression_checks.test_h03_stop_admission_contract': 'ROS2 interface contract fixture; exercised by the dedicated interface/toolchain workflow when its external fixture is provisioned.',
    'Scripts.ros2forunity.interfaces.regression_checks.test_h04_draco_framing_contract': 'ROS2 interface contract fixture; exercised by the dedicated interface/toolchain workflow when its external fixture is provisioned.',
    'Scripts.ros2forunity.interfaces.regression_checks.test_h04_native_registration_phase_contract': 'ROS2 interface contract fixture; exercised by the dedicated interface/toolchain workflow when its external fixture is provisioned.',
    'Scripts.ros2forunity.interfaces.regression_checks.test_h04_node_lifetime_contract': 'ROS2 interface contract fixture; exercised by the dedicated interface/toolchain workflow when its external fixture is provisioned.',
    'Scripts.ros2forunity.interfaces.regression_checks.test_h04_post_init_cleanup_contract': 'ROS2 interface contract fixture; exercised by the dedicated interface/toolchain workflow when its external fixture is provisioned.',
    'Scripts.ros2forunity.interfaces.regression_checks.test_h04_runtime_identity_contract': 'ROS2 interface contract fixture; exercised by the dedicated interface/toolchain workflow when its external fixture is provisioned.',
    'Scripts.ros2forunity.interfaces.regression_checks.test_h04_tool_identity_contract': 'ROS2 interface contract fixture; exercised by the dedicated interface/toolchain workflow when its external fixture is provisioned.',
    'Scripts.ros2forunity.interfaces.regression_checks.test_h06_sigpipe_contract': 'ROS2 interface contract fixture; exercised by the dedicated interface/toolchain workflow when its external fixture is provisioned.',
    'Scripts.ros2forunity.interfaces.regression_checks.test_h06_write_deadline_contract': 'ROS2 interface contract fixture; exercised by the dedicated interface/toolchain workflow when its external fixture is provisioned.',
    'Scripts.ros2forunity.interfaces.regression_checks.test_h07_command_runner_contract': 'ROS2 interface contract fixture; exercised by the dedicated interface/toolchain workflow when its external fixture is provisioned.',
    'Scripts.ros2forunity.interfaces.regression_checks.test_h07_payload_ownership_contract': 'ROS2 interface contract fixture; exercised by the dedicated interface/toolchain workflow when its external fixture is provisioned.',
    'Scripts.ros2forunity.interfaces.regression_checks.test_h07_queue_accounting_contract': 'ROS2 interface contract fixture; exercised by the dedicated interface/toolchain workflow when its external fixture is provisioned.',
    'Scripts.ros2forunity.interfaces.regression_checks.test_h07_startup_rollback_contract': 'ROS2 interface contract fixture; exercised by the dedicated interface/toolchain workflow when its external fixture is provisioned.',
    'Scripts.ros2forunity.interfaces.regression_checks.test_interface_digest_h02': 'ROS2 interface contract fixture; exercised by the dedicated interface/toolchain workflow when its external fixture is provisioned.',
    'Scripts.ros2forunity.interfaces.regression_checks.test_interface_digest_h02005': 'ROS2 interface contract fixture; exercised by the dedicated interface/toolchain workflow when its external fixture is provisioned.',
    'Scripts.ros2forunity.windows.humble.regression_checks.test_build_r2fu_runtime_package': 'Windows package build or artifact lane; runs only with the corresponding self-hosted ROS2 fixture.',
    'Scripts.ros2forunity.windows.humble.regression_checks.test_inspect_r2fu_runtime_artifact': 'Windows package build or artifact lane; runs only with the corresponding self-hosted ROS2 fixture.',
    'Scripts.ros2forunity.windows.humble.regression_checks.test_phase160_build_defaults': 'Windows package build or artifact lane; runs only with the corresponding self-hosted ROS2 fixture.',
    'Scripts.ros2forunity.windows.humble.regression_checks.test_sync_r2fu_artifact_to_unity2foxglove': 'Windows package build or artifact lane; runs only with the corresponding self-hosted ROS2 fixture.',
    'Scripts.ros2forunity.windows.humble.regression_checks.test_validate_r2fu_runtime_package': 'Windows package build or artifact lane; runs only with the corresponding self-hosted ROS2 fixture.',
    'Scripts.ros2forunity.windows.humble.regression_checks.test_validate_ros2forunity_package': 'Windows package build or artifact lane; runs only with the corresponding self-hosted ROS2 fixture.',
    'Scripts.ros2forunity.windows.jazzy.regression_checks.test_build_r2fu_runtime_package': 'Windows package build or artifact lane; runs only with the corresponding self-hosted ROS2 fixture.',
    'Scripts.ros2forunity.windows.jazzy.regression_checks.test_phase138b_build_defaults': 'Windows package build or artifact lane; runs only with the corresponding self-hosted ROS2 fixture.',
    'Scripts.ros2forunity.windows.jazzy.regression_checks.test_sync_r2fu_artifact_to_unity2foxglove': 'Windows package build or artifact lane; runs only with the corresponding self-hosted ROS2 fixture.',
    'Scripts.ros2forunity.windows.jazzy.regression_checks.test_validate_r2fu_runtime_package': 'Windows package build or artifact lane; runs only with the corresponding self-hosted ROS2 fixture.',
    'Scripts.ros2forunity.windows.lyrical.regression_checks.test_build_r2fu_runtime_package': 'Windows package build or artifact lane; runs only with the corresponding self-hosted ROS2 fixture.',
    'Scripts.ros2forunity.windows.lyrical.regression_checks.test_phase146b_build_defaults': 'Windows package build or artifact lane; runs only with the corresponding self-hosted ROS2 fixture.',
    'Scripts.ros2forunity.windows.lyrical.regression_checks.test_sync_r2fu_artifact_to_unity2foxglove': 'Windows package build or artifact lane; runs only with the corresponding self-hosted ROS2 fixture.',
    'Scripts.ros2forunity.windows.lyrical.regression_checks.test_validate_r2fu_runtime_package': 'Windows package build or artifact lane; runs only with the corresponding self-hosted ROS2 fixture.',
    'Scripts.smoke.foxrun.regression_checks.test_atomic_publication': 'FoxRun publication integration fixture; manual lane requiring generated artifact inputs.',
    'Scripts.smoke.foxrun.regression_checks.test_phase189_component_messagepack_manual': 'Phase189 manual/probe lane; requires provisioned Unity/native artifacts.',
    'Scripts.smoke.foxrun.regression_checks.test_phase189_component_messagepack_probe': 'Phase189 manual/probe lane; requires provisioned Unity/native artifacts.',
    'Scripts.smoke.ros2.regression_checks.test_i10_native_cleanup': 'ROS2 live/native smoke lane; exercised by the optional or self-hosted ROS2 workflow, not the default package lane.',
    'Scripts.smoke.ros2.regression_checks.test_i10_phase106_cleanup': 'ROS2 live/native smoke lane; exercised by the optional or self-hosted ROS2 workflow, not the default package lane.',
    'Scripts.smoke.ros2.regression_checks.test_i10_phase109_cleanup': 'ROS2 live/native smoke lane; exercised by the optional or self-hosted ROS2 workflow, not the default package lane.',
    'Scripts.smoke.ros2.regression_checks.test_i10_remaining_cleanup': 'ROS2 live/native smoke lane; exercised by the optional or self-hosted ROS2 workflow, not the default package lane.',
    'Scripts.smoke.ros2.regression_checks.test_launch_phase138l_rviz2': 'ROS2 live/native smoke lane; exercised by the optional or self-hosted ROS2 workflow, not the default package lane.',
}


def discover_regression_modules(root: Path = _REPO_ROOT) -> set[str]:
    """Return every regression-check module discovered under Scripts."""
    return {
        ".".join(path.relative_to(root).with_suffix("").parts)
        for path in root.glob("Scripts/**/regression_checks/test_*.py")
    }


def discover_ci_modules(root: Path = _REPO_ROOT) -> set[str]:
    """Return regression modules executed by the checked-in workflows."""
    sources = sorted((root / ".github/workflows").glob("*.y*ml"))
    text = "\n".join(
        path.read_text(encoding="utf-8", errors="replace")
        for path in sources
        if path.exists()
    )
    modules = {match.replace("/", ".") for match in _MODULE_PATTERN.findall(text)}
    for lane, lane_modules in _WORKFLOW_RUNNER_MODULES.items():
        if re.search(rf"run_ci\.py\s+--only\s+{re.escape(lane)}\b", text):
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


def main() -> int:
    """Validate the live inventory and emit the machine-readable success marker."""
    errors = inventory_errors()
    if errors:
        for error in errors:
            print(error)
        return 1
    print("REGRESSION_INVENTORY_OK")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
