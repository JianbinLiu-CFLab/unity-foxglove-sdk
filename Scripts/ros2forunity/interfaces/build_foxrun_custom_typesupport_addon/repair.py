#!/usr/bin/env python3
"""Build one Phase181 custom ROS2 typesupport add-on below ``build/phase181``.

The script deliberately has two materialization boundaries.  rosidl output is
first characterized in an out-of-tree candidate workspace.  A Unity batch
importer then generates PluginImporter metadata for every managed and native
candidate DLL.  Only the sibling ``sync_...`` command may copy a fully
validated candidate to ``Packages/``.
"""


from __future__ import annotations

from pathlib import Path as _Phase192Path
_PHASE192_FACADE_FILE = _Phase192Path(__file__).resolve().parents[1] / "build_foxrun_custom_typesupport_addon.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _Phase192Path, _PHASE192_FACADE_FILE

import hashlib
from pathlib import Path
from typing import Mapping

from .models import *
from .serialization import *

def _repair_tracked_addon_catalog(request: CandidateBuildRequest) -> Path:
    """Regenerate tracked catalog and canonical legal metadata without payload rebuild.

    This is intentionally narrower than ``build_candidate``: it never touches a
    native or managed payload, never creates a package, and derives generated
    metadata exclusively from the validated static lock plus tracked sources.
    """

    interface_digest = verify_candidate_source_lock(request)
    repository_root = _repo_root(request).resolve()
    package_root = repository_root / "Packages" / addon_package_id(request.distro)
    if not package_root.is_dir():
        raise CandidateBuildError("repair-tracked-typesupport-package")

    manifest = _load_json(
        package_root / "RuntimeSupport" / "typesupport-manifest.json",
        "repair-typesupport-manifest",
    )
    source = manifest.get("source")
    base_runtime = manifest.get("baseRuntime")
    managed = manifest.get("managed")
    if (
        not isinstance(source, Mapping)
        or source.get("upmPackageId") != STATIC_INTERFACE_PACKAGE_ID
        or source.get("rosPackageName") != ROS_PACKAGE_NAME
        or source.get("interfaceRevision") != 1
        or source.get("interfaceDigest") != interface_digest
        or not isinstance(base_runtime, Mapping)
        or base_runtime.get("packageId") != base_runtime_package_id(request.distro)
        or not isinstance(managed, Mapping)
        or not isinstance(managed.get("typeMap"), list)
    ):
        raise CandidateBuildError("repair-typesupport-catalog-source")

    supported_rmws = manifest.get("supportedRmwImplementations")
    if (
        not isinstance(supported_rmws, list)
        or not supported_rmws
        or not all(isinstance(item, str) and item for item in supported_rmws)
    ):
        raise CandidateBuildError("repair-typesupport-catalog-rmw-capability")

    type_map: list[dict[str, str]] = []
    managed_prefix = ROS_PACKAGE_NAME + ".msg."
    for item in managed["typeMap"]:
        if not isinstance(item, Mapping):
            raise CandidateBuildError("repair-typesupport-catalog-source")
        canonical = item.get("canonicalRosType")
        managed_type = item.get("managedType")
        if not isinstance(canonical, str) or not isinstance(managed_type, str):
            raise CandidateBuildError("repair-typesupport-catalog-source")
        if not managed_type.startswith(managed_prefix):
            raise CandidateBuildError("repair-typesupport-catalog-source")
        message_name = managed_type[len(managed_prefix):]
        if (
            not message_name
            or not all(character.isalnum() or character == "_" for character in message_name)
            or canonical != ROS_PACKAGE_NAME + "/msg/" + message_name
        ):
            raise CandidateBuildError("repair-typesupport-catalog-source")
        type_map.append({"canonicalRosType": canonical, "managedType": managed_type})
    if not type_map:
        raise CandidateBuildError("repair-typesupport-catalog-source")

    generated_root = package_root / "Runtime" / "FoxRun" / "Generated"
    catalog_path = generated_root / GENERATED_CATALOG_FILE
    if not generated_root.is_dir() or not catalog_path.is_file():
        raise CandidateBuildError("repair-typesupport-catalog-source")

    _write_utf8_lf(
        catalog_path,
        _catalog_source(
            distro=request.distro,
            interface_digest=interface_digest,
            type_map=tuple(type_map),
            supported_rmws=tuple(supported_rmws),
        ),
    )
    _write_canonical_license(repository_root / "LICENSE", package_root / "LICENSE")
    _write_inventory(package_root)
    return catalog_path


def _repair_tracked_addon_license_eol(request: CandidateBuildRequest) -> bool:
    """Restore only a license whose canonical source bytes are already inventory-locked.

    This deliberately does not rewrite the inventory or any generated/native
    payload.  It is safe to run before a strict validator: a noncanonical
    inventory, a missing license, or any other payload drift still fails closed
    in the subsequent full add-on validation.
    """

    verify_candidate_source_lock(request)
    repository_root = _repo_root(request).resolve()
    package_root = repository_root / "Packages" / addon_package_id(request.distro)
    license_path = package_root / "LICENSE"
    if not license_path.is_file():
        raise CandidateBuildError("repair-typesupport-license-inventory")
    inventory = _load_json(
        package_root / "RuntimeSupport" / "typesupport-inventory.json",
        "repair-typesupport-license-inventory",
    )
    entries = inventory.get("entries") if isinstance(inventory, Mapping) else None
    if not isinstance(entries, list):
        raise CandidateBuildError("repair-typesupport-license-inventory")
    license_entries = [item for item in entries if isinstance(item, Mapping) and item.get("path") == "LICENSE"]
    if len(license_entries) != 1:
        raise CandidateBuildError("repair-typesupport-license-inventory")
    expected_length = license_entries[0].get("byteLength")
    expected_sha256 = license_entries[0].get("sha256")
    if (
        not isinstance(expected_length, int)
        or isinstance(expected_length, bool)
        or expected_length < 0
        or not isinstance(expected_sha256, str)
        or len(expected_sha256) != 64
        or any(character not in "0123456789abcdef" for character in expected_sha256)
    ):
        raise CandidateBuildError("repair-typesupport-license-inventory")
    canonical_bytes = _read_canonical_license_bytes(repository_root / "LICENSE")
    if len(canonical_bytes) != expected_length or hashlib.sha256(canonical_bytes).hexdigest() != expected_sha256:
        raise CandidateBuildError("repair-typesupport-license-inventory")
    try:
        actual_bytes = license_path.read_bytes()
    except OSError as exc:
        raise CandidateBuildError("repair-typesupport-license-inventory") from exc
    if actual_bytes == canonical_bytes:
        return False
    try:
        license_path.write_bytes(canonical_bytes)
    except OSError as exc:
        raise CandidateBuildError("repair-typesupport-license-inventory") from exc
    return True

__all__ = [name for name in globals() if not name.startswith("__")]
