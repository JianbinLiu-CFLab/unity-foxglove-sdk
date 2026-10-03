from __future__ import annotations
from .read_unity_log_and_capture_unity_ready_marker_tokens import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase179_foxrun_ros2_inbound_acceptance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
def main(argv: Sequence[str] | None = None) -> int:
    """Run all selected native input probes and record an honest evidence verdict."""

    args = parse_args(argv)
    token = args.token or f"phase179-{uuid.uuid4().hex}"
    message_results: list[dict[str, object]] = []
    applied_markers: dict[str, UnityMarker] = {}
    summary: dict[str, object] = {
        "phase": 179,
        "role": "linux-ros2-peer",
        "distro": args.distro,
        "rmwImplementation": args.rmw,
        "domainId": args.domain_id,
        "discoveryRange": args.discovery_range,
        "token": token,
        "topicPrefix": args.topic_prefix,
        "messageSet": list(args.message_set),
        "unityLogProvided": args.unity_log is not None,
        "negativeCase": args.negative_case,
        "messageResults": message_results,
    }
    if args.profile_id is not None:
        summary["profileId"] = args.profile_id
        summary["surface"] = args.surface
    if args.zenoh_topology_id is not None:
        summary["zenohTopologyId"] = args.zenoh_topology_id
    if args.negative_peer_rmw is not None:
        summary["negativePeerRmw"] = args.negative_peer_rmw
    configured_topology: zenoh_topology.ZenohTopologyHandle | str | None = None
    failure: AcceptanceFailure | None = None
    exit_code = 1
    try:
        env = build_linux_environment(args)
        summary["optionalWindowsPeerDiagnostic"] = collect_optional_windows_peer_diagnostic(args)
        configured_topology = configure_zenoh_topology(args, env)
        summary["zenohTopology"] = topology_summary(configured_topology)
        ros2_executable = find_ros2_executable(env)

        if args.negative_case is not None:
            message_results.append(run_negative_case(args, env, ros2_executable, token))
        else:
            for name in args.message_set:
                spec = MESSAGE_SPECS[name]
                topic = topic_for_spec(args.topic_prefix, spec)
                result: dict[str, object] = {"name": name, "topic": topic, "published": False, "unityProof": False}
                message_results.append(result)

                interface = run_bounded_command(
                    [str(ros2_executable), "interface", "show", spec.message_type],
                    env,
                    min(10.0, args.timeout_seconds),
                    "ros2 interface show",
                )
                require_command_success(interface, "ENDPOINT", "ros2 interface show")
                wait_for_unity_subscription_topic(
                    ros2_executable,
                    env,
                    topic,
                    spec.message_type,
                    args.timeout_seconds,
                )
                endpoint = query_unity_subscription_endpoint(ros2_executable, env, topic, spec, args.timeout_seconds)
                result["graph"] = {
                    "messageType": endpoint.message_type,
                    "subscriptionCount": endpoint.subscription_count,
                    "qosReliability": endpoint.qos_reliability,
                    "qosHistory": endpoint.qos_history,
                    "qosDepth": endpoint.qos_depth,
                    "qosDurability": endpoint.qos_durability,
                }

                marker_offset = unity_log_offset(args.unity_log) if args.unity_log is not None else None
                published = run_bounded_command(
                    build_publish_command(ros2_executable, spec, token, args.topic_prefix),
                    env,
                    args.timeout_seconds,
                    f"ros2 topic pub {name}",
                )
                require_command_success(published, "PUBLISH", f"ros2 topic pub {name}")
                result["published"] = True

                if args.unity_log is not None:
                    marker = wait_for_unity_marker(
                        args.unity_log,
                        topic,
                        token,
                        spec.expected_value(token),
                        args.timeout_seconds,
                        start_offset=marker_offset,
                    )
                    result["unityProof"] = True
                    result["received"] = marker.received
                    result["applied"] = marker.applied
                    result["replaced"] = marker.replaced
                    applied_markers[name] = marker

        if args.string_burst_final_sequence is not None:
            string_spec = MESSAGE_SPECS["string"]
            string_topic = topic_for_spec(args.topic_prefix, string_spec)
            string_result = next(result for result in message_results if result["name"] == "string")
            burst_marker_offset = unity_log_offset(args.unity_log) if args.unity_log is not None else None
            run_string_burst(
                env,
                string_topic,
                token,
                args.string_burst_final_sequence,
                args.string_burst_rate_hz,
                args.timeout_seconds,
            )
            if args.unity_log is None:
                string_result["burst"] = {
                    "finalSequence": args.string_burst_final_sequence,
                    "total": args.string_burst_final_sequence + 1,
                    "unityProofPending": True,
                }
            else:
                final_marker = wait_for_unity_marker(
                    args.unity_log,
                    string_topic,
                    token,
                    expected_string_burst_value(token, args.string_burst_final_sequence),
                    args.timeout_seconds,
                    start_offset=burst_marker_offset,
                )
                string_result["burst"] = validate_string_burst_marker(
                    applied_markers["string"],
                    final_marker,
                    token,
                    args.string_burst_final_sequence,
                )

        if args.negative_case is not None:
            negative_result = message_results[0]
            summary["verdict"] = classify_negative_verdict(
                negative_case=args.negative_case,
                unity_log_available=args.unity_log is not None,
                expectation_observed=bool(negative_result.get("expectationObserved")),
                unity_ready=bool(negative_result.get("unityReady")),
                contract_identity=bool(negative_result.get("contractIdentity")),
                unity_no_apply=bool(negative_result.get("unityNoApply")),
                failure=None,
            )
            exit_code = 0 if str(summary["verdict"]).startswith("EXPECTED_NEGATIVE_") else 2
        else:
            summary["verdict"] = classify_verdict(
                unity_log_available=args.unity_log is not None,
                message_results=message_results,
                failure=None,
            )
            exit_code = 0 if summary["verdict"] == "PASS" else 2
    except AcceptanceFailure as exc:
        failure = exc
        summary["failureCategory"] = exc.category
    except KeyboardInterrupt:
        failure = AcceptanceFailure("INTERRUPTED", "Acceptance was interrupted by the operator.")
        summary["failureCategory"] = failure.category
    except (OSError, subprocess.SubprocessError) as exc:
        failure = AcceptanceFailure("ENVIRONMENT", "A helper-owned ROS2 process could not be started or completed.")
        summary["failureCategory"] = failure.category
    finally:
        if failure is not None:
            if args.negative_case is not None:
                negative_result = message_results[0] if message_results else {}
                summary["verdict"] = classify_negative_verdict(
                    negative_case=args.negative_case,
                    unity_log_available=args.unity_log is not None,
                    expectation_observed=bool(negative_result.get("expectationObserved")),
                    unity_ready=bool(negative_result.get("unityReady")),
                    contract_identity=bool(negative_result.get("contractIdentity")),
                    unity_no_apply=bool(negative_result.get("unityNoApply")),
                    failure=failure,
                )
            else:
                summary["verdict"] = classify_verdict(
                    unity_log_available=args.unity_log is not None,
                    message_results=message_results,
                    failure=failure,
                )
        try:
            write_summary(args.summary_json, summary)
            print(f"Summary: {args.summary_json}")
            print(f"Verdict: {summary['verdict']}")
        finally:
            close_configured_topology(configured_topology)
    return exit_code


__all__ = [name for name in globals() if not name.startswith("__")]
