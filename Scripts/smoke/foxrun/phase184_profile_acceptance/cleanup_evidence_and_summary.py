from __future__ import annotations
from .actor_startup_and_summary import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "phase184_profile_acceptance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
def _required_section(values: Mapping[str, object]) -> dict[str, object]:
    """Handle the required section step."""

    return {"applicability": "required", **dict(values)}


def _not_applicable_section(rule: protocol.ApplicabilityRule) -> dict[str, object]:
    """Handle the not applicable section step."""

    if rule.required or not rule.reason:
        raise ValueError("Only an approved N/A rule can create an N/A section.")
    return {"applicability": "not_applicable", "reason": rule.reason}


def _actor_evidence(
    results: Mapping[str, Mapping[str, object]],
    role: str,
) -> Mapping[str, object]:
    """Handle the actor evidence step."""

    result = results.get(role)
    evidence = result.get("evidence") if isinstance(result, Mapping) else None
    if (
        not isinstance(result, Mapping)
        or result.get("verdict") != "PASS"
        or not isinstance(evidence, Mapping)
    ):
        raise AcceptanceFailure(
            "FAIL_TERMINAL",
            f"{role} has no usable PASS evidence object.",
        )
    return evidence


def build_pass_summary(
    *,
    config: Mapping[str, object],
    terminal: TerminalMarker,
    results: Mapping[str, Mapping[str, object]],
    process_exit_codes: Mapping[str, int],
    unity_version: str,
    cleanup: Mapping[str, bool],
    owner_stopped_roles: Iterable[str] = (),
) -> dict[str, object]:
    """Compose one strict case-specific PASS summary from independent evidence."""

    case = str(config["case"])
    token = str(config["token"])
    contract = protocol.CASE_CONTRACTS[case]
    if set(results) != set(contract.required_actors):
        raise AcceptanceFailure(
            "FAIL_TERMINAL",
            "Actor results do not cover the exact selected case.",
        )
    for role in contract.required_actors:
        _actor_evidence(results, role)
    expected_qos = _expected_qos_by_topic(config)
    fox = (
        _actor_evidence(results, "foxglove-client")
        if "foxglove-client" in results
        else {}
    )
    graph = (
        _actor_evidence(results, "graph-observer")
        if "graph-observer" in results
        else {}
    )
    peer = (
        _actor_evidence(results, "ros2-peer")
        if "ros2-peer" in results
        else {}
    )
    bridge = (
        _actor_evidence(results, "bridge")
        if "bridge" in results
        else {}
    )
    profile_evidence = _observed_profile_evidence(config)
    observed_targets = _observed_target_evidence(config, terminal)

    if case == "foxglove-profile":
        target_values = {
            **observed_targets,
            "healthyDelivery": bool(fox.get("deliveryObserved")),
        }
        origin_values = {
            "remoteApplied": bool(fox.get("remoteApplied")),
            "sameOriginDropped": bool(fox.get("sameOriginDropped")),
            "laterLocalPublished": bool(fox.get("laterLocalPublished")),
        }
    elif case == "multi-target":
        target_values = {
            **observed_targets,
            "healthyDelivery": (
                bool(fox.get("deliveryObserved"))
                and int(peer.get("distinctFanoutPublishers", 0)) >= 2
            ),
        }
        origin_values = {
            "remoteApplied": bool(peer.get("remoteApplied")),
            "sameOriginDropped": bool(peer.get("sameOriginDropped")),
            "laterLocalPublished": bool(peer.get("laterLocalPublished")),
        }
    elif case == "degraded-target":
        target_values = {
            **observed_targets,
            "healthyDelivery": (
                bool(fox.get("deliveryObserved"))
                and bool(graph.get("noFallbackPublisher"))
            ),
        }
        origin_values = {}
    elif case == "qos-contract":
        delivery_by_topic = peer.get("deliveryByTopic", {})
        exact_delivery_topics = (
            isinstance(delivery_by_topic, Mapping)
            and {str(topic) for topic in delivery_by_topic}
            == {str(topic) for topic in config["topics"]}
        )
        target_values = {
            **observed_targets,
            "healthyDelivery": exact_delivery_topics and all(
                len(gids) >= 2
                for gids in delivery_by_topic.values()
            ),
        }
        origin_values = {}
    elif case == "stream-640hz":
        target_values = {
            **observed_targets,
            "healthyDelivery": bool(graph.get("endpointsObserved")),
        }
        origin_values = {
            "remoteApplied": bool(peer.get("remoteApplied")),
            "sameOriginDropped": bool(peer.get("sameOriginDropped")),
            "laterLocalPublished": bool(peer.get("laterLocalPublished")),
        }
    else:
        raise AcceptanceFailure("FAIL_TERMINAL", "Unknown case summary mapping.")

    source = str(profile_evidence["source"])
    targets = list(profile_evidence["targets"])
    publish_encoding = str(profile_evidence["publishEncoding"])
    subscribe_encoding = str(profile_evidence["subscribeEncoding"])

    transport_observed: dict[str, object] = {
        "graph": dict(graph.get("transportObservedQos", {})),
    }
    if bridge:
        if bridge.get("healthReady") is not True:
            raise AcceptanceFailure(
                "FAIL_BRIDGE",
                "Bridge result has no current health readiness evidence.",
            )
        publishers = bridge.get("publishers")
        if not isinstance(publishers, Mapping):
            raise AcceptanceFailure(
                "FAIL_BRIDGE",
                "Bridge result has no parsed publisher evidence.",
            )
        transport_observed["bridge"] = dict(publishers)

    sample_publisher_gids: dict[str, object] = {}
    if case == "multi-target":
        for suffix, field in (
            ("multi-local-1", "local1PublisherGids"),
            ("multi-local-3", "local3PublisherGids"),
        ):
            attribution_field = (
                "local1Attribution"
                if suffix == "multi-local-1"
                else "local3Attribution"
            )
            sample_publisher_gids[suffix] = {
                "sampleSha256": protocol.token_sha256(token + "-" + suffix),
                "publisherGids": list(peer.get(field, [])),
                "attribution": str(peer.get(attribution_field, "")),
            }
    elif case == "qos-contract":
        suffixes = (
            "qos-system-default",
            "qos-keep-all",
            "qos-keep-last-depth",
        )
        delivery = peer.get("deliveryByTopic", {})
        if not isinstance(delivery, Mapping):
            raise AcceptanceFailure(
                "FAIL_QOS",
                "QoS peer delivery evidence is malformed.",
            )
        for topic, suffix in zip(config["topics"], suffixes):
            sample_publisher_gids[str(topic)] = {
                "sampleSha256": protocol.token_sha256(token + "-" + suffix),
                "publisherGids": list(delivery.get(topic, [])),
                "attribution": str(
                    peer.get("deliveryAttributionByTopic", {}).get(topic, "")
                ),
            }
    elif case == "stream-640hz":
        sample_publisher_gids["origin-local"] = {
            "sampleSha256": protocol.token_sha256(token + "-origin-local"),
            "publisherGids": list(peer.get("localOriginPublisherGids", [])),
            "attribution": str(peer.get("localOriginAttribution", "")),
        }

    section_values: dict[str, Mapping[str, object]] = {
        "foxglove": {
            "deliveryObserved": bool(fox.get("deliveryObserved")),
            "channelEncodings": list(fox.get("channelEncodings", [])),
            "sampleToken": str(fox.get("sampleToken", "")),
            "sampleStages": list(fox.get("sampleStages", [])),
            "timestamp": float(fox.get("timestamp", 0.0)),
        },
        "rosGraph": {
            "endpointsObserved": bool(graph.get("endpointsObserved")),
            "nodeIdentities": list(graph.get("nodeIdentities", [])),
            "publisherGids": list(graph.get("publisherGids", [])),
            "publishersByTopic": dict(graph.get("publishersByTopic", {})),
            "samplePublisherGids": sample_publisher_gids,
            "negativeObservationSeconds": float(
                graph.get("negativeObservationSeconds", 0)
            ),
        },
        "qos": {
            "requested": expected_qos,
            "transportObserved": transport_observed,
            "matches": bool(graph.get("qosMatches")),
        },
        "targets": target_values,
        "origin": origin_values,
        "stream": {},
    }
    if case == "stream-640hz":
        section_values["stream"] = _validated_stream_evidence(terminal, peer)

    sections: dict[str, object] = {}
    for name, rule in contract.applicability.items():
        sections[name] = (
            _required_section(section_values[name])
            if rule.required
            else _not_applicable_section(rule)
        )

    owner_stopped = frozenset(owner_stopped_roles)
    process_entries = [
        {
            "role": role,
            "started": True,
            "exitCode": int(process_exit_codes[role]),
            "termination": (
                "owner_requested" if role in owner_stopped else "self"
            ),
        }
        for role in sorted(contract.required_actors | {"unity"})
    ]
    process_entries.extend(
        {"role": role, "started": False, "reason": reason}
        for role, reason in sorted(contract.deliberately_absent_actors.items())
    )
    summary = {
        "summarySchemaVersion": protocol.SUMMARY_SCHEMA_VERSION,
        "identity": {
            "runId": config["runId"],
            "case": case,
            "tokenSha256": protocol.token_sha256(token),
            "unityVersion": unity_version,
            "interfaceIdentity": config["interfaceType"],
            "interfaceDigest": config["interfaceDigest"],
        },
        "profile": {
            "profile": config["profile"],
            "runtime": config["rosDistro"],
            "rmw": config["rmw"],
            "source": source,
            "targets": targets,
            "publishEncoding": publish_encoding,
            "subscribeEncoding": subscribe_encoding,
            "requestedQos": expected_qos,
        },
        **sections,
        "processes": process_entries,
        "cleanup": dict(cleanup),
        "verdict": "PASS",
    }
    protocol.validate_summary(
        summary,
        expected_case=case,
        expected_token=token,
    )
    return summary


def _wait_for_clean_worker_exits(
    owner: OwnedProcessSet,
    roles: Iterable[str],
    timeout_seconds: float = 30.0,
) -> None:
    """Wait for clean worker exits."""

    deadline = time.monotonic() + timeout_seconds
    for role in roles:
        process = owner.process(role)
        if process is None:
            raise AcceptanceFailure(
                "FAIL_PROCESS_EXIT",
                f"{role} was not registered with the process owner.",
            )
        remaining = deadline - time.monotonic()
        if remaining <= 0:
            raise AcceptanceFailure(
                "FAIL_PROCESS_EXIT",
                "Worker exit window expired.",
            )
        try:
            exit_code = int(process.wait(timeout=remaining))
        except subprocess.TimeoutExpired as exc:
            raise AcceptanceFailure(
                "FAIL_PROCESS_EXIT",
                f"{role} did not exit after terminal evidence.",
            ) from exc
        if exit_code != 0:
            raise AcceptanceFailure(
                "FAIL_PROCESS_EXIT",
                f"{role} exited with code {exit_code}.",
            )


def _cleanup_evidence(
    output: pathlib.Path,
    owner: OwnedProcessSet,
    subst_roots: Iterable[pathlib.Path],
) -> dict[str, bool]:
    """Handle the cleanup evidence step."""

    return {
        "processes": owner.all_stopped(),
        "files": not any(output.rglob("*.tmp")),
        "junctions": True,
        "subst": all(not pathlib.Path(root).exists() for root in subst_roots),
    }


def _write_failure_record(
    output: pathlib.Path | None,
    repository: pathlib.Path,
    *,
    case: str | None,
    run_id: str | None,
    failure: AcceptanceFailure,
) -> None:
    """Write failure record."""

    if output is None:
        return
    protocol.write_json_atomic(
        output / "failure.json",
        {
            "phase": "184-G",
            "runId": run_id or "unallocated",
            "case": case or "unselected",
            "verdict": failure.code,
            "diagnostic": str(failure),
        },
        repo_root=repository,
    )




__all__ = [name for name in globals() if not name.startswith("__")]
