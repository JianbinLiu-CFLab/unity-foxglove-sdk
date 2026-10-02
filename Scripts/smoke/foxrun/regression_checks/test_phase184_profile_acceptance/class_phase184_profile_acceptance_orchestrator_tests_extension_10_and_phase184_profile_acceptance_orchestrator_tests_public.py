from __future__ import annotations
from .class_phase184_profile_acceptance_orchestrator_tests_extension_9 import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "test_phase184_profile_acceptance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
class _Phase184ProfileAcceptanceOrchestratorTests_extension_10:
    """Decomposed Phase192 implementation component."""
    def test_summary_evidence_comes_from_correlated_unity_runtime_markers(self):
        """Profile and healthy fanout claims must be parsed from current Unity markers."""

        module = load_module()
        token = "p184g_A1b2C3d4E5f6"
        case = "multi-target"
        TEST_ROOT.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix="observed-", dir=TEST_ROOT) as raw:
            log_path = pathlib.Path(raw) / "unity.log"
            log_path.write_text(
                "\n".join(
                    (
                        "PHASE184G_PROFILE_EVIDENCE "
                        f"case={case} token=p184g_Stale00000000 "
                        "source=Foxglove targets=Foxglove "
                        "publishEncoding=json subscribeEncoding=json",
                        "PHASE184G_PROFILE_EVIDENCE "
                        f"case={case} token={token} "
                        "source=Ros2Native "
                        "targets=Foxglove,Ros2Native,Ros2Bridge "
                        "publishEncoding=protobuf subscribeEncoding=protobuf",
                        "PHASE184G_MULTI_TARGET_STATUS "
                        f"case={case} token={token} status=Ready "
                        "succeeded=Foxglove,Ros2Native,Ros2Bridge failed=None "
                        "bridgeRuntimeFailures=0",
                    )
                )
                + "\n",
                encoding="utf-8",
            )
            config = {
                "case": case,
                "token": token,
                "unityLog": str(log_path),
                "topics": ["/foxrun/phase184/multi/state"],
            }

            self.assertEqual(
                {
                    "source": "Ros2Native",
                    "targets": ["Foxglove", "Ros2Native", "Ros2Bridge"],
                    "publishEncoding": "protobuf",
                    "subscribeEncoding": "protobuf",
                },
                module._observed_profile_evidence(config),
            )
            self.assertEqual(
                {
                    "states": {
                        "foxglove": "Ready",
                        "ros2Native": "Ready",
                        "ros2Bridge": "Ready",
                    },
                    "diagnosticCounts": {
                        "failedTargets": 0,
                        "bridgeRuntimeFailures": 0,
                    },
                    "statusEvidence": {
                        "aggregate": "Ready",
                        "succeeded": "Foxglove,Ros2Native,Ros2Bridge",
                        "failed": "None",
                        "bridgeRuntimeFailures": 0,
                    },
                },
                module._observed_target_evidence(
                    config,
                    module.TerminalMarker("PASS", "PHASE184G_CASE_PASS", {}),
                ),
            )

            log_path.write_text(
                "PHASE184G_PROFILE_EVIDENCE "
                f"case={case} token=p184g_Stale00000000 "
                "source=Foxglove targets=Foxglove "
                "publishEncoding=json subscribeEncoding=json\n",
                encoding="utf-8",
            )
            with self.assertRaisesRegex(module.AcceptanceFailure, "FAIL_TERMINAL"):
                module._observed_profile_evidence(config)
    def test_degraded_summary_consumes_exact_unity_target_status_fields(self):
        """The parent must carry the runtime status marker instead of recreating it."""

        module = load_module()
        token = "p184g_A1b2C3d4E5f6"
        run_id = "phase184g-20260726-degraded01"
        output = ROOT / "build" / "phase184" / "acceptance" / run_id
        config = module.make_run_config(
            repository=ROOT,
            run_id=run_id,
            token=token,
            case="degraded-target",
            profile="jazzy-fastrtps",
            output_root=output,
            domain_id=84,
            foxglove_port=18765,
            bridge_port=18767,
            phase181_workspace=(
                ROOT
                / "build"
                / "phase181"
                / "jazzy-fastrtps"
                / "peer-workspace"
            ),
            interface_package="unity2foxglove_foxrun_interfaces_v1",
            interface_type=(
                "unity2foxglove_foxrun_interfaces_v1"
                "/msg/Phase181State48D288ED82F1Envelope"
            ),
            interface_digest="a" * 64,
        )
        topic = str(config["topics"][0])
        results = {
            "foxglove-client": {
                "verdict": "PASS",
                "evidence": {
                    "deliveryObserved": True,
                    "channelEncodings": ["protobuf"],
                    "sampleToken": module.protocol.token_sha256(token),
                    "sampleStages": ["degraded-local"],
                    "timestamp": 1.0,
                },
            },
            "graph-observer": {
                "verdict": "PASS",
                "evidence": {
                    "endpointsObserved": True,
                    "nodeIdentities": [],
                    "publisherGids": [],
                    "publishersByTopic": {topic: []},
                    "negativeObservationSeconds": 3.0,
                    "noFallbackPublisher": True,
                    "transportObservedQos": {},
                    "qosMatches": False,
                },
            },
        }
        process_codes = {
            "unity": 0,
            "foxglove-client": 0,
            "graph-observer": 0,
        }
        cleanup = {
            "processes": True,
            "files": True,
            "junctions": True,
            "subst": True,
        }
        runtime_fields = {
            "status": "Degraded",
            "succeeded": "Foxglove",
            "failed": "Ros2Bridge",
            "foxgloveState": "Ready",
            "ros2BridgeState": "Unavailable",
            "bridgeDiagnostics": "1",
        }
        self._write_observed_runtime_markers(config)

        summary = module.build_pass_summary(
            config=config,
            terminal=module.TerminalMarker(
                "PASS",
                "PHASE184G_CASE_PASS",
                runtime_fields,
            ),
            results=results,
            process_exit_codes=process_codes,
            unity_version="6000.3.14f1",
            cleanup=cleanup,
        )

        self.assertEqual(
            {
                "foxglove": runtime_fields["foxgloveState"],
                "ros2Bridge": runtime_fields["ros2BridgeState"],
            },
            summary["targets"]["states"],
        )
        self.assertEqual(
            {
                "aggregate": runtime_fields["status"],
                "succeeded": runtime_fields["succeeded"],
                "failed": runtime_fields["failed"],
                "bridgeDiagnostics": 1,
            },
            summary["targets"]["statusEvidence"],
        )

        for field, value in (
            ("status", "Ready"),
            ("succeeded", "Ros2Bridge"),
            ("failed", "Foxglove"),
            ("foxgloveState", "Degraded"),
            ("ros2BridgeState", "Ready"),
            ("bridgeDiagnostics", "0"),
        ):
            with self.subTest(field=field):
                invalid_fields = dict(runtime_fields)
                invalid_fields[field] = value
                with self.assertRaisesRegex(
                    module.AcceptanceFailure,
                    "FAIL_FANOUT",
                ):
                    module.build_pass_summary(
                        config=config,
                        terminal=module.TerminalMarker(
                            "PASS",
                            "PHASE184G_CASE_PASS",
                            invalid_fields,
                        ),
                        results=results,
                        process_exit_codes=process_codes,
                        unity_version="6000.3.14f1",
                        cleanup=cleanup,
                    )
    def test_stream_peer_evidence_must_match_unity_and_nominal_rate(self):
        """Verify stream peer evidence must match unity and nominal rate."""

        module = load_module()
        terminal = module.TerminalMarker(
            "PASS",
            "PHASE184G_CASE_PASS",
            {
                "received": "792",
                "accepted": "792",
                "drained": "625",
                "replaced": "167",
                "rateDropped": "0",
                "highWater": "32",
                "disposalFailures": "0",
                "lastSequence": "1279",
                "ordered": "True",
                "ownershipBalanced": "True",
            },
        )
        evidence = module._validated_stream_evidence(
            terminal,
            {
                "offered": 1280,
                "nominalHz": 640,
                "productionElapsedSeconds": 2.0,
            },
        )
        self.assertEqual(1280, evidence["offered"])
        self.assertEqual(792, evidence["received"])
        self.assertEqual(488, evidence["transportDropped"])
        self.assertEqual(488, evidence["dropped"])
        self.assertEqual(1279, evidence["lastSequence"])

        with self.assertRaisesRegex(module.AcceptanceFailure, "FAIL_STREAM"):
            module._validated_stream_evidence(
                terminal,
                {
                    "offered": 1279,
                    "nominalHz": 640,
                    "productionElapsedSeconds": 2.0,
                },
            )
        too_many_received = module.TerminalMarker(
            terminal.verdict,
            terminal.line,
            dict(terminal.fields, received="1281"),
        )
        with self.assertRaisesRegex(module.AcceptanceFailure, "FAIL_STREAM"):
            module._validated_stream_evidence(
                too_many_received,
                {
                    "offered": 1280,
                    "nominalHz": 640,
                    "productionElapsedSeconds": 2.0,
                },
            )
        with self.assertRaisesRegex(module.AcceptanceFailure, "FAIL_STREAM"):
            module._validated_stream_evidence(
                terminal,
                {
                    "offered": 1280,
                    "nominalHz": 640,
                    "productionElapsedSeconds": 4.0,
                },
            )
    def test_foxglove_worker_persists_unexpected_failure(self):
        """Verify foxglove worker persists unexpected failure."""

        module = load_module()
        config = {"case": "foxglove-profile"}

        async def fail_unexpectedly(_config):
            """Handle the fail unexpectedly step."""

            raise ValueError("boom")

        with mock.patch.object(
            module,
            "_run_foxglove_client_async",
            side_effect=fail_unexpectedly,
        ):
            with mock.patch.object(module, "write_actor_result") as write_result:
                self.assertEqual(1, module.run_foxglove_client_worker(config))
        self.assertEqual("FAIL_CLIENT", write_result.call_args.kwargs["verdict"])
        self.assertEqual(
            {"diagnostic": "ValueError"},
            write_result.call_args.kwargs["evidence"],
        )
    def test_main_dispatches_manual_mode_without_launching_batch_parent(self):
        """Verify main dispatches manual mode without launching batch parent."""

        module = load_module()
        with mock.patch.object(module, "run_manual_parent", return_value=0) as manual:
            with mock.patch.object(module, "run_batch_parent") as batch:
                result = module.main(
                    [
                        "--case",
                        "multi-target",
                        "--profile",
                        "jazzy-fastrtps",
                        "--manual-editor",
                        "--unity-editor",
                        r"C:\Unity.exe",
                    ]
                )
        self.assertEqual(0, result)
        manual.assert_called_once()
        batch.assert_not_called()
class Phase184ProfileAcceptanceOrchestratorTests(_Phase184ProfileAcceptanceOrchestratorTests_support, _Phase184ProfileAcceptanceOrchestratorTests_fixtures, _Phase184ProfileAcceptanceOrchestratorTests_validation, _Phase184ProfileAcceptanceOrchestratorTests_runtime, _Phase184ProfileAcceptanceOrchestratorTests_cleanup, _Phase184ProfileAcceptanceOrchestratorTests_edge_cases, _Phase184ProfileAcceptanceOrchestratorTests_additional, _Phase184ProfileAcceptanceOrchestratorTests_extension_8, _Phase184ProfileAcceptanceOrchestratorTests_extension_9, _Phase184ProfileAcceptanceOrchestratorTests_extension_10, unittest.TestCase):
    """Lock command, ownership, configuration, and evidence boundaries."""
    pass


__all__ = [name for name in globals() if not name.startswith("__")]
