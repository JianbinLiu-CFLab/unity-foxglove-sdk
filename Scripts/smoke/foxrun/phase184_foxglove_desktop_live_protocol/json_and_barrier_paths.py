from __future__ import annotations
from .identity_and_receipt_contracts import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase184_foxglove_desktop_live_protocol.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
class _DuplicateJsonKey(ValueError):
    """Represent the duplicate JSON key contract."""

    pass


def _unique_object(pairs: list[tuple[str, object]]) -> dict[str, object]:
    """Handle the unique object step."""

    result: dict[str, object] = {}
    for key, value in pairs:
        if key in result:
            raise _DuplicateJsonKey("Duplicate JSON key.")
        result[key] = value
    return result


def load_json_bounded(
    path: os.PathLike[str] | str,
    *,
    max_bytes: int = MAX_RECEIPT_BYTES,
) -> object:
    """Load one UTF-8 JSON document without reading beyond its fixed bound."""

    if (
        isinstance(max_bytes, bool)
        or not isinstance(max_bytes, int)
        or max_bytes < 1
        or max_bytes > MAX_RECEIPT_BYTES
    ):
        raise _fail("JSON size bound is invalid.")
    try:
        with pathlib.Path(path).open("rb") as stream:
            raw = stream.read(max_bytes + 1)
    except (OSError, TypeError, ValueError) as exc:
        raise _fail("CLI receipt is unavailable.") from exc
    if not raw or len(raw) > max_bytes:
        raise _fail("CLI receipt size is invalid.")
    try:
        text = raw.decode("utf-8")
        return json.loads(text, object_pairs_hook=_unique_object)
    except (UnicodeError, ValueError, RecursionError) as exc:
        raise _fail("CLI receipt JSON is malformed.") from exc


def load_cli_receipt(path: os.PathLike[str] | str) -> dict[str, object]:
    """Load and intrinsically validate one bounded CLI installation receipt."""

    receipt = load_json_bounded(path)
    validated, _, _, _ = _validate_receipt(receipt)
    return validated


def write_json_atomic(
    destination: os.PathLike[str] | str,
    payload: Mapping[str, Any],
    *,
    max_bytes: int = MAX_RECEIPT_BYTES,
) -> None:
    """Write deterministic bounded JSON through an atomic sibling replace."""

    if (
        isinstance(max_bytes, bool)
        or not isinstance(max_bytes, int)
        or max_bytes < 1
        or max_bytes > MAX_RECEIPT_BYTES
    ):
        raise _fail("JSON size bound is invalid.")
    if not isinstance(payload, Mapping):
        raise _fail("JSON payload must be an object.")
    try:
        serialized = (
            json.dumps(
                dict(payload),
                allow_nan=False,
                ensure_ascii=True,
                separators=(",", ":"),
                sort_keys=True,
            )
            + "\n"
        ).encode("utf-8")
    except (TypeError, ValueError) as exc:
        raise _fail("JSON payload is not serializable.") from exc
    if len(serialized) > max_bytes:
        raise _fail("JSON payload exceeds the fixed size bound.")

    try:
        target = pathlib.Path(destination)
        target.parent.mkdir(parents=True, exist_ok=True)
    except (OSError, TypeError, ValueError) as exc:
        raise _fail("JSON destination is invalid.") from exc
    temporary = target.with_name(f"{target.name}.{uuid.uuid4().hex}.tmp")
    try:
        with temporary.open("xb") as stream:
            stream.write(serialized)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, target)
    except OSError as exc:
        raise _fail("JSON document could not be written atomically.") from exc
    finally:
        with contextlib.suppress(OSError):
            temporary.unlink()


def _client_failure(message: object) -> AcceptanceFailure:
    """Handle the client failure step."""

    return AcceptanceFailure(FAIL_CLIENT, message)


def _evidence_failure(message: object) -> AcceptanceFailure:
    """Handle the evidence failure step."""

    return AcceptanceFailure(FAIL_EVIDENCE, message)


def _is_reparse_point(info: os.stat_result) -> bool:
    """Return whether reparse point."""

    attributes = getattr(info, "st_file_attributes", 0)
    reparse_flag = getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 0x400)
    return bool(attributes & reparse_flag)


def _plain_resolved_output_root(value: object) -> pathlib.Path:
    """Handle the plain resolved output root step."""

    try:
        raw = os.fspath(value)
    except TypeError as exc:
        raise _client_failure("Desktop barrier output root is invalid.") from exc
    if (
        not isinstance(raw, str)
        or not raw
        or len(raw) > _MAX_WINDOWS_PATH_CHARACTERS
        or "\x00" in raw
        or "\r" in raw
        or "\n" in raw
    ):
        raise _client_failure("Desktop barrier output root is invalid.")

    candidate = pathlib.Path(raw)
    if not candidate.is_absolute() or ".." in candidate.parts:
        raise _client_failure("Desktop barrier output root is invalid.")
    try:
        info = candidate.lstat()
        resolved = candidate.resolve(strict=True)
        absolute = candidate.absolute()
    except OSError as exc:
        raise _client_failure("Desktop barrier output root is unavailable.") from exc
    if (
        not stat.S_ISDIR(info.st_mode)
        or stat.S_ISLNK(info.st_mode)
        or _is_reparse_point(info)
        or absolute != resolved
    ):
        raise _client_failure(
            "Desktop barrier output root must be one plain owned directory."
        )
    return resolved


def resolve_desktop_client_barrier_path(
    output_root: os.PathLike[str] | str,
) -> pathlib.Path:
    """Return the one fixed barrier path below a plain owned output root."""

    return _plain_resolved_output_root(output_root) / DESKTOP_CLIENT_BARRIER_FILENAME


@dataclasses.dataclass(frozen=True, slots=True)
class _DesktopBarrierContract:
    """Represent the desktop barrier contract contract."""

    output_root: pathlib.Path
    run_id: str
    token_digest: str
    positive_seconds: int


def _desktop_barrier_contract(config: object) -> _DesktopBarrierContract:
    """Handle the desktop barrier contract step."""

    if not isinstance(config, Mapping):
        raise _client_failure("Desktop barrier configuration is invalid.")

    run_id = config.get("runId")
    token = config.get("token")
    windows = config.get("observationWindows")
    if not isinstance(run_id, str) or _SAFE_RUN_ID.fullmatch(run_id) is None:
        raise _client_failure("Desktop barrier run identity is invalid.")
    if not isinstance(token, str) or _SAFE_TOKEN.fullmatch(token) is None:
        raise _client_failure("Desktop barrier token identity is invalid.")
    if not isinstance(windows, Mapping):
        raise _client_failure("Desktop barrier observation window is invalid.")
    positive_seconds = windows.get("positiveSeconds")
    if (
        isinstance(positive_seconds, bool)
        or not isinstance(positive_seconds, int)
        or positive_seconds < 1
        or positive_seconds > _MAX_OBSERVATION_SECONDS
    ):
        raise _client_failure("Desktop barrier observation window is invalid.")

    output_root = _plain_resolved_output_root(config.get("outputRoot"))
    token_digest = hashlib.sha256(token.encode("utf-8")).hexdigest().upper()
    return _DesktopBarrierContract(
        output_root=output_root,
        run_id=run_id,
        token_digest=token_digest,
        positive_seconds=positive_seconds,
    )


def _exact_desktop_barrier_path(
    value: os.PathLike[str] | str,
    output_root: pathlib.Path,
) -> pathlib.Path:
    """Handle the exact desktop barrier path step."""

    try:
        raw = os.fspath(value)
    except TypeError as exc:
        raise _client_failure("Desktop barrier path is invalid.") from exc
    if (
        not isinstance(raw, str)
        or not raw
        or len(raw) > _MAX_WINDOWS_PATH_CHARACTERS
        or "\x00" in raw
        or "\r" in raw
        or "\n" in raw
    ):
        raise _client_failure("Desktop barrier path is invalid.")

    candidate = pathlib.Path(raw)
    expected = output_root / DESKTOP_CLIENT_BARRIER_FILENAME
    if (
        not candidate.is_absolute()
        or ".." in candidate.parts
        or candidate.name != DESKTOP_CLIENT_BARRIER_FILENAME
        or candidate.parent != output_root
        or candidate != expected
    ):
        raise _client_failure(
            "Desktop barrier path must be the fixed owned output path."
        )
    try:
        if candidate.resolve(strict=False) != expected:
            raise _client_failure(
                "Desktop barrier path must not use a filesystem alias."
            )
    except OSError as exc:
        raise _client_failure("Desktop barrier path could not be resolved.") from exc
    return expected




__all__ = [name for name in globals() if not name.startswith("__")]
