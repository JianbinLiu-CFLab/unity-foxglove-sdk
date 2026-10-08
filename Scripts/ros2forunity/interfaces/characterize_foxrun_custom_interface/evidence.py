#!/usr/bin/env python3
from __future__ import annotations

from pathlib import Path as _Phase192Path
_PHASE192_FACADE_FILE = _Phase192Path(__file__).resolve().parents[1] / "characterize_foxrun_custom_interface.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _Phase192Path, _PHASE192_FACADE_FILE

import json
import os
import subprocess
from pathlib import Path
from typing import Mapping

from .foundation import (
    CharacterizationError,
    CharacterizationRequest,
    ToolchainPreflightRequest,
    build_toolchain_environment,
)

def inspect_managed_evidence(
    evidence_path: Path,
    expected_assembly: str,
    expected_envelope_type: str,
    expected_ros2_message_identity: str,
) -> dict[str, object]:
    """Validate only the identity facts that make custom output usable."""

    try:
        payload = json.loads(evidence_path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        raise CharacterizationError("repair-managed-characterization-evidence") from exc
    assembly = payload.get("managedAssembly")
    messages = payload.get("messages")
    if not isinstance(assembly, dict) or assembly.get("name") != expected_assembly:
        raise CharacterizationError("repair-generated-managed-assembly-name")
    if payload.get("ros2MessageIdentity") != expected_ros2_message_identity:
        raise CharacterizationError("repair-ros2cs-message-identity")
    if not isinstance(messages, list):
        raise CharacterizationError("repair-managed-characterization-evidence")
    envelope = next(
        (item for item in messages if isinstance(item, dict) and item.get("fullName") == expected_envelope_type),
        None,
    )
    if not isinstance(envelope, dict):
        raise CharacterizationError("repair-generated-envelope-type")
    interfaces = envelope.get("interfaces")
    constructors = envelope.get("constructors")
    if not isinstance(interfaces, list) or expected_ros2_message_identity not in interfaces:
        raise CharacterizationError("repair-ros2cs-message-identity")
    if not isinstance(constructors, list) or ".ctor()" not in constructors:
        raise CharacterizationError("repair-generated-envelope-constructor")
    if envelope.get("disposable") is not True:
        raise CharacterizationError("repair-generated-envelope-dispose")
    return payload


def _capture_msvc_environment(base_environment: Mapping[str, str]) -> dict[str, str]:
    """Capture only the explicitly discovered Visual Studio x64 tool variables.

    ``VsDevCmd.bat`` is a Visual Studio tool activator found through ``vswhere``;
    it is not a user shell profile.  The captured result is whitelisted and
    folded into the caller-owned environment rather than inheriting arbitrary
    host environment state.
    """

    vswhere = Path(os.environ.get("ProgramFiles(x86)", r"C:\Program Files (x86)")) / "Microsoft Visual Studio" / "Installer" / "vswhere.exe"
    if not vswhere.is_file():
        raise CharacterizationError("install-msvc-build-tools")
    discovered = subprocess.run(
        (
            str(vswhere),
            "-latest",
            "-products",
            "*",
            "-requires",
            "Microsoft.VisualStudio.Component.VC.Tools.x86.x64",
            "-property",
            "installationPath",
        ),
        shell=False,
        capture_output=True,
        text=True,
        errors="replace",
        env=dict(base_environment),
        check=False,
    )
    root_text = discovered.stdout.strip().splitlines()
    if discovered.returncode != 0 or not root_text:
        raise CharacterizationError("install-msvc-build-tools")
    visual_studio = Path(root_text[0].strip())
    command = visual_studio / "Common7" / "Tools" / "VsDevCmd.bat"
    if not command.is_file() or '"' in str(command) or "%" in str(command):
        raise CharacterizationError("repair-vsdevcmd")
    # On Windows, passing this as a process command line (not ``shell=True``)
    # preserves the one quoted batch path. Passing a list would cause Python's
    # list2cmdline to escape the nested quotes before cmd.exe sees them.
    command_line = (
        f'"{os.environ.get("ComSpec", r"C:\Windows\System32\cmd.exe")}" '
        f'/d /s /c call "{command}" -arch=x64 -host_arch=x64 >nul && set'
    )
    result = subprocess.run(
        command_line,
        shell=False,
        capture_output=True,
        text=True,
        errors="replace",
        env=dict(base_environment),
        check=False,
    )
    if result.returncode != 0:
        raise CharacterizationError("repair-msvc-command-prompt")
    allowed_names = {
        "DevEnvDir",
        "Framework40Version",
        "FrameworkDir",
        "FrameworkDir32",
        "FrameworkDir64",
        "FrameworkVersion",
        "FrameworkVersion32",
        "FrameworkVersion64",
        "INCLUDE",
        "LIB",
        "LIBPATH",
        "NETFXSDKDir",
        "UCRTVersion",
        "VCIDEInstallDir",
        "VCINSTALLDIR",
        "VCToolsInstallDir",
        "VCToolsRedistDir",
        "VCToolsVersion",
        "VisualStudioVersion",
        "VSINSTALLDIR",
        "WindowsLibPath",
        "WindowsSdkBinPath",
        "WindowsSdkDir",
        "WindowsSDKLibVersion",
        "WindowsSDKVersion",
    }
    captured: dict[str, str] = {}
    visual_path: str | None = None
    for line in result.stdout.splitlines():
        if "=" not in line:
            continue
        name, value = line.split("=", 1)
        if name in allowed_names:
            captured[name] = value
        elif name.lower() == "path":
            visual_path = value
    if not captured.get("INCLUDE") or not captured.get("LIB") or not visual_path:
        raise CharacterizationError("repair-msvc-command-prompt")
    allowed_path_entries = [
        entry
        for entry in visual_path.split(os.pathsep)
        if "microsoft visual studio" in entry.lower() or "windows kits" in entry.lower()
    ]
    if not allowed_path_entries:
        raise CharacterizationError("repair-msvc-command-prompt")
    captured["PATH"] = os.pathsep.join(allowed_path_entries)
    return captured


def build_characterization_environment(request: CharacterizationRequest) -> dict[str, str]:
    """Construct a ROS/ros2cs/MSVC environment from declared roots only."""

    base = build_toolchain_environment(
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
    if not (request.ros2cs_install / "share" / "rosidl_generator_cs").is_dir():
        raise CharacterizationError("provide-matching-ros2cs-install")
    environment = dict(base)
    toolchain_path = environment["PATH"]
    environment.update(_capture_msvc_environment(base))
    pixi = request.ros2_root / ".pixi" / "envs" / "default"
    system_root = Path(base.get("SystemRoot", r"C:\Windows"))
    path_entries = (
        environment["PATH"],
        toolchain_path,
        request.ros2cs_install / "bin",
        request.ros2cs_install / "lib",
        request.ros2_root / "bin",
        request.ros2_root / "Scripts",
        pixi / "Scripts",
        pixi / "Library" / "bin",
        system_root / "System32",
        system_root / "System32" / "WindowsPowerShell" / "v1.0",
    )
    environment["PATH"] = os.pathsep.join(str(entry) for entry in path_entries)
    # ros2cs is a complete overlay after its explicit install preflight: each
    # dependency exports native ROSIDL headers/targets *and* its managed
    # assembly.  Layer it before the base ROS2 root so custom generated C# and
    # native code resolve the same package revision.
    prefix_entries = (request.ros2cs_install, request.ros2_root)
    environment["AMENT_PREFIX_PATH"] = os.pathsep.join(str(entry) for entry in prefix_entries)
    environment["CMAKE_PREFIX_PATH"] = os.pathsep.join(str(entry) for entry in prefix_entries)
    environment["COLCON_PREFIX_PATH"] = os.pathsep.join(str(entry) for entry in prefix_entries)
    environment["PYTHONPATH"] = os.pathsep.join(
        str(entry)
        for entry in (request.ros2cs_install / "Lib" / "site-packages", request.ros2_root / "Lib" / "site-packages")
    )
    environment["COLCON_PYTHON_EXECUTABLE"] = str(pixi / "python.exe")
    environment["CMAKE_GENERATOR"] = request.generator
    environment["RMW_IMPLEMENTATION"] = "rmw_fastrtps_cpp"
    # rosidl templates include UTF-8 source text.  Windows' active code page
    # must not determine how the pinned Python opens those templates.
    environment["PYTHONUTF8"] = "1"
    return environment

__all__ = [name for name in globals() if not name.startswith("__")]
