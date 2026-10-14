#!/usr/bin/env python3
"""Shared validation primitives for Phase181 custom ROS2 typesupport add-ons.

The add-on package is intentionally a verified artifact layer.  This module
contains no build invocation, ROS environment mutation, or Unity API use; it
only compares declared package data against the selected static interface and
base runtime inputs.
"""


from __future__ import annotations

from pathlib import Path as _Phase192Path
_PHASE192_FACADE_FILE = _Phase192Path(__file__).resolve().parents[1] / "foxrun_custom_typesupport_common.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _Phase192Path, _PHASE192_FACADE_FILE

from pathlib import Path
from typing import Any, Mapping, Sequence

from .identity import compute_static_interface_digest, normalized_json_sha256
from .inventory import (
    _load_json,
    _reject_absolute_values,
    _validate_inventory,
)
from .managed import (
    _base_ros2_message_identity,
    _validate_managed_payload,
)
from .models import (
    AddonValidationError,
    AddonValidationRequest,
    AddonValidationResult,
    OPTIONAL_FACADE_PACKAGE_ID,
    ROS_PACKAGE_NAME,
    STATIC_INTERFACE_PACKAGE_ID,
    SUPPORTED_DISTROS,
    _normalize_distro,
)
from .native import _validate_native_payload, _validate_rmw_closures

def validate_addon(request: AddonValidationRequest) -> AddonValidationResult:
    """Validate one add-on against exact source and runtime package inputs."""

    distro = _normalize_distro(request.distro)
    addon_root = Path(request.addon_package)
    static_root = Path(request.static_interface_package)
    base_root = Path(request.base_runtime_package)
    if not addon_root.is_dir() or not static_root.is_dir() or not base_root.is_dir():
        raise AddonValidationError("provide-selected-package-inputs")

    package = _load_json(addon_root / "package.json", "repair-addon-package-json")
    manifest = _load_json(addon_root / "RuntimeSupport" / "typesupport-manifest.json", "repair-typesupport-manifest")
    inventory = _load_json(addon_root / "RuntimeSupport" / "typesupport-inventory.json", "repair-typesupport-inventory")
    static_lock = _load_json(
        static_root / "RuntimeSupport" / "foxrun-ros2-interface-lock.json",
        "repair-static-interface-lock",
    )
    runtime_manifest = _load_json(
        base_root / "RuntimeSupport" / "runtime-manifest.json",
        "repair-base-runtime-manifest",
    )

    _reject_absolute_values(package)
    _reject_absolute_values(manifest)
    _reject_absolute_values(inventory)
    _validate_static_source(static_root, static_lock)
    _validate_package_metadata(package, distro, runtime_manifest)
    _validate_manifest(manifest, distro, static_lock, runtime_manifest)
    _validate_notices(addon_root)
    base_ros2_identity = _base_ros2_message_identity(
        base_root,
        request.base_ros2_message_identity,
        allow_hash_only=True,
    )
    _validate_managed_payload(addon_root, manifest, base_ros2_identity)
    _validate_native_payload(addon_root, manifest, base_root)
    _validate_inventory(addon_root, inventory)

    requested_rmws = tuple(request.require_rmws)
    supported_rmws = tuple(manifest["supportedRmwImplementations"])
    if any(rmw not in supported_rmws for rmw in requested_rmws):
        raise AddonValidationError("repair-required-rmw-closure")
    if distro == "lyrical" and not {"rmw_fastrtps_cpp", "rmw_zenoh_cpp"}.issubset(supported_rmws):
        raise AddonValidationError("repair-lyrical-rmw-closure")
    _validate_rmw_closures(manifest, supported_rmws, addon_root, base_root)

    return AddonValidationResult(
        distro=distro,
        package_id=package["name"],
        interface_digest=manifest["source"]["interfaceDigest"],
        supported_rmws=supported_rmws,
    )


def _validate_static_source(static_root: Path, static_lock: Mapping[str, Any]) -> None:
    """Implement the internal validate static source step."""
    if static_lock.get("unityPackageId") != STATIC_INTERFACE_PACKAGE_ID:
        raise AddonValidationError("repair-static-interface-source")
    if static_lock.get("rosPackageName") != ROS_PACKAGE_NAME:
        raise AddonValidationError("repair-static-interface-source")
    expected = static_lock.get("interfaceDigest")
    if not isinstance(expected, str) or _SHA256.fullmatch(expected) is None:
        raise AddonValidationError("repair-static-interface-source")
    if compute_static_interface_digest(static_root) != expected:
        raise AddonValidationError("repair-static-interface-source")


def validate_addon_set(requests: Sequence[AddonValidationRequest]) -> tuple[AddonValidationResult, ...]:
    """Reject duplicate active add-ons before validating their individual data."""

    results = tuple(validate_addon(request) for request in requests)
    package_ids = [result.package_id for result in results]
    if len(package_ids) != len(set(package_ids)):
        raise AddonValidationError("remove-duplicate-custom-typesupport-addons")
    return results

def _validate_package_metadata(package: Mapping[str, Any], distro: str, runtime_manifest: Mapping[str, Any]) -> None:
    """Implement the internal validate package metadata step."""
    if _require_text(package, "name", "repair-addon-package-name") != addon_package_id(distro):
        raise AddonValidationError("repair-addon-package-name")
    if package.get("unity2foxgloveFoxRunCustomTypesupportAddOn") is not True:
        raise AddonValidationError("repair-addon-marker")
    license_name = _require_text(package, "license", "repair-addon-license")
    if license_name not in {"Apache-2.0"}:
        raise AddonValidationError("repair-addon-license")
    conflicts = package.get("unity2foxgloveConflicts")
    if not isinstance(conflicts, list):
        raise AddonValidationError("repair-addon-conflicts")
    expected_conflicts = {addon_package_id(item) for item in SUPPORTED_DISTROS if item != distro}
    if not expected_conflicts.issubset(set(conflicts)):
        raise AddonValidationError("repair-addon-conflicts")
    dependencies = package.get("dependencies")
    if not isinstance(dependencies, dict):
        raise AddonValidationError("repair-addon-dependencies")
    if not isinstance(dependencies.get(OPTIONAL_FACADE_PACKAGE_ID), str):
        raise AddonValidationError("repair-addon-dependencies")
    expected_runtime = base_runtime_package_id(distro)
    if runtime_manifest.get("packageName") != expected_runtime:
        raise AddonValidationError("repair-base-runtime-package")
    if dependencies.get(expected_runtime) != runtime_manifest.get("packageVersion"):
        raise AddonValidationError("repair-addon-dependencies")


def _validate_manifest(
    manifest: Mapping[str, Any],
    distro: str,
    static_lock: Mapping[str, Any],
    runtime_manifest: Mapping[str, Any],
) -> None:
    """Implement the internal validate manifest step."""
    if manifest.get("schemaVersion") != 1:
        raise AddonValidationError("repair-typesupport-manifest")
    if _require_text(manifest, "distro", "repair-addon-distro") != distro:
        raise AddonValidationError("repair-addon-distro")
    if _require_text(manifest, "platform", "repair-addon-platform") != "win64":
        raise AddonValidationError("repair-addon-platform")
    if _require_text(manifest, "architecture", "repair-addon-platform") != "x86_64":
        raise AddonValidationError("repair-addon-platform")

    source = manifest.get("source")
    if not isinstance(source, dict):
        raise AddonValidationError("repair-typesupport-source-identity")
    if _require_text(source, "upmPackageId", "repair-typesupport-source-identity") != STATIC_INTERFACE_PACKAGE_ID:
        raise AddonValidationError("repair-typesupport-source-identity")
    if _require_text(source, "rosPackageName", "repair-typesupport-source-identity") != ROS_PACKAGE_NAME:
        raise AddonValidationError("repair-typesupport-source-identity")
    if source.get("interfaceRevision") != static_lock.get("interfaceRevision"):
        raise AddonValidationError("repair-interface-revision")
    if source.get("generatorSchemaVersion") != 1:
        raise AddonValidationError("repair-generator-schema-version")
    interface_digest = _require_sha256(source, "interfaceDigest", "repair-interface-digest")
    if (
        static_lock.get("unityPackageId") != STATIC_INTERFACE_PACKAGE_ID
        or static_lock.get("rosPackageName") != ROS_PACKAGE_NAME
        or static_lock.get("interfaceDigest") != interface_digest
    ):
        raise AddonValidationError("repair-interface-digest")

    base_runtime = manifest.get("baseRuntime")
    if not isinstance(base_runtime, dict):
        raise AddonValidationError("repair-base-runtime-identity")
    if _require_text(base_runtime, "packageId", "repair-base-runtime-identity") != base_runtime_package_id(distro):
        raise AddonValidationError("repair-base-runtime-identity")
    if base_runtime.get("runtimeManifestVersion") != runtime_manifest.get("schemaVersion"):
        raise AddonValidationError("repair-base-runtime-identity")
    if _require_sha256(base_runtime, "runtimeManifestSha256", "repair-base-runtime-identity") != normalized_json_sha256(runtime_manifest):
        raise AddonValidationError("repair-base-runtime-identity")

    rmws = manifest.get("supportedRmwImplementations")
    if not isinstance(rmws, list) or not rmws or any(not isinstance(item, str) or not item for item in rmws):
        raise AddonValidationError("repair-supported-rmw-list")
    if len(rmws) != len(set(rmws)):
        raise AddonValidationError("repair-supported-rmw-list")


def _validate_notices(addon_root: Path) -> None:
    """Implement the internal validate notices step."""
    for relative in REQUIRED_LICENSE_FILES:
        path = addon_root / relative
        if not path.is_file() or not path.read_text(encoding="utf-8", errors="replace").strip():
            raise AddonValidationError("repair-addon-license-notices")

__all__ = [name for name in globals() if not name.startswith("__")]
