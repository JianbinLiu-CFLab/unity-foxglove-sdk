#!/usr/bin/env python3
from __future__ import annotations

from pathlib import Path as _Phase192Path
_PHASE192_FACADE_FILE = _Phase192Path(__file__).resolve().parents[1] / "characterize_foxrun_custom_interface.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _Phase192Path, _PHASE192_FACADE_FILE

import json
from pathlib import Path

from .foundation import (
    CharacterizationRequest,
    CharacterizationResult,
    CharacterizationError,
    ENVELOPE_TYPE_NAME,
    MANAGED_ASSEMBLY_NAME,
    ROS2_MESSAGE_IDENTITY,
    ToolchainPreflightRequest,
    preflight_toolchain,
)
from .foundation import prepare_characterization_workspace
from .workspace import _temporary_short_workspace_root, build_colcon_command
from .evidence import build_characterization_environment, inspect_managed_evidence
from .assembly import _inspect_managed_assembly, _native_evidence, _run

def characterize(request: CharacterizationRequest, *, replace_existing: bool = False) -> CharacterizationResult:
    """Run preflight, build once, inspect output, and emit machine-local evidence."""

    preflight_toolchain(
        ToolchainPreflightRequest(
            distro=request.distro,
            ros2_root=request.ros2_root,
            ros2cs_source=request.ros2cs_source,
            r2fu_source=request.r2fu_source,
            build_root=request.build_root,
            generator=request.generator,
            dotnet=request.dotnet,
        )
    )
    with _temporary_short_workspace_root(request) as (physical_workspace, active_workspace):
        workspace = prepare_characterization_workspace(
            request,
            replace_existing=replace_existing,
            _workspace_root=active_workspace,
        )
        environment = build_characterization_environment(request)
        pixi = request.ros2_root / ".pixi" / "envs" / "default"
        command = build_colcon_command(
            request,
            workspace,
            colcon=str(pixi / "Scripts" / "colcon.exe"),
            python=str(pixi / "python.exe"),
        )
        _run(command, cwd=workspace, environment=environment, log_path=workspace / "e" / "colcon.log")
        assembly = workspace / "i" / "lib" / "dotnet" / (MANAGED_ASSEMBLY_NAME + ".dll")
        if not assembly.is_file():
            raise CharacterizationError("repair-generated-managed-assembly")
        managed_evidence = _inspect_managed_assembly(workspace, assembly, request.ros2cs_install / "lib" / "dotnet", environment)
        managed = inspect_managed_evidence(managed_evidence, MANAGED_ASSEMBLY_NAME, ENVELOPE_TYPE_NAME, ROS2_MESSAGE_IDENTITY)
        evidence_path = workspace / "e" / "characterization.json"
        evidence_path.write_text(
            json.dumps(
                {
                    "schemaVersion": 1,
                    "distro": request.distro,
                    "managed": managed,
                    "nativeLibraries": _native_evidence(workspace),
                },
                sort_keys=True,
                indent=2,
            ) + "\n",
            encoding="utf-8",
        )
        native = tuple(
            physical_workspace / "i" / item.relative_to(workspace / "i")
            for item in sorted((workspace / "i").rglob("*.dll"), key=lambda item: item.as_posix().lower())
        )
        result = CharacterizationResult(
            request.distro,
            physical_workspace / "e" / "characterization.json",
            physical_workspace / "i" / "lib" / "dotnet" / (MANAGED_ASSEMBLY_NAME + ".dll"),
            native,
        )
    return result

__all__ = [name for name in globals() if not name.startswith("__")]
