from __future__ import annotations
from .readiness_and_markers import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase186_bridge_live.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE


def _restart_sidecar_after_observed_disconnect(
    owner: OwnedLiveProcesses,
    runtime: PreparedRuntime,
    config: Mapping[str, Any],
    health_generations: list[Mapping[str, Any]],
) -> None:
    """Handle restart sidecar after observed disconnect for Phase186 acceptance."""
    disconnect_checkpoint = _unity_progress_count(config)
    owner.stop("sidecar-1")
    _wait_until_port_released(str(config["bridgeHost"]), int(config["bridgePort"]))
    _wait_unity_progress_after(
        config,
        owner,
        disconnect_checkpoint,
        {"ready": "false", "connected": "false"},
    )

    recovery_checkpoint = _unity_progress_count(config)
    _sidecar, health = _launch_sidecar(owner, runtime, config, "sidecar-2")
    health_generations.append(health)
    _wait_unity_progress_after(
        config,
        owner,
        recovery_checkpoint,
        {
            "ready": "true",
            "connected": "true",
            "publish": "Ready",
            "subscribe": "Ready",
        },
    )
    _write_exercise_gate(config)


def _write_identity_gate(config: Mapping[str, Any], key: str) -> pathlib.Path:
    """Write identity gate."""
    if key not in {"externalGate", "exerciseGate"}:
        raise LiveFailure("FAIL_PROTOCOL", "unknown Unity gate key")
    target = pathlib.Path(str(config[key]))
    value = {
        "schemaVersion": 1,
        "runId": config["runId"],
        "caseId": config["caseId"],
        "tokenHash": config["tokenHash"],
        "head": config["head"],
        "ready": True,
    }
    with tempfile.NamedTemporaryFile(
        mode="w",
        encoding="utf-8",
        newline="\n",
        dir=target.parent,
        prefix=target.name + ".",
        suffix=".tmp",
        delete=False,
    ) as stream:
        json.dump(value, stream, indent=2, sort_keys=True)
        stream.write("\n")
        temporary = pathlib.Path(stream.name)
    os.replace(temporary, target)
    return target


def _write_gate(config: Mapping[str, Any]) -> pathlib.Path:
    """Write gate."""
    return _write_identity_gate(config, "externalGate")


def _write_exercise_gate(config: Mapping[str, Any]) -> pathlib.Path:
    """Write exercise gate."""
    return _write_identity_gate(config, "exerciseGate")


def _parse_unity_evidence(
    config: Mapping[str, Any],
    *,
    require_pass_marker: bool = True,
) -> Mapping[str, Any]:
    """Parse unity evidence."""
    log = pathlib.Path(str(config["unityLog"]))
    text = live_peer._read_log(log)
    if (
        require_pass_marker
        and not _marker_in_log(config, "PHASE186_ACCEPTANCE_PASS")
    ):
        raise LiveFailure("FAIL_TERMINAL", "exact Unity PASS marker is absent")
    evidence_line = next(
        (
            line
            for line in reversed(text.splitlines())
            if line.startswith("PHASE186_ACCEPTANCE_EVIDENCE ")
            and f"run={config['runId']}" in line
            and f"case={config['caseId']}" in line
            and f"tokenHash={config['tokenHash']}" in line
        ),
        None,
    )
    if evidence_line is None:
        raise LiveFailure("FAIL_EVIDENCE", "exact Unity evidence marker is absent")
    fields: dict[str, str] = {}
    for part in evidence_line.split()[1:]:
        if "=" in part:
            key, value = part.split("=", 1)
            fields[key] = value
    numeric = {
        key: int(value)
        for key, value in fields.items()
        if key not in {"run", "case", "tokenHash"}
    }
    if numeric.get("sent", 0) <= 0 and any(
        live_peer._is_publish(kind)
        for kind in protocol.CASE_CONTRACT_KINDS[str(config["caseId"])]
    ):
        raise LiveFailure("FAIL_EVIDENCE", "Unity reported no sent Bridge frames")
    if any(
        live_peer._is_subscribe(kind)
        for kind in protocol.CASE_CONTRACT_KINDS[str(config["caseId"])]
    ) and (
        numeric.get("received", 0) <= 0
        or numeric.get("applied", 0) <= 0
    ):
        raise LiveFailure(
            "FAIL_EVIDENCE",
            "Unity reported no applied Bridge subscription frames",
        )
    if config["caseId"] == "slow-main-thread-640hz" and numeric.get("replaced", 0) <= 0:
        raise LiveFailure("FAIL_EVIDENCE", "slow-main-thread case reported no replacement")
    if config["caseId"] in {"reconnect-degraded-recovery", "lifecycle"}:
        if numeric.get("disconnectTransitions", 0) <= 0 or numeric.get("connectTransitions", 0) < 2:
            raise LiveFailure("FAIL_EVIDENCE", "reconnect transition evidence is incomplete")
    document = {
        "marker": evidence_line,
        "fields": numeric,
        "unityVersion": _unity_version(text),
    }
    path = pathlib.Path(str(config["outputRoot"])) / "unity-evidence.json"
    live_peer._write_json_atomic(path, document)
    return document


def _unity_version(log_text: str) -> str:
    """Handle unity version for Phase186 acceptance."""
    for line in log_text.splitlines():
        if "Version is '" in line:
            return line.split("Version is '", 1)[1].split("'", 1)[0]
        if "Initialize engine version:" in line:
            return line.split("Initialize engine version:", 1)[1].strip()
    return "unknown"


def _manual_editor_log() -> pathlib.Path:
    """Handle manual editor log for Phase186 acceptance."""
    local = os.environ.get("LOCALAPPDATA")
    if not local:
        raise LiveNotRun("LOCALAPPDATA for the Unity Editor log")
    return pathlib.Path(local) / "Unity" / "Editor" / "Editor.log"


def _mirror_manual_log(config: Mapping[str, Any]) -> None:
    """Handle mirror manual log for Phase186 acceptance."""
    source = _manual_editor_log()
    target = pathlib.Path(str(config["unityLog"]))
    text = live_peer._read_log(source)
    target.write_text(text, encoding="utf-8", newline="\n")


def _write_manual_pointer(repository: pathlib.Path, config: Mapping[str, Any]) -> pathlib.Path:
    """Write manual pointer."""
    pointer = repository / MANUAL_POINTER
    pointer.parent.mkdir(parents=True, exist_ok=True)
    if pointer.exists():
        try:
            current = json.loads(pointer.read_text(encoding="utf-8"))
        except (OSError, UnicodeError, json.JSONDecodeError) as exc:
            raise LiveFailure("FAIL_PREFLIGHT", "manual pointer is foreign or malformed") from exc
        if current.get("tokenHash") != config["tokenHash"]:
            raise LiveFailure("FAIL_PREFLIGHT", "another Phase186 manual run is active")
    shutil.copyfile(
        pathlib.Path(str(config["outputRoot"])) / "run-config.json",
        pointer,
    )
    return pointer


def _remove_manual_pointer(pointer: pathlib.Path | None, config: Mapping[str, Any]) -> None:
    """Remove manual pointer."""
    if pointer is None or not pointer.exists():
        return
    try:
        current = json.loads(pointer.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as exc:
        raise LiveFailure("FAIL_CLEANUP", "manual pointer changed during the run") from exc
    if current.get("tokenHash") != config["tokenHash"]:
        raise LiveFailure("FAIL_CLEANUP", "manual pointer ownership changed")
    pointer.unlink()


def _worker_roles(config: Mapping[str, Any]) -> tuple[str, ...]:
    """Handle worker roles for Phase186 acceptance."""
    return tuple(
        role for role in sorted(config["requiredActors"]) if role in WORKER_ROLES
    )


def _worker_process_roles(config: Mapping[str, Any]) -> tuple[str, ...]:
    """Return physical workers, cohosting graph APIs in the external ROS peer."""

    roles = _worker_roles(config)
    if "ros-peer" in roles and "graph-observer" in roles:
        return tuple(role for role in roles if role != "graph-observer")
    return roles


def _raise_if_worker_process_exited(
    config: Mapping[str, Any], owner: OwnedLiveProcesses
) -> None:
    """Surface an owned actor failure during the manual wait immediately."""

    output = pathlib.Path(str(config["outputRoot"]))
    for role in _worker_process_roles(config):
        return_code = owner.poll(role)
        if return_code is None:
            continue
        logical_roles = tuple(
            logical_role
            for logical_role in _worker_roles(config)
            if _owner_role_for_document(config, logical_role) == role
        )
        if logical_roles and all(
            _read_actor_document(config, logical_role, "result") is not None
            for logical_role in logical_roles
        ):
            continue
        failure = output / "actors" / f"{role}-failure.json"
        detail = ""
        if failure.is_file():
            try:
                detail = failure.read_text(encoding="utf-8")[:512]
            except (OSError, UnicodeError):
                detail = ""
        suffix = f": {detail}" if detail else ""
        raise LiveFailure(
            "FAIL_PROCESS_EXIT",
            f"{role} exited with code {return_code}{suffix}",
        )


def _owner_role_for_document(config: Mapping[str, Any], role: str) -> str:
    """Resolve the owned process that is responsible for one logical document."""

    roles = _worker_roles(config)
    if role == "graph-observer" and "ros-peer" in roles:
        return "ros-peer"
    return role


def _observation_path(
    config: Mapping[str, Any], worker_results: Mapping[str, Mapping[str, Any]], name: str
) -> pathlib.Path:
    """Handle observation path for Phase186 acceptance."""
    output = pathlib.Path(str(config["outputRoot"]))
    if name == "unity":
        return output / "unity-evidence.json"
    if name == "bridge":
        return output / "bridge-evidence.json"
    if name == "packages":
        return output / "preflight.json"
    if name == "resources":
        return output / "cleanup.json"
    if name in {"graph", "qos"} and "graph-observer" in worker_results:
        return output / "actors" / "graph-observer-result.json"
    if name in {"data", "origin", "peer"}:
        for role in ("ros-peer", "hostile-peer", "wire-peer", "foxglove-client"):
            if role in worker_results:
                return output / "actors" / f"{role}-result.json"
    raise LiveFailure("FAIL_EVIDENCE", f"no live evidence path exists for {name}")


def _cleanup_document(
    config: Mapping[str, Any],
    owner: OwnedLiveProcesses,
    pointer: pathlib.Path | None,
    *,
    extra_endpoints: Sequence[tuple[str, int]] = (),
    cleanup_errors: Sequence[str] = (),
) -> dict[str, Any]:
    """Clean up document."""
    residual_ports: list[int] = []
    endpoints = (
        (str(config["bridgeHost"]), int(config["bridgePort"])),
        (str(config["foxgloveHost"]), int(config["foxglovePort"])),
        *tuple(extra_endpoints),
    )
    for host, port in endpoints:
        try:
            with socket.create_connection((host, port), timeout=0.1):
                residual_ports.append(port)
        except OSError:
            pass
    residual_files = []
    for candidate in (
        pointer,
        pathlib.Path(str(config["externalGate"])),
        pathlib.Path(str(config["exerciseGate"])),
    ):
        if candidate is not None and candidate.exists():
            residual_files.append(str(candidate.resolve()))
    bounded_errors = [str(value)[:512] for value in cleanup_errors]
    result = {
        "complete": not owner.residual_pids()
        and not residual_ports
        and not residual_files
        and not bounded_errors,
        "cleanupErrors": bounded_errors,
        "residualProcesses": owner.residual_pids(),
        "residualPorts": residual_ports,
        "residualOverlays": [],
        "residualTemporaryProjects": residual_files,
    }
    live_peer._write_json_atomic(
        pathlib.Path(str(config["outputRoot"])) / "cleanup.json", result
    )
    return result




__all__ = [name for name in globals() if not name.startswith("__")]
