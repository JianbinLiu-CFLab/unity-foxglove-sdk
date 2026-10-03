from __future__ import annotations
from .owned_process_and_protocol import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase184_profile_acceptance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE


def _states_from_dispatch_marker(marker: TerminalMarker) -> dict[str, str]:
    """Derive endpoint states from one observed publish dispatch."""

    succeeded = _split_runtime_endpoints(marker.fields.get("succeeded"))
    failed = _split_runtime_endpoints(marker.fields.get("failed"))
    if set(succeeded) & set(failed):
        raise AcceptanceFailure(
            "FAIL_FANOUT",
            "Unity runtime dispatch marks one endpoint both succeeded and failed.",
        )
    return {
        **{_endpoint_state_key(endpoint): "Ready" for endpoint in succeeded},
        **{_endpoint_state_key(endpoint): "Unavailable" for endpoint in failed},
    }


def _observed_target_evidence(
    config: Mapping[str, object],
    terminal: TerminalMarker,
) -> dict[str, object]:
    """Consume target state and diagnostic counters observed by Unity."""

    case = str(config["case"])
    if case == "foxglove-profile":
        marker = _require_latest_runtime_marker(
            config,
            "PHASE184G_FOXGLOVE_TARGET_STATUS",
        )
        topics = _runtime_marker_int(marker, "topics")
        failed = _split_runtime_endpoints(marker.fields.get("failed"))
        return {
            "states": _states_from_dispatch_marker(marker),
            "diagnosticCounts": {"failedTargets": len(failed)},
            "statusEvidence": {
                "aggregate": marker.fields.get("status"),
                "succeeded": marker.fields.get("succeeded"),
                "failed": marker.fields.get("failed"),
                "topics": topics,
            },
        }
    if case == "multi-target":
        marker = _require_latest_runtime_marker(
            config,
            "PHASE184G_MULTI_TARGET_STATUS",
        )
        failed = _split_runtime_endpoints(marker.fields.get("failed"))
        bridge_failures = _runtime_marker_int(
            marker,
            "bridgeRuntimeFailures",
        )
        return {
            "states": _states_from_dispatch_marker(marker),
            "diagnosticCounts": {
                "failedTargets": len(failed),
                "bridgeRuntimeFailures": bridge_failures,
            },
            "statusEvidence": {
                "aggregate": marker.fields.get("status"),
                "succeeded": marker.fields.get("succeeded"),
                "failed": marker.fields.get("failed"),
                "bridgeRuntimeFailures": bridge_failures,
            },
        }
    if case == "degraded-target":
        bridge_diagnostics = _marker_int(terminal, "bridgeDiagnostics")
        aggregate_status = terminal.fields.get("status")
        succeeded_targets = terminal.fields.get("succeeded")
        failed_targets = terminal.fields.get("failed")
        foxglove_state = terminal.fields.get("foxgloveState")
        bridge_state = terminal.fields.get("ros2BridgeState")
        if (
            aggregate_status != "Degraded"
            or succeeded_targets != "Foxglove"
            or failed_targets != "Ros2Bridge"
            or foxglove_state != "Ready"
            or bridge_state != "Unavailable"
            or bridge_diagnostics != 1
        ):
            raise AcceptanceFailure(
                "FAIL_FANOUT",
                "Unity did not report the exact degraded Bridge status transition.",
            )
        return {
            "states": {
                "foxglove": foxglove_state,
                "ros2Bridge": bridge_state,
            },
            "diagnosticCounts": {
                "failedTargets": 1,
                "bridgeDiagnostics": bridge_diagnostics,
            },
            "statusEvidence": {
                "aggregate": aggregate_status,
                "succeeded": succeeded_targets,
                "failed": failed_targets,
                "bridgeDiagnostics": bridge_diagnostics,
            },
        }
    if case == "qos-contract":
        markers = _correlated_runtime_markers(
            config,
            "PHASE184G_QOS_TARGET_STATUS",
        )
        by_topic: dict[str, TerminalMarker] = {}
        for marker in markers:
            topic = marker.fields.get("topic")
            if topic:
                by_topic[topic] = marker
        expected_topics = {str(topic) for topic in config["topics"]}
        if set(by_topic) != expected_topics:
            raise AcceptanceFailure(
                "FAIL_FANOUT",
                "Unity QoS target evidence does not cover the exact topic set.",
            )
        status_by_topic: dict[str, object] = {}
        states: dict[str, str] = {}
        failed_count = 0
        for topic in sorted(expected_topics):
            marker = by_topic[topic]
            failed = _split_runtime_endpoints(marker.fields.get("failed"))
            failed_count += len(failed)
            status = str(marker.fields.get("status", ""))
            states[topic] = status
            status_by_topic[topic] = {
                "aggregate": status,
                "succeeded": marker.fields.get("succeeded"),
                "failed": marker.fields.get("failed"),
            }
        return {
            "states": states,
            "diagnosticCounts": {"failedTargets": failed_count},
            "statusEvidence": {"topics": status_by_topic},
        }
    if case == "stream-640hz":
        marker = _require_latest_runtime_marker(
            config,
            "PHASE184G_STREAM_SUBSCRIPTION_STATUS",
        )
        binding_state = str(marker.fields.get("state", ""))
        received = _runtime_marker_int(marker, "received")
        copy_failed = _runtime_marker_int(marker, "copyFailed")
        stale_callbacks = _runtime_marker_int(marker, "staleCallbacks")
        rejected_after_stop = _runtime_marker_int(marker, "rejectedAfterStop")
        return {
            "states": {
                "ros2Native": (
                    "Ready"
                    if binding_state in {"Ready", "Receiving"}
                    else "Unavailable"
                )
            },
            "diagnosticCounts": {
                "copyFailed": copy_failed,
                "staleCallbacks": stale_callbacks,
                "rejectedAfterStop": rejected_after_stop,
            },
            "statusEvidence": {
                "bindingState": binding_state,
                "received": received,
                "copyFailed": copy_failed,
                "staleCallbacks": stale_callbacks,
                "rejectedAfterStop": rejected_after_stop,
            },
        }
    raise AcceptanceFailure("FAIL_TERMINAL", "Unknown case target evidence mapping.")


def _actor_path(config: Mapping[str, object], collection: str, role: str) -> pathlib.Path:
    """Handle the actor path step."""

    paths = config.get(collection)
    if not isinstance(paths, Mapping) or role not in paths:
        raise AcceptanceFailure("FAIL_PREFLIGHT", f"{role} has no configured {collection} path.")
    return pathlib.Path(str(paths[role]))


def write_actor_ready(
    config: Mapping[str, object],
    role: str,
    details: Mapping[str, object],
) -> None:
    """Atomically publish one current-run worker readiness document."""

    write_private_json_atomic(
        _actor_path(config, "readyFiles", role),
        {
            "schemaVersion": 1,
            "runId": config["runId"],
            "case": config["case"],
            "role": role,
            "tokenSha256": protocol.token_sha256(str(config["token"])),
            "ready": True,
            "details": dict(details),
        },
    )


def write_actor_result(
    config: Mapping[str, object],
    role: str,
    *,
    verdict: str,
    evidence: Mapping[str, object],
) -> None:
    """Atomically publish bounded current-run worker evidence."""

    write_private_json_atomic(
        _actor_path(config, "resultFiles", role),
        {
            "schemaVersion": 1,
            "runId": config["runId"],
            "case": config["case"],
            "role": role,
            "tokenSha256": protocol.token_sha256(str(config["token"])),
            "verdict": verdict,
            "evidence": dict(evidence),
        },
    )


def read_actor_document(
    config: Mapping[str, object],
    role: str,
    collection: str,
    *,
    require_pass: bool = True,
) -> dict[str, object]:
    """Read one exact actor document and reject stale or synthetic evidence."""

    path = _actor_path(config, collection, role)
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, UnicodeError, json.JSONDecodeError) as exc:
        raise AcceptanceFailure(
            "FAIL_TERMINAL",
            f"{role} did not produce valid {collection} evidence.",
        ) from exc
    if not isinstance(value, dict):
        raise AcceptanceFailure("FAIL_TERMINAL", f"{role} evidence is not an object.")
    expected_identity = {
        "schemaVersion": 1,
        "runId": config["runId"],
        "case": config["case"],
        "role": role,
        "tokenSha256": protocol.token_sha256(str(config["token"])),
    }
    if any(value.get(key) != expected for key, expected in expected_identity.items()):
        raise AcceptanceFailure("FAIL_TERMINAL", f"{role} evidence is stale or mismatched.")
    if collection == "readyFiles":
        if set(value) != {*expected_identity, "ready", "details"} or value["ready"] is not True:
            raise AcceptanceFailure("FAIL_TERMINAL", f"{role} readiness is malformed.")
    elif collection == "resultFiles":
        if set(value) != {*expected_identity, "verdict", "evidence"}:
            raise AcceptanceFailure("FAIL_TERMINAL", f"{role} result is malformed.")
        verdict = value["verdict"]
        if (
            not isinstance(verdict, str)
            or (
                verdict != "PASS"
                and re.fullmatch(r"(?:FAIL|BLOCKED)_[A-Z0-9_]+", verdict) is None
            )
            or not isinstance(value["evidence"], Mapping)
        ):
            raise AcceptanceFailure("FAIL_TERMINAL", f"{role} result is malformed.")
        if require_pass and verdict != "PASS":
            raise AcceptanceFailure("FAIL_TERMINAL", f"{role} did not report PASS evidence.")
    else:
        raise ValueError("Unknown actor document collection.")
    return value


def require_finished_actor_pass(
    config: Mapping[str, object],
    role: str,
) -> None:
    """Surface one finished worker's persisted verdict without waiting for Unity."""

    result = read_actor_document(
        config,
        role,
        "resultFiles",
        require_pass=False,
    )
    verdict = str(result["verdict"])
    if verdict == "PASS":
        return
    evidence = result["evidence"]
    diagnostic = evidence.get("diagnostic") if isinstance(evidence, Mapping) else None
    detail = (
        diagnostic.strip()
        if isinstance(diagnostic, str) and diagnostic.strip()
        else "the worker reported a non-PASS result"
    )
    raise AcceptanceFailure(
        verdict,
        f"{role} finished before manual completion: {detail}",
    )


def read_log_lines(path: pathlib.Path) -> list[str]:
    """Read one dedicated bounded-size current run log."""

    try:
        size = pathlib.Path(path).stat().st_size
        if size > 64 * 1024 * 1024:
            raise AcceptanceFailure("FAIL_TERMINAL", "Unity log exceeded the acceptance bound.")
        return pathlib.Path(path).read_text(encoding="utf-8", errors="replace").splitlines()
    except FileNotFoundError:
        return []
    except OSError as exc:
        raise AcceptanceFailure("FAIL_TERMINAL", "Unity log could not be read.") from exc


def wait_for_log_marker(
    config: Mapping[str, object],
    marker: str,
    timeout_seconds: float,
) -> str:
    """Wait for one current case/token marker in the dedicated Unity log."""

    deadline = time.monotonic() + timeout_seconds
    case = str(config["case"])
    token = str(config["token"])
    log_path = pathlib.Path(str(config["unityLog"]))
    while True:
        for line in read_log_lines(log_path):
            if marker not in line:
                continue
            fields = _parse_marker_fields(line.strip())
            if fields.get("case") == case and fields.get("token") == token:
                return line.strip()
        terminal = find_terminal_marker(read_log_lines(log_path), case, token)
        if terminal is not None and terminal.verdict == "FAIL":
            raise AcceptanceFailure("FAIL_TERMINAL", "Unity reported a correlated case failure.")
        remaining = deadline - time.monotonic()
        if remaining <= 0:
            raise AcceptanceFailure("FAIL_TERMINAL", f"Unity marker {marker} did not arrive.")
        time.sleep(min(0.1, remaining))


def wait_for_terminal_marker(
    config: Mapping[str, object],
    timeout_seconds: float,
) -> TerminalMarker:
    """Wait for the current run's correlated Unity PASS/FAIL."""

    deadline = time.monotonic() + timeout_seconds
    case = str(config["case"])
    token = str(config["token"])
    path = pathlib.Path(str(config["unityLog"]))
    while True:
        marker = find_terminal_marker(read_log_lines(path), case, token)
        if marker is not None:
            if marker.verdict != "PASS":
                raise AcceptanceFailure("FAIL_TERMINAL", "Unity reported a correlated case failure.")
            return marker
        remaining = deadline - time.monotonic()
        if remaining <= 0:
            raise AcceptanceFailure("FAIL_TERMINAL", "Unity emitted no correlated terminal marker.")
        time.sleep(min(0.1, remaining))


def _wait_for_unity_context(config: Mapping[str, object]) -> None:
    """Start finite actor windows only after the correlated Play session exists."""

    wait_for_log_marker(
        config,
        "PHASE184G_CONTEXT_READY",
        900.0,
    )


def _message_contains_stage(value: object, expected: str) -> bool:
    """Search decoded JSON/Protobuf values for one exact correlation stage."""

    if isinstance(value, str):
        return value == expected
    if isinstance(value, Mapping):
        return any(_message_contains_stage(nested, expected) for nested in value.values())
    if isinstance(value, (list, tuple)):
        return any(_message_contains_stage(nested, expected) for nested in value)
    descriptor = getattr(value, "DESCRIPTOR", None)
    if descriptor is not None:
        for field, nested in value.ListFields():
            if field.label == field.LABEL_REPEATED:
                if _message_contains_stage(list(nested), expected):
                    return True
            elif _message_contains_stage(nested, expected):
                return True
    return False


__all__ = [name for name in globals() if not name.startswith("__")]
