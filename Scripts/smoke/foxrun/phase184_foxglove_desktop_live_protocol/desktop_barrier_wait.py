from __future__ import annotations
from .json_and_barrier_paths import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase184_foxglove_desktop_live_protocol.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
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


def _load_visible_desktop_barrier(
    path: pathlib.Path,
    contract: _DesktopBarrierContract,
) -> dict[str, object] | None:
    """Load visible desktop barrier."""

    try:
        before = path.lstat()
    except FileNotFoundError:
        return None
    except OSError as exc:
        raise _client_failure("Desktop barrier could not be inspected.") from exc

    if (
        not stat.S_ISREG(before.st_mode)
        or stat.S_ISLNK(before.st_mode)
        or _is_reparse_point(before)
    ):
        raise _client_failure("Desktop barrier must be one plain regular file.")

    try:
        with path.open("rb") as stream:
            opened = os.fstat(stream.fileno())
            raw = stream.read(MAX_DESKTOP_CLIENT_BARRIER_BYTES + 1)
        after = path.lstat()
    except OSError as exc:
        raise _client_failure("Desktop barrier could not be read.") from exc
    if (
        not _same_file_snapshot(before, opened)
        or not _same_file_snapshot(opened, after)
        or stat.S_ISLNK(after.st_mode)
        or _is_reparse_point(after)
    ):
        raise _client_failure("Desktop barrier changed while it was read.")
    if not raw or len(raw) > MAX_DESKTOP_CLIENT_BARRIER_BYTES:
        raise _client_failure("Desktop barrier size is invalid.")

    try:
        text = raw.decode("utf-8")
        document = json.loads(text, object_pairs_hook=_unique_object)
    except (UnicodeError, ValueError, RecursionError) as exc:
        raise _client_failure("Desktop barrier JSON is malformed.") from exc
    if not isinstance(document, dict):
        raise _client_failure("Desktop barrier root must be an object.")
    if frozenset(document) != DESKTOP_CLIENT_BARRIER_KEYS:
        raise _client_failure("Desktop barrier keys do not match schemaVersion 1.")

    schema_version = document["schemaVersion"]
    accepted_clients = document["acceptedClients"]
    if (
        isinstance(schema_version, bool)
        or not isinstance(schema_version, int)
        or schema_version != DESKTOP_CLIENT_BARRIER_SCHEMA_VERSION
    ):
        raise _client_failure("Desktop barrier schemaVersion is unsupported.")
    if (
        not isinstance(document["runId"], str)
        or document["runId"] != contract.run_id
    ):
        raise _client_failure("Desktop barrier run identity is stale or mismatched.")
    token_digest = document["tokenDigest"]
    if (
        not isinstance(token_digest, str)
        or _UPPER_SHA256.fullmatch(token_digest) is None
        or token_digest != contract.token_digest
    ):
        raise _client_failure("Desktop barrier token identity is stale or mismatched.")
    if (
        not isinstance(document["state"], str)
        or document["state"] != DESKTOP_CLIENT_BARRIER_STATE
    ):
        raise _client_failure("Desktop barrier state is invalid.")
    if (
        isinstance(accepted_clients, bool)
        or not isinstance(accepted_clients, int)
        or accepted_clients != 1
    ):
        raise _client_failure("Desktop barrier acceptedClients must equal one.")
    return document


def _monotonic_value(clock: Callable[[], float]) -> float:
    """Handle the monotonic value step."""

    try:
        value = clock()
    except Exception as exc:
        raise _client_failure("Desktop barrier clock failed.") from exc
    if (
        isinstance(value, bool)
        or not isinstance(value, (int, float))
        or not math.isfinite(float(value))
    ):
        raise _client_failure("Desktop barrier clock is invalid.")
    return float(value)


def wait_for_desktop_barrier(
    config: Mapping[str, Any],
    path: os.PathLike[str] | str,
    *,
    clock: Callable[[], float] | None = None,
    sleep: Callable[[float], None] | None = None,
    deadline: float | None = None,
) -> dict[str, object]:
    """Wait for one exact token-bound Desktop barrier without doing I/O elsewhere."""

    contract = _desktop_barrier_contract(config)
    barrier = _exact_desktop_barrier_path(path, contract.output_root)
    selected_clock = time.monotonic if clock is None else clock
    selected_sleep = time.sleep if sleep is None else sleep
    if not callable(selected_clock) or not callable(selected_sleep):
        raise _client_failure("Desktop barrier wait dependencies are invalid.")

    started = _monotonic_value(selected_clock)
    bounded_deadline = (
        started
        + contract.positive_seconds
        + DESKTOP_CLIENT_BARRIER_STARTUP_ALLOWANCE_SECONDS
    )
    if deadline is not None:
        if (
            isinstance(deadline, bool)
            or not isinstance(deadline, (int, float))
            or not math.isfinite(float(deadline))
        ):
            raise _client_failure("Desktop barrier deadline is invalid.")
        bounded_deadline = min(bounded_deadline, float(deadline))

    while True:
        now = _monotonic_value(selected_clock)
        if now >= bounded_deadline:
            raise _client_failure(
                "Desktop client barrier did not appear before the bounded deadline."
            )

        document = _load_visible_desktop_barrier(barrier, contract)
        if document is not None:
            return document

        remaining = bounded_deadline - now
        delay = min(DESKTOP_CLIENT_BARRIER_POLL_SECONDS, remaining)
        try:
            selected_sleep(delay)
        except Exception as exc:
            raise _client_failure("Desktop barrier polling failed.") from exc




__all__ = [name for name in globals() if not name.startswith("__")]
