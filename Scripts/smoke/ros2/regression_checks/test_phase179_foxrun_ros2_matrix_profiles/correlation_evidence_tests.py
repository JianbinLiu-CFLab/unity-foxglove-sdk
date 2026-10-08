from __future__ import annotations

from .foundation import *


class Phase179CorrelationEvidenceTests:
    """Exercise pending-exit, correlation, and endpoint evidence rules"""

    def test_linux_pending_exit_is_accepted_only_with_complete_graph_and_publication_evidence(self) -> None:
        """The existing Linux peer's exit 2 is meaningful only for its documented pending half-evidence verdict."""

        profile = self.profiles.PROFILES["humble-fastrtps"]
        summary = self._linux_summary(profile, surface="editor", token="row-token")

        self.profiles.validate_linux_peer_result(2, summary, profile, surface="editor", token="row-token")

        incomplete = json.loads(json.dumps(summary))
        incomplete["messageResults"][1]["published"] = False
        with self.assertRaises(self.profiles.MatrixFailure) as publication_failure:
            self.profiles.validate_linux_peer_result(2, incomplete, profile, surface="editor", token="row-token")
        self.assertEqual("LINUX_PUBLICATION", publication_failure.exception.category)

        with self.assertRaises(self.profiles.MatrixFailure) as exit_failure:
            self.profiles.validate_linux_peer_result(0, summary, profile, surface="editor", token="row-token")
        self.assertEqual("LINUX_EXIT", exit_failure.exception.category)

    def test_player_pending_exit_requires_zero_embedded_player_exit_and_copied_values(self) -> None:
        """The Player host's own pending process exit remains valid only with full Unity-value proof."""

        profile = self.profiles.PROFILES["jazzy-fastrtps"]
        summary = self._player_summary(profile, token="row-token")

        self.profiles.validate_windows_player_result(2, summary, profile, token="row-token")

        incomplete = json.loads(json.dumps(summary))
        incomplete["playerExitCode"] = 7
        with self.assertRaises(self.profiles.MatrixFailure) as exit_failure:
            self.profiles.validate_windows_player_result(2, incomplete, profile, token="row-token")
        self.assertEqual("PLAYER_EXIT", exit_failure.exception.category)

    def test_correlation_requires_both_matching_halves_and_zenoh_topology_identity(self) -> None:
        """A matching Linux publication and Unity copied-value proof are the minimum final interop evidence."""

        profile = self.profiles.PROFILES["lyrical-zenoh"]
        linux = self._linux_summary(
            profile,
            surface="player",
            token="row-token",
            topology_id="phase179-lyrical-zenoh-router-a",
        )
        windows = self._player_summary(
            profile,
            token="row-token",
            topology_id="phase179-lyrical-zenoh-router-a",
        )

        combined = self.profiles.correlate_summaries(profile, "player", linux, windows)

        self.assertEqual("PHASE179_LYRICAL_ZENOH_PLAYER_PASS", combined["verdict"])
        self.assertEqual("phase179-lyrical-zenoh-router-a", combined["zenohTopologyId"])

        mismatched = json.loads(json.dumps(windows))
        mismatched["zenohTopologyId"] = "phase179-other-router"
        with self.assertRaises(self.profiles.MatrixFailure) as topology_failure:
            self.profiles.correlate_summaries(profile, "player", linux, mismatched)
        self.assertEqual("ZENOH_TOPOLOGY", topology_failure.exception.category)

    def test_editor_correlation_requires_each_serialized_copied_value_not_only_a_boolean(self) -> None:
        """A hand-edited or incomplete Editor summary cannot turn preflight readiness into final interoperability proof."""

        profile = self.profiles.PROFILES["humble-fastrtps"]
        linux = self._linux_summary(profile, surface="editor", token="row-token")
        editor = self._editor_summary(profile, token="row-token")

        combined = self.profiles.correlate_summaries(profile, "editor", linux, editor)
        self.assertEqual("PHASE179_HUMBLE_FASTRTPS_EDITOR_PASS", combined["verdict"])

        incomplete = json.loads(json.dumps(editor))
        incomplete["messageResults"] = incomplete["messageResults"][:2]
        with self.assertRaises(self.profiles.MatrixFailure) as value_failure:
            self.profiles.correlate_summaries(profile, "editor", linux, incomplete)
        self.assertEqual("EDITOR_APPLIED", value_failure.exception.category)

    def test_correlation_consumes_an_exact_pair_once(self) -> None:
        """A stale matching pair cannot be replayed as a second final PASS."""
        profile = self.profiles.PROFILES["humble-fastrtps"]
        linux = self._linux_summary(profile, surface="editor", token="row-token")
        editor = self._editor_summary(profile, token="row-token")
        with tempfile.TemporaryDirectory() as temp:
            receipt = Path(temp) / "correlation-consumed.json"
            first = self.profiles.correlate_summaries(
                profile, "editor", linux, editor, consumed_receipts_path=receipt
            )
            self.assertTrue(first["correlationReceipt"])
            with self.assertRaises(self.profiles.MatrixFailure) as replay:
                self.profiles.correlate_summaries(
                    profile, "editor", linux, editor, consumed_receipts_path=receipt
                )
            self.assertEqual("REPLAY", replay.exception.category)

    def test_windows_editor_root_uses_repo_local_ros_python_rclpy_endpoint_evidence(self) -> None:
        """Editor preflight queries all three endpoint contracts through the selected Windows ROS Python, never its CLI."""

        profile = self.profiles.PROFILES["humble-fastrtps"]
        root = ROOT / "ros2-windows" / "ros2_humble"
        args = SimpleNamespace(ros2_root=None)
        with mock.patch.object(self.profiles.ros2env, "default_ros2_root", return_value=root) as default_root:
            with mock.patch.object(self.profiles.ros2env, "validate_ros2_root", return_value=(Path("python.exe"), Path("ros2.py"))):
                resolved, python, ros2_script = self.profiles.resolve_windows_ros2_root(profile, args, workspace_root=ROOT)

        self.assertEqual(root, resolved)
        self.assertEqual(Path("python.exe"), python)
        self.assertEqual(Path("ros2.py"), ros2_script)
        default_root.assert_called_once_with("humble", ROOT)

        expected_outputs = []
        for name in profile.message_set:
            spec = self.profiles.inbound.MESSAGE_SPECS[name]
            expected_outputs.append(
                SimpleNamespace(
                    returncode=0,
                    stdout=json.dumps(
                        {
                            "topic": f"/foxrun/phase179/{name}",
                            "subscriptionCount": 1,
                            "endpoints": [
                                {
                                    "messageType": spec.message_type,
                                    "qosReliability": spec.qos_reliability,
                                    "qosHistory": spec.qos_history,
                                    "qosDepth": spec.qos_depth,
                                    "qosDurability": spec.qos_durability,
                                }
                            ],
                        }
                    ),
                )
            )
        with mock.patch.object(
            self.profiles.ros2env,
            "run_ros2",
            side_effect=AssertionError("Windows endpoint evidence must not invoke ros2-script.py."),
        ) as run_ros2:
            with mock.patch.object(self.profiles.subprocess, "run", side_effect=expected_outputs) as probe:
                evidence = self.profiles.validate_windows_subscription_endpoints(
                    Path("python.exe"),
                    Path("ros2.py"),
                    {},
                    profile,
                    timeout_seconds=0.1,
                )

        self.assertEqual(["string", "twist", "joy"], [item["name"] for item in evidence])
        run_ros2.assert_not_called()
        self.assertEqual(3, probe.call_count)
        for call in probe.call_args_list:
            self.assertEqual("python.exe", call.args[0][0])
            self.assertEqual(str(WINDOWS_ENDPOINT_PROBE_PATH), call.args[0][1])
            self.assertNotIn("ros2.py", call.args[0])

    def test_linux_role_accepts_only_the_documented_pending_half_exit_without_unity_log(self) -> None:
        """The profile wrapper delegates a pinned Linux row and preserves exit 2 as correlation-pending evidence."""

        profile = self.profiles.PROFILES["humble-fastrtps"]
        with tempfile.TemporaryDirectory() as temporary:
            summary_path = Path(temporary) / "linux-editor.json"
            summary = self._linux_summary(profile, surface="editor", token="row-token")

            def fake_linux_main(argv):
                """Write the synthetic peer summary instead of loading a ROS2 runtime."""

                summary_path.write_text(json.dumps(summary), encoding="utf-8")
                return 2

            with mock.patch.object(self.profiles.inbound, "main", side_effect=fake_linux_main) as delegate:
                exit_code = self.profiles.run_profile(
                    "humble-fastrtps",
                    [
                        "--role",
                        "linux-peer",
                        "--surface",
                        "editor",
                        "--token",
                        "row-token",
                        "--summary-json",
                        str(summary_path),
                    ],
                )

        self.assertEqual(2, exit_code)
        child_argv = delegate.call_args.args[0]
        self.assertNotIn("--unity-log", child_argv)
        self.assertEqual("humble", child_argv[child_argv.index("--distro") + 1])
        self.assertEqual("rmw_fastrtps_cpp", child_argv[child_argv.index("--rmw") + 1])

    def test_player_role_accepts_only_the_documented_pending_half_exit_without_windows_cli_path_injection(self) -> None:
        """The profile wrapper lets the R2FU Player own its DLL selection instead of adding ros2-windows paths."""

        profile = self.profiles.PROFILES["jazzy-fastrtps"]
        with tempfile.TemporaryDirectory() as temporary:
            summary_path = Path(temporary) / "windows-player.json"
            summary = self._player_summary(profile, token="row-token")

            def fake_player_main(argv):
                """Write the synthetic Player summary instead of launching a Unity executable."""

                summary_path.write_text(json.dumps(summary), encoding="utf-8")
                return 2

            with mock.patch.object(self.profiles.player_host, "main", side_effect=fake_player_main) as delegate:
                exit_code = self.profiles.run_profile(
                    "jazzy-fastrtps",
                    [
                        "--role",
                        "windows-player",
                        "--token",
                        "row-token",
                        "--player",
                        r"C:\build\Phase179.exe",
                        "--player-log",
                        str(Path(temporary) / "Player.log"),
                        "--summary-json",
                        str(summary_path),
                    ],
                )

        self.assertEqual(2, exit_code)
        child_argv = delegate.call_args.args[0]
        self.assertNotIn("--ros2-root", child_argv)
        self.assertEqual("jazzy", child_argv[child_argv.index("--distro") + 1])
        self.assertEqual("rmw_fastrtps_cpp", child_argv[child_argv.index("--rmw") + 1])

    def test_editor_role_uses_fresh_ready_and_apply_offsets_before_writing_pending_half_evidence(self) -> None:
        """Editor proof rejects stale log markers by observing READY and all copied values only after fresh offsets."""

        profile = self.profiles.PROFILES["humble-fastrtps"]
        token = "row-token"
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            unity_log = root / "Editor.log"
            unity_log.write_text("stale content\n", encoding="utf-8")
            summary_path = root / "windows-editor.json"
            marker_by_name = {
                name: self.profiles.inbound.UnityMarker(
                    session=4,
                    topic=f"/foxrun/phase179/{name}",
                    token=token,
                    received=1,
                    applied=1,
                    replaced=0,
                    value=self.profiles.inbound.MESSAGE_SPECS[name].expected_value(token),
                )
                for name in profile.message_set
            }
            with mock.patch.object(
                self.profiles,
                "resolve_windows_ros2_root",
                return_value=(Path("C:/ros2"), Path("python.exe"), Path("ros2.py")),
            ):
                with mock.patch.object(self.profiles.ros2env, "build_ros_env", return_value={}) as build_env:
                    with mock.patch.object(self.profiles.inbound, "unity_log_offset", side_effect=[12, 34]):
                        with mock.patch.object(self.profiles.inbound, "wait_for_unity_ready_marker") as wait_ready:
                            with mock.patch.object(
                                self.profiles,
                                "validate_windows_subscription_endpoints",
                                return_value=[{"name": name} for name in profile.message_set],
                            ):
                                with mock.patch.object(
                                    self.profiles.inbound,
                                    "wait_for_unity_marker",
                                    side_effect=[marker_by_name[name] for name in profile.message_set],
                                ) as wait_marker:
                                    exit_code = self.profiles.run_profile(
                                        "humble-fastrtps",
                                        [
                                            "--role",
                                            "windows-editor",
                                            "--surface",
                                            "editor",
                                            "--token",
                                            token,
                                            "--unity-log",
                                            str(unity_log),
                                            "--summary-json",
                                            str(summary_path),
                                        ],
                                    )
            summary = json.loads(summary_path.read_text(encoding="utf-8"))

        self.assertEqual(2, exit_code)
        self.assertEqual("WINDOWS_EDITOR_PROOF_COMPLETE_LINUX_PEER_CORRELATION_PENDING", summary["verdict"])
        self.assertEqual(["string", "twist", "joy"], [entry["name"] for entry in summary["messageResults"]])
        self.assertEqual(12, wait_ready.call_args.kwargs["start_offset"])
        self.assertEqual([34, 34, 34], [call.kwargs["start_offset"] for call in wait_marker.call_args_list])
        build_env.assert_called_once()

    def test_correlation_role_writes_the_only_final_pass_artifact(self) -> None:
        """A named wrapper turns two compatible half-summaries into a final PASS only during explicit correlation."""

        profile = self.profiles.PROFILES["jazzy-fastrtps"]
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            linux_path = root / "linux.json"
            player_path = root / "player.json"
            combined_path = root / "combined.json"
            linux_path.write_text(json.dumps(self._linux_summary(profile, surface="player", token="row-token")), encoding="utf-8")
            player_path.write_text(json.dumps(self._player_summary(profile, token="row-token")), encoding="utf-8")

            exit_code = self.profiles.run_profile(
                "jazzy-fastrtps",
                [
                    "--role",
                    "correlate",
                    "--surface",
                    "player",
                    "--linux-summary-json",
                    str(linux_path),
                    "--windows-summary-json",
                    str(player_path),
                    "--summary-json",
                    str(combined_path),
                ],
            )
            combined = json.loads(combined_path.read_text(encoding="utf-8"))

        self.assertEqual(0, exit_code)
        self.assertEqual("PHASE179_JAZZY_FASTRTPS_PLAYER_PASS", combined["verdict"])


__all__ = ["Phase179CorrelationEvidenceTests"]
