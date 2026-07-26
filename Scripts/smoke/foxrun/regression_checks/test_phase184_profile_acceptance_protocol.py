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
    return {
        "schemaVersion": protocol.RUN_CONFIG_SCHEMA_VERSION,
        "executionMode": "batch",
        "runId": run_id,
        "token": "p184g_A1b2C3d4E5f6",
        "case": case,
        "profile": profile,
        "projectPath": str(ROOT / "Unity2Foxglove"),
        "outputRoot": str(output),
        "rosDistro": "jazzy" if profile == "jazzy-fastrtps" else "lyrical",
        "rmw": "rmw_fastrtps_cpp" if profile == "jazzy-fastrtps" else "rmw_zenoh_cpp",
        "domainId": 48,
        "discoveryRange": "LOCALHOST",
        "zenohTopologyId": "phase184-local" if profile == "lyrical-zenoh" else "",
        "phase181Workspace": str(ROOT / "build" / "phase181" / profile),
        "phase181Install": str(ROOT / "build" / "phase181" / profile / "install"),
        "bridgeOverlayInstall": str(output / "bridge-overlay" / "install"),
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
    contract = protocol.CASE_CONTRACTS[case]
    applicability = contract.applicability
    transport_observed = {
        "graph": {
            topic: {"publishers": [{"reliability": "reliable"}]}
            for topic in contract.topics
        },
    }
    if case in {"multi-target", "qos-contract"}:
        transport_observed["bridge"] = {
            topic: {"reliability": "reliable"}
            for topic in contract.topics
        }
    sections: dict[str, object] = {}
    evidence = {
        "foxglove": {
            "deliveryObserved": True,
            "channelEncodings": ["protobuf"],
            "sampleToken": "logical-1",
            "timestamp": 42,
        },
        "rosGraph": {
            "endpointsObserved": True,
            "nodeIdentities": [
                "/unity2foxglove_foxrun",
                "/unity2foxglove_ros2_bridge",
            ],
            "publisherGids": ["gid-native", "gid-bridge"],
        },
        "qos": {
            "requested": {"profile": "default"},
            "transportObserved": transport_observed,
            "matches": True,
        },
        "targets": {
            "states": {"foxglove": "Ready", "ros2Native": "Ready"},
            "diagnosticCounts": {"warning": 0, "error": 0},
            "healthyDelivery": True,
        },
        "origin": {
            "remoteApplied": True,
            "sameOriginDropped": True,
            "laterLocalPublished": True,
        },
        "stream": {
            "offered": 1280,
            "accepted": 1024,
            "replaced": 224,
            "dropped": 256,
            "drained": 800,
            "disposed": 1024,
            "maximumQueueDepth": 32,
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
    if case == "degraded-target":
        sections["targets"] = {
            "applicability": "required",
            "states": {
                "foxglove": "Ready",
                "ros2Bridge": "Unavailable",
            },
            "diagnosticCounts": {"bridge": 1, "error": 0},
            "healthyDelivery": True,
        }

    required_actors = protocol.CASE_CONTRACTS[case].required_actors
    absent_actors = protocol.CASE_CONTRACTS[case].deliberately_absent_actors
    process_entries = [
        {"role": actor, "started": True, "exitCode": 0}
        for actor in sorted(required_actors | {"unity"})
    ]
    process_entries.extend(
        {"role": actor, "started": False, "reason": reason}
        for actor, reason in sorted(absent_actors.items())
    )
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
            "source": "Ros2Native",
            "targets": ["Foxglove", "Ros2Native", "Ros2Bridge"],
            "publishEncoding": "protobuf",
            "subscribeEncoding": "protobuf",
            "requestedQos": {"profile": "default"},
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

    def test_run_config_rejects_unsafe_identity_profile_paths_hosts_ports_and_topics(self):
        """Run configuration fails closed before actors start."""

        protocol = load_protocol_module()
        base = run_config(protocol)
        protocol.validate_run_config(base, ROOT)

        mutations = (
            ("token", "../unsafe"),
            ("outputRoot", str(ROOT.parent / "outside")),
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

        for field, value in (
            ("offered", 1279),
            ("maximumQueueDepth", 31),
            ("disposed", 1023),
            ("replaced", 0),
        ):
            invalid = copy.deepcopy(summary)
            invalid["stream"][field] = value
            with self.assertRaisesRegex(protocol.ProtocolFailure, "FAIL_STREAM"):
                protocol.validate_summary(
                    invalid,
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
