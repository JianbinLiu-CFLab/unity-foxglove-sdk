from __future__ import annotations
from .foundation import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "test_phase179_foxrun_ros2_inbound_acceptance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
class _Phase179FoxRunRos2InboundAcceptanceTests_fixtures:
    """Decomposed Phase192 implementation component."""
    def test_ready_marker_baseline_requires_a_new_unseen_token_when_editor_log_is_reused(self) -> None:
        """A reused Editor.log may overwrite below EOF, so local acceptance must reject its pre-run READY token."""

        stale_token = "phase179-ready-before-play"
        fresh_token = "phase179-ready-after-play"
        stale = f"PHASE179_ROS2_INBOUND_READY runtime=humble rmw=rmw_fastrtps_cpp token={stale_token}\n"
        fresh = f"PHASE179_ROS2_INBOUND_READY runtime=humble rmw=rmw_fastrtps_cpp token={fresh_token}\n"
        with tempfile.TemporaryDirectory() as temp:
            log = Path(temp) / "Editor.log"
            log.write_text(stale, encoding="utf-8")
            baseline = self.smoke.capture_unity_ready_marker_tokens(log, "humble", "rmw_fastrtps_cpp")

        self.assertEqual(frozenset({stale_token}), baseline)
        ready = self.smoke.find_matching_unity_ready_marker(
            stale + fresh,
            "humble",
            "rmw_fastrtps_cpp",
            None,
            excluded_tokens=baseline,
        )
        self.assertEqual(fresh_token, ready.token)
        with self.assertRaises(self.smoke.AcceptanceFailure) as context:
            self.smoke.find_matching_unity_ready_marker(
                stale,
                "humble",
                "rmw_fastrtps_cpp",
                None,
                excluded_tokens=baseline,
            )
        self.assertEqual("READY_STALE", context.exception.category)
    def test_ready_wait_after_offset_cannot_reuse_a_stale_identity(self) -> None:
        """An Editor host must not accept a READY marker written before it started observing the log."""

        token = "phase179-ready-reused-token"
        stale = f"PHASE179_ROS2_INBOUND_READY runtime=humble rmw=rmw_fastrtps_cpp token={token}\n"
        with tempfile.TemporaryDirectory() as temp:
            log = Path(temp) / "Editor.log"
            log.write_text(stale, encoding="utf-8")
            offset = self.smoke.unity_log_offset(log)
            with self.assertRaises(self.smoke.AcceptanceFailure) as context:
                self.smoke.wait_for_unity_ready_marker(
                    log,
                    "humble",
                    "rmw_fastrtps_cpp",
                    token,
                    timeout_seconds=0.0,
                    start_offset=offset,
                )

        self.assertEqual("READY_TIMEOUT", context.exception.category)
    def test_endpoint_validation_requires_subscriber_type_and_contract_qos(self) -> None:
        """Discovery must reject an endpoint that reports the wrong native QoS."""

        spec = self.smoke.MESSAGE_SPECS["string"]
        wrong_qos = (
            "Type: std_msgs/msg/String\n"
            "Subscription count: 1\n"
            "Subscription #0:\n"
            "  Reliability: BEST_EFFORT\n"
            "  History (Depth): KEEP_LAST (10)\n"
            "  Durability: VOLATILE\n"
        )
        with self.assertRaises(self.smoke.AcceptanceFailure) as context:
            self.smoke.validate_unity_subscription_endpoint(wrong_qos, spec)

        self.assertEqual("ENDPOINT", context.exception.category)
    def test_endpoint_validation_returns_safe_type_count_and_qos_evidence(self) -> None:
        """Summary evidence records validated graph facts, not raw machine-specific CLI output."""

        spec = self.smoke.MESSAGE_SPECS["twist"]
        info = (
            "Type: geometry_msgs/msg/Twist\n"
            "Subscription count: 2\n"
            "Subscription #0:\n"
            "  Reliability: RELIABLE\n"
            "  History (Depth): KEEP_LAST (10)\n"
            "  Durability: VOLATILE\n"
        )

        evidence = self.smoke.validate_unity_subscription_endpoint(info, spec)

        self.assertEqual("geometry_msgs/msg/Twist", evidence.message_type)
        self.assertEqual(2, evidence.subscription_count)
        self.assertEqual("reliable", evidence.qos_reliability)
        self.assertEqual("keep_last", evidence.qos_history)
        self.assertEqual(10, evidence.qos_depth)
        self.assertEqual("volatile", evidence.qos_durability)
    def test_endpoint_validation_requires_keep_last_depth_and_volatile_durability(self) -> None:
        """A matching reliability alone cannot hide an incompatible native endpoint profile."""

        spec = self.smoke.MESSAGE_SPECS["joy"]
        valid = (
            "Type: sensor_msgs/msg/Joy\n"
            "Subscription count: 1\n"
            "Subscription #0:\n"
            "  Reliability: BEST_EFFORT\n"
            "  History (Depth): KEEP_LAST (5)\n"
            "  Durability: VOLATILE\n"
        )
        evidence = self.smoke.validate_unity_subscription_endpoint(valid, spec)
        self.assertEqual("keep_last", evidence.qos_history)
        self.assertEqual(5, evidence.qos_depth)
        self.assertEqual("volatile", evidence.qos_durability)

        for label, invalid in (
            ("history", valid.replace("KEEP_LAST", "KEEP_ALL")),
            ("depth", valid.replace("(5)", "(10)")),
            ("durability", valid.replace("VOLATILE", "TRANSIENT_LOCAL")),
        ):
            with self.subTest(label=label):
                with self.assertRaises(self.smoke.AcceptanceFailure) as context:
                    self.smoke.validate_unity_subscription_endpoint(invalid, spec)
                self.assertEqual("ENDPOINT", context.exception.category)
    def test_endpoint_probe_waits_through_graph_lag_until_unity_qos_is_visible(self) -> None:
        """A listed topic may precede verbose endpoint QoS discovery by one probe."""

        spec = self.smoke.MESSAGE_SPECS["string"]
        unavailable = self.smoke.CommandResult(
            ("ros2", "topic", "info"),
            0,
            "Type: std_msgs/msg/String\nSubscription count: 0\n",
            False,
        )
        ready = self.smoke.CommandResult(
            ("ros2", "topic", "info"),
            0,
            "Type: std_msgs/msg/String\nSubscription count: 1\nSubscription #0:\n"
            "  Reliability: RELIABLE\n  History (Depth): KEEP_LAST (10)\n  Durability: VOLATILE\n",
            False,
        )
        with mock.patch.object(self.smoke, "run_bounded_command", side_effect=[unavailable, ready]) as probe:
            with mock.patch.object(self.smoke.time, "sleep"):
                evidence = self.smoke.query_unity_subscription_endpoint(
                    Path("/usr/bin/ros2"),
                    {},
                    "/foxrun/phase179/string",
                    spec,
                    timeout_seconds=1.0,
                )

        self.assertEqual(2, probe.call_count)
        self.assertEqual(1, evidence.subscription_count)
    def test_positive_main_captures_log_offset_before_publication(self) -> None:
        """A positive reused token is accepted only from log content appended after its own publish starts."""

        token = "phase179-positive-offset"
        spec = self.smoke.MESSAGE_SPECS["string"]
        endpoint = self.smoke.EndpointEvidence(
            spec.message_type,
            1,
            spec.qos_reliability,
            spec.qos_history,
            spec.qos_depth,
            spec.qos_durability,
        )
        marker = self.smoke.UnityMarker(
            1,
            "/foxrun/phase179/string",
            token,
            1,
            1,
            0,
            spec.expected_value(token),
        )
        events: list[str] = []

        def command(command_argv, _env, _timeout, _label):
            """Record the command ordering while returning a successful owned process result."""
            events.append("publish" if command_argv[1:3] == ["topic", "pub"] else "interface")
            return self.smoke.CommandResult(tuple(command_argv), 0, "", False)

        def offset(_log: Path) -> int:
            """Record the fresh-log checkpoint used by the positive correlation test."""
            events.append("offset")
            return 41

        with tempfile.TemporaryDirectory() as temp:
            summary_path = Path(temp) / "positive-offset-summary.json"
            unity_log = Path(temp) / "Unity.log"
            unity_log.write_text("Unity started\n", encoding="utf-8")
            with mock.patch.object(self.smoke, "build_linux_environment", return_value={}):
                with mock.patch.object(self.smoke, "collect_optional_windows_peer_diagnostic", return_value="not-requested"):
                    with mock.patch.object(self.smoke, "configure_zenoh_topology", return_value="not-applicable"):
                        with mock.patch.object(self.smoke, "find_ros2_executable", return_value=Path("/usr/bin/ros2")):
                            with mock.patch.object(self.smoke, "wait_for_unity_subscription_topic"):
                                with mock.patch.object(self.smoke, "query_unity_subscription_endpoint", return_value=endpoint):
                                    with mock.patch.object(self.smoke, "run_bounded_command", side_effect=command):
                                        with mock.patch.object(self.smoke, "unity_log_offset", side_effect=offset):
                                            with mock.patch.object(self.smoke, "wait_for_unity_marker", return_value=marker) as wait_marker:
                                                exit_code = self.smoke.main(
                                                    [
                                                        "--message-set",
                                                        "string",
                                                        "--token",
                                                        token,
                                                        "--unity-log",
                                                        str(unity_log),
                                                        "--summary-json",
                                                        str(summary_path),
                                                    ]
                                                )

        self.assertEqual(0, exit_code)
        self.assertLess(events.index("offset"), events.index("publish"))
        self.assertEqual(41, wait_marker.call_args.kwargs["start_offset"])
    def test_burst_main_captures_a_fresh_log_offset_before_burst_publication(self) -> None:
        """The final burst marker cannot be satisfied by the baseline marker or an older same-token burst."""

        token = "phase179-burst-offset"
        spec = self.smoke.MESSAGE_SPECS["string"]
        endpoint = self.smoke.EndpointEvidence(
            spec.message_type,
            1,
            spec.qos_reliability,
            spec.qos_history,
            spec.qos_depth,
            spec.qos_durability,
        )
        baseline = self.smoke.UnityMarker(1, "/foxrun/phase179/string", token, 1, 1, 0, spec.expected_value(token))
        final = self.smoke.UnityMarker(
            1,
            "/foxrun/phase179/string",
            token,
            3,
            2,
            1,
            self.smoke.expected_string_burst_value(token, 1),
        )
        events: list[str] = []

        def command(command_argv, _env, _timeout, _label):
            """Record each baseline or burst process command without launching ROS2."""
            events.append("publish" if command_argv[1:3] == ["topic", "pub"] else "interface")
            return self.smoke.CommandResult(tuple(command_argv), 0, "", False)

        def offset(_log: Path) -> int:
            """Return distinct checkpoints to prove the burst path takes a fresh offset."""
            value = 101 if events.count("offset") == 0 else 202
            events.append("offset")
            return value

        with tempfile.TemporaryDirectory() as temp:
            summary_path = Path(temp) / "burst-offset-summary.json"
            unity_log = Path(temp) / "Unity.log"
            unity_log.write_text("Unity started\n", encoding="utf-8")
            with mock.patch.object(self.smoke, "build_linux_environment", return_value={}):
                with mock.patch.object(self.smoke, "collect_optional_windows_peer_diagnostic", return_value="not-requested"):
                    with mock.patch.object(self.smoke, "configure_zenoh_topology", return_value="not-applicable"):
                        with mock.patch.object(self.smoke, "find_ros2_executable", return_value=Path("/usr/bin/ros2")):
                            with mock.patch.object(self.smoke, "wait_for_unity_subscription_topic"):
                                with mock.patch.object(self.smoke, "query_unity_subscription_endpoint", return_value=endpoint):
                                    with mock.patch.object(self.smoke, "run_bounded_command", side_effect=command):
                                        with mock.patch.object(self.smoke, "unity_log_offset", side_effect=offset):
                                            with mock.patch.object(self.smoke, "run_string_burst", side_effect=lambda *_args: events.append("burst")):
                                                with mock.patch.object(self.smoke, "wait_for_unity_marker", side_effect=[baseline, final]) as wait_marker:
                                                    exit_code = self.smoke.main(
                                                        [
                                                            "--message-set",
                                                            "string",
                                                            "--token",
                                                            token,
                                                            "--unity-log",
                                                            str(unity_log),
                                                            "--string-burst-final-sequence",
                                                            "1",
                                                            "--summary-json",
                                                            str(summary_path),
                                                        ]
                                                    )

        self.assertEqual(0, exit_code)
        self.assertEqual(101, wait_marker.call_args_list[0].kwargs["start_offset"])
        self.assertEqual(202, wait_marker.call_args_list[1].kwargs["start_offset"])
        self.assertLess([index for index, event in enumerate(events) if event == "offset"][1], events.index("burst"))
    def test_negative_publish_commands_are_shell_free_and_deliberately_incompatible(self) -> None:
        """Type and QoS negative probes must not mutate the selected transport or contract topic."""

        string_spec = self.smoke.MESSAGE_SPECS["string"]
        type_mismatch = self.smoke.build_negative_publish_command(
            Path("/usr/bin/ros2"),
            string_spec,
            "phase179-negative",
            "type-mismatch",
        )
        self.assertIsInstance(type_mismatch, list)
        type_topic_index = type_mismatch.index("/foxrun/phase179/string")
        self.assertEqual("geometry_msgs/msg/Twist", type_mismatch[type_topic_index + 1])
        self.assertNotIn("shell", " ".join(type_mismatch).lower())

        qos_mismatch = self.smoke.build_negative_publish_command(
            Path("/usr/bin/ros2"),
            string_spec,
            "phase179-negative",
            "qos-incompatible",
        )
        qos_topic_index = qos_mismatch.index("/foxrun/phase179/string")
        self.assertEqual("std_msgs/msg/String", qos_mismatch[qos_topic_index + 1])
        self.assertEqual("best_effort", qos_mismatch[qos_mismatch.index("--qos-reliability") + 1])
        self.assertEqual("keep_last", qos_mismatch[qos_mismatch.index("--qos-history") + 1])
        self.assertEqual("volatile", qos_mismatch[qos_mismatch.index("--qos-durability") + 1])
    def test_negative_verdicts_never_claim_positive_interoperability(self) -> None:
        """Expected rejection stays distinct from a PASS and retains missing-Unity-proof status."""

        verified = self.smoke.classify_negative_verdict(
            negative_case="qos-incompatible",
            unity_log_available=True,
            expectation_observed=True,
            unity_ready=True,
            contract_identity=True,
            unity_no_apply=True,
            failure=None,
        )
        pending = self.smoke.classify_negative_verdict(
            negative_case="rmw-mismatch",
            unity_log_available=False,
            expectation_observed=True,
            unity_ready=False,
            contract_identity=False,
            unity_no_apply=False,
            failure=None,
        )
        missing_ready = self.smoke.classify_negative_verdict(
            negative_case="type-mismatch",
            unity_log_available=True,
            expectation_observed=True,
            unity_ready=False,
            contract_identity=False,
            unity_no_apply=True,
            failure=None,
        )
        missing_contract = self.smoke.classify_negative_verdict(
            negative_case="type-mismatch",
            unity_log_available=True,
            expectation_observed=True,
            unity_ready=True,
            contract_identity=False,
            unity_no_apply=True,
            failure=None,
        )

        self.assertEqual("EXPECTED_NEGATIVE_QOS_INCOMPATIBLE", verified)
        self.assertEqual("LOCAL_NEGATIVE_EVIDENCE_RMW_MISMATCH_UNITY_PROOF_PENDING", pending)
        self.assertEqual("FAIL_READY", missing_ready)
        self.assertEqual("FAIL_CONTRACT_IDENTITY", missing_contract)
        self.assertNotEqual("PASS", verified)
        self.assertNotEqual("PASS", pending)


__all__ = [name for name in globals() if not name.startswith("__")]
