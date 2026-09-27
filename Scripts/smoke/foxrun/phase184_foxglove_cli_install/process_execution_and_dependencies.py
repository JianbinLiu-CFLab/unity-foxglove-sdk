from __future__ import annotations
from .release_download_and_urls import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase184_foxglove_cli_install.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE


def _run_bounded_process(
    command: Sequence[str],
    *,
    timeout_seconds: float = COMMAND_TIMEOUT_SECONDS,
    environment: Mapping[str, str] | None = None,
) -> _BoundedProcessResult:
    """Run one helper while retaining at most one bounded buffer per stream."""

    if (
        not command
        or any(not isinstance(argument, str) or not argument for argument in command)
        or not isinstance(timeout_seconds, (int, float))
        or timeout_seconds <= 0
        or (
            environment is not None
            and not isinstance(environment, Mapping)
        )
    ):
        raise _fail("Bounded Foxglove CLI helper command is invalid.")

    try:
        process = subprocess.Popen(
            list(command),
            shell=False,
            stdin=subprocess.DEVNULL,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            env=(
                None
                if environment is None
                else dict(environment)
            ),
        )
    except (OSError, subprocess.SubprocessError) as exc:
        raise _fail("Bounded Foxglove CLI helper could not start.") from exc
    if process.stdout is None or process.stderr is None:
        _terminate_and_reap(process)
        raise _fail("Bounded Foxglove CLI helper pipes are unavailable.")

    stdout_buffer = bytearray()
    stderr_buffer = bytearray()
    overflow = threading.Event()
    reader_failed = threading.Event()

    def drain(stream: Any, output: bytearray) -> None:
        """Drain one child stream into a bounded shared buffer."""

        read_chunk = getattr(stream, "read1", stream.read)
        try:
            while True:
                chunk = read_chunk(4096)
                if not chunk:
                    return
                remaining = MAX_COMMAND_OUTPUT_BYTES - len(output)
                if remaining > 0:
                    output.extend(chunk[:remaining])
                if len(chunk) > remaining:
                    overflow.set()
                    return
        except (OSError, ValueError):
            reader_failed.set()
        finally:
            with contextlib.suppress(OSError, ValueError):
                stream.close()

    readers = (
        threading.Thread(
            target=drain,
            args=(process.stdout, stdout_buffer),
            name="foxglove-cli-stdout",
            daemon=True,
        ),
        threading.Thread(
            target=drain,
            args=(process.stderr, stderr_buffer),
            name="foxglove-cli-stderr",
            daemon=True,
        ),
    )
    for reader in readers:
        reader.start()

    timed_out = False
    cleanup_failure: Exception | None = None
    deadline = time.monotonic() + float(timeout_seconds)
    try:
        while process.poll() is None:
            if overflow.is_set():
                break
            remaining = deadline - time.monotonic()
            if remaining <= 0:
                timed_out = True
                break
            overflow.wait(min(remaining, 0.01))
        if process.poll() is None:
            _terminate_and_reap(process)
        else:
            process.wait(timeout=0)
    except (KeyboardInterrupt, SystemExit):
        try:
            _terminate_and_reap(process)
        except Exception:
            pass
        raise
    except Exception as exc:
        cleanup_failure = exc
        try:
            _terminate_and_reap(process)
        except Exception:
            pass
    finally:
        for reader in readers:
            reader.join(timeout=1)
        for stream in (process.stdout, process.stderr):
            with contextlib.suppress(OSError, ValueError):
                stream.close()
        for reader in readers:
            reader.join(timeout=1)

    if cleanup_failure is not None:
        if isinstance(cleanup_failure, protocol.AcceptanceFailure):
            raise cleanup_failure
        raise _fail("Bounded Foxglove CLI helper failed.") from cleanup_failure
    if any(reader.is_alive() for reader in readers) or reader_failed.is_set():
        raise _fail("Bounded Foxglove CLI helper output could not be drained.")
    if timed_out:
        raise _fail("Bounded Foxglove CLI helper timed out.")
    if overflow.is_set():
        raise _fail("Bounded Foxglove CLI helper output exceeded the size bound.")
    if process.returncode is None:
        raise _fail("Bounded Foxglove CLI helper was not reaped.")
    return _BoundedProcessResult(
        returncode=process.returncode,
        stdout=bytes(stdout_buffer),
        stderr=bytes(stderr_buffer),
    )


def _run_command_production(
    executable: str,
    arguments: tuple[str, ...],
    environment: Mapping[str, str] | None = None,
) -> str:
    """Run the sole permitted CLI version command with bounded output."""

    if arguments != ("version",):
        raise _fail("Foxglove CLI command is not permitted.")
    try:
        completed = _run_bounded_process(
            [executable, *arguments],
            timeout_seconds=COMMAND_TIMEOUT_SECONDS,
            environment=(
                build_minimal_process_environment(os.environ)
                if environment is None
                else environment
            ),
        )
    except protocol.AcceptanceFailure:
        raise
    except (OSError, subprocess.SubprocessError) as exc:
        raise _fail("Foxglove CLI version command could not run.") from exc
    if completed.returncode != 0:
        raise _fail("Foxglove CLI version command failed.")
    if (
        not completed.stdout
        or len(completed.stdout) > MAX_COMMAND_OUTPUT_BYTES
    ):
        raise _fail("Foxglove CLI version output size is invalid.")
    try:
        return completed.stdout.decode("utf-8")
    except UnicodeError as exc:
        raise _fail("Foxglove CLI version output is not UTF-8.") from exc


def _resolve_command_production(
    environment: Mapping[str, str] | None = None,
) -> str:
    """Resolve the active CLI path in a fresh non-interactive PowerShell."""

    command = (
        "$resolved = Get-Command foxglove -CommandType Application "
        "-ErrorAction Stop; [Console]::Out.Write($resolved.Source)"
    )
    try:
        completed = _run_bounded_process(
            [
                "powershell.exe",
                "-NoLogo",
                "-NoProfile",
                "-NonInteractive",
                "-Command",
                command,
            ],
            timeout_seconds=COMMAND_TIMEOUT_SECONDS,
            environment=(
                build_minimal_process_environment(os.environ)
                if environment is None
                else environment
            ),
        )
    except protocol.AcceptanceFailure:
        raise
    except (OSError, subprocess.SubprocessError) as exc:
        raise _fail("Fresh PowerShell Foxglove CLI resolution failed.") from exc
    if completed.returncode != 0:
        raise _fail("Fresh PowerShell Foxglove CLI resolution failed.")
    if (
        not completed.stdout
        or len(completed.stdout) > MAX_COMMAND_OUTPUT_BYTES
    ):
        raise _fail("Fresh PowerShell Foxglove CLI resolution is invalid.")
    try:
        resolved = completed.stdout.decode("utf-8").strip()
    except UnicodeError as exc:
        raise _fail("Fresh PowerShell Foxglove CLI resolution is invalid.") from exc
    if not resolved or "\r" in resolved or "\n" in resolved:
        raise _fail("Fresh PowerShell Foxglove CLI resolution is ambiguous.")
    return resolved


def _atomic_replace_production(source: str, destination: str) -> None:
    """Atomically replace the destination with one sibling source file."""

    os.replace(pathlib.Path(source), pathlib.Path(destination))


def _production_dependencies() -> InstallerDependencies:
    """Construct the Windows production dependency set."""

    if os.name != "nt":
        raise _fail("Foxglove CLI installation is supported only on Windows.")
    return InstallerDependencies(
        release_fetcher=_fetch_release_production,
        downloader=_download_production,
        command_runner=_run_command_production,
        command_resolver=_resolve_command_production,
        clock=lambda: dt.datetime.now(dt.timezone.utc),
        atomic_replacer=_atomic_replace_production,
        filesystem=LocalFilesystem(),
        process_environment=dict(os.environ),
        executable_lease_factory=WindowsExecutableLease,
    )


def _run_version(
    path: str,
    dependencies: InstallerDependencies,
) -> str:
    """Run and normalize the version reported by one CLI executable."""

    environment = build_minimal_process_environment(
        dependencies.process_environment
    )
    try:
        raw_version = dependencies.command_runner(
            path,
            ("version",),
            environment,
        )
    except protocol.AcceptanceFailure:
        raise
    except Exception as exc:
        raise _fail("Foxglove CLI version command failed.") from exc
    return protocol.normalize_semantic_version(raw_version)


def _installed_utc(clock: Callable[[], dt.datetime]) -> str:
    """Return one timezone-aware installation timestamp in UTC."""

    try:
        value = clock()
    except Exception as exc:
        raise _fail("Foxglove CLI installation clock failed.") from exc
    if (
        not isinstance(value, dt.datetime)
        or value.tzinfo is None
        or value.utcoffset() is None
    ):
        raise _fail("Foxglove CLI installation clock must be timezone-aware.")
    utc = value.astimezone(dt.timezone.utc)
    return utc.isoformat().replace("+00:00", "Z")


def _remove_quietly(filesystem: Any, path: str | None) -> None:
    """Best-effort remove one installer-owned temporary file."""

    if path is None:
        return
    try:
        filesystem.remove(path)
    except Exception:
        pass


__all__ = [name for name in globals() if not name.startswith("__")]
