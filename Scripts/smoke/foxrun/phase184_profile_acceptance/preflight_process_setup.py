from __future__ import annotations
from .graph_observer_and_runtime import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase184_profile_acceptance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
def _run_logged_preflight(
    command: Sequence[str],
    *,
    cwd: pathlib.Path,
    environment: Mapping[str, str],
    log_path: pathlib.Path,
    job: WindowsKillOnCloseJob | None,
    failure_code: str,
    operation: str,
    progress_paths: Iterable[pathlib.Path] = (),
) -> None:
    """Run one preparatory child with Job ownership and a no-progress watchdog."""

    log_path.parent.mkdir(parents=True, exist_ok=True)
    watchdog = protocol.ProgressWatchdog(operation)
    process = None
    try:
        with log_path.open("w", encoding="utf-8", errors="replace") as stream:
            process = subprocess.Popen(
                list(command),
                cwd=str(cwd),
                env=dict(environment),
                stdout=stream,
                stderr=subprocess.STDOUT,
                text=True,
                shell=False,
                **process_group_options(),
            )
            if job is not None:
                job.assign(process)
            observed_paths = (log_path, *(pathlib.Path(item) for item in progress_paths))
            last_progress = _progress_snapshot(observed_paths)
            watchdog.progress("preflight process started")
            while process.poll() is None:
                progress = _progress_snapshot(observed_paths)
                if progress != last_progress:
                    last_progress = progress
                    total_bytes = sum(max(0, item[1]) for item in progress)
                    watchdog.progress(f"log bytes={total_bytes}")
                try:
                    watchdog.check()
                except protocol.ProtocolFailure as exc:
                    terminate_owned_process(process)
                    raise AcceptanceFailure(failure_code, str(exc)) from exc
                time.sleep(0.1)
            exit_code = int(process.returncode)
    except AcceptanceFailure:
        if process is not None:
            terminate_owned_process(process)
        raise
    except OSError as exc:
        if process is not None:
            terminate_owned_process(process)
        raise AcceptanceFailure(failure_code, f"{operation} could not start.") from exc
    if exit_code != 0:
        raise AcceptanceFailure(
            failure_code,
            f"{operation} exited with code {exit_code}.",
        )
def _launch_logged_process(
    role: str,
    command: Sequence[str],
    *,
    cwd: pathlib.Path,
    environment: Mapping[str, str],
    log_path: pathlib.Path,
    owner: OwnedProcessSet,
    streams: list[TextIO],
):
    """Launch and register one long-lived named actor."""

    log_path.parent.mkdir(parents=True, exist_ok=True)
    stream: TextIO | None = None
    process = None
    try:
        stream = log_path.open("w", encoding="utf-8", errors="replace")
        process = subprocess.Popen(
            list(command),
            cwd=str(cwd),
            env=dict(environment),
            stdout=stream,
            stderr=subprocess.STDOUT,
            text=True,
            shell=False,
            **process_group_options(),
        )
        owner.register(role, process)
        streams.append(stream)
        return process
    except BaseException:
        if process is not None:
            terminate_owned_process(process)
        if stream is not None:
            with contextlib.suppress(Exception):
                stream.close()
        raise
def _wait_for_actor_readiness(
    config: Mapping[str, object],
    roles: Iterable[str],
    owner: OwnedProcessSet,
    timeout_seconds: float = 120.0,
) -> dict[str, dict[str, object]]:
    """Wait for actor readiness."""

    pending = set(roles)
    ready: dict[str, dict[str, object]] = {}
    deadline = time.monotonic() + timeout_seconds
    while pending:
        for role in tuple(pending):
            process = owner.process(role)
            if process is not None and process.poll() is not None:
                raise AcceptanceFailure(
                    protocol.failure_code(
                        "client" if role == "foxglove-client" else "peer"
                    ),
                    f"{role} exited before readiness.",
                )
            path = _actor_path(config, "readyFiles", role)
            if path.is_file():
                ready[role] = read_actor_document(config, role, "readyFiles")
                pending.remove(role)
        if not pending:
            break
        if time.monotonic() >= deadline:
            raise AcceptanceFailure(
                "FAIL_PREFLIGHT",
                "Required actor readiness expired: " + ",".join(sorted(pending)),
            )
        time.sleep(0.1)
    return ready
def _wait_for_actor_results(
    config: Mapping[str, object],
    roles: Iterable[str],
    owner: OwnedProcessSet,
    timeout_seconds: float = 60.0,
) -> dict[str, dict[str, object]]:
    """Wait for actor results."""

    pending = set(roles)
    results: dict[str, dict[str, object]] = {}
    deadline = time.monotonic() + timeout_seconds
    while pending:
        for role in tuple(pending):
            path = _actor_path(config, "resultFiles", role)
            if path.is_file():
                results[role] = read_actor_document(config, role, "resultFiles")
                pending.remove(role)
                continue
            process = owner.process(role)
            if process is not None and process.poll() is not None:
                raise AcceptanceFailure(
                    "FAIL_PROCESS_EXIT",
                    f"{role} exited without current PASS evidence.",
                )
        if not pending:
            break
        if time.monotonic() >= deadline:
            raise AcceptanceFailure(
                "FAIL_TERMINAL",
                "Required actor results expired: " + ",".join(sorted(pending)),
            )
        time.sleep(0.1)
    return results
def _read_one_u2r2_frame(connection: socket.socket) -> bytes:
    """Read one u2r2 frame."""

    fixed = bytearray()
    while len(fixed) < 16:
        chunk = connection.recv(16 - len(fixed))
        if not chunk:
            raise AcceptanceFailure("FAIL_BRIDGE", "Bridge closed during health response.")
        fixed.extend(chunk)
    if bytes(fixed[:4]) != U2R2_MAGIC:
        raise AcceptanceFailure("FAIL_BRIDGE", "Bridge health response magic is invalid.")
    _version, _flags, header_size, payload_size = struct.unpack("<HHII", fixed[4:16])
    total = 16 + int(header_size) + int(payload_size)
    if (
        header_size <= 0
        or header_size > MAX_FRAME_HEADER_BYTES
        or payload_size > MAX_FRAME_PAYLOAD_BYTES
    ):
        raise AcceptanceFailure("FAIL_BRIDGE", "Bridge health response length is invalid.")
    while len(fixed) < total:
        chunk = connection.recv(total - len(fixed))
        if not chunk:
            raise AcceptanceFailure("FAIL_BRIDGE", "Bridge closed during health response.")
        fixed.extend(chunk)
    return bytes(fixed)
def wait_for_bridge_health(
    config: Mapping[str, object],
    process,
    timeout_seconds: float = 120.0,
) -> dict[str, object]:
    """Require one exact current-run response from a disposable health sidecar."""

    request_id = str(config["token"])
    deadline = time.monotonic() + timeout_seconds
    last_error: Exception | None = None
    while time.monotonic() < deadline:
        if process.poll() is not None:
            raise AcceptanceFailure("FAIL_BRIDGE", "Bridge exited before health readiness.")
        try:
            with socket.create_connection(
                (str(config["bridgeHost"]), int(config["bridgePort"])),
                timeout=1.0,
            ) as connection:
                connection.settimeout(2.0)
                connection.sendall(build_u2r2_health_frame(request_id))
                header, payload = decode_u2r2_frame(_read_one_u2r2_frame(connection))
                validate_bridge_health_response(header, payload, request_id)
                return dict(header)
        except (OSError, AcceptanceFailure) as exc:
            last_error = exc
            time.sleep(0.1)
    raise AcceptanceFailure(
        "FAIL_BRIDGE",
        "Bridge health readiness expired"
        + (f" ({type(last_error).__name__})." if last_error is not None else "."),
    )
_BRIDGE_PUBLISHER_LINE = re.compile(
    r"publisher (?P<topic>/\S+) (?P<type>\S+) "
    r"profile=(?P<profile>\S+) reliability=(?P<reliability>\S+) "
    r"durability=(?P<durability>\S+) history=(?P<history>\S+) "
    r"depth=(?P<depth>\d+)"
)


__all__ = [name for name in globals() if not name.startswith("__")]
