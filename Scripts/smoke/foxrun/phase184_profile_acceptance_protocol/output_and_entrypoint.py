from __future__ import annotations
from .summary_validation import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase184_profile_acceptance_protocol.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
def _redact_for_json(value: object, repo_root: pathlib.Path) -> object:
    """Handle the redact for JSON step."""

    if isinstance(value, Mapping):
        result: dict[str, object] = {}
        for key, nested in value.items():
            safe_key = str(key)
            normalized_key = safe_key.casefold()
            if normalized_key in {"commandline", "environment"}:
                continue
            if normalized_key == "token":
                if isinstance(nested, str):
                    result["tokenSha256"] = token_sha256(nested)
                continue
            result[safe_key] = _redact_for_json(nested, repo_root)
        return result
    if isinstance(value, (list, tuple)):
        return [_redact_for_json(item, repo_root) for item in value]
    if isinstance(value, pathlib.Path):
        value = str(value)
    if isinstance(value, str):
        redacted = _TOKEN_IN_TEXT.sub("<redacted-token>", value)
        root_text = str(repo_root).rstrip("\\/")
        if redacted.casefold() == root_text.casefold():
            redacted = "<repo>"
        elif redacted.casefold().startswith((root_text + os.sep).casefold()):
            relative = redacted[len(root_text) :].lstrip("\\/").replace("\\", "/")
            redacted = f"<repo>/{relative}"
        elif _WINDOWS_ABSOLUTE_PATH.match(redacted):
            redacted = "<redacted-path>"
        if len(redacted) > MAX_DIAGNOSTIC_CHARACTERS:
            redacted = redacted[: MAX_DIAGNOSTIC_CHARACTERS - 1] + "…"
        return redacted
    return value


def write_json_atomic(
    destination: os.PathLike[str] | str,
    payload: Mapping[str, Any],
    *,
    repo_root: os.PathLike[str] | str,
) -> None:
    """Write bounded, redacted JSON through a same-directory atomic replace."""

    path = pathlib.Path(destination)
    path.parent.mkdir(parents=True, exist_ok=True)
    repo = pathlib.Path(repo_root).resolve(strict=False)
    redacted = _redact_for_json(payload, repo)
    temporary = path.with_name(f"{path.name}.{uuid.uuid4().hex}.tmp")
    try:
        with temporary.open("w", encoding="utf-8", newline="\n") as handle:
            json.dump(redacted, handle, ensure_ascii=False, indent=2, sort_keys=True)
            handle.write("\n")
            handle.flush()
            os.fsync(handle.fileno())
        os.replace(temporary, path)
    finally:
        try:
            temporary.unlink()
        except FileNotFoundError:
            pass


class ProgressWatchdog:
    """No-progress watchdog whose deadline resets only on observable progress."""

    def __init__(
        self,
        operation: str,
        *,
        stall_seconds: float | None = None,
        now: Callable[[], float] = time.monotonic,
    ):
        """Initialize the progress watchdog."""

        if not operation or not isinstance(operation, str):
            raise ValueError("operation must be a non-empty string")
        if stall_seconds is None:
            try:
                stall_seconds = float(OPERATION_STALL_SECONDS[operation])
            except KeyError as exc:
                raise ValueError(f"No watchdog default for {operation!r}") from exc
        if not math.isfinite(stall_seconds) or stall_seconds <= 0:
            raise ValueError("stall_seconds must be finite and positive")
        self.operation = operation
        self.stall_seconds = float(stall_seconds)
        self._now = now
        self.started_at = now()
        self.last_progress_at = self.started_at
        self.last_progress = "operation started"

    def progress(self, description: str) -> None:
        """Record one bounded progress update."""

        self.last_progress = description
        self.last_progress_at = self._now()

    def check(self) -> None:
        """Run one bounded progress check."""

        age = self._now() - self.last_progress_at
        if age > self.stall_seconds:
            normalized = self.operation.replace("-", "_").upper()
            raise ProtocolFailure(
                f"FAIL_{normalized}_STALLED",
                f"No {self.operation} progress for {age:.1f}s; "
                f"last progress: {self.last_progress}.",
            )


def subprocess_group_options(platform_name: str) -> dict[str, object]:
    """Return safe owned-process-group construction options for Popen."""

    if platform_name == "nt":
        return {
            "creationflags": getattr(subprocess, "CREATE_NEW_PROCESS_GROUP", 0x200),
            "start_new_session": False,
        }
    if platform_name == "posix":
        return {"creationflags": 0, "start_new_session": True}
    raise ValueError(f"Unsupported process platform: {platform_name}")


__all__ = [name for name in globals() if not name.startswith("__")]
