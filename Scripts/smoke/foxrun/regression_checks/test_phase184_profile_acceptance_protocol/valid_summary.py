from __future__ import annotations
from .foundation import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "test_phase184_profile_acceptance_protocol.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
def valid_summary(protocol, config: dict[str, object]) -> dict[str, object]:
    """Build one positive summary satisfying the canonical section contract."""

    case = str(config["case"])
    token = str(config["token"])
    contract = protocol.CASE_CONTRACTS[case]
    applicability = contract.applicability
    expected_qos = protocol.expected_qos_by_topic(case)
    publishers_by_topic: dict[str, list[dict[str, str]]] = {
        topic: [] for topic in contract.topics
    }
    graph_transport: dict[str, object] = {}
    node_identities: set[str] = set()
    publisher_gids: set[str] = set()

    def transport_qos(topic: str) -> dict[str, object]:
        """Handle the transport QoS step."""

        value = {
            key: value
            for key, value in expected_qos[topic].items()
            if key != "profile"
        }
        value["representedAxes"] = [
            "reliability",
            "durability",
            "history",
            "depth",
        ]
        return value

    for topic_index, topic in enumerate(contract.topics):
        publisher_count = (
            2
            if case in {"multi-target", "qos-contract"}
            else 1
            if case == "stream-640hz" and topic_index == 1
            else 0
        )
        subscription_count = (
            1
            if case == "multi-target"
            or case == "stream-640hz"
            else 0
        )
        topic_publishers: list[dict[str, str]] = []
        for publisher_index in range(publisher_count):
            node = (
                "/unity2foxglove_ros2_bridge"
                if publisher_index == 1
                else "/unity2foxglove_foxrun"
            )
            gid = f"gid-{topic_index}-{publisher_index}"
            topic_publishers.append({"node": node, "gid": gid})
            node_identities.add(node)
            publisher_gids.add(gid)
        publishers_by_topic[topic] = topic_publishers
        if topic in expected_qos:
            graph_transport[topic] = {
                "publishers": [
                    transport_qos(topic) for _ in range(publisher_count)
                ],
                "subscriptions": [
                    transport_qos(topic) for _ in range(subscription_count)
                ],
            }
            if subscription_count:
                node_identities.add("/unity2foxglove_foxrun")

    transport_observed = {"graph": graph_transport}
    if case in {"multi-target", "qos-contract"}:
        transport_observed["bridge"] = {
            topic: copy.deepcopy(expected_qos[topic]) for topic in contract.topics
        }
    sample_publisher_gids: dict[str, object] = {}
    if case == "multi-target":
        gids = [item["gid"] for item in publishers_by_topic[contract.topics[0]]]
        for suffix in ("multi-local-1", "multi-local-3"):
            sample_publisher_gids[suffix] = {
                "sampleSha256": protocol.token_sha256(token + "-" + suffix),
                "publisherGids": list(gids),
                "attribution": "publication-sequence-plus-graph-gid",
            }
    elif case == "qos-contract":
        suffixes = (
            "qos-system-default",
            "qos-keep-all",
            "qos-keep-last-depth",
        )
        for topic, suffix in zip(contract.topics, suffixes):
            sample_publisher_gids[topic] = {
                "sampleSha256": protocol.token_sha256(token + "-" + suffix),
                "publisherGids": [
                    item["gid"] for item in publishers_by_topic[topic]
                ],
                "attribution": "publication-sequence-plus-graph-gid",
            }
    elif case == "stream-640hz":
        origin_topic = contract.topics[1]
        sample_publisher_gids["origin-local"] = {
            "sampleSha256": protocol.token_sha256(token + "-origin-local"),
            "publisherGids": [
                item["gid"] for item in publishers_by_topic[origin_topic]
            ],
            "attribution": "message-info-publisher-gid",
        }

    expected_stages = {
        "foxglove-profile": [
            "profile-outbound",
            "json-outbound",
            "profile-a",
            "profile-b",
            "profile-local-after-remote",
        ],
        "multi-target": ["multi-local-1", "multi-local-3"],
        "degraded-target": ["degraded-local"],
    }
    expected_states = {
        "foxglove-profile": {"foxglove": "Ready"},
        "multi-target": {
            "foxglove": "Ready",
            "ros2Native": "Ready",
            "ros2Bridge": "Ready",
        },
        "degraded-target": {
            "foxglove": "Ready",
            "ros2Bridge": "Unavailable",
        },
        "qos-contract": {topic: "Ready" for topic in contract.topics},
        "stream-640hz": {"ros2Native": "Ready"},
    }
    expected_diagnostics = {
        "foxglove-profile": {"failedTargets": 0},
        "multi-target": {
            "failedTargets": 0,
            "bridgeRuntimeFailures": 0,
        },
        "degraded-target": {
            "failedTargets": 1,
            "bridgeDiagnostics": 1,
        },
        "qos-contract": {"failedTargets": 0},
        "stream-640hz": {
            "copyFailed": 0,
            "staleCallbacks": 0,
            "rejectedAfterStop": 0,
        },
    }
    status_evidence = {
        "foxglove-profile": {
            "aggregate": "Ready",
            "succeeded": "Foxglove",
            "failed": "None",
            "topics": 2,
        },
        "multi-target": {
            "aggregate": "Ready",
            "succeeded": "Foxglove,Ros2Native,Ros2Bridge",
            "failed": "None",
            "bridgeRuntimeFailures": 0,
        },
        "degraded-target": {
            "aggregate": "Degraded",
            "succeeded": "Foxglove",
            "failed": "Ros2Bridge",
            "bridgeDiagnostics": 1,
        },
        "qos-contract": {
            "topics": {
                topic: {
                    "aggregate": "Ready",
                    "succeeded": "Ros2Native,Ros2Bridge",
                    "failed": "None",
                }
                for topic in contract.topics
            }
        },
        "stream-640hz": {
            "bindingState": "Receiving",
            "received": 1280,
            "copyFailed": 0,
            "staleCallbacks": 0,
            "rejectedAfterStop": 0,
        },
    }
    sections: dict[str, object] = {}
    evidence = {
        "foxglove": {
            "deliveryObserved": True,
            "channelEncodings": (
                ["protobuf", "json"]
                if case == "foxglove-profile"
                else ["protobuf"]
            ),
            "sampleToken": protocol.token_sha256(token),
            "sampleStages": expected_stages.get(case, []),
            "timestamp": 42,
        },
        "rosGraph": {
            "endpointsObserved": True,
            "nodeIdentities": sorted(node_identities),
            "publisherGids": sorted(publisher_gids),
            "publishersByTopic": publishers_by_topic,
            "samplePublisherGids": sample_publisher_gids,
            "negativeObservationSeconds": 3 if case == "degraded-target" else 0,
        },
        "qos": {
            "requested": copy.deepcopy(expected_qos),
            "transportObserved": transport_observed,
            "matches": True,
        },
        "targets": {
            "states": expected_states[case],
            "diagnosticCounts": expected_diagnostics[case],
            "healthyDelivery": True,
            "statusEvidence": status_evidence[case],
        },
        "origin": {
            "remoteApplied": True,
            "sameOriginDropped": True,
            "laterLocalPublished": True,
        },
        "stream": {
            "offered": 1280,
            "received": 1280,
            "accepted": 1280,
            "replaced": 224,
            "rateDropped": 0,
            "transportDropped": 0,
            "dropped": 0,
            "drained": 1056,
            "disposed": 1280,
            "maximumQueueDepth": 32,
            "lastSequence": 1279,
            "retainedOrdered": True,
            "ownershipBalanced": True,
        },
    }
    for section_name, rule in applicability.items():
        if rule.required:
            sections[section_name] = {
                "applicability": "required",
                **copy.deepcopy(evidence[section_name]),
            }
        else:
            sections[section_name] = {
                "applicability": "not_applicable",
                "reason": rule.reason,
            }
    required_actors = protocol.CASE_CONTRACTS[case].required_actors
    absent_actors = protocol.CASE_CONTRACTS[case].deliberately_absent_actors
    process_entries = [
        {
            "role": actor,
            "started": True,
            "exitCode": 0,
            "termination": "self",
        }
        for actor in sorted(required_actors | {"unity"})
    ]
    process_entries.extend(
        {"role": actor, "started": False, "reason": reason}
        for actor, reason in sorted(absent_actors.items())
    )
    profile_evidence = {
        "foxglove-profile": (
            "Foxglove",
            ["Foxglove"],
            "protobuf,json",
            "protobuf,json",
        ),
        "multi-target": (
            "Ros2Native",
            ["Foxglove", "Ros2Native", "Ros2Bridge"],
            "protobuf",
            "protobuf",
        ),
        "degraded-target": (
            "None",
            ["Foxglove", "Ros2Bridge"],
            "protobuf",
            "not_applicable",
        ),
        "qos-contract": (
            "None",
            ["Ros2Native", "Ros2Bridge"],
            "protobuf",
            "not_applicable",
        ),
        "stream-640hz": (
            "Ros2Native",
            ["Ros2Native"],
            "protobuf",
            "protobuf",
        ),
    }
    source, targets, publish_encoding, subscribe_encoding = profile_evidence[case]
    return {
        "summarySchemaVersion": protocol.SUMMARY_SCHEMA_VERSION,
        "identity": {
            "runId": config["runId"],
            "case": case,
            "tokenSha256": protocol.token_sha256(str(config["token"])),
            "unityVersion": "6000.3.14f1",
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
            "requestedQos": copy.deepcopy(expected_qos),
        },
        **sections,
        "processes": process_entries,
        "cleanup": {
            "processes": True,
            "files": True,
            "junctions": True,
            "subst": True,
        },
        "verdict": "PASS",
    }


__all__ = [name for name in globals() if not name.startswith("__")]
