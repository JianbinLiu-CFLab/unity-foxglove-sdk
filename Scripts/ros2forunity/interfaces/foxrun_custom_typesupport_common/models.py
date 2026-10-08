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

from dataclasses import dataclass
from pathlib import Path, PurePosixPath
import re
from typing import Any, Iterable, Mapping, Sequence

STATIC_INTERFACE_PACKAGE_ID = "dev.unity2foxglove.foxrun.ros2.interfaces"
ROS_PACKAGE_NAME = "unity2foxglove_foxrun_interfaces_v1"
OPTIONAL_FACADE_PACKAGE_ID = "dev.unity2foxglove.ros2forunity"
SUPPORTED_DISTROS = ("humble", "jazzy", "lyrical")
REQUIRED_LICENSE_FILES = ("LICENSE", "README.md", "THIRD_PARTY_NOTICES.md")
EXPECTED_PLUGIN_PLATFORMS = ("Editor", "WindowsStandalone64")
_KNOWN_RMW_BASE_RUNTIME_LIBRARIES = {
    "rmw_fastrtps_cpp": ("rmw_fastrtps_cpp.dll",),
    "rmw_zenoh_cpp": ("rmw_zenoh_cpp.dll", "zenohc.dll"),
}

_SHA256 = re.compile(r"^[0-9a-f]{64}$")
_MVID = re.compile(r"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$")
_ABSOLUTE_PATH = re.compile(r"^(?:[A-Za-z]:[\\\\/]|[\\\\/]{2}|/)")


class AddonValidationError(ValueError):
    """A bounded add-on validation failure with no machine-private path text."""

    code = "FOXRUN_TYPESUPPORT001"

    def __init__(self, remediation: str):
        """Initialize this object."""
        self.remediation = remediation
        super().__init__(self.code + ": " + remediation)


@dataclass(frozen=True)
class AddonValidationRequest:
    """Explicit selected-package inputs for one add-on validation."""

    distro: str
    addon_package: Path
    static_interface_package: Path
    base_runtime_package: Path
    require_rmws: tuple[str, ...] = ()
    # Test fixtures use this injection point to keep the schema validator pure
    # when their tiny fake DLLs are deliberately not CLR assemblies. Production
    # callers leave it empty and inspect the selected base runtime read-only.
    base_ros2_message_identity: Mapping[str, str] | None = None


@dataclass(frozen=True)
class AddonValidationResult:
    """Publicly useful, machine-neutral result from a validated add-on."""

    distro: str
    package_id: str
    interface_digest: str
    supported_rmws: tuple[str, ...]

__all__ = [name for name in globals() if not name.startswith("__")]
