from __future__ import annotations
from .foundation import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "test_phase186_bridge_live.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
class _Phase186BridgeLiveTests_fixtures:
    """Decomposed Phase192 implementation component."""
    def test_manual_wait_fails_immediately_when_live_actor_exits(self) -> None:
        """Verify that manual wait fails immediately when live actor exits."""
        with tempfile.TemporaryDirectory() as temp:
            output = pathlib.Path(temp)
            actors = output / "actors"
            actors.mkdir()
            (actors / "ros-peer-failure.json").write_text(
                json.dumps(
                    {
                        "failureCode": "FAIL_PEER",
                        "failureMessage": (
                            "FAIL_PEER: Unity Bridge outbound sample did not reach "
                            "the exact ROS peer"
                        ),
                    }
                ),
                encoding="utf-8",
            )
            config = {
                "outputRoot": str(output),
                "runtimeRowId": "jazzy-fastrtps",
                "bridgeHost": "127.0.0.1",
                "bridgePort": 18767,
                "foxgloveHost": "127.0.0.1",
                "foxglovePort": 18768,
                "externalGate": str(output / "external-gate.json"),
                "exerciseGate": str(output / "exercise-gate.json"),
                "caseId": "manual-jazzy-fastrtps-duplex",
                "manual": True,
                "requiredActors": ["ros-peer"],
                "runId": "phase186h-current-0123456789ab",
                "tokenHash": "a" * 64,
                "head": "b" * 40,
                "unityLog": str(output / "unity.log"),
            }
            runtime = types.SimpleNamespace(
                zenoh_router=None,
                zenoh_endpoint=None,
                row_id="jazzy-fastrtps",
                distro="jazzy",
                rmw="rmw_fastrtps_cpp",
                python_executable=pathlib.Path("python.exe"),
                environment={},
            )
            owner = mock.Mock()
            owner.poll.return_value = 1
            owner.residual_pids.return_value = []
            cleanup = {
                "complete": True,
                "cleanupErrors": [],
                "residualProcesses": [],
                "residualPorts": [],
                "residualOverlays": [],
                "residualTemporaryProjects": [],
            }

            with mock.patch.object(live, "prepare_runtime", return_value=runtime), \
                    mock.patch.object(live, "OwnedLiveProcesses", return_value=owner), \
                    mock.patch.object(live, "_launch_sidecar", return_value=(mock.Mock(), {})), \
                    mock.patch.object(live, "_wait_actor_document", return_value={}), \
                    mock.patch.object(live, "_write_manual_pointer", return_value=output / "pointer.json"), \
                    mock.patch.object(live, "_mirror_manual_log"), \
                    mock.patch.object(live, "_manual_scene_prepare_failure", return_value=None), \
                    mock.patch.object(live, "_manual_context_failure", return_value=None), \
                    mock.patch.object(live, "_manual_scene_preparing_in_log", return_value=False), \
                    mock.patch.object(live, "_manual_scene_ready_in_log", return_value=False), \
                    mock.patch.object(live, "_report_manual_progress"), \
                    mock.patch.object(live, "_remove_manual_pointer"), \
                    mock.patch.object(live, "_cleanup_document", return_value=cleanup), \
                    mock.patch.object(live.time, "monotonic", side_effect=(0.0, 0.0, 31.0)), \
                    mock.patch.object(live.time, "sleep"), \
                    self.assertRaises(live.LiveFailure) as failure:
                live.run_live(
                    pathlib.Path("D:/repo"),
                    config,
                    unity_editor=pathlib.Path("Unity.exe"),
                    manual_timeout_seconds=30.0,
                    reporter=mock.Mock(),
                )

        self.assertEqual("FAIL_PROCESS_EXIT", failure.exception.code)
        self.assertIn("FAIL_PEER", str(failure.exception))
        self.assertIn("Unity Bridge outbound sample", str(failure.exception))
        owner.close.assert_called_once_with()
    def test_manual_completion_waits_for_editor_release_before_pointer_cleanup(self) -> None:
        """Verify that manual completion waits for editor release before pointer cleanup."""
        with tempfile.TemporaryDirectory() as temp:
            output = pathlib.Path(temp)
            config = {
                "outputRoot": str(output),
                "runtimeRowId": "jazzy-fastrtps",
                "bridgeHost": "127.0.0.1",
                "bridgePort": 18767,
                "foxgloveHost": "127.0.0.1",
                "foxglovePort": 18768,
                "externalGate": str(output / "external-gate.json"),
                "exerciseGate": str(output / "exercise-gate.json"),
                "caseId": "manual-jazzy-fastrtps-duplex",
                "manual": True,
                "requiredActors": [],
                "runId": "phase186h-current-0123456789ab",
                "tokenHash": "a" * 64,
                "head": "b" * 40,
                "unityLog": str(output / "unity.log"),
            }
            runtime = types.SimpleNamespace(
                zenoh_router=None,
                zenoh_endpoint=None,
                row_id="jazzy-fastrtps",
                distro="jazzy",
                rmw="rmw_fastrtps_cpp",
            )
            owner = mock.Mock()
            owner.residual_pids.return_value = []
            pointer = output / "current-run.json"
            cleanup = {
                "complete": True,
                "cleanupErrors": [],
                "residualProcesses": [],
                "residualPorts": [],
                "residualOverlays": [],
                "residualTemporaryProjects": [],
            }
            events: list[str] = []

            with mock.patch.object(live, "prepare_runtime", return_value=runtime), \
                    mock.patch.object(live, "OwnedLiveProcesses", return_value=owner), \
                    mock.patch.object(live, "_launch_sidecar", return_value=(mock.Mock(), {})), \
                    mock.patch.object(live, "_write_manual_pointer", return_value=pointer), \
                    mock.patch.object(live, "_mirror_manual_log"), \
                    mock.patch.object(live, "_manual_scene_prepare_failure", return_value=None), \
                    mock.patch.object(live, "_manual_context_failure", return_value=None), \
                    mock.patch.object(live, "_manual_scene_preparing_in_log", return_value=False), \
                    mock.patch.object(live, "_manual_scene_ready_in_log", return_value=True), \
                    mock.patch.object(live, "_report_manual_progress"), \
                    mock.patch.object(live, "_manual_marker_in_log", return_value=True), \
                    mock.patch.object(
                        live,
                        "_wait_manual_editor_release",
                        side_effect=lambda *_args, **_kwargs: events.append("editor-release"),
                        create=True,
                    ), \
                    mock.patch.object(
                        live,
                        "_parse_unity_evidence",
                        side_effect=lambda *_args, **_kwargs: events.append("unity-evidence") or {},
                    ), \
                    mock.patch.object(
                        live,
                        "_remove_manual_pointer",
                        side_effect=lambda *_args, **_kwargs: events.append("pointer-cleanup"),
                    ), \
                    mock.patch.object(live, "_cleanup_document", return_value=cleanup), \
                    mock.patch.object(
                        live,
                        "_build_observations",
                        return_value={},
                    ):
                live.run_live(
                    pathlib.Path("D:/repo"),
                    config,
                    unity_editor=pathlib.Path("Unity.exe"),
                    manual_timeout_seconds=30.0,
                    reporter=mock.Mock(),
                )

        self.assertEqual(
            ["editor-release", "unity-evidence", "pointer-cleanup"],
            events,
        )
    def test_observations_require_existing_evidence_files(self) -> None:
        """Do not promote resolved path strings into observed live evidence."""
        with tempfile.TemporaryDirectory() as temp:
            output = pathlib.Path(temp)
            config = {
                "outputRoot": str(output),
                "caseId": "product-inspector",
            }
            (output / "preflight.json").write_text("{}", encoding="utf-8")
            (output / "cleanup.json").write_text("{}", encoding="utf-8")

            with self.assertRaises(live.LiveFailure):
                live._build_observations(config, {})

            (output / "unity-evidence.json").write_text("{}", encoding="utf-8")
            observations = live._build_observations(config, {})
            self.assertTrue(all(item["observed"] for item in observations.values()))
    def test_hostile_peer_data_uses_rejection_evidence_label(self) -> None:
        """Do not label a hostile-frame rejection document as delivered payload."""
        self.assertEqual(
            "live-hostile-frame-rejection",
            live._observation_source("bounds-hostile-peer", "data"),
        )
    def test_manual_editor_release_marker_requires_exact_run_identity(self) -> None:
        """Verify that manual editor release marker requires exact run identity."""
        self.assertTrue(
            hasattr(live, "_manual_editor_released_in_log"),
            "manual completion needs an identity-bound EnteredEditMode marker",
        )
        with tempfile.TemporaryDirectory() as temp:
            log = pathlib.Path(temp) / "unity.log"
            config = {
                "unityLog": str(log),
                "runId": "phase186h-current-0123456789ab",
                "caseId": "manual-jazzy-fastrtps-duplex",
                "tokenHash": "a" * 64,
                "head": "b" * 40,
            }
            exact = (
                "PHASE186_MANUAL_PLAY_EXITED "
                "run=phase186h-current-0123456789ab "
                "case=manual-jazzy-fastrtps-duplex "
                f"tokenHash={'a' * 64} head={'b' * 40}"
            )
            log.write_text(
                exact.replace("phase186h-current", "phase186h-foreign") + "\n",
                encoding="utf-8",
            )
            self.assertFalse(live._manual_editor_released_in_log(config))
            log.write_text(
                exact.replace("phase186h-current", "phase186h-foreign")
                + "\n"
                + exact
                + "\n",
                encoding="utf-8",
            )
            self.assertTrue(live._manual_editor_released_in_log(config))
    def test_manual_unity_evidence_requires_real_publish_and_apply_counts(self) -> None:
        """Manual completion must parse generated runtime counters."""
        with tempfile.TemporaryDirectory() as temp:
            output = pathlib.Path(temp)
            log = output / "unity.log"
            config = {
                "outputRoot": str(output),
                "unityLog": str(log),
                "runId": "phase186h-current-0123456789ab",
                "caseId": "manual-jazzy-fastrtps-duplex",
                "tokenHash": "a" * 64,
            }
            prefix = (
                "PHASE186_ACCEPTANCE_EVIDENCE "
                f"run={config['runId']} "
                f"case={config['caseId']} "
                f"tokenHash={config['tokenHash']} "
            )
            log.write_text(
                prefix
                + "generation=1 received=2 applied=2 replaced=0 "
                + "localMutations=1 accepted=1 sent=1 failed=0 "
                + "connectTransitions=1 disconnectTransitions=0 "
                + "generationChanges=0 dropped=0 providerReplaced=0\n",
                encoding="utf-8",
            )

            evidence = live._parse_unity_evidence(
                config,
                require_pass_marker=False,
            )
            self.assertEqual(1, evidence["fields"]["sent"])
            self.assertEqual(2, evidence["fields"]["applied"])

            log.write_text(
                prefix
                + "generation=1 received=0 applied=0 replaced=0 "
                + "localMutations=0 accepted=0 sent=0 failed=0 "
                + "connectTransitions=1 disconnectTransitions=0 "
                + "generationChanges=0 dropped=0 providerReplaced=0\n",
                encoding="utf-8",
            )
            with self.assertRaises(live.LiveFailure):
                live._parse_unity_evidence(
                    config,
                    require_pass_marker=False,
                )
    def test_manual_wait_accepts_actor_exit_after_all_owned_results_exist(self) -> None:
        """Verify that manual wait accepts actor exit after all owned results exist."""
        config = {
            "outputRoot": "unused",
            "requiredActors": ["graph-observer", "ros-peer"],
        }
        owner = mock.Mock()
        owner.poll.return_value = 0

        with mock.patch.object(live, "_read_actor_document", return_value={}) as read:
            try:
                live._raise_if_worker_process_exited(config, owner)
            except live.LiveFailure as exc:
                self.fail(f"completed actor was misreported as failed: {exc}")

        self.assertEqual(
            [
                mock.call(config, "graph-observer", "result"),
                mock.call(config, "ros-peer", "result"),
            ],
            read.call_args_list,
        )
    def test_manual_scene_ready_requires_exact_identity_and_stable_schema(self) -> None:
        """Verify that manual scene ready requires exact identity and stable schema."""
        config = {
            "unityLog": "unity.log",
            "runId": "phase186h-current-0123456789ab",
            "caseId": "manual-jazzy-fastrtps-duplex",
            "tokenHash": "a" * 64,
            "head": "b" * 40,
        }
        prefix = "PHASE186_MANUAL_SCENE_READY "
        stale = (
            prefix
            + "run=phase186h-stale-0123456789ab "
            + f"case={config['caseId']} tokenHash={config['tokenHash']} "
            + f"head={config['head']} schemaInfoChanged=false"
        )
        changing = (
            prefix
            + f"run={config['runId']} case={config['caseId']} "
            + f"tokenHash={config['tokenHash']} head={config['head']} "
            + "schemaInfoChanged=true"
        )
        exact = (
            prefix
            + f"run={config['runId']} case={config['caseId']} "
            + f"tokenHash={config['tokenHash']} head={config['head']} "
            + "manifest=abc schemaInfoChanged=false"
        )
        preparing = (
            "PHASE186_MANUAL_SCENE_PREPARING "
            + f"run={config['runId']} case={config['caseId']} "
            + f"tokenHash={config['tokenHash']} head={config['head']} "
            + "schemaRefresh=1"
        )

        with mock.patch.object(live_peer, "_read_log", return_value=stale + "\n" + changing):
            self.assertFalse(live._manual_scene_ready_in_log(config))
        with mock.patch.object(live_peer, "_read_log", return_value=stale + "\n" + exact):
            self.assertTrue(live._manual_scene_ready_in_log(config))
        with mock.patch.object(live_peer, "_read_log", return_value=stale):
            self.assertFalse(live._manual_scene_preparing_in_log(config))
        with mock.patch.object(
            live_peer,
            "_read_log",
            return_value=stale + "\n" + preparing,
        ):
            self.assertTrue(live._manual_scene_preparing_in_log(config))

        failed = (
            "PHASE186_MANUAL_SCENE_PREPARE_FAIL "
            + f"run={config['runId']} case={config['caseId']} "
            + f"tokenHash={config['tokenHash']} head={config['head']} "
            + "reason=InvalidOperationException"
        )
        with mock.patch.object(live_peer, "_read_log", return_value=stale + "\n" + failed):
            self.assertEqual(
                "InvalidOperationException",
                live._manual_scene_prepare_failure(config),
            )
    def test_manual_context_failure_requires_exact_generated_identity(self) -> None:
        """Verify that manual context failure requires exact generated identity."""
        config = {
            "unityLog": "unity.log",
            "runId": "phase186h-current-0123456789ab",
            "caseId": "manual-jazzy-fastrtps-duplex",
            "tokenHash": "a" * 64,
            "head": "b" * 40,
        }
        prefix = "PHASE186_ACCEPTANCE_CONTEXT_FAIL "
        exact = (
            prefix
            + f"run={config['runId']} case={config['caseId']} "
            + f"tokenHash={config['tokenHash']} head={config['head']} "
            + "reason=serialized-run-identity-differs"
        )
        stale = exact.replace(
            str(config["runId"]),
            "phase186h-stale-0123456789ab",
        )

        with mock.patch.object(live_peer, "_read_log", return_value=stale):
            self.assertIsNone(live._manual_context_failure(config))
        with mock.patch.object(
            live_peer,
            "_read_log",
            return_value=stale + "\n" + exact,
        ):
            self.assertEqual(
                "serialized-run-identity-differs",
                live._manual_context_failure(config),
            )


__all__ = [name for name in globals() if not name.startswith("__")]
