#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Phase184 Win32 ownership implementation section."""

from __future__ import annotations
from pathlib import Path as _Phase192Path
_PHASE192_FACADE_FILE = _Phase192Path(__file__).resolve().parents[1] / "phase184_windows_job_owner.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
from .foundation import *

class _Win32ApiCreate:
    """Compatibility implementation section."""

    def _error(self) -> OSError:
        """Handle the error step."""

        return OSError(int(self.ctypes.get_last_error()))

    def _is_valid_handle(self, handle: object) -> bool:
        """Return whether valid handle."""

        value = int(handle or 0)
        return value not in (0, self._invalid_handle)

    def close_handle(self, handle: int) -> None:
        """Handle the close handle step."""

        if handle:
            self.ctypes.set_last_error(0)
            if not self.kernel32.CloseHandle(handle):
                raise HandleCloseFailure(
                    int(self.ctypes.get_last_error())
                )

    def create_kill_on_close_job(self) -> int:
        """Handle the create kill on close job step."""

        handle = self.kernel32.CreateJobObjectW(None, None)
        if not self._is_valid_handle(handle):
            raise self._error()
        try:
            if not self.kernel32.SetHandleInformation(
                handle,
                self.HANDLE_FLAG_INHERIT,
                0,
            ):
                raise self._error()
            info = self.EXTENDED_LIMIT_INFORMATION()
            info.BasicLimitInformation.LimitFlags = (
                self.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
            )
            if not self.kernel32.SetInformationJobObject(
                handle,
                self.JobObjectExtendedLimitInformation,
                self.ctypes.byref(info),
                self.ctypes.sizeof(info),
            ):
                raise self._error()
            return int(handle)
        except BaseException:
            try:
                self.close_handle(int(handle))
            except BaseException:
                pass
            raise

    def _open_inheritable_file(
        self,
        path: str,
        *,
        access: int,
        disposition: int,
    ) -> int:
        """Handle the open inheritable file step."""

        security = self.SECURITY_ATTRIBUTES()
        security.nLength = self.ctypes.sizeof(security)
        security.bInheritHandle = True
        handle = self.kernel32.CreateFileW(
            path,
            access,
            self.FILE_SHARE_READ | self.FILE_SHARE_WRITE | self.FILE_SHARE_DELETE,
            self.ctypes.byref(security),
            disposition,
            self.FILE_ATTRIBUTE_NORMAL,
            None,
        )
        if not self._is_valid_handle(handle):
            raise self._error()
        return int(handle)

    def _created_process_result(self, process_info) -> _CreatedProcess:
        """Handle the created process result step."""

        return _CreatedProcess(
            pid=int(process_info.dwProcessId),
            process_handle=int(process_info.hProcess),
            thread_handle=int(process_info.hThread),
            _cleanup=lambda: self._cleanup_created_process_info(process_info),
        )

    def _cleanup_created_process_info(self, process_info) -> bool:
        """Handle the cleanup created process info step."""

        process_handle = int(getattr(process_info, "hProcess", 0) or 0)
        thread_handle = int(getattr(process_info, "hThread", 0) or 0)
        if not process_handle and not thread_handle:
            return True

        exited = process_handle <= 0
        handles_closed = True
        if process_handle:
            try:
                self.terminate_process(process_handle)
            except BaseException:
                pass
            try:
                exited = bool(self.wait_process(process_handle, 5.0))
            except BaseException:
                exited = False
        if thread_handle:
            try:
                self.close_handle(thread_handle)
            except BaseException:
                handles_closed = False
        if process_handle:
            try:
                self.close_handle(process_handle)
            except BaseException:
                handles_closed = False
        with contextlib.suppress(BaseException):
            process_info.hThread = 0
        with contextlib.suppress(BaseException):
            process_info.hProcess = 0
        return exited and handles_closed

    def _transfer_created_process_or_cleanup(
        self,
        process_info,
    ) -> _CreatedProcess:
        """Handle the transfer created process or cleanup step."""

        try:
            return self._created_process_result(process_info)
        except BaseException as exc:
            if self._cleanup_created_process_info(process_info):
                raise exc.with_traceback(exc.__traceback__)
            raise OSError("Created-process transfer cleanup failed.") from None

    def create_process_suspended(
        self,
        *,
        application_path: str,
        arguments: tuple[str, ...],
        command_line: str,
        cwd: str,
        environment: Mapping[str, str],
        stdout_log: str,
        stderr_log: str,
    ) -> _CreatedProcess:
        """Handle the create process suspended step."""

        del arguments
        ctypes = self.ctypes
        log_handles: list[int] = []
        attribute_list = None
        attribute_initialized = False
        process_info = self.PROCESS_INFORMATION()
        try:
            pathlib.Path(stdout_log).parent.mkdir(parents=True, exist_ok=True)
            pathlib.Path(stderr_log).parent.mkdir(parents=True, exist_ok=True)
            stdin_handle = self._open_inheritable_file(
                "NUL",
                access=self.GENERIC_READ,
                disposition=self.OPEN_EXISTING,
            )
            log_handles.append(stdin_handle)
            stdout_handle = self._open_inheritable_file(
                stdout_log,
                access=self.GENERIC_WRITE,
                disposition=self.CREATE_ALWAYS,
            )
            log_handles.append(stdout_handle)
            if _same_path(stdout_log, stderr_log):
                stderr_handle = stdout_handle
            else:
                stderr_handle = self._open_inheritable_file(
                    stderr_log,
                    access=self.GENERIC_WRITE,
                    disposition=self.CREATE_ALWAYS,
                )
                log_handles.append(stderr_handle)

            size = ctypes.c_size_t()
            self.kernel32.InitializeProcThreadAttributeList(None, 1, 0, ctypes.byref(size))
            if size.value <= 0:
                raise self._error()
            attribute_list = ctypes.create_string_buffer(size.value)
            if not self.kernel32.InitializeProcThreadAttributeList(
                ctypes.cast(attribute_list, self.wintypes.LPVOID),
                1,
                0,
                ctypes.byref(size),
            ):
                raise self._error()
            attribute_initialized = True

            inherited_values = tuple(dict.fromkeys(log_handles))
            inherited_array_type = self.wintypes.HANDLE * len(inherited_values)
            inherited_array = inherited_array_type(*inherited_values)
            if not self.kernel32.UpdateProcThreadAttribute(
                ctypes.cast(attribute_list, self.wintypes.LPVOID),
                0,
                self.PROC_THREAD_ATTRIBUTE_HANDLE_LIST,
                ctypes.cast(inherited_array, self.wintypes.LPVOID),
                ctypes.sizeof(inherited_array),
                None,
                None,
            ):
                raise self._error()

            startup = self.STARTUPINFOEXW()
            startup.StartupInfo.cb = ctypes.sizeof(startup)
            startup.StartupInfo.dwFlags = self.STARTF_USESTDHANDLES
            startup.StartupInfo.hStdInput = stdin_handle
            startup.StartupInfo.hStdOutput = stdout_handle
            startup.StartupInfo.hStdError = stderr_handle
            startup.lpAttributeList = ctypes.cast(
                attribute_list,
                self.wintypes.LPVOID,
            )

            command_buffer = ctypes.create_unicode_buffer(command_line)
            environment_text = "".join(
                f"{key}={value}\0"
                for key, value in sorted(
                    environment.items(),
                    key=lambda item: item[0].casefold(),
                )
            ) + "\0"
            environment_buffer = ctypes.create_unicode_buffer(environment_text)
            flags = (
                self.CREATE_SUSPENDED
                | self.CREATE_NEW_PROCESS_GROUP
                | self.CREATE_UNICODE_ENVIRONMENT
                | self.EXTENDED_STARTUPINFO_PRESENT
            )
            if not self.kernel32.CreateProcessW(
                application_path,
                command_buffer,
                None,
                None,
                True,
                flags,
                ctypes.cast(environment_buffer, self.wintypes.LPVOID),
                cwd,
                ctypes.cast(ctypes.byref(startup), ctypes.POINTER(self.STARTUPINFOW)),
                ctypes.byref(process_info),
            ):
                raise self._error()
            try:
                return self._transfer_created_process_or_cleanup(process_info)
            except BaseException:
                if int(getattr(process_info, "hProcess", 0) or 0):
                    self._cleanup_created_process_info(process_info)
                raise
        except BaseException as exc:
            if int(getattr(process_info, "hProcess", 0) or 0):
                if not self._cleanup_created_process_info(process_info):
                    raise OSError(
                        "Created-process interruption cleanup failed."
                    ) from None
            raise exc.with_traceback(exc.__traceback__)
        finally:
            if attribute_initialized and attribute_list is not None:
                with contextlib.suppress(BaseException):
                    self.kernel32.DeleteProcThreadAttributeList(
                        self.ctypes.cast(attribute_list, self.wintypes.LPVOID)
                    )
            for handle in tuple(dict.fromkeys(log_handles)):
                with contextlib.suppress(BaseException):
                    self.close_handle(handle)

del _Phase192Path

__all__ = [name for name in globals() if not name.startswith("__")]
