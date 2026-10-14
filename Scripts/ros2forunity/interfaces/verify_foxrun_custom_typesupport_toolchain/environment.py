#!/usr/bin/env python3
"""Non-mutating Phase181 Win64 custom-typesupport toolchain preflight.

The tool accepts explicit ROS2 and source roots, builds a minimal child
environment, and probes only the selected toolchain. It never downloads a
tool, sources a user shell profile, or writes below ``Packages/``. Successful
results are recorded as redacted provenance below ``build/phase181``.
"""

from __future__ import annotations

from pathlib import Path as _Phase192Path
_PHASE192_FACADE_FILE = _Phase192Path(__file__).resolve().parents[1] / "verify_foxrun_custom_typesupport_toolchain.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _Phase192Path, _PHASE192_FACADE_FILE

import os
from pathlib import Path

from .models import (
    DEFAULT_GENERATOR,
    SUPPORTED_DISTROS,
    ToolchainPreflightError,
    ToolchainPreflightRequest,
)

def build_toolchain_environment(request: ToolchainPreflightRequest) -> dict[str, str]:
    """Return a controlled child environment for one ROS2 distribution.

    The host environment supplies only Windows process essentials. ROS paths,
    Python paths, compiler selection, and RMW choice are selected by this
    request; no setup script or arbitrary profile is sourced.
    """

    root = request.ros2_root
    pixi = root / ".pixi" / "envs" / "default"
    inherited_keys = (
        # dotnet/NuGet resolves its user config and SDK roots through these
        # Windows process variables. They are process essentials, not a shell
        # profile or ROS configuration.
        "APPDATA",
        "ComSpec",
        "HOMEDRIVE",
        "HOMEPATH",
        "NUMBER_OF_PROCESSORS",
        "PATHEXT",
        "ProgramFiles",
        "SystemDrive",
        "SystemRoot",
        "TEMP",
        "TMP",
        "USERPROFILE",
        "WINDIR",
    )
    environment = {key: os.environ[key] for key in inherited_keys if os.environ.get(key)}
    environment["ROS_DISTRO"] = request.distro
    environment["RMW_IMPLEMENTATION"] = "rmw_fastrtps_cpp"
    environment["AMENT_PREFIX_PATH"] = str(root)
    environment["CMAKE_PREFIX_PATH"] = str(root)
    environment["COLCON_PREFIX_PATH"] = str(root)
    environment["PYTHONPATH"] = str(root / "Lib" / "site-packages")
    path_entries = (
        pixi / "Scripts",
        pixi / "Library" / "bin",
        root / "Scripts",
        root / "bin",
        request.dotnet.parent if request.dotnet else Path(os.environ.get("ProgramFiles", r"C:\\Program Files")) / "dotnet",
        Path(environment.get("SystemRoot", r"C:\Windows")) / "System32",
    )
    environment["PATH"] = os.pathsep.join(str(entry) for entry in path_entries)
    return environment


def _normalize_request(request: ToolchainPreflightRequest) -> ToolchainPreflightRequest:
    """Implement the internal normalize request step."""
    distro = (request.distro or "").strip().lower()
    if distro not in SUPPORTED_DISTROS:
        raise ToolchainPreflightError("select-supported-ros-distro")
    if not (request.generator or "").strip():
        raise ToolchainPreflightError("select-supported-cmake-generator")
    return ToolchainPreflightRequest(
        distro=distro,
        ros2_root=Path(request.ros2_root),
        ros2cs_source=Path(request.ros2cs_source),
        r2fu_source=Path(request.r2fu_source),
        build_root=Path(request.build_root),
        ros2cs_install=Path(request.ros2cs_install) if request.ros2cs_install else None,
        generator=request.generator.strip(),
        vswhere=Path(request.vswhere) if request.vswhere else None,
        dotnet=Path(request.dotnet) if request.dotnet else Path(os.environ.get("ProgramFiles", r"C:\\Program Files")) / "dotnet" / "dotnet.exe",
    )

__all__ = [name for name in globals() if not name.startswith("__")]
