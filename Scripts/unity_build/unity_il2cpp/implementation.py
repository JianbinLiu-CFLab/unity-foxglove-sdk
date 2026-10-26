#!/usr/bin/env python3
from __future__ import annotations

import sys as _phase192_sys
from pathlib import Path as _Phase192Path

_PHASE192_PACKAGE_DIR = _Phase192Path(__file__).resolve().parent
if str(_PHASE192_PACKAGE_DIR) not in _phase192_sys.path:
    _phase192_sys.path.append(str(_PHASE192_PACKAGE_DIR))

from parts.foundation import (
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
from parts.il2cpp_logging import (
    _LogTailState,
    read_new_important_lines,
    finish_log_tail,
)
from parts.process import (
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
from parts.cli import (
    non_negative_int,
    parse_args,
    _interrupt_build,
    main,
)
import parts.foundation as _phase192_foundation
import parts.il2cpp_logging as _phase192_logging
import parts.process as _phase192_process
import parts.cli as _phase192_cli

_phase192_foundation.REPO_ROOT_PARENT_DEPTH = 2
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


def build_command(*args, **kwargs):
    """Build the Unity command through the split foundation implementation."""
    _phase192_sync_parts()
    return _phase192_build_command_impl(*args, **kwargs)


def validate_generated_artifacts(*args, **kwargs):
    """Validate generated artifacts through the split foundation implementation."""
    _phase192_sync_parts()
    return _phase192_validate_generated_artifacts_impl(*args, **kwargs)


def read_new_important_lines(*args, **kwargs):
    """Read important log lines through the split logging implementation."""
    _phase192_sync_parts()
    return _phase192_read_new_important_lines_impl(*args, **kwargs)


def finish_log_tail(*args, **kwargs):
    """Finish log-tail processing through the split logging implementation."""
    _phase192_sync_parts()
    return _phase192_finish_log_tail_impl(*args, **kwargs)


def _posix_process_group_pids(*args, **kwargs):
    """Enumerate process-group members through the split process implementation."""
    _phase192_sync_parts()
    return _phase192_posix_process_group_pids_impl(*args, **kwargs)


def start_owned_process(*args, **kwargs):
    """Start an owned process tree through the split process implementation."""
    _phase192_sync_parts()
    return _phase192_start_owned_process_impl(*args, **kwargs)


def await_tree_quiescence(*args, **kwargs):
    """Wait for an owned process tree through the split process implementation."""
    _phase192_sync_parts()
    return _phase192_await_tree_quiescence_impl(*args, **kwargs)


def terminate_process(*args, **kwargs):
    """Terminate an owned process tree through the split process implementation."""
    _phase192_sync_parts()
    return _phase192_terminate_process_impl(*args, **kwargs)


def run_with_progress(*args, **kwargs):
    """Run Unity with progress reporting through the split process implementation."""
    _phase192_sync_parts()
    return _phase192_run_with_progress_impl(*args, **kwargs)


def main(*args, **kwargs):
    """Run the Unity IL2CPP command through the split CLI implementation."""
    _phase192_sync_parts()
    return _phase192_main_impl(*args, **kwargs)
__all__ = ['argparse', 'ctypes', 'os', 'platform', 're', 'signal', 'stat', 'subprocess', 'sys', 'time', 'uuid', 'datetime', 'timezone', 'Path', 'Dict', 'List', 'Optional', 'Tuple', 'TARGETS', 'REPO_ROOT_PARENT_DEPTH', 'EXIT_SUCCESS', 'EXIT_PREFLIGHT_FAILURE', 'EXIT_USAGE_ERROR', 'EXIT_TIMEOUT', 'SECONDS_PER_HOUR', 'SECONDS_PER_MINUTE', 'LOG_POLL_SLEEP_SECONDS', 'DEFAULT_PROGRESS_INTERVAL_SECONDS', 'MIN_PROGRESS_INTERVAL_SECONDS', 'MAX_PROGRESS_INTERVAL_SECONDS', 'DEFAULT_BUILD_TIMEOUT_MINUTES', 'UNITY_TERMINATION_WAIT_SECONDS', 'PROCESS_DIAGNOSTIC_TIMEOUT_SECONDS', 'PROCESS_TREE_POLL_SECONDS', 'WINDOWS_CREATE_SUSPENDED', 'WINDOWS_JOB_OBJECT_BASIC_PROCESS_ID_LIST', 'WINDOWS_JOB_OBJECT_EXTENDED_LIMIT_INFORMATION', 'WINDOWS_JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE', 'WINDOWS_JOB_QUERY_BUFFER_BYTES', 'WINDOWS_PROCESS_SET_QUOTA', 'WINDOWS_PROCESS_TERMINATE', 'WINDOWS_THREAD_SUSPEND_RESUME', 'WINDOWS_TH32CS_SNAPTHREAD', 'WINDOWS_DWORD_FAILURE', 'WINDOWS_JOB_TERMINATE_EXIT_CODE', 'WINDOWS_EXECUTABLE_MAGIC', 'PROJECT_VERSION_SPLIT_MAX', 'PROJECT_VERSION_VALUE_INDEX', 'PROJECT_VERSION_MAX_BYTES', 'UNITY_EDITOR_VERSION_PATTERN', 'INITIAL_LOG_OFFSET', 'MAX_LOG_READ_BYTES', 'MAX_FINAL_LOG_CHUNKS', 'UNITY_EXECUTABLE_COMMAND_INDEX', 'REQUIRED_GENERATED_ARTIFACTS', 'IMPORTANT_LOG_MARKERS', 'repo_root', 'default_target', 'unity_version_key', 'accepted_unity_candidate', 'is_unity_editor_version', 'contained_unity_candidate', 'newest_existing', 'find_unity_explicit', 'find_unity_from_env', 'find_unity_from_project_version', 'find_unity_from_hub', 'find_unity', 'relative_to_root', 'require_workspace_path', 'validate_generated_artifacts', 'resolve_unity_for_command', 'output_fingerprint', 'build_command', 'default_build_dir', 'default_output_path', 'format_elapsed', 'is_important_log_line', '_LogTailState', 'read_new_important_lines', 'finish_log_tail', '_WindowsKillOnCloseJob', '_resume_suspended_windows_process', '_posix_process_group_exists', '_posix_process_group_pids', '_posix_descendant_pids', '_posix_pid_exists', 'OwnedProcessTree', 'start_owned_process', 'await_tree_quiescence', 'terminate_process', 'run_with_progress', 'non_negative_int', 'parse_args', '_interrupt_build', 'main']


