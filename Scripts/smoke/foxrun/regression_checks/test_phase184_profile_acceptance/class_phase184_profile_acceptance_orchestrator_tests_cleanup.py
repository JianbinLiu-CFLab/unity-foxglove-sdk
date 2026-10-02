from __future__ import annotations
from .class_phase184_profile_acceptance_orchestrator_tests_runtime import *
_PHASE192_FACADE_FILE = __import__("pathlib").Path(__file__).resolve().parents[1] / "test_phase184_profile_acceptance.py"
__file__ = str(_PHASE192_FACADE_FILE)
del _PHASE192_FACADE_FILE
class _Phase184ProfileAcceptanceOrchestratorTests_cleanup:
    """Decomposed Phase192 implementation component."""
    def test_failed_job_registration_terminates_the_untracked_child(self):
        """Job assignment failure cannot leave the just-spawned process orphaned."""

        module = load_module()
        process = FakeProcess(18402)
        job = mock.Mock()
        job.assign.side_effect = module.AcceptanceFailure(
            "FAIL_PREFLIGHT",
            "job assignment failed",
        )
        owner = module.OwnedProcessSet(job=job)

        with self.assertRaisesRegex(module.AcceptanceFailure, "FAIL_PREFLIGHT"):
            owner.register("bridge", process)

        self.assertEqual(1, process.terminated)
        self.assertTrue(owner.all_stopped())
    def test_preflight_assignment_failure_terminates_the_spawned_process(self):
        """Preparatory children are reclaimed before owner registration exists."""

        module = load_module()
        TEST_ROOT.mkdir(parents=True, exist_ok=True)
        process = FakeProcess(18403)
        job = mock.Mock()
        job.assign.side_effect = module.AcceptanceFailure(
            "FAIL_PREFLIGHT",
            "job assignment failed",
        )
        with tempfile.TemporaryDirectory(prefix="assign-fail-", dir=TEST_ROOT) as raw:
            log = pathlib.Path(raw) / "preflight.log"
            with mock.patch.object(module.subprocess, "Popen", return_value=process):
                with self.assertRaisesRegex(module.AcceptanceFailure, "FAIL_PREFLIGHT"):
                    module._run_logged_preflight(
                        ["owned-tool"],
                        cwd=ROOT,
                        environment={},
                        log_path=log,
                        job=job,
                        failure_code="FAIL_PREFLIGHT",
                        operation="preflight",
                    )

        self.assertEqual(1, process.terminated)
    def test_owner_requested_daemon_exit_preserves_raw_windows_semantics(self):
        """Only owned Bridge/router control-break exits are accepted as clean stops."""

        module = load_module()
        for code in (-1073741510, 3221225786):
            with self.subTest(code=code):
                self.assertTrue(
                    module.process_exit_is_acceptable(
                        "bridge",
                        code,
                        owner_requested=True,
                    )
                )
                self.assertTrue(
                    module.process_exit_is_acceptable(
                        "zenoh-router",
                        code,
                        owner_requested=True,
                    )
                )
                self.assertFalse(
                    module.process_exit_is_acceptable(
                        "ros2-peer",
                        code,
                        owner_requested=True,
                    )
                )
                self.assertFalse(
                    module.process_exit_is_acceptable(
                        "bridge",
                        code,
                        owner_requested=False,
                    )
                )
    def test_explicit_loopback_port_must_be_bindable(self):
        """Verify explicit loopback port must be bindable."""

        module = load_module()
        with module.socket.socket(module.socket.AF_INET, module.socket.SOCK_STREAM) as held:
            exclusive = getattr(module.socket, "SO_EXCLUSIVEADDRUSE", None)
            if exclusive is not None:
                held.setsockopt(module.socket.SOL_SOCKET, exclusive, 1)
            held.bind(("127.0.0.1", 0))
            port = int(held.getsockname()[1])
            with self.assertRaisesRegex(module.AcceptanceFailure, "FAIL_PREFLIGHT"):
                module.require_available_loopback_port(port, "Foxglove")

        module.require_available_loopback_port(port, "Foxglove")
    def test_progress_snapshot_observes_secondary_unity_log(self):
        """Verify progress snapshot observes secondary unity log."""

        module = load_module()
        TEST_ROOT.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix="progress-", dir=TEST_ROOT) as raw:
            root = pathlib.Path(raw)
            process_log = root / "process.log"
            unity_log = root / "unity.log"
            process_log.write_text("", encoding="utf-8")
            unity_log.write_text("first\n", encoding="utf-8")
            before = module._progress_snapshot((process_log, unity_log))
            unity_log.write_text("first\nsecond\n", encoding="utf-8")
            after = module._progress_snapshot((process_log, unity_log))
            self.assertNotEqual(before, after)
    def test_unity_wait_uses_progress_watchdog_instead_of_total_duration(self):
        """A progressing cold import is bounded by silence, not total duration."""

        module = load_module()
        unity = mock.Mock()
        unity.poll.side_effect = (None, 0)
        unity.returncode = 0
        terminal = module.TerminalMarker("PASS", "pass", {})
        watchdog = mock.Mock()
        config = {
            "case": "multi-target",
            "token": "p184g_A1b2C3d4E5f6",
            "unityLog": str(TEST_ROOT / "unity-progress.log"),
        }
        snapshots = (
            (("unity-progress.log", 10, 1),),
            (("unity-progress.log", 20, 2),),
        )
        with mock.patch.object(
            module.protocol,
            "ProgressWatchdog",
            return_value=watchdog,
        ) as create_watchdog:
            with mock.patch.object(module, "_progress_snapshot", side_effect=snapshots):
                with mock.patch.object(module, "read_log_lines", return_value=[]):
                    with mock.patch.object(
                        module,
                        "wait_for_terminal_marker",
                        return_value=terminal,
                    ):
                        with mock.patch.object(module.time, "sleep"):
                            observed = module._wait_for_unity_exit(
                                config,
                                unity,
                                owner=mock.Mock(),
                                worker_roles=(),
                            )

        self.assertIs(terminal, observed)
        create_watchdog.assert_called_once_with("unity-startup")
        self.assertGreaterEqual(watchdog.progress.call_count, 2)
        watchdog.check.assert_called()
    def test_manual_editor_log_mirror_survives_truncation_and_correlates_token(self):
        """Verify manual editor log mirror survives truncation and correlates token."""

        module = load_module()
        TEST_ROOT.mkdir(parents=True, exist_ok=True)
        token = "p184g_A1b2C3d4E5f6"
        with tempfile.TemporaryDirectory(prefix="mirror-", dir=TEST_ROOT) as raw:
            root = pathlib.Path(raw)
            editor_log = root / "Editor.log"
            owned_log = root / "unity-editor.log"
            editor_log.write_text("stale history\n", encoding="utf-8")
            mirror = module.EditorLogMirror(editor_log, owned_log, token)
            mirror.capture()

            with editor_log.open("a", encoding="utf-8") as stream:
                stream.write(
                    f"PHASE184G_CONTEXT_READY case=multi-target token={token}\n"
                )
            mirror.poll()
            self.assertIn(token, owned_log.read_text(encoding="utf-8"))

            editor_log.write_text(
                f"PHASE184G_MANUAL_PLAY_EXITED case=multi-target token={token}\n",
                encoding="utf-8",
            )
            mirror.poll()
            copied = owned_log.read_text(encoding="utf-8")
            self.assertIn("PHASE184G_CONTEXT_READY", copied)
            self.assertIn("PHASE184G_MANUAL_PLAY_EXITED", copied)
    def test_manual_editor_log_mirror_ignores_oversized_stale_history(self):
        """Historical log size must not poison fresh token-correlated appends."""

        module = load_module()
        TEST_ROOT.mkdir(parents=True, exist_ok=True)
        token = "p184g_A1b2C3d4E5f6"
        with tempfile.TemporaryDirectory(prefix="mirror-large-", dir=TEST_ROOT) as raw:
            root = pathlib.Path(raw)
            editor_log = root / "Editor.log"
            owned_log = root / "unity-editor.log"
            editor_log.write_text("stale history exceeds cap\n", encoding="utf-8")
            mirror = module.EditorLogMirror(editor_log, owned_log, token)
            mirror._MAX_SOURCE_BYTES = 8
            mirror.capture()

            with editor_log.open("a", encoding="utf-8") as stream:
                stream.write(
                    f"PHASE184G_CONTEXT_READY case=multi-target token={token}\n"
                )

            mirror.poll()

            copied = owned_log.read_text(encoding="utf-8")
            self.assertIn("PHASE184G_CONTEXT_READY", copied)
            self.assertNotIn("stale history", copied)
    def test_manual_editor_log_mirror_retries_transient_windows_open_contention(self):
        """Unity log rotation may briefly deny the stat-to-open transition."""

        module = load_module()
        TEST_ROOT.mkdir(parents=True, exist_ok=True)
        token = "p184g_A1b2C3d4E5f6"
        with tempfile.TemporaryDirectory(prefix="mirror-contention-", dir=TEST_ROOT) as raw:
            root = pathlib.Path(raw)
            editor_log = root / "Editor.log"
            owned_log = root / "unity-editor.log"
            editor_log.write_text("existing\n", encoding="utf-8")
            mirror = module.EditorLogMirror(editor_log, owned_log, token)
            mirror.capture()
            with editor_log.open("a", encoding="utf-8") as stream:
                stream.write(
                    f"PHASE184G_CONTEXT_READY case=multi-target token={token}\n"
                )

            original_open = pathlib.Path.open
            source_attempts = 0

            def open_with_one_contention(path, *args, **kwargs):
                """Fail only the first binary source read."""

                nonlocal source_attempts
                if path == editor_log and args and args[0] == "rb":
                    source_attempts += 1
                    if source_attempts == 1:
                        raise PermissionError("simulated Windows sharing violation")
                return original_open(path, *args, **kwargs)

            with mock.patch.object(
                pathlib.Path,
                "open",
                autospec=True,
                side_effect=open_with_one_contention,
            ):
                mirror.poll()
                mirror.poll()

            self.assertEqual(2, source_attempts)
            self.assertIn(token, owned_log.read_text(encoding="utf-8"))
    def test_manual_editor_log_mirror_fails_after_bounded_open_contention(self):
        """Persistent inability to read Editor.log must remain fail closed."""

        module = load_module()
        TEST_ROOT.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(prefix="mirror-contention-bound-", dir=TEST_ROOT) as raw:
            root = pathlib.Path(raw)
            editor_log = root / "Editor.log"
            owned_log = root / "unity-editor.log"
            editor_log.write_text("existing\n", encoding="utf-8")
            mirror = module.EditorLogMirror(
                editor_log,
                owned_log,
                "p184g_A1b2C3d4E5f6",
            )
            mirror._ACCESS_FAILURE_GRACE_SECONDS = 0.0
            mirror.capture()

            original_open = pathlib.Path.open

            def deny_source_open(path, *args, **kwargs):
                """Deny only binary reads of the interactive Editor log."""

                if path == editor_log and args and args[0] == "rb":
                    raise PermissionError("persistent Windows sharing violation")
                return original_open(path, *args, **kwargs)

            with mock.patch.object(
                pathlib.Path,
                "open",
                autospec=True,
                side_effect=deny_source_open,
            ):
                mirror.poll()
                with self.assertRaisesRegex(
                    module.AcceptanceFailure,
                    r"FAIL_TERMINAL.*bounded retry window",
                ):
                    mirror.poll()
    def test_manual_session_does_not_latch_pass_over_a_later_failure(self):
        """The latest correlated terminal marker remains authoritative until exit."""

        module = load_module()
        token = "p184g_A1b2C3d4E5f6"
        config = {
            "case": "multi-target",
            "token": token,
            "unityLog": str(TEST_ROOT / "manual-latch.log"),
        }
        context = f"PHASE184G_CONTEXT_READY case=multi-target token={token}"
        passed = f"PHASE184G_CASE_PASS case=multi-target token={token}"
        failed = f"PHASE184G_CASE_FAIL case=multi-target token={token}"

        with mock.patch.object(
            module,
            "read_log_lines",
            side_effect=([context, passed], [context, passed, failed]),
        ):
            with mock.patch.object(
                module,
                "_manual_exit_seen",
                side_effect=(False, True),
            ):
                with mock.patch.object(module.time, "sleep"):
                    with self.assertRaisesRegex(
                        module.AcceptanceFailure,
                        "FAIL_TERMINAL",
                    ):
                        module._wait_for_manual_session(
                            config,
                            mirror=mock.Mock(),
                            owner=mock.Mock(),
                            worker_roles=(),
                        )
    def test_manual_session_fails_immediately_from_finished_worker_result(self):
        """A persisted worker failure must not consume the manual review timeout."""

        module = load_module()
        TEST_ROOT.mkdir(parents=True, exist_ok=True)
        token = "p184g_A1b2C3d4E5f6"
        with tempfile.TemporaryDirectory(prefix="manual-worker-", dir=TEST_ROOT) as raw:
            root = pathlib.Path(raw)
            result = root / "foxglove-client.json"
            config = {
                "runId": "phase184g-20260728-worker-fail01",
                "case": "multi-target",
                "token": token,
                "unityLog": str(root / "unity-editor.log"),
                "resultFiles": {"foxglove-client": str(result)},
            }
            module.write_actor_result(
                config,
                "foxglove-client",
                verdict="FAIL_CLIENT",
                evidence={"diagnostic": "incomplete delivery"},
            )
            process = FakeProcess()
            process.returncode = 1
            owner = mock.Mock()
            owner.process.return_value = process
            context = f"PHASE184G_CONTEXT_READY case=multi-target token={token}"

            with mock.patch.object(module, "read_log_lines", return_value=[context]):
                with mock.patch.object(module, "_manual_exit_seen", return_value=False):
                    with mock.patch.object(
                        module,
                        "MANUAL_REVIEW_TIMEOUT_SECONDS",
                        0.0,
                    ):
                        with mock.patch.object(module.time, "monotonic", return_value=0.0):
                            with self.assertRaisesRegex(
                                module.AcceptanceFailure,
                                r"FAIL_CLIENT.*incomplete delivery",
                            ):
                                module._wait_for_manual_session(
                                    config,
                                    mirror=mock.Mock(),
                                    owner=owner,
                                    worker_roles=("foxglove-client",),
                                )


__all__ = [name for name in globals() if not name.startswith("__")]
