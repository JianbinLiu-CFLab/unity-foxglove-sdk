#!/usr/bin/env python3
from __future__ import annotations

from .foundation import (
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
from .logging import (
    _LogTailState,
    read_new_important_lines,
    finish_log_tail,
)
from .process import (
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
from .cli import (
    non_negative_int,
    parse_args,
    _interrupt_build,
    main,
)
from . import foundation as _phase192_foundation
from . import logging as _phase192_logging
from . import process as _phase192_process
from . import cli as _phase192_cli

_phase192_foundation.REPO_ROOT_PARENT_DEPTH = 2
_PHASE192_SECTION_MODULES = (
    _phase192_foundation,
    _phase192_logging,
    _phase192_process,
    _phase192_cli,
)
def _phase192_sync_exported_state(name, value):
    """Synchronize facade-patched globals with split implementation modules."""
    for _phase192_section in _PHASE192_SECTION_MODULES:
        setattr(_phase192_section, name, value)


def _phase192_delete_exported_state(name):
    """Remove facade-deleted globals from split implementation modules."""
    for _phase192_section in _PHASE192_SECTION_MODULES:
        delattr(_phase192_section, name)


__all__ = ['argparse', 'ctypes', 'os', 'platform', 're', 'signal', 'stat', 'subprocess', 'sys', 'time', 'uuid', 'datetime', 'timezone', 'Path', 'Dict', 'List', 'Optional', 'Tuple', 'TARGETS', 'REPO_ROOT_PARENT_DEPTH', 'EXIT_SUCCESS', 'EXIT_PREFLIGHT_FAILURE', 'EXIT_USAGE_ERROR', 'EXIT_TIMEOUT', 'SECONDS_PER_HOUR', 'SECONDS_PER_MINUTE', 'LOG_POLL_SLEEP_SECONDS', 'DEFAULT_PROGRESS_INTERVAL_SECONDS', 'MIN_PROGRESS_INTERVAL_SECONDS', 'MAX_PROGRESS_INTERVAL_SECONDS', 'DEFAULT_BUILD_TIMEOUT_MINUTES', 'UNITY_TERMINATION_WAIT_SECONDS', 'PROCESS_DIAGNOSTIC_TIMEOUT_SECONDS', 'PROCESS_TREE_POLL_SECONDS', 'WINDOWS_CREATE_SUSPENDED', 'WINDOWS_JOB_OBJECT_BASIC_PROCESS_ID_LIST', 'WINDOWS_JOB_OBJECT_EXTENDED_LIMIT_INFORMATION', 'WINDOWS_JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE', 'WINDOWS_JOB_QUERY_BUFFER_BYTES', 'WINDOWS_PROCESS_SET_QUOTA', 'WINDOWS_PROCESS_TERMINATE', 'WINDOWS_THREAD_SUSPEND_RESUME', 'WINDOWS_TH32CS_SNAPTHREAD', 'WINDOWS_DWORD_FAILURE', 'WINDOWS_JOB_TERMINATE_EXIT_CODE', 'WINDOWS_EXECUTABLE_MAGIC', 'PROJECT_VERSION_SPLIT_MAX', 'PROJECT_VERSION_VALUE_INDEX', 'PROJECT_VERSION_MAX_BYTES', 'UNITY_EDITOR_VERSION_PATTERN', 'INITIAL_LOG_OFFSET', 'MAX_LOG_READ_BYTES', 'MAX_FINAL_LOG_CHUNKS', 'UNITY_EXECUTABLE_COMMAND_INDEX', 'REQUIRED_GENERATED_ARTIFACTS', 'IMPORTANT_LOG_MARKERS', 'repo_root', 'default_target', 'unity_version_key', 'accepted_unity_candidate', 'is_unity_editor_version', 'contained_unity_candidate', 'newest_existing', 'find_unity_explicit', 'find_unity_from_env', 'find_unity_from_project_version', 'find_unity_from_hub', 'find_unity', 'relative_to_root', 'require_workspace_path', 'validate_generated_artifacts', 'resolve_unity_for_command', 'output_fingerprint', 'build_command', 'default_build_dir', 'default_output_path', 'format_elapsed', 'is_important_log_line', '_LogTailState', 'read_new_important_lines', 'finish_log_tail', '_WindowsKillOnCloseJob', '_resume_suspended_windows_process', '_posix_process_group_exists', '_posix_process_group_pids', '_posix_descendant_pids', '_posix_pid_exists', 'OwnedProcessTree', 'start_owned_process', 'await_tree_quiescence', 'terminate_process', 'run_with_progress', 'non_negative_int', 'parse_args', '_interrupt_build', 'main', '_phase192_sync_exported_state', '_phase192_delete_exported_state']
