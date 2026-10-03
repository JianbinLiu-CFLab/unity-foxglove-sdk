from __future__ import annotations
from .evaluate_graph_evidence_and_summarize_graph_endpoints import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase181_custom_ros2_peer.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
def run_typed_worker(args: argparse.Namespace) -> int:
    """Run the real generated-envelope peer loop after an owned workspace is built."""

    if args.unity_log is None or args.worker_result_json is None:
        raise PeerFailure("FAIL_WORKER_ARGUMENTS", "The typed worker requires a Unity log and owned result path.")
    root = workspace_root()
    static_package = pathlib.Path(args.static_interface_package or default_static_interface_package(root))
    lock = load_static_interface_lock(static_package)
    protocol.require_interface_digest(lock.interface_digest, args.interface_digest)
    probe_role = _normalize_probe_role(args.probe_role)
    if args.surface == "player" and probe_role != "orchestrate":
        raise PeerFailure("FAIL_ARGUMENTS", "Windows Player acceptance requires the complete orchestrated custom-interface proof.")
    requires_outbound, requires_bidirectional, _ = _role_requires(probe_role)
    try:
        import rclpy
        from rclpy.qos import HistoryPolicy, QoSProfile, ReliabilityPolicy
    except ImportError as exc:
        raise PeerFailure("FAIL_PEER_RUNTIME", "The selected peer Python cannot import rclpy.") from exc

    envelope_type, payload_type, nested_type = _load_generated_message_types(lock)
    qos = QoSProfile(depth=10, reliability=ReliabilityPolicy.RELIABLE, history=HistoryPolicy.KEEP_LAST)
    rclpy.init(args=None)
    try:
        (
            node,
            received_publish,
            received_bidirectional,
            subscribe_publisher,
            bidirectional_publisher,
        ) = create_typed_worker_endpoints(
            rclpy,
            envelope_type,
            "phase181_custom_peer_" + str(os.getpid()),
            qos,
            DEFAULT_TOPICS["publish"],
            DEFAULT_TOPICS["subscribe"],
            DEFAULT_TOPICS["bidirectional"],
        )
    except BaseException:
        try:
            rclpy.shutdown()
        except Exception:  # noqa: BLE001 - setup failure remains the terminal worker cause.
            pass
        raise
    expected_type = lock.ros_package_name + "/msg/" + lock.envelope_message_name

    start_offset = max(0, args.unity_log_offset)
    marker_offset = start_offset
    markers: list[protocol.UnityMarker] = []
    marker_seen: set[str] = set()
    run_token: str | None = None
    remote_origin = ""
    sequence = 1
    first_inbound_sent = False
    replay_sent = False
    next_initial_bidirectional_publish_time: float | None = None
    initial_bidirectional_send_count = 0
    next_final_bidirectional_publish_time: float | None = None
    final_bidirectional_send_count = 0
    next_publish_time = 0.0
    observed_outbound_messages: set[int] = set()
    previous_outbound_sequence: int | None = None
    previous_unity_bidirectional_sequence: int | None = None
    worker_state = protocol.EvidenceStateMachine()
    worker_state.transition(protocol.ProtocolState.PEER_SOURCE_READY)
    worker_state.transition(protocol.ProtocolState.STRING_SUBSCRIBER_WAITING)
    ready_deadline = time.monotonic() + args.ready_timeout_seconds
    apply_deadline: float | None = None
    evidence: dict[str, object] = {
        "interfaceDigestMatches": True,
        "graphEvidence": False,
        "outboundObserved": False,
        "inboundApplied": False,
        "sameOriginDropped": False,
        "remoteOriginApplied": False,
        "nullableEmptyObserved": False,
        "initialBidirectionalSends": 0,
        "finalBidirectionalSends": 0,
        "unityTerminalPass": False,
        "cleanStop": False,
    }
    terminal_error: PeerFailure | None = None

    try:
        worker_ready_json = getattr(args, "worker_ready_json", None)
        if worker_ready_json is not None:
            write_worker_ready(worker_ready_json, lock)
        while time.monotonic() < worker_phase_deadline(run_token, ready_deadline, apply_deadline):
            rclpy.spin_once(node, timeout_sec=0.05)
            newly_observed, marker_offset = protocol.read_new_markers(args.unity_log, marker_offset)
            _append_unique_markers(markers, marker_seen, newly_observed)
            if run_token is None:
                ready = _matching_marker(markers, "PHASE181_CUSTOM_ROS2_READY")
                interface_ready = _matching_marker(markers, "PHASE181_CUSTOM_INTERFACE_READY")
                if ready is not None and interface_ready is not None:
                    run_token = require_matching_unity_readiness(
                        ready,
                        interface_ready,
                        lock,
                        args.distro,
                        args.rmw,
                        args.token if args.role == "windows-player" else None,
                    )
                    remote_origin = "remote-" + run_token
                    worker_state.transition(protocol.ProtocolState.UNITY_READY)
                    apply_deadline = time.monotonic() + args.apply_timeout_seconds

            if run_token is None:
                continue

            publish_publishers = node.get_publishers_info_by_topic(DEFAULT_TOPICS["publish"])
            subscribe_subscriptions = node.get_subscriptions_info_by_topic(DEFAULT_TOPICS["subscribe"])
            bidirectional_publishers = node.get_publishers_info_by_topic(DEFAULT_TOPICS["bidirectional"])
            bidirectional_subscriptions = node.get_subscriptions_info_by_topic(DEFAULT_TOPICS["bidirectional"])
            graph_checks = evaluate_graph_evidence(
                publish_publishers,
                subscribe_subscriptions,
                bidirectional_publishers,
                bidirectional_subscriptions,
                node.get_name(),
                expected_type,
                ReliabilityPolicy.RELIABLE,
                requires_outbound,
                requires_bidirectional,
            )
            observed_graph_checks = merge_graph_observations(
                evidence.get("graphChecks", {}),
                graph_checks,
            )
            evidence["graphEvidence"] = bool(all(observed_graph_checks.values()))
            evidence["graphChecks"] = observed_graph_checks
            evidence["graphEndpointCounts"] = {
                "publish": summarize_graph_endpoints(
                    publish_publishers, node.get_name(), expected_type, ReliabilityPolicy.RELIABLE
                ),
                "subscribe": summarize_graph_endpoints(
                    subscribe_subscriptions, node.get_name(), expected_type, ReliabilityPolicy.RELIABLE
                ),
                "bidirectionalPublish": summarize_graph_endpoints(
                    bidirectional_publishers, node.get_name(), expected_type, ReliabilityPolicy.RELIABLE
                ),
                "bidirectionalSubscribe": summarize_graph_endpoints(
                    bidirectional_subscriptions, node.get_name(), expected_type, ReliabilityPolicy.RELIABLE
                ),
            }

            now = time.monotonic()
            subscribe_applied = _matching_marker(
                markers, "PHASE181_CUSTOM_ROS2_APPLIED", run_token, DEFAULT_TOPICS["subscribe"]
            ) is not None
            if not first_inbound_sent or (not subscribe_applied and now >= next_publish_time):
                subscribe_publisher.publish(
                    _make_envelope(
                        node,
                        envelope_type,
                        payload_type,
                        nested_type,
                        custom_payload_fields(run_token, null_empty=False),
                        remote_origin,
                        sequence,
                    )
                )
                sequence += 1
                first_inbound_sent = True
                next_publish_time = now + 0.75

            if subscribe_applied and not evidence["inboundApplied"]:
                evidence["inboundApplied"] = True
                worker_state.transition(protocol.ProtocolState.STRING_CORRELATED)

            if (
                evidence["inboundApplied"]
                and not requires_bidirectional
                and worker_state.state == protocol.ProtocolState.STRING_CORRELATED
            ):
                worker_state.transition(protocol.ProtocolState.PROBES_RUNNING)

            matching_outbound = [
                message
                for message in received_publish
                if _payload_evidence(message).get("message") == "unity-publish"
            ]
            for message in matching_outbound:
                if id(message) in observed_outbound_messages:
                    continue
                observed_outbound_messages.add(id(message))
                if _payload_evidence(message) != custom_payload_fields("unity-publish", null_empty=False):
                    raise PeerFailure("FAIL_PAYLOAD_SHAPE", "Unity's native custom Publish envelope did not preserve the locked payload.")
                previous_outbound_sequence = protocol.require_envelope_metadata(
                    _envelope_metadata(message),
                    previous_outbound_sequence,
                )
            evidence["outboundObserved"] = bool(observed_outbound_messages)

            unity_origin_probe = next(
                (
                    message
                    for message in received_bidirectional
                    if is_unity_origin_probe(
                        getattr(message, "foxrun_origin_id", ""),
                        _payload_evidence(message),
                        run_token,
                    )
                ),
                None,
            )
            evidence["originProbeObserved"] = unity_origin_probe is not None
            bidirectional_apply_count = count_bidirectional_apply_markers(markers, run_token)
            initial_remote_applied = bidirectional_apply_count >= 1
            if requires_bidirectional and bidirectional_apply_count >= 2:
                evidence["remoteOriginApplied"] = True
                # The acceptance component emits its second marker only after
                # validating every nullable/empty field on the applied DTO.
                evidence["nullableEmptyObserved"] = True

            if should_publish_initial_bidirectional_probe(
                requires_bidirectional=requires_bidirectional,
                inbound_applied=evidence["inboundApplied"] is True,
                graph_evidence=evidence["graphEvidence"] is True,
                origin_probe_ready=unity_origin_probe is not None,
                initial_remote_applied=initial_remote_applied,
                now=now,
                next_publish_time=next_initial_bidirectional_publish_time,
            ):
                bidirectional_publisher.publish(
                    _make_envelope(
                        node,
                        envelope_type,
                        payload_type,
                        nested_type,
                        custom_payload_fields(run_token, null_empty=False),
                        remote_origin,
                        sequence,
                    )
                )
                sequence += 1
                initial_bidirectional_send_count += 1
                evidence["initialBidirectionalSends"] = initial_bidirectional_send_count
                next_initial_bidirectional_publish_time = now + 0.75
                if worker_state.state == protocol.ProtocolState.STRING_CORRELATED:
                    worker_state.transition(protocol.ProtocolState.PROBES_RUNNING)

            if (
                initial_remote_applied
                and unity_origin_probe is not None
                and not replay_sent
            ):
                previous_unity_bidirectional_sequence = protocol.require_envelope_metadata(
                    _envelope_metadata(unity_origin_probe),
                    previous_unity_bidirectional_sequence,
                )
                bidirectional_publisher.publish(unity_origin_probe)
                replay_sent = True

            same_origin_dropped = _matching_marker(
                markers,
                "PHASE181_CUSTOM_ROS2_SAME_ORIGIN_DROPPED",
                run_token,
                DEFAULT_TOPICS["bidirectional"],
            ) is not None
            evidence["sameOriginDropped"] = bool(same_origin_dropped)
            if should_publish_final_bidirectional_probe(
                requires_bidirectional=requires_bidirectional,
                same_origin_dropped=same_origin_dropped,
                remote_origin_applied=evidence["remoteOriginApplied"] is True,
                now=now,
                next_publish_time=next_final_bidirectional_publish_time,
            ):
                bidirectional_publisher.publish(
                    _make_envelope(
                        node,
                        envelope_type,
                        payload_type,
                        nested_type,
                        custom_payload_fields(run_token, null_empty=True),
                        "remote-final-" + run_token,
                        sequence,
                    )
                )
                sequence += 1
                final_bidirectional_send_count += 1
                evidence["finalBidirectionalSends"] = final_bidirectional_send_count
                next_final_bidirectional_publish_time = now + 0.75

            if has_role_transport_evidence(evidence, probe_role) and worker_state.state == protocol.ProtocolState.PROBES_RUNNING:
                worker_state.transition(protocol.ProtocolState.UNITY_APPLIED)
                worker_state.transition(protocol.ProtocolState.ORIGIN_CHECKED)

            if args.surface == "player":
                evidence["unityTerminalPass"] = _matching_marker(markers, "PHASE181_CUSTOM_ROS2_PASS", run_token) is not None
            else:
                # Editor has no terminal marker by design; its complete correlated
                # marker set is the equivalent bounded Unity-side proof.
                evidence["unityTerminalPass"] = has_role_transport_evidence(evidence, probe_role)

            if can_complete_live_evidence(evidence, probe_role):
                break
        else:
            if run_token is None:
                terminal_error = PeerFailure("FAIL_READY_TIMEOUT", "Unity did not emit correlated custom interface readiness.")
            elif (
                args.surface == "player"
                and evidence["interfaceDigestMatches"]
                and evidence["graphEvidence"]
                and evidence["outboundObserved"]
                and evidence["inboundApplied"]
                and evidence["sameOriginDropped"]
                and evidence["remoteOriginApplied"]
                and evidence["nullableEmptyObserved"]
                and not evidence["unityTerminalPass"]
            ):
                terminal_error = PeerFailure("FAIL_UNITY_TIMEOUT", "The Player exchanged data but did not emit its terminal marker.")
            else:
                terminal_error = PeerFailure(classify_evidence(evidence, probe_role), "The typed custom envelope proof did not complete.")
    except PeerFailure as exc:
        terminal_error = exc
    except protocol.ProtocolFailure as exc:
        print("FAIL_PEER_PROTOCOL code=" + exc.code, file=sys.stderr)
        terminal_error = PeerFailure(exc.code, "The typed peer rejected one bounded protocol invariant.")
    except Exception as exc:  # noqa: BLE001 - a peer must return a bounded failure instead of leaking a stack into summary.
        print("FAIL_PEER_RUNTIME exception=" + type(exc).__name__, file=sys.stderr)
        terminal_error = PeerFailure("FAIL_PEER_RUNTIME", "The typed worker stopped before completing its bounded probe.")
    finally:
        teardown_failed = False
        try:
            node.destroy_node()
        except Exception:  # noqa: BLE001 - a teardown error must become a bounded acceptance failure.
            teardown_failed = True
        try:
            rclpy.shutdown()
        except Exception:  # noqa: BLE001 - do not leak a native teardown stack into the summary.
            teardown_failed = True
        if run_token is not None:
            no_late_apply, marker_offset = observe_no_late_unity_apply(
                args.unity_log,
                marker_offset,
                run_token,
            )
            evidence["postStopMarkerOffset"] = marker_offset
            evidence["cleanStop"] = no_late_apply and not teardown_failed
            if not no_late_apply and terminal_error is None:
                terminal_error = PeerFailure("FAIL_LATE_APPLY", "Unity applied a correlated custom envelope after the peer stop offset.")
        else:
            evidence["cleanStop"] = not teardown_failed
        if teardown_failed and terminal_error is None:
            terminal_error = PeerFailure("FAIL_CLEAN_STOP", "The helper-owned generated ROS2 endpoints did not stop cleanly.")

    verdict = terminal_error.code if terminal_error is not None else classify_evidence(evidence, probe_role)
    if terminal_error is None and verdict == "PASS":
        worker_state.transition(protocol.ProtocolState.CLEAN_STOP)
        worker_state.transition(protocol.ProtocolState.PASS)
    result = _worker_result_base(
        lock,
        verdict,
        role=args.role,
        probeRole=probe_role,
        surface=args.surface,
        markerOffsetStart=start_offset,
        markerOffsetEnd=marker_offset,
        unityRuntime=args.distro,
        unityRmw=args.rmw,
        unityDigestPrefix=protocol.digest_prefix(lock.interface_digest),
        stateTransitions=[transition.state.value for transition in worker_state.transitions],
        evidence=evidence,
        markerNames=[marker.name for marker in markers],
        token=run_token or args.token,
    )
    if terminal_error is not None:
        result["error"] = str(terminal_error)
    write_worker_result(args.worker_result_json, result)
    print(verdict)
    return 0 if verdict == "PASS" else 1
def default_unity_editor_log_path() -> pathlib.Path:
    """Return Unity's normal Windows Editor log without depending on a shell profile."""

    local_app_data = os.environ.get("LOCALAPPDATA")
    if local_app_data:
        return pathlib.Path(local_app_data) / "Unity" / "Editor" / "Editor.log"
    return pathlib.Path.home() / "AppData" / "Local" / "Unity" / "Editor" / "Editor.log"
def _profile_build_directory(repository: pathlib.Path, profile_id: str) -> pathlib.Path:
    """Return one stable summary directory without allowing arbitrary output roots."""

    if _PROFILE_ID.fullmatch(profile_id) is None:
        raise PeerFailure("FAIL_PROFILE", "The Phase181 profile identifier is not safe.")
    return pathlib.Path(repository) / "build" / "phase181" / profile_id
def _require_positive_timeout(value: float, name: str) -> float:
    """Reject invalid operator timeout input before launching an owned process."""

    if value <= 0.0 or value > 600.0:
        raise PeerFailure("FAIL_ARGUMENTS", name + " must be within the bounded Phase181 acceptance range.")
    return value
def peer_build_timeout_seconds() -> float:
    """Return the cold-build no-progress watchdog, independent from total build duration and Unity readiness."""

    return _PEER_BUILD_STALL_SECONDS
def _terminate_owned_child(process: subprocess.Popen[str]) -> None:
    """Terminate just one helper-created process tree on the current host."""

    if process.poll() is not None:
        return
    if os.name == "nt":
        result = subprocess.run(
            ["taskkill", "/PID", str(process.pid), "/T", "/F"],
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            check=False,
            shell=False,
        )
        try:
            process.wait(timeout=5.0)
        except subprocess.TimeoutExpired:
            raise PeerFailure("FAIL_PEER_CLEANUP", "Owned Windows process tree did not exit within the cleanup deadline.")
        if result.returncode != 0 or process.poll() is None:
            raise PeerFailure("FAIL_PEER_CLEANUP", "Owned Windows process tree could not be retired.")
        return
    protocol.terminate_owned_process(process)
    if process.poll() is None:
        raise PeerFailure("FAIL_PEER_CLEANUP", "Owned process could not be retired after bounded cleanup.")


__all__ = [name for name in globals() if not name.startswith("__")]
