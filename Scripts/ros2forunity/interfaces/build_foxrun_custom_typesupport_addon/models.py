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

import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
from dataclasses import dataclass
from typing import Any, Iterable, Mapping, Sequence

try:
    from characterize_foxrun_custom_interface import (
        CharacterizationError,
        CharacterizationRequest,
        characterize,
    )
    from foxrun_custom_typesupport_common import (
        AddonValidationRequest,
        OPTIONAL_FACADE_PACKAGE_ID,
        ROS_PACKAGE_NAME,
        STATIC_INTERFACE_PACKAGE_ID,
        addon_package_id,
        base_runtime_package_id,
        compute_static_interface_digest,
        file_sha256,
        normalized_json_sha256,
        validate_addon,
    )
except ModuleNotFoundError:  # pragma: no cover - direct script invocation
    from Scripts.ros2forunity.interfaces.characterize_foxrun_custom_interface import (
        CharacterizationError,
        CharacterizationRequest,
        characterize,
    )
    from Scripts.ros2forunity.interfaces.foxrun_custom_typesupport_common import (
        AddonValidationRequest,
        OPTIONAL_FACADE_PACKAGE_ID,
        ROS_PACKAGE_NAME,
        STATIC_INTERFACE_PACKAGE_ID,
        addon_package_id,
        base_runtime_package_id,
        compute_static_interface_digest,
        file_sha256,
        normalized_json_sha256,
        validate_addon,
    )


ERROR_CODE = "FOXRUN_TYPESUPPORT002"
MANAGED_ASSEMBLY_NAME = ROS_PACKAGE_NAME + "_assembly"
MANAGED_ASSEMBLY_FILE = MANAGED_ASSEMBLY_NAME + ".dll"
GENERATED_CATALOG_FILE = "FoxRunCustomTypesupportCatalog.g.cs"
GENERATED_CATALOG_ASMDEF = "Unity2Foxglove.FoxRun.CustomRos2Typesupport.asmdef"
_RMW_BASE_RUNTIME_LIBRARIES = {
    "rmw_fastrtps_cpp": ("rmw_fastrtps_cpp.dll",),
    "rmw_zenoh_cpp": ("rmw_zenoh_cpp.dll", "zenohc.dll"),
}


class CandidateBuildError(RuntimeError):
    """A bounded candidate-build failure that exposes no machine-private data."""

    def __init__(self, remediation: str):
        """Initializes the stable public code and operator remediation."""
        self.code = ERROR_CODE
        self.remediation = remediation
        super().__init__(self.code + ": " + remediation)


@dataclass(frozen=True)
class CandidateBuildRequest:
    """Immutable operator input for one disposable custom-typesupport candidate build."""

    distro: str
    static_interface_package: Path
    base_runtime_package: Path
    ros2_root: Path
    ros2cs_source: Path | None
    ros2cs_install: Path | None
    r2fu_source: Path | None
    build_root: Path
    generator: str = "Ninja"
    unity_executable: Path | None = None
    repo_root: Path | None = None


@dataclass(frozen=True)
class CandidateBuildResult:
    """Successful candidate package identity and the static interface digest it embeds."""

    distro: str
    candidate_package: Path
    interface_digest: str


def _phase181_distro_root(request: CandidateBuildRequest) -> Path:
    """Return the bounded per-distribution candidate root below the repository build directory."""
    build_root = Path(request.build_root)
    if build_root.name.lower() != "build":
        raise CandidateBuildError("use-repository-build-root")
    root = build_root / "phase181" / request.distro
    try:
        root.resolve().relative_to(build_root.resolve())
    except ValueError as exc:
        raise CandidateBuildError("use-repository-build-root") from exc
    return root


def candidate_package_root(request: CandidateBuildRequest) -> Path:
    """Return the sole private candidate package location for one distro."""

    return _phase181_distro_root(request) / "candidate" / "package"


def verify_candidate_source_lock(request: CandidateBuildRequest) -> str:
    """Validate the tracked static lock before invoking any native build tool."""

    lock_path = Path(request.static_interface_package) / "RuntimeSupport" / "foxrun-ros2-interface-lock.json"
    try:
        lock = json.loads(lock_path.read_text(encoding="utf-8"))
    except (OSError, UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise CandidateBuildError("repair-static-interface-lock") from exc
    if not isinstance(lock, dict):
        raise CandidateBuildError("repair-static-interface-lock")
    digest = lock.get("interfaceDigest")
    if (
        lock.get("unityPackageId") != STATIC_INTERFACE_PACKAGE_ID
        or lock.get("rosPackageName") != ROS_PACKAGE_NAME
        or lock.get("interfaceRevision") != 1
        or not isinstance(digest, str)
        or len(digest) != 64
        or compute_static_interface_digest(Path(request.static_interface_package)) != digest
    ):
        raise CandidateBuildError("repair-static-interface-lock")
    return digest


def select_candidate_native_libraries(paths: Iterable[Path]) -> tuple[Path, ...]:
    """Keep the exact native custom-message closure and reject Python tooling.

    rosidl emits a Python generator DLL that depends on the ROS build Python
    runtime.  It is a build-time artifact, not a Unity Player dependency.  All
    remaining custom package DLLs are retained and their PE imports are checked
    by the shared add-on validator before any sync.
    """

    prefix = ROS_PACKAGE_NAME.lower()
    selected = []
    for path in paths:
        name = path.name.lower()
        if not name.endswith(".dll") or not name.startswith(prefix):
            continue
        if name.endswith("__rosidl_generator_py.dll"):
            continue
        selected.append(path)
    return tuple(selected)


def _repo_root(request: CandidateBuildRequest) -> Path:
    """Resolve the repository root supplied by a test or the checked-in script location."""
    if request.repo_root is not None:
        return Path(request.repo_root)
    return Path(__file__).resolve().parents[3]


def _load_json(path: Path, remediation: str) -> Mapping[str, Any]:
    """Load one object-shaped JSON document or raise a bounded remediation error."""
    try:
        payload = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise CandidateBuildError(remediation) from exc
    if not isinstance(payload, dict):
        raise CandidateBuildError(remediation)
    return payload


def _runtime_rmws(runtime_manifest: Mapping[str, Any], distro: str) -> tuple[str, ...]:
    """Read RMW support through the same capability precedence as the runtime.

    Older Humble/Jazzy runtime manifests expose one legacy ``rmwImplementation``
    field; newer manifests may provide an explicit union or communication-mode
    records.  The build tool accepts all three representations but never
    invents an RMW from the distro name.
    """

    explicit = runtime_manifest.get("supportedRmwImplementations")
    if isinstance(explicit, list) and explicit and all(isinstance(item, str) and item for item in explicit):
        rmws = explicit
    else:
        modes = runtime_manifest.get("communicationModes")
        if isinstance(modes, list):
            from_modes = [
                item.get("rmwImplementation")
                for item in modes
                if isinstance(item, dict) and isinstance(item.get("rmwImplementation"), str) and item["rmwImplementation"]
            ]
            rmws = from_modes if from_modes else None
        else:
            rmws = None
        if not rmws:
            legacy = runtime_manifest.get("rmwImplementation")
            rmws = [legacy] if isinstance(legacy, str) and legacy else None

    if not rmws:
        raise CandidateBuildError("repair-base-runtime-rmw-policy")
    normalized = tuple(sorted(set(rmws)))
    if distro == "lyrical" and not {"rmw_fastrtps_cpp", "rmw_zenoh_cpp"}.issubset(normalized):
        raise CandidateBuildError("repair-lyrical-rmw-policy")
    return normalized


def _rmw_closures(
    runtime_manifest: Mapping[str, Any],
    distro: str,
    native_entries: Sequence[Mapping[str, str]],
) -> Mapping[str, Mapping[str, list[str]]]:
    """Record the generated closure that must be available for each RMW."""

    add_on_libraries = [entry["path"] for entry in native_entries]
    closures: dict[str, Mapping[str, list[str]]] = {}
    for rmw in _runtime_rmws(runtime_manifest, distro):
        base_libraries = _RMW_BASE_RUNTIME_LIBRARIES.get(rmw)
        if base_libraries is None:
            raise CandidateBuildError("repair-required-rmw-closure")
        closures[rmw] = {
            "baseRuntimeLibraries": list(base_libraries),
            "addOnLibraries": add_on_libraries,
        }
    return closures


def _candidate_assembly_path(request: CandidateBuildRequest) -> Path:
    """Return the managed assembly emitted by the controlled candidate characterization."""
    return _phase181_distro_root(request) / "candidate" / "i" / "lib" / "dotnet" / MANAGED_ASSEMBLY_FILE


def _candidate_native_paths(request: CandidateBuildRequest) -> tuple[Path, ...]:
    """Return the deterministic native custom-message closure from the candidate output."""
    native_root = _phase181_distro_root(request) / "candidate" / "i" / "bin"
    return select_candidate_native_libraries(sorted(native_root.glob("*.dll"), key=lambda item: item.name.lower()))


def _managed_package_assembly_path(package_root: Path) -> Path:
    """Place ros2cs managed messages beside the base runtime's managed assemblies.

    R2FU keeps CLR message assemblies directly below ``Plugins/`` while native
    typesupport remains in the platform-specific ``Windows/x86_64`` child.
    Keeping those loader domains separate is required for Unity's managed
    assembly resolver and mirrors the selected runtime package layout.
    """
    return Path(package_root) / "Runtime" / "Ros2ForUnity" / "Plugins" / MANAGED_ASSEMBLY_FILE

__all__ = [name for name in globals() if not name.startswith("__")]
