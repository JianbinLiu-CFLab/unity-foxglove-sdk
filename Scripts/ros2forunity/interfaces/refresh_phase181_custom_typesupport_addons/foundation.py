#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Detect, rebuild, sync, and validate only stale Phase181 custom ROS2 typesupport add-ons.

"""Refresh verified Phase181 custom ROS2 typesupport add-ons without operator-memory steps.

The command deliberately orchestrates the existing materialization boundaries:

* the candidate builder writes only below ``build/phase181/<distro>/candidate``;
* the sync command copies only a validated candidate inventory into ``Packages``;
* the final validator proves the tracked add-on matches its static interface and
  current base runtime.

Without ``--apply`` this is a read-only check.  With ``--apply`` it rebuilds
only add-ons whose static-interface or base-runtime manifest identity changed.
The caller must provide explicit external source and Unity paths; this script
never invents machine-specific toolchain locations.
"""

from __future__ import annotations


from pathlib import Path as _Phase192Path
_PHASE192_FACADE_FILE = _Phase192Path(__file__).resolve().parents[1] / "refresh_phase181_custom_typesupport_addons.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _Phase192Path, _PHASE192_FACADE_FILE

import argparse
from dataclasses import dataclass
import json
import os
from pathlib import Path, PurePosixPath
import subprocess
import sys
from typing import Callable, Mapping, Sequence

try:
    from foxrun_custom_typesupport_common import (
        SUPPORTED_DISTROS,
        addon_package_id,
        base_runtime_package_id,
        normalized_json_sha256,
    )
except ModuleNotFoundError:  # pragma: no cover - direct script invocation
    from Scripts.ros2forunity.interfaces.foxrun_custom_typesupport_common import (
        SUPPORTED_DISTROS,
        addon_package_id,
        base_runtime_package_id,
        normalized_json_sha256,
    )


ERROR_CODE = "FOXRUN_TYPESUPPORT004"
RUNTIME_PACKAGE_PREFIX = "dev.unity2foxglove.ros2forunity.runtime."
STATIC_INTERFACE_PACKAGE_ID = "dev.unity2foxglove.foxrun.ros2.interfaces"
_ROS2CS_INSTALL_READY_FILES = (
    "share/rosidl_generator_cs/cmake/rosidl_generator_csConfig.cmake",
    "share/builtin_interfaces/cmake/builtin_interfacesConfig.cmake",
    "include/builtin_interfaces/builtin_interfaces/msg/detail/time__struct.hpp",
    "lib/dotnet/ros2cs_common.dll",
    "lib/dotnet/builtin_interfaces_assembly.dll",
)


class AddonRefreshError(RuntimeError):
    """A bounded failure for the Phase181 add-on refresh transaction."""

    def __init__(self, remediation: str):
        """Record the operator-facing remediation with the stable refresh error code."""

        self.code = ERROR_CODE
        self.remediation = remediation
        super().__init__(self.code + ": " + remediation)


@dataclass(frozen=True)
class AddonRefreshState:
    """Describe whether one tracked add-on still matches its two source identities."""

    distro: str
    current: bool
    reasons: tuple[str, ...]


@dataclass(frozen=True)
class AddonRefreshRequest:
    """Represent one explicit bounded add-on refresh request."""

    root: Path
    distros: tuple[str, ...]
    apply: bool
    ros2cs_source: Path | None = None
    r2fu_source: Path | None = None
    unity: Path | None = None
    ros2_roots: tuple[tuple[str, Path], ...] = ()


def repository_root() -> Path:
    """Return this repository root without relying on the current directory."""

    return Path(__file__).resolve().parents[3]


def _read_object(path: Path, remediation: str) -> dict[str, object]:
    """Read one required JSON object using a bounded public failure label."""

    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise AddonRefreshError(remediation) from exc
    if not isinstance(value, dict):
        raise AddonRefreshError(remediation)
    return value


def _mapping_text(value: object, key: str) -> str | None:
    """Return one non-empty text field from a JSON object-like value."""

    if not isinstance(value, Mapping):
        return None
    candidate = value.get(key)
    return candidate if isinstance(candidate, str) and candidate else None


def _static_interface_digest(root: Path) -> str:
    """Read the canonical digest that every generated add-on must embed."""

    lock = _read_object(
        root / "Packages" / STATIC_INTERFACE_PACKAGE_ID / "RuntimeSupport" / "foxrun-ros2-interface-lock.json",
        "repair-static-interface-lock",
    )
    digest = _mapping_text(lock, "interfaceDigest")
    if digest is None:
        raise AddonRefreshError("repair-static-interface-lock")
    return digest


def _runtime_manifest_hash(root: Path, distro: str) -> str:
    """Return the normalized identity that binds an add-on to its base runtime."""

    manifest = _read_object(
        root / "Packages" / base_runtime_package_id(distro) / "RuntimeSupport" / "runtime-manifest.json",
        "repair-base-runtime-manifest",
    )
    return normalized_json_sha256(manifest)

__all__ = [name for name in globals() if not name.startswith("__")]
