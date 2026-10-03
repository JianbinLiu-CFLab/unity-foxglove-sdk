from __future__ import annotations
from .class_phase184_profile_acceptance_protocol_tests_support import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "test_phase184_profile_acceptance_protocol.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
class _Phase184ProfileAcceptanceProtocolTests_fixtures:
    """Decomposed Phase192 implementation component."""
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


__all__ = [name for name in globals() if not name.startswith("__")]
