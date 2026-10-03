from __future__ import annotations
from .class_phase179_fox_run_ros2_inbound_acceptance_tests_fixtures import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "test_phase179_foxrun_ros2_inbound_acceptance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
class _Phase179FoxRunRos2InboundAcceptanceTests_validation:
    """Decomposed Phase192 implementation component."""
    def test_qos_negative_establishes_current_string_contract_identity_before_no_apply_proof(self) -> None:
        """A QoS rejection is full evidence only after READY and a new positive String identity baseline."""

        token = "phase179-negative-qos-identity"
        spec = self.smoke.MESSAGE_SPECS["string"]
        args = self.smoke.parse_args(
            [
                "--negative-case",
                "qos-incompatible",
                "--message-set",
                "string",
                "--token",
                token,
                "--unity-ready-token",
                "manual",
                "--unity-log",
                "placeholder.log",
            ]
        )
        endpoint = self.smoke.EndpointEvidence(
            spec.message_type,
            1,
            spec.qos_reliability,
            spec.qos_history,
            spec.qos_depth,
            spec.qos_durability,
        )
        ready = self.smoke.UnityReadyMarker("jazzy", "rmw_fastrtps_cpp", "manual")
        baseline = self.smoke.UnityMarker(
            6,
            "/foxrun/phase179/string",
            token,
            1,
            1,
            0,
            spec.expected_value(token),
        )
        interface = self.smoke.CommandResult(("ros2", "interface", "show"), 0, "", False)
        positive = self.smoke.CommandResult(("ros2", "topic", "pub"), 0, "", False)
        negative = self.smoke.CommandResult(("ros2", "topic", "pub"), 0, "", False)
        with mock.patch.object(self.smoke, "wait_for_unity_ready_marker", return_value=ready) as wait_ready:
            with mock.patch.object(self.smoke, "wait_for_unity_subscription_topic"):
                with mock.patch.object(self.smoke, "query_unity_subscription_endpoint", return_value=endpoint):
                    with mock.patch.object(self.smoke, "unity_log_offset", side_effect=[101, 202]):
                        with mock.patch.object(self.smoke, "wait_for_unity_marker", return_value=baseline) as wait_marker:
                            with mock.patch.object(self.smoke, "wait_for_no_unity_apply_after_offset") as wait_no_apply:
                                with mock.patch.object(self.smoke, "run_bounded_command", side_effect=[interface, positive, negative]) as commands:
                                    result = self.smoke.run_negative_case(args, {}, Path("/usr/bin/ros2"), token)

        self.assertTrue(result["unityReady"])
        self.assertTrue(result["contractIdentity"])
        self.assertTrue(result["unityNoApply"])
        self.assertEqual("best_effort", result["attemptedQosReliability"])
        self.assertEqual("rmw_fastrtps_cpp", wait_ready.call_args.args[2])
        self.assertEqual(101, wait_marker.call_args.kwargs["start_offset"])
        self.assertEqual(202, wait_no_apply.call_args.args[2])
        baseline_publish = commands.call_args_list[1].args[0]
        baseline_topic_index = baseline_publish.index("/foxrun/phase179/string")
        self.assertEqual("std_msgs/msg/String", baseline_publish[baseline_topic_index + 1])
        self.assertEqual("best_effort", commands.call_args_list[2].args[0][commands.call_args_list[2].args[0].index("--qos-reliability") + 1])
        self.assertEqual(
            "EXPECTED_NEGATIVE_QOS_INCOMPATIBLE",
            self.smoke.classify_negative_verdict(
                negative_case="qos-incompatible",
                unity_log_available=True,
                expectation_observed=bool(result["expectationObserved"]),
                unity_ready=bool(result["unityReady"]),
                contract_identity=bool(result["contractIdentity"]),
                unity_no_apply=bool(result["unityNoApply"]),
                failure=None,
            ),
        )
    def test_rmw_negative_binds_ready_marker_to_the_expected_peer_rmw_and_current_token(self) -> None:
        """RMW non-discovery is never full evidence unless Unity reports the deliberately opposite active transport."""

        token = "phase179-negative-rmw-identity"
        args = self.smoke.parse_args(
            [
                "--negative-case",
                "rmw-mismatch",
                "--distro",
                "lyrical",
                "--rmw",
                "rmw_fastrtps_cpp",
                "--negative-peer-rmw",
                "rmw_zenoh_cpp",
                "--message-set",
                "string",
                "--token",
                token,
                "--unity-ready-token",
                token,
                "--unity-log",
                "placeholder.log",
            ]
        )
        ready = self.smoke.UnityReadyMarker("lyrical", "rmw_zenoh_cpp", token)
        interface = self.smoke.CommandResult(("ros2", "interface", "show"), 0, "", False)
        with mock.patch.object(self.smoke, "wait_for_unity_ready_marker", return_value=ready) as wait_ready:
            with mock.patch.object(self.smoke, "unity_log_offset", return_value=77):
                with mock.patch.object(self.smoke, "wait_for_unity_subscription_absence"):
                    with mock.patch.object(self.smoke, "wait_for_no_unity_apply_after_offset"):
                        with mock.patch.object(self.smoke, "run_bounded_command", return_value=interface):
                            result = self.smoke.run_negative_case(args, {}, Path("/usr/bin/ros2"), token)

        self.assertTrue(result["unityReady"])
        self.assertTrue(result["contractIdentity"])
        self.assertEqual("rmw_zenoh_cpp", result["expectedPeerRmw"])
        self.assertEqual("rmw_zenoh_cpp", wait_ready.call_args.args[2])
    def test_type_mismatch_main_records_only_a_local_expected_negative(self) -> None:
        """A rejected wrong-type publication cannot be upgraded into normal interop success."""

        endpoint = self.smoke.EndpointEvidence(
            "std_msgs/msg/String",
            1,
            "reliable",
            "keep_last",
            10,
            "volatile",
        )
        rejected = self.smoke.CommandResult(
            ("ros2", "topic", "pub"),
            1,
            "",
            False,
        )
        interface = self.smoke.CommandResult(
            ("ros2", "interface", "show"),
            0,
            "std_msgs/msg/String\n",
            False,
        )
        with tempfile.TemporaryDirectory() as temp:
            summary_path = Path(temp) / "negative-summary.json"
            with mock.patch.object(self.smoke, "build_linux_environment", return_value={}):
                with mock.patch.object(self.smoke, "collect_optional_windows_peer_diagnostic", return_value="not-requested"):
                    with mock.patch.object(self.smoke, "configure_zenoh_topology", return_value="not-applicable"):
                        with mock.patch.object(self.smoke, "find_ros2_executable", return_value=Path("/usr/bin/ros2")):
                            with mock.patch.object(self.smoke, "wait_for_unity_subscription_topic"):
                                with mock.patch.object(self.smoke, "query_unity_subscription_endpoint", return_value=endpoint):
                                    with mock.patch.object(self.smoke, "run_bounded_command", side_effect=[interface, rejected]) as command:
                                        exit_code = self.smoke.main(
                                            [
                                                "--negative-case",
                                                "type-mismatch",
                                                "--message-set",
                                                "string",
                                                "--token",
                                                "phase179-negative",
                                                "--summary-json",
                                                str(summary_path),
                                            ]
                                        )
            summary = json.loads(summary_path.read_text(encoding="utf-8"))

        self.assertEqual(2, exit_code)
        self.assertEqual(2, command.call_count)
        self.assertEqual("LOCAL_NEGATIVE_EVIDENCE_TYPE_MISMATCH_UNITY_PROOF_PENDING", summary["verdict"])
        self.assertNotEqual("PASS", summary["verdict"])
        self.assertTrue(summary["messageResults"][0]["expectationObserved"])
        self.assertEqual("geometry_msgs/msg/Twist", summary["messageResults"][0]["attemptedMessageType"])
        self.assertFalse(summary["messageResults"][0]["unityCounterUnchanged"])
    def test_type_mismatch_uses_graph_type_evidence_even_when_cli_finishes(self) -> None:
        """A CLI exit code is not transport proof; the observed endpoint type is the mismatch evidence."""

        endpoint = self.smoke.EndpointEvidence(
            "std_msgs/msg/String",
            1,
            "reliable",
            "keep_last",
            10,
            "volatile",
        )
        interface = self.smoke.CommandResult(("ros2", "interface", "show"), 0, "std_msgs/msg/String\n", False)
        completed = self.smoke.CommandResult(("ros2", "topic", "pub"), 0, "", False)
        with tempfile.TemporaryDirectory() as temp:
            summary_path = Path(temp) / "type-mismatch-completed.json"
            with mock.patch.object(self.smoke, "build_linux_environment", return_value={}):
                with mock.patch.object(self.smoke, "collect_optional_windows_peer_diagnostic", return_value="not-requested"):
                    with mock.patch.object(self.smoke, "configure_zenoh_topology", return_value="not-applicable"):
                        with mock.patch.object(self.smoke, "find_ros2_executable", return_value=Path("/usr/bin/ros2")):
                            with mock.patch.object(self.smoke, "wait_for_unity_subscription_topic"):
                                with mock.patch.object(self.smoke, "query_unity_subscription_endpoint", return_value=endpoint):
                                    with mock.patch.object(self.smoke, "run_bounded_command", side_effect=[interface, completed]):
                                        exit_code = self.smoke.main(
                                            [
                                                "--negative-case",
                                                "type-mismatch",
                                                "--message-set",
                                                "string",
                                                "--token",
                                                "phase179-negative-completed",
                                                "--summary-json",
                                                str(summary_path),
                                            ]
                                        )
            summary = json.loads(summary_path.read_text(encoding="utf-8"))

        self.assertEqual(2, exit_code)
        self.assertEqual("LOCAL_NEGATIVE_EVIDENCE_TYPE_MISMATCH_UNITY_PROOF_PENDING", summary["verdict"])
        self.assertEqual("completed", summary["messageResults"][0]["negativePublishOutcome"])
    def test_qos_negative_requires_a_current_ready_identity_before_full_expected_negative(self) -> None:
        """A no-apply window without a current Unity READY identity cannot certify a QoS rejection."""

        endpoint = self.smoke.EndpointEvidence(
            "std_msgs/msg/String",
            1,
            "reliable",
            "keep_last",
            10,
            "volatile",
        )
        interface = self.smoke.CommandResult(("ros2", "interface", "show"), 0, "std_msgs/msg/String\n", False)
        completed = self.smoke.CommandResult(("ros2", "topic", "pub"), 0, "", False)
        with tempfile.TemporaryDirectory() as temp:
            summary_path = Path(temp) / "qos-negative-summary.json"
            unity_log = Path(temp) / "Unity.log"
            unity_log.write_text("Unity started\n", encoding="utf-8")
            with mock.patch.object(self.smoke, "build_linux_environment", return_value={}):
                with mock.patch.object(self.smoke, "collect_optional_windows_peer_diagnostic", return_value="not-requested"):
                    with mock.patch.object(self.smoke, "configure_zenoh_topology", return_value="not-applicable"):
                        with mock.patch.object(self.smoke, "find_ros2_executable", return_value=Path("/usr/bin/ros2")):
                            with mock.patch.object(self.smoke, "wait_for_unity_subscription_topic"):
                                with mock.patch.object(self.smoke, "query_unity_subscription_endpoint", return_value=endpoint):
                                    with mock.patch.object(self.smoke, "run_bounded_command", side_effect=[interface, completed]):
                                        exit_code = self.smoke.main(
                                            [
                                                "--negative-case",
                                                "qos-incompatible",
                                                "--message-set",
                                                "string",
                                                "--token",
                                                "phase179-negative-qos",
                                                "--unity-log",
                                                str(unity_log),
                                                "--timeout-seconds",
                                                "0.01",
                                                "--summary-json",
                                                str(summary_path),
                                            ]
                                        )
            summary = json.loads(summary_path.read_text(encoding="utf-8"))

        self.assertEqual(2, exit_code)
        self.assertEqual("FAIL_READY", summary["verdict"])
        self.assertEqual("best_effort", summary["messageResults"][0]["attemptedQosReliability"])
        self.assertFalse(summary["messageResults"][0]["unityNoApply"])
        self.assertFalse(summary["messageResults"][0]["unityCounterUnchanged"])
        self.assertNotEqual("PASS", summary["verdict"])
    def test_rmw_negative_observes_absence_without_constructing_a_fallback_publication(self) -> None:
        """RMW mismatch keeps the caller-selected environment and only records bounded non-discovery."""

        interface = self.smoke.CommandResult(("ros2", "interface", "show"), 0, "std_msgs/msg/String\n", False)
        with tempfile.TemporaryDirectory() as temp:
            summary_path = Path(temp) / "rmw-negative-summary.json"
            with mock.patch.object(self.smoke, "build_linux_environment", return_value={}):
                with mock.patch.object(self.smoke, "collect_optional_windows_peer_diagnostic", return_value="not-requested"):
                    with mock.patch.object(self.smoke, "configure_zenoh_topology", return_value="not-applicable"):
                        with mock.patch.object(self.smoke, "find_ros2_executable", return_value=Path("/usr/bin/ros2")):
                            with mock.patch.object(self.smoke, "wait_for_unity_subscription_absence") as absent:
                                with mock.patch.object(self.smoke, "build_negative_publish_command") as build_negative:
                                    with mock.patch.object(self.smoke, "run_bounded_command", return_value=interface) as command:
                                        exit_code = self.smoke.main(
                                            [
                                                "--negative-case",
                                                "rmw-mismatch",
                                                "--rmw",
                                                "rmw_fastrtps_cpp",
                                                "--negative-peer-rmw",
                                                "rmw_zenoh_cpp",
                                                "--message-set",
                                                "string",
                                                "--token",
                                                "phase179-negative-rmw",
                                                "--summary-json",
                                                str(summary_path),
                                            ]
                                        )
            summary = json.loads(summary_path.read_text(encoding="utf-8"))

        self.assertEqual(2, exit_code)
        absent.assert_called_once()
        build_negative.assert_not_called()
        self.assertEqual(1, command.call_count)
        self.assertEqual("rmw_fastrtps_cpp", summary["rmwImplementation"])
        self.assertEqual("rmw_zenoh_cpp", summary["negativePeerRmw"])
        self.assertEqual("LOCAL_NEGATIVE_EVIDENCE_RMW_MISMATCH_UNITY_PROOF_PENDING", summary["verdict"])
        self.assertNotEqual("PASS", summary["verdict"])
    def test_no_unity_log_is_explicit_peer_publish_pending_not_pass(self) -> None:
        """Linux publication alone must never produce a green interop result."""

        verdict = self.smoke.classify_verdict(
            unity_log_available=False,
            message_results=[{"name": "string", "published": True}],
            failure=None,
        )

        self.assertEqual("PEER_PUBLISH_COMPLETE_UNITY_PROOF_PENDING", verdict)
        self.assertNotEqual("PASS", verdict)
    def test_string_burst_uses_bounded_rclpy_argv_and_requires_final_latest_wins_marker(self) -> None:
        """Burst acceptance proves the final sequence survived without requiring every intermediate apply."""

        token = "phase179-burst"
        command = self.smoke.build_string_burst_command(
            Path("/usr/bin/python3"),
            "/foxrun/phase179/string",
            token,
            final_sequence=8,
            rate_hz=500.0,
        )
        self.assertEqual(str(Path("/usr/bin/python3")), command[0])
        self.assertEqual("-c", command[1])
        self.assertIn("rclpy", command[2])
        self.assertIn("get_subscription_count", command[2])
        self.assertIn("DurabilityPolicy", command[2])
        self.assertIn("durability=DurabilityPolicy.VOLATILE", command[2])
        self.assertEqual("8", command[-2])
        self.assertEqual("500.0", command[-1])

        baseline = self.smoke.UnityMarker(
            session=3,
            topic="/foxrun/phase179/string",
            token=token,
            received=1,
            applied=1,
            replaced=0,
            value={"type": "String", "data": token},
        )
        final = self.smoke.UnityMarker(
            session=3,
            topic="/foxrun/phase179/string",
            token=token,
            received=9,
            applied=2,
            replaced=7,
            value=self.smoke.expected_string_burst_value(token, 8),
        )

        evidence = self.smoke.validate_string_burst_marker(baseline, final, token, 8)

        self.assertEqual(8, evidence["finalSequence"])
        self.assertEqual(9, evidence["total"])
        self.assertEqual(7, evidence["replaced"])
    def test_string_burst_rejects_zero_final_sequence_before_constructing_a_probe(self) -> None:
        """A one-message burst cannot prove latest-wins replacement and is not an acceptance case."""

        with self.assertRaises(ValueError):
            self.smoke.build_string_burst_command(
                Path("/usr/bin/python3"),
                "/foxrun/phase179/string",
                "phase179-burst-zero",
                final_sequence=0,
                rate_hz=500.0,
            )
    def test_string_burst_rejects_final_marker_without_latest_wins_replacement(self) -> None:
        """A final value alone is insufficient if the configured overload never exercised replacement."""

        token = "phase179-burst"
        baseline = self.smoke.UnityMarker(3, "/foxrun/phase179/string", token, 1, 1, 0, {"type": "String", "data": token})
        final = self.smoke.UnityMarker(
            3,
            "/foxrun/phase179/string",
            token,
            9,
            9,
            0,
            self.smoke.expected_string_burst_value(token, 8),
        )

        with self.assertRaises(self.smoke.AcceptanceFailure) as context:
            self.smoke.validate_string_burst_marker(baseline, final, token, 8)

        self.assertEqual("BURST", context.exception.category)
    def test_wait_for_unity_marker_times_out_with_stable_category(self) -> None:
        """A missing bounded Unity proof is a Unity timeout, not a publish pass."""

        with tempfile.TemporaryDirectory() as temp:
            log = Path(temp) / "Unity.log"
            log.write_text("Unity started\n", encoding="utf-8")
            with self.assertRaises(self.smoke.AcceptanceFailure) as context:
                self.smoke.wait_for_unity_marker(
                    log,
                    "/foxrun/phase179/string",
                    "phase179-timeout",
                    {"data": "phase179-timeout"},
                    timeout_seconds=0.0,
                )

        self.assertEqual("UNITY_TIMEOUT", context.exception.category)
class _Phase179FoxRunRos2InboundAcceptanceTests_runtime:
    """Decomposed Phase192 implementation component."""
    def test_bounded_command_terminates_only_its_owned_process_after_timeout(self) -> None:
        """Timeout cleanup targets the launched CLI process and never a global ROS process list."""

        process = mock.Mock()
        process.pid = 321
        process.communicate.side_effect = [
            subprocess.TimeoutExpired(["ros2"], 0.1),
            ("timed out", None),
        ]
        process.returncode = -9
        with mock.patch.object(self.smoke.subprocess, "Popen", return_value=process):
            with mock.patch.object(self.smoke, "terminate_owned_process") as terminate:
                result = self.smoke.run_bounded_command(
                    ["ros2", "topic", "list"],
                    {},
                    timeout_seconds=0.1,
                    label="topic list",
                )

        self.assertTrue(result.timed_out)
        terminate.assert_called_once_with(process)
    def test_summary_sanitization_does_not_persist_zenoh_paths_or_secrets(self) -> None:
        """Machine topology details and credentials stay out of portable evidence JSON."""

        payload = self.smoke.sanitize_summary(
            {
                "token": "correlation-token",
                "zenohRouterPath": "C:/private/router?password=super-secret",
                "error": "password=super-secret",
                "messageResults": [],
            }
        )
        serialized = json.dumps(payload)

        self.assertIn("correlation-token", serialized)
        self.assertNotIn("super-secret", serialized)
        self.assertNotIn("zenohRouterPath", payload)


__all__ = [name for name in globals() if not name.startswith("__")]
