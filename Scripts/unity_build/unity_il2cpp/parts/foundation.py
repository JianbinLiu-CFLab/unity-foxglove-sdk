#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0
#
# Purpose: Cross-platform IL2CPP standalone Player build script.
# Usage: python Scripts/unity_build/unity_il2cpp.py --target win64
# Inputs: --target (win64|linux64|macos), --unity (path, optional)
# Outputs: Defaults to build/Unity/<target>-il2cpp-<timestamp>/; overridable via --build-dir and --output.

"""Build the Unity Foxglove demo project for IL2CPP standalone.

The script resolves project and output paths relative to its own location.
No hard-coded absolute paths - safe to use across clones.

Examples:
  python Scripts/unity_build/unity_il2cpp.py
  python Scripts/unity_build/unity_il2cpp.py --target linux64
  python Scripts/unity_build/unity_il2cpp.py --target macos --unity /path/to/Unity
"""

from __future__ import annotations

import argparse
import ctypes
import os
import platform
import re
import signal
import stat
import subprocess
import sys
import time
import uuid
from datetime import datetime, timezone
from pathlib import Path
from typing import Dict, List, Optional, Tuple


# Build targets supported by the Unity-side FoxgloveBuild method.
TARGETS = ("win64", "linux64", "macos")

# Number of parent directories between this script and the repository root.
REPO_ROOT_PARENT_DEPTH = 4

# Process exit codes returned by this build CLI.
EXIT_SUCCESS = 0
EXIT_PREFLIGHT_FAILURE = 1
EXIT_USAGE_ERROR = 2
EXIT_TIMEOUT = 124

# Time constants used for elapsed-time formatting and log polling.
SECONDS_PER_HOUR = 3_600
SECONDS_PER_MINUTE = 60
LOG_POLL_SLEEP_SECONDS = 1

# Keep progress heartbeats useful while avoiding console spam.
DEFAULT_PROGRESS_INTERVAL_SECONDS = 15
MIN_PROGRESS_INTERVAL_SECONDS = 1
# Prevent arbitrary-precision CLI values from overflowing monotonic-float math.
MAX_PROGRESS_INTERVAL_SECONDS = 24 * 60 * 60
DEFAULT_BUILD_TIMEOUT_MINUTES = 120
UNITY_TERMINATION_WAIT_SECONDS = 30
PROCESS_DIAGNOSTIC_TIMEOUT_SECONDS = 5
PROCESS_TREE_POLL_SECONDS = 0.05

# Win32 process/job constants kept local so the script remains dependency-free.
WINDOWS_CREATE_SUSPENDED = 0x00000004
WINDOWS_JOB_OBJECT_BASIC_PROCESS_ID_LIST = 3
WINDOWS_JOB_OBJECT_EXTENDED_LIMIT_INFORMATION = 9
WINDOWS_JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000
WINDOWS_JOB_QUERY_BUFFER_BYTES = 1024 * 1024
WINDOWS_PROCESS_SET_QUOTA = 0x0100
WINDOWS_PROCESS_TERMINATE = 0x0001
WINDOWS_THREAD_SUSPEND_RESUME = 0x0002
WINDOWS_TH32CS_SNAPTHREAD = 0x00000004
WINDOWS_DWORD_FAILURE = 0xFFFFFFFF
WINDOWS_JOB_TERMINATE_EXIT_CODE = 1
WINDOWS_EXECUTABLE_MAGIC = b"MZ"

# Split only the ProjectVersion key/value separator.
PROJECT_VERSION_SPLIT_MAX = 1
PROJECT_VERSION_VALUE_INDEX = 1
PROJECT_VERSION_MAX_BYTES = 64 * 1024

# A Unity editor version component, e.g. 2022.3.10f1 or 6000.3.14. Project metadata is
# untrusted input joined into a filesystem path, so the value must match this whole and
# cannot carry a separator, a drive letter, or a parent segment.
UNITY_EDITOR_VERSION_PATTERN = re.compile(r"\d+\.\d+\.\d+(?:[abfp]\d+)?")

# Initial offsets and command indexes used for log tailing and diagnostics.
INITIAL_LOG_OFFSET = 0
MAX_LOG_READ_BYTES = 256 * 1024
MAX_FINAL_LOG_CHUNKS = 16
UNITY_EXECUTABLE_COMMAND_INDEX = 0

# Generated artifacts required before Unity can compile the package in IL2CPP.
REQUIRED_GENERATED_ARTIFACTS = (
    "Packages/dev.unity2foxglove.sdk/Editor/SourceGenerators/analyzers/dotnet/cs/FoxgloveLogSourceGenerator.dll",
    "Packages/dev.unity2foxglove.ros2bridge/Runtime/Schemas/Ros2Msg/FoxgloveRos2MsgSchemaCatalog.cs",
    "Packages/dev.unity2foxglove.ros2bridge/Runtime/Schemas/Ros2Msg/Generated.meta",
    "Packages/dev.unity2foxglove.ros2bridge/Runtime/Schemas/Ros2Msg/Generated/Ros2CdrGeneratedSerializers.g.cs",
    "Packages/dev.unity2foxglove.ros2bridge/Runtime/Schemas/Ros2Msg/Generated/Ros2CdrGeneratedSerializers.g.cs.meta",
    "Packages/dev.unity2foxglove.ros2bridge/Runtime/Schemas/Ros2Msg/Generated/Ros2CdrGeneratedDeserializers.g.cs",
    "Packages/dev.unity2foxglove.ros2bridge/Runtime/Schemas/Ros2Msg/Generated/Ros2CdrGeneratedDeserializers.g.cs.meta",
    "Packages/dev.unity2foxglove.ros2bridge/Runtime/Schemas/Ros2Msg/Generated/Ros2CdrSerializerRegistry.g.cs",
    "Packages/dev.unity2foxglove.ros2bridge/Runtime/Schemas/Ros2Msg/Generated/Ros2CdrSerializerRegistry.g.cs.meta",
    "Packages/dev.unity2foxglove.ros2bridge/Runtime/Schemas/Ros2Msg/Generated/Ros2CdrDeserializerRegistry.g.cs",
    "Packages/dev.unity2foxglove.ros2bridge/Runtime/Schemas/Ros2Msg/Generated/Ros2CdrDeserializerRegistry.g.cs.meta",
    "Packages/dev.unity2foxglove.ros2bridge/Runtime/Schemas/Ros2Msg/Generated/Ros2CdrSampleFactory.g.cs",
    "Packages/dev.unity2foxglove.ros2bridge/Runtime/Schemas/Ros2Msg/Generated/Ros2CdrSampleFactory.g.cs.meta",
)

# Log markers that indicate important Unity/Bee/IL2CPP build progress or failures.
IMPORTANT_LOG_MARKERS = (
    "[Foxrun",
    "[FoxgloveBuild]",
    "Build Finished",
    "Build succeeded",
    "Build failed",
    "Scripts have compiler errors",
    "Script Compilation",
    "Tundra build failed",
    "error CS",
    "Exception",
    "NullReference",
    "IL2CPP",
    "Csc ",
    "Bee",
)


def repo_root() -> Path:
    """Repository root resolved from the configured parent-depth constant."""
    return Path(__file__).resolve().parents[4]


def default_target() -> str:
    """Detect the current host platform as the default build target."""
    system = platform.system().lower()
    if system == "windows":
        return "win64"
    if system == "darwin":
        return "macos"
    if system == "linux":
        return "linux64"
    return "win64"


def unity_version_key(path: Path) -> Tuple[int, ...]:
    """Extract a comparable Unity version tuple from a Hub editor path."""
    for part in reversed(path.parts):
        match = re.fullmatch(r"(\d+)\.(\d+)\.(\d+)(?:[abfp](\d+))?", part)
        if match:
            return tuple(int(number) for number in match.groups(default="0"))
    return ()


def accepted_unity_candidate(path: Path) -> Optional[Path]:
    """Resolve a discovery candidate, accepting only a regular executable file.

    Existence proves namespace occupancy, not type or host executability, so a
    directory, a reparse point, or an ordinary text file can otherwise win a
    discovery tier. Resolving here also pins the identity that discovery checks
    to the identity that is launched, because Unity is started with the working
    directory rebased to the repository root.
    """
    try:
        resolved = path.expanduser().resolve(strict=True)
    except OSError:
        return None
    if not resolved.is_file():
        return None
    if os.name == "nt":
        try:
            with resolved.open("rb") as handle:
                if handle.read(len(WINDOWS_EXECUTABLE_MAGIC)) != WINDOWS_EXECUTABLE_MAGIC:
                    return None
        except OSError:
            return None
    elif not os.access(resolved, os.X_OK):
        return None
    return resolved


def is_unity_editor_version(value: str) -> bool:
    """Accept only a Unity editor version component, never a path fragment."""
    return bool(UNITY_EDITOR_VERSION_PATTERN.fullmatch(value))


def contained_unity_candidate(candidate: Path, version_root: Path) -> Optional[Path]:
    """Accept a candidate only when it resolves beneath the given Hub version root.

    Containment is tested after resolution, so a symlink or reparse point inside the
    version root cannot redirect the launch outside it. This is independent of the
    version-component check: either alone would leave the other escape open.
    """
    accepted = accepted_unity_candidate(candidate)
    if accepted is None:
        return None
    try:
        resolved_root = version_root.resolve(strict=True)
    except OSError:
        return None
    if not accepted.is_relative_to(resolved_root):
        return None
    return accepted


def newest_existing(paths: List[Path]) -> Optional[Path]:
    """Return the newest Unity version among the accepted executable candidates."""
    existing = []
    for candidate in paths:
        accepted = accepted_unity_candidate(candidate)
        if accepted is None:
            continue
        # Hub candidates are nested as <version>/Editor/Unity; reject any
        # trailing-junk or unknown-channel directory before ranking it.
        version_component = accepted.parent.parent.name
        if not is_unity_editor_version(version_component):
            continue
        existing.append(accepted)
    if not existing:
        return None
    return max(existing, key=lambda p: (unity_version_key(p), p.stat().st_mtime))


def find_unity_explicit(path: Optional[str]) -> Optional[Path]:
    """Resolve the Unity executable from an explicit --unity argument."""
    if not path:
        return None
    unity = Path(path).expanduser()
    accepted = accepted_unity_candidate(unity)
    if accepted:
        return accepted
    if not unity.exists():
        raise FileNotFoundError(f"--unity path does not exist: {unity}")
    raise FileNotFoundError(f"--unity path is not a regular executable file: {unity}")


def find_unity_from_env() -> Optional[Path]:
    """Try UNITY_EXE or UNITY_PATH environment variables."""
    for name in ("UNITY_EXE", "UNITY_PATH"):
        value = os.environ.get(name)
        if value:
            unity = Path(value).expanduser()
            accepted = accepted_unity_candidate(unity)
            if accepted:
                return accepted
            if not unity.exists():
                raise FileNotFoundError(f"{name} points to a missing file: {unity}")
            raise FileNotFoundError(f"{name} does not point to a regular executable file: {unity}")
    return None


def find_unity_from_project_version(project_path: Path) -> Optional[Path]:
    """Resolve Unity from ProjectSettings/ProjectVersion.txt when available."""
    version_file = project_path / "ProjectSettings" / "ProjectVersion.txt"
    if not version_file.exists() or not version_file.is_file():
        return None

    editor_version = None
    try:
        with version_file.open("rb") as handle:
            contents = handle.read(PROJECT_VERSION_MAX_BYTES)
    except OSError:
        return None
    for line in contents.decode("utf-8", errors="replace").splitlines():
        if line.startswith("m_EditorVersion:"):
            editor_version = line.split(":", PROJECT_VERSION_SPLIT_MAX)[PROJECT_VERSION_VALUE_INDEX].strip()
            break
    if not editor_version:
        return None

    if not is_unity_editor_version(editor_version):
        print(
            f"[build_unity_il2cpp] ProjectVersion.txt pins {editor_version!r}, which is not a "
            "Unity editor version; ignoring the project pin.",
            file=sys.stderr,
        )
        return None

    system = platform.system().lower()
    if system == "windows":
        roots = [
            Path(os.environ.get("PROGRAMFILES", r"C:\Program Files")),
            Path(os.environ.get("PROGRAMFILES(X86)", r"C:\Program Files (x86)")),
        ]
        for root in roots:
            version_root = root / "Unity" / "Hub" / "Editor" / editor_version
            accepted = contained_unity_candidate(version_root / "Editor" / "Unity.exe", version_root)
            if accepted:
                return accepted
    elif system == "darwin":
        version_root = Path("/Applications/Unity/Hub/Editor") / editor_version
        accepted = contained_unity_candidate(
            version_root / "Unity.app" / "Contents" / "MacOS" / "Unity", version_root)
        if accepted:
            return accepted
    elif system == "linux":
        for root in (Path.home() / "Unity" / "Hub" / "Editor", Path("/opt/Unity/Hub/Editor")):
            version_root = root / editor_version
            accepted = contained_unity_candidate(version_root / "Editor" / "Unity", version_root)
            if accepted:
                return accepted

    print(
        f"[build_unity_il2cpp] Project-pinned Unity {editor_version} was not found; "
        "falling back to generic Unity Hub discovery.",
        file=sys.stderr,
    )
    return None


def find_unity_from_hub() -> Optional[Path]:
    """Search Unity Hub installations on the current platform."""
    system = platform.system().lower()
    candidates: List[Path] = []

    if system == "windows":
        program_files = [
            Path(os.environ.get("PROGRAMFILES", r"C:\Program Files")),
            Path(os.environ.get("PROGRAMFILES(X86)", r"C:\Program Files (x86)")),
        ]
        for root in program_files:
            candidates.extend(root.glob(r"Unity/Hub/Editor/*/Editor/Unity.exe"))

    elif system == "darwin":
        candidates.extend(Path("/Applications/Unity/Hub/Editor").glob("*/Unity.app/Contents/MacOS/Unity"))
        candidates.append(Path("/Applications/Unity/Unity.app/Contents/MacOS/Unity"))

    elif system == "linux":
        home = Path.home()
        candidates.extend((home / "Unity/Hub/Editor").glob("*/Editor/Unity"))
        candidates.extend(Path("/opt/Unity/Hub/Editor").glob("*/Editor/Unity"))
        candidates.append(Path("/opt/Unity/Editor/Unity"))

    return newest_existing(candidates)


def find_unity(path: Optional[str], project_path: Path) -> Path:
    """Resolve Unity executable, preferring the project-pinned editor before generic Hub installs."""
    unity = (
        find_unity_explicit(path)
        or find_unity_from_env()
        or find_unity_from_project_version(project_path)
        or find_unity_from_hub()
    )
    if unity:
        return unity

    raise FileNotFoundError(
        "Unity executable was not found. Pass --unity or set UNITY_EXE/UNITY_PATH."
    )


def relative_to_root(path: Path, root: Path) -> str:
    """Format a path relative to repo root for readable log output."""
    try:
        return str(path.resolve().relative_to(root.resolve()))
    except ValueError:
        return str(path)


def require_workspace_path(path: Path, root: Path, label: str) -> Path:
    """Require a resolved CLI path to remain inside the workspace root."""
    resolved = path.resolve()
    try:
        resolved.relative_to(root.resolve())
    except ValueError as exc:
        raise ValueError(f"{label} must remain inside the workspace root: {resolved}") from exc
    return resolved


def validate_generated_artifacts(root: Path) -> List[str]:
    """Return missing, non-regular, or empty generated artifacts needed for Unity compilation."""
    failures: List[str] = []
    for relative in REQUIRED_GENERATED_ARTIFACTS:
        path = root / relative
        if not path.exists():
            failures.append(f"missing generated artifact: {relative}")
        elif not path.is_file():
            failures.append(f"generated artifact is not a regular file: {relative}")
        elif path.stat().st_size == 0:
            failures.append(f"empty generated artifact: {relative}")
    return failures


def resolve_unity_for_command(args: argparse.Namespace, project_path: Path) -> str:
    """Resolve Unity, allowing an explicit dry-run placeholder for CI path checks."""
    try:
        return str(find_unity(args.unity, project_path))
    except FileNotFoundError as exc:
        if args.dry_run and args.allow_missing_unity:
            return f"<Unity not found: {exc}>"
        raise


def output_fingerprint(path: Path) -> Optional[Tuple[int, int]]:
    """Identify one build output by size and modification time, or None if absent.

    Comparing a fingerprint taken before the build with one taken after is what
    distinguishes a Player this invocation produced from a stale artifact that
    merely occupied the requested path. Size and mtime are used together so the
    verdict does not depend on filesystem timestamp resolution alone.
    """
    try:
        if not path.is_file():
            return None
        info = path.stat()
    except OSError:
        return None
    return (info.st_size, info.st_mtime_ns)


def build_command(args: argparse.Namespace) -> Tuple[List[str], Path, Path, Path]:
    """Build the full Unity batchmode command line from parsed arguments."""
    root = repo_root()
    project_path = require_workspace_path(root / args.project, root, "project")
    build_dir = require_workspace_path(root / args.build_dir, root, "build directory") if args.build_dir else default_build_dir(root, args.target)
    log_path = require_workspace_path(root / args.log, root, "log path") if args.log else build_dir / "build.log"
    output_path = require_workspace_path(root / args.output, root, "output path") if args.output else default_output_path(build_dir, args.target)
    unity = resolve_unity_for_command(args, project_path)

    if not project_path.exists():
        raise FileNotFoundError(f"Unity project was not found: {project_path}")

    cmd = [
        unity,
        "-batchmode",
        "-quit",
        "-projectPath",
        str(project_path),
        "-executeMethod",
        "FoxgloveBuild.BuildIl2CppFromCommandLine",
        "-foxgloveBuildTarget",
        args.target,
        "-foxgloveOutputPath",
        str(output_path),
        "-logFile",
        str(log_path),
    ]

    return cmd, project_path, log_path, output_path


def default_build_dir(root: Path, target: str) -> Path:
    """Default build output directory with platform and timestamp."""
    stamp = datetime.now(timezone.utc).strftime("%Y%m%d-%H%M%SZ")
    reservation = uuid.uuid4().hex[:8]
    return root / "build" / "Unity" / f"{target}-il2cpp-{stamp}-{reservation}"


def default_output_path(build_dir: Path, target: str) -> Path:
    """Default Player executable path for the given target."""
    if target == "win64":
        return build_dir / "WindowsIL2CPP" / "FoxgloveDemo.exe"
    if target == "linux64":
        return build_dir / "LinuxIL2CPP" / "FoxgloveDemo.x86_64"
    if target == "macos":
        return build_dir / "MacOSIL2CPP" / "FoxgloveDemo.app"
    raise ValueError(f"Unknown target: {target}")


def format_elapsed(seconds: float) -> str:
    """Format elapsed seconds as mm:ss or hh:mm:ss."""
    total = int(seconds)
    hours, remainder = divmod(total, SECONDS_PER_HOUR)
    minutes, seconds = divmod(remainder, SECONDS_PER_MINUTE)
    if hours:
        return f"{hours:02d}:{minutes:02d}:{seconds:02d}"
    return f"{minutes:02d}:{seconds:02d}"


def is_important_log_line(line: str) -> bool:
    """Check if a log line matches a known important marker."""
    stripped = line.strip()
    if not stripped:
        return False
    return any(marker in stripped for marker in IMPORTANT_LOG_MARKERS)
__all__ = ['argparse', 'ctypes', 'os', 'platform', 're', 'signal', 'stat', 'subprocess', 'sys', 'time', 'uuid', 'datetime', 'timezone', 'Path', 'Dict', 'List', 'Optional', 'Tuple', 'TARGETS', 'REPO_ROOT_PARENT_DEPTH', 'EXIT_SUCCESS', 'EXIT_PREFLIGHT_FAILURE', 'EXIT_USAGE_ERROR', 'EXIT_TIMEOUT', 'SECONDS_PER_HOUR', 'SECONDS_PER_MINUTE', 'LOG_POLL_SLEEP_SECONDS', 'DEFAULT_PROGRESS_INTERVAL_SECONDS', 'MIN_PROGRESS_INTERVAL_SECONDS', 'MAX_PROGRESS_INTERVAL_SECONDS', 'DEFAULT_BUILD_TIMEOUT_MINUTES', 'UNITY_TERMINATION_WAIT_SECONDS', 'PROCESS_DIAGNOSTIC_TIMEOUT_SECONDS', 'PROCESS_TREE_POLL_SECONDS', 'WINDOWS_CREATE_SUSPENDED', 'WINDOWS_JOB_OBJECT_BASIC_PROCESS_ID_LIST', 'WINDOWS_JOB_OBJECT_EXTENDED_LIMIT_INFORMATION', 'WINDOWS_JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE', 'WINDOWS_JOB_QUERY_BUFFER_BYTES', 'WINDOWS_PROCESS_SET_QUOTA', 'WINDOWS_PROCESS_TERMINATE', 'WINDOWS_THREAD_SUSPEND_RESUME', 'WINDOWS_TH32CS_SNAPTHREAD', 'WINDOWS_DWORD_FAILURE', 'WINDOWS_JOB_TERMINATE_EXIT_CODE', 'WINDOWS_EXECUTABLE_MAGIC', 'PROJECT_VERSION_SPLIT_MAX', 'PROJECT_VERSION_VALUE_INDEX', 'PROJECT_VERSION_MAX_BYTES', 'UNITY_EDITOR_VERSION_PATTERN', 'INITIAL_LOG_OFFSET', 'MAX_LOG_READ_BYTES', 'MAX_FINAL_LOG_CHUNKS', 'UNITY_EXECUTABLE_COMMAND_INDEX', 'REQUIRED_GENERATED_ARTIFACTS', 'IMPORTANT_LOG_MARKERS', 'repo_root', 'default_target', 'unity_version_key', 'accepted_unity_candidate', 'is_unity_editor_version', 'contained_unity_candidate', 'newest_existing', 'find_unity_explicit', 'find_unity_from_env', 'find_unity_from_project_version', 'find_unity_from_hub', 'find_unity', 'relative_to_root', 'require_workspace_path', 'validate_generated_artifacts', 'resolve_unity_for_command', 'output_fingerprint', 'build_command', 'default_build_dir', 'default_output_path', 'format_elapsed', 'is_important_log_line']
