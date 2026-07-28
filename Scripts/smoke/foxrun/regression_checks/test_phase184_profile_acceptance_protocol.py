#!/usr/bin/env python3
# Copyright (c) 2026 Jianbin Liu and Unity2Foxglove contributors.
# SPDX-License-Identifier: Apache-2.0

"""Regression checks for the pure Phase184-G acceptance evidence protocol."""

from __future__ import annotations

import copy
import importlib.util
import json
import pathlib
import sys
import tempfile
import unittest


ROOT = pathlib.Path(__file__).resolve().parents[4]
PROTOCOL_PATH = (
    ROOT
    / "Scripts"
    / "smoke"
    / "foxrun"
    / "phase184_profile_acceptance_protocol.py"
)
PHASE184_TEST_ROOT = ROOT / "build" / "Tests" / "Phase184"


def load_protocol_module():
    """Load the Phase184-G protocol module under test."""

    spec = importlib.util.spec_from_file_location(
        "phase184_profile_acceptance_protocol",
        PROTOCOL_PATH,
    )
    if spec is None or spec.loader is None:
        raise RuntimeError("Could not load the Phase184-G acceptance protocol module.")
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


def temporary_directory(prefix: str):
    """Return a Phase184-owned temporary directory context."""

    PHASE184_TEST_ROOT.mkdir(parents=True, exist_ok=True)
    return tempfile.TemporaryDirectory(prefix=prefix, dir=PHASE184_TEST_ROOT)


def run_config(
    protocol,
    *,
    case: str = "multi-target",
    profile: str = "jazzy-fastrtps",
) -> dict[str, object]:
    """Build one valid immutable run-config fixture."""

    run_id = "phase184g-20260726-a1b2c3d4"
    output = ROOT / "build" / "phase184" / "acceptance" / run_id
    contract = protocol.CASE_CONTRACTS[case]
    actors = sorted(contract.required_actors | contract.deliberately_absent_actors.keys())
    bridge_install = (
        ROOT
        / "build"
        / "phase184"
        / "bridge-cache"
        / profile
        / "bridge-overlay"
        / "install"
        if "bridge" in contract.required_actors
        else output / "bridge-overlay" / "install"
    )
    return {
        "schemaVersion": protocol.RUN_CONFIG_SCHEMA_VERSION,
        "executionMode": "batch",
        "runId": run_id,
        "token": "p184g_A1b2C3d4E5f6",
        "case": case,
        "profile": profile,
        "projectPath": str(ROOT / "Unity2Foxglove"),
        "outputRoot": str(output),
        "rosDistro": protocol.PROFILE_CONTRACTS[profile].runtime,
        "rmw": protocol.PROFILE_CONTRACTS[profile].rmw,
        "domainId": 48,
        "discoveryRange": (
            "SUBNET" if profile == "jazzy-fastrtps" else "LOCALHOST"
        ),
        "zenohTopologyId": "phase184-local" if profile == "lyrical-zenoh" else "",
        "phase181Workspace": str(ROOT / "build" / "phase181" / profile),
        "phase181Install": str(ROOT / "build" / "phase181" / profile / "install"),
        "bridgeOverlayInstall": str(bridge_install),
        "foxgloveHost": "127.0.0.1",
        "foxglovePort": 18765,
        "bridgeHost": "127.0.0.1",
        "bridgePort": 18766,
        "interfacePackage": "unity2foxglove_phase181_v1",
        "interfaceType": "unity2foxglove_phase181_v1/msg/Phase181State",
        "interfaceDigest": "a" * 64,
        "topics": list(contract.topics),
        "observationWindows": {
            "positiveSeconds": 3,
            "negativeSeconds": 3,
            "streamProductionSeconds": 2,
            "terminalSeconds": 30,
            "teardownSeconds": 30,
        },
        "readyFiles": {
            actor: str(output / "ready" / f"{actor}.json")
            for actor in actors
        },
        "resultFiles": {
            actor: str(output / "results" / f"{actor}.json")
            for actor in actors
        },
        "unityLog": str(output / "unity-editor.log"),
    }


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


class Phase184ProfileAcceptanceProtocolTests(unittest.TestCase):
    """Reject incomplete, contradictory, stale, or unsafe Phase184-G evidence."""

    def test_case_profile_table_is_exact_and_directionally_allocated(self):
        """Five cases remain bound to the approved representative profiles."""

        protocol = load_protocol_module()

        self.assertEqual(
            {
                "foxglove-profile",
                "multi-target",
                "degraded-target",
                "qos-contract",
                "stream-640hz",
            },
            set(protocol.CASE_CONTRACTS),
        )
        self.assertEqual(
            "core-foxglove",
            protocol.validate_case_profile("foxglove-profile", None).profile,
        )
        self.assertEqual(
            "jazzy-fastrtps",
            protocol.validate_case_profile("multi-target", "jazzy-fastrtps").profile,
        )
        self.assertEqual(
            "lyrical-zenoh",
            protocol.validate_case_profile("stream-640hz", "lyrical-zenoh").profile,
        )
        with self.assertRaisesRegex(protocol.ProtocolFailure, "FAIL_PREFLIGHT"):
            protocol.validate_case_profile("unknown-case", None)
        with self.assertRaisesRegex(protocol.ProtocolFailure, "FAIL_RUNTIME_SELECTION"):
            protocol.validate_case_profile("stream-640hz", "jazzy-fastrtps")

    def test_mode_validation_rejects_missing_or_contradictory_manual_batch_modes(self):
        """Exactly one of Batch or manual Editor mode is selected."""

        protocol = load_protocol_module()

        self.assertEqual("batch", protocol.validate_execution_mode(batch=True, manual_editor=False))
        self.assertEqual("manual", protocol.validate_execution_mode(batch=False, manual_editor=True))
        for batch, manual in ((False, False), (True, True)):
            with self.assertRaisesRegex(protocol.ProtocolFailure, "FAIL_PREFLIGHT"):
                protocol.validate_execution_mode(batch=batch, manual_editor=manual)

    def test_case_contracts_use_concrete_ros_topic_names(self):
        """Verify case contracts use concrete ROS topic names."""

        protocol = load_protocol_module()

        for contract in protocol.CASE_CONTRACTS.values():
            for topic in contract.topics:
                self.assertTrue(
                    protocol.is_valid_ros_topic_name(topic),
                    topic,
                )
        for topic in (
            "/foxrun/phase184/qos/system-default",
            "/double//slash",
            "relative/topic",
            "/trailing/",
        ):
            self.assertFalse(protocol.is_valid_ros_topic_name(topic), topic)

    def test_run_config_rejects_unsafe_identity_profile_paths_hosts_ports_and_topics(self):
        """Run configuration fails closed before actors start."""

        protocol = load_protocol_module()
        base = run_config(protocol)
        protocol.validate_run_config(base, ROOT)

        mutations = (
            ("token", "../unsafe"),
            ("outputRoot", str(ROOT.parent / "outside")),
            (
                "bridgeOverlayInstall",
                str(
                    ROOT
                    / "build"
                    / "phase184"
                    / "bridge-cache"
                    / "lyrical-zenoh"
                    / "bridge-overlay"
                    / "install"
                ),
            ),
            ("foxgloveHost", "0.0.0.0"),
            ("bridgePort", 70000),
            ("topics", ["/wrong/topic"]),
            ("rmw", "rmw_zenoh_cpp"),
        )
        for key, value in mutations:
            with self.subTest(key=key):
                invalid = copy.deepcopy(base)
                invalid[key] = value
                with self.assertRaises(protocol.ProtocolFailure):
                    protocol.validate_run_config(invalid, ROOT)

    def test_run_config_requires_the_profile_specific_discovery_range(self):
        """Windows discovery stays explicit without breaking the proven FastDDS runtime."""

        protocol = load_protocol_module()
        expected = {
            "core-foxglove": "LOCALHOST",
            "jazzy-fastrtps": "SUBNET",
            "lyrical-zenoh": "LOCALHOST",
        }
        for case, profile in (
            ("foxglove-profile", "core-foxglove"),
            ("multi-target", "jazzy-fastrtps"),
            ("stream-640hz", "lyrical-zenoh"),
        ):
            with self.subTest(profile=profile):
                config = run_config(protocol, case=case, profile=profile)
                self.assertEqual(expected[profile], config["discoveryRange"])
                protocol.validate_run_config(config, ROOT)

                invalid = copy.deepcopy(config)
                invalid["discoveryRange"] = (
                    "LOCALHOST"
                    if expected[profile] == "SUBNET"
                    else "SUBNET"
                )
                with self.assertRaisesRegex(
                    protocol.ProtocolFailure,
                    "FAIL_PREFLIGHT",
                ):
                    protocol.validate_run_config(invalid, ROOT)

    def test_run_config_requires_exact_actor_paths_below_the_owned_output(self):
        """Every required/absent actor has immutable ready and result locations."""

        protocol = load_protocol_module()
        base = run_config(protocol)
        protocol.validate_run_config(base, ROOT)

        missing = copy.deepcopy(base)
        del missing["readyFiles"]["ros2-peer"]
        with self.assertRaisesRegex(protocol.ProtocolFailure, "FAIL_PREFLIGHT"):
            protocol.validate_run_config(missing, ROOT)

        escaped = copy.deepcopy(base)
        escaped["resultFiles"]["bridge"] = str(ROOT / "build" / "escape.json")
        with self.assertRaisesRegex(protocol.ProtocolFailure, "FAIL_PREFLIGHT"):
            protocol.validate_run_config(escaped, ROOT)

    def test_applicability_table_matches_every_approved_case(self):
        """Required and not-applicable sections cannot drift between cases."""

        protocol = load_protocol_module()
        self.assertTrue(protocol.CASE_CONTRACTS["foxglove-profile"].applicability["foxglove"].required)
        self.assertEqual(
            "Foxglove-only case",
            protocol.CASE_CONTRACTS["foxglove-profile"].applicability["rosGraph"].reason,
        )
        self.assertTrue(protocol.CASE_CONTRACTS["multi-target"].applicability["origin"].required)
        self.assertEqual(
            "No ROS publisher is allowed",
            protocol.CASE_CONTRACTS["degraded-target"].applicability["qos"].reason,
        )
        self.assertEqual(
            "No Foxglove direction",
            protocol.CASE_CONTRACTS["qos-contract"].applicability["foxglove"].reason,
        )
        self.assertTrue(protocol.CASE_CONTRACTS["stream-640hz"].applicability["stream"].required)

    def test_positive_summary_requires_all_sections_and_current_token(self):
        """A complete correlated summary is authoritative for PASS."""

        protocol = load_protocol_module()
        config = run_config(protocol)
        summary = valid_summary(protocol, config)

        protocol.validate_summary(
            summary,
            expected_case=str(config["case"]),
            expected_token=str(config["token"]),
        )

        missing = copy.deepcopy(summary)
        del missing["origin"]
        with self.assertRaisesRegex(protocol.ProtocolFailure, "FAIL_TERMINAL"):
            protocol.validate_summary(
                missing,
                expected_case=str(config["case"]),
                expected_token=str(config["token"]),
            )

        with self.assertRaisesRegex(protocol.ProtocolFailure, "FAIL_TERMINAL"):
            protocol.validate_summary(
                summary,
                expected_case=str(config["case"]),
                expected_token="p184g_StaleToken000",
            )

    def test_every_case_fixture_reaches_the_real_summary_validator(self):
        """All five canonical fixtures exercise their case-specific PASS rules."""

        protocol = load_protocol_module()
        for case, profile in (
            ("foxglove-profile", "core-foxglove"),
            ("multi-target", "jazzy-fastrtps"),
            ("degraded-target", "jazzy-fastrtps"),
            ("qos-contract", "jazzy-fastrtps"),
            ("stream-640hz", "lyrical-zenoh"),
        ):
            with self.subTest(case=case):
                config = run_config(protocol, case=case, profile=profile)
                protocol.validate_summary(
                    valid_summary(protocol, config),
                    expected_case=case,
                    expected_token=str(config["token"]),
                )

    def test_positive_summary_rejects_false_required_evidence(self):
        """Exit zero or a terminal marker cannot mask a false proof field."""

        protocol = load_protocol_module()
        config = run_config(protocol)
        summary = valid_summary(protocol, config)
        summary["origin"]["sameOriginDropped"] = False

        with self.assertRaisesRegex(protocol.ProtocolFailure, "FAIL_ORIGIN"):
            protocol.validate_summary(
                summary,
                expected_case=str(config["case"]),
                expected_token=str(config["token"]),
            )

    def test_graph_qos_accepts_explicitly_unrepresented_axes_but_not_conflicts(self):
        """Verify graph QoS accepts explicitly unrepresented axes but not conflicts."""

        protocol = load_protocol_module()
        config = run_config(protocol, case="multi-target")
        summary = valid_summary(protocol, config)
        topic = str(config["topics"][0])
        for endpoint in summary["qos"]["transportObserved"]["graph"][topic][
            "publishers"
        ]:
            endpoint["history"] = "unknown"
            endpoint["depth"] = 0
            endpoint["representedAxes"] = [
                "reliability",
                "durability",
            ]
        for endpoint in summary["qos"]["transportObserved"]["graph"][topic][
            "subscriptions"
        ]:
            endpoint["history"] = "unknown"
            endpoint["depth"] = 0
            endpoint["representedAxes"] = [
                "reliability",
                "durability",
            ]

        protocol.validate_summary(
            summary,
            expected_case="multi-target",
            expected_token=str(config["token"]),
        )

        conflict = copy.deepcopy(summary)
        conflict["qos"]["transportObserved"]["graph"][topic]["publishers"][0][
            "reliability"
        ] = "best_effort"
        with self.assertRaisesRegex(protocol.ProtocolFailure, "FAIL_QOS"):
            protocol.validate_summary(
                conflict,
                expected_case="multi-target",
                expected_token=str(config["token"]),
            )

        unlabeled = copy.deepcopy(summary)
        del unlabeled["qos"]["transportObserved"]["graph"][topic]["publishers"][
            0
        ]["representedAxes"]
        with self.assertRaisesRegex(protocol.ProtocolFailure, "FAIL_QOS"):
            protocol.validate_summary(
                unlabeled,
                expected_case="multi-target",
                expected_token=str(config["token"]),
            )

    def test_qos_system_default_requires_matching_actual_graph_resolution(self):
        """Verify QoS system default requires matching actual graph resolution."""

        protocol = load_protocol_module()
        config = run_config(protocol, case="qos-contract")
        summary = valid_summary(protocol, config)
        topic = str(config["topics"][0])
        publishers = summary["qos"]["transportObserved"]["graph"][topic][
            "publishers"
        ]
        for endpoint in publishers:
            endpoint.update(
                {
                    "reliability": "reliable",
                    "durability": "transient_local",
                    "history": "unknown",
                    "depth": 0,
                    "representedAxes": ["reliability", "durability"],
                }
            )

        protocol.validate_summary(
            summary,
            expected_case="qos-contract",
            expected_token=str(config["token"]),
        )

        divergent = copy.deepcopy(summary)
        divergent["qos"]["transportObserved"]["graph"][topic]["publishers"][1][
            "durability"
        ] = "volatile"
        with self.assertRaisesRegex(protocol.ProtocolFailure, "FAIL_QOS"):
            protocol.validate_summary(
                divergent,
                expected_case="qos-contract",
                expected_token=str(config["token"]),
            )

    def test_qos_transport_sources_are_exact_for_each_case(self):
        """Bridge cases cannot pass on graph-only QoS, and Zenoh stays graph-only."""

        protocol = load_protocol_module()
        for case, profile in (
            ("multi-target", "jazzy-fastrtps"),
            ("qos-contract", "jazzy-fastrtps"),
        ):
            config = run_config(protocol, case=case, profile=profile)
            summary = valid_summary(protocol, config)
            protocol.validate_summary(
                summary,
                expected_case=case,
                expected_token=str(config["token"]),
            )
            for source in ("graph", "bridge"):
                incomplete = copy.deepcopy(summary)
                del incomplete["qos"]["transportObserved"][source]
                with self.assertRaisesRegex(protocol.ProtocolFailure, "FAIL_QOS"):
                    protocol.validate_summary(
                        incomplete,
                        expected_case=case,
                        expected_token=str(config["token"]),
                    )
                incomplete_topic = copy.deepcopy(summary)
                incomplete_topic["qos"]["transportObserved"][source].pop(
                    next(iter(protocol.CASE_CONTRACTS[case].topics))
                )
                with self.assertRaisesRegex(protocol.ProtocolFailure, "FAIL_QOS"):
                    protocol.validate_summary(
                        incomplete_topic,
                        expected_case=case,
                        expected_token=str(config["token"]),
                    )

        config = run_config(
            protocol,
            case="stream-640hz",
            profile="lyrical-zenoh",
        )
        summary = valid_summary(protocol, config)
        protocol.validate_summary(
            summary,
            expected_case="stream-640hz",
            expected_token=str(config["token"]),
        )
        summary["qos"]["transportObserved"]["bridge"] = {"synthetic": {}}
        with self.assertRaisesRegex(protocol.ProtocolFailure, "FAIL_QOS"):
            protocol.validate_summary(
                summary,
                expected_case="stream-640hz",
                expected_token=str(config["token"]),
            )
        incomplete_topic = valid_summary(protocol, config)
        incomplete_topic["qos"]["transportObserved"]["graph"].pop(
            next(iter(protocol.CASE_CONTRACTS["stream-640hz"].topics))
        )
        with self.assertRaisesRegex(protocol.ProtocolFailure, "FAIL_QOS"):
            protocol.validate_summary(
                incomplete_topic,
                expected_case="stream-640hz",
                expected_token=str(config["token"]),
            )

    def test_qos_transport_policy_mismatch_cannot_hide_behind_matches_true(self):
        """Every observed policy axis is compared with the requested contract."""

        protocol = load_protocol_module()
        config = run_config(
            protocol,
            case="qos-contract",
            profile="jazzy-fastrtps",
        )
        summary = valid_summary(protocol, config)
        topic = protocol.CASE_CONTRACTS["qos-contract"].topics[0]
        summary["qos"]["transportObserved"]["graph"][topic]["publishers"][0][
            "reliability"
        ] = "reliable"
        summary["qos"]["matches"] = True

        with self.assertRaisesRegex(protocol.ProtocolFailure, "FAIL_QOS"):
            protocol.validate_summary(
                summary,
                expected_case="qos-contract",
                expected_token=str(config["token"]),
            )

    def test_multi_target_rejects_unbound_sample_and_invalid_publisher_gids(self):
        """A delivery boolean cannot replace token-correlated publisher identity."""

        protocol = load_protocol_module()
        config = run_config(protocol, case="multi-target")
        summary = valid_summary(protocol, config)
        summary["foxglove"]["sampleToken"] = "not-the-current-token"
        summary["rosGraph"]["publisherGids"] = [None, ""]
        summary["targets"]["healthyDelivery"] = True

        with self.assertRaisesRegex(protocol.ProtocolFailure, "FAIL_(?:CLIENT|GRAPH)"):
            protocol.validate_summary(
                summary,
                expected_case="multi-target",
                expected_token=str(config["token"]),
            )

        wrong_sample = valid_summary(protocol, config)
        wrong_sample["rosGraph"]["samplePublisherGids"]["multi-local-1"][
            "sampleSha256"
        ] = "0" * 64
        with self.assertRaisesRegex(protocol.ProtocolFailure, "FAIL_GRAPH"):
            protocol.validate_summary(
                wrong_sample,
                expected_case="multi-target",
                expected_token=str(config["token"]),
            )

        empty_gid = valid_summary(protocol, config)
        empty_gid["rosGraph"]["samplePublisherGids"]["multi-local-1"][
            "publisherGids"
        ] = ["", "gid-0-1"]
        with self.assertRaisesRegex(protocol.ProtocolFailure, "FAIL_GRAPH"):
            protocol.validate_summary(
                empty_gid,
                expected_case="multi-target",
                expected_token=str(config["token"]),
            )

    def test_profile_targets_and_target_state_vocabulary_are_case_exact(self):
        """Empty targets, unknown states, and undeclared fallbacks fail closed."""

        protocol = load_protocol_module()
        profile_config = run_config(
            protocol,
            case="foxglove-profile",
            profile="core-foxglove",
        )
        empty_targets = valid_summary(protocol, profile_config)
        empty_targets["profile"]["targets"] = []
        with self.assertRaisesRegex(protocol.ProtocolFailure, "FAIL_TERMINAL"):
            protocol.validate_summary(
                empty_targets,
                expected_case="foxglove-profile",
                expected_token=str(profile_config["token"]),
            )

        degraded_config = run_config(
            protocol,
            case="degraded-target",
            profile="jazzy-fastrtps",
        )
        fallback = valid_summary(protocol, degraded_config)
        fallback["targets"]["states"]["ros2Native"] = "Ready"
        fallback["targets"]["states"]["foxglove"] = "ON_FIRE"
        with self.assertRaisesRegex(protocol.ProtocolFailure, "FAIL_FANOUT"):
            protocol.validate_summary(
                fallback,
                expected_case="degraded-target",
                expected_token=str(degraded_config["token"]),
            )

        hidden_publisher = valid_summary(protocol, degraded_config)
        topic = protocol.CASE_CONTRACTS["degraded-target"].topics[0]
        hidden_publisher["rosGraph"]["publishersByTopic"][topic] = [
            {"node": "/fallback_native", "gid": "fallback-gid"}
        ]
        hidden_publisher["rosGraph"]["nodeIdentities"] = ["/fallback_native"]
        hidden_publisher["rosGraph"]["publisherGids"] = ["fallback-gid"]
        with self.assertRaisesRegex(protocol.ProtocolFailure, "FAIL_GRAPH"):
            protocol.validate_summary(
                hidden_publisher,
                expected_case="degraded-target",
                expected_token=str(degraded_config["token"]),
            )

    def test_stream_summary_locks_rate_capacity_and_ownership_arithmetic(self):
        """A plausible-looking stream summary cannot hide lost ownership."""

        protocol = load_protocol_module()
        config = run_config(
            protocol,
            case="stream-640hz",
            profile="lyrical-zenoh",
        )
        summary = valid_summary(protocol, config)
        protocol.validate_summary(
            summary,
            expected_case="stream-640hz",
            expected_token=str(config["token"]),
        )

        best_effort_loss = copy.deepcopy(summary)
        best_effort_loss["stream"].update(
            {
                "received": 792,
                "accepted": 792,
                "replaced": 167,
                "rateDropped": 0,
                "transportDropped": 488,
                "dropped": 488,
                "drained": 625,
                "disposed": 792,
                "lastSequence": 1279,
            }
        )
        best_effort_loss["targets"]["statusEvidence"]["received"] = 792
        protocol.validate_summary(
            best_effort_loss,
            expected_case="stream-640hz",
            expected_token=str(config["token"]),
        )

        for field, value in (
            ("offered", 1279),
            ("received", 1281),
            ("maximumQueueDepth", 31),
            ("disposed", 791),
            ("replaced", 0),
            ("lastSequence", 100),
        ):
            invalid = copy.deepcopy(summary)
            invalid["stream"][field] = value
            if field == "received":
                invalid["targets"]["statusEvidence"]["received"] = value
            with self.assertRaisesRegex(protocol.ProtocolFailure, "FAIL_STREAM"):
                protocol.validate_summary(
                    invalid,
                    expected_case="stream-640hz",
                    expected_token=str(config["token"]),
                )

        near_total_loss = valid_summary(protocol, config)
        near_total_loss["stream"].update(
            {
                "received": 1,
                "accepted": 1,
                "replaced": 1,
                "rateDropped": 0,
                "transportDropped": 1279,
                "dropped": 1279,
                "drained": 0,
                "disposed": 1,
                "maximumQueueDepth": 1,
                "lastSequence": 0,
            }
        )
        near_total_loss["targets"]["statusEvidence"]["received"] = 1
        with self.assertRaisesRegex(protocol.ProtocolFailure, "FAIL_STREAM"):
            protocol.validate_summary(
                near_total_loss,
                expected_case="stream-640hz",
                expected_token=str(config["token"]),
            )

        sparse_but_bounded = valid_summary(protocol, config)
        sparse_but_bounded["stream"].update(
            {
                "received": 64,
                "accepted": 64,
                "replaced": 32,
                "rateDropped": 0,
                "transportDropped": 1216,
                "dropped": 1216,
                "drained": 32,
                "disposed": 64,
                "lastSequence": 1279,
            }
        )
        sparse_but_bounded["targets"]["statusEvidence"]["received"] = 64
        protocol.validate_summary(
            sparse_but_bounded,
            expected_case="stream-640hz",
            expected_token=str(config["token"]),
        )

    def test_not_applicable_reason_must_be_case_defined_and_has_no_synthetic_pass(self):
        """N/A evidence is typed, exact, and never carries PASS."""

        protocol = load_protocol_module()
        config = run_config(
            protocol,
            case="degraded-target",
            profile="jazzy-fastrtps",
        )
        summary = valid_summary(protocol, config)
        protocol.validate_summary(
            summary,
            expected_case="degraded-target",
            expected_token=str(config["token"]),
        )

        wrong_reason = copy.deepcopy(summary)
        wrong_reason["origin"]["reason"] = "not used"
        with self.assertRaisesRegex(protocol.ProtocolFailure, "FAIL_TERMINAL"):
            protocol.validate_summary(
                wrong_reason,
                expected_case="degraded-target",
                expected_token=str(config["token"]),
            )

        fake_pass = copy.deepcopy(summary)
        fake_pass["origin"]["result"] = "PASS"
        with self.assertRaisesRegex(protocol.ProtocolFailure, "FAIL_TERMINAL"):
            protocol.validate_summary(
                fake_pass,
                expected_case="degraded-target",
                expected_token=str(config["token"]),
            )

    def test_process_entries_distinguish_unstarted_actor_from_zero_exit(self):
        """A deliberately absent Bridge cannot fabricate exit code zero."""

        protocol = load_protocol_module()
        config = run_config(
            protocol,
            case="degraded-target",
            profile="jazzy-fastrtps",
        )
        summary = valid_summary(protocol, config)
        bridge = next(item for item in summary["processes"] if item["role"] == "bridge")
        self.assertFalse(bridge["started"])
        self.assertNotIn("exitCode", bridge)

        bridge["exitCode"] = 0
        with self.assertRaisesRegex(protocol.ProtocolFailure, "FAIL_PROCESS_EXIT"):
            protocol.validate_summary(
                summary,
                expected_case="degraded-target",
                expected_token=str(config["token"]),
            )

    def test_batch_summary_requires_owned_unity_zero_exit(self):
        """External evidence cannot pass when the owned Batch Editor is absent."""

        protocol = load_protocol_module()
        config = run_config(protocol)
        summary = valid_summary(protocol, config)
        summary["processes"] = [
            entry for entry in summary["processes"] if entry["role"] != "unity"
        ]
        with self.assertRaisesRegex(protocol.ProtocolFailure, "FAIL_PROCESS_EXIT"):
            protocol.validate_summary(
                summary,
                expected_case=str(config["case"]),
                expected_token=str(config["token"]),
            )

    def test_owner_requested_daemon_exit_preserves_raw_windows_evidence(self):
        """Only an owned Bridge/router stop may explain Windows CTRL_BREAK."""

        protocol = load_protocol_module()
        config = run_config(protocol)
        summary = valid_summary(protocol, config)
        bridge = next(item for item in summary["processes"] if item["role"] == "bridge")

        for raw_code in (-1073741510, 3221225786):
            with self.subTest(raw_code=raw_code):
                candidate = copy.deepcopy(summary)
                candidate_bridge = next(
                    item for item in candidate["processes"] if item["role"] == "bridge"
                )
                candidate_bridge["exitCode"] = raw_code
                candidate_bridge["termination"] = "owner_requested"
                protocol.validate_summary(
                    candidate,
                    expected_case=str(config["case"]),
                    expected_token=str(config["token"]),
                )

        bridge["exitCode"] = -1073741510
        bridge["termination"] = "self"
        with self.assertRaisesRegex(protocol.ProtocolFailure, "FAIL_PROCESS_EXIT"):
            protocol.validate_summary(
                summary,
                expected_case=str(config["case"]),
                expected_token=str(config["token"]),
            )

        wrong_role = valid_summary(protocol, config)
        peer = next(
            item for item in wrong_role["processes"] if item["role"] == "ros2-peer"
        )
        peer["exitCode"] = -1073741510
        peer["termination"] = "owner_requested"
        with self.assertRaisesRegex(protocol.ProtocolFailure, "FAIL_PROCESS_EXIT"):
            protocol.validate_summary(
                wrong_role,
                expected_case=str(config["case"]),
                expected_token=str(config["token"]),
            )

    def test_process_exit_requires_explicit_termination_provenance(self):
        """Raw exit codes cannot be interpreted without owner/self provenance."""

        protocol = load_protocol_module()
        config = run_config(protocol)
        summary = valid_summary(protocol, config)
        unity = next(item for item in summary["processes"] if item["role"] == "unity")
        unity.pop("termination")

        with self.assertRaisesRegex(protocol.ProtocolFailure, "FAIL_PROCESS_EXIT"):
            protocol.validate_summary(
                summary,
                expected_case=str(config["case"]),
                expected_token=str(config["token"]),
            )

    def test_failure_classifications_cover_every_planned_stage(self):
        """All automatic failure domains have stable codes."""

        protocol = load_protocol_module()
        expected = {
            "preflight",
            "build",
            "runtime-selection",
            "unity-startup",
            "client",
            "peer",
            "bridge",
            "graph",
            "qos",
            "fanout",
            "origin",
            "stream",
            "terminal",
            "process-exit",
            "cleanup",
            "manual-stopped-early",
        }
        self.assertEqual(expected, set(protocol.FAILURE_CODES))
        self.assertEqual("FAIL_RUNTIME_SELECTION", protocol.failure_code("runtime-selection"))
        self.assertEqual("BLOCKED_BRIDGE", protocol.failure_code("bridge", blocked=True))
        with self.assertRaises(ValueError):
            protocol.failure_code("unknown")

    def test_atomic_json_write_redacts_tokens_commands_and_machine_paths(self):
        """Durable evidence stays bounded and portable without losing safe fields."""

        protocol = load_protocol_module()
        with temporary_directory("protocol-write-") as temporary:
            destination = pathlib.Path(temporary) / "summary.json"
            protocol.write_json_atomic(
                destination,
                {
                    "token": "p184g_secret",
                    "commandLine": ["python", "--token", "p184g_secret"],
                    "environment": {"PASSWORD": "secret"},
                    "repoPath": str(ROOT / "build" / "phase184" / "acceptance"),
                    "externalPath": r"C:\Users\Alice\private",
                    "diagnostic": "x" * 2000,
                    "safe": "retained",
                },
                repo_root=ROOT,
            )
            text = destination.read_text(encoding="utf-8")
            payload = json.loads(text)
            temporary_files = list(destination.parent.glob(destination.name + ".*.tmp"))

        self.assertNotIn("p184g_secret", text)
        self.assertNotIn(r"C:\Users\Alice", text)
        self.assertNotIn("PASSWORD", text)
        self.assertNotIn("commandLine", payload)
        self.assertEqual("<repo>/build/phase184/acceptance", payload["repoPath"])
        self.assertEqual("<redacted-path>", payload["externalPath"])
        self.assertLessEqual(len(payload["diagnostic"]), protocol.MAX_DIAGNOSTIC_CHARACTERS)
        self.assertEqual("retained", payload["safe"])
        self.assertEqual([], temporary_files)

    def test_progress_watchdog_uses_last_progress_age_not_total_duration(self):
        """A long progressing build survives while a silent operation fails."""

        protocol = load_protocol_module()
        clock = [0.0]
        watchdog = protocol.ProgressWatchdog(
            "build",
            stall_seconds=10.0,
            now=lambda: clock[0],
        )
        clock[0] = 9.0
        watchdog.check()
        watchdog.progress("colcon output")
        clock[0] = 18.0
        watchdog.check()
        clock[0] = 20.1
        with self.assertRaisesRegex(protocol.ProtocolFailure, "FAIL_BUILD_STALLED"):
            watchdog.check()

    def test_operation_watchdog_defaults_preserve_unity_and_cold_build_ceilings(self):
        """Established no-progress ceilings remain explicit protocol values."""

        protocol = load_protocol_module()
        self.assertEqual(900, protocol.OPERATION_STALL_SECONDS["runtime-selection"])
        self.assertEqual(1800, protocol.OPERATION_STALL_SECONDS["build"])
        self.assertEqual(30, protocol.OPERATION_STALL_SECONDS["teardown"])

    def test_process_group_options_are_platform_specific_and_argument_array_safe(self):
        """Windows and POSIX children receive owned group construction options."""

        protocol = load_protocol_module()
        windows = protocol.subprocess_group_options("nt")
        posix = protocol.subprocess_group_options("posix")

        self.assertNotEqual(0, windows["creationflags"])
        self.assertFalse(windows["start_new_session"])
        self.assertEqual(0, posix["creationflags"])
        self.assertTrue(posix["start_new_session"])
        with self.assertRaises(ValueError):
            protocol.subprocess_group_options("unknown")


if __name__ == "__main__":
    unittest.main()
