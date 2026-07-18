#!/usr/bin/env python3
"""Synchronize one already-validated Phase181 candidate add-on into Packages/."""

from __future__ import annotations

import argparse
import json
from pathlib import Path, PurePosixPath
import shutil
import sys
from dataclasses import dataclass
from typing import Callable, Sequence

try:
    from foxrun_custom_typesupport_common import (
        AddonValidationRequest,
        AddonValidationResult,
        SUPPORTED_DISTROS,
        addon_package_id,
        base_runtime_package_id,
        validate_addon,
    )
except ModuleNotFoundError:  # pragma: no cover - direct script invocation
    from Scripts.ros2forunity.interfaces.foxrun_custom_typesupport_common import (
        AddonValidationRequest,
        AddonValidationResult,
        SUPPORTED_DISTROS,
        addon_package_id,
        base_runtime_package_id,
        validate_addon,
    )


ERROR_CODE = "FOXRUN_TYPESUPPORT003"


class AddonSyncError(RuntimeError):
    """A bounded failure for an add-on sync transaction."""

    def __init__(self, remediation: str):
        self.code = ERROR_CODE
        self.remediation = remediation
        super().__init__(self.code + ": " + remediation)


@dataclass(frozen=True)
class AddonSyncRequest:
    distro: str
    candidate_package: Path
    target_package: Path
    validation_request: AddonValidationRequest


def _candidate_root(request: AddonSyncRequest) -> Path:
    candidate = Path(request.candidate_package).resolve()
    if candidate.name != "package" or candidate.parent.name != "candidate":
        raise AddonSyncError("use-phase181-candidate-package")
    phase181 = candidate.parents[2]
    if phase181.name != "phase181" or candidate.parents[1].name != request.distro:
        raise AddonSyncError("use-phase181-candidate-package")
    if phase181.parent.name.lower() != "build":
        raise AddonSyncError("use-phase181-candidate-package")
    return candidate.parent


def _safe_inventory_path(value: object) -> str:
    if not isinstance(value, str) or not value or "\\" in value:
        raise AddonSyncError("repair-typesupport-inventory")
    path = PurePosixPath(value)
    if path.is_absolute() or any(part in {"", ".", ".."} for part in path.parts):
        raise AddonSyncError("repair-typesupport-inventory")
    return path.as_posix()


def allowed_inventory_paths(request: AddonSyncRequest) -> tuple[str, ...]:
    """Return the exact candidate payload allowlist, including its inventory."""

    candidate = Path(request.candidate_package)
    try:
        inventory = json.loads(
            (candidate / "RuntimeSupport" / "typesupport-inventory.json").read_text(encoding="utf-8")
        )
    except (OSError, UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise AddonSyncError("repair-typesupport-inventory") from exc
    entries = inventory.get("entries") if isinstance(inventory, dict) else None
    if not isinstance(entries, list):
        raise AddonSyncError("repair-typesupport-inventory")
    paths = [_safe_inventory_path(item.get("path")) for item in entries if isinstance(item, dict)]
    if len(paths) != len(entries) or len(paths) != len(set(paths)):
        raise AddonSyncError("repair-typesupport-inventory")
    paths.append("RuntimeSupport/typesupport-inventory.json")
    paths = sorted(set(paths), key=str.lower)
    return tuple(paths)


def _candidate_evidence_path(request: AddonSyncRequest) -> Path:
    return _candidate_root(request) / "e" / "candidate-validation.json"


def _validate_candidate_evidence(request: AddonSyncRequest) -> None:
    try:
        evidence = json.loads(_candidate_evidence_path(request).read_text(encoding="utf-8"))
    except (OSError, UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise AddonSyncError("build-and-validate-candidate-before-sync") from exc
    if (
        not isinstance(evidence, dict)
        or evidence.get("schemaVersion") != 1
        or evidence.get("distro") != request.distro
        or evidence.get("validated") is not True
    ):
        raise AddonSyncError("build-and-validate-candidate-before-sync")


def _validate_candidate_files(candidate: Path, allowed: Sequence[str]) -> None:
    actual = {
        path.relative_to(candidate).as_posix()
        for path in candidate.rglob("*")
        if path.is_file()
    }
    if actual != set(allowed):
        raise AddonSyncError("remove-unexpected-candidate-payload")


def verify_sync_ready(
    request: AddonSyncRequest,
    *,
    validator: Callable[[AddonValidationRequest], AddonValidationResult] = validate_addon,
) -> tuple[str, ...]:
    """Check proof, semantic validation, and exact allowlist before any copy."""

    candidate_root = _candidate_root(request)
    candidate = Path(request.candidate_package)
    _validate_candidate_evidence(request)
    if request.validation_request.addon_package.resolve() != candidate.resolve():
        raise AddonSyncError("repair-candidate-validation-request")
    try:
        validator(request.validation_request)
    except Exception as exc:
        raise AddonSyncError("repair-candidate-typesupport-validation") from exc
    allowed = allowed_inventory_paths(request)
    _validate_candidate_files(candidate, allowed)
    _validate_target_path(request)
    return allowed


def _validate_target_path(request: AddonSyncRequest) -> None:
    target = Path(request.target_package)
    if target.name != addon_package_id(request.distro) or target.parent.name != "Packages":
        raise AddonSyncError("use-matching-addon-package-target")


def _target_has_only_expected_payload(target: Path, allowed: Sequence[str]) -> bool:
    if not target.exists():
        return True
    actual = {
        path.relative_to(target).as_posix()
        for path in target.rglob("*")
        if path.is_file() and not path.name.endswith(".meta")
    }
    return actual.issubset(set(allowed))


def sync_addon(request: AddonSyncRequest) -> Path:
    """Copy just the verified candidate inventory into its exact sibling package."""

    allowed = verify_sync_ready(request)
    candidate = Path(request.candidate_package)
    target = Path(request.target_package)
    if not _target_has_only_expected_payload(target, allowed):
        raise AddonSyncError("remove-stale-addon-payload-before-sync")

    staging = _candidate_root(request) / "sync" / "package"
    if staging.exists():
        shutil.rmtree(staging)
    staging.mkdir(parents=True, exist_ok=True)
    for relative in allowed:
        source = candidate / relative
        destination = staging / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, destination)
    _validate_candidate_files(staging, allowed)

    target.mkdir(parents=True, exist_ok=True)
    for relative in allowed:
        source = staging / relative
        destination = target / relative
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, destination)
    return target


def _default_repo_root() -> Path:
    return Path(__file__).resolve().parents[3]


def parse_args(argv: Sequence[str] | None = None) -> AddonSyncRequest:
    root = _default_repo_root()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--distro", required=True, choices=SUPPORTED_DISTROS)
    parser.add_argument("--candidate-package", type=Path)
    parser.add_argument("--target-package", type=Path)
    parser.add_argument("--static-interface-package", type=Path)
    parser.add_argument("--base-runtime-package", type=Path)
    args = parser.parse_args(argv)
    candidate = args.candidate_package or root / "build" / "phase181" / args.distro / "candidate" / "package"
    target = args.target_package or root / "Packages" / addon_package_id(args.distro)
    static = args.static_interface_package or root / "Packages" / "dev.unity2foxglove.foxrun.ros2.interfaces"
    base = args.base_runtime_package or root / "Packages" / base_runtime_package_id(args.distro)
    return AddonSyncRequest(
        distro=args.distro,
        candidate_package=candidate,
        target_package=target,
        validation_request=AddonValidationRequest(
            distro=args.distro,
            addon_package=candidate,
            static_interface_package=static,
            base_runtime_package=base,
            require_rmws=("rmw_fastrtps_cpp", "rmw_zenoh_cpp") if args.distro == "lyrical" else (),
        ),
    )


def main(argv: Sequence[str] | None = None) -> int:
    request = parse_args(argv)
    try:
        target = sync_addon(request)
    except AddonSyncError as exc:
        print(str(exc), file=sys.stderr)
        return 1
    print("PASS:", request.distro, target)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
