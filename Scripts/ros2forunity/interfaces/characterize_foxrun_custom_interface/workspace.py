#!/usr/bin/env python3
from __future__ import annotations

from pathlib import Path as _Phase192Path
_PHASE192_FACADE_FILE = _Phase192Path(__file__).resolve().parents[1] / "characterize_foxrun_custom_interface.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _Phase192Path, _PHASE192_FACADE_FILE

from contextlib import contextmanager
import os
import shutil
import subprocess
import time
from pathlib import Path
from typing import Iterator

from .foundation import (
    CharacterizationError,
    CharacterizationRequest,
    ROS_PACKAGE_NAME,
    _WORKSPACE_DELETE_ATTEMPTS,
    _WORKSPACE_DELETE_RETRY_SECONDS,
    _require_under,
    characterization_root,
    requires_short_windows_build_alias,
)

@contextmanager
def _temporary_short_workspace_root(request: CharacterizationRequest) -> Iterator[tuple[Path, Path]]:
    """Map only this verified workspace root when Windows path length demands it.

    The mapping is a temporary alias of the exact physical
    ``build/phase181/<distro>/c`` directory; it neither moves nor copies files.
    It is removed in ``finally`` and never replaces a pre-existing drive.
    """

    physical_workspace = characterization_root(request)
    _require_under(physical_workspace, request.build_root)
    physical_workspace.mkdir(parents=True, exist_ok=True)
    if not requires_short_windows_build_alias(physical_workspace):
        yield physical_workspace, physical_workspace
        return

    subst = Path(os.environ.get("SystemRoot", r"C:\\Windows")) / "System32" / "subst.exe"
    if not subst.is_file():
        raise CharacterizationError("provide-short-windows-build-root")

    mapped_drive: str | None = None
    for letter in "ZYXWVUTSRQPONMLKJIHGFED":
        candidate = Path(letter + ":\\")
        if candidate.exists():
            continue
        result = subprocess.run(
            (str(subst), letter + ":", str(physical_workspace)),
            shell=False,
            capture_output=True,
            text=True,
            errors="replace",
            check=False,
        )
        if result.returncode == 0:
            if candidate.exists():
                mapped_drive = letter
                break
            # ``subst`` may acknowledge a mapping before the drive is
            # visible to this process. Undo that reservation before trying
            # another letter so failed probes cannot leak mappings.
            cleanup = subprocess.run(
                (str(subst), letter + ":", "/D"),
                shell=False,
                capture_output=True,
                text=True,
                errors="replace",
                check=False,
            )
            if cleanup.returncode != 0 or candidate.exists():
                raise CharacterizationError("provide-short-windows-build-root")
    if mapped_drive is None:
        raise CharacterizationError("provide-short-windows-build-root")

    mapped_workspace = Path(mapped_drive + ":\\")
    primary_error: BaseException | None = None
    try:
        yield physical_workspace, mapped_workspace
    except BaseException as exc:
        primary_error = exc
        raise
    finally:
        cleanup = subprocess.run(
            (str(subst), mapped_drive + ":", "/D"),
            shell=False,
            capture_output=True,
            text=True,
            errors="replace",
            check=False,
        )
        if cleanup.returncode != 0 or Path(mapped_drive + ":\\").exists():
            cleanup_error = CharacterizationError("cleanup-short-windows-build-root")
            if primary_error is not None:
                raise cleanup_error from primary_error
            raise cleanup_error


def build_colcon_command(
    request: CharacterizationRequest,
    workspace: Path,
    *,
    colcon: str = "colcon",
    python: str = "python",
) -> tuple[str, ...]:
    """Return the explicit out-of-tree rosidl build command without a shell."""

    cmake_python = python.replace("\\", "/")
    openssl_root = (request.ros2_root / ".pixi" / "envs" / "default" / "Library").as_posix()

    return (
        colcon,
        "--log-base",
        str(workspace / "l"),
        "build",
        "--merge-install",
        "--base-paths",
        str(workspace / "s"),
        "--build-base",
        str(workspace / "b"),
        "--install-base",
        str(workspace / "i"),
        "--event-handlers",
        "console_direct+",
        "--cmake-args",
        "-G",
        request.generator,
        "-DCMAKE_BUILD_TYPE=Release",
        "-DBUILD_TESTING=OFF",
        "-DSTANDALONE_BUILD:BOOL=ON",
        "-DPython3_EXECUTABLE=" + cmake_python,
        "-DPYTHON_EXECUTABLE=" + cmake_python,
        "-DOPENSSL_ROOT_DIR=" + openssl_root,
        "--no-warn-unused-cli",
    )

__all__ = [name for name in globals() if not name.startswith("__")]
