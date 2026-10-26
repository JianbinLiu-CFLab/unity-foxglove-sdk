#!/usr/bin/env python3
from __future__ import annotations

import sys as _phase192_sys
import types as _phase192_types

from .foundation import *
from .logging import *
from .process import *
from .cli import *
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
__all__ = ['DEFAULT_BUILD_TIMEOUT_MINUTES', 'DEFAULT_PROGRESS_INTERVAL_SECONDS', 'Dict', 'EXIT_PREFLIGHT_FAILURE', 'EXIT_SUCCESS', 'EXIT_TIMEOUT', 'EXIT_USAGE_ERROR', 'IMPORTANT_LOG_MARKERS', 'INITIAL_LOG_OFFSET', 'LOG_POLL_SLEEP_SECONDS', 'List', 'MAX_FINAL_LOG_CHUNKS', 'MAX_LOG_READ_BYTES', 'MAX_PROGRESS_INTERVAL_SECONDS', 'MIN_PROGRESS_INTERVAL_SECONDS', 'Optional', 'OwnedProcessTree', 'PROCESS_DIAGNOSTIC_TIMEOUT_SECONDS', 'PROCESS_TREE_POLL_SECONDS', 'PROJECT_VERSION_MAX_BYTES', 'PROJECT_VERSION_SPLIT_MAX', 'PROJECT_VERSION_VALUE_INDEX', 'Path', 'REPO_ROOT_PARENT_DEPTH', 'REQUIRED_GENERATED_ARTIFACTS', 'SECONDS_PER_HOUR', 'SECONDS_PER_MINUTE', 'TARGETS', 'Tuple', 'UNITY_EDITOR_VERSION_PATTERN', 'UNITY_EXECUTABLE_COMMAND_INDEX', 'UNITY_TERMINATION_WAIT_SECONDS', 'WINDOWS_CREATE_SUSPENDED', 'WINDOWS_DWORD_FAILURE', 'WINDOWS_EXECUTABLE_MAGIC', 'WINDOWS_JOB_OBJECT_BASIC_PROCESS_ID_LIST', 'WINDOWS_JOB_OBJECT_EXTENDED_LIMIT_INFORMATION', 'WINDOWS_JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE', 'WINDOWS_JOB_QUERY_BUFFER_BYTES', 'WINDOWS_JOB_TERMINATE_EXIT_CODE', 'WINDOWS_PROCESS_SET_QUOTA', 'WINDOWS_PROCESS_TERMINATE', 'WINDOWS_TH32CS_SNAPTHREAD', 'WINDOWS_THREAD_SUSPEND_RESUME', '_LogTailState', '_WindowsKillOnCloseJob', '_interrupt_build', '_posix_descendant_pids', '_posix_pid_exists', '_posix_process_group_exists', '_posix_process_group_pids', '_resume_suspended_windows_process', 'accepted_unity_candidate', 'argparse', 'await_tree_quiescence', 'build_command', 'contained_unity_candidate', 'ctypes', 'datetime', 'default_build_dir', 'default_output_path', 'default_target', 'find_unity', 'find_unity_explicit', 'find_unity_from_env', 'find_unity_from_hub', 'find_unity_from_project_version', 'finish_log_tail', 'format_elapsed', 'is_important_log_line', 'is_unity_editor_version', 'main', 'newest_existing', 'non_negative_int', 'os', 'output_fingerprint', 'parse_args', 'platform', 're', 'read_new_important_lines', 'relative_to_root', 'repo_root', 'require_workspace_path', 'resolve_unity_for_command', 'run_with_progress', 'signal', 'start_owned_process', 'stat', 'subprocess', 'sys', 'terminate_process', 'time', 'timezone', 'unity_version_key', 'uuid', 'validate_generated_artifacts']
