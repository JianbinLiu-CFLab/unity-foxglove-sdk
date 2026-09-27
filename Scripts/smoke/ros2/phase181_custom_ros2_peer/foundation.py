#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Module: Scripts/smoke
# Purpose: Typed peer harness for one locked Phase181 custom ROS2 envelope.

"""Build and run a peer for the locked Phase181 custom ROS2 interface.

The outer command owns only a disposable workspace below ``build/phase181``.
It never sources a shell profile, invokes a bare ``ros2`` command, or treats
one-sided graph/Unity evidence as a passing interop result.  The ``--worker``
entry point is intentionally separate so the peer uses the pinned Python from
the selected ROS2 distribution after the exact interface source is built.
"""
from __future__ import annotations
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase181_custom_ros2_peer.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
import argparse
import contextlib
import hashlib
import importlib
import json
import os
import pathlib
import re
import shutil
import subprocess
import sys
import threading
import time
import uuid
from dataclasses import dataclass
from typing import Iterator, Mapping, Sequence
import _ros2_windows_env as ros2env
import phase181_custom_ros2_peer_protocol as protocol
STATIC_INTERFACE_PACKAGE_ID = "dev.unity2foxglove.foxrun.ros2.interfaces"
ROS_PACKAGE_NAME = "unity2foxglove_foxrun_interfaces_v1"
LOCK_RELATIVE_PATH = pathlib.PurePosixPath("RuntimeSupport/foxrun-ros2-interface-lock.json")
DEFAULT_TOPIC_PREFIX = "/foxrun/phase181/custom"
DEFAULT_TOPICS = {
    "publish": DEFAULT_TOPIC_PREFIX + "/publish",
    "subscribe": DEFAULT_TOPIC_PREFIX + "/subscribe",
    "bidirectional": DEFAULT_TOPIC_PREFIX + "/bidirectional",
}
PROBE_ROLES = ("correlate", "publisher", "subscriber", "bidirectional", "orchestrate")
_SHA256 = re.compile(r"^[0-9a-f]{64}$")
_PROFILE_ID = re.compile(r"^[a-z0-9][a-z0-9-]{0,63}$")
OWNERSHIP_MARKER_NAME = ".phase181-peer-owned"
_OWNERSHIP_MARKER_CONTENT = "phase181-peer-workspace-v1\n"
PEER_BUILD_CACHE_NAME = ".phase181-peer-build.json"
_PEER_BUILD_CACHE_FORMAT = "phase181-peer-build-v1"
_PEER_BUILD_STALL_SECONDS = 1800.0
_WORKER_STARTUP_TIMEOUT_SECONDS = 60.0
_RUNTIME_SELECTION_STALL_SECONDS = 900.0
_RUNTIME_SELECTION_MAX_SECONDS = 3600.0
_RUNTIME_SELECTION_MAX_LOG_BYTES = 8 * 1024 * 1024
_TOOLCHAIN_CAPTURE_MAX_BYTES = 64 * 1024
_RUNTIME_SELECTION_PROGRESS_INTERVAL_SECONDS = 30.0
_RUNTIME_SELECTION_READY_MARKER = "PHASE181_BATCH_RUNTIME_SELECTION_READY"
_RUNTIME_SELECTION_EXECUTE_METHOD = (
    "Unity2Foxglove.Ros2ForUnity.Editor.Phase181Ros2RuntimeBatchSelection.SelectFromCommandLine"
)
_COMMUNICATION_MODE_BY_RMW = {
    "rmw_fastrtps_cpp": "fastdds",
    "rmw_zenoh_cpp": "zenoh",
}
_LONGEST_WINDOWS_ROSIDL_OBJECT = (
    "95441c87d059a3e1deffafe69425029c/"
    "_unity2foxglove_foxrun_interfaces_v1_s.ep.rosidl_typesupport_introspection_c.c.obj"
)
class PeerFailure(protocol.ProtocolFailure):
    """Stable peer failure category that remains safe to persist in a summary."""
@dataclass(frozen=True)
class StaticInterfaceLock:
    """The immutable interface facts required by every peer and Unity surface."""

    ros_package_name: str
    interface_revision: int
    interface_digest: str
    payload_message_name: str
    envelope_message_name: str
@dataclass(frozen=True)
class WindowsPeerToolchain:
    """Pinned Windows ROS2 executables required to build one disposable peer."""

    ros2_root: pathlib.Path
    python_executable: pathlib.Path
    colcon_executable: pathlib.Path
def capture_windows_msvc_environment(base_environment: Mapping[str, str]) -> dict[str, str]:
    """Capture only the discovered Visual Studio x64 build variables for the owned peer build."""

    vswhere = pathlib.Path(os.environ.get("ProgramFiles(x86)", r"C:\Program Files (x86)")) / "Microsoft Visual Studio" / "Installer" / "vswhere.exe"
    if not vswhere.is_file():
        raise PeerFailure("FAIL_PEER_TOOLCHAIN", "The Windows peer build requires the Visual Studio C++ x64 toolset.")
    try:
        discovery = subprocess.run(
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
            timeout=60.0,
        )
    except subprocess.TimeoutExpired as exc:
        raise PeerFailure("FAIL_PEER_TOOLCHAIN", "Visual Studio discovery exceeded its bounded timeout.") from exc
    if len(discovery.stdout or "") + len(discovery.stderr or "") > _TOOLCHAIN_CAPTURE_MAX_BYTES:
        raise PeerFailure("FAIL_PEER_TOOLCHAIN", "Visual Studio discovery exceeded its bounded output capacity.")
    roots = discovery.stdout.strip().splitlines()
    if discovery.returncode != 0 or not roots:
        raise PeerFailure("FAIL_PEER_TOOLCHAIN", "The Windows peer build requires the Visual Studio C++ x64 toolset.")
    command = pathlib.Path(roots[0].strip()) / "Common7" / "Tools" / "VsDevCmd.bat"
    if not command.is_file() or '"' in str(command) or "%" in str(command):
        raise PeerFailure("FAIL_PEER_TOOLCHAIN", "The discovered Visual Studio x64 tool activator is not usable.")
    # ROS 2 Humble's pinned Python is 3.10, whose f-string parser rejects a
    # backslash anywhere inside the expression. Compute the fallback first so
    # the command stays parseable by the same Python which will run the worker.
    comspec = os.environ.get("ComSpec", r"C:\Windows\System32\cmd.exe")
    command_line = (
        f'"{comspec}" '
        f'/d /s /c call "{command}" -arch=x64 -host_arch=x64 >nul && set'
    )
    try:
        result = subprocess.run(
            command_line,
            shell=False,
            capture_output=True,
            text=True,
            errors="replace",
            env=dict(base_environment),
            check=False,
            timeout=60.0,
        )
    except subprocess.TimeoutExpired as exc:
        raise PeerFailure("FAIL_PEER_TOOLCHAIN", "The Visual Studio x64 tool activator exceeded its bounded timeout.") from exc
    if len(result.stdout or "") + len(result.stderr or "") > _TOOLCHAIN_CAPTURE_MAX_BYTES:
        raise PeerFailure("FAIL_PEER_TOOLCHAIN", "The Visual Studio x64 tool activator exceeded its bounded output capacity.")
    if result.returncode != 0:
        raise PeerFailure("FAIL_PEER_TOOLCHAIN", "The Visual Studio x64 tool activator could not prepare a build environment.")
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
        raise PeerFailure("FAIL_PEER_TOOLCHAIN", "The Visual Studio x64 tool activator returned an incomplete build environment.")
    visual_entries = [
        entry
        for entry in visual_path.split(os.pathsep)
        if "microsoft visual studio" in entry.lower() or "windows kits" in entry.lower()
    ]
    if not visual_entries:
        raise PeerFailure("FAIL_PEER_TOOLCHAIN", "The Visual Studio x64 tool activator returned no approved build paths.")
    captured["PATH"] = os.pathsep.join(visual_entries)
    return captured
def merge_windows_peer_build_environment(
    ros_environment: Mapping[str, str],
    msvc_environment: Mapping[str, str],
) -> dict[str, str]:
    """Keep explicit ROS tool paths while adding a captured Visual Studio x64 environment."""

    environment = ros2env.sanitized_subprocess_env(dict(ros_environment))
    ros_path = environment.get("PATH", "")
    visual_path = str(msvc_environment.get("PATH", "")).strip()
    if not ros_path or not visual_path:
        raise PeerFailure("FAIL_PEER_TOOLCHAIN", "The Windows peer build environment is incomplete.")
    environment.update(dict(msvc_environment))
    environment["PATH"] = os.pathsep.join((visual_path, ros_path))
    # rosidl templates are UTF-8. Do not let the host active code page choose
    # the decoder used by the selected ROS2 Python.
    environment["PYTHONUTF8"] = "1"
    return environment
def workspace_root() -> pathlib.Path:
    """Find the repository root without expanding local ROS junctions."""

    for candidate in (pathlib.Path(__file__).resolve().parent, *pathlib.Path(__file__).resolve().parents):
        if (candidate / "Packages").is_dir() and (candidate / "Scripts").is_dir():
            return candidate
    return pathlib.Path.cwd()
def default_static_interface_package(root: pathlib.Path | None = None) -> pathlib.Path:
    """Return the tracked static source package, never a generated runtime add-on."""

    return (root or workspace_root()) / "Packages" / STATIC_INTERFACE_PACKAGE_ID
def resolve_windows_peer_toolchain(ros2_root: pathlib.Path) -> WindowsPeerToolchain:
    """Resolve only the selected repository-local ROS2 Python and colcon executable."""

    root = pathlib.Path(ros2_root)
    try:
        python_executable, _ = ros2env.validate_ros2_root(root)
    except FileNotFoundError as exc:
        raise PeerFailure("FAIL_PEER_TOOLCHAIN", "The selected repository-local Windows ROS2 root is unavailable.") from exc
    colcon_executable = root / ".pixi" / "envs" / "default" / "Scripts" / "colcon.exe"
    if not colcon_executable.is_file():
        raise PeerFailure("FAIL_PEER_TOOLCHAIN", "The selected ROS2 environment does not contain the required colcon executable.")
    return WindowsPeerToolchain(root, python_executable, colcon_executable)
def build_addon_validator_command(repository: pathlib.Path, distro: str, rmw: str) -> list[str]:
    """Build the bounded add-on preflight command for exactly one profile."""

    if distro not in {"humble", "jazzy", "lyrical"} or not rmw.startswith("rmw_"):
        raise PeerFailure("FAIL_TYPESUPPORT_PREFLIGHT", "The custom typesupport profile is not valid.")
    validator = pathlib.Path(repository) / "Scripts" / "ros2forunity" / "interfaces" / "validate_foxrun_custom_typesupport_addon.py"
    if not validator.is_file():
        raise PeerFailure("FAIL_TYPESUPPORT_PREFLIGHT", "The custom typesupport validator is unavailable.")
    return [sys.executable, str(validator), "--distro", distro, "--require-rmw", rmw]
def build_addon_license_repair_command(repository: pathlib.Path, distro: str) -> list[str]:
    """Build the narrowly scoped canonical-license repair before strict add-on validation."""

    if distro not in {"humble", "jazzy", "lyrical"}:
        raise PeerFailure("FAIL_TYPESUPPORT_PREFLIGHT", "The custom typesupport distribution is not valid.")
    builder = pathlib.Path(repository) / "Scripts" / "ros2forunity" / "interfaces" / "build_foxrun_custom_typesupport_addon.py"
    if not builder.is_file():
        raise PeerFailure("FAIL_TYPESUPPORT_PREFLIGHT", "The custom typesupport repair helper is unavailable.")
    return [sys.executable, str(builder), "--distro", distro, "--repair-tracked-license-eol"]
def require_selected_typesupport_addon(repository: pathlib.Path, distro: str) -> str:
    """Require the Unity project to select exactly the matching runtime/add-on pair."""

    if distro not in {"humble", "jazzy", "lyrical"}:
        raise PeerFailure("FAIL_TYPESUPPORT_SELECTION", "The selected ROS2 distribution is not supported by the custom add-on.")
    manifest_path = pathlib.Path(repository) / "Unity2Foxglove" / "Packages" / "manifest.json"
    try:
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        raise PeerFailure("FAIL_TYPESUPPORT_SELECTION", "Unity's package manifest is unavailable or malformed.") from exc
    dependencies = manifest.get("dependencies") if isinstance(manifest, Mapping) else None
    if not isinstance(dependencies, Mapping):
        raise PeerFailure("FAIL_TYPESUPPORT_SELECTION", "Unity's package manifest has no dependency map.")
    runtime = "dev.unity2foxglove.ros2forunity.runtime." + distro + ".win64"
    expected = "dev.unity2foxglove.foxrun.ros2.interfaces.typesupport." + distro + ".win64"
    prefix = "dev.unity2foxglove.foxrun.ros2.interfaces.typesupport."
    active = sorted(name for name in dependencies if isinstance(name, str) and name.startswith(prefix))
    if runtime not in dependencies or active != [expected]:
        raise PeerFailure("FAIL_TYPESUPPORT_SELECTION", "Unity has not selected exactly one matching custom typesupport add-on.")
    return expected
def resolve_editor_batch_native_plugin_directories(
    repository: pathlib.Path,
    distro: str,
    selected_addon: str,
) -> tuple[pathlib.Path, pathlib.Path]:
    """Resolve only the selected runtime and custom add-on native plugin directories below this repository."""

    runtime_package = "dev.unity2foxglove.ros2forunity.runtime." + distro + ".win64"
    expected_addon = "dev.unity2foxglove.foxrun.ros2.interfaces.typesupport." + distro + ".win64"
    if selected_addon != expected_addon:
        raise PeerFailure("FAIL_TYPESUPPORT_SELECTION", "The selected custom typesupport add-on does not match the requested Editor Batch runtime.")
    suffix = pathlib.Path("Runtime") / "Ros2ForUnity" / "Plugins" / "Windows" / "x86_64"
    root = pathlib.Path(repository).resolve()
    runtime_plugins = root / "Packages" / runtime_package / suffix
    custom_plugins = root / "Packages" / selected_addon / suffix
    if not runtime_plugins.is_dir() or not custom_plugins.is_dir():
        raise PeerFailure("FAIL_EDITOR_BATCH", "The selected Unity native runtime or custom-interface plugin directory is unavailable.")
    return runtime_plugins, custom_plugins
def run_logged_owned_command(
    command: Sequence[str],
    *,
    cwd: pathlib.Path,
    env: Mapping[str, str],
    log_path: pathlib.Path,
    timeout_seconds: float,
    failure_code: str,
    runner=None,
    stream_output: bool = False,
    output_prefix: str = "",
    process_factory=None,
) -> None:
    """Run one helper-owned command with a completion cap or a streamed no-progress watchdog."""

    if not command or timeout_seconds <= 0.0 or not failure_code.startswith("FAIL_"):
        raise ValueError("Phase181 owned commands require a command, positive timeout, and stable failure code.")
    if stream_output and runner is not None:
        raise ValueError("A streamed Phase181 command cannot use the completed-process test runner.")
    execute = runner or subprocess.run
    try:
        pathlib.Path(log_path).parent.mkdir(parents=True, exist_ok=True)
        with pathlib.Path(log_path).open("w", encoding="utf-8", errors="replace") as log_stream:
            if not stream_output:
                result = execute(
                    list(command),
                    cwd=str(cwd),
                    env=dict(env),
                    stdout=log_stream,
                    stderr=subprocess.STDOUT,
                    text=True,
                    check=False,
                    timeout=timeout_seconds,
                    shell=False,
                )
                returncode = result.returncode
            else:
                create_process = process_factory or subprocess.Popen
                process = create_process(
                    list(command),
                    cwd=str(cwd),
                    env=dict(env),
                    stdout=subprocess.PIPE,
                    stderr=subprocess.STDOUT,
                    text=True,
                    errors="replace",
                    bufsize=1,
                    shell=False,
                    **worker_launch_options(),
                )
                if process.stdout is None:
                    raise PeerFailure(failure_code, "A helper-owned command did not expose readable progress output.")

                last_output_at = time.monotonic()

                def copy_output() -> None:
                    """Tee one owned command's line-buffered output without holding up its timeout."""

                    nonlocal last_output_at

                    for line in iter(process.stdout.readline, ""):
                        log_stream.write(line)
                        log_stream.flush()
                        last_output_at = time.monotonic()
                        print(output_prefix + line.rstrip("\r\n"), flush=True)

                reader = threading.Thread(target=copy_output, name="phase181-peer-command-output", daemon=True)
                reader.start()
                try:
                    while True:
                        try:
                            returncode = process.wait(timeout=min(1.0, timeout_seconds))
                            break
                        except subprocess.TimeoutExpired:
                            if log_stream.tell() > _RUNTIME_SELECTION_MAX_LOG_BYTES:
                                _terminate_owned_child(process)
                                raise PeerFailure(failure_code, "A helper-owned command exceeded its bounded log capacity.")
                            if not stream_output_is_stalled(last_output_at, time.monotonic(), timeout_seconds):
                                continue
                            try:
                                _terminate_owned_child(process)
                            except (OSError, subprocess.TimeoutExpired):
                                pass
                            raise
                finally:
                    reader.join(timeout=5.0)
    except subprocess.TimeoutExpired as exc:
        message = (
            "A helper-owned command stopped emitting progress before its watchdog window."
            if stream_output
            else "A helper-owned command did not finish before its bounded timeout."
        )
        raise PeerFailure(failure_code, message) from exc
    except OSError as exc:
        raise PeerFailure(failure_code, "A helper-owned command could not be started.") from exc
    if returncode != 0:
        raise PeerFailure(failure_code, "A helper-owned command returned a nonzero status.")
def stream_output_is_stalled(last_output_at: float, current_time: float, stall_seconds: float) -> bool:
    """Treat an active streamed build as healthy regardless of total duration; only a silent interval can fail it."""

    return current_time - last_output_at > stall_seconds
def worker_launch_options(platform_name: str | None = None) -> dict[str, object]:
    """Create a private process group for a helper-owned worker only."""

    if (platform_name or os.name) == "nt":
        return {"creationflags": int(getattr(subprocess, "CREATE_NEW_PROCESS_GROUP", 0))}
    return {"start_new_session": True}
def read_successful_worker_result(path: pathlib.Path, lock: StaticInterfaceLock) -> Mapping[str, object]:
    """Accept only one complete worker result for the exact locked interface digest."""

    try:
        parsed = json.loads(pathlib.Path(path).read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        raise PeerFailure("FAIL_PEER_RESULT", "The helper-owned typed worker did not produce a valid result summary.") from exc
    if not isinstance(parsed, Mapping):
        raise PeerFailure("FAIL_PEER_RESULT", "The helper-owned typed worker result was not an object.")
    verdict = parsed.get("verdict")
    if isinstance(verdict, str) and verdict.startswith("FAIL_"):
        raise PeerFailure(verdict, "The helper-owned typed worker did not complete every required proof.")
    try:
        protocol.require_interface_digest(lock.interface_digest, parsed.get("interfaceDigest", ""))
    except protocol.ProtocolFailure as exc:
        raise PeerFailure(exc.code, "The typed worker result did not match the locked interface digest.") from exc
    if verdict == "PASS":
        return parsed
    raise PeerFailure("FAIL_PEER_RESULT", "The helper-owned typed worker emitted an invalid verdict.")


__all__ = [name for name in globals() if not name.startswith("__")]
