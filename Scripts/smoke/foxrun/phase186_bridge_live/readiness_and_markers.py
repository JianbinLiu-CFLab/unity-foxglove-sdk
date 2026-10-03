from __future__ import annotations
from .runtime_models_and_setup import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase186_bridge_live.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE


def _wait_port(host: str, port: int, process: ProcessRecord, timeout_seconds: float) -> None:
    """Wait for port."""
    deadline = time.monotonic() + timeout_seconds
    while time.monotonic() < deadline:
        if process.process.poll() is not None:
            raise LiveFailure("FAIL_PROCESS_EXIT", f"{process.logical_role} exited before readiness")
        try:
            with socket.create_connection((host, port), timeout=0.2):
                return
        except OSError:
            time.sleep(0.05)
    raise LiveFailure("FAIL_RUNTIME_SELECTION", f"{process.logical_role} listener expired")


def _wait_sidecar(config: Mapping[str, Any], process: ProcessRecord) -> Mapping[str, Any]:
    """Wait for sidecar."""
    deadline = time.monotonic() + 120.0
    last: Exception | None = None
    while time.monotonic() < deadline:
        if process.process.poll() is not None:
            raise LiveFailure("FAIL_PROCESS_EXIT", "sidecar exited before health readiness")
        try:
            return live_peer._health(config, str(config["token"]) + "-parent-health")
        except (OSError, protocol.ProtocolFailure) as exc:
            last = exc
            time.sleep(0.1)
    raise LiveFailure(
        "FAIL_BRIDGE",
        "sidecar health readiness expired"
        + (f" ({type(last).__name__})" if last is not None else ""),
    )


def _launch_sidecar(
    owner: OwnedLiveProcesses,
    runtime: PreparedRuntime,
    config: Mapping[str, Any],
    key: str,
) -> tuple[ProcessRecord, Mapping[str, Any]]:
    """Handle launch sidecar for Phase186 acceptance."""
    command = [
        str(runtime.bridge_executable),
        "--host",
        str(config["bridgeHost"]),
        "--port",
        str(config["bridgePort"]),
        "--payload-format",
        "cdr-with-encapsulation",
    ]
    process = owner.launch(
        key,
        "sidecar",
        command,
        cwd=runtime.bridge_executable.parent,
        environment=runtime.environment,
        output_root=pathlib.Path(str(config["outputRoot"])),
    )
    return process, _wait_sidecar(config, process)


def _worker_command(python: pathlib.Path, role: str, config_path: pathlib.Path) -> list[str]:
    """Handle worker command for Phase186 acceptance."""
    return [
        str(python),
        str(pathlib.Path(live_peer.__file__).resolve()),
        "--role",
        role,
        "--run-config",
        str(config_path),
    ]


def _read_actor_document(
    config: Mapping[str, Any],
    role: str,
    kind: str,
) -> Mapping[str, Any] | None:
    """Read actor document."""
    path = pathlib.Path(str(config["outputRoot"])) / "actors" / f"{role}-{kind}.json"
    if not path.is_file():
        return None
    try:
        if path.stat().st_size <= 0 or path.stat().st_size > MAX_DOCUMENT_BYTES:
            raise OSError("actor document size differs")
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as exc:
        raise LiveFailure("FAIL_EVIDENCE", f"{role} {kind} evidence is invalid") from exc
    expected = {
        "schemaVersion",
        "runId",
        "caseId",
        "runtimeRowId",
        "tokenHash",
        "head",
        "role",
        "kind",
        "pid",
        "verdict",
        "evidence",
        "createdAt",
    }
    if (
        not isinstance(value, Mapping)
        or set(value) != expected
        or value["schemaVersion"] != 1
        or value["runId"] != config["runId"]
        or value["caseId"] != config["caseId"]
        or value["runtimeRowId"] != config["runtimeRowId"]
        or value["tokenHash"] != config["tokenHash"]
        or value["head"] != config["head"]
        or value["role"] != role
        or value["kind"] != kind
        or value["verdict"] != ("READY" if kind == "ready" else "PASS")
        or not isinstance(value["evidence"], Mapping)
    ):
        raise LiveFailure("FAIL_EVIDENCE", f"{role} {kind} identity differs")
    return value


def _wait_actor_document(
    config: Mapping[str, Any],
    owner: OwnedLiveProcesses,
    role: str,
    kind: str,
    timeout_seconds: float,
    *,
    owner_role: str | None = None,
) -> Mapping[str, Any]:
    """Wait for actor document."""
    process_role = owner_role or role
    deadline = time.monotonic() + timeout_seconds
    while time.monotonic() < deadline:
        document = _read_actor_document(config, role, kind)
        if document is not None:
            return document
        if owner.poll(process_role) is not None:
            failure = (
                pathlib.Path(str(config["outputRoot"]))
                / "actors"
                / f"{process_role}-failure.json"
            )
            detail = failure.read_text(encoding="utf-8")[:512] if failure.is_file() else ""
            raise LiveFailure(
                "FAIL_PROCESS_EXIT",
                f"{role} exited before {kind}: {detail}",
            )
        time.sleep(0.05)
    raise LiveFailure("FAIL_TERMINAL", f"{role} {kind} evidence expired")


def _unity_command(
    unity: pathlib.Path,
    config: Mapping[str, Any],
) -> list[str]:
    """Handle unity command for Phase186 acceptance."""
    return [
        str(unity),
        "-batchmode",
        "-nographics",
        "-projectPath",
        str(config["projectPath"]),
        "-executeMethod",
        UNITY_EXECUTE_METHOD,
        "-phase186RunConfig",
        str(pathlib.Path(str(config["outputRoot"])) / "run-config.json"),
        "-logFile",
        str(config["unityLog"]),
    ]


def _marker_in_log(config: Mapping[str, Any], prefix: str) -> bool:
    """Handle marker in log for Phase186 acceptance."""
    return live_peer._has_unity_marker(config, prefix)


def _manual_marker_in_log(config: Mapping[str, Any]) -> bool:
    """Handle manual marker in log for Phase186 acceptance."""
    for line in live_peer._read_log(pathlib.Path(str(config["unityLog"]))).splitlines():
        try:
            protocol.parse_manual_completion_marker(
                line.strip(),
                case_id=str(config["caseId"]),
                run_id=str(config["runId"]),
                token=str(config["token"]),
                head=str(config["head"]),
            )
            return True
        except protocol.ProtocolFailure:
            continue
    return False


def _manual_editor_released_in_log(config: Mapping[str, Any]) -> bool:
    """Handle manual editor released in log for Phase186 acceptance."""
    prefix = "PHASE186_MANUAL_PLAY_EXITED "
    log = pathlib.Path(str(config["unityLog"]))
    for line in live_peer._read_log(log).splitlines():
        if not line.startswith(prefix):
            continue
        fields: dict[str, str] = {}
        for part in line[len(prefix) :].split():
            if "=" in part:
                key, value = part.split("=", 1)
                fields[key] = value
        if (
            fields.get("run") == str(config["runId"])
            and fields.get("case") == str(config["caseId"])
            and fields.get("tokenHash") == str(config["tokenHash"])
            and fields.get("head") == str(config["head"])
        ):
            return True
    return False


def _wait_manual_editor_release(
    config: Mapping[str, Any],
    reporter: Any | None,
    timeout_seconds: float = MANUAL_EDITOR_RELEASE_TIMEOUT_SECONDS,
) -> None:
    """Wait for manual editor release."""
    if reporter is not None:
        reporter.transition(
            "4/5", "Complete accepted; Unity is exiting Play Mode"
        )
    deadline = time.monotonic() + timeout_seconds
    while time.monotonic() < deadline:
        _mirror_manual_log(config)
        if _manual_editor_released_in_log(config):
            if reporter is not None:
                reporter.detail("Unity entered Edit Mode; cleanup can begin")
            return
        time.sleep(0.1)
    raise LiveFailure(
        "FAIL_TERMINAL",
        "Unity did not enter Edit Mode within the 300-second cleanup window",
    )


def _manual_scene_ready_in_log(config: Mapping[str, Any]) -> bool:
    """Handle manual scene ready in log for Phase186 acceptance."""
    prefix = "PHASE186_MANUAL_SCENE_READY "
    log = pathlib.Path(str(config["unityLog"]))
    for line in live_peer._read_log(log).splitlines():
        if not line.startswith(prefix):
            continue
        fields: dict[str, str] = {}
        for part in line[len(prefix) :].split():
            if "=" in part:
                key, value = part.split("=", 1)
                fields[key] = value
        if (
            fields.get("run") == str(config["runId"])
            and fields.get("case") == str(config["caseId"])
            and fields.get("tokenHash") == str(config["tokenHash"])
            and fields.get("head") == str(config["head"])
            and fields.get("schemaInfoChanged") == "false"
        ):
            return True
    return False


def _manual_scene_preparing_in_log(config: Mapping[str, Any]) -> bool:
    """Handle manual scene preparing in log for Phase186 acceptance."""
    prefix = "PHASE186_MANUAL_SCENE_PREPARING "
    log = pathlib.Path(str(config["unityLog"]))
    for line in live_peer._read_log(log).splitlines():
        if not line.startswith(prefix):
            continue
        fields: dict[str, str] = {}
        for part in line[len(prefix) :].split():
            if "=" in part:
                key, value = part.split("=", 1)
                fields[key] = value
        if (
            fields.get("run") == str(config["runId"])
            and fields.get("case") == str(config["caseId"])
            and fields.get("tokenHash") == str(config["tokenHash"])
            and fields.get("head") == str(config["head"])
        ):
            return True
    return False


def _manual_scene_prepare_failure(config: Mapping[str, Any]) -> str | None:
    """Handle manual scene prepare failure for Phase186 acceptance."""
    prefix = "PHASE186_MANUAL_SCENE_PREPARE_FAIL "
    log = pathlib.Path(str(config["unityLog"]))
    for line in live_peer._read_log(log).splitlines():
        if not line.startswith(prefix):
            continue
        fields: dict[str, str] = {}
        for part in line[len(prefix) :].split():
            if "=" in part:
                key, value = part.split("=", 1)
                fields[key] = value
        if (
            fields.get("run") == str(config["runId"])
            and fields.get("case") == str(config["caseId"])
            and fields.get("tokenHash") == str(config["tokenHash"])
            and fields.get("head") == str(config["head"])
        ):
            return fields.get("reason") or "unknown"
    return None


def _manual_context_failure(config: Mapping[str, Any]) -> str | None:
    """Handle manual context failure for Phase186 acceptance."""
    prefix = "PHASE186_ACCEPTANCE_CONTEXT_FAIL "
    log = pathlib.Path(str(config["unityLog"]))
    for line in live_peer._read_log(log).splitlines():
        if not line.startswith(prefix):
            continue
        identity, separator, reason = line[len(prefix) :].partition(" reason=")
        if not separator:
            continue
        fields: dict[str, str] = {}
        for part in identity.split():
            if "=" in part:
                key, value = part.split("=", 1)
                fields[key] = value
        if (
            fields.get("run") == str(config["runId"])
            and fields.get("case") == str(config["caseId"])
            and fields.get("tokenHash") == str(config["tokenHash"])
            and fields.get("head") == str(config["head"])
        ):
            return reason.strip() or "unknown"
    return None


def _wait_unity_ready(
    config: Mapping[str, Any],
    owner: OwnedLiveProcesses,
    timeout_seconds: float = protocol.COORDINATOR_UNITY_READY_TIMEOUT_SECONDS,
) -> None:
    """Wait for unity ready."""
    deadline = time.monotonic() + timeout_seconds
    while time.monotonic() < deadline:
        if _marker_in_log(config, "PHASE186_ACCEPTANCE_READY"):
            return
        if owner.poll("unity") is not None:
            raise LiveFailure("FAIL_PROCESS_EXIT", "Unity exited before readiness")
        time.sleep(0.05)
    raise LiveFailure("FAIL_TERMINAL", "Unity readiness expired")


def _unity_progress_documents(config: Mapping[str, Any]) -> tuple[Mapping[str, str], ...]:
    """Handle unity progress documents for Phase186 acceptance."""
    prefix = "PHASE186_ACCEPTANCE_PROGRESS "
    documents: list[Mapping[str, str]] = []
    log = pathlib.Path(str(config["unityLog"]))
    for line in live_peer._read_log(log).splitlines():
        if not line.startswith(prefix):
            continue
        fields: dict[str, str] = {}
        for part in line[len(prefix) :].split():
            if "=" in part:
                key, value = part.split("=", 1)
                fields[key] = value
        identity_matches = (
            fields.get("run") == str(config["runId"])
            and fields.get("case") == str(config["caseId"])
        )
        manual_identity_matches = (
            not bool(config.get("manual"))
            or (
                fields.get("tokenHash") == str(config["tokenHash"])
                and fields.get("head") == str(config["head"])
            )
        )
        if identity_matches and manual_identity_matches:
            documents.append(fields)
    return tuple(documents)


def _report_manual_progress(
    config: Mapping[str, Any],
    reporter: Any,
    emitted: set[str],
) -> None:
    """Translate exact current-run Editor progress into concise operator detail."""

    transitions = (
        (
            "provider-ready",
            lambda value: value.get("ready") == "true"
            and value.get("connected") == "true"
            and value.get("publish") == "Ready"
            and value.get("subscribe") == "Ready",
            "provider directions ready",
        ),
        (
            "external-a",
            lambda value: value.get("externalA") == "true",
            "external A observed",
        ),
        (
            "local-b",
            lambda value: value.get("generated") == "true",
            "local B published; waiting for peer verification",
        ),
        (
            "completion",
            lambda value: value.get("external") == "true"
            and value.get("generated") == "true"
            and value.get("caseSpecific") == "true",
            "peer verification complete; Complete enabled",
        ),
    )
    for document in _unity_progress_documents(config):
        for key, predicate, message in transitions:
            if key not in emitted and predicate(document):
                emitted.add(key)
                reporter.detail(message)


def _unity_progress_count(config: Mapping[str, Any]) -> int:
    """Handle unity progress count for Phase186 acceptance."""
    return len(_unity_progress_documents(config))


def _wait_unity_progress_after(
    config: Mapping[str, Any],
    owner: OwnedLiveProcesses,
    checkpoint: int,
    required: Mapping[str, str],
) -> None:
    """Wait for unity progress after."""
    deadline = time.monotonic() + live_peer.LIVE_ACTOR_OPERATION_TIMEOUT_SECONDS
    while time.monotonic() < deadline:
        documents = _unity_progress_documents(config)
        if any(
            all(document.get(key) == value for key, value in required.items())
            for document in documents[checkpoint:]
        ):
            return
        if owner.poll("unity") is not None:
            raise LiveFailure(
                "FAIL_PROCESS_EXIT",
                "Unity exited before the reconnect transition was observed",
            )
        time.sleep(0.05)
    state = ",".join(f"{key}={value}" for key, value in sorted(required.items()))
    raise LiveFailure(
        "FAIL_TERMINAL",
        "Unity reconnect transition expired: " + state,
    )


__all__ = [name for name in globals() if not name.startswith("__")]
