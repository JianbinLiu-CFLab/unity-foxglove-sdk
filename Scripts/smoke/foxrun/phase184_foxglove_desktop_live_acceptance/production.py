from __future__ import annotations
from .summary import *
@dataclasses.dataclass(frozen=True)
class IdentityExitVerification:
    """Independent post-Job proof for every captured PID-safe identity."""

    exited: tuple[job_owner.ProcessIdentity, ...]
    residual: tuple[job_owner.ProcessIdentity, ...]

    def __post_init__(self) -> None:
        """Validate the identity exit verification invariants."""

        if not isinstance(self.exited, tuple) or not isinstance(
            self.residual,
            tuple,
        ):
            raise TypeError("Identity exit verification must use tuples.")
        exited_keys = {
            _process_identity_key(identity)
            for identity in self.exited
        }
        residual_keys = {
            _process_identity_key(identity)
            for identity in self.residual
        }
        if (
            len(exited_keys) != len(self.exited)
            or len(residual_keys) != len(self.residual)
            or exited_keys & residual_keys
            or len(exited_keys | residual_keys) > MAX_SUMMARY_IDENTITIES
        ):
            raise ValueError("Identity exit verification is contradictory.")


@dataclasses.dataclass(frozen=True)
class CoordinatorDependencies:
    """Injected external-state boundary for pure coordinator regression tests."""

    repository_root: pathlib.Path
    platform_name: str
    environment: Mapping[str, str]
    clock: Callable[[], float]
    sleep: Callable[[float], None]
    utc_now: Callable[[], dt.datetime]
    nonce: Callable[[], str]
    is_file: Callable[[pathlib.Path], bool]
    path_exists: Callable[[pathlib.Path], bool]
    make_directory: Callable[[pathlib.Path], None]
    verify_cli: Callable[
        [os.PathLike[str] | str, os.PathLike[str] | str],
        cli_install.VerifiedCliIdentity,
    ]
    sha256_file: Callable[[os.PathLike[str] | str], str]
    desktop_executable_lease_factory: Callable[[pathlib.Path], Any]
    read_desktop_file_version: Callable[[pathlib.Path], str]
    read_uri_handler_command: Callable[[], str]
    parse_windows_command_line: Callable[[object], tuple[str, ...]]
    read_repository_head: Callable[[pathlib.Path], str]
    read_windows_version: Callable[[], str]
    reserve_port: Callable[[int | None], Any]
    port_is_bindable: Callable[[str, int], bool]
    job_owner_factory: Callable[[pathlib.Path], Any]
    verify_identities_exited: Callable[
        [Sequence[job_owner.ProcessIdentity], float],
        IdentityExitVerification,
    ]
    read_log_lines: Callable[[pathlib.Path, int], Sequence[str]]
    coordinator_logs_within_bound: Callable[
        [Sequence[pathlib.Path], int],
        bool,
    ]
    load_json_snapshot: Callable[[pathlib.Path, int], object]
    write_json_atomic: Callable[..., None]
    remove_owned_file: Callable[[pathlib.Path], bool]


class _LoopbackPortReservation:
    """One exclusive 127.0.0.1 bind held until the owned child launch."""

    def __init__(self, requested_port: int | None):
        """Initialize the loopback port reservation."""

        if requested_port is not None:
            requested_port = _validate_port(requested_port)
        selected = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        try:
            if hasattr(socket, "SO_EXCLUSIVEADDRUSE"):
                selected.setsockopt(
                    socket.SOL_SOCKET,
                    socket.SO_EXCLUSIVEADDRUSE,
                    1,
                )
            selected.bind((LOOPBACK_HOST, requested_port or 0))
            host, port = selected.getsockname()[:2]
            if host != LOOPBACK_HOST:
                raise OSError("Loopback reservation drifted.")
            self.port = _validate_port(int(port))
            self._socket: socket.socket | None = selected
        except BaseException:
            selected.close()
            raise

    def release(self) -> None:
        """Release the owned reservation."""

        selected = self._socket
        self._socket = None
        if selected is not None:
            selected.close()


def _reserve_port_production(requested_port: int | None) -> _LoopbackPortReservation:
    """Handle the reserve port production step."""

    try:
        return _LoopbackPortReservation(requested_port)
    except protocol.AcceptanceFailure:
        raise
    except Exception:
        _raise(
            protocol.FAIL_DESKTOP_PREFLIGHT,
            "Exclusive loopback Foxglove port reservation failed.",
        )


def _port_is_bindable_production(host: str, port: int) -> bool:
    """Handle the port is bindable production step."""

    if host != LOOPBACK_HOST:
        return False
    selected: socket.socket | None = None
    try:
        selected = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        if hasattr(socket, "SO_EXCLUSIVEADDRUSE"):
            selected.setsockopt(
                socket.SOL_SOCKET,
                socket.SO_EXCLUSIVEADDRUSE,
                1,
            )
        selected.bind((host, _validate_port(port)))
        return True
    except Exception:
        return False
    finally:
        if selected is not None:
            with contextlib.suppress(Exception):
                selected.close()


class _FILETIME(ctypes.Structure):
    """Mirror the Win32 filetime structure."""

    _fields_ = (
        ("dwLowDateTime", wintypes.DWORD),
        ("dwHighDateTime", wintypes.DWORD),
    )


def _filetime_value(value: _FILETIME) -> int:
    """Handle the filetime value step."""

    return (
        int(value.dwHighDateTime) << 32
    ) | int(value.dwLowDateTime)


def _identity_still_live_production(
    identity: job_owner.ProcessIdentity,
) -> bool | None:
    """Return live, exited/reused, or unproven without consulting a Job."""

    if os.name != "nt":
        return None
    process_query_limited_information = 0x1000
    synchronize = 0x00100000
    wait_object_0 = 0x00000000
    wait_timeout = 0x00000102
    error_invalid_parameter = 87

    try:
        kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
        open_process = kernel32.OpenProcess
        open_process.argtypes = [
            wintypes.DWORD,
            wintypes.BOOL,
            wintypes.DWORD,
        ]
        open_process.restype = wintypes.HANDLE
        get_process_times = kernel32.GetProcessTimes
        get_process_times.argtypes = [
            wintypes.HANDLE,
            ctypes.POINTER(_FILETIME),
            ctypes.POINTER(_FILETIME),
            ctypes.POINTER(_FILETIME),
            ctypes.POINTER(_FILETIME),
        ]
        get_process_times.restype = wintypes.BOOL
        query_image = kernel32.QueryFullProcessImageNameW
        query_image.argtypes = [
            wintypes.HANDLE,
            wintypes.DWORD,
            wintypes.LPWSTR,
            ctypes.POINTER(wintypes.DWORD),
        ]
        query_image.restype = wintypes.BOOL
        wait_for_single = kernel32.WaitForSingleObject
        wait_for_single.argtypes = [wintypes.HANDLE, wintypes.DWORD]
        wait_for_single.restype = wintypes.DWORD
        close_handle = kernel32.CloseHandle
        close_handle.argtypes = [wintypes.HANDLE]
        close_handle.restype = wintypes.BOOL
    except Exception:
        return None

    handle = open_process(
        process_query_limited_information | synchronize,
        False,
        identity.pid,
    )
    if not handle:
        return (
            False
            if ctypes.get_last_error() == error_invalid_parameter
            else None
        )

    observation: bool | None = None
    try:
        creation = _FILETIME()
        exit_time = _FILETIME()
        kernel_time = _FILETIME()
        user_time = _FILETIME()
        if not get_process_times(
            handle,
            ctypes.byref(creation),
            ctypes.byref(exit_time),
            ctypes.byref(kernel_time),
            ctypes.byref(user_time),
        ):
            return None
        image = ctypes.create_unicode_buffer(32_768)
        image_length = wintypes.DWORD(len(image))
        if not query_image(
            handle,
            0,
            image,
            ctypes.byref(image_length),
        ):
            return None
        current_creation = _filetime_value(creation)
        current_executable = image.value
        if (
            current_creation != identity.creation_time_100ns
            or not protocol.windows_paths_equal(
                current_executable,
                identity.executable,
            )
        ):
            observation = False
        else:
            wait_result = int(wait_for_single(handle, 0))
            if wait_result == wait_object_0:
                observation = False
            elif wait_result == wait_timeout:
                observation = True
    except Exception:
        observation = None
    finally:
        if not close_handle(handle):
            observation = None
    return observation


def _verify_identities_exited_production(
    identities: Sequence[job_owner.ProcessIdentity],
    timeout_seconds: float,
) -> IdentityExitVerification:
    """Boundedly prove captured identities exited after the Job was closed."""

    if (
        isinstance(identities, (str, bytes))
        or len(identities) > MAX_SUMMARY_IDENTITIES
        or isinstance(timeout_seconds, bool)
        or not isinstance(timeout_seconds, (int, float))
        or not math.isfinite(float(timeout_seconds))
        or float(timeout_seconds) < 0
        or float(timeout_seconds) > 60.0
    ):
        raise ValueError("Post-Job identity verification inputs are invalid.")
    frozen = tuple(identities)
    keys = [_process_identity_key(identity) for identity in frozen]
    if len(set(keys)) != len(keys):
        raise ValueError("Post-Job identity verification contains duplicates.")

    pending = dict(zip(keys, frozen, strict=True))
    exited_keys: set[tuple[int, int, str]] = set()
    deadline = time.monotonic() + float(timeout_seconds)
    while pending:
        for key, identity in tuple(pending.items()):
            observation = _identity_still_live_production(identity)
            if observation is False:
                exited_keys.add(key)
                del pending[key]
        if not pending or time.monotonic() >= deadline:
            break
        time.sleep(
            min(
                0.05,
                max(0.0, deadline - time.monotonic()),
            )
        )
    return IdentityExitVerification(
        exited=tuple(
            identity
            for key, identity in zip(keys, frozen, strict=True)
            if key in exited_keys
        ),
        residual=tuple(
            identity
            for key, identity in zip(keys, frozen, strict=True)
            if key in pending
        ),
    )


def _read_desktop_file_version_production(path: pathlib.Path) -> str:
    """Read the selected executable's fixed Windows version resource."""

    class VS_FIXEDFILEINFO(ctypes.Structure):
        """Represent the vs fixedfileinfo contract."""

        _fields_ = [
            ("dwSignature", ctypes.c_uint32),
            ("dwStrucVersion", ctypes.c_uint32),
            ("dwFileVersionMS", ctypes.c_uint32),
            ("dwFileVersionLS", ctypes.c_uint32),
            ("dwProductVersionMS", ctypes.c_uint32),
            ("dwProductVersionLS", ctypes.c_uint32),
            ("dwFileFlagsMask", ctypes.c_uint32),
            ("dwFileFlags", ctypes.c_uint32),
            ("dwFileOS", ctypes.c_uint32),
            ("dwFileType", ctypes.c_uint32),
            ("dwFileSubtype", ctypes.c_uint32),
            ("dwFileDateMS", ctypes.c_uint32),
            ("dwFileDateLS", ctypes.c_uint32),
        ]

    try:
        version = ctypes.WinDLL("version", use_last_error=True)
        get_size = version.GetFileVersionInfoSizeW
        get_size.argtypes = [ctypes.c_wchar_p, ctypes.POINTER(ctypes.c_uint32)]
        get_size.restype = ctypes.c_uint32
        get_info = version.GetFileVersionInfoW
        get_info.argtypes = [
            ctypes.c_wchar_p,
            ctypes.c_uint32,
            ctypes.c_uint32,
            ctypes.c_void_p,
        ]
        get_info.restype = ctypes.c_int
        query = version.VerQueryValueW
        query.argtypes = [
            ctypes.c_void_p,
            ctypes.c_wchar_p,
            ctypes.POINTER(ctypes.c_void_p),
            ctypes.POINTER(ctypes.c_uint32),
        ]
        query.restype = ctypes.c_int

        ignored = ctypes.c_uint32()
        size = int(get_size(str(path), ctypes.byref(ignored)))
        if size <= 0 or size > 16 * 1024 * 1024:
            raise OSError("Invalid version-resource size.")
        buffer = ctypes.create_string_buffer(size)
        if not get_info(str(path), 0, size, buffer):
            raise OSError("GetFileVersionInfoW failed.")
        pointer = ctypes.c_void_p()
        length = ctypes.c_uint32()
        if not query(
            buffer,
            "\\",
            ctypes.byref(pointer),
            ctypes.byref(length),
        ):
            raise OSError("VerQueryValueW failed.")
        if length.value < ctypes.sizeof(VS_FIXEDFILEINFO):
            raise OSError("Fixed version resource is truncated.")
        fixed = ctypes.cast(
            pointer,
            ctypes.POINTER(VS_FIXEDFILEINFO),
        ).contents
        if fixed.dwSignature != 0xFEEF04BD:
            raise OSError("Fixed version signature is invalid.")
        return ".".join(
            str(value)
            for value in (
                fixed.dwFileVersionMS >> 16,
                fixed.dwFileVersionMS & 0xFFFF,
                fixed.dwFileVersionLS >> 16,
                fixed.dwFileVersionLS & 0xFFFF,
            )
        )
    except Exception:
        _raise(
            protocol.FAIL_DESKTOP_PREFLIGHT,
            "Foxglove Desktop file version could not be read.",
        )


def _read_uri_handler_command_production() -> str:
    """Read URI handler command production."""

    try:
        import winreg

        with winreg.OpenKey(
            winreg.HKEY_CLASSES_ROOT,
            r"foxglove\shell\open\command",
            0,
            winreg.KEY_READ,
        ) as key:
            value, value_type = winreg.QueryValueEx(key, None)
        if value_type not in (winreg.REG_SZ, winreg.REG_EXPAND_SZ):
            raise OSError("URI-handler registry value has another type.")
        if value_type == winreg.REG_EXPAND_SZ:
            value = os.path.expandvars(value)
        if not isinstance(value, str):
            raise OSError("URI-handler registry value is not text.")
        return value
    except Exception:
        _raise(
            protocol.FAIL_DESKTOP_PREFLIGHT,
            "Merged HKCR Foxglove URI handler could not be read.",
        )


def _read_repository_head_production(repository: pathlib.Path) -> str:
    """Read repository head production."""

    try:
        completed = subprocess.run(
            ["git", "rev-parse", "HEAD"],
            cwd=repository,
            stdin=subprocess.DEVNULL,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            timeout=30,
            check=False,
            shell=False,
        )
        if (
            completed.returncode != 0
            or len(completed.stdout) > 256
            or len(completed.stderr) > 4096
        ):
            raise OSError("git rev-parse failed.")
        head = completed.stdout.decode("ascii").strip().casefold()
        if _LOWER_GIT_OBJECT.fullmatch(head) is None:
            raise OSError("git object identity is malformed.")
        return head
    except Exception:
        _raise(
            protocol.FAIL_DESKTOP_PREFLIGHT,
            "Repository HEAD could not be captured.",
        )


def _read_windows_version_production() -> str:
    """Read windows version production."""

    value = platform.platform(aliased=False, terse=False)
    if not value:
        _raise(
            protocol.FAIL_DESKTOP_PREFLIGHT,
            "Windows version identity could not be captured.",
        )
    return value


class _DuplicateJsonKey(ValueError):
    """Represent the duplicate JSON key contract."""

    pass


def _unique_json_object(
    pairs: list[tuple[str, object]],
) -> dict[str, object]:
    """Handle the unique JSON object step."""

    result: dict[str, object] = {}
    for key, value in pairs:
        if key in result:
            raise _DuplicateJsonKey("Duplicate JSON key.")
        result[key] = value
    return result


def _same_file_snapshot(left: os.stat_result, right: os.stat_result) -> bool:
    """Handle the same file snapshot step."""

    return (
        left.st_dev,
        left.st_ino,
        left.st_size,
        left.st_mtime_ns,
    ) == (
        right.st_dev,
        right.st_ino,
        right.st_size,
        right.st_mtime_ns,
    )


def _read_json_snapshot_production(
    path: pathlib.Path,
    max_bytes: int,
) -> object:
    """Read JSON snapshot production."""

    if (
        isinstance(max_bytes, bool)
        or not isinstance(max_bytes, int)
        or max_bytes < 1
        or max_bytes > MAX_RUN_CONFIG_BYTES
    ):
        raise ValueError("JSON snapshot bound is invalid.")
    target = pathlib.Path(path)
    before = target.lstat()
    if (
        not stat.S_ISREG(before.st_mode)
        or stat.S_ISLNK(before.st_mode)
        or bool(
            getattr(before, "st_file_attributes", 0)
            & getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 0x400)
        )
    ):
        raise OSError("JSON snapshot is not a plain regular file.")
    with target.open("rb") as stream:
        opened = os.fstat(stream.fileno())
        raw = stream.read(max_bytes + 1)
    after = target.lstat()
    if (
        not _same_file_snapshot(before, opened)
        or not _same_file_snapshot(opened, after)
        or not raw
        or len(raw) > max_bytes
    ):
        raise OSError("JSON snapshot changed or exceeded its bound.")
    return json.loads(
        raw.decode("utf-8"),
        object_pairs_hook=_unique_json_object,
    )


def _read_log_lines_production(
    path: pathlib.Path,
    max_bytes: int,
) -> tuple[str, ...]:
    """Read log lines production."""

    if max_bytes != MAX_UNITY_LOG_BYTES:
        raise ValueError("Unity log bound drifted.")
    target = pathlib.Path(path)
    try:
        with target.open("rb") as stream:
            raw = stream.read(max_bytes + 1)
    except FileNotFoundError:
        return ()
    if len(raw) > max_bytes:
        _raise(
            protocol.FAIL_DESKTOP_CONNECTION,
            "Unity log exceeded the Desktop-live bound.",
        )
    return tuple(
        raw.decode("utf-8", errors="replace").splitlines()
    )


def _coordinator_logs_within_bound_production(
    paths: Sequence[pathlib.Path],
    max_bytes: int,
) -> bool:
    """Handle the coordinator logs within bound production step."""

    if (
        isinstance(paths, (str, bytes))
        or max_bytes != MAX_COORDINATOR_LOG_BYTES
    ):
        return False
    for value in paths:
        target = pathlib.Path(value)
        try:
            info = target.lstat()
        except FileNotFoundError:
            continue
        except OSError:
            return False
        if (
            not stat.S_ISREG(info.st_mode)
            or stat.S_ISLNK(info.st_mode)
            or bool(
                getattr(info, "st_file_attributes", 0)
                & getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 0x400)
            )
            or info.st_size > max_bytes
        ):
            return False
    return True


def _write_json_atomic_production(
    path: pathlib.Path,
    payload: Mapping[str, Any],
    *,
    max_bytes: int,
) -> None:
    """Write JSON atomic production."""

    protocol.write_json_atomic(
        path,
        payload,
        max_bytes=max_bytes,
    )


def _remove_owned_file_production(path: pathlib.Path) -> bool:
    """Remove owned file production."""

    target = pathlib.Path(path)
    try:
        target.unlink()
    except FileNotFoundError:
        pass
    return not target.exists()


def _default_dependencies() -> CoordinatorDependencies:
    """Handle the default dependencies step."""

    return CoordinatorDependencies(
        repository_root=REPOSITORY_ROOT,
        platform_name=os.name,
        environment=dict(os.environ),
        clock=time.monotonic,
        sleep=time.sleep,
        utc_now=lambda: dt.datetime.now(dt.timezone.utc),
        nonce=lambda: uuid.uuid4().hex[:10],
        is_file=lambda path: pathlib.Path(path).is_file(),
        path_exists=lambda path: pathlib.Path(path).exists(),
        make_directory=lambda path: pathlib.Path(path).mkdir(
            parents=True,
            exist_ok=False,
        ),
        verify_cli=cli_install.verify_installed_cli_provenance,
        sha256_file=protocol.sha256_file,
        desktop_executable_lease_factory=(
            cli_install.WindowsExecutableLease
        ),
        read_desktop_file_version=_read_desktop_file_version_production,
        read_uri_handler_command=_read_uri_handler_command_production,
        parse_windows_command_line=parse_windows_command_line,
        read_repository_head=_read_repository_head_production,
        read_windows_version=_read_windows_version_production,
        reserve_port=_reserve_port_production,
        port_is_bindable=_port_is_bindable_production,
        job_owner_factory=lambda executable: job_owner.WindowsJobOwner(
            executable
        ),
        verify_identities_exited=_verify_identities_exited_production,
        read_log_lines=_read_log_lines_production,
        coordinator_logs_within_bound=(
            _coordinator_logs_within_bound_production
        ),
        load_json_snapshot=_read_json_snapshot_production,
        write_json_atomic=_write_json_atomic_production,
        remove_owned_file=_remove_owned_file_production,
    )


def _empty_summary(
    *,
    run_id: str,
    base_output: pathlib.Path,
    barrier_path: pathlib.Path,
    desktop_executable: object,
    receipt_path: object,
) -> dict[str, object]:
    """Handle the empty summary step."""

    desktop_text = (
        os.fspath(desktop_executable)
        if isinstance(desktop_executable, (str, os.PathLike))
        else None
    )
    receipt_text = (
        os.fspath(receipt_path)
        if isinstance(receipt_path, (str, os.PathLike))
        else None
    )
    return {
        "schemaVersion": SUMMARY_SCHEMA_VERSION,
        "identity": {
            "runId": run_id,
            "baseCase": BASE_CASE,
            "tokenSha256": None,
            "repositoryHead": None,
            "windowsVersion": None,
            "unityVersion": None,
        },
        "cli": {
            "architecture": None,
            "assetUrl": None,
            "installedPath": None,
            "installedSha256": None,
            "installedVersion": None,
            "receiptPath": receipt_text,
            "releaseTag": None,
        },
        "desktop": {
            "executable": desktop_text,
            "fileVersion": None,
            "sha256": None,
            "uriHandler": None,
            "dataSource": DATA_SOURCE,
            "deeplink": None,
            "rootIdentity": None,
            "ownedMemberIdentities": [],
            "externalIdentities": [],
            "jobOwned": False,
        },
        "connection": {
            "host": LOOPBACK_HOST,
            "port": None,
            "portPreflight": False,
            "contextMarker": None,
            "initialMarker": None,
            "firstMarker": None,
            "secondMarker": None,
            "contextObservedAt": None,
            "initialObservedAt": None,
            "desktopIdentityCapturedAt": None,
            "firstObservedAt": None,
            "barrierWrittenAt": None,
            "secondObservedAt": None,
            "barrierPath": str(barrier_path),
            "barrierDigest": None,
            "barrierRemoved": False,
        },
        "foxrun": {
            "baseSummaryPath": str(base_output / "summary.json"),
            "baseVerdict": None,
            "channelEncodings": [],
            "deliveryObserved": False,
            "remoteApplied": False,
            "sameOriginDropped": False,
            "laterLocalPublished": False,
        },
        "cleanup": {
            "jobClosed": False,
            "processes": False,
            "port": False,
            "barrier": False,
            "files": False,
            "junctions": False,
            "subst": False,
            "gracefulOwnedIdentities": [],
            "forcedOwnedIdentities": [],
            "exitedOwnedIdentities": [],
            "residualOwnedIdentities": [],
        },
        "verdict": protocol.FAIL_DESKTOP_PREFLIGHT,
    }



__all__ = [name for name in globals() if not name.startswith("__")]
