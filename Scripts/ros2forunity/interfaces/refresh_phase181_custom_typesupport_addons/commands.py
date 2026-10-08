#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

from __future__ import annotations

from . import foundation as _foundation
from . import inspection as _inspection
from .foundation import *
from .inspection import *

from pathlib import Path as _Phase192Path
_PHASE192_FACADE_FILE = _Phase192Path(__file__).resolve().parents[1] / "refresh_phase181_custom_typesupport_addons.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _Phase192Path, _PHASE192_FACADE_FILE

def _script(root: Path, name: str) -> Path:
    """Resolve one sibling Phase181 script beneath this repository only."""

    path = root / "Scripts" / "ros2forunity" / "interfaces" / name
    if not path.is_file():
        raise _inspection.AddonRefreshError("repair-phase181-add-on-tooling")
    return path


def _explicit_ros2_root(request: _inspection.AddonRefreshRequest, distro: str) -> Path | None:
    """Return the sole explicit ROS root for one distro, when supplied."""

    matches = [Path(path).resolve() for candidate, path in request.ros2_roots if candidate == distro]
    if len(matches) > 1:
        raise _inspection.AddonRefreshError("provide-one-ros2-root-per-distro")
    return matches[0] if matches else None


def build_command(
    request: _inspection.AddonRefreshRequest,
    distro: str,
    *,
    toolchain: tuple[Path, Path, Path, Path] | None = None,
) -> list[str]:
    """Build the exact argument vector for one candidate-only materialization."""

    root = Path(request.root).resolve()
    ros2cs_source, ros2cs_install, r2fu_source, unity = (
        toolchain
        if toolchain is not None
        else _inspection._require_apply_toolchain(request, distro)
    )
    command = [
        sys.executable,
        str(_script(root, "build_foxrun_custom_typesupport_addon.py")),
        "--distro",
        distro,
        "--ros2cs-source",
        str(ros2cs_source),
        "--ros2cs-install",
        str(ros2cs_install),
        "--r2fu-source",
        str(r2fu_source),
        "--build-root",
        str(root / "build"),
        "--unity",
        str(unity),
    ]
    ros2_root = _explicit_ros2_root(request, distro)
    if ros2_root is not None:
        command.extend(("--ros2-root", str(ros2_root)))
    return command


def sync_command(root: Path, distro: str) -> list[str]:
    """Build the exact argument vector for the validated-candidate sync boundary."""

    return [
        sys.executable,
        str(_script(root, "sync_foxrun_custom_typesupport_addon.py")),
        "--distro",
        distro,
    ]


def validation_command(root: Path, distro: str) -> list[str]:
    """Build the final validation command for one distro's advertised RMW closure."""

    command = [
        sys.executable,
        str(_script(root, "validate_foxrun_custom_typesupport_addon.py")),
        "--distro",
        distro,
    ]
    required_rmws = ("rmw_fastrtps_cpp", "rmw_zenoh_cpp") if distro == "lyrical" else ("rmw_fastrtps_cpp",)
    for rmw in required_rmws:
        command.extend(("--require-rmw", rmw))
    return command


def _run_child(command: Sequence[str], *, root: Path, runner: Callable[..., subprocess.CompletedProcess] | None) -> None:
    """Run one child by argv only and surface a bounded orchestration failure."""

    execute = runner or subprocess.run
    result = execute(list(command), cwd=str(root), check=False)
    if getattr(result, "returncode", 1) != 0:
        raise _inspection.AddonRefreshError("repair-phase181-add-on-command")

__all__ = [name for name in globals() if not name.startswith("__")]
