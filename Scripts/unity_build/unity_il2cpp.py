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
import subprocess
import sys
import time
from datetime import datetime, timezone
from pathlib import Path
from typing import List, Optional, Tuple


# Build targets supported by the Unity-side FoxgloveBuild method.
TARGETS = ("win64", "linux64", "macos")

# Number of parent directories between this script and the repository root.
REPO_ROOT_PARENT_DEPTH = 2

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
DEFAULT_BUILD_TIMEOUT_MINUTES = 120
UNITY_TERMINATION_WAIT_SECONDS = 30
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

# Initial offsets and command indexes used for log tailing and diagnostics.
INITIAL_LOG_OFFSET = 0
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
    return Path(__file__).resolve().parents[REPO_ROOT_PARENT_DEPTH]


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
        match = re.match(r"^(\d+)\.(\d+)\.(\d+)(?:[a-z](\d+))?", part)
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


def newest_existing(paths: List[Path]) -> Optional[Path]:
    """Return the newest Unity version among the accepted executable candidates."""
    existing = [accepted for accepted in map(accepted_unity_candidate, paths) if accepted]
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
    if not version_file.exists():
        return None

    editor_version = None
    for line in version_file.read_text(encoding="utf-8", errors="replace").splitlines():
        if line.startswith("m_EditorVersion:"):
            editor_version = line.split(":", PROJECT_VERSION_SPLIT_MAX)[PROJECT_VERSION_VALUE_INDEX].strip()
            break
    if not editor_version:
        return None

    system = platform.system().lower()
    if system == "windows":
        roots = [
            Path(os.environ.get("PROGRAMFILES", r"C:\Program Files")),
            Path(os.environ.get("PROGRAMFILES(X86)", r"C:\Program Files (x86)")),
        ]
        for root in roots:
            unity = root / "Unity" / "Hub" / "Editor" / editor_version / "Editor" / "Unity.exe"
            accepted = accepted_unity_candidate(unity)
            if accepted:
                return accepted
    elif system == "darwin":
        unity = Path("/Applications/Unity/Hub/Editor") / editor_version / "Unity.app" / "Contents" / "MacOS" / "Unity"
        accepted = accepted_unity_candidate(unity)
        if accepted:
            return accepted
    elif system == "linux":
        for root in (Path.home() / "Unity" / "Hub" / "Editor", Path("/opt/Unity/Hub/Editor")):
            unity = root / editor_version / "Editor" / "Unity"
            accepted = accepted_unity_candidate(unity)
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


def build_command(args: argparse.Namespace) -> Tuple[List[str], Path, Path, Path]:
    """Build the full Unity batchmode command line from parsed arguments."""
    root = repo_root()
    project_path = (root / args.project).resolve()
    build_dir = (root / args.build_dir).resolve() if args.build_dir else default_build_dir(root, args.target)
    log_path = (root / args.log).resolve() if args.log else build_dir / "build.log"
    output_path = (root / args.output).resolve() if args.output else default_output_path(build_dir, args.target)
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
    return root / "build" / "Unity" / f"{target}-il2cpp-{stamp}"


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


def read_new_important_lines(log_path: Path, offset: int) -> Tuple[int, List[str]]:
    """Read new important log lines since the given byte offset."""
    if not log_path.exists():
        return offset, []

    try:
        with log_path.open("r", encoding="utf-8", errors="replace") as handle:
            handle.seek(offset)
            lines = handle.readlines()
            new_offset = handle.tell()
    except OSError:
        return offset, []

    important = [line.strip() for line in lines if is_important_log_line(line)]
    return new_offset, important


class _WindowsKillOnCloseJob:
    """Own one Windows process tree through a kill-on-close Job Object."""

    def __init__(self) -> None:
        """Create and configure an empty kill-on-close Job Object."""
        from ctypes import wintypes

        class IoCounters(ctypes.Structure):
            """Mirror the Windows IO_COUNTERS structure."""

            _fields_ = [
                ("read_operation_count", ctypes.c_ulonglong),
                ("write_operation_count", ctypes.c_ulonglong),
                ("other_operation_count", ctypes.c_ulonglong),
                ("read_transfer_count", ctypes.c_ulonglong),
                ("write_transfer_count", ctypes.c_ulonglong),
                ("other_transfer_count", ctypes.c_ulonglong),
            ]

        class BasicLimitInformation(ctypes.Structure):
            """Mirror the Windows JOBOBJECT_BASIC_LIMIT_INFORMATION structure."""

            _fields_ = [
                ("per_process_user_time_limit", ctypes.c_longlong),
                ("per_job_user_time_limit", ctypes.c_longlong),
                ("limit_flags", wintypes.DWORD),
                ("minimum_working_set_size", ctypes.c_size_t),
                ("maximum_working_set_size", ctypes.c_size_t),
                ("active_process_limit", wintypes.DWORD),
                ("affinity", ctypes.c_size_t),
                ("priority_class", wintypes.DWORD),
                ("scheduling_class", wintypes.DWORD),
            ]

        class ExtendedLimitInformation(ctypes.Structure):
            """Mirror the Windows JOBOBJECT_EXTENDED_LIMIT_INFORMATION structure."""

            _fields_ = [
                ("basic_limit_information", BasicLimitInformation),
                ("io_info", IoCounters),
                ("process_memory_limit", ctypes.c_size_t),
                ("job_memory_limit", ctypes.c_size_t),
                ("peak_process_memory_used", ctypes.c_size_t),
                ("peak_job_memory_used", ctypes.c_size_t),
            ]

        self._kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
        self._kernel32.CreateJobObjectW.argtypes = [ctypes.c_void_p, wintypes.LPCWSTR]
        self._kernel32.CreateJobObjectW.restype = wintypes.HANDLE
        self._kernel32.SetInformationJobObject.argtypes = [
            wintypes.HANDLE,
            ctypes.c_int,
            ctypes.c_void_p,
            wintypes.DWORD,
        ]
        self._kernel32.SetInformationJobObject.restype = wintypes.BOOL
        self._kernel32.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
        self._kernel32.OpenProcess.restype = wintypes.HANDLE
        self._kernel32.AssignProcessToJobObject.argtypes = [wintypes.HANDLE, wintypes.HANDLE]
        self._kernel32.AssignProcessToJobObject.restype = wintypes.BOOL
        self._kernel32.QueryInformationJobObject.argtypes = [
            wintypes.HANDLE,
            ctypes.c_int,
            ctypes.c_void_p,
            wintypes.DWORD,
            ctypes.POINTER(wintypes.DWORD),
        ]
        self._kernel32.QueryInformationJobObject.restype = wintypes.BOOL
        self._kernel32.TerminateJobObject.argtypes = [wintypes.HANDLE, wintypes.UINT]
        self._kernel32.TerminateJobObject.restype = wintypes.BOOL
        self._kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
        self._kernel32.CloseHandle.restype = wintypes.BOOL

        self._handle = self._kernel32.CreateJobObjectW(None, None)
        if not self._handle:
            raise ctypes.WinError(ctypes.get_last_error())

        info = ExtendedLimitInformation()
        info.basic_limit_information.limit_flags = WINDOWS_JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
        if not self._kernel32.SetInformationJobObject(
            self._handle,
            WINDOWS_JOB_OBJECT_EXTENDED_LIMIT_INFORMATION,
            ctypes.byref(info),
            ctypes.sizeof(info),
        ):
            error = ctypes.WinError(ctypes.get_last_error())
            self.close()
            raise error

    def assign(self, process_id: int) -> None:
        """Assign a still-suspended process before it can create descendants."""
        access = WINDOWS_PROCESS_SET_QUOTA | WINDOWS_PROCESS_TERMINATE
        process_handle = self._kernel32.OpenProcess(access, False, process_id)
        if not process_handle:
            raise ctypes.WinError(ctypes.get_last_error())
        try:
            if not self._kernel32.AssignProcessToJobObject(self._handle, process_handle):
                raise ctypes.WinError(ctypes.get_last_error())
        finally:
            self._kernel32.CloseHandle(process_handle)

    def active_pids(self) -> List[int]:
        """Return active process IDs still assigned to this job."""
        from ctypes import wintypes

        if not self._handle:
            return []
        buffer = ctypes.create_string_buffer(WINDOWS_JOB_QUERY_BUFFER_BYTES)
        returned = wintypes.DWORD()
        if not self._kernel32.QueryInformationJobObject(
            self._handle,
            WINDOWS_JOB_OBJECT_BASIC_PROCESS_ID_LIST,
            buffer,
            len(buffer),
            ctypes.byref(returned),
        ):
            raise ctypes.WinError(ctypes.get_last_error())

        count = ctypes.c_uint32.from_buffer(buffer, ctypes.sizeof(ctypes.c_uint32)).value
        first_pid_offset = ctypes.sizeof(ctypes.c_uint32) * 2
        pid_size = ctypes.sizeof(ctypes.c_size_t)
        capacity = (len(buffer) - first_pid_offset) // pid_size
        if count > capacity:
            raise OSError(f"Windows Job Object PID list exceeded capacity: {count} > {capacity}")
        return [
            int(ctypes.c_size_t.from_buffer(buffer, first_pid_offset + index * pid_size).value)
            for index in range(count)
        ]

    def terminate(self) -> None:
        """Terminate every process currently assigned to the job."""
        if self._handle and not self._kernel32.TerminateJobObject(
            self._handle,
            WINDOWS_JOB_TERMINATE_EXIT_CODE,
        ):
            raise ctypes.WinError(ctypes.get_last_error())

    def close(self) -> None:
        """Close the job; kill-on-close covers any late residual process."""
        handle = getattr(self, "_handle", None)
        if handle:
            self._handle = None
            self._kernel32.CloseHandle(handle)


def _resume_suspended_windows_process(process_id: int) -> None:
    """Resume the primary thread after its process has joined the Job Object."""
    from ctypes import wintypes

    class ThreadEntry32(ctypes.Structure):
        """Mirror the Windows THREADENTRY32 structure."""

        _fields_ = [
            ("size", wintypes.DWORD),
            ("usage_count", wintypes.DWORD),
            ("thread_id", wintypes.DWORD),
            ("owner_process_id", wintypes.DWORD),
            ("base_priority", wintypes.LONG),
            ("delta_priority", wintypes.LONG),
            ("flags", wintypes.DWORD),
        ]

    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel32.CreateToolhelp32Snapshot.argtypes = [wintypes.DWORD, wintypes.DWORD]
    kernel32.CreateToolhelp32Snapshot.restype = wintypes.HANDLE
    kernel32.Thread32First.argtypes = [wintypes.HANDLE, ctypes.POINTER(ThreadEntry32)]
    kernel32.Thread32First.restype = wintypes.BOOL
    kernel32.Thread32Next.argtypes = [wintypes.HANDLE, ctypes.POINTER(ThreadEntry32)]
    kernel32.Thread32Next.restype = wintypes.BOOL
    kernel32.OpenThread.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
    kernel32.OpenThread.restype = wintypes.HANDLE
    kernel32.ResumeThread.argtypes = [wintypes.HANDLE]
    kernel32.ResumeThread.restype = wintypes.DWORD
    kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
    kernel32.CloseHandle.restype = wintypes.BOOL

    snapshot = kernel32.CreateToolhelp32Snapshot(WINDOWS_TH32CS_SNAPTHREAD, 0)
    if snapshot == ctypes.c_void_p(-1).value:
        raise ctypes.WinError(ctypes.get_last_error())
    thread_id = None
    try:
        entry = ThreadEntry32()
        entry.size = ctypes.sizeof(entry)
        available = kernel32.Thread32First(snapshot, ctypes.byref(entry))
        while available:
            if entry.owner_process_id == process_id:
                thread_id = int(entry.thread_id)
                break
            available = kernel32.Thread32Next(snapshot, ctypes.byref(entry))
    finally:
        kernel32.CloseHandle(snapshot)

    if thread_id is None:
        raise OSError(f"Could not locate suspended primary thread for process {process_id}")
    thread_handle = kernel32.OpenThread(WINDOWS_THREAD_SUSPEND_RESUME, False, thread_id)
    if not thread_handle:
        raise ctypes.WinError(ctypes.get_last_error())
    try:
        if kernel32.ResumeThread(thread_handle) == WINDOWS_DWORD_FAILURE:
            raise ctypes.WinError(ctypes.get_last_error())
    finally:
        kernel32.CloseHandle(thread_handle)


def _posix_process_group_exists(process_group_id: int) -> bool:
    """Probe one POSIX process group without spawning another process."""

    try:
        os.killpg(process_group_id, 0)
    except ProcessLookupError:
        return False
    except PermissionError:
        return True
    return True


def _posix_process_group_pids(process_group_id: int) -> List[int]:
    """Enumerate a POSIX process group for bounded residual diagnostics."""
    try:
        result = subprocess.run(
            ["ps", "-eo", "pid=,pgid="],
            check=False,
            capture_output=True,
            text=True,
        )
    except OSError:
        return [process_group_id] if _posix_process_group_exists(process_group_id) else []
    pids = []
    for line in result.stdout.splitlines():
        fields = line.split()
        if len(fields) != 2:
            continue
        try:
            pid, group = (int(field) for field in fields)
        except ValueError:
            continue
        if group == process_group_id:
            pids.append(pid)
    if not pids and _posix_process_group_exists(process_group_id):
        return [process_group_id]
    return pids


class OwnedProcessTree:
    """Platform-owned process tree used for one Unity build invocation."""

    def __init__(
        self,
        process: subprocess.Popen,
        windows_job: Optional[_WindowsKillOnCloseJob] = None,
        posix_process_group_id: Optional[int] = None,
    ) -> None:
        """Record one launched process and its platform ownership boundary."""
        self.process = process
        self._windows_job = windows_job
        self._posix_process_group_id = posix_process_group_id

    def active_pids(self) -> List[int]:
        """Return the process IDs still owned by this invocation."""
        if self._windows_job is not None:
            return self._windows_job.active_pids()
        if self._posix_process_group_id is not None:
            return _posix_process_group_pids(self._posix_process_group_id)
        return []

    def terminate(self) -> List[int]:
        """Terminate the owned tree and return PIDs that miss the quiescence bound."""
        if self._windows_job is not None:
            self._windows_job.terminate()
        elif self._posix_process_group_id is not None:
            try:
                os.killpg(self._posix_process_group_id, signal.SIGKILL)
            except ProcessLookupError:
                pass

        try:
            self.process.wait(timeout=UNITY_TERMINATION_WAIT_SECONDS)
        except subprocess.TimeoutExpired:
            pass

        deadline = time.monotonic() + UNITY_TERMINATION_WAIT_SECONDS
        while True:
            if self._posix_process_group_id is not None:
                if not _posix_process_group_exists(self._posix_process_group_id):
                    return []
                if time.monotonic() >= deadline:
                    return _posix_process_group_pids(self._posix_process_group_id)
                time.sleep(PROCESS_TREE_POLL_SECONDS)
                continue
            residual_pids = self.active_pids()
            if not residual_pids or time.monotonic() >= deadline:
                return residual_pids
            time.sleep(PROCESS_TREE_POLL_SECONDS)

    def close(self) -> None:
        """Release platform ownership after the invocation is quiescent."""
        if self._windows_job is not None:
            self._windows_job.close()
            self._windows_job = None
        elif self._posix_process_group_id is not None:
            try:
                os.killpg(self._posix_process_group_id, signal.SIGKILL)
            except ProcessLookupError:
                pass
            self._posix_process_group_id = None


def start_owned_process(cmd: List[str], root: Path) -> OwnedProcessTree:
    """Start Unity inside a platform-owned process tree."""
    if os.name == "nt":
        job = _WindowsKillOnCloseJob()
        process = None
        try:
            process = subprocess.Popen(
                cmd,
                cwd=root,
                creationflags=subprocess.CREATE_NEW_PROCESS_GROUP | WINDOWS_CREATE_SUSPENDED,
            )
            job.assign(process.pid)
            _resume_suspended_windows_process(process.pid)
            return OwnedProcessTree(process, windows_job=job)
        except Exception:
            if process is not None:
                try:
                    process.kill()
                    process.wait(timeout=UNITY_TERMINATION_WAIT_SECONDS)
                except (OSError, subprocess.TimeoutExpired):
                    pass
            job.close()
            raise

    process = subprocess.Popen(cmd, cwd=root, start_new_session=True)
    return OwnedProcessTree(process, posix_process_group_id=process.pid)


def terminate_process(process_tree: OwnedProcessTree) -> List[int]:
    """Terminate an owned Unity process tree and return residual process IDs."""
    try:
        return process_tree.terminate()
    except OSError as exc:
        print(
            f"[build_unity_il2cpp] Owned process-tree termination failed: {exc}",
            file=sys.stderr,
            flush=True,
        )
        try:
            process_tree.process.kill()
            process_tree.process.wait(timeout=UNITY_TERMINATION_WAIT_SECONDS)
        except (OSError, subprocess.TimeoutExpired):
            pass
        try:
            return process_tree.active_pids()
        except OSError:
            return [process_tree.process.pid] if process_tree.process.poll() is None else []


def run_with_progress(cmd: List[str], root: Path, log_path: Path, interval: int, timeout_minutes: int) -> int:
    """Run the Unity process, tailing important log lines at the given interval."""
    started = time.monotonic()
    next_heartbeat = started + interval
    timeout_seconds = timeout_minutes * SECONDS_PER_MINUTE if timeout_minutes > 0 else None
    offset = INITIAL_LOG_OFFSET

    process_tree = start_owned_process(cmd, root)
    process = process_tree.process
    try:
        while True:
            offset, lines = read_new_important_lines(log_path, offset)
            for line in lines:
                print(f"[unity-log] {line}", flush=True)

            returncode = process.poll()
            now = time.monotonic()
            if returncode is not None:
                break

            if timeout_seconds is not None and now - started >= timeout_seconds:
                offset, lines = read_new_important_lines(log_path, offset)
                for line in lines:
                    print(f"[unity-log] {line}", flush=True)
                print(
                    f"[build_unity_il2cpp] Unity timed out after {format_elapsed(now - started)}; "
                    f"terminating owned process tree. Log: {relative_to_root(log_path, root)}",
                    file=sys.stderr,
                    flush=True,
                )
                residual_pids = terminate_process(process_tree)
                if residual_pids:
                    print(
                        "[build_unity_il2cpp] Owned process tree did not quiesce; "
                        f"residual PIDs: {', '.join(str(pid) for pid in residual_pids)}",
                        file=sys.stderr,
                        flush=True,
                    )
                return EXIT_TIMEOUT

            if now >= next_heartbeat:
                elapsed = format_elapsed(now - started)
                print(
                    f"[build_unity_il2cpp] Elapsed {elapsed}; still building. "
                    f"Log: {relative_to_root(log_path, root)}",
                    flush=True,
                )
                next_heartbeat = now + interval

            time.sleep(LOG_POLL_SLEEP_SECONDS)
    finally:
        process_tree.close()

    offset, lines = read_new_important_lines(log_path, offset)
    for line in lines:
        print(f"[unity-log] {line}", flush=True)

    elapsed = format_elapsed(time.monotonic() - started)
    print(f"[build_unity_il2cpp] Unity exited after {elapsed}.", flush=True)
    return returncode


def non_negative_int(value: str) -> int:
    """Parse an integer whose only timeout-disable sentinel is zero."""

    parsed = int(value)
    if parsed < 0:
        raise argparse.ArgumentTypeError("must be zero or greater")
    return parsed


def parse_args() -> argparse.Namespace:
    """Parse CLI arguments for the build script."""
    parser = argparse.ArgumentParser(
        description="Run Unity batchmode IL2CPP build for the Foxglove demo project."
    )
    parser.add_argument(
        "--target",
        choices=TARGETS,
        default=default_target(),
        help="Build target. Defaults to the current host platform.",
    )
    parser.add_argument(
        "--unity",
        help="Path to the Unity executable. Defaults to UNITY_EXE/UNITY_PATH or Unity Hub discovery.",
    )
    parser.add_argument(
        "--project",
        default="Unity2Foxglove",
        help="Unity project path relative to the workspace root.",
    )
    parser.add_argument(
        "--log",
        help="Log path relative to the workspace root. Defaults to <build-dir>/build.log.",
    )
    parser.add_argument(
        "--build-dir",
        help="Build run directory relative to the workspace root. Defaults to build/Unity/<target>-il2cpp-<timestamp>/.",
    )
    parser.add_argument(
        "--output",
        help="Player output path relative to the workspace root. Defaults inside <build-dir>.",
    )
    parser.add_argument(
        "--dry-run",
        action="store_true",
        help="Print the resolved project, target, and log path without starting Unity.",
    )
    parser.add_argument(
        "--allow-missing-unity",
        action="store_true",
        help="Allow dry-run path validation when Unity is not installed. Valid only with --dry-run.",
    )
    parser.add_argument(
        "--progress-interval",
        type=int,
        default=DEFAULT_PROGRESS_INTERVAL_SECONDS,
        help="Seconds between progress heartbeats while Unity is running.",
    )
    parser.add_argument(
        "--timeout-minutes",
        type=non_negative_int,
        default=DEFAULT_BUILD_TIMEOUT_MINUTES,
        help="Maximum Unity build runtime before terminating. Use 0 to disable the timeout.",
    )
    return parser.parse_args()


def main() -> int:
    """Main entry: parse args, build command, run Unity, report result."""
    args = parse_args()
    root = repo_root()

    if args.allow_missing_unity and not args.dry_run:
        print(
            "[build_unity_il2cpp] --allow-missing-unity is only valid with --dry-run.",
            file=sys.stderr,
        )
        return EXIT_USAGE_ERROR

    try:
        cmd, project_path, log_path, output_path = build_command(args)
    except (OSError, ValueError) as exc:
        print(f"[build_unity_il2cpp] {exc}", file=sys.stderr)
        return EXIT_USAGE_ERROR

    print(f"[build_unity_il2cpp] Unity:    {cmd[UNITY_EXECUTABLE_COMMAND_INDEX]}")
    print(f"[build_unity_il2cpp] Project:   {relative_to_root(project_path, root)}")
    print(f"[build_unity_il2cpp] Target:    {args.target}")
    print(f"[build_unity_il2cpp] Log:       {relative_to_root(log_path, root)}")
    print(f"[build_unity_il2cpp] Output:    {relative_to_root(output_path, root)}")

    generated_failures = validate_generated_artifacts(root)
    if generated_failures:
        print("[build_unity_il2cpp] Generated artifact preflight failed:", file=sys.stderr)
        for failure in generated_failures:
            print(f"  {failure}", file=sys.stderr)
        print(
            "[build_unity_il2cpp] Regenerate schema/source-generator artifacts before invoking Unity.",
            file=sys.stderr,
        )
        return EXIT_PREFLIGHT_FAILURE

    if args.dry_run:
        print("[build_unity_il2cpp] Dry run only; Unity was not started.")
        return EXIT_SUCCESS

    log_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.parent.mkdir(parents=True, exist_ok=True)

    print("[build_unity_il2cpp] Starting Unity batchmode build...")

    try:
        returncode = run_with_progress(
            cmd,
            root,
            log_path,
            max(MIN_PROGRESS_INTERVAL_SECONDS, args.progress_interval),
            args.timeout_minutes,
        )
    except OSError as exc:
        print(f"[build_unity_il2cpp] Unity could not be started: {exc}", file=sys.stderr)
        return EXIT_PREFLIGHT_FAILURE
    if returncode == EXIT_SUCCESS:
        print("[build_unity_il2cpp] Build command completed successfully.")
    else:
        print(
            f"[build_unity_il2cpp] Build failed with exit code {returncode}. "
            f"See log: {relative_to_root(log_path, root)}",
            file=sys.stderr,
        )

    return returncode


if __name__ == "__main__":
    raise SystemExit(main())
