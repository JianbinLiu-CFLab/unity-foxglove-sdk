#!/usr/bin/env python3
"""Build and characterize the Phase181 static ROS2 interface out of tree.

This is deliberately a characterization gate, not package generation.  It
uses an explicitly selected ROS2 root, ros2cs overlay, and Visual Studio
installation to build the source-only package into ``build/phase181``.  The
only products are machine-local evidence and candidate files below that build
root; no Unity package is modified here.
"""

from __future__ import annotations

from pathlib import Path as _Phase192Path
_PHASE192_FACADE_FILE = _Phase192Path(__file__).resolve().parents[1] / "characterize_foxrun_custom_interface.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _Phase192Path, _PHASE192_FACADE_FILE

import os
import re
import shutil
import time
from dataclasses import dataclass
from pathlib import Path

try:
    from verify_foxrun_custom_typesupport_toolchain import (
        ToolchainPreflightRequest,
        build_toolchain_environment,
        preflight_toolchain,
    )
except ModuleNotFoundError:  # pragma: no cover - package import test path
    from Scripts.ros2forunity.interfaces.verify_foxrun_custom_typesupport_toolchain import (
        ToolchainPreflightRequest,
        build_toolchain_environment,
        preflight_toolchain,
    )

ERROR_CODE = "FOXRUN_TOOLCHAIN002"
ROS_PACKAGE_NAME = "unity2foxglove_foxrun_interfaces_v1"
MANAGED_ASSEMBLY_NAME = ROS_PACKAGE_NAME + "_assembly"
ENVELOPE_TYPE_NAME = ROS_PACKAGE_NAME + ".msg.Phase181State48D288ED82F1Envelope"
ROS2_MESSAGE_IDENTITY = "ROS2.Message"
_LONGEST_NATIVE_TARGET = "unity2foxglove_foxrun_interfaces_v1__rosidl_typesupport_introspection_c__pyext.dir"
_LONGEST_NATIVE_OBJECT = "95441c87d059a3e1deffafe69425029c/_unity2foxglove_foxrun_interfaces_v1_s.ep.rosidl_typesupport_introspection_c.c.obj"
_WORKSPACE_DELETE_ATTEMPTS = 5
_WORKSPACE_DELETE_RETRY_SECONDS = 1.0


class CharacterizationError(RuntimeError):
    """A bounded, actionable custom-interface characterization failure."""

    def __init__(self, remediation: str):
        """Initialize this object."""
        self.code = ERROR_CODE
        self.remediation = remediation
        super().__init__(self.code + ": " + remediation)


@dataclass(frozen=True)
class CharacterizationRequest:
    """Represent CharacterizationRequest."""
    distro: str
    static_package: Path
    ros2_root: Path
    ros2cs_source: Path
    ros2cs_install: Path
    r2fu_source: Path
    build_root: Path
    generator: str = "Ninja"
    dotnet: Path | None = None
    workspace_name: str = "c"


@dataclass(frozen=True)
class CharacterizationResult:
    """Represent CharacterizationResult."""
    distro: str
    evidence_path: Path
    managed_assembly: Path
    native_libraries: tuple[Path, ...]


def _require_under(path: Path, root: Path) -> Path:
    """Implement the internal require under step."""
    resolved = path.resolve()
    try:
        resolved.relative_to(root.resolve())
    except ValueError as exc:
        raise CharacterizationError("use-phase181-build-root") from exc
    return resolved


def characterization_root(request: CharacterizationRequest) -> Path:
    """Select the short, private build root for one characterization run."""
    # rosidl target names are deliberately descriptive and can otherwise push
    # MSVC object/PDB paths past the Windows limit. Keep this private build
    # layout short while retaining the public evidence boundary below
    # ``build/phase181/<distro>/``.
    workspace_name = request.workspace_name or ""
    if re.fullmatch(r"[a-z0-9][a-z0-9_-]*", workspace_name) is None:
        raise CharacterizationError("select-safe-phase181-workspace-name")
    return _characterization_parent(request) / workspace_name


def _characterization_parent(request: CharacterizationRequest) -> Path:
    """Implement the internal characterization parent step."""
    return request.build_root / "phase181" / request.distro


def _is_windows_host() -> bool:
    """Return whether host-specific Windows path workarounds may run."""

    return os.name == "nt"


def requires_short_windows_build_alias(characterization_parent: Path) -> bool:
    """Return whether the deepest rosidl object would exceed safe MSVC space."""

    if not _is_windows_host():
        return False
    projected = (
        characterization_parent
        / "b"
        / ROS_PACKAGE_NAME
        / "CMakeFiles"
        / _LONGEST_NATIVE_TARGET
        / _LONGEST_NATIVE_OBJECT
    )
    # CMake's Windows generator limit is 250 characters. Mapping the physical
    # workspace itself removes the complete ``build/phase181/<distro>/c``
    # prefix when needed.
    return len(str(projected)) > 250


def _remove_prior_workspace(root: Path) -> None:
    """Remove a build-owned workspace, tolerating a short-lived Ninja lock."""

    for attempt in range(_WORKSPACE_DELETE_ATTEMPTS):
        try:
            shutil.rmtree(root)
            return
        except PermissionError as exc:
            is_transient_windows_lock = getattr(exc, "winerror", None) == 32
            if not is_transient_windows_lock or attempt + 1 == _WORKSPACE_DELETE_ATTEMPTS:
                raise CharacterizationError("close-build-handles-and-retry-characterization") from exc
            time.sleep(_WORKSPACE_DELETE_RETRY_SECONDS)


def prepare_characterization_workspace(
    request: CharacterizationRequest,
    *,
    replace_existing: bool = False,
    _workspace_root: Path | None = None,
) -> Path:
    """Stage the static package into a controlled out-of-tree colcon source."""

    source = request.static_package / "Ros2Package~"
    if not (source / "package.xml").is_file() or not (source / "CMakeLists.txt").is_file():
        raise CharacterizationError("provide-locked-static-interface-source")
    root = _workspace_root or characterization_root(request)
    if _workspace_root is None:
        _require_under(root, request.build_root)
    if root.exists():
        if not replace_existing:
            raise CharacterizationError("remove-or-replace-prior-characterization")
        _remove_prior_workspace(root)
    workspace = root
    destination = workspace / "s" / ROS_PACKAGE_NAME
    destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.copytree(source, destination)
    return workspace

__all__ = [name for name in globals() if not name.startswith("__")]
