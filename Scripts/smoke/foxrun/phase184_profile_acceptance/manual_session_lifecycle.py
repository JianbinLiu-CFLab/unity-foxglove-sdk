from __future__ import annotations
from .cleanup_evidence_and_summary import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase184_profile_acceptance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
def default_unity_editor_log_path() -> pathlib.Path:
    """Return the interactive Editor log without relying on a shell profile."""

    local_app_data = os.environ.get("LOCALAPPDATA")
    if local_app_data:
        return pathlib.Path(local_app_data) / "Unity" / "Editor" / "Editor.log"
    return pathlib.Path.home() / "AppData" / "Local" / "Unity" / "Editor" / "Editor.log"


class EditorLogMirror:
    """Mirror only post-capture Editor output plus current-token rescue markers."""

    _MAX_SOURCE_BYTES = 64 * 1024 * 1024
    _RESCUE_SCAN_INTERVAL_SECONDS = 1.0
    _ACCESS_FAILURE_GRACE_SECONDS = 5.0

    def __init__(
        self,
        source: pathlib.Path,
        destination: pathlib.Path,
        token: str,
    ) -> None:
        """Initialize the editor log mirror."""

        self._source = pathlib.Path(source)
        self._destination = pathlib.Path(destination)
        self._token = token
        self._identity: tuple[int, int] | None = None
        self._offset = 0
        self._seen_token_lines: set[str] = set()
        self._next_rescue_scan = 0.0
        self._access_failure_started: dict[str, float] = {}

    @staticmethod
    def _stat_identity(stat: os.stat_result) -> tuple[int, int]:
        """Handle the stat identity step."""

        return int(stat.st_dev), int(stat.st_ino)

    def capture(self) -> None:
        """Capture the current file identity/EOF and reset the owned mirror."""

        self._destination.parent.mkdir(parents=True, exist_ok=True)
        self._destination.write_text("", encoding="utf-8")
        self._next_rescue_scan = 0.0
        try:
            stat = self._source.stat()
        except FileNotFoundError:
            self._identity = None
            self._offset = 0
            return
        self._identity = self._stat_identity(stat)
        self._offset = int(stat.st_size)

    def _append(self, text: str) -> None:
        """Handle the append step."""

        if not text:
            return
        with self._destination.open("a", encoding="utf-8", newline="") as stream:
            stream.write(text)
            stream.flush()

    def _defer_access_failure(
        self,
        operation: str,
        diagnostic: str,
        failure: OSError,
    ) -> None:
        """Retry transient Windows file contention but keep a bounded terminal."""

        now = time.monotonic()
        started = self._access_failure_started.get(operation)
        if started is None:
            self._access_failure_started[operation] = now
            return
        if now - started < self._ACCESS_FAILURE_GRACE_SECONDS:
            return
        raise AcceptanceFailure(
            "FAIL_TERMINAL",
            diagnostic
            + " after a bounded retry window ("
            + type(failure).__name__
            + ").",
        ) from failure

    def _clear_access_failure(self, operation: str) -> None:
        """Reset one retry clock only after that source operation succeeds."""

        self._access_failure_started.pop(operation, None)

    def poll(self) -> None:
        """Copy fresh bytes, resetting safely after truncation or replacement."""

        try:
            stat = self._source.stat()
        except FileNotFoundError as exc:
            if self._identity is not None:
                self._defer_access_failure(
                    "mirror",
                    "The interactive Unity Editor log could not be mirrored",
                    exc,
                )
            return
        except OSError as exc:
            self._defer_access_failure(
                "mirror",
                "The interactive Unity Editor log could not be mirrored",
                exc,
            )
            return
        identity = self._stat_identity(stat)
        if (
            self._identity is None
            or identity != self._identity
            or int(stat.st_size) < self._offset
        ):
            self._identity = identity
            self._offset = 0

        try:
            with self._source.open("rb") as stream:
                stream.seek(self._offset)
                appended = stream.read()
                self._offset = stream.tell()
        except OSError as exc:
            self._defer_access_failure(
                "mirror",
                "The interactive Unity Editor log could not be mirrored",
                exc,
            )
            return
        self._clear_access_failure("mirror")

        text = appended.decode("utf-8", errors="replace")
        self._append(text)
        token_field = "token=" + self._token
        for line in text.splitlines():
            if token_field in line:
                self._seen_token_lines.add(line)

        # Unity can reuse Editor.log storage below an apparent EOF. A unique
        # current-run token makes a bounded full-file marker rescue unambiguous.
        if stat.st_size > self._MAX_SOURCE_BYTES:
            # Historical log volume is not a current-run failure. Fresh bytes
            # above the captured offset were already mirrored, while a
            # truncation or replacement resets the offset. Skip only the
            # optional full-file rescue when that scan would be unbounded.
            return
        now = time.monotonic()
        if now < self._next_rescue_scan:
            return
        self._next_rescue_scan = now + self._RESCUE_SCAN_INTERVAL_SECONDS
        try:
            current = self._source.read_text(
                encoding="utf-8",
                errors="replace",
            )
        except OSError as exc:
            self._defer_access_failure(
                "rescan",
                "The interactive Unity Editor log could not be rescanned",
                exc,
            )
            return
        self._clear_access_failure("rescan")
        rescued: list[str] = []
        for line in current.splitlines():
            if token_field not in line or line in self._seen_token_lines:
                continue
            self._seen_token_lines.add(line)
            rescued.append(line)
        if rescued:
            self._append("\n".join(rescued) + "\n")


def _process_creation_unix_seconds(pid: int) -> float | None:
    """Read one process creation time without mutating or opening broad state."""

    if pid <= 0:
        return None
    if os.name == "nt":
        class FileTime(ctypes.Structure):
            """Represent the file time contract."""

            _fields_ = [
                ("low", ctypes.c_uint32),
                ("high", ctypes.c_uint32),
            ]

        kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
        open_process = kernel32.OpenProcess
        open_process.argtypes = [ctypes.c_uint32, ctypes.c_int, ctypes.c_uint32]
        open_process.restype = ctypes.c_void_p
        get_times = kernel32.GetProcessTimes
        get_times.argtypes = [
            ctypes.c_void_p,
            ctypes.POINTER(FileTime),
            ctypes.POINTER(FileTime),
            ctypes.POINTER(FileTime),
            ctypes.POINTER(FileTime),
        ]
        get_times.restype = ctypes.c_int
        close_handle = kernel32.CloseHandle
        close_handle.argtypes = [ctypes.c_void_p]
        close_handle.restype = ctypes.c_int
        handle = open_process(0x1000, 0, int(pid))
        if not handle:
            return None
        creation = FileTime()
        exit_time = FileTime()
        kernel_time = FileTime()
        user_time = FileTime()
        try:
            if not get_times(
                handle,
                ctypes.byref(creation),
                ctypes.byref(exit_time),
                ctypes.byref(kernel_time),
                ctypes.byref(user_time),
            ):
                return None
        finally:
            close_handle(handle)
        ticks = (int(creation.high) << 32) | int(creation.low)
        return ticks / 10_000_000.0 - 11_644_473_600.0
    if pid == os.getpid():
        return _PROCESS_IMPORTED_UNIX_SECONDS
    try:
        import psutil

        return float(psutil.Process(pid).create_time())
    except (ImportError, OSError, ValueError):
        return None


def _write_manual_pointer(
    pointer: pathlib.Path,
    run_config: pathlib.Path,
    token: str,
    *,
    helper_pid: int,
    helper_created: float,
    expires_utc: dt.datetime,
) -> None:
    """Publish the one bounded private pointer consumed by interactive Unity."""

    expiry = expires_utc.astimezone(dt.timezone.utc).isoformat().replace("+00:00", "Z")
    write_private_json_atomic(
        pointer,
        {
            "runConfig": str(pathlib.Path(run_config).resolve()),
            "token": token,
            "helperPid": int(helper_pid),
            "helperCreationUnixSeconds": float(helper_created),
            "expiresUtc": expiry,
        },
    )


def _remove_manual_pointer_if_owned(
    pointer: pathlib.Path,
    token: str,
    helper_pid: int,
) -> bool:
    """Remove only the exact pointer written by this helper identity."""

    try:
        value = json.loads(pathlib.Path(pointer).read_text(encoding="utf-8"))
    except (FileNotFoundError, UnicodeError, json.JSONDecodeError, OSError):
        return False
    if (
        not isinstance(value, Mapping)
        or value.get("token") != token
        or value.get("helperPid") != int(helper_pid)
    ):
        return False
    try:
        pathlib.Path(pointer).unlink()
        return True
    except FileNotFoundError:
        return True
    except OSError:
        return False


def _recover_abandoned_manual_pointer(pointer: pathlib.Path) -> None:
    """Clear only a malformed or demonstrably dead prior helper pointer."""

    path = pathlib.Path(pointer)
    if not path.is_file():
        return
    try:
        if path.stat().st_size <= 0 or path.stat().st_size > MAX_CONFIG_BYTES:
            raise ValueError("pointer size")
        value = json.loads(path.read_text(encoding="utf-8"))
        pid = int(value["helperPid"])
        created = float(value["helperCreationUnixSeconds"])
    except (OSError, UnicodeError, json.JSONDecodeError, KeyError, TypeError, ValueError):
        with contextlib.suppress(FileNotFoundError):
            path.unlink()
        return
    actual = _process_creation_unix_seconds(pid)
    if actual is not None and abs(actual - created) <= 2.0:
        raise AcceptanceFailure(
            "FAIL_PREFLIGHT",
            "Another live Phase184 manual helper already owns manual-active.json.",
        )
    try:
        path.unlink()
    except OSError as exc:
        raise AcceptanceFailure(
            "FAIL_CLEANUP",
            "An abandoned Phase184 manual pointer could not be removed.",
        ) from exc


def _manual_exit_seen(config: Mapping[str, object]) -> bool:
    """Handle the manual exit seen step."""

    case = str(config["case"])
    token = str(config["token"])
    for line in read_log_lines(pathlib.Path(str(config["unityLog"]))):
        if "PHASE184G_MANUAL_PLAY_EXITED" not in line:
            continue
        fields = _parse_marker_fields(line)
        if fields.get("case") == case and fields.get("token") == token:
            return True
    return False


def _wait_for_manual_session(
    config: Mapping[str, object],
    mirror: EditorLogMirror,
    owner: OwnedProcessSet,
    worker_roles: Iterable[str],
) -> TerminalMarker:
    """Wait for current-token Play entry, route proof, and user-owned Play exit."""

    entry_deadline = time.monotonic() + MANUAL_ENTRY_TIMEOUT_SECONDS
    review_deadline: float | None = None
    terminal: TerminalMarker | None = None
    context_ready = False
    announced_terminal = False
    while True:
        mirror.poll()
        lines = read_log_lines(pathlib.Path(str(config["unityLog"])))
        case = str(config["case"])
        token = str(config["token"])
        if not context_ready:
            for line in lines:
                if "PHASE184G_CONTEXT_READY" not in line:
                    continue
                fields = _parse_marker_fields(line)
                if fields.get("case") == case and fields.get("token") == token:
                    context_ready = True
                    review_deadline = time.monotonic() + MANUAL_REVIEW_TIMEOUT_SECONDS
                    break
        observed_terminal = find_terminal_marker(lines, case, token)
        if observed_terminal is not None:
            terminal = observed_terminal
        if terminal is not None and terminal.verdict == "FAIL":
            raise AcceptanceFailure(
                "FAIL_TERMINAL",
                "The interactive Unity route reported a correlated failure.",
            )
        exited = _manual_exit_seen(config)
        if exited and terminal is None:
            raise AcceptanceFailure(
                "FAIL_MANUAL_STOPPED_EARLY",
                "Play Mode exited before the correlated route proof completed.",
            )
        if terminal is not None and not announced_terminal:
            announced_terminal = True
            print(
                "[phase184] Automated route evidence is complete. "
                "Finish the visible checklist, then exit Play Mode.",
                flush=True,
            )
        if terminal is not None and exited:
            return terminal

        for role in worker_roles:
            process = owner.process(role)
            if process is None or process.poll() is None:
                continue
            result_path = _actor_path(config, "resultFiles", role)
            if result_path.is_file():
                require_finished_actor_pass(config, role)
                continue
            raise AcceptanceFailure(
                "FAIL_PROCESS_EXIT",
                f"{role} exited before current manual evidence.",
            )

        now = time.monotonic()
        if not context_ready and now >= entry_deadline:
            raise AcceptanceFailure(
                "FAIL_UNITY_STARTUP",
                "The user-owned Editor did not enter the current Play session within 900 seconds.",
            )
        if context_ready and review_deadline is not None and now >= review_deadline:
            raise AcceptanceFailure(
                "FAIL_TERMINAL",
                "The current manual Play session did not finish within its review window.",
            )
        time.sleep(0.1)




__all__ = [name for name in globals() if not name.startswith("__")]
