from __future__ import annotations
from .matrix_contracts import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase186_bridge_capability_probe.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE


def _write_json_atomic(path: pathlib.Path, value: Mapping[str, object]) -> None:
    """Write one probe evidence object by atomic file replacement."""

    path.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile(
        mode="w",
        encoding="utf-8",
        newline="\n",
        dir=path.parent,
        prefix=path.name + ".",
        suffix=".tmp",
        delete=False,
    ) as stream:
        json.dump(value, stream, indent=2, sort_keys=True)
        stream.write("\n")
        temporary = pathlib.Path(stream.name)
    os.replace(temporary, path)


def _read_json(path: pathlib.Path) -> Mapping[str, object]:
    """Read a required JSON evidence object or fail closed."""

    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise ProbeFailure("required JSON evidence is unavailable: " + str(path)) from exc
    if not isinstance(value, Mapping):
        raise ProbeFailure("required JSON evidence is not an object: " + str(path))
    return value


def _load_last_json_line(path: pathlib.Path) -> Mapping[str, object]:
    """Load the last valid JSON object emitted to an owned probe log."""

    try:
        lines = path.read_text(encoding="utf-8", errors="replace").splitlines()
    except OSError as exc:
        raise ProbeFailure("owned probe log is unavailable") from exc
    for line in reversed(lines):
        if not line.startswith("{"):
            continue
        try:
            value = json.loads(line)
        except json.JSONDecodeError:
            continue
        if isinstance(value, Mapping):
            return value
    raise ProbeFailure("owned probe emitted no machine-readable result")


def _wait_for_marker(
    path: pathlib.Path,
    process: subprocess.Popen[str],
    marker: str,
    timeout_seconds: float,
) -> None:
    """Wait until an owned process emits the required readiness marker."""

    deadline = time.monotonic() + timeout_seconds
    while time.monotonic() < deadline:
        if path.is_file() and marker in path.read_text(
            encoding="utf-8", errors="replace"
        ):
            return
        if process.poll() is not None:
            raise ProbeFailure("subscriber exited before its ready marker")
        time.sleep(0.05)
    raise ProbeFailure("subscriber did not reach its ready marker")


def _terminate_owned(process: subprocess.Popen[str] | None) -> str | None:
    """Terminate and reap one process, returning a bounded cleanup diagnostic."""

    if process is None:
        return None
    pid = int(getattr(process, "pid", 0) or 0)
    try:
        if process.poll() is not None:
            return None
        if os.name == "nt":
            subprocess.run(
                ["taskkill", "/PID", str(pid), "/T", "/F"],
                check=False,
                stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL,
            )
        else:
            os.killpg(os.getpgid(pid), signal.SIGKILL)
        waiter = getattr(process, "wait", None)
        if not callable(waiter):
            process.kill()
        else:
            waiter(timeout=10)
    except (OSError, subprocess.TimeoutExpired) as exc:
        return (
            f"owned process {pid} could not be terminated or reaped "
            f"({type(exc).__name__})"
        )
    return None


def _record_cleanup_failures(
    result: dict[str, object],
    diagnostics: Sequence[str],
) -> None:
    """Make owned-process cleanup failures part of the durable row result."""

    failures = tuple(value for value in diagnostics if value)
    if not failures:
        return
    cleanup_message = "owned-process cleanup failed: " + "; ".join(failures)
    existing = result.get("failure")
    result["failure"] = (
        str(existing) + "; " + cleanup_message
        if isinstance(existing, str) and existing
        else cleanup_message
    )
    result["verdict"] = "FAIL"
    owned = result.get("ownedProcesses")
    if isinstance(owned, dict):
        owned["cleanupComplete"] = False


def _process_options() -> dict[str, object]:
    """Return platform-specific options for an owned process group."""

    if os.name == "nt":
        return {
            "creationflags": int(
                getattr(subprocess, "CREATE_NEW_PROCESS_GROUP", 0)
            )
        }
    return {"start_new_session": True}


__all__ = [name for name in globals() if not name.startswith("__")]
