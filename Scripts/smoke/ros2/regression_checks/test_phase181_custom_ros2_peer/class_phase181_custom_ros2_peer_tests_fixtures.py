from __future__ import annotations
from .foundation import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "test_phase181_custom_ros2_peer.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
class _Phase181CustomRos2PeerTests_fixtures:
    """Decomposed Phase192 implementation component."""
    def test_one_sided_graph_or_inspector_evidence_cannot_pass(self):
        """Verify Phase181 behavior: one sided graph or inspector evidence cannot pass."""
        peer = load_peer_module()
        graph_only = peer.classify_evidence(
            {
                "interfaceDigestMatches": True,
                "graphEvidence": True,
                "outboundObserved": True,
                "inboundApplied": False,
                "sameOriginDropped": False,
                "remoteOriginApplied": False,
                "unityTerminalPass": False,
                "cleanStop": False,
            }
        )
        inspector_only = peer.classify_evidence(
            {
                "interfaceDigestMatches": True,
                "graphEvidence": False,
                "outboundObserved": False,
                "inboundApplied": True,
                "sameOriginDropped": True,
                "remoteOriginApplied": True,
                "nullableEmptyObserved": True,
                "unityTerminalPass": True,
                "cleanStop": True,
            }
        )

        self.assertEqual("FAIL_REMOTE_APPLY", graph_only)
        self.assertEqual("FAIL_GRAPH_EVIDENCE", inspector_only)
    def test_full_evidence_requires_native_both_direction_and_origin_proof(self):
        """Verify Phase181 behavior: full evidence requires native both direction and origin proof."""
        peer = load_peer_module()
        verdict = peer.classify_evidence(
            {
                "interfaceDigestMatches": True,
                "graphEvidence": True,
                "outboundObserved": True,
                "inboundApplied": True,
                "sameOriginDropped": True,
                "remoteOriginApplied": True,
                "nullableEmptyObserved": True,
                "unityTerminalPass": True,
                "cleanStop": True,
            }
        )

        self.assertEqual("PASS", verdict)
    def test_live_completion_does_not_wait_for_the_finally_only_clean_stop_flag(self):
        """Verify Phase181 behavior: live completion does not wait for the finally only clean stop flag."""
        peer = load_peer_module()
        live_evidence = {
            "interfaceDigestMatches": True,
            "graphEvidence": True,
            "outboundObserved": True,
            "inboundApplied": True,
            "sameOriginDropped": True,
            "remoteOriginApplied": True,
            "nullableEmptyObserved": True,
            "unityTerminalPass": True,
            "cleanStop": False,
        }

        self.assertEqual("FAIL_CLEAN_STOP", peer.classify_evidence(live_evidence))
        self.assertTrue(peer.can_complete_live_evidence(live_evidence))
    def test_linux_peer_roles_require_only_the_directional_proof_they_own(self):
        """Verify Phase181 behavior: linux peer roles require only the directional proof they own."""
        peer = load_peer_module()
        base = {
            "interfaceDigestMatches": True,
            "graphEvidence": True,
            "outboundObserved": False,
            "inboundApplied": True,
            "sameOriginDropped": False,
            "remoteOriginApplied": False,
            "nullableEmptyObserved": False,
            "unityTerminalPass": True,
            "cleanStop": True,
        }

        self.assertEqual("PASS", peer.classify_evidence(base, "publisher"))
        self.assertEqual("FAIL_OUTBOUND_EVIDENCE", peer.classify_evidence(base, "subscriber"))
        self.assertEqual("FAIL_SAME_ORIGIN", peer.classify_evidence(base, "bidirectional"))
        self.assertEqual("FAIL_OUTBOUND_EVIDENCE", peer.classify_evidence(base, "orchestrate"))
        self.assertEqual("PASS", peer.classify_evidence(base, "correlate"))
    def test_worker_result_json_never_persists_the_correlation_token(self):
        """Verify Phase181 behavior: worker result json never persists the correlation token."""
        peer = load_peer_module()
        with temporary_directory("peer-") as temporary:
            destination = pathlib.Path(temporary) / "worker-result.json"
            peer.write_worker_result(
                destination,
                {"token": "private-correlation", "verdict": "PASS", "error": "token=private-correlation"},
            )
            text = destination.read_text(encoding="utf-8")
            result = json.loads(text)

        self.assertNotIn("private-correlation", text)
        self.assertEqual("redacted", result["token"])
        self.assertEqual("redacted", result["error"])
    def test_probe_payloads_preserve_correlated_and_nullable_empty_cases(self):
        """Verify Phase181 behavior: probe payloads preserve correlated and nullable empty cases."""
        peer = load_peer_module()

        correlated = peer.custom_payload_fields("opaque-local-token", null_empty=False)
        nullable_empty = peer.custom_payload_fields("opaque-local-token", null_empty=True)

        self.assertEqual("opaque-local-token", correlated["message"])
        self.assertTrue(correlated["has_nested"])
        self.assertEqual([181, 182, 183], correlated["values"])
        self.assertEqual(697348082, nullable_empty["count"])
        self.assertEqual("", nullable_empty["message"])
        self.assertTrue(nullable_empty["has_message"])
        self.assertEqual([], nullable_empty["bytes"])
        self.assertEqual([], nullable_empty["values"])
        self.assertFalse(nullable_empty["has_nested"])
        self.assertFalse(nullable_empty["has_optional_count"])
        self.assertFalse(nullable_empty["has_optional_text"])
    def test_initial_bidirectional_probe_retries_after_graph_readiness_until_unity_applies(self):
        """Verify Phase181 behavior: a volatile first P&S sample is not a one-shot discovery race."""
        peer = load_peer_module()

        self.assertFalse(
            peer.should_publish_initial_bidirectional_probe(
                requires_bidirectional=True,
                inbound_applied=False,
                graph_evidence=True,
                origin_probe_ready=True,
                initial_remote_applied=False,
                now=10.0,
                next_publish_time=None,
            )
        )
        self.assertFalse(
            peer.should_publish_initial_bidirectional_probe(
                requires_bidirectional=True,
                inbound_applied=True,
                graph_evidence=False,
                origin_probe_ready=True,
                initial_remote_applied=False,
                now=10.0,
                next_publish_time=None,
            )
        )
        self.assertFalse(
            peer.should_publish_initial_bidirectional_probe(
                requires_bidirectional=True,
                inbound_applied=True,
                graph_evidence=True,
                origin_probe_ready=False,
                initial_remote_applied=False,
                now=10.0,
                next_publish_time=None,
            )
        )
        self.assertTrue(
            peer.should_publish_initial_bidirectional_probe(
                requires_bidirectional=True,
                inbound_applied=True,
                graph_evidence=True,
                origin_probe_ready=True,
                initial_remote_applied=False,
                now=10.0,
                next_publish_time=None,
            )
        )
        self.assertFalse(
            peer.should_publish_initial_bidirectional_probe(
                requires_bidirectional=True,
                inbound_applied=True,
                graph_evidence=True,
                origin_probe_ready=True,
                initial_remote_applied=False,
                now=10.5,
                next_publish_time=10.75,
            )
        )
        self.assertTrue(
            peer.should_publish_initial_bidirectional_probe(
                requires_bidirectional=True,
                inbound_applied=True,
                graph_evidence=True,
                origin_probe_ready=True,
                initial_remote_applied=False,
                now=10.75,
                next_publish_time=10.75,
            )
        )
        self.assertFalse(
            peer.should_publish_initial_bidirectional_probe(
                requires_bidirectional=True,
                inbound_applied=True,
                graph_evidence=True,
                origin_probe_ready=True,
                initial_remote_applied=True,
                now=11.0,
                next_publish_time=10.75,
            )
        )
    def test_same_origin_probe_reuses_a_pre_remote_unity_envelope(self):
        """Verify Phase181 behavior: origin proof does not wait for a correctly suppressed remote echo."""
        peer = load_peer_module()
        token = "opaque-local-token"
        initial_local = peer.custom_payload_fields(token, null_empty=True)
        foreign_local = peer.custom_payload_fields("foreign-token", null_empty=True)
        correlated_remote = peer.custom_payload_fields(token, null_empty=False)

        self.assertTrue(peer.is_unity_origin_probe("unity-origin", initial_local, token))
        self.assertNotEqual(initial_local["count"], foreign_local["count"])
        self.assertFalse(peer.is_unity_origin_probe("unity-origin", foreign_local, token))
        self.assertFalse(peer.is_unity_origin_probe("", initial_local, token))
        self.assertFalse(peer.is_unity_origin_probe("remote-" + token, initial_local, token))
        self.assertFalse(peer.is_unity_origin_probe("remote-final-" + token, initial_local, token))
        self.assertFalse(peer.is_unity_origin_probe("unity-origin", correlated_remote, token))
    def test_bidirectional_apply_count_requires_the_exact_token_and_topic(self):
        """Verify Phase181 behavior: nullable proof comes only from Unity's second correlated apply."""
        peer = load_peer_module()
        token = "opaque-local-token"
        topic = peer.DEFAULT_TOPICS["bidirectional"]
        markers = [
            peer.protocol.UnityMarker(
                "PHASE181_CUSTOM_ROS2_APPLIED",
                {"token": token, "topic": topic, "applied": "1"},
                "first",
            ),
            peer.protocol.UnityMarker(
                "PHASE181_CUSTOM_ROS2_APPLIED",
                {"token": token, "topic": topic, "applied": "2"},
                "second",
            ),
            peer.protocol.UnityMarker(
                "PHASE181_CUSTOM_ROS2_APPLIED",
                {"token": "foreign-token", "topic": topic, "applied": "3"},
                "foreign-token",
            ),
            peer.protocol.UnityMarker(
                "PHASE181_CUSTOM_ROS2_APPLIED",
                {"token": token, "topic": peer.DEFAULT_TOPICS["subscribe"], "applied": "4"},
                "foreign-topic",
            ),
        ]

        self.assertEqual(2, peer.count_bidirectional_apply_markers(markers, token))
    def test_final_bidirectional_probe_retries_until_unity_reports_the_remote_apply(self):
        """Verify Phase181 behavior: the final nullable probe is retried after the same-origin replay barrier."""
        peer = load_peer_module()

        self.assertFalse(
            peer.should_publish_final_bidirectional_probe(
                requires_bidirectional=True,
                same_origin_dropped=False,
                remote_origin_applied=False,
                now=10.0,
                next_publish_time=None,
            )
        )
        self.assertTrue(
            peer.should_publish_final_bidirectional_probe(
                requires_bidirectional=True,
                same_origin_dropped=True,
                remote_origin_applied=False,
                now=10.0,
                next_publish_time=None,
            )
        )
        self.assertFalse(
            peer.should_publish_final_bidirectional_probe(
                requires_bidirectional=True,
                same_origin_dropped=True,
                remote_origin_applied=False,
                now=10.5,
                next_publish_time=10.75,
            )
        )
        self.assertTrue(
            peer.should_publish_final_bidirectional_probe(
                requires_bidirectional=True,
                same_origin_dropped=True,
                remote_origin_applied=False,
                now=10.75,
                next_publish_time=10.75,
            )
        )
        self.assertFalse(
            peer.should_publish_final_bidirectional_probe(
                requires_bidirectional=True,
                same_origin_dropped=True,
                remote_origin_applied=True,
                now=11.0,
                next_publish_time=10.75,
            )
        )
    def test_owned_workspace_refuses_unmarked_or_outside_build_paths(self):
        """Verify Phase181 behavior: owned workspace refuses unmarked or outside build paths."""
        peer = load_peer_module()
        with temporary_directory("peer-") as temporary:
            root = pathlib.Path(temporary)
            build_root = root / "build" / "phase181"
            workspace = peer.prepare_owned_workspace(build_root, "jazzy-fastrtps")
            self.assertEqual(build_root / "jazzy-fastrtps" / "peer-workspace", workspace)
            self.assertTrue((workspace / peer.OWNERSHIP_MARKER_NAME).is_file())

            unsafe = build_root / "outside"
            unsafe.mkdir(parents=True)
            with self.assertRaisesRegex(peer.PeerFailure, "FAIL_PEER_WORKSPACE"):
                peer.cleanup_owned_workspace(unsafe, build_root)
    def test_peer_build_workspace_reuses_only_a_sealed_matching_install(self):
        """Verify Phase181 behavior: peer reuse requires an exact owned build seal and install outputs."""
        peer = load_peer_module()
        with temporary_directory("peer-") as temporary:
            root = pathlib.Path(temporary)
            build_root = root / "build" / "phase181"
            workspace, reused = peer.prepare_peer_build_workspace(
                build_root,
                "lyrical-fastrtps",
                "a" * 64,
                "example_interfaces",
            )
            self.assertFalse(reused)
            (workspace / "install" / "share" / "example_interfaces").mkdir(parents=True)
            (workspace / "install" / "local_setup.bat").write_text("@echo off\n", encoding="utf-8")
            (workspace / "install" / "share" / "example_interfaces" / "package.xml").write_text(
                "<package/>\n",
                encoding="utf-8",
            )
            peer.seal_peer_build_workspace(workspace, "a" * 64, "example_interfaces")

            reused_workspace, reused = peer.prepare_peer_build_workspace(
                build_root,
                "lyrical-fastrtps",
                "a" * 64,
                "example_interfaces",
            )
            self.assertEqual(workspace, reused_workspace)
            self.assertTrue(reused)

            rebuilt_workspace, reused = peer.prepare_peer_build_workspace(
                build_root,
                "lyrical-fastrtps",
                "b" * 64,
                "example_interfaces",
            )
            self.assertEqual(workspace, rebuilt_workspace)
            self.assertFalse(reused)
            self.assertFalse((rebuilt_workspace / "install" / "local_setup.bat").exists())
    def test_peer_build_cache_key_binds_lock_profile_runtime_and_toolchain(self):
        """Verify Phase181 behavior: a peer build cache key cannot cross locked runtime inputs."""
        peer = load_peer_module()
        toolchain = peer.WindowsPeerToolchain(
            pathlib.Path("C:/ros2/lyrical"),
            pathlib.Path("C:/ros2/lyrical/python.exe"),
            pathlib.Path("C:/ros2/lyrical/colcon.exe"),
        )
        lock = peer.StaticInterfaceLock("example_interfaces", 1, "a" * 64, "State", "Envelope")
        command = ["C:/ros2/lyrical/colcon.exe", "build", "--merge-install"]

        key = peer.peer_build_cache_key(lock, "lyrical-fastrtps", "lyrical", "rmw_fastrtps_cpp", toolchain, command)
        changed_digest = peer.peer_build_cache_key(
            peer.StaticInterfaceLock("example_interfaces", 1, "b" * 64, "State", "Envelope"),
            "lyrical-fastrtps",
            "lyrical",
            "rmw_fastrtps_cpp",
            toolchain,
            command,
        )
        changed_runtime = peer.peer_build_cache_key(
            lock,
            "lyrical-zenoh",
            "lyrical",
            "rmw_zenoh_cpp",
            toolchain,
            command,
        )

        self.assertRegex(key, r"^[0-9a-f]{64}$")
        self.assertNotEqual(key, changed_digest)
        self.assertNotEqual(key, changed_runtime)


__all__ = [name for name in globals() if not name.startswith("__")]
