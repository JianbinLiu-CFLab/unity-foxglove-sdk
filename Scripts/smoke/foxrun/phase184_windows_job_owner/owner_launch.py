#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Phase184 WindowsJobOwner implementation section."""

from __future__ import annotations
from pathlib import Path as _Phase192Path
_PHASE192_FACADE_FILE = _Phase192Path(__file__).resolve().parents[1] / "phase184_windows_job_owner.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
from .foundation import *

class _WindowsJobOwnerLaunch:
    """Compatibility implementation section."""

    def launch_suspended_owned(
        self,
        application_path: os.PathLike[str] | str,
        arguments: Sequence[str],
        *,
        cwd: os.PathLike[str] | str,
        environment: Mapping[str, str],
        stdout_log: os.PathLike[str] | str,
        stderr_log: os.PathLike[str] | str,
        handoff_policy: RootHandoffPolicy | None = None,
    ) -> ProcessIdentity:
        """Create suspended, assign, prove identity/membership, then resume."""

        api, job_handle = self._require_active()
        selected_policy = (
            self._default_handoff_policy
            if handoff_policy is None
            else handoff_policy
        )
        if not isinstance(selected_policy, RootHandoffPolicy):
            raise _fail(
                FAIL_PROCESS_OWNERSHIP,
                "Root handoff policy is invalid.",
            )
        (
            application,
            frozen_arguments,
            working_directory,
            frozen_environment,
            standard_output,
            standard_error,
            command_line,
        ) = _validate_launch(
            application_path,
            arguments,
            cwd,
            environment,
            stdout_log,
            stderr_log,
        )
        created = None
        try:
            created = api.create_process_suspended(
                application_path=application,
                arguments=frozen_arguments,
                command_line=command_line,
                cwd=working_directory,
                environment=frozen_environment,
                stdout_log=standard_output,
                stderr_log=standard_error,
            )
            pid = int(created.pid)
            process_handle = int(created.process_handle)
            thread_handle = int(created.thread_handle)
            if pid <= 0 or process_handle <= 0 or thread_handle <= 0:
                raise OSError("Invalid created-process identity.")
        except BaseException as exc:
            if created is not None:
                self._raise_launch_stage_failure(
                    created,
                    exc,
                    code=FAIL_PROCESS_CREATE,
                    message="The suspended Windows process could not be created.",
                )
            if not isinstance(exc, Exception):
                raise
            raise _fail(
                FAIL_PROCESS_CREATE,
                "The suspended Windows process could not be created.",
            ) from None

        ownership_transferred = False
        try:
            assigned = bool(
                api.assign_process_to_job(job_handle, process_handle)
            )
        except BaseException as exc:
            self._raise_launch_stage_failure(
                created,
                exc,
                code=FAIL_PROCESS_ASSIGN,
                message="The suspended process could not be assigned to its Job Object.",
            )
        if not assigned:
            self._raise_launch_stage_failure(
                created,
                OSError("AssignProcessToJobObject returned false."),
                code=FAIL_PROCESS_ASSIGN,
                message="The suspended process could not be assigned to its Job Object.",
            )

        try:
            if not bool(api.is_process_in_job(process_handle, job_handle)):
                raise OSError("Created process is not in the exact Job.")
            ownership_transferred = True
        except BaseException as exc:
            self._raise_launch_stage_failure(
                created,
                exc,
                code=FAIL_PROCESS_OWNERSHIP,
                message="The suspended process was not confirmed in the exact Job.",
            )

        try:
            identity = api.capture_process_identity(process_handle, pid)
            if not isinstance(identity, ProcessIdentity):
                raise TypeError("Invalid process identity.")
            if identity.pid != pid or not _same_path(identity.executable, application):
                raise ValueError("Process identity does not match selected executable.")
        except BaseException as exc:
            self._raise_launch_stage_failure(
                created,
                exc,
                code=FAIL_PROCESS_IDENTITY,
                message=(
                    "The suspended process identity did not match "
                    "the selected executable."
                ),
            )

        try:
            member_pids = tuple(int(value) for value in api.job_member_pids(job_handle))
            if pid not in member_pids:
                raise ValueError("Created process is not a Job member.")
        except BaseException as exc:
            self._raise_launch_stage_failure(
                created,
                exc,
                code=FAIL_PROCESS_OWNERSHIP,
                message="The suspended process was not confirmed as a Job member.",
            )

        try:
            if not bool(api.resume_thread(thread_handle)):
                raise OSError("ResumeThread failed.")
        except BaseException as exc:
            self._raise_launch_stage_failure(
                created,
                exc,
                code=FAIL_PROCESS_RESUME,
                message="The owned suspended process could not be resumed.",
            )

        if not ownership_transferred:
            self._raise_launch_stage_failure(
                created,
                OSError("Created-process ownership transfer was not recorded."),
                code=FAIL_PROCESS_OWNERSHIP,
                message="Created-process ownership transfer was not recorded.",
            )
        try:
            api.close_handle(thread_handle)
        except BaseException as exc:
            self._raise_launch_stage_failure(
                created,
                exc,
                code=FAIL_CLEANUP,
                message="Owned process thread-handle cleanup failed.",
            )
        self._roots[identity] = process_handle
        self._root_handoff_policies[identity] = selected_policy
        disarm_cleanup = getattr(created, "disarm_cleanup", None)
        if callable(disarm_cleanup):
            disarm_cleanup()
        return identity

del _Phase192Path

__all__ = ['_WindowsJobOwnerLaunch']
