from __future__ import annotations
from .class_phase181_custom_ros2_peer_tests_validation import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "test_phase181_custom_ros2_peer.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
class _Phase181CustomRos2PeerTests_runtime:
    def test_streamed_owned_command_tees_live_progress_to_the_console_and_log(self):
        """Verify Phase181 behavior: cold builds visibly stream their bounded helper output."""
        peer = load_peer_module()
        calls: list[tuple[list[str], dict[str, object]]] = []

        class Process:
            """Stalled streamed-build process double."""
            """Minimal owned process with two colcon progress lines."""

            def __init__(self):
                """Seed the deterministic streamed build output."""

                self.stdout = io.StringIO("Starting >>> example_interfaces\nFinished <<< example_interfaces\n")

            def wait(self, timeout):
                """Remain stalled when the bounded wait expires."""
                """Record the supplied timeout and complete successfully."""

                self.timeout = timeout
                return 0

            def kill(self):
                """Reject termination because the synthetic helper completed successfully."""

                raise AssertionError("A successful streamed process must not be killed.")

        def process_factory(command, **kwargs):
            """Capture the strict Phase181 subprocess construction."""
            calls.append((list(command), kwargs))
            return Process()

        with temporary_directory("peer-") as temporary:
            root = pathlib.Path(temporary)
            console = io.StringIO()
            with contextlib.redirect_stdout(console):
                peer.run_logged_owned_command(
                    ["colcon.exe", "build"],
                    cwd=root,
                    env={"PATH": "safe"},
                    log_path=root / "colcon.log",
                    timeout_seconds=5.0,
                    failure_code="FAIL_PEER_BUILD",
                    stream_output=True,
                    output_prefix="[phase181:test][build] ",
                    process_factory=process_factory,
                )

            self.assertIn("Starting >>> example_interfaces", console.getvalue())
            self.assertIn("Finished <<< example_interfaces", console.getvalue())
            self.assertEqual(
                "Starting >>> example_interfaces\nFinished <<< example_interfaces\n",
                (root / "colcon.log").read_text(encoding="utf-8"),
            )

        self.assertFalse(calls[0][1]["shell"])
        self.assertEqual({"PATH": "safe"}, calls[0][1]["env"])
        self.assertEqual(subprocess.PIPE, calls[0][1]["stdout"])
        self.assertTrue(set(peer.worker_launch_options()).issubset(calls[0][1]))
    def test_streamed_timeout_terminates_owned_process_tree(self):
        """A stalled streamed build must use the owned tree terminator, not root-only kill."""
        peer = load_peer_module()

        class Process:
            """Stalled streamed-build process double."""
            pid = 4242
            stdout = io.StringIO("")
            returncode = None

            def wait(self, timeout):
                """Raise a bounded wait timeout."""
                raise subprocess.TimeoutExpired(["colcon.exe"], timeout)

        with temporary_directory("peer-") as temporary:
            root = pathlib.Path(temporary)
            with mock.patch.object(peer, "stream_output_is_stalled", return_value=True), mock.patch.object(
                peer, "_terminate_owned_child"
            ) as terminate:
                with self.assertRaisesRegex(peer.PeerFailure, "FAIL_PEER_BUILD"):
                    peer.run_logged_owned_command(
                        ["colcon.exe", "build"],
                        cwd=root,
                        env={"PATH": "safe"},
                        log_path=root / "colcon.log",
                        timeout_seconds=1.0,
                        failure_code="FAIL_PEER_BUILD",
                        stream_output=True,
                        process_factory=lambda *args, **kwargs: Process(),
                    )
            terminate.assert_called_once()
    def test_worker_ready_file_requires_the_locked_full_interface_digest(self):
        """Verify Phase181 behavior: manual Play prompts cannot follow a stale or foreign worker startup file."""
        peer = load_peer_module()
        lock = peer.StaticInterfaceLock("example_interfaces", 1, "a" * 64, "State", "Envelope")
        with temporary_directory("peer-") as temporary:
            ready_path = pathlib.Path(temporary) / "worker-ready.json"
            peer.write_worker_ready(ready_path, lock)
            peer.require_matching_worker_ready(ready_path, lock)

            ready_path.write_text(
                json.dumps({"phase": 181, "ready": True, "interfaceDigest": "b" * 64}),
                encoding="utf-8",
            )
            with self.assertRaisesRegex(peer.PeerFailure, "FAIL_WORKER_READY"):
                peer.require_matching_worker_ready(ready_path, lock)
    def test_failure_log_archive_retains_only_named_owned_diagnostic(self):
        """Verify Phase181 behavior: cleanup may retain a bounded profile diagnostic for failures."""
        peer = load_peer_module()
        with temporary_directory("peer-") as temporary:
            root = pathlib.Path(temporary)
            workspace = root / "peer-workspace"
            output = root / "profile-output"
            workspace.mkdir()
            (workspace / "colcon-build.log").write_text("bounded build failure\n", encoding="utf-8")

            peer.preserve_failure_log(workspace, output, "colcon-build.log", "peer-build-failure.log")

            self.assertEqual(
                "bounded build failure\n",
                (output / "peer-build-failure.log").read_text(encoding="utf-8"),
            )
            self.assertFalse((output / "unexpected.log").exists())
    def test_failure_log_archive_retains_redacted_worker_result(self):
        """Verify Phase181 behavior: failed peers preserve their bounded result evidence."""
        peer = load_peer_module()
        with temporary_directory("peer-") as temporary:
            root = pathlib.Path(temporary)
            workspace = root / "peer-workspace"
            output = root / "profile-output"
            workspace.mkdir()
            expected = '{"verdict":"FAIL_GRAPH_EVIDENCE","token":"redacted"}\n'
            (workspace / "worker-result.json").write_text(expected, encoding="utf-8")

            peer.preserve_failure_log(
                workspace,
                output,
                "worker-result.json",
                "peer-worker-result-failure.json",
            )

            self.assertEqual(
                expected,
                (output / "peer-worker-result-failure.json").read_text(encoding="utf-8"),
            )
    def test_selected_typesupport_requires_exactly_one_matching_runtime_and_addon(self):
        """Verify Phase181 behavior: selected typesupport requires exactly one matching runtime and addon."""
        peer = load_peer_module()
        with temporary_directory("peer-") as temporary:
            root = pathlib.Path(temporary)
            manifest = root / "Unity2Foxglove" / "Packages" / "manifest.json"
            manifest.parent.mkdir(parents=True)
            manifest.write_text(
                json.dumps(
                    {
                        "dependencies": {
                            "dev.unity2foxglove.ros2forunity.runtime.lyrical.win64": "file:../../Packages/runtime",
                            "dev.unity2foxglove.foxrun.ros2.interfaces.typesupport.lyrical.win64": "file:../../Packages/addon",
                        }
                    }
                ),
                encoding="utf-8",
            )

            self.assertEqual(
                "dev.unity2foxglove.foxrun.ros2.interfaces.typesupport.lyrical.win64",
                peer.require_selected_typesupport_addon(root, "lyrical"),
            )

            manifest.write_text(
                json.dumps(
                    {
                        "dependencies": {
                            "dev.unity2foxglove.ros2forunity.runtime.lyrical.win64": "file:../../Packages/runtime",
                            "dev.unity2foxglove.foxrun.ros2.interfaces.typesupport.jazzy.win64": "file:../../Packages/wrong",
                        }
                    }
                ),
                encoding="utf-8",
            )
            with self.assertRaisesRegex(peer.PeerFailure, "FAIL_TYPESUPPORT_SELECTION"):
                peer.require_selected_typesupport_addon(root, "lyrical")
    def test_graph_endpoint_evidence_requires_matching_type_external_owner_and_reliability(self):
        """Verify Phase181 behavior: graph endpoint evidence requires matching type external owner and reliability."""
        peer = load_peer_module()

        class Qos:
            """Provide a lightweight Phase181 test double for Qos."""
            reliability = 1

        class Endpoint:
            """Provide a lightweight Phase181 test double for Endpoint."""
            topic_type = "example/msg/Envelope"
            node_name = "unity"
            qos_profile = Qos()

        self.assertTrue(
            peer.external_endpoint_has_reliability(
                [Endpoint()],
                "phase181_peer",
                "example/msg/Envelope",
                1,
            )
        )
        self.assertFalse(
            peer.external_endpoint_has_reliability(
                [Endpoint()],
                "unity",
                "example/msg/Envelope",
                1,
            )
        )
        self.assertFalse(
            peer.external_endpoint_has_reliability(
                [Endpoint()],
                "phase181_peer",
                "example/msg/Envelope",
                2,
            )
        )
    def test_graph_evidence_reports_each_required_direction_without_endpoint_identity(self):
        """Verify Phase181 behavior: graph failures retain bounded per-direction evidence."""
        peer = load_peer_module()

        class Qos:
            """Provide a lightweight Phase181 test double for Qos."""
            reliability = 1

        class Endpoint:
            """Provide a lightweight Phase181 test double for Endpoint."""
            topic_type = "example/msg/Envelope"
            node_name = "unity"
            qos_profile = Qos()

        checks = peer.evaluate_graph_evidence(
            [Endpoint()],
            [Endpoint()],
            [Endpoint()],
            [Endpoint()],
            "phase181_peer",
            "example/msg/Envelope",
            1,
            True,
            True,
        )

        self.assertEqual(
            {
                "subscribeReliable": True,
                "publishPublisher": True,
                "bidirectionalPublisher": True,
                "bidirectionalReliable": True,
            },
            checks,
        )
    def test_graph_endpoint_summary_distinguishes_type_mismatch_from_reliability_mismatch(self):
        """Verify Phase181 behavior: diagnostic endpoint counts expose no identities or raw endpoint data."""
        peer = load_peer_module()

        class Qos:
            """Provide a lightweight Phase181 test double for Qos."""
            def __init__(self, reliability):
                """Store the synthetic reliability value used by this test."""
                self.reliability = reliability

        class Endpoint:
            """Provide a lightweight Phase181 test double for Endpoint."""
            def __init__(self, topic_type, node_name, reliability):
                """Store the synthetic graph endpoint fields used by this test."""
                self.topic_type = topic_type
                self.node_name = node_name
                self.qos_profile = Qos(reliability)

        summary = peer.summarize_graph_endpoints(
            [
                Endpoint("wrong/msg/Envelope", "unity", 1),
                Endpoint("example/msg/Envelope", "phase181_peer", 1),
                Endpoint("example/msg/Envelope", "unity", 2),
            ],
            "phase181_peer",
            "example/msg/Envelope",
            1,
        )

        self.assertEqual(
            {
                "total": 3,
                "matchingType": 2,
                "externalMatchingType": 1,
                "externalMatchingReliable": 0,
            },
            summary,
        )
    def test_graph_observation_survives_unity_endpoint_teardown_in_the_same_run(self):
        """Verify Phase181 behavior: a post-stop graph query cannot erase live endpoint evidence."""
        peer = load_peer_module()

        observed = peer.merge_graph_observations(
            {},
            {
                "subscribeReliable": True,
                "publishPublisher": True,
                "bidirectionalPublisher": True,
                "bidirectionalReliable": True,
            },
        )
        after_unity_exit = peer.merge_graph_observations(
            observed,
            {
                "subscribeReliable": False,
                "publishPublisher": False,
                "bidirectionalPublisher": False,
                "bidirectionalReliable": False,
            },
        )

        self.assertEqual(observed, after_unity_exit)
    def test_peer_never_mistakes_its_remote_final_origin_for_a_unity_echo(self):
        """Verify Phase181 behavior: peer never mistakes its remote final origin for a unity echo."""
        peer = load_peer_module()
        token = "phase181-test"

        self.assertTrue(peer.is_peer_remote_origin("remote-" + token, token))
        self.assertTrue(peer.is_peer_remote_origin("remote-final-" + token, token))
        self.assertFalse(peer.is_peer_remote_origin("unity-origin-123", token))
    def test_typed_worker_endpoint_setup_disposes_a_partially_created_node_on_failure(self):
        """Verify Phase181 behavior: typed worker endpoint setup disposes a partially created node on failure."""
        peer = load_peer_module()

        class FakeNode:
            """Provide a lightweight Phase181 test double for FakeNode."""
            def __init__(self):
                """Initialize the lightweight Phase181 test double."""
                self.destroy_calls = 0

            def create_subscription(self, *args):
                """Implement the Phase181 create subscription step."""
                raise RuntimeError("subscription setup failed")

            def destroy_node(self):
                """Implement the Phase181 destroy node step."""
                self.destroy_calls += 1

        class FakeRclpy:
            """Provide a lightweight Phase181 test double for FakeRclpy."""
            def __init__(self, node):
                """Initialize the lightweight Phase181 test double."""
                self.node = node

            def create_node(self, name):
                """Implement the Phase181 create node step."""
                self.name = name
                return self.node

        node = FakeNode()
        with self.assertRaisesRegex(peer.PeerFailure, "FAIL_PEER_RUNTIME"):
            peer.create_typed_worker_endpoints(
                FakeRclpy(node),
                object(),
                "phase181-worker",
                object(),
                "/phase181/publish",
                "/phase181/subscribe",
                "/phase181/bidirectional",
            )

        self.assertEqual(1, node.destroy_calls)
    def test_post_stop_observer_rejects_a_late_correlated_unity_apply_marker(self):
        """Verify Phase181 behavior: post stop observer rejects a late correlated unity apply marker."""
        peer = load_peer_module()
        with temporary_directory("peer-") as temporary:
            log = pathlib.Path(temporary) / "unity.log"
            log.write_text("before stop\n", encoding="utf-8")
            offset = peer.protocol.log_offset(log)
            with log.open("a", encoding="utf-8") as stream:
                stream.write(
                    "PHASE181_CUSTOM_ROS2_APPLIED token=phase181-stop-check "
                    "topic=/unity2foxglove/phase181/custom/subscribe\n"
                )

            clean_stop, end_offset = peer.observe_no_late_unity_apply(
                log,
                offset,
                "phase181-stop-check",
                observation_seconds=0.0,
            )

        self.assertFalse(clean_stop)
        self.assertGreater(end_offset, offset)
class _Phase181CustomRos2PeerTests_cleanup:
    def test_worker_main_writes_a_bounded_result_for_an_unhandled_runtime_setup_error(self):
        """Verify Phase181 behavior: worker main writes a bounded result for an unhandled runtime setup error."""
        peer = load_peer_module()
        with temporary_directory("peer-") as temporary:
            result_path = pathlib.Path(temporary) / "worker-result.json"
            args = SimpleNamespace(worker_result_json=result_path)
            original = peer.run_typed_worker
            try:
                def fail_runtime(_):
                    """Implement the Phase181 fail runtime step."""
                    raise RuntimeError("unbounded native setup detail")

                peer.run_typed_worker = fail_runtime
                with contextlib.redirect_stderr(io.StringIO()):
                    self.assertEqual(1, peer.worker_main(args))
            finally:
                peer.run_typed_worker = original

            result = json.loads(result_path.read_text(encoding="utf-8"))
            self.assertEqual("FAIL_PEER_RUNTIME", result["verdict"])
            self.assertEqual("redacted", result["error"])
class Phase181CustomRos2PeerTests(_Phase181CustomRos2PeerTests_support, _Phase181CustomRos2PeerTests_fixtures, _Phase181CustomRos2PeerTests_validation, _Phase181CustomRos2PeerTests_runtime, _Phase181CustomRos2PeerTests_cleanup, unittest.TestCase):
    """Verify peer setup cannot turn a partial observation into a PASS."""
    pass


__all__ = [name for name in globals() if not name.startswith("__")]
