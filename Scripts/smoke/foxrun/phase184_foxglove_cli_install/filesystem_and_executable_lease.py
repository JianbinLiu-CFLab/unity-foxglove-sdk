from __future__ import annotations
from .cli_identity_contracts import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase184_foxglove_cli_install.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE


class LocalFilesystem:
    """Small production filesystem seam used by the transaction."""

    @staticmethod
    def _path(path: str) -> pathlib.Path:
        """Convert a validated path string to a local path object."""

        return pathlib.Path(path)

    def ensure_parent(self, path: str) -> None:
        """Create the destination parent directory when absent."""

        self._path(path).parent.mkdir(parents=True, exist_ok=True)

    def exists(self, path: str) -> bool:
        """Return whether the path names a regular file."""

        return self._path(path).is_file()

    def size(self, path: str) -> int:
        """Return the file size in bytes."""

        return self._path(path).stat().st_size

    def sha256(self, path: str) -> str:
        """Return the file's lowercase SHA-256 digest."""

        return protocol.sha256_file(self._path(path))

    def new_sibling_temp(self, target: str, purpose: str) -> str:
        """Allocate a unique sibling path without creating the file."""

        safe_purpose = re.sub(r"[^a-z0-9-]+", "-", purpose.lower()).strip("-")
        if not safe_purpose:
            raise _fail("CLI temporary-file purpose is invalid.")
        directory = ntpath.dirname(target)
        filename = ntpath.basename(target)
        temporary = ntpath.join(
            directory,
            f".{filename}.{safe_purpose}.{uuid.uuid4().hex}.tmp",
        )
        if len(temporary) > _MAX_WINDOWS_PATH_CHARACTERS:
            raise _fail("CLI sibling temporary path is too long.")
        return temporary

    def copy_exclusive(self, source: str, destination: str) -> None:
        """Copy a file durably while refusing to overwrite the destination."""

        destination_path = self._path(destination)
        destination_path.parent.mkdir(parents=True, exist_ok=True)
        with self._path(source).open("rb") as input_stream:
            with destination_path.open("xb") as output_stream:
                shutil.copyfileobj(
                    input_stream,
                    output_stream,
                    length=1024 * 1024,
                )
                output_stream.flush()
                os.fsync(output_stream.fileno())

    def publish_exclusive(self, source: str, destination: str) -> None:
        """Publish a sibling temporary file without replacing an existing file."""

        os.rename(self._path(source), self._path(destination))

    def remove(self, path: str) -> None:
        """Remove one file when present."""

        with contextlib.suppress(FileNotFoundError):
            self._path(path).unlink()

    def write_receipt(
        self,
        path: str,
        payload: Mapping[str, object],
    ) -> None:
        """Write one bounded installer receipt atomically."""

        protocol.write_json_atomic(self._path(path), payload)

    def load_receipt(self, path: str) -> dict[str, object]:
        """Load and validate one installer receipt."""

        return protocol.load_cli_receipt(self._path(path))


def _validated_windows_path(path: object, label: str) -> str:
    """Return one normalized absolute Windows path or fail closed."""

    try:
        value = os.fspath(path)
    except TypeError as exc:
        raise _fail(f"{label} must be one absolute Windows path.") from exc
    protocol.windows_path_key(value, label=label)
    return ntpath.normpath(value)


def build_minimal_process_environment(
    source: Mapping[str, str],
) -> dict[str, str]:
    """Copy only Windows process basics; never forward credentials or ROS state."""

    if not isinstance(source, Mapping):
        raise _fail("Foxglove CLI process environment is invalid.")
    result: dict[str, str] = {}
    seen_names: set[str] = set()
    for key, value in source.items():
        if (
            not isinstance(key, str)
            or not isinstance(value, str)
            or key.upper() not in _MINIMAL_PROCESS_ENVIRONMENT_NAMES
            or not key
            or "=" in key
            or "\x00" in key
            or "\x00" in value
            or "\r" in key
            or "\n" in key
            or "\r" in value
            or "\n" in value
        ):
            continue
        canonical_name = key.upper()
        if canonical_name in seen_names:
            raise _fail(
                "Foxglove CLI process environment has duplicate Windows names."
            )
        seen_names.add(canonical_name)
        result[key] = value
    result["USERPROFILE"] = CLI_ISOLATED_USERPROFILE
    return dict(sorted(result.items(), key=lambda item: item[0].casefold()))


class _BY_HANDLE_FILE_INFORMATION(ctypes.Structure):
    """Win32 file metadata returned for an open executable handle."""

    _fields_ = (
        ("dwFileAttributes", wintypes.DWORD),
        ("ftCreationTime", wintypes.FILETIME),
        ("ftLastAccessTime", wintypes.FILETIME),
        ("ftLastWriteTime", wintypes.FILETIME),
        ("dwVolumeSerialNumber", wintypes.DWORD),
        ("nFileSizeHigh", wintypes.DWORD),
        ("nFileSizeLow", wintypes.DWORD),
        ("nNumberOfLinks", wintypes.DWORD),
        ("nFileIndexHigh", wintypes.DWORD),
        ("nFileIndexLow", wintypes.DWORD),
    )


class WindowsExecutableLease:
    """Hold a non-reparse executable deny-write/delete through verification."""

    _GENERIC_READ = 0x80000000
    _FILE_READ_ATTRIBUTES = 0x0080
    _FILE_SHARE_READ = 0x00000001
    _FILE_SHARE_WRITE = 0x00000002
    _OPEN_EXISTING = 3
    _FILE_ATTRIBUTE_REPARSE_POINT = 0x00000400
    _FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000
    _FILE_FLAG_BACKUP_SEMANTICS = 0x02000000
    _DUPLICATE_SAME_ACCESS = 0x00000002

    def __init__(self, path: os.PathLike[str] | str):
        """Prepare a lease for one normalized executable path."""

        self.path = _validated_windows_path(
            path,
            "Executable lease path",
        )
        self._handles: list[int] = []
        self._file_handle: int | None = None
        self._kernel32: Any | None = None

    def _configure_api(self) -> Any:
        """Load and type the Win32 handle APIs used by the lease."""

        if os.name != "nt":
            raise _fail("Executable lease requires Windows.")
        kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
        kernel32.CreateFileW.argtypes = [
            wintypes.LPCWSTR,
            wintypes.DWORD,
            wintypes.DWORD,
            wintypes.LPVOID,
            wintypes.DWORD,
            wintypes.DWORD,
            wintypes.HANDLE,
        ]
        kernel32.CreateFileW.restype = wintypes.HANDLE
        kernel32.GetFileInformationByHandle.argtypes = [
            wintypes.HANDLE,
            ctypes.POINTER(_BY_HANDLE_FILE_INFORMATION),
        ]
        kernel32.GetFileInformationByHandle.restype = wintypes.BOOL
        kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
        kernel32.CloseHandle.restype = wintypes.BOOL
        kernel32.GetCurrentProcess.argtypes = []
        kernel32.GetCurrentProcess.restype = wintypes.HANDLE
        kernel32.DuplicateHandle.argtypes = [
            wintypes.HANDLE,
            wintypes.HANDLE,
            wintypes.HANDLE,
            ctypes.POINTER(wintypes.HANDLE),
            wintypes.DWORD,
            wintypes.BOOL,
            wintypes.DWORD,
        ]
        kernel32.DuplicateHandle.restype = wintypes.BOOL
        return kernel32

    def _close_handles(self) -> bool:
        """Close all owned handles and report whether cleanup was complete."""

        kernel32 = self._kernel32
        handles = tuple(reversed(self._handles))
        self._handles.clear()
        self._file_handle = None
        clean = True
        if kernel32 is not None:
            for handle in handles:
                try:
                    if not kernel32.CloseHandle(handle):
                        clean = False
                except Exception:
                    clean = False
        return clean

    def _handle_information(
        self,
        handle: int,
    ) -> tuple[ExecutableFileIdentity, int]:
        """Read stable identity and attributes from one owned handle."""

        kernel32 = self._kernel32
        if kernel32 is None:
            raise _fail("Executable lease is not active.")
        information = _BY_HANDLE_FILE_INFORMATION()
        if not kernel32.GetFileInformationByHandle(
            handle,
            ctypes.byref(information),
        ):
            raise _fail("Executable lease identity could not be read.")
        identity = ExecutableFileIdentity(
            volume_serial=int(information.dwVolumeSerialNumber),
            file_id=(
                int(information.nFileIndexHigh) << 32
            )
            | int(information.nFileIndexLow),
        )
        return identity, int(information.dwFileAttributes)

    def _open_component(
        self,
        path: pathlib.Path,
        *,
        final: bool,
    ) -> int:
        """Open one path component while rejecting reparse points."""

        kernel32 = self._kernel32
        if kernel32 is None:
            raise _fail("Executable lease is not active.")
        desired_access = (
            self._GENERIC_READ
            if final
            else self._FILE_READ_ATTRIBUTES
        )
        share_mode = (
            self._FILE_SHARE_READ
            if final
            else self._FILE_SHARE_READ | self._FILE_SHARE_WRITE
        )
        flags = self._FILE_FLAG_OPEN_REPARSE_POINT
        if not final:
            flags |= self._FILE_FLAG_BACKUP_SEMANTICS
        handle = kernel32.CreateFileW(
            str(path),
            desired_access,
            share_mode,
            None,
            self._OPEN_EXISTING,
            flags,
            None,
        )
        invalid_handle = ctypes.c_void_p(-1).value
        handle_value = int(handle or 0)
        if handle_value in (0, invalid_handle):
            raise _fail("Executable lease component could not be opened.")
        try:
            _identity, attributes = self._handle_information(handle_value)
            if attributes & self._FILE_ATTRIBUTE_REPARSE_POINT:
                raise _fail("Executable path contains a reparse component.")
        except BaseException:
            with contextlib.suppress(Exception):
                kernel32.CloseHandle(handle_value)
            raise
        return handle_value

    def __enter__(self) -> WindowsExecutableLease:
        """Acquire handles for every path component and the executable."""

        self._kernel32 = self._configure_api()
        target = pathlib.Path(self.path)
        components = [*reversed(target.parents), target]
        try:
            for index, component in enumerate(components):
                handle = self._open_component(
                    component,
                    final=index == len(components) - 1,
                )
                self._handles.append(handle)
            self._file_handle = self._handles[-1]
            return self
        except BaseException:
            self._close_handles()
            raise

    def __exit__(self, exc_type, exc, traceback) -> bool:
        """Release every lease handle without suppressing body failures."""

        del exc, traceback
        clean = self._close_handles()
        if not clean and exc_type is None:
            raise _fail("Executable lease handles could not be released.")
        return False

    def path_identity(self) -> ExecutableFileIdentity:
        """Reopen the path and return its current stable file identity."""

        if self._file_handle is None:
            raise _fail("Executable lease is not active.")
        handle = self._open_component(pathlib.Path(self.path), final=True)
        try:
            identity, _attributes = self._handle_information(handle)
            return identity
        finally:
            kernel32 = self._kernel32
            if kernel32 is not None and not kernel32.CloseHandle(handle):
                raise _fail(
                    "Executable path identity handle could not be released."
                )

    def snapshot(self) -> ExecutableSnapshot:
        """Hash the leased executable through a duplicated read handle."""

        handle = self._file_handle
        kernel32 = self._kernel32
        if handle is None or kernel32 is None:
            raise _fail("Executable lease is not active.")
        identity, _attributes = self._handle_information(handle)
        duplicate = wintypes.HANDLE()
        current = kernel32.GetCurrentProcess()
        if not kernel32.DuplicateHandle(
            current,
            handle,
            current,
            ctypes.byref(duplicate),
            0,
            False,
            self._DUPLICATE_SAME_ACCESS,
        ):
            raise _fail("Executable lease could not duplicate its read handle.")
        duplicate_value = int(duplicate.value or 0)
        transferred = False
        try:
            import msvcrt

            descriptor = msvcrt.open_osfhandle(
                duplicate_value,
                os.O_RDONLY,
            )
            transferred = True
            digest = hashlib.sha256()
            with os.fdopen(descriptor, "rb", closefd=True) as stream:
                stream.seek(0)
                while chunk := stream.read(1024 * 1024):
                    digest.update(chunk)
        except BaseException:
            if not transferred:
                with contextlib.suppress(Exception):
                    kernel32.CloseHandle(duplicate_value)
            raise
        return ExecutableSnapshot(
            identity=identity,
            sha256=digest.hexdigest().upper(),
        )


__all__ = [name for name in globals() if not name.startswith("__")]
