from __future__ import annotations
from .valid_summary import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "test_phase184_profile_acceptance_protocol.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
class _Phase184ProfileAcceptanceProtocolTests_support:
    def test_runtime_type_hints_resolve_for_all_protocol_functions(self):
        """Keep postponed annotations resolvable by runtime tooling."""

        protocol = load_protocol_module()

        for value in vars(protocol).values():
            if (
                callable(value)
                and getattr(value, "__module__", None) == protocol.__name__
            ):
                typing.get_type_hints(value)
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


__all__ = [name for name in globals() if not name.startswith("__")]
