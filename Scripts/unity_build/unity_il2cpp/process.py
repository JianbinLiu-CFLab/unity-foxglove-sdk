#!/usr/bin/env python3
from __future__ import annotations
from .foundation import *
from .logging import *
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
    """Enumerate a POSIX process group with bounded residual diagnostics."""
    try:
        result = subprocess.run(
            ["ps", "-eo", "pid=,pgid="],
            check=False,
            capture_output=True,
            text=True,
            timeout=PROCESS_DIAGNOSTIC_TIMEOUT_SECONDS,
        )
    except (OSError, subprocess.TimeoutExpired):
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


def _posix_descendant_pids(root_pid: int) -> List[int]:
    """Enumerate descendants by authenticated parent links, including setsid escapes."""
    try:
        result = subprocess.run(
            ["ps", "-eo", "pid=,ppid="],
            check=False,
            capture_output=True,
            text=True,
            timeout=PROCESS_DIAGNOSTIC_TIMEOUT_SECONDS,
        )
    except (OSError, subprocess.TimeoutExpired):
        return []
    children: dict[int, list[int]] = {}
    for line in result.stdout.splitlines():
        fields = line.split()
        if len(fields) != 2:
            continue
        try:
            pid, parent = (int(field) for field in fields)
        except ValueError:
            continue
        children.setdefault(parent, []).append(pid)
    pending = list(children.get(root_pid, []))
    descendants: list[int] = []
    while pending:
        pid = pending.pop()
        descendants.append(pid)
        pending.extend(children.get(pid, []))
    return descendants


def _posix_pid_exists(process_id: int) -> bool:
    """Return whether one previously observed POSIX PID still exists."""
    try:
        os.kill(process_id, 0)
    except ProcessLookupError:
        return False
    except PermissionError:
        return True
    return True


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
        self._observed_posix_pids = {process.pid} if posix_process_group_id is not None else set()

    def active_pids(self) -> List[int]:
        """Return the process IDs still owned by this invocation."""
        if self._windows_job is not None:
            return self._windows_job.active_pids()
        if self._posix_process_group_id is not None:
            # Discover descendants while the root is alive. Once it exits,
            # never trust a recycled numeric PGID; use only authenticated PIDs
            # observed during this invocation.
            if self.process.poll() is None:
                self._observed_posix_pids.update(_posix_descendant_pids(self.process.pid))
                self._observed_posix_pids.update(
                    _posix_process_group_pids(self._posix_process_group_id)
                )
            return sorted(
                pid for pid in self._observed_posix_pids if _posix_pid_exists(pid)
            )
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
            if self.process.poll() is None:
                try:
                    os.killpg(self._posix_process_group_id, signal.SIGKILL)
                except ProcessLookupError:
                    pass
            else:
                # The group number may have been reused after root exit. Kill
                # authenticated PIDs observed while this invocation owned the
                # root, plus any descendants still present in that group.
                owned_pids = set(self._observed_posix_pids)
                if _posix_process_group_exists(self._posix_process_group_id):
                    owned_pids.update(_posix_process_group_pids(self._posix_process_group_id))
                for process_id in owned_pids:
                    try:
                        os.kill(process_id, signal.SIGKILL)
                    except (OSError, ProcessLookupError):
                        pass
            self._posix_process_group_id = None


def start_owned_process(cmd: List[str], root: Path, **popen_kwargs) -> OwnedProcessTree:
    """Start Unity inside a platform-owned process tree."""
    if os.name == "nt":
        job = _WindowsKillOnCloseJob()
        process = None
        try:
            process = subprocess.Popen(
                cmd,
                cwd=root,
                creationflags=subprocess.CREATE_NEW_PROCESS_GROUP | WINDOWS_CREATE_SUSPENDED,
                **popen_kwargs,
            )
            job.assign(process.pid)
            _resume_suspended_windows_process(process.pid)
            return OwnedProcessTree(process, windows_job=job)
        except BaseException:
            if process is not None:
                try:
                    process.kill()
                    process.wait(timeout=UNITY_TERMINATION_WAIT_SECONDS)
                except (OSError, subprocess.TimeoutExpired):
                    pass
            job.close()
            raise

    process = None
    try:
        process = subprocess.Popen(cmd, cwd=root, start_new_session=True, **popen_kwargs)
        return OwnedProcessTree(process, posix_process_group_id=process.pid)
    except BaseException:
        if process is not None:
            try:
                os.killpg(process.pid, signal.SIGKILL)
            except (OSError, ProcessLookupError):
                pass
            try:
                process.wait(timeout=UNITY_TERMINATION_WAIT_SECONDS)
            except (OSError, subprocess.TimeoutExpired):
                pass
        raise


def await_tree_quiescence(process_tree: OwnedProcessTree, deadline_seconds: float) -> List[int]:
    """Wait for an owned tree to drain and return PIDs that miss the quiescence bound.

    Ownership alone does not prove the descendants were absent when the root
    process exited. Releasing the job or process group would force any survivor
    to die, so quiescence has to be decided before ownership is released.
    """
    deadline = time.monotonic() + deadline_seconds
    while True:
        residual_pids = process_tree.active_pids()
        if not residual_pids:
            return []
        if time.monotonic() >= deadline:
            return residual_pids
        time.sleep(PROCESS_TREE_POLL_SECONDS)


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
    bounded_interval = min(max(interval, MIN_PROGRESS_INTERVAL_SECONDS), MAX_PROGRESS_INTERVAL_SECONDS)
    next_heartbeat = started + float(bounded_interval)
    timeout_seconds = timeout_minutes * SECONDS_PER_MINUTE if timeout_minutes > 0 else None
    log_state = _LogTailState()
    offset = log_state.seed(log_path)

    process_tree = start_owned_process(cmd, root)
    process = process_tree.process
    try:
        while True:
            offset, lines = read_new_important_lines(log_path, offset, state=log_state)
            for line in lines:
                print(f"[unity-log] {line}", flush=True)

            # Observe descendants before polling root completion so an escaped
            # child remains authenticated for post-exit cleanup.
            if process.poll() is None:
                process_tree.active_pids()
            returncode = process.poll()
            now = time.monotonic()
            if returncode is not None:
                break

            if timeout_seconds is not None and now - started >= timeout_seconds:
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
                next_heartbeat = now + float(bounded_interval)

            time.sleep(LOG_POLL_SLEEP_SECONDS)

        residual_pids = await_tree_quiescence(process_tree, UNITY_TERMINATION_WAIT_SECONDS)
    finally:
        try:
            process_tree.close()
        finally:
            finish_log_tail(log_path, offset, log_state)

    elapsed = format_elapsed(time.monotonic() - started)
    print(f"[build_unity_il2cpp] Unity exited after {elapsed}.", flush=True)

    if residual_pids:
        print(
            "[build_unity_il2cpp] Owned process tree did not quiesce after Unity exited; "
            f"residual PIDs: {', '.join(str(pid) for pid in residual_pids)}",
            file=sys.stderr,
            flush=True,
        )
        if returncode == EXIT_SUCCESS:
            return EXIT_PREFLIGHT_FAILURE

    return returncode

__all__ = ['_WindowsKillOnCloseJob', '_resume_suspended_windows_process', '_posix_process_group_exists', '_posix_process_group_pids', '_posix_descendant_pids', '_posix_pid_exists', 'OwnedProcessTree', 'start_owned_process', 'await_tree_quiescence', 'terminate_process', 'run_with_progress']
