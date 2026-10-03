from __future__ import annotations
from .build_publish_command_and_build_negative_publish_command import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase179_foxrun_ros2_inbound_acceptance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
def _read_unity_log(log_path: pathlib.Path, start_offset: int | None) -> str:
    """Read all or only post-publication Unity log content without accepting a truncated log."""

    if not log_path.is_file():
        raise AcceptanceFailure("UNITY_LOG", "Unity log is unavailable while waiting for acceptance evidence.")
    if start_offset is None:
        return log_path.read_text(encoding="utf-8", errors="replace")
    if start_offset < 0:
        raise ValueError("start_offset must be non-negative")
    if log_path.stat().st_size < start_offset:
        raise AcceptanceFailure("UNITY_LOG", "Unity log was truncated during acceptance observation.")
    with log_path.open("rb") as stream:
        stream.seek(start_offset)
        return stream.read().decode("utf-8", errors="replace")
def capture_unity_ready_marker_tokens(log_path: pathlib.Path, runtime: str, rmw: str) -> frozenset[str]:
    """Snapshot matching READY tokens before local launch without assuming Unity's Editor.log grows append-only."""

    return frozenset(
        marker.token
        for marker in parse_unity_ready_markers(_read_unity_log(log_path, None))
        if marker.runtime == runtime and marker.rmw == rmw
    )
def find_matching_unity_marker(
    text: str,
    topic: str,
    token: str,
    expected_value: dict[str, object],
) -> UnityMarker:
    """Return a fully matching marker or a stable proof/value failure."""

    matches = [marker for marker in parse_unity_markers(text) if marker.topic == topic and marker.token == token]
    if not matches:
        raise AcceptanceFailure("UNITY_TIMEOUT", "Unity did not yet emit a matching applied marker.")
    for marker in reversed(matches):
        if marker.received <= 0 or marker.applied <= 0 or marker.applied > marker.received or marker.replaced < 0:
            continue
        if marker.value == expected_value:
            return marker
    raise AcceptanceFailure("VALUE_MISMATCH", "Unity marker counters or copied value did not match the published contract.")
def wait_for_unity_marker(
    log_path: pathlib.Path,
    topic: str,
    token: str,
    expected_value: dict[str, object],
    timeout_seconds: float,
    start_offset: int | None = None,
) -> UnityMarker:
    """Wait for a matching Unity marker, optionally only from post-publication log content."""

    deadline = time.monotonic() + timeout_seconds
    last_value_failure: AcceptanceFailure | None = None
    while True:
        if log_path.is_file():
            text = _read_unity_log(log_path, start_offset)
            try:
                return find_matching_unity_marker(text, topic, token, expected_value)
            except AcceptanceFailure as exc:
                if exc.category == "VALUE_MISMATCH":
                    last_value_failure = exc
        if time.monotonic() >= deadline:
            if last_value_failure is not None:
                raise last_value_failure
            raise AcceptanceFailure("UNITY_TIMEOUT", "Unity did not emit the matching applied marker before timeout.")
        time.sleep(min(0.25, max(0.0, deadline - time.monotonic())))
def wait_for_unity_ready_marker(
    log_path: pathlib.Path,
    runtime: str,
    rmw: str,
    token: str | None,
    timeout_seconds: float,
    start_offset: int | None = None,
    excluded_tokens: Sequence[str] = (),
) -> UnityReadyMarker:
    """Wait for a Unity native-runtime marker after an optional log offset and token check."""

    deadline = time.monotonic() + timeout_seconds
    last_mismatch: AcceptanceFailure | None = None
    while True:
        if log_path.is_file():
            try:
                return find_matching_unity_ready_marker(
                    _read_unity_log(log_path, start_offset), runtime, rmw, token, excluded_tokens
                )
            except AcceptanceFailure as exc:
                if exc.category in ("READY_MISMATCH", "READY_STALE"):
                    last_mismatch = exc
        if time.monotonic() >= deadline:
            if last_mismatch is not None:
                raise last_mismatch
            raise AcceptanceFailure("READY_TIMEOUT", "Unity did not emit the expected native runtime READY marker before timeout.")
        time.sleep(min(0.25, max(0.0, deadline - time.monotonic())))
def classify_verdict(
    *,
    unity_log_available: bool,
    message_results: Sequence[Mapping[str, object]],
    failure: AcceptanceFailure | None,
) -> str:
    """Classify results without treating publish-only evidence as a PASS."""

    if failure is not None:
        return f"FAIL_{failure.category}"
    if not unity_log_available:
        return "PEER_PUBLISH_COMPLETE_UNITY_PROOF_PENDING"
    if not message_results or not all(result.get("published") and result.get("unityProof") for result in message_results):
        return "FAIL_UNITY_TIMEOUT"
    return "PASS"
def classify_negative_verdict(
    *,
    negative_case: str,
    unity_log_available: bool,
    expectation_observed: bool,
    unity_ready: bool,
    contract_identity: bool,
    unity_no_apply: bool,
    failure: AcceptanceFailure | None,
) -> str:
    """Keep an expected rejection distinct from successful positive interoperability."""

    if failure is not None:
        return f"FAIL_{failure.category}"
    if not expectation_observed:
        return "FAIL_NEGATIVE_EXPECTATION"
    normalized_case = negative_case.upper().replace("-", "_")
    if not unity_log_available:
        return f"LOCAL_NEGATIVE_EVIDENCE_{normalized_case}_UNITY_PROOF_PENDING"
    if not unity_ready:
        return "FAIL_READY"
    if not contract_identity:
        return "FAIL_CONTRACT_IDENTITY"
    if not unity_no_apply:
        return "FAIL_NEGATIVE_APPLY"
    return f"EXPECTED_NEGATIVE_{normalized_case}"
def unity_log_offset(log_path: pathlib.Path) -> int:
    """Capture an append-only Unity log offset before a deliberate no-data probe."""

    if not log_path.is_file():
        raise AcceptanceFailure("UNITY_LOG", "--unity-log was supplied but does not exist for negative acceptance.")
    return log_path.stat().st_size
def wait_for_no_unity_apply_after_offset(
    log_path: pathlib.Path,
    topic: str,
    offset: int,
    timeout_seconds: float,
) -> None:
    """Require a bounded observation window with no new applied marker for a native topic."""

    deadline = time.monotonic() + timeout_seconds
    while True:
        if not log_path.is_file():
            raise AcceptanceFailure("UNITY_LOG", "Unity log disappeared during negative acceptance observation.")
        if log_path.stat().st_size < offset:
            raise AcceptanceFailure("UNITY_LOG", "Unity log was truncated during negative acceptance observation.")
        with log_path.open("rb") as stream:
            stream.seek(offset)
            appended = stream.read().decode("utf-8", errors="replace")
        if any(marker.topic == topic for marker in parse_unity_markers(appended)):
            raise AcceptanceFailure("NEGATIVE_APPLY", "Unity applied data during an expected-rejection probe.")
        if time.monotonic() >= deadline:
            return
        time.sleep(min(0.25, max(0.0, deadline - time.monotonic())))
def topic_list_has_topic(output: str, topic: str) -> bool:
    """Return whether ``ros2 topic list -t`` exposed any type for a named topic."""

    return any(line.strip().startswith(f"{topic} [") for line in output.splitlines())
def wait_for_unity_subscription_absence(
    ros2_executable: pathlib.Path,
    env: Mapping[str, str],
    topic: str,
    timeout_seconds: float,
) -> None:
    """Observe a bounded graph window where an intentionally mismatched-RMW Unity peer is absent."""

    deadline = time.monotonic() + timeout_seconds
    observed_graph = False
    while True:
        remaining = deadline - time.monotonic()
        if remaining < 0.0:
            break
        result = run_bounded_command(
            [str(ros2_executable), "topic", "list", "-t", "--no-daemon"],
            env,
            max(0.25, min(5.0, remaining or 0.25)),
            "ros2 topic list",
        )
        if not result.timed_out and result.return_code == 0:
            observed_graph = True
            if topic_list_has_topic(result.output, topic):
                raise AcceptanceFailure(
                    "NEGATIVE_EXPECTATION",
                    "A Unity endpoint was discovered despite the requested RMW mismatch; no fallback transport was attempted.",
                )
        if time.monotonic() >= deadline:
            break
        time.sleep(min(0.5, max(0.0, deadline - time.monotonic())))
    if not observed_graph:
        raise AcceptanceFailure("DISCOVERY", "ROS2 topic list did not produce graph evidence for the RMW mismatch probe.")
def _negative_command_outcome(result: CommandResult) -> str:
    """Return a safe, output-free description of one negative publication attempt."""

    if result.timed_out:
        return "timed-out-no-match"
    if result.return_code == 0:
        return "completed"
    return "rejected"
def verify_current_unity_ready_identity(args: argparse.Namespace, expected_rmw: str) -> bool:
    """Require an explicitly identified current Unity native runtime when log proof is requested."""

    if args.unity_log is None or args.unity_ready_token is None:
        return False
    wait_for_unity_ready_marker(
        args.unity_log,
        args.distro,
        expected_rmw,
        args.unity_ready_token,
        args.timeout_seconds,
    )
    return True
def run_negative_case(
    args: argparse.Namespace,
    env: Mapping[str, str],
    ros2_executable: pathlib.Path,
    token: str,
) -> dict[str, object]:
    """Run one bounded expected-rejection probe without mutating the selected ROS transport."""

    if args.negative_case is None or len(args.message_set) != 1:
        raise ValueError("run_negative_case requires one parsed negative case and one message contract")
    spec = MESSAGE_SPECS[args.message_set[0]]
    topic = topic_for_spec(args.topic_prefix, spec)
    result: dict[str, object] = {
        "name": spec.name,
        "topic": topic,
        "negativeCase": args.negative_case,
        "expectationObserved": False,
        "unityReady": False,
        "contractIdentity": False,
        "unityNoApply": False,
        "unityCounterUnchanged": False,
    }
    interface = run_bounded_command(
        [str(ros2_executable), "interface", "show", spec.message_type],
        env,
        min(10.0, args.timeout_seconds),
        "ros2 interface show",
    )
    require_command_success(interface, "ENDPOINT", "ros2 interface show")

    observation_seconds = min(3.0, args.timeout_seconds)
    if args.negative_case == "rmw-mismatch":
        result["expectedPeerRmw"] = args.negative_peer_rmw
        result["unityReady"] = verify_current_unity_ready_identity(args, args.negative_peer_rmw)
        result["contractIdentity"] = result["unityReady"]
        log_offset = unity_log_offset(args.unity_log) if bool(result["unityReady"]) else None
        wait_for_unity_subscription_absence(ros2_executable, env, topic, args.timeout_seconds)
        result["graph"] = {"topicVisible": False}
        result["expectationObserved"] = True
        if log_offset is not None:
            wait_for_no_unity_apply_after_offset(args.unity_log, topic, log_offset, observation_seconds)
            result["unityNoApply"] = True
            result["unityCounterUnchanged"] = True
        return result

    wait_for_unity_subscription_topic(ros2_executable, env, topic, spec.message_type, args.timeout_seconds)
    endpoint = query_unity_subscription_endpoint(ros2_executable, env, topic, spec, args.timeout_seconds)
    result["graph"] = {
        "messageType": endpoint.message_type,
        "subscriptionCount": endpoint.subscription_count,
        "qosReliability": endpoint.qos_reliability,
        "qosHistory": endpoint.qos_history,
        "qosDepth": endpoint.qos_depth,
        "qosDurability": endpoint.qos_durability,
    }
    result["unityReady"] = verify_current_unity_ready_identity(args, args.rmw)
    if bool(result["unityReady"]):
        baseline_offset = unity_log_offset(args.unity_log)
        positive = run_bounded_command(
            build_publish_command(ros2_executable, spec, token, args.topic_prefix),
            env,
            args.timeout_seconds,
            f"ros2 topic pub current-contract {spec.name}",
        )
        require_command_success(positive, "PUBLISH", f"ros2 topic pub current-contract {spec.name}")
        baseline = wait_for_unity_marker(
            args.unity_log,
            topic,
            token,
            spec.expected_value(token),
            args.timeout_seconds,
            start_offset=baseline_offset,
        )
        result["contractIdentity"] = True
        result["identitySession"] = baseline.session
    log_offset = unity_log_offset(args.unity_log) if bool(result["contractIdentity"]) else None
    topic = topic_for_spec(args.topic_prefix, spec)
    negative_command = build_negative_publish_command(ros2_executable, spec, token, args.negative_case, args.topic_prefix)
    result["attemptedMessageType"] = negative_command[negative_command.index(topic) + 1]
    result["attemptedQosReliability"] = negative_command[negative_command.index("--qos-reliability") + 1]
    published = run_bounded_command(
        negative_command,
        env,
        args.timeout_seconds,
        f"ros2 topic pub negative {args.negative_case}",
    )
    result["negativePublishOutcome"] = _negative_command_outcome(published)
    if args.negative_case == "type-mismatch":
        result["typeMismatchObserved"] = endpoint.message_type != result["attemptedMessageType"]
        if not result["typeMismatchObserved"]:
            raise AcceptanceFailure("NEGATIVE_EXPECTATION", "ROS2 graph did not retain a type mismatch for the negative probe.")
    else:
        result["qosMismatchObserved"] = endpoint.qos_reliability != result["attemptedQosReliability"]
        if not result["qosMismatchObserved"]:
            raise AcceptanceFailure("NEGATIVE_EXPECTATION", "ROS2 graph did not retain a QoS mismatch for the negative probe.")
    result["expectationObserved"] = True
    if log_offset is not None:
        wait_for_no_unity_apply_after_offset(args.unity_log, topic, log_offset, observation_seconds)
        result["unityNoApply"] = True
        result["unityCounterUnchanged"] = True
    return result
def sanitize_summary(value: object) -> object:
    """Remove machine-specific Zenoh paths and secret-bearing diagnostic text."""

    if isinstance(value, Mapping):
        sanitized: dict[str, object] = {}
        for key, child in value.items():
            lower = str(key).lower()
            if any(part in lower for part in _SENSITIVE_KEY_PARTS):
                continue
            if lower == "error":
                sanitized[str(key)] = "redacted"
                continue
            sanitized[str(key)] = sanitize_summary(child)
        return sanitized
    if isinstance(value, list):
        return [sanitize_summary(child) for child in value]
    if isinstance(value, str) and _SENSITIVE_VALUE_RE.search(value):
        return "redacted"
    return value
def write_summary(path: pathlib.Path, summary: Mapping[str, object]) -> None:
    """Persist portable evidence JSON outside package source directories."""

    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(sanitize_summary(summary), indent=2, sort_keys=True) + "\n", encoding="utf-8")
def configure_zenoh_topology(
    args: argparse.Namespace,
    env: dict[str, str],
) -> zenoh_topology.ZenohTopologyHandle:
    """Start or select one explicit Zenoh topology and wait for its real ready marker."""

    try:
        options = zenoh_topology.validate_topology_options(
            args.rmw,
            router=args.zenoh_router,
            no_router=args.no_zenoh_router,
            topology_id=args.zenoh_topology_id,
        )
        return zenoh_topology.start_topology(
            options,
            env=env,
            cwd=workspace_root(),
            log_path=args.summary_json.with_name(args.summary_json.stem + "-zenoh-router.log"),
            ready_timeout_seconds=args.timeout_seconds,
            ready_marker=args.zenoh_router_ready_marker,
        )
    except zenoh_topology.ZenohTopologyError as exc:
        raise AcceptanceFailure(exc.category, "Zenoh topology setup did not complete.") from exc
    except ValueError as exc:
        raise AcceptanceFailure("ENVIRONMENT", "Zenoh topology arguments were invalid.") from exc
def topology_summary(configured: zenoh_topology.ZenohTopologyHandle | str) -> dict[str, object]:
    """Return portable topology evidence without leaking router or session-config paths."""

    if isinstance(configured, str):
        return {"mode": configured, "readiness": configured}
    return {"mode": configured.mode, "readiness": configured.readiness}
def close_configured_topology(configured: zenoh_topology.ZenohTopologyHandle | str | None) -> None:
    """Release only a topology process actually owned by this helper."""

    if isinstance(configured, zenoh_topology.ZenohTopologyHandle):
        zenoh_topology.close_topology(configured)
def collect_optional_windows_peer_diagnostic(args: argparse.Namespace) -> str:
    """Use the shared Windows helper only when an operator explicitly asks for it."""

    if args.ros2_root is None:
        return "not-requested"
    try:
        import _ros2_windows_env as ros2env

        root = args.ros2_root.resolve(strict=True)
        pixi_python, ros2_script = ros2env.validate_ros2_root(root)
        env = ros2env.build_ros_env(root, args.rmw, args.discovery_range, str(args.domain_id), args.distro)
        result = ros2env.run_ros2(
            pixi_python,
            ros2_script,
            env,
            ["topic", "list", "-t", "--no-daemon"],
            check=False,
            timeout_seconds=5.0,
        )
        return "available" if result.returncode == 0 else "unavailable"
    except (FileNotFoundError, RuntimeError, subprocess.TimeoutExpired):
        return "unavailable"


__all__ = [name for name in globals() if not name.startswith("__")]
