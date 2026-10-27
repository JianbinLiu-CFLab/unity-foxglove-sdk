#!/usr/bin/env python3
from __future__ import annotations

from unity_il2cpp.parts.foundation import (
    argparse,
    ctypes,
    os,
    platform,
    re,
    signal,
    stat,
    subprocess,
    sys,
    time,
    uuid,
    datetime,
    timezone,
    Path,
    Dict,
    List,
    Optional,
    Tuple,
    TARGETS,
    REPO_ROOT_PARENT_DEPTH,
    EXIT_SUCCESS,
    EXIT_PREFLIGHT_FAILURE,
    EXIT_USAGE_ERROR,
    EXIT_TIMEOUT,
    SECONDS_PER_HOUR,
    SECONDS_PER_MINUTE,
    LOG_POLL_SLEEP_SECONDS,
    DEFAULT_PROGRESS_INTERVAL_SECONDS,
    MIN_PROGRESS_INTERVAL_SECONDS,
    MAX_PROGRESS_INTERVAL_SECONDS,
    DEFAULT_BUILD_TIMEOUT_MINUTES,
    UNITY_TERMINATION_WAIT_SECONDS,
    PROCESS_DIAGNOSTIC_TIMEOUT_SECONDS,
    PROCESS_TREE_POLL_SECONDS,
    WINDOWS_CREATE_SUSPENDED,
    WINDOWS_JOB_OBJECT_BASIC_PROCESS_ID_LIST,
    WINDOWS_JOB_OBJECT_EXTENDED_LIMIT_INFORMATION,
    WINDOWS_JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE,
    WINDOWS_JOB_QUERY_BUFFER_BYTES,
    WINDOWS_PROCESS_SET_QUOTA,
    WINDOWS_PROCESS_TERMINATE,
    WINDOWS_THREAD_SUSPEND_RESUME,
    WINDOWS_TH32CS_SNAPTHREAD,
    WINDOWS_DWORD_FAILURE,
    WINDOWS_JOB_TERMINATE_EXIT_CODE,
    WINDOWS_EXECUTABLE_MAGIC,
    PROJECT_VERSION_SPLIT_MAX,
    PROJECT_VERSION_VALUE_INDEX,
    PROJECT_VERSION_MAX_BYTES,
    UNITY_EDITOR_VERSION_PATTERN,
    INITIAL_LOG_OFFSET,
    MAX_LOG_READ_BYTES,
    MAX_FINAL_LOG_CHUNKS,
    UNITY_EXECUTABLE_COMMAND_INDEX,
    REQUIRED_GENERATED_ARTIFACTS,
    IMPORTANT_LOG_MARKERS,
    repo_root,
    default_target,
    unity_version_key,
    accepted_unity_candidate,
    is_unity_editor_version,
    contained_unity_candidate,
    newest_existing,
    find_unity_explicit,
    find_unity_from_env,
    find_unity_from_project_version,
    find_unity_from_hub,
    find_unity,
    relative_to_root,
    require_workspace_path,
    validate_generated_artifacts,
    resolve_unity_for_command,
    output_fingerprint,
    build_command,
    default_build_dir,
    default_output_path,
    format_elapsed,
    is_important_log_line,
)
from unity_il2cpp.parts.il2cpp_logging import (
    _LogTailState,
    read_new_important_lines,
    finish_log_tail,
)
from unity_il2cpp.parts.process import (
    _WindowsKillOnCloseJob,
    _resume_suspended_windows_process,
    _posix_process_group_exists,
    _posix_process_group_pids,
    _posix_descendant_pids,
    _posix_pid_exists,
    OwnedProcessTree,
    start_owned_process,
    await_tree_quiescence,
    terminate_process,
    run_with_progress,
)
from unity_il2cpp.parts.cli import (
    non_negative_int,
    parse_args,
    _interrupt_build,
    main,
)
import unity_il2cpp.parts.foundation as _phase192_foundation
import unity_il2cpp.parts.il2cpp_logging as _phase192_logging
import unity_il2cpp.parts.process as _phase192_process
import unity_il2cpp.parts.cli as _phase192_cli

_phase192_foundation.REPO_ROOT_PARENT_DEPTH = 4
_PHASE192_SECTION_MODULES = (
    _phase192_foundation,
    _phase192_logging,
    _phase192_process,
    _phase192_cli,
)

_phase192_build_command_impl = build_command
_phase192_validate_generated_artifacts_impl = validate_generated_artifacts
_phase192_read_new_important_lines_impl = read_new_important_lines
_phase192_finish_log_tail_impl = finish_log_tail
_phase192_posix_process_group_pids_impl = _posix_process_group_pids
_phase192_start_owned_process_impl = start_owned_process
_phase192_await_tree_quiescence_impl = await_tree_quiescence
_phase192_terminate_process_impl = terminate_process
_phase192_run_with_progress_impl = run_with_progress
_phase192_main_impl = main


def _phase192_sync_parts():
    """Synchronize patched facade state with split implementation modules."""
    for _phase192_target in _PHASE192_SECTION_MODULES:
        _phase192_target.argparse = argparse
        _phase192_target.ctypes = ctypes
        _phase192_target.os = os
        _phase192_target.platform = platform
        _phase192_target.re = re
        _phase192_target.signal = signal
        _phase192_target.stat = stat
        _phase192_target.subprocess = subprocess
        _phase192_target.sys = sys
        _phase192_target.time = time
        _phase192_target.Path = Path
        _phase192_target.repo_root = repo_root
        _phase192_target.resolve_unity_for_command = resolve_unity_for_command
        _phase192_target.REPO_ROOT_PARENT_DEPTH = REPO_ROOT_PARENT_DEPTH
        _phase192_target.LOG_POLL_SLEEP_SECONDS = LOG_POLL_SLEEP_SECONDS
        _phase192_target.DEFAULT_PROGRESS_INTERVAL_SECONDS = DEFAULT_PROGRESS_INTERVAL_SECONDS
        _phase192_target.MIN_PROGRESS_INTERVAL_SECONDS = MIN_PROGRESS_INTERVAL_SECONDS
        _phase192_target.MAX_PROGRESS_INTERVAL_SECONDS = MAX_PROGRESS_INTERVAL_SECONDS
        _phase192_target.PROCESS_DIAGNOSTIC_TIMEOUT_SECONDS = PROCESS_DIAGNOSTIC_TIMEOUT_SECONDS
        _phase192_target.PROCESS_TREE_POLL_SECONDS = PROCESS_TREE_POLL_SECONDS
        _phase192_target.UNITY_TERMINATION_WAIT_SECONDS = UNITY_TERMINATION_WAIT_SECONDS
        _phase192_target.SECONDS_PER_MINUTE = SECONDS_PER_MINUTE
        _phase192_target.MAX_LOG_READ_BYTES = MAX_LOG_READ_BYTES
        _phase192_target.MAX_FINAL_LOG_CHUNKS = MAX_FINAL_LOG_CHUNKS
        _phase192_target.uuid = uuid
        _phase192_target.datetime = datetime
        _phase192_target.timezone = timezone
        _phase192_target.Dict = Dict
        _phase192_target.List = List
        _phase192_target.Optional = Optional
        _phase192_target.Tuple = Tuple
        _phase192_target.TARGETS = TARGETS
        _phase192_target.EXIT_SUCCESS = EXIT_SUCCESS
        _phase192_target.EXIT_PREFLIGHT_FAILURE = EXIT_PREFLIGHT_FAILURE
        _phase192_target.EXIT_USAGE_ERROR = EXIT_USAGE_ERROR
        _phase192_target.EXIT_TIMEOUT = EXIT_TIMEOUT
        _phase192_target.SECONDS_PER_HOUR = SECONDS_PER_HOUR
        _phase192_target.DEFAULT_BUILD_TIMEOUT_MINUTES = DEFAULT_BUILD_TIMEOUT_MINUTES
        _phase192_target.WINDOWS_CREATE_SUSPENDED = WINDOWS_CREATE_SUSPENDED
        _phase192_target.WINDOWS_JOB_OBJECT_BASIC_PROCESS_ID_LIST = WINDOWS_JOB_OBJECT_BASIC_PROCESS_ID_LIST
        _phase192_target.WINDOWS_JOB_OBJECT_EXTENDED_LIMIT_INFORMATION = WINDOWS_JOB_OBJECT_EXTENDED_LIMIT_INFORMATION
        _phase192_target.WINDOWS_JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = WINDOWS_JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
        _phase192_target.WINDOWS_JOB_QUERY_BUFFER_BYTES = WINDOWS_JOB_QUERY_BUFFER_BYTES
        _phase192_target.WINDOWS_PROCESS_SET_QUOTA = WINDOWS_PROCESS_SET_QUOTA
        _phase192_target.WINDOWS_PROCESS_TERMINATE = WINDOWS_PROCESS_TERMINATE
        _phase192_target.WINDOWS_THREAD_SUSPEND_RESUME = WINDOWS_THREAD_SUSPEND_RESUME
        _phase192_target.WINDOWS_TH32CS_SNAPTHREAD = WINDOWS_TH32CS_SNAPTHREAD
        _phase192_target.WINDOWS_DWORD_FAILURE = WINDOWS_DWORD_FAILURE
        _phase192_target.WINDOWS_JOB_TERMINATE_EXIT_CODE = WINDOWS_JOB_TERMINATE_EXIT_CODE
        _phase192_target.WINDOWS_EXECUTABLE_MAGIC = WINDOWS_EXECUTABLE_MAGIC
        _phase192_target.PROJECT_VERSION_SPLIT_MAX = PROJECT_VERSION_SPLIT_MAX
        _phase192_target.PROJECT_VERSION_VALUE_INDEX = PROJECT_VERSION_VALUE_INDEX
        _phase192_target.PROJECT_VERSION_MAX_BYTES = PROJECT_VERSION_MAX_BYTES
        _phase192_target.UNITY_EDITOR_VERSION_PATTERN = UNITY_EDITOR_VERSION_PATTERN
        _phase192_target.INITIAL_LOG_OFFSET = INITIAL_LOG_OFFSET
        _phase192_target.UNITY_EXECUTABLE_COMMAND_INDEX = UNITY_EXECUTABLE_COMMAND_INDEX
        _phase192_target.REQUIRED_GENERATED_ARTIFACTS = REQUIRED_GENERATED_ARTIFACTS
        _phase192_target.IMPORTANT_LOG_MARKERS = IMPORTANT_LOG_MARKERS
        _phase192_target.default_target = default_target
        _phase192_target.unity_version_key = unity_version_key
        _phase192_target.accepted_unity_candidate = accepted_unity_candidate
        _phase192_target.is_unity_editor_version = is_unity_editor_version
        _phase192_target.contained_unity_candidate = contained_unity_candidate
        _phase192_target.newest_existing = newest_existing
        _phase192_target.find_unity_explicit = find_unity_explicit
        _phase192_target.find_unity_from_env = find_unity_from_env
        _phase192_target.find_unity_from_project_version = find_unity_from_project_version
        _phase192_target.find_unity_from_hub = find_unity_from_hub
        _phase192_target.find_unity = find_unity
        _phase192_target.relative_to_root = relative_to_root
        _phase192_target.require_workspace_path = require_workspace_path
        _phase192_target.validate_generated_artifacts = validate_generated_artifacts
        _phase192_target.output_fingerprint = output_fingerprint
        _phase192_target.build_command = build_command
        _phase192_target.default_build_dir = default_build_dir
        _phase192_target.default_output_path = default_output_path
        _phase192_target.format_elapsed = format_elapsed
        _phase192_target.is_important_log_line = is_important_log_line
    _phase192_process._posix_process_group_exists = _posix_process_group_exists
    _phase192_process._posix_process_group_pids = _posix_process_group_pids
    _phase192_process.start_owned_process = start_owned_process
    _phase192_process.await_tree_quiescence = await_tree_quiescence
    _phase192_process.terminate_process = terminate_process
    _phase192_process.run_with_progress = run_with_progress
    _phase192_process._WindowsKillOnCloseJob = _WindowsKillOnCloseJob
    _phase192_process._resume_suspended_windows_process = _resume_suspended_windows_process
    _phase192_cli.build_command = build_command
    _phase192_cli.validate_generated_artifacts = validate_generated_artifacts
    _phase192_cli.run_with_progress = run_with_progress
    _phase192_cli.start_owned_process = start_owned_process


def build_command(args: argparse.Namespace) -> Tuple[List[str], Path, Path, Path]:
    """Build the Unity command through the split foundation implementation."""
    _phase192_sync_parts()
    return _phase192_build_command_impl(args)


def validate_generated_artifacts(root: Path) -> List[str]:
    """Validate generated artifacts through the split foundation implementation."""
    _phase192_sync_parts()
    return _phase192_validate_generated_artifacts_impl(root)


def read_new_important_lines(log_path: Path, offset: int, *, state: Optional[_LogTailState] = None, final: bool = False) -> Tuple[int, List[str]]:
    """Read important log lines through the split logging implementation."""
    _phase192_sync_parts()
    return _phase192_read_new_important_lines_impl(log_path, offset, state=state, final=final)


def finish_log_tail(log_path: Path, offset: int, state: _LogTailState) -> None:
    """Finish log-tail processing through the split logging implementation."""
    _phase192_sync_parts()
    return _phase192_finish_log_tail_impl(log_path, offset, state)


def _posix_process_group_pids(pid: int) -> List[int]:
    """Enumerate process-group members through the split process implementation."""
    _phase192_sync_parts()
    return _phase192_posix_process_group_pids_impl(pid)


def start_owned_process(cmd: List[str], root: Path, **popen_kwargs) -> OwnedProcessTree:
    """Start an owned process tree through the split process implementation."""
    _phase192_sync_parts()
    return _phase192_start_owned_process_impl(cmd, root, **popen_kwargs)


def await_tree_quiescence(process_tree: OwnedProcessTree, deadline_seconds: float) -> List[int]:
    """Wait for an owned process tree through the split process implementation."""
    _phase192_sync_parts()
    return _phase192_await_tree_quiescence_impl(process_tree, deadline_seconds)


def terminate_process(process_tree: OwnedProcessTree) -> List[int]:
    """Terminate an owned process tree through the split process implementation."""
    _phase192_sync_parts()
    return _phase192_terminate_process_impl(process_tree)


def run_with_progress(cmd: List[str], root: Path, log_path: Path, interval: int, timeout_minutes: int) -> int:
    """Run Unity with progress reporting through the split process implementation."""
    _phase192_sync_parts()
    return _phase192_run_with_progress_impl(cmd, root, log_path, interval, timeout_minutes)


def main() -> int:
    """Run the Unity IL2CPP command through the split CLI implementation."""
    _phase192_sync_parts()
    return _phase192_main_impl()
__all__ = ['argparse', 'ctypes', 'os', 'platform', 're', 'signal', 'stat', 'subprocess', 'sys', 'time', 'uuid', 'datetime', 'timezone', 'Path', 'Dict', 'List', 'Optional', 'Tuple', 'TARGETS', 'REPO_ROOT_PARENT_DEPTH', 'EXIT_SUCCESS', 'EXIT_PREFLIGHT_FAILURE', 'EXIT_USAGE_ERROR', 'EXIT_TIMEOUT', 'SECONDS_PER_HOUR', 'SECONDS_PER_MINUTE', 'LOG_POLL_SLEEP_SECONDS', 'DEFAULT_PROGRESS_INTERVAL_SECONDS', 'MIN_PROGRESS_INTERVAL_SECONDS', 'MAX_PROGRESS_INTERVAL_SECONDS', 'DEFAULT_BUILD_TIMEOUT_MINUTES', 'UNITY_TERMINATION_WAIT_SECONDS', 'PROCESS_DIAGNOSTIC_TIMEOUT_SECONDS', 'PROCESS_TREE_POLL_SECONDS', 'WINDOWS_CREATE_SUSPENDED', 'WINDOWS_JOB_OBJECT_BASIC_PROCESS_ID_LIST', 'WINDOWS_JOB_OBJECT_EXTENDED_LIMIT_INFORMATION', 'WINDOWS_JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE', 'WINDOWS_JOB_QUERY_BUFFER_BYTES', 'WINDOWS_PROCESS_SET_QUOTA', 'WINDOWS_PROCESS_TERMINATE', 'WINDOWS_THREAD_SUSPEND_RESUME', 'WINDOWS_TH32CS_SNAPTHREAD', 'WINDOWS_DWORD_FAILURE', 'WINDOWS_JOB_TERMINATE_EXIT_CODE', 'WINDOWS_EXECUTABLE_MAGIC', 'PROJECT_VERSION_SPLIT_MAX', 'PROJECT_VERSION_VALUE_INDEX', 'PROJECT_VERSION_MAX_BYTES', 'UNITY_EDITOR_VERSION_PATTERN', 'INITIAL_LOG_OFFSET', 'MAX_LOG_READ_BYTES', 'MAX_FINAL_LOG_CHUNKS', 'UNITY_EXECUTABLE_COMMAND_INDEX', 'REQUIRED_GENERATED_ARTIFACTS', 'IMPORTANT_LOG_MARKERS', 'repo_root', 'default_target', 'unity_version_key', 'accepted_unity_candidate', 'is_unity_editor_version', 'contained_unity_candidate', 'newest_existing', 'find_unity_explicit', 'find_unity_from_env', 'find_unity_from_project_version', 'find_unity_from_hub', 'find_unity', 'relative_to_root', 'require_workspace_path', 'validate_generated_artifacts', 'resolve_unity_for_command', 'output_fingerprint', 'build_command', 'default_build_dir', 'default_output_path', 'format_elapsed', 'is_important_log_line', '_LogTailState', 'read_new_important_lines', 'finish_log_tail', '_WindowsKillOnCloseJob', '_resume_suspended_windows_process', '_posix_process_group_exists', '_posix_process_group_pids', '_posix_descendant_pids', '_posix_pid_exists', 'OwnedProcessTree', 'start_owned_process', 'await_tree_quiescence', 'terminate_process', 'run_with_progress', 'non_negative_int', 'parse_args', '_interrupt_build', 'main']


